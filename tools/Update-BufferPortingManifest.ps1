param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'),
    [string]$Baseline,
    [switch]$Check,
    # Alternate manifest to check, e.g. a deliberately broken copy proving that the checker rejects it.
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'

function Get-DeclaredJavaTests([string]$Source) {
    # Mask comments and literals before inspecting annotations. Preserve newlines
    # so declarations cannot be invented by joining unrelated source lines.
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*'
    $masked = [regex]::Replace($Source, $tokens, {
        param($match)
        [regex]::Replace($match.Value, '[^\r\n]', ' ')
    })
    # Annotation arrays (for example @ValueSource(ints = { 0, 1 })) contain
    # braces inside their parentheses; they are not test method bodies.
    $declarations = '(?m)^[ \t]*(?<annotations>(?:@[\w.]+(?:[ \t]*\([^();]*\))?[ \t\r\n]*)+)' +
        '(?:(?:public|protected|private|static|final|synchronized|abstract)[ \t]+)*' +
        'void[ \t]+(?<name>\w+)[ \t]*\([^;{}]*\)(?:[ \t\r\n]+throws[^{;]+)?[ \t\r\n]*\{'
    $names = [Collections.Generic.List[string]]::new()
    foreach ($declaration in [regex]::Matches($masked, $declarations)) {
        # JUnit runs @Test, @ParameterizedTest and @RepeatedTest methods; each declaration needs one decision.
        if ($declaration.Groups['annotations'].Value -match '@(?:Test|ParameterizedTest|RepeatedTest)\b') {
            $names.Add($declaration.Groups['name'].Value)
        }
    }
    $annotations = [regex]::Matches($masked, '(?m)^[ \t]*@(?:Test|ParameterizedTest|RepeatedTest)\b').Count
    if ($names.Count -ne $annotations -or @($names | Select-Object -Unique).Count -ne $names.Count) {
        throw 'Cannot account for each declared JUnit test exactly once; inspect the source parser.'
    }
    $names.ToArray()
}

function Get-TestMethodReview($Entry, [string[]]$Methods) {
    $mapped = @($Entry.caseMappings | Where-Object { $null -ne $_ } | ForEach-Object { $_.upstreamMethod })
    $findings = @($Entry.methodFindings | Where-Object { $null -ne $_ } | ForEach-Object { $_.upstreamMethod })
    $reviewed = @($mapped) + @($findings)
    if (@($reviewed | Select-Object -Unique).Count -ne $reviewed.Count) {
        throw "Duplicate method decision in $($Entry.upstream)."
    }
    foreach ($method in $reviewed) {
        if ($method -notin $Methods) { throw "Unknown original test $method in $($Entry.upstream)." }
    }
    foreach ($mapping in $Entry.caseMappings) {
        if (-not $mapping.adaptation -or -not $mapping.clrTests) {
            throw "Missing mapping evidence for $($mapping.upstreamMethod)."
        }
        foreach ($target in $mapping.clrTests) {
            if ($target.path -notin $Entry.implementation) { throw "Unregistered test target $($target.path)." }
            $targetPath = Join-Path $repositoryRoot $target.path
            $targetCode = Get-Content -LiteralPath $targetPath -Raw -Encoding UTF8
            if ($targetCode -notmatch ($clrTestDeclaration + [regex]::Escape($target.method) + '\s*\(')) {
                throw "Cannot find CLR test $($target.path):$($target.method)."
            }
        }
    }
    foreach ($finding in $Entry.methodFindings) {
        if ($finding.state -notin @('test-gap', 'implementation-gap') -or -not $finding.reason -or
                -not $finding.sourceEvidence) { throw "Incomplete method finding for $($finding.upstreamMethod)." }
        foreach ($path in $finding.sourceEvidence) {
            if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $path) -PathType Leaf)) {
                throw "Missing finding source $path."
            }
        }
    }
    [ordered]@{
        scope = 'Declared JUnit test methods only; inherited provider variants are not counted. A mapping records its stated scenarios, not whole-method/provider completion.'
        unreviewedMeaning = 'No individual decision yet; this does not mean the implementation or all equivalent CLR tests are missing.'
        declaredCount = $Methods.Count
        mappedCount = $mapped.Count
        testGapCount = @($Entry.methodFindings | Where-Object state -eq 'test-gap').Count
        implementationGapCount = @($Entry.methodFindings | Where-Object state -eq 'implementation-gap').Count
        unreviewedCount = @($Methods | Where-Object { $_ -notin $reviewed }).Count
        unreviewedMethods = @($Methods | Where-Object { $_ -notin $reviewed })
    }
}

