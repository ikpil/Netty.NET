param(
    # Every ported codec Maven module; each has docs/<module>-porting-manifest.json.
    [string[]]$Module = @('codec-base', 'codec-dns', 'codec-haproxy', 'codec-memcache', 'codec-mqtt', 'codec-redis',
        'codec-smtp', 'codec-socks', 'codec-stomp'),
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'),
    [switch]$AllowPending)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$head = git -C $UpstreamRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Unexpected upstream baseline.' }

function Get-NormalizedComments([string]$source) {
    # Skip literals before recognizing comment tokens; URLs inside strings are not comments.
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|(?<comment>/\*.*?\*/|//[^\r\n]*)'
    foreach ($match in [regex]::Matches($source, $tokens)) {
        if (-not $match.Groups['comment'].Success) { continue }
        $comment = $match.Groups['comment'].Value -replace '^/\*+|\*/$|^//', ''
        $comment = $comment -replace '(?m)^\s*\* ?', ''
        ($comment -replace '\s+', ' ').Trim()
    }
}

function Get-DeclaredJavaTests([string]$source) {
    # Mask literals/comments first: @Timeout, parameter providers and annotations
    # between @Test and a method must not silently remove tests from the inventory.
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*'
    $masked = [regex]::Replace($source, $tokens, {
        param($match)
        [regex]::Replace($match.Value, '[^\r\n]', ' ')
    })
    $declarations = '(?m)^[ \t]*(?<annotations>(?:@[\w.]+(?:[ \t]*\([^();]*\))?[ \t\r\n]*)+)' +
        '(?:(?:public|protected|private|static|final|synchronized|abstract)[ \t]+)*' +
        'void[ \t]+(?<name>\w+)[ \t]*\([^;{}]*\)(?:[ \t\r\n]+throws[^{;]+)?[ \t\r\n]*\{'
    $names = @([regex]::Matches($masked, $declarations) | Where-Object {
        $_.Groups['annotations'].Value -match '@(?:Test|ParameterizedTest|RepeatedIfExceptionsTest)\b'
    } | ForEach-Object { $_.Groups['name'].Value })
    $annotationCount = [regex]::Matches($masked, '(?m)^[ \t]*@(?:Test|ParameterizedTest|RepeatedIfExceptionsTest)\b').Count
    if ($names.Count -ne $annotationCount -or @($names | Select-Object -Unique).Count -ne $names.Count) {
        throw 'Cannot account for every declared JUnit method exactly once.'
    }
    $names
}

function Get-NormalizedYamlComments([string]$source) {
    # module-info.yml keeps its license in '#' lines; each contiguous '#' block is one comment.
    foreach ($match in [regex]::Matches($source, '(?m)(?:^[ \t]*#[^\n]*(?:\n|$))+')) {
        (($match.Value -replace '(?m)^[ \t]*#', '') -replace '\s+', ' ').Trim()
    }
}

function Get-MaskedJava([string]$source) {
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*'
    [regex]::Replace($source, $tokens, {
        param($match)
        [regex]::Replace($match.Value, '[^\r\n]', ' ')
    })
}

function Get-DeprecatedTags([string]$source) {
    # Exact text of every Javadoc @deprecated block tag: from the tag up to the next block tag or the comment end.
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|(?<comment>/\*.*?\*/|//[^\r\n]*)'
    foreach ($match in [regex]::Matches($source, $tokens)) {
        $comment = $match.Groups['comment']
        if (-not $comment.Success -or -not $comment.Value.StartsWith('/**')) { continue }
        $tag = $null
        foreach ($line in (($comment.Value -replace '^/\*+|\*/$', '') -split "`n")) {
            $line = $line -replace '^\s*\* ?', ''
            if ($line -match '^\s*@deprecated\b') {
                if ($null -ne $tag) { ($tag -replace '\s+', ' ').Trim() }
                $tag = $line
            } elseif ($line -match '^\s*@\w') {
                if ($null -ne $tag) { ($tag -replace '\s+', ' ').Trim() }
                $tag = $null
            } elseif ($null -ne $tag) {
                $tag += ' ' + $line
            }
        }
        if ($null -ne $tag) { ($tag -replace '\s+', ' ').Trim() }
    }
}

function Test-DeprecatedType([string]$masked, [string]$typeName) {
    # The @Deprecated annotation must sit on the named top-level type declaration, outside comments and literals.
    $pattern = '@Deprecated\b\s*(?:@[\w.]+(?:\s*\([^)]*\))?\s*)*' +
        '(?:(?:public|protected|private|abstract|final|static|strictfp)\s+)*(?:class|interface|enum|record|@interface)\s+' +
        [regex]::Escape($typeName) + '\b'
    [regex]::IsMatch($masked, $pattern)
}

function Get-TextSha256([string]$text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-CommentSpans([string]$source, [string]$text) {
    # Start/end offsets of every original comment whose normalized text equals $text.
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|(?<comment>/\*.*?\*/|//[^\r\n]*)'
    foreach ($match in [regex]::Matches($source, $tokens)) {
        $comment = $match.Groups['comment']
        if (-not $comment.Success) { continue }
        $normalized = $comment.Value -replace '^/\*+|\*/$|^//', ''
        $normalized = (($normalized -replace '(?m)^\s*\* ?', '') -replace '\s+', ' ').Trim()
        if ($normalized -ceq $text) { , @($comment.Index, ($comment.Index + $comment.Length)) }
    }
}

function Get-MemberEnd([string]$masked, [int]$start) {
    # End of a Java declaration or statement: its first ';' outside braces, or the end of its balanced body.
    $depth = 0
    for ($i = $start; $i -lt $masked.Length; $i++) {
        $c = $masked[$i]
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth--; if ($depth -le 0) { return $i + 1 } }
        elseif ($c -eq ';' -and $depth -eq 0) { return $i + 1 }
    }
    return $masked.Length
}

function Test-CommentDisposition([string]$source, [string]$masked, $disposition) {
    # A per-comment disposition names the Java member the comment documents (Javadoc/line comment directly before it,
    # trailing it on the same line, or inside its body). The member must be @Deprecated, or its code must use the
    # named JVM-only API; anything else is ported code whose comment belongs in the C# source.
    if ($disposition.disposition -cnotin @('deprecated-not-ported', 'jvm-only-replaced') -or -not $disposition.member -or
        -not $disposition.reason -or -not $disposition.clr) {
        throw "Comment disposition needs disposition, member, reason and clr: $($disposition.sourceComment)"
    }
    $jvmOnly = $disposition.disposition -ceq 'jvm-only-replaced'
    if ($jvmOnly) {
        if (-not $disposition.javaApi -or $disposition.javaApi -cnotmatch '^[\w.$]+$' -or @($disposition.evidence).Count -eq 0) {
            throw "JVM-only comment disposition needs the JVM API token and evidence: $($disposition.sourceComment)"
        }
        foreach ($evidence in $disposition.evidence) {
            if ($evidence -cnotmatch '^https://\S+$' -and -not (Test-Path (Join-Path $repositoryRoot $evidence) -PathType Leaf)) {
                throw "Invalid comment disposition evidence: $evidence"
            }
        }
    } elseif ($disposition.javaApi) { throw "Deprecated comment dispositions rely on @Deprecated, not an API token." }
    $memberPattern = [regex]::Escape((($disposition.member -replace '\s+', ' ').Trim())).Replace('\ ', '\s+')
    $preamble = '^\s*(?:@[\w.]+(?:\s*\([^()]*\))?\s*)*(?:(?:public|protected|private|static|final|abstract|synchronized|default|native|transient|volatile)\s+)*$'
    $spans = @(Get-CommentSpans $source $disposition.sourceComment)
    if ($spans.Count -eq 0) { throw "Original comment not found: $($disposition.sourceComment)" }
    foreach ($member in [regex]::Matches($masked, $memberPattern)) {
        $memberEnd = Get-MemberEnd $masked $member.Index
        foreach ($span in $spans) {
            $commentStart = $span[0]; $commentEnd = $span[1]
            $documented = $member.Index -ge $commentEnd -and
                $masked.Substring($commentEnd, $member.Index - $commentEnd) -match $preamble
            $trailing = $member.Index + $member.Length -le $commentStart -and $memberEnd -le $commentStart -and
                $masked.Substring($member.Index, $commentStart - $member.Index) -notmatch '\n'
            $enclosed = $member.Index + $member.Length -le $commentStart -and $memberEnd -gt $commentEnd -and
                $masked.Substring($member.Index, $commentStart - $member.Index).Contains('{')
            if (-not ($documented -or $trailing -or $enclosed)) { continue }
            $memberText = $masked.Substring($member.Index, $memberEnd - $member.Index)
            if ($jvmOnly) {
                if ($memberText -cmatch ('(?<![\w$])' + [regex]::Escape($disposition.javaApi) + '(?![\w$])')) { return }
            } else {
                $declarationStart = [Math]::Max($masked.LastIndexOfAny([char[]]@(';', '{', '}'), [Math]::Max($member.Index - 1, 0)) + 1, 0)
                if ($masked.Substring($declarationStart, $member.Index - $declarationStart) -cmatch '@Deprecated\b') { return }
            }
        }
    }
    throw "Comment disposition does not match a $($disposition.disposition) Java member: $($disposition.sourceComment)"
}

    foreach ($moduleName in $Module) {
    $manifestPath = Join-Path $repositoryRoot "docs/$moduleName-porting-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "Missing manifest: docs/$moduleName-porting-manifest.json" }
    $manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.module -cne $moduleName) { throw "Manifest module differs: $manifestPath" }
    if ($head -ne $manifest.baseline) { throw 'Unexpected upstream baseline.' }

    $expected = @($manifest.entries | ForEach-Object { $_.upstream } | Sort-Object)
    # Every pinned file of the module: Java sources, module-info.yml and the native-image reflection metadata.
    $actual = @(Get-ChildItem (Join-Path $UpstreamRoot "$moduleName/src") -Recurse -File |
        ForEach-Object { $_.FullName.Substring((Resolve-Path $UpstreamRoot).Path.Length + 1).Replace('\', '/') } | Sort-Object)
    if (@($expected | Select-Object -Unique).Count -ne $expected.Count) { throw "Duplicate $($moduleName) inventory entry." }
    if (Compare-Object $expected $actual -CaseSensitive) { throw "$($moduleName) inventory changed." }

    $mappedCount = 0
    $inheritedMappedCount = 0
    $notApplicableCount = 0
    $partialCommentCount = 0
    $deprecatedCommentCount = 0
    $jvmOnlyCommentCount = 0
    $replacedCommentCount = 0
    $deprecatedNotPortedCount = 0
    $jvmOnlyReplacedCount = 0
    $notPortedCommentCount = 0
    $notPortedMethodCount = 0
    $notPortedStatuses = @('deprecated-not-ported', 'jvm-only-replaced')
    $testsuiteEntries = @()
    foreach ($entry in @($manifest.entries)) {
        $path = Join-Path $UpstreamRoot $entry.upstream
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($entry.hashMode -eq 'utf8-lf') {
            $source = [Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n", "`n")
            $bytes = [Text.Encoding]::UTF8.GetBytes($source)
        }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($hash -ne $entry.sha256) { throw "Original source changed: $($entry.upstream)" }

        $implementation = ''
        foreach ($target in $entry.implementation) {
            $targetPath = Join-Path $repositoryRoot $target
            if (-not (Test-Path $targetPath -PathType Leaf)) { throw "Missing target: $target" }
            if ($entry.hashMode -eq 'binary') {
                $targetBytes = [IO.File]::ReadAllBytes($targetPath)
                if ([Convert]::ToBase64String($bytes) -ne [Convert]::ToBase64String($targetBytes)) { throw 'Test resource changed.' }
            } else {
                $implementation += Get-Content $targetPath -Raw -Encoding UTF8
            }
        }
        if ($entry.status -eq 'pending') { continue }
        $notPorted = $entry.status -cin $notPortedStatuses
        if ($notPorted) {
            # Owner dispositions for Java units that are deliberately not ported as code (2026-11-16). Nothing is waived:
            # the reason, replacement, evidence, Java deprecation facts and every original comment/test are explicit.
            $decision = $entry.notPorted
            if (@($entry.implementation).Count -ne 0 -or $entry.hashMode -cne 'utf8-lf' -or -not $decision -or
                -not $decision.reason -or -not $decision.sourceContract -or -not $decision.replacement.clr -or
                @($decision.evidence.files).Count -eq 0 -or @($decision.evidence.urls).Count -eq 0) {
                throw "Incomplete not-ported disposition: $($entry.upstream)"
            }
            foreach ($evidencePath in $decision.evidence.files) {
                if (-not (Test-Path (Join-Path $repositoryRoot $evidencePath) -PathType Leaf)) {
                    throw "Missing not-ported evidence: $evidencePath"
                }
            }
            foreach ($url in $decision.evidence.urls) {
                if ($url -cnotmatch '^https://\S+$') { throw "Invalid not-ported evidence URL: $url" }
            }
            foreach ($gap in @($decision.replacement.noClrCounterpart)) {
                if ($null -ne $gap -and -not $gap) { throw 'Empty no-CLR-counterpart statement.' }
            }
            $isJava = $path.EndsWith('.java')
            $typeName = ''
            $masked = ''
            $tags = @()
            $annotated = $false
            if ($isJava) {
                $typeName = [IO.Path]::GetFileNameWithoutExtension($path)
                $masked = Get-MaskedJava $source
                $tags = @(Get-DeprecatedTags $source)
                $annotated = $typeName -cne 'package-info' -and (Test-DeprecatedType $masked $typeName)
            } elseif (-not $path.EndsWith('.yml') -and -not $path.EndsWith('.json')) { throw "Unsupported not-ported source: $($entry.upstream)" }
            $deprecation = $decision.deprecation
            if ($deprecation) {
                if ($deprecation.annotated -isnot [bool] -or $deprecation.annotated -ne $annotated -or
                    $deprecation.PSObject.Properties.Name -notcontains 'tags' -or
                    [string]::Join("`n", @($deprecation.tags)) -cne [string]::Join("`n", $tags)) {
                    throw "Recorded @Deprecated/@deprecated facts differ from the Java source: $($entry.upstream)"
                }
            } elseif ($annotated -or $tags.Count) { throw "Unrecorded Java deprecation: $($entry.upstream)" }
            if ($entry.status -ceq 'deprecated-not-ported') {
                $deprecatedNotPortedCount++
                if (-not $deprecation) { throw "Deprecated disposition without deprecation facts: $($entry.upstream)" }
                $nameSource = $tags
                if (-not $annotated) {
                    # A unit without its own @Deprecated type must serve a recorded @Deprecated public type.
                    $family = $deprecation.family
                    $owner = @($manifest.entries | Where-Object { $family -and $_.upstream -ceq $family.upstream })
                    if (-not $family -or -not $family.type -or -not $family.relation -or $owner.Count -ne 1 -or
                        $owner[0].status -cne 'deprecated-not-ported' -or
                        $family.type -cne [IO.Path]::GetFileNameWithoutExtension($family.upstream)) {
                        throw "Missing deprecated family owner: $($entry.upstream)"
                    }
                    $familySource = [Text.Encoding]::UTF8.GetString(
                        [IO.File]::ReadAllBytes((Join-Path $UpstreamRoot $family.upstream))).Replace("`r`n", "`n")
                    $familyMasked = Get-MaskedJava $familySource
                    if (-not (Test-DeprecatedType $familyMasked $family.type)) {
                        throw "Deprecated family type is not annotated @Deprecated: $($family.upstream)"
                    }
                    $samePackage = $entry.upstream.Substring(0, $entry.upstream.LastIndexOf('/')) -ceq
                        $family.upstream.Substring(0, $family.upstream.LastIndexOf('/'))
                    $linked = ($masked -cmatch ('\b' + [regex]::Escape($family.type) + '\b')) -or
                        ($typeName -and $typeName -cne 'package-info' -and
                            $familyMasked -cmatch ('\b' + [regex]::Escape($typeName) + '\b')) -or
                        ($typeName -ceq 'package-info' -and $samePackage)
                    if (-not $linked -and $family.via) {
                        # A helper the family reaches only through another not-ported helper (codec-base: ReferenceMap is
                        # the base class of WeakReferenceMap/SoftReferenceMap, which ClassResolvers creates). The via entry
                        # must share the family, sit in the same package and name this type in its code.
                        $via = @($manifest.entries | Where-Object { $_.upstream -ceq $family.via })
                        if ($via.Count -ne 1 -or $via[0].status -cne 'deprecated-not-ported' -or
                            $via[0].notPorted.deprecation.family.upstream -cne $family.upstream -or -not $samePackage -or
                            $family.via.Substring(0, $family.via.LastIndexOf('/')) -cne $entry.upstream.Substring(0, $entry.upstream.LastIndexOf('/'))) {
                            throw "Invalid deprecated family path: $($entry.upstream)"
                        }
                        $viaMasked = Get-MaskedJava ([Text.Encoding]::UTF8.GetString(
                            [IO.File]::ReadAllBytes((Join-Path $UpstreamRoot $family.via))).Replace("`r`n", "`n"))
                        $linked = $typeName -and $viaMasked -cmatch ('\b' + [regex]::Escape($typeName) + '\b')
                    }
                    if (-not $linked) { throw "Deprecated family is not referenced by code: $($entry.upstream)" }
                    if (-not $tags.Count) { $nameSource = @(Get-DeprecatedTags $familySource) }
                } elseif ($deprecation.family) { throw "An annotated deprecated type must not borrow a family: $($entry.upstream)" }
                $tagText = [string]::Join("`n", $nameSource)
                $names = @($decision.replacement.upstreamNames | Where-Object { $null -ne $_ })
                if (-not $tagText -or ($names.Count -eq 0 -and -not $decision.replacement.upstreamNamesAbsent)) {
                    throw "Deprecated entry needs its @deprecated replacement names: $($entry.upstream)"
                }
                foreach ($name in $names) {
                    if (-not $name -or -not $tagText.Contains($name)) { throw "Replacement name is not in the @deprecated text: $name" }
                }
            } else {
                $jvmOnlyReplacedCount++
                if ($decision.replacement.upstreamNames -or $decision.replacement.upstreamNamesAbsent -or $deprecation.family) {
                    throw "JVM-only replacements record CLR replacements, not deprecated-family names: $($entry.upstream)"
                }
            }
            if ($isJava) { $comments = @(Get-NormalizedComments $source | Select-Object -Unique) }
            elseif ($path.EndsWith('.json')) { $comments = @() }
            else { $comments = @(Get-NormalizedYamlComments $source | Select-Object -Unique) }
            $record = $entry.comments
            if ($path.EndsWith('.json') -and $record.disposition -cne 'none') {
                throw "Comment-free JSON metadata must use comment disposition none: $($entry.upstream)"
            }
            if ($record.status -cne 'not-ported' -or $record.uniqueCount -ne $comments.Count -or $record.preservedCount -ne 0 -or
                $record.unreviewedCount -ne 0 -or $record.notApplicableCount -ne $comments.Count) {
                throw "Incorrect not-ported comment accounting: $($entry.upstream)"
            }
            if ($record.disposition -ceq 'whole-file') {
                # One reason for the whole unit; the hash pins the exact ordered original comment set.
                if (-not $record.reason -or $record.PSObject.Properties.Name -contains 'notApplicable' -or
                    $record.sha256 -cne (Get-TextSha256 ([string]::Join("`n", $comments)))) {
                    throw "Whole-file comment disposition does not pin the original comments: $($entry.upstream)"
                }
            } elseif ($record.disposition -ceq 'none') {
                # The RFC 8259 grammar has no comment token (between tokens only the ws rule applies), so there is
                # nothing to keep or to give a disposition.
                if (-not $path.EndsWith('.json') -or $comments.Count -ne 0) { throw "Only comment-free JSON uses disposition none: $($entry.upstream)" }
            } elseif ($record.disposition -ceq 'per-comment') {
                $dispositions = @($record.notApplicable | Where-Object { $null -ne $_ })
                if ($dispositions.Count -ne $comments.Count -or
                    [string]::Join("`n", @($dispositions | ForEach-Object { $_.sourceComment })) -cne [string]::Join("`n", $comments)) {
                    throw "Per-comment dispositions must list every original comment exactly, in order: $($entry.upstream)"
                }
                foreach ($disposition in $dispositions) {
                    if (-not $disposition.reason) { throw 'Missing original-comment not-ported reason.' }
                }
            } else { throw "Unknown not-ported comment disposition: $($entry.upstream)" }
            $notPortedCommentCount += $comments.Count
        } elseif ($entry.notPorted -or $entry.comments.status -ceq 'not-ported' -or $entry.methodReview.notPortedMethods) {
            throw "Not-ported dispositions require a not-ported status: $($entry.upstream)"
        }
        if (-not $notPorted -and $path.EndsWith('.java')) {
            $comments = @(Get-NormalizedComments $source | Select-Object -Unique)
            $ported = @(Get-NormalizedComments $implementation | Select-Object -Unique)
            $missing = @($comments | Where-Object { $_ -cnotin $ported })
            if ($comments.Count -ne $entry.comments.uniqueCount) { throw 'Comment inventory changed.' }
            if ($entry.comments.status -eq 'partial') {
                if ($entry.status -ne 'in-progress' -or $missing.Count -ne $entry.comments.unreviewedCount -or
                    $comments.Count - $missing.Count -ne $entry.comments.preservedCount) {
                    throw "Incorrect partial comment accounting: $($entry.upstream)"
                }
                $partialCommentCount += $missing.Count
            } elseif ($entry.comments.status -eq 'clr-replaced') {
                # The BCL-wrapper branch of the Transport checker has no codec module use.
                throw "$($moduleName) has no BCL-wrapper comment replacements: $($entry.upstream)"
            } elseif ($entry.comments.status -ceq 'reviewed') {
                # Owner rule (2026-11-16): an original comment is ported together with the Java code it documents. A ported
                # entry may leave a comment out of the C# source only when that code is a @Deprecated Java member or uses a
                # JVM-only API; each such comment names the member, which is located in the pinned source. Never for tests.
                $dispositions = @($entry.comments.notApplicable | Where-Object { $null -ne $_ })
                if ($entry.kind -cne 'implementation' -or $entry.status -cne 'clr-adapted' -or
                    $entry.comments.unreviewedCount -ne 0 -or $entry.comments.notApplicableCount -ne $missing.Count -or
                    $entry.comments.preservedCount -ne $comments.Count - $missing.Count -or $dispositions.Count -ne $missing.Count -or
                    $missing.Count -eq 0 -or
                    [string]::Join("`n", @($dispositions | ForEach-Object { $_.sourceComment })) -cne [string]::Join("`n", $missing)) {
                    throw "Reviewed comment dispositions must list every unmatched original comment exactly, in source order: $($entry.upstream)"
                }
                $maskedSource = Get-MaskedJava $source
                foreach ($disposition in $dispositions) { Test-CommentDisposition $source $maskedSource $disposition }
                foreach ($disposition in $dispositions) {
                    if ($disposition.disposition -ceq 'deprecated-not-ported') { $deprecatedCommentCount++ } else { $jvmOnlyCommentCount++ }
                }
            } elseif ($missing.Count) {
                throw "Missing original comment in $($entry.upstream): $($missing[0])"
            }
        }
        if ($entry.kind -eq 'test') {
            $methods = @(Get-DeclaredJavaTests $source | Sort-Object)
            $mapped = @($entry.caseMappings | ForEach-Object { $_.upstreamMethod } | Sort-Object)
            $unreviewed = @($entry.methodReview.unreviewedMethods | Where-Object { $null -ne $_ })
            $notApplicable = @($entry.methodReview.notApplicableMethods | Where-Object { $null -ne $_ })
            $inherited = @($entry.inheritedCaseMappings | Where-Object { $null -ne $_ })
            $notPortedMethods = @($entry.methodReview.notPortedMethods | Where-Object { $null -ne $_ })
            $notApplicableCount += $notApplicable.Count
            $accounted = @($mapped) + @($unreviewed) + @($notApplicable | ForEach-Object { $_.upstreamMethod }) +
                @($notPortedMethods | ForEach-Object { $_.upstreamMethod })
            if (@($accounted | Select-Object -Unique).Count -ne $accounted.Count -or
                ($methods.Count -ne $accounted.Count) -or
                ($methods.Count -gt 0 -and (Compare-Object $methods ($accounted | Sort-Object)))) {
                throw "Test mapping changed or duplicated: $($entry.upstream)"
            }
            if ($entry.methodReview -and ($entry.methodReview.declaredCount -ne $methods.Count -or
                $entry.methodReview.mappedCount -ne $mapped.Count)) { throw 'Incorrect method review counts.' }
            if ($unreviewed.Count -gt 0 -and $entry.status -ne 'in-progress') { throw 'Incomplete fixture marked complete.' }
            if ($notApplicable.Count -gt 0 -and $entry.status -notin @('in-progress', 'clr-adapted')) {
                throw 'Fixture with CLR-inapplicable source tests must not claim full original test equivalence.'
            }
            foreach ($decision in $notApplicable) {
                if ($decision.disposition -ne 'clr-not-applicable' -or -not $decision.reason -or
                    -not $decision.sourceContract -or @($decision.clrEvidence).Count -eq 0) {
                    throw 'Every CLR-inapplicable original method needs its source contract, reason and concrete CLR evidence.'
                }
                foreach ($evidencePath in $decision.clrEvidence) {
                    if (-not (Test-Path (Join-Path $repositoryRoot $evidencePath) -PathType Leaf)) {
                        throw "Missing CLR decision evidence: $evidencePath"
                    }
                }
            }
            if ($entry.methodReview.PSObject.Properties.Name -contains 'reviewedCount') {
                if ($entry.methodReview.reviewedCount -ne $mapped.Count + $notApplicable.Count + $notPortedMethods.Count) {
                    throw 'Incorrect reviewed method count.'
                }
            }
            if ($notPorted) {
                # A not-ported fixture gives every declared original test its own disposition; none is mapped or skipped.
                if (-not $entry.methodReview -or $mapped.Count -or $unreviewed.Count -or $notApplicable.Count -or $inherited.Count -or
                    $notPortedMethods.Count -ne $methods.Count -or $entry.methodReview.reviewedCount -ne $methods.Count) {
                    throw "Not-ported fixture must disposition every declared Java test: $($entry.upstream)"
                }
                foreach ($methodDecision in $notPortedMethods) {
                    if ($methodDecision.disposition -cne $entry.status -or -not $methodDecision.reason -or
                        -not $methodDecision.sourceContract -or @($methodDecision.clrEvidence).Count -eq 0) {
                        throw "Every not-ported original test needs its source contract, reason and CLR evidence: $($methodDecision.upstreamMethod)"
                    }
                    foreach ($evidencePath in $methodDecision.clrEvidence) {
                        if (-not (Test-Path (Join-Path $repositoryRoot $evidencePath) -PathType Leaf)) {
                            throw "Missing not-ported test evidence: $evidencePath"
                        }
                    }
                }
                $notPortedMethodCount += $notPortedMethods.Count
            }
            if (@($inherited.upstreamMethod | Select-Object -Unique).Count -ne $inherited.Count) {
                throw 'Duplicate inherited test mapping.'
            }
            if ($inherited.Count) { throw "$($moduleName) tests have no inherited scenarios: $($entry.upstream)" }
            foreach ($mapping in $inherited) {
                $owner = @($testsuiteEntries | Where-Object { $_.upstream -ceq $mapping.upstreamOwner -and $_.kind -eq 'test' })
                if ($owner.Count -ne 1 -or $mapping.upstreamMethod -cin $methods) { throw 'Unexpected inherited test owner.' }
                $ownerSource = Get-Content (Join-Path $UpstreamRoot $owner[0].upstream) -Raw -Encoding UTF8
                if ($mapping.upstreamMethod -cnotin @(Get-DeclaredJavaTests $ownerSource)) { throw 'Missing inherited original test.' }
            }
            $testCode = [regex]::Replace($implementation,
                '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*', {
                    param($match)
                    [regex]::Replace($match.Value, '[^\r\n]', ' ')
                })
            foreach ($mapping in (@($entry.caseMappings | Where-Object { $null -ne $_ }) + $inherited)) {
                $nativeDeclaration = '\[(?:Fact|Theory)(?:\([^\]]*\))?\]\s*(?:\[[^\]]+\]\s*)*public\s+(?:async\s+)?(?:void|Task)\s+' +
                    [regex]::Escape($mapping.clrMethod) + '\('
                if ($testCode -notmatch $nativeDeclaration) {
                    throw "Missing original test body: $($mapping.clrMethod)"
                }
                if ($mapping.parameterCases) {
                    # A mapped parameterized method must retain every declared input, not just its method name.
                    $theory = [regex]::Match($testCode, '\[Theory\]\s*(?<rows>(?:\[InlineData\([^\]]*\)\]\s*)+)public\s+(?:async\s+)?Task\s+' +
                        [regex]::Escape($mapping.clrMethod) + '\(')
                    if (-not $theory.Success) { throw 'Missing native parameter cases.' }
                    $rows = @([regex]::Matches($theory.Groups['rows'].Value, '\[InlineData\((?<input>[^)]*)\)\]') |
                        ForEach-Object { $_.Groups['input'].Value.Trim().ToLowerInvariant() } | Sort-Object)
                    $expectedRows = @($mapping.parameterCases | ForEach-Object { ([string]::Join(', ', @($_.arguments))).ToLowerInvariant() } | Sort-Object)
                    if (Compare-Object $rows $expectedRows) { throw 'Native parameter cases differ from their mapping.' }
                    $javaRows = @([regex]::Matches($source, '@ValueSource\(booleans\s*=\s*\{([^}]*)\}\)\s*public\s+void\s+' +
                        [regex]::Escape($mapping.upstreamMethod) + '\(') | ForEach-Object { $_.Groups[1].Value.Split(',') } |
                        ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object)
                    if (-not $javaRows.Count -or (Compare-Object $javaRows $expectedRows)) { throw 'Original parameter inputs differ from their mapping.' }
                }
                if ($mapping.nativeCases) {
                    $theory = [regex]::Match($testCode, '\[Theory(?:\([^\]]*\))?\]\s*(?<rows>(?:\[InlineData\([^\]]*\)\]\s*)+)public\s+(?:async\s+)?(?:void|Task)\s+' +
                        [regex]::Escape($mapping.clrMethod) + '\(')
                    if (-not $theory.Success) { throw 'Missing CLR permutation rows.' }
                    $rows = @([regex]::Matches($theory.Groups['rows'].Value, '\[InlineData\((?<input>[^)]*)\)\]') |
                        ForEach-Object { $_.Groups['input'].Value.Replace(' ', '').ToLowerInvariant() } | Sort-Object)
                    $expectedRows = @($mapping.nativeCases | ForEach-Object { ([string]::Join(',', @($_))).ToLowerInvariant() } | Sort-Object)
                    if (Compare-Object $rows $expectedRows) { throw 'CLR permutation rows differ from the manifest.' }
                }
            }
            $mappedCount += $mapped.Count
            $inheritedMappedCount += $inherited.Count
        }
    }

    $entries = @($manifest.entries)
    $pendingEntries = @($entries | Where-Object { $_.status -ceq 'pending' })
    if ($pendingEntries.Count -and -not $AllowPending) { throw "Unreviewed $($moduleName) entry: $($pendingEntries[0].upstream)" }
    $byStatus = @($entries | Group-Object status | Sort-Object Name | ForEach-Object { "$($_.Count) $($_.Name)" })
    Write-Output ("$($moduleName) manifest verified: $($entries.Count) pinned files (" + [string]::Join(', ', $byStatus) + "), " +
        "$mappedCount mapped original tests, $notApplicableCount CLR-inapplicable and $notPortedMethodCount not-ported original tests, " +
        "$deprecatedCommentCount deprecated-member and $jvmOnlyCommentCount JVM-only-API comment dispositions; " +
        "$deprecatedNotPortedCount deprecated-not-ported and $jvmOnlyReplacedCount JVM-only-replaced entries carry $notPortedCommentCount original comments.")
}
