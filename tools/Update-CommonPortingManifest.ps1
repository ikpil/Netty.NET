param(
    [string]$UpstreamRoot = (Join-Path $PSScriptRoot '../../netty')
)

$ErrorActionPreference = 'Stop'
$baseline = '64cc10f38ea5f5bd7eae48507817c66680d0afdc'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repositoryRoot 'docs/common-porting-manifest.json'
$previous = @{}
if (Test-Path -LiteralPath $manifestPath) {
    $existing = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in $existing.entries) { $previous[$entry.upstream] = $entry }
}

# Since 2026-11-19 the scope is all of common/src (Java roots, collection templates, codegen script, resources and
# test data) and every file is inventoried, not only *.java.
$scope = @('common/src')
$upstreamFiles = @(git -C $UpstreamRoot ls-tree -r --name-only $baseline -- $scope)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the pinned upstream commit.' }
$localFiles = @(git -C $repositoryRoot ls-files --cached --others --exclude-standard -- src test)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory local files.' }
$entries = foreach ($path in $upstreamFiles) {
    $name = [IO.Path]::GetFileNameWithoutExtension($path)
    $isTest = $path.StartsWith('common/src/test/')
    $root = if ($isTest) { 'test/' } else { 'src/' }
    $candidates = @($localFiles | Where-Object {
        $_.StartsWith($root) -and
        ([IO.Path]::GetFileName($_) -eq "$name.cs" -or
         [IO.Path]::GetFileName($_) -eq "I$name.cs") -and
        (Test-Path -LiteralPath (Join-Path $repositoryRoot $_))
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
    scope = $scope
    statusMeaning = 'verified records the stated behavioral review and tests, not completion of the native API/backend redesign; clr-replacement records a CLR framework substitution; not-applicable records an upstream implementation/test with no CLR purpose; deprecated-not-ported records an upstream @Deprecated type that is not ported by owner decision, with its deprecation text and replacement; candidates alone imply nothing'
    entries = @($entries)
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Inventoried $(@($entries).Count) upstream files."