function Assert-ProviderInvocationReview($Entry, [string]$Source, [string[]]$Methods) {
    $review = $Entry.providerInvocationReview
    if (-not $review) { return }
    # This ledger explicitly supports ByteBufUtilTest's pinned noUnsafe/ValueSource
    # factories. Fail on new provider shapes instead of guessing executable rows.
    if ($review.status -ne 'verified' -or $review.providerSource -ne 'noUnsafe') {
        throw "Unsupported provider invocation review in $($Entry.upstream)."
    }
    $factory = [regex]::Match($Source, '(?s)public static Collection<Object\[\]> noUnsafe\(\)\s*\{(?<body>.*?)\r?\n    \}')
    if (-not $factory.Success) { throw 'Cannot parse pinned noUnsafe provider factory.' }
    $providers = @([regex]::Matches($factory.Groups['body'].Value, 'BufferType\.(\w+)') | ForEach-Object { $_.Groups[1].Value })
    if ($providers.Count -ne 6 -or @($providers | Select-Object -Unique).Count -ne 6) { throw 'Unexpected noUnsafe providers.' }
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*'
    $masked = [regex]::Replace($Source, $tokens, { param($match) [regex]::Replace($match.Value, '[^\r\n]', ' ') })
    $expected = @{}
    foreach ($method in $Methods) {
        $signature = [regex]::Match($masked, ('(?m)^    public void ' + [regex]::Escape($method) + '\((?<args>[^)]*)\)'))
        if (-not $signature.Success) { throw "Cannot parse provider signature $method." }
        $parameterShape = $signature.Groups['args'].Value.Trim()
        $arguments = @('none')
        if ($parameterShape -eq 'BufferType bufferType') {
            if ($Source -notmatch ('@MethodSource\("noUnsafe"\)\s+public void ' + [regex]::Escape($method) + '\(')) {
                throw "Unknown provider source on $method."
            }
            $arguments = $providers
        } elseif ($parameterShape -eq 'int needleReaderIndex') {
            $values = [regex]::Match($Source, ('@ValueSource\(ints\s*=\s*\{(?<values>[-\d,\s]+)\}\)\s+public void ' + [regex]::Escape($method) + '\('))
            if (-not $values.Success) { throw "Unknown value source on $method." }
            $arguments = @($values.Groups['values'].Value.Split(',') | ForEach-Object { ([int]$_.Trim()).ToString() })
        } elseif ($parameterShape) { throw "Unsupported parameter shape on $method." }
        $opening = $masked.IndexOf('{', $signature.Index + $signature.Length)
        $depth = 1; $closing = $opening + 1
        while ($depth -gt 0 -and $closing -lt $masked.Length) {
            if ($masked[$closing] -eq '{') { ++$depth } elseif ($masked[$closing] -eq '}') { --$depth }
            ++$closing
        }
        if ($depth -ne 0) { throw "Unclosed provider method $method." }
        $body = $masked.Substring($opening, $closing - $opening)
        $heapOnly = $body -match 'assumeTrue\(bufferType\s*==\s*BufferType\.HEAP_UNPOOLED\)'
        if ($body -match '\bassumeTrue\s*\(' -and -not $heapOnly) { throw "Unknown assumption on $method." }
        foreach ($argument in $arguments) {
            $key = "$method|$argument"
            if ($expected.ContainsKey($key)) { throw "Duplicate source invocation $key." }
            $expected[$key] = if ($heapOnly -and $argument -ne 'HEAP_UNPOOLED') { 'original-assumption-only' } else { 'ported' }
        }
    }
    $seen = @{}; $ported = 0; $assumptions = 0
    foreach ($row in $review.invocations) {
        $key = "$($row.upstreamMethod)|$($row.argument)"
        if ($seen.ContainsKey($key) -or -not $expected.ContainsKey($key) -or $expected[$key] -ne $row.state) {
            throw "Unknown, duplicate or misclassified provider invocation $key."
        }
        $seen[$key] = $true
        if ($row.state -eq 'original-assumption-only') {
            ++$assumptions
            if ($row.clrTests.Count -ne 0 -or -not $row.adaptation) { throw "Missing assumption decision $key." }
            continue
        }
        ++$ported
        if (-not $row.clrTests -or -not $row.adaptation) { throw "Missing CLR provider evidence $key." }
        foreach ($target in $row.clrTests) {
            if ($target.path -notin $Entry.implementation -or -not $target.selector) { throw "Unregistered CLR row target $key." }
            $targetCode = Get-Content -LiteralPath (Join-Path $repositoryRoot $target.path) -Raw -Encoding UTF8
            if ($targetCode -notmatch ($clrTestDeclaration + [regex]::Escape($target.method) + '\s*\(')) {
                throw "Missing CLR provider test $key."
            }
        }
    }
    if ($seen.Count -ne $expected.Count -or $review.originalMethods -ne $Methods.Count -or
        $review.originalInvocations -ne $expected.Count -or $review.executedBodies -ne $ported -or $review.assumptionOnly -ne $assumptions) {
        throw "Stale provider invocation summary in $($Entry.upstream)."
    }
    Write-Output "$([IO.Path]::GetFileName($Entry.upstream)): $ported executable provider tuples mapped, $assumptions original assumption-only tuples accounted for."
}

