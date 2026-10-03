param(
    [Parameter(Mandatory)][string]$LibraryPath,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceName = 'unordered-queue-costs'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = Join-Path $repositoryRoot "artifacts/$EvidenceName"
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$library = (Resolve-Path -LiteralPath $LibraryPath).Path
$project = Join-Path $PSScriptRoot 'queue-costs/QueueCosts.csproj'
& dotnet build $project -c Release --artifacts-path (Join-Path $outputRoot 'build') "-p:LibraryPath=$library" --verbosity quiet *> (Join-Path $outputRoot 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $outputRoot 'build.log') -Tail 12; throw 'Queue measurement build failed.' }
$previousTiering = [Environment]::GetEnvironmentVariable('DOTNET_TieredCompilation')
try {
    # Avoid candidate order changing tiered-JIT state halfway through a short probe.
    [Environment]::SetEnvironmentVariable('DOTNET_TieredCompilation', '0')
    & dotnet (Join-Path $outputRoot 'build/bin/QueueCosts/release/QueueCosts.dll') (Join-Path $outputRoot 'results.json') *> (Join-Path $outputRoot 'run.log')
    $probeExit = $LASTEXITCODE
}
finally { [Environment]::SetEnvironmentVariable('DOTNET_TieredCompilation', $previousTiering) }
Get-Content (Join-Path $outputRoot 'run.log')
[ordered]@{
    library = $library
    librarySha256 = (Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash
    sourceHead = (git -C $repositoryRoot rev-parse HEAD)
    measuredAtUtc = [DateTime]::UtcNow.ToString('O')
    exitCode = $probeExit
} | ConvertTo-Json | Set-Content (Join-Path $outputRoot 'evidence.json') -Encoding utf8
if ($probeExit -ne 0) { throw "Queue measurement failed with exit $probeExit." }
