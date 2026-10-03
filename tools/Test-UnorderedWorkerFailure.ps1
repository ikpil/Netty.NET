param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceName = 'unordered-worker-failure',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$probeRoot = Join-Path $repositoryRoot "artifacts/$EvidenceName"
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
$library = [Security.SecurityElement]::Escape((Join-Path $repositoryRoot 'src/Netty.NET.Common/Netty.NET.Common.csproj'))
# The existing test friend name permits an internal fault injection. This assembly
# lives only in ignored artifacts, executes in isolation, and is never packaged.
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Netty.NET.Common.Tests</AssemblyName><ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$library" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $probeRoot 'Probe.csproj') -Encoding utf8
@'
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal.Logging;
using System.Reflection;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        string mode = args[0];
        if (mode == "logger") InternalLoggerFactory.setDefaultFactory(new LoggingFactory());
        var expected = new InvalidOperationException("replacement factory failure");
        var factory = new Factory(mode, expected);
        var executor = new UnorderedThreadPoolEventExecutor(1, factory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failed = new EscapingSubmission(entered, release);
        executor.execute(failed);
        if (!entered.Wait(TimeSpan.FromSeconds(5))) return 40;
        Task<int> queued = executor.SubmitAsync(() => 7);
        Task<int> delayed = executor.ScheduleAsync(() => 8, TimeSpan.FromDays(1));
        release.Set();
        try
        {
            Exception claimed = await Failure(failed.Result);
            Exception queuedFailure = await Failure(queued);
            Exception delayedFailure = await Failure(delayed);
            Exception terminationFailure = await Failure(executor.Termination);
            bool correct = ReferenceEquals(claimed, failed.Error) &&
                ReferenceEquals(queuedFailure, delayedFailure) && ReferenceEquals(queuedFailure, terminationFailure) &&
                (mode is not ("throw" or "logger") || ReferenceEquals(queuedFailure, expected)) &&
                (mode != "started" || queuedFailure is ThreadStateException) &&
                (mode != "logger" || ThrowingLogger.Calls >= 2) &&
                executor.isShutdown() && executor.isTerminated() && executor.WorkerCount == 0 && executor.PendingTaskCount == 0;
            Console.WriteLine($"{mode}: claimed={claimed.GetType().Name}; queued={queuedFailure.GetType().Name}; terminated={executor.isTerminated()}; correct={correct}");
            return correct ? 0 : 42;
        }
        catch (Exception error)
        {
            Console.WriteLine($"{mode}: {error}");
            executor.shutdownNow();
            return 41;
        }
    }

    static async Task<Exception> Failure(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception error) when (error is not TimeoutException) { return error; }
        throw new InvalidOperationException("Expected an observable operation failure.");
    }

    sealed class Factory(string mode, Exception failure) : IThreadFactory
    {
        int count;
        public Thread newThread(IRunnable task)
        {
            if (Interlocked.Increment(ref count) == 1) return new Thread(task.run) { IsBackground = true };
            if (mode is "throw" or "logger") throw failure;
            if (mode == "null") return null;
            var started = new Thread(() => { }) { IsBackground = true };
            started.Start();
            started.Join();
            return started;
        }
    }

    sealed class LoggingFactory : IInternalLoggerFactory
    {
        public IInternalLogger newInstance(string name) => DispatchProxy.Create<IInternalLogger, ThrowingLogger>();
    }

    sealed class EscapingSubmission(ManualResetEventSlim entered, ManualResetEventSlim release) : INativeSubmission
    {
        readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception Error { get; } = new InvalidOperationException("escaped invocation failure");
        internal Task Result => completion.Task;
        public bool IsCanceled => Result.IsCanceled;
        public void CancelForShutdown() => completion.TrySetCanceled();
        public void Reject(Exception error) => completion.TrySetException(error);
        public void run() { entered.Set(); release.Wait(); throw Error; }
    }
}

public class ThrowingLogger : DispatchProxy
{
    public static int Calls;
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "warn") { Interlocked.Increment(ref Calls); throw new InvalidOperationException("logging provider failed"); }
        return method.ReturnType == typeof(bool) ? false : method.ReturnType == typeof(string) ? "probe" : null;
    }
}
'@ | Set-Content (Join-Path $probeRoot 'Program.cs') -Encoding utf8

& dotnet build (Join-Path $probeRoot 'Probe.csproj') -c $Configuration --artifacts-path (Join-Path $probeRoot 'build') --verbosity quiet *> (Join-Path $probeRoot 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $probeRoot 'build.log') -Tail 12; throw 'Probe build failed.' }
if ($BuildOnly) { return }
$results = foreach ($mode in @('throw', 'null', 'started', 'logger')) {
    $log = Join-Path $probeRoot "$mode.log"
    & dotnet (Join-Path $probeRoot "build/bin/Probe/$($Configuration.ToLowerInvariant())/Netty.NET.Common.Tests.dll") $mode *> $log
    [pscustomobject]@{ mode = $mode; exitCode = $LASTEXITCODE; log = $log }
}
$results | ConvertTo-Json | Set-Content (Join-Path $probeRoot 'results.json') -Encoding utf8
$results | Format-Table -AutoSize
if (@($results | Where-Object exitCode -ne 0).Count) { throw 'Isolated worker failure probes failed; see retained logs.' }
