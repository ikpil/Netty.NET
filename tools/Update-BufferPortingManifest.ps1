param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'),
    [string]$Baseline,
    [switch]$Check
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
        if ($declaration.Groups['annotations'].Value -match '@(?:Test|ParameterizedTest)\b') {
            $names.Add($declaration.Groups['name'].Value)
        }
    }
    $annotations = [regex]::Matches($masked, '(?m)^[ \t]*@(?:Test|ParameterizedTest)\b').Count
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
            if ($targetCode -notmatch ('\bpublic\s+(?:(?:async|unsafe)\s+)*(?:void|Task|ValueTask)\s+' +
                    [regex]::Escape($target.method) + '\s*\(')) {
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
            if ($targetCode -notmatch ('\bpublic\s+(?:(?:async|unsafe)\s+)*(?:void|Task|ValueTask)\s+' + [regex]::Escape($target.method) + '\s*\(')) {
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

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repositoryRoot 'docs/buffer-porting-manifest.json'
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
