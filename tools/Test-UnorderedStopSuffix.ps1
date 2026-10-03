param(
    [Parameter(Mandatory)][string]$LibraryPath,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceName = 'unordered-stop-suffix'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$probeRoot = Join-Path $repositoryRoot "artifacts/$EvidenceName"
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
$library = [Security.SecurityElement]::Escape((Resolve-Path -LiteralPath $LibraryPath).Path)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><Reference Include="Netty.NET.Common"><HintPath>$library</HintPath></Reference></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $probeRoot 'Probe.csproj') -Encoding utf8
@'
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
var factory = new Factory();
var executor = new UnorderedThreadPoolEventExecutor(1, factory);
try
{
    executor.execute(Runnables.Create(() => { entered.Set(); while (!release.IsSet) Thread.SpinWait(64); }));
    if (!entered.Wait(TimeSpan.FromSeconds(5))) return 2;
    executor.shutdownNow();
    release.Set();
    if (!executor.awaitTermination(TimeSpan.FromSeconds(5)) || !factory.Worker.Join(TimeSpan.FromSeconds(5))) return 3;
    Console.WriteLine($"terminated={executor.isTerminated()}; factorySuffixInterrupted={factory.Interrupted}");
    return factory.Interrupted ? 1 : 0;
}
finally { release.Set(); executor.shutdownNow(); }
sealed class Factory : IThreadFactory
{
    internal Thread Worker;
    internal bool Interrupted;
    public Thread newThread(IRunnable work) => Worker = new Thread(() =>
    {
        work.run();
        try { Thread.Sleep(1); }
        catch (ThreadInterruptedException) { Interrupted = true; }
    }) { IsBackground = true };
}
'@ | Set-Content (Join-Path $probeRoot 'Program.cs') -Encoding utf8
& dotnet build (Join-Path $probeRoot 'Probe.csproj') -c Release --verbosity quiet *> (Join-Path $probeRoot 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $probeRoot 'build.log') -Tail 8; throw 'Suffix probe build failed.' }
& dotnet (Join-Path $probeRoot 'bin/Release/net10.0/Probe.dll') *> (Join-Path $probeRoot 'run.log')
$probeExit = $LASTEXITCODE
Get-Content (Join-Path $probeRoot 'run.log')
"exitCode=$probeExit" | Set-Content (Join-Path $probeRoot 'result.txt') -Encoding utf8
if ($probeExit -ne 0) { throw "Suffix ownership probe failed with exit $probeExit." }
