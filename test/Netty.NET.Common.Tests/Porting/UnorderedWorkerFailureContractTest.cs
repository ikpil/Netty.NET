using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedWorkerFailureContractTest
{
    private sealed class Factory(Func<Action, Thread> create) : IThreadFactory
    {
        public Thread NewThread(Action task) => create(task);
    }

    // Simulate a native backend defect escaping the usual producer boundary.
    // User delegates normally have their failures captured by their own TCS.
    private sealed class EscapingSubmission(ManualResetEventSlim entered, ManualResetEventSlim release) : INativeSubmission
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Exception Error = new InvalidOperationException("escaped invocation failure");
        internal Task Result => _completion.Task;
        public bool IsCanceled => Result.IsCanceled;
        public void CancelForShutdown() => _completion.TrySetCanceled();
        public void Reject(Exception error) => _completion.TrySetException(error);
        public void Run() { entered.Set(); release.Wait(); throw Error; }
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("null")]
    [InlineData("started")]
    public async Task ReplacementFailureFaultsOwnedWaitingResultsAndTermination(string mode)
    {
        var expected = new InvalidOperationException("replacement creation failed");
        int creations = 0, rawCalls = 0;
        var factory = new Factory(task =>
        {
            if (Interlocked.Increment(ref creations) == 1) return new Thread(task.Invoke) { IsBackground = true };
            if (mode == "throw") throw expected;
            if (mode == "null") return null;
            var started = new Thread(() => { }) { IsBackground = true };
            started.Start();
            started.Join();
            return started;
        });
        var executor = new UnorderedThreadPoolEventExecutor(1, factory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.Execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> queued = executor.SubmitAsync(() => 7);
            Task<int> deadline = executor.ScheduleAsync(() => 8, TimeSpan.FromDays(1));
            Task repeating = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
            IEventExecutor child = new NonStickyEventExecutorGroup(executor).Next();
            Task<int> forwarded = child.SubmitAsync(() => 9);
            using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
            using var notification = observation.Register(_ => Assert.Fail("Rejected notification ran"));
            executor.Execute(() => Interlocked.Increment(ref rawCalls));
            release.Set();

            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(
                () => escaping.Result.WaitAsync(TimeSpan.FromSeconds(5))));
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsNotType<TimeoutException>(failure);
            if (mode == "throw") Assert.Same(expected, failure);
            else if (mode == "started") Assert.IsType<ThreadStateException>(failure);
            else Assert.IsType<InvalidOperationException>(failure);
            foreach (Task result in new Task[] { deadline, repeating, forwarded, notification.NotificationCompleted, executor.Termination })
                Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => result.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.True(executor.IsShutdown());
            Assert.True(executor.IsTerminated());
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(0, executor.WorkerCount);
            Assert.Equal(0, rawCalls);
            Assert.Equal(2, creations);
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
            Assert.Same(executor.Termination, executor.ShutdownGracefullyAsync());
            Assert.Same(executor.Termination, executor.StopAsync());
            await Assert.ThrowsAsync<RejectedExecutionException>(() => executor.SubmitAsync(() => 10));
        }
        finally
        {
            release.Set();
            _ = executor.StopAsync();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ReplacementFailureWaitsForSurvivingInvocationWithoutInterruptingIt()
    {
        int creations = 0;
        var expected = new InvalidOperationException("replacement creation failed");
        var factory = new Factory(task => Interlocked.Increment(ref creations) <= 2
            ? new Thread(task.Invoke) { IsBackground = true } : throw expected);
        var executor = new UnorderedThreadPoolEventExecutor(2, factory);
        using var escapedEntered = new ManualResetEventSlim();
        using var escapedRelease = new ManualResetEventSlim();
        using var survivorEntered = new ManualResetEventSlim();
        using var survivorRelease = new ManualResetEventSlim();
        bool interrupted = false;
        var escaping = new EscapingSubmission(escapedEntered, escapedRelease);
        executor.Execute(escaping);
        Task<int> survivor = executor.SubmitAsync(() =>
        {
            survivorEntered.Set();
            try { survivorRelease.Wait(); }
            catch (ThreadInterruptedException) { interrupted = true; throw; }
            return 7;
        });
        try
        {
            Assert.True(escapedEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(survivorEntered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> queued = executor.SubmitAsync(() => 8);
            escapedRelease.Set();
            Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(
                () => queued.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.False(executor.Termination.IsCompleted);
            Assert.False(executor.AwaitTermination(TimeSpan.FromMilliseconds(1)));
            Assert.False(survivor.IsCompleted);
            survivorRelease.Set();
            Assert.Equal(7, await survivor.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(interrupted);
            Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.Termination.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(() => escaping.Result));
            Assert.Equal(0, executor.WorkerCount);
        }
        finally
        {
            escapedRelease.Set();
            survivorRelease.Set();
            _ = executor.StopAsync();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task SuccessfulReplacementKeepsTheInvocationFailureAndProcessesWaitingWork()
    {
        int creations = 0;
        var executor = new UnorderedThreadPoolEventExecutor(1,
            new Factory(task => { Interlocked.Increment(ref creations); return new Thread(task.Invoke) { IsBackground = true }; }));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.Execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> queued = executor.SubmitAsync(() => 7);
            release.Set();
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(
                () => escaping.Result.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Equal(7, await queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, creations);
            Assert.False(executor.IsShutdown());
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            _ = executor.StopAsync();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ClosedAndDrainedPoolDoesNotCreateAnUnneededReplacement()
    {
        int creations = 0;
        var executor = new UnorderedThreadPoolEventExecutor(1,
            new Factory(task => Interlocked.Increment(ref creations) == 1
                ? new Thread(task.Invoke) { IsBackground = true } : throw new InvalidOperationException("Unneeded worker")));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.Execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            executor.Shutdown();
            release.Set();
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(
                () => escaping.Result.WaitAsync(TimeSpan.FromSeconds(5))));
            await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, creations);
        }
        finally
        {
            release.Set();
            _ = executor.StopAsync();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReentrantReplacementShutdownWaitsForTheFactoryOutcome(bool throwAfterClosing)
    {
        int creations = 0;
        bool pendingInsideFactory = false;
        UnorderedThreadPoolEventExecutor executor = null;
        var expected = new InvalidOperationException("failure after reentrant closure");
        var factory = new Factory(task =>
        {
            if (Interlocked.Increment(ref creations) == 1) return new Thread(task.Invoke) { IsBackground = true };
            _ = executor.StopAsync();
            pendingInsideFactory = !executor.Termination.IsCompleted;
            if (throwAfterClosing) throw expected;
            return null;
        });
        executor = new UnorderedThreadPoolEventExecutor(1, factory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.Execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task queued = executor.SubmitAsync(() => { });
            release.Set();
            if (throwAfterClosing)
                Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(
                    () => executor.Termination.WaitAsync(TimeSpan.FromSeconds(5))));
            else await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(pendingInsideFactory);
            Assert.Equal(2, creations);
            Assert.True(queued.IsCanceled);
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(() => escaping.Result));
            Assert.True(executor.IsTerminated());
        }
        finally
        {
            release.Set();
            _ = executor.StopAsync();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ReentrantStopKeepsBothBackendAndCancellationCallbackFailures()
    {
        int creations = 0;
        UnorderedThreadPoolEventExecutor executor = null;
        var backend = new InvalidOperationException("replacement failed");
        var callback = new ArgumentException("stop callback failed");
        var factory = new Factory(task =>
        {
            if (Interlocked.Increment(ref creations) == 1) return new Thread(task.Invoke) { IsBackground = true };
            executor.StopAsync();
            throw backend;
        });
        executor = new UnorderedThreadPoolEventExecutor(1, factory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var registration = executor.StopToken.UnsafeRegister(_ => throw callback, null);
        var escaping = new EscapingSubmission(entered, release);
        executor.Execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            release.Set();
            Assert.Same(backend, await Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.Termination.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Contains(backend, executor.Termination.Exception.Flatten().InnerExceptions);
            Assert.Contains(callback, executor.Termination.Exception.Flatten().InnerExceptions);
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(() => escaping.Result));
            Assert.True(executor.IsTerminated());
        }
        finally { release.Set(); _ = executor.StopAsync(); Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5))); }
    }
}
