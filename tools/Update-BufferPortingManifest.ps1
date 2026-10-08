param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty'),
    [string]$Baseline
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repositoryRoot 'docs/buffer-porting-manifest.json'
$previous = @{}
if (Test-Path -LiteralPath $manifestPath) {
    $existing = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $Baseline) { $Baseline = $existing.baseline }
    foreach ($entry in $existing.entries) { $previous[$entry.upstream] = $entry }
}
if (-not $Baseline) { $Baseline = '64cc10f38ea5f5bd7eae48507817c66680d0afdc' }
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
[ordered]@{
    baseline = $Baseline
    scope = @('buffer/src/main/java', 'buffer/src/test/java')
    statusMeaning = 'pending: no completed review; in-progress: only stated API/scenarios are implemented; verified: the stated complete source/test contract is reviewed; clr-replacement/not-applicable require an explicit consumer-based decision. Building a project or translating selected scenarios does not complete buffer.'
    entries = @($entries)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Inventoried $(@($entries).Count) upstream Java files."