$pinnedSources = @{}
function Get-PinnedJava([string]$Path) {
    if (-not $pinnedSources.ContainsKey($Path)) {
        $text = @(git -C $UpstreamRoot show "${Baseline}:$Path") -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "Cannot read pinned source $Path." }
        $pinnedSources[$Path] = $text
    }
    $pinnedSources[$Path]
}

function Get-MaskedJavaSource([string]$Source) {
    $tokens = '(?s)"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|/\*.*?\*/|//[^\r\n]*'
    [regex]::Replace($Source, $tokens, { param($match) [regex]::Replace($match.Value, '[^\r\n]', ' ') })
}

function Get-JavaTestAncestors([string]$Path) {
    # Pinned test classes this fixture extends, nearest first. JUnit runs their tests in this fixture too.
    $chain = [Collections.Generic.List[string]]::new()
    $current = $Path
    while ($true) {
        $masked = Get-MaskedJavaSource (Get-PinnedJava $current)
        $type = [IO.Path]::GetFileNameWithoutExtension($current)
        $match = [regex]::Match($masked, '\bclass\s+' + [regex]::Escape($type) + '\b[^{]*?\bextends\s+(\w+)')
        if (-not $match.Success) { break }
        $parent = $current.Substring(0, $current.LastIndexOf('/') + 1) + $match.Groups[1].Value + '.java'
        if ($parent -notin $upstreamFiles) { break }
        $chain.Add($parent)
        $current = $parent
    }
    $chain.ToArray()
}

function Assert-DeprecationRecord($Record, [string]$Fixture, [string[]]$Ancestors) {
    # Owner decision 2026-11-16: @Deprecated Java APIs whose javadoc names a replacement are not ported.
    # The record must quote the pinned @deprecated text exactly and show that the fixture depends on the member.
    if (-not $Record -or -not $Record.upstream -or -not $Record.member -or -not $Record.javadoc -or
            @($Record.replacementNames | Where-Object { $_ }).Count -eq 0 -or -not $Record.fixtureUse) {
        throw "Incomplete deprecation record: $Fixture"
    }
    $lines = (Get-PinnedJava $Record.upstream) -split "`n"
    $at = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        if ($line -ceq "$($Record.member);" -or $line -ceq "$($Record.member) {") {
            if ($at -ge 0) { throw "Ambiguous deprecated member $($Record.member)." }
            $at = $i
        }
    }
    if ($at -lt 2 -or $lines[$at - 1].Trim() -cne '@Deprecated' -or $lines[$at - 2].Trim() -cne '*/') {
        throw "Recorded member is not a documented @Deprecated declaration: $($Record.member)"
    }
    $start = $at - 2
    while ($start -ge 0 -and $lines[$start].Trim() -cne '/**') { --$start }
    if ($start -lt 0) { throw "Cannot find the javadoc of $($Record.member)." }
    $doc = @($lines[($start + 1)..($at - 3)] | ForEach-Object { $_.Trim() -replace '^\*\s?', '' })
    $tag = @()
    $inTag = $false
    foreach ($line in $doc) {
        if ($line.StartsWith('@deprecated ')) { $inTag = $true; $tag += $line.Substring(12); continue }
        if ($inTag -and $line.StartsWith('@')) { break }
        if ($inTag) { $tag += $line }
    }
    $text = (($tag -join ' ') -replace '\s+', ' ').Trim()
    if (-not $text -or $text -cne $Record.javadoc) {
        throw "Recorded @deprecated text differs from the pinned Java source: $Fixture"
    }
    foreach ($name in $Record.replacementNames) {
        if (-not $text.Contains($name)) { throw "Replacement name is not in the @deprecated text: $name" }
    }
    $memberCall = [regex]::Match($Record.member, '(\w+)\s*\(').Groups[1].Value + '('
    if (-not (Get-MaskedJavaSource (Get-PinnedJava $Fixture)).Contains($Record.fixtureUse)) {
        throw "Fixture code does not contain the recorded deprecated use: $Fixture"
    }
    if (-not $Record.fixtureUse.Contains($memberCall)) {
        # The fixture only selects the argument; an ancestor fixture makes the deprecated call.
        $use = $Record.memberUse
        if (-not $use -or $use.upstream -notin $Ancestors -or -not $use.snippet.Contains($memberCall) -or
                -not (Get-MaskedJavaSource (Get-PinnedJava $use.upstream)).Contains($use.snippet)) {
            throw "No recorded ancestor call of the deprecated member for $Fixture"
        }
    }
}

