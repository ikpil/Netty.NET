param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty')
)

$ErrorActionPreference = 'Stop'
$baseline = 'e66ce34777f9c4a0c57ac74bb97396ca2f54b43c'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repositoryRoot 'docs/common-porting-manifest.json'
$previous = @{}
if (Test-Path -LiteralPath $manifestPath) {
    $existing = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $existing.entries) { $previous[$entry.upstream] = $entry }
}

$upstreamFiles = @(git -C $UpstreamRoot ls-tree -r --name-only $baseline -- common/src/main/java common/src/test/java)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the pinned upstream commit.' }
$localFiles = @(git -C $repositoryRoot ls-files --cached --others --exclude-standard -- src test)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory local files.' }
$entries = foreach ($path in $upstreamFiles) {
    if (-not $path.EndsWith('.java')) { continue }
    $name = [IO.Path]::GetFileNameWithoutExtension($path)
    $isTest = $path.StartsWith('common/src/test/')
    $root = if ($isTest) { 'test/' } else { 'src/' }
    $candidates = @($localFiles | Where-Object {
        $_.StartsWith($root) -and
        ([IO.Path]::GetFileName($_) -eq "$name.cs" -or
         [IO.Path]::GetFileName($_) -eq "I$name.cs")
    })
    if ($previous.ContainsKey($path)) {
        $entry = $previous[$path]
        $entry.candidates = $candidates
        $entry
        continue
    }
    [ordered]@{
        upstream = $path
        kind = if ($isTest) { 'test' } else { 'source' }
        status = 'pending'
        candidates = $candidates
        implementation = @()
        evidence = @()
        adaptation = ''
    }
}

$manifest = [ordered]@{
    baseline = $baseline
    scope = @('common/src/main/java', 'common/src/test/java')
    statusMeaning = 'verified means reviewed against the pinned source and tested; candidates alone imply nothing'
    entries = @($entries)
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Inventoried $(@($entries).Count) upstream Java files."
