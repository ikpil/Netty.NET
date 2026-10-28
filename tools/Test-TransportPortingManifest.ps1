param([string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'))

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $repositoryRoot 'docs/transport-porting-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$head = git -C $UpstreamRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $manifest.baseline) { throw 'Unexpected upstream baseline.' }

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
        $_.Groups['annotations'].Value -match '@(?:Test|ParameterizedTest)\b'
    } | ForEach-Object { $_.Groups['name'].Value })
    $annotationCount = [regex]::Matches($masked, '(?m)^[ \t]*@(?:Test|ParameterizedTest)\b').Count
    if ($names.Count -ne $annotationCount -or @($names | Select-Object -Unique).Count -ne $names.Count) {
        throw 'Cannot account for every declared JUnit method exactly once.'
    }
    $names
}

$expected = @($manifest.entries | ForEach-Object { $_.upstream } | Sort-Object)
$actual = @(Get-ChildItem (Join-Path $UpstreamRoot 'transport/src') -Recurse -File |
    Where-Object { $_.Extension -eq '.java' -or $_.Name -in @('module-info.yml', 'hosts-unicode') } |
    ForEach-Object { $_.FullName.Substring((Resolve-Path $UpstreamRoot).Path.Length + 1).Replace('\', '/') } | Sort-Object)
if (Compare-Object $expected $actual) { throw 'Transport inventory changed.' }

$mappedCount = 0
$partialCommentCount = 0
foreach ($entry in $manifest.entries) {
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
    if ($path.EndsWith('.java')) {
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
        } elseif ($missing.Count) {
            throw "Missing original comment in $($entry.upstream): $($missing[0])"
        }
    }
    if ($entry.kind -eq 'test') {
        $methods = @(Get-DeclaredJavaTests $source | Sort-Object)
        $mapped = @($entry.caseMappings | ForEach-Object { $_.upstreamMethod } | Sort-Object)
        $unreviewed = @($entry.methodReview.unreviewedMethods | Where-Object { $null -ne $_ })
        $accounted = @($mapped) + @($unreviewed)
        if (@($accounted | Select-Object -Unique).Count -ne $accounted.Count -or
            ($methods.Count -ne $accounted.Count) -or
            ($methods.Count -gt 0 -and (Compare-Object $methods ($accounted | Sort-Object)))) {
            throw "Test mapping changed or duplicated: $($entry.upstream)"
        }
        if ($entry.methodReview -and ($entry.methodReview.declaredCount -ne $methods.Count -or
            $entry.methodReview.mappedCount -ne $mapped.Count)) { throw 'Incorrect method review counts.' }
        if ($unreviewed.Count -gt 0 -and $entry.status -ne 'in-progress') { throw 'Incomplete fixture marked complete.' }
        $testCode = [regex]::Replace($implementation,
            '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*', {
                param($match)
                [regex]::Replace($match.Value, '[^\r\n]', ' ')
            })
        foreach ($mapping in $entry.caseMappings) {
            if ($testCode -notmatch ('\[Fact(?:\([^\]]*\))?\]\s*public\s+(?:async\s+)?(?:void|Task)\s+' + [regex]::Escape($mapping.clrMethod) + '\(')) {
                throw "Missing original test body: $($mapping.clrMethod)"
            }
            $mappedCount++
        }
    }
}
Write-Output "Transport manifest verified: $($manifest.entries.Count) inventory entries, $mappedCount mapped original tests, $partialCommentCount original comments still awaiting review in partial entries. Pending entries remain unreviewed."