function Assert-EvidenceFiles($Paths, $Urls, [string]$Context) {
    if (@($Paths | Where-Object { $_ }).Count -eq 0 -or @($Urls | Where-Object { $_ }).Count -eq 0) {
        throw "Missing evidence files or URLs: $Context"
    }
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $path) -PathType Leaf)) { throw "Missing evidence $path ($Context)." }
    }
    foreach ($url in $Urls) {
        if ($url -cnotmatch '^https://\S+$') { throw "Invalid evidence URL $url ($Context)." }
    }
}

function Assert-ClrTestTarget($Target, $Implementation, [string]$Context) {
    if ($null -ne $Implementation -and $Target.path -notin $Implementation) { throw "Unregistered test target $($Target.path) ($Context)." }
    $targetPath = Join-Path $repositoryRoot $Target.path
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { throw "Missing CLR test file $($Target.path) ($Context)." }
    $targetCode = Get-Content -LiteralPath $targetPath -Raw -Encoding UTF8
    if ($targetCode -notmatch ($clrTestDeclaration + [regex]::Escape($Target.method) + '\s*\(')) {
        throw "Cannot find CLR test $($Target.path):$($Target.method) ($Context)."
    }
}

function Assert-TestDisposition($Entry) {
    # Finished test entries (verified, deprecated-not-ported, jvm-only-replaced) give every declared original
    # test exactly one decision: a same-name CLR test or a not-ported record with reason and evidence.
    $status = $Entry.status
    $notPorted = $status -cin $notPortedStatuses
    $methodDecisions = @($Entry.notPortedMethods | Where-Object { $null -ne $_ })
    if (-not $notPorted -and $Entry.notPorted) { throw "Not-ported decision without a not-ported status: $($Entry.upstream)" }
    if ($status -cne 'verified' -and -not $notPorted -and $methodDecisions.Count -eq 0) { return $null }
    $methods = @(Get-DeclaredJavaTests (Get-PinnedJava $Entry.upstream))
    $ancestors = @(Get-JavaTestAncestors $Entry.upstream)
    $mappings = @($Entry.caseMappings | Where-Object { $null -ne $_ })
    $decided = @($mappings | ForEach-Object { $_.upstreamMethod }) + @($methodDecisions | ForEach-Object { $_.upstreamMethod })
    if (@($decided | Select-Object -Unique).Count -ne $decided.Count) { throw "Duplicate method decision in $($Entry.upstream)." }
    foreach ($method in $decided) {
        if ($method -cnotin $methods) { throw "Unknown original test $method in $($Entry.upstream)." }
    }
    foreach ($decision in $methodDecisions) {
        $context = "$($Entry.upstream)#$($decision.upstreamMethod)"
        if ($decision.disposition -cnotin $notPortedStatuses -or -not $decision.sourceContract -or -not $decision.reason) {
            throw "Incomplete not-ported method disposition: $context"
        }
        Assert-EvidenceFiles $decision.clrEvidence $decision.urls $context
        if ($decision.disposition -ceq 'deprecated-not-ported') { Assert-DeprecationRecord $decision.deprecation $Entry.upstream $ancestors }
        elseif ($decision.deprecation) { throw "JVM-only method decisions carry no deprecation record: $context" }
    }
    if ($status -cne 'verified' -and -not $notPorted) { return $null }
    $script:notPortedMethodTotal += $methodDecisions.Count
    $undecided = @($methods | Where-Object { $_ -cnotin $decided })
    if ($undecided.Count) { throw "Finished entry $($Entry.upstream) leaves original tests undecided: $($undecided -join ', ')" }
    if (@($Entry.methodFindings | Where-Object { $null -ne $_ }).Count) { throw "Finished entry still lists open method findings: $($Entry.upstream)" }
    if ($status -ceq 'verified') {
        if (@($Entry.implementation).Count -eq 0) { throw "Verified test entry without CLR implementation: $($Entry.upstream)" }
        foreach ($path in $Entry.implementation) {
            if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $path) -PathType Leaf)) { throw "Missing implementation $path." }
        }
        foreach ($mapping in $mappings) {
            $context = "$($Entry.upstream)#$($mapping.upstreamMethod)"
            if (-not $mapping.adaptation -or @($mapping.clrTests).Count -eq 0) { throw "Missing mapping evidence: $context" }
            # 2026-11-14 owner decision: the first CLR test carries the original name; only the first letter's case differs.
            $expected = $mapping.upstreamMethod.Substring(0, 1).ToUpperInvariant() + $mapping.upstreamMethod.Substring(1)
            if (@($mapping.clrTests)[0].method -cne $expected) { throw "First CLR test must be $expected ($context)." }
            foreach ($target in $mapping.clrTests) { Assert-ClrTestTarget $target $Entry.implementation $context }
        }
        if ($ancestors.Count -and -not $Entry.inheritedSuiteReview.summary) {
            throw "Verified fixture inherits original tests from $($ancestors -join ', ') but records no inheritedSuiteReview: $($Entry.upstream)"
        }
        return 'verified'
    }
    $decision = $Entry.notPorted
    if (@($Entry.implementation).Count -ne 0 -or $mappings.Count -ne 0 -or -not $decision -or -not $decision.decisionDate -or
            -not $decision.sourceContract -or -not $decision.reason -or -not $decision.replacement.clr -or
            @($decision.replacement.clrTests).Count -eq 0) {
        throw "Incomplete not-ported disposition: $($Entry.upstream)"
    }
    Assert-EvidenceFiles $decision.evidence.files $decision.evidence.urls $Entry.upstream
    foreach ($target in $decision.replacement.clrTests) { Assert-ClrTestTarget $target $null $Entry.upstream }
    foreach ($methodDecision in $methodDecisions) {
        if ($methodDecision.disposition -cne $status) { throw "Method disposition differs from the fixture's: $($Entry.upstream)#$($methodDecision.upstreamMethod)" }
    }
    if ($status -ceq 'deprecated-not-ported') {
        if ($decision.jvmOnly) { throw "A deprecated fixture records no JVM-only selection: $($Entry.upstream)" }
        Assert-DeprecationRecord $decision.deprecation $Entry.upstream $ancestors
    } else {
        if ($decision.deprecation) { throw "A JVM-only fixture records no deprecation: $($Entry.upstream)" }
        # The JVM-only provider selection must be what the pinned fixture code actually does.
        $selection = @($decision.jvmOnly.selection | Where-Object { $_ })
        $masked = Get-MaskedJavaSource (Get-PinnedJava $Entry.upstream)
        if ($selection.Count -eq 0 -or -not $decision.jvmOnly.api) { throw "Missing JVM-only selection: $($Entry.upstream)" }
        foreach ($snippet in $selection) {
            if (-not $masked.Contains($snippet)) { throw "Fixture code does not contain the recorded JVM-only selection '$snippet': $($Entry.upstream)" }
        }
    }
    $status
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# A CLR test may be async/unsafe, and a port of an original test that a subclass fixture overrides is
# virtual/override/sealed: xUnit, like JUnit, runs the most-derived override once per concrete class.
$clrTestDeclaration = '\bpublic\s+(?:(?:async|unsafe|virtual|override|sealed)\s+)*(?:void|Task|ValueTask)\s+'
# Owner decision 2026-11-16: deprecated-with-replacement and JVM-only original fixtures are recorded, not ported.
$notPortedStatuses = @('deprecated-not-ported', 'jvm-only-replaced')
$notPortedMethodTotal = 0
$manifestPath = if ($ManifestPath) { [IO.Path]::GetFullPath($ManifestPath) } else { Join-Path $repositoryRoot 'docs/buffer-porting-manifest.json' }
$previous = @{}
if (Test-Path -LiteralPath $manifestPath) {
    $existing = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $Baseline) { $Baseline = $existing.baseline }
    foreach ($entry in $existing.entries) { $previous[$entry.upstream] = $entry }
}
if (-not $Baseline) { $Baseline = '64cc10f38ea5f5bd7eae48507817c66680d0afdc' }
if ($Check -and (-not $existing -or $Baseline -ne $existing.baseline)) {
    throw 'Check requires an existing manifest with the same baseline.'
}
$upstreamFiles = @(git -C $UpstreamRoot ls-tree -r --name-only $Baseline -- buffer/src/main/java buffer/src/test/java)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the pinned upstream commit.' }
$entries = foreach ($path in $upstreamFiles) {
    if (-not $path.EndsWith('.java')) { continue }
    if ($previous.ContainsKey($path)) { $previous[$path]; continue }
    [ordered]@{
        upstream = $path
        kind = if ($path.StartsWith('buffer/src/test/')) { 'test' } else { 'source' }
        status = 'pending'
        implementation = @()
        evidence = @()
        adaptation = ''
    }
}
$entries = @($entries)
$baseFixtures = @('AbstractByteBufTest', 'AbstractCompositeByteBufTest', 'AbstractByteBufAllocatorTest', 'AbstractPooledByteBufTest', 'FixedCompositeByteBufTest', 'ReadOnlyByteBufTest', 'DuplicatedByteBufTest', 'ReadOnlyByteBufferBufTest', 'UnpooledTest', 'ByteBufUtilTest')
foreach ($name in $baseFixtures) {
    $path = "buffer/src/test/java/io/netty/buffer/$name.java"
    $entry = $entries | Where-Object upstream -eq $path
    if (-not $entry) { throw "Missing fixture $path." }
    $source = @(git -C $UpstreamRoot show "${Baseline}:$path") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read pinned fixture $path." }
    $methods = @(Get-DeclaredJavaTests $source)
    $review = Get-TestMethodReview $entry $methods
    Assert-ProviderInvocationReview $entry $source $methods
    if ($Check) {
        if (($entry.testMethodReview | ConvertTo-Json -Depth 20 -Compress) -cne
                ($review | ConvertTo-Json -Depth 20 -Compress)) {
            throw "Stale method review in $path; regenerate the manifest."
        }
    } else {
        $entry | Add-Member -NotePropertyName testMethodReview -NotePropertyValue $review -Force
    }
    Write-Output "$name`: $($review.declaredCount) declared, $($review.mappedCount) mapped, $($review.testGapCount) test gaps, $($review.implementationGapCount) implementation gaps, $($review.unreviewedCount) unreviewed."
}
$finished = @{ 'verified' = 0; 'deprecated-not-ported' = 0; 'jvm-only-replaced' = 0 }
$open = @()
foreach ($entry in $entries) {
    if ($entry.kind -ne 'test') {
        if ($entry.status -cin $notPortedStatuses -or $entry.notPorted -or $entry.notPortedMethods) {
            throw "Not-ported test dispositions apply to test entries only: $($entry.upstream)"
        }
        continue
    }
    if ($entry.status -cnotin @('pending', 'in-progress', 'verified', 'clr-replacement') + $notPortedStatuses) {
        throw "Unknown test status $($entry.status): $($entry.upstream)"
    }
    $result = Assert-TestDisposition $entry
    if ($result) { ++$finished[$result] }
    if ($entry.status -cin @('pending', 'in-progress')) { $open += [IO.Path]::GetFileNameWithoutExtension($entry.upstream) }
}
Write-Output ("Test dispositions: $($finished['verified']) verified fixtures with complete declared-method partitions, " +
    "$($finished['deprecated-not-ported']) deprecated-not-ported and $($finished['jvm-only-replaced']) JVM-only-replaced fixtures, " +
    "$notPortedMethodTotal not-ported declared methods; $($open.Count) test entries pending/in-progress$(if ($open.Count) { ': ' + ($open -join ', ') }).")
if ($Check) {
    $expected = @($entries | ForEach-Object { $_.upstream })
    $actual = @($existing.entries | ForEach-Object { $_.upstream })
    if (($expected -join "`n") -cne ($actual -join "`n")) { throw 'Stale original file inventory.' }
    Write-Output 'Pinned file inventory, method partition and CLR target declarations agree.'
    return
}
[ordered]@{
    baseline = $Baseline
    scope = @('buffer/src/main/java', 'buffer/src/test/java')
    statusMeaning = if ($existing.statusMeaning) { $existing.statusMeaning } else {
        'pending: no completed review; in-progress: only stated API/scenarios are implemented; verified: the stated complete source/test contract is reviewed; clr-replacement/not-applicable require an explicit consumer-based decision. Building a project or translating selected scenarios does not complete buffer.'
    }
    entries = @($entries)
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Inventoried $(@($entries).Count) upstream Java files."
