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
    $declarations = '(?m)^[ \t]*(?<annotations>(?:@[\w.]+(?:[ \t]*\([^{};]*?\))?[ \t\r\n]*)+)' +
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
$baseFixtures = @('AbstractByteBufTest', 'AbstractCompositeByteBufTest', 'AbstractByteBufAllocatorTest', 'AbstractPooledByteBufTest', 'FixedCompositeByteBufTest', 'ReadOnlyByteBufTest', 'DuplicatedByteBufTest', 'ReadOnlyByteBufferBufTest', 'UnpooledTest')
foreach ($name in $baseFixtures) {
    $path = "buffer/src/test/java/io/netty/buffer/$name.java"
    $entry = $entries | Where-Object upstream -eq $path
    if (-not $entry) { throw "Missing fixture $path." }
    $source = @(git -C $UpstreamRoot show "${Baseline}:$path") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read pinned fixture $path." }
    $methods = @(Get-DeclaredJavaTests $source)
    $review = Get-TestMethodReview $entry $methods
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
