using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedWorkerFailureContractTest
{
    private sealed class Factory(Func<IRunnable, Thread> create) : IThreadFactory
    {
        public Thread newThread(IRunnable task) => create(task);
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
        public void run() { entered.Set(); release.Wait(); throw Error; }
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
            if (Interlocked.Increment(ref creations) == 1) return new Thread(task.run) { IsBackground = true };
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
        executor.execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> queued = executor.SubmitAsync(() => 7);
            Task<int> deadline = executor.ScheduleAsync(() => 8, TimeSpan.FromDays(1));
            Task repeating = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
            IEventExecutor child = new NonStickyEventExecutorGroup(executor).next();
            Task<int> forwarded = child.SubmitAsync(() => 9);
            using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
            using var notification = observation.Register(_ => Assert.Fail("Rejected notification ran"));
            executor.execute(Runnables.Create(() => Interlocked.Increment(ref rawCalls)));
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
            Assert.True(executor.isShutdown());
            Assert.True(executor.isTerminated());
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(0, executor.WorkerCount);
            Assert.Equal(0, rawCalls);
            Assert.Equal(2, creations);
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.Same(executor.Termination, executor.ShutdownGracefullyAsync());
            Assert.Empty(executor.shutdownNow());
            await Assert.ThrowsAsync<RejectedExecutionException>(() => executor.SubmitAsync(() => 10));
        }
        finally
        {
            release.Set();
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ReplacementFailureWaitsForSurvivingInvocationWithoutInterruptingIt()
    {
        int creations = 0;
        var expected = new InvalidOperationException("replacement creation failed");
        var factory = new Factory(task => Interlocked.Increment(ref creations) <= 2
            ? new Thread(task.run) { IsBackground = true } : throw expected);
        var executor = new UnorderedThreadPoolEventExecutor(2, factory);
        using var escapedEntered = new ManualResetEventSlim();
        using var escapedRelease = new ManualResetEventSlim();
        using var survivorEntered = new ManualResetEventSlim();
        using var survivorRelease = new ManualResetEventSlim();
        bool interrupted = false;
        var escaping = new EscapingSubmission(escapedEntered, escapedRelease);
        executor.execute(escaping);
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
            Assert.False(executor.awaitTermination(TimeSpan.FromMilliseconds(1)));
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
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task SuccessfulReplacementKeepsTheInvocationFailureAndProcessesWaitingWork()
    {
        int creations = 0;
        var executor = new UnorderedThreadPoolEventExecutor(1,
            new Factory(task => { Interlocked.Increment(ref creations); return new Thread(task.run) { IsBackground = true }; }));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> queued = executor.SubmitAsync(() => 7);
            release.Set();
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(
                () => escaping.Result.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Equal(7, await queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, creations);
            Assert.False(executor.isShutdown());
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task ClosedAndDrainedPoolDoesNotCreateAnUnneededReplacement()
    {
        int creations = 0;
        var executor = new UnorderedThreadPoolEventExecutor(1,
            new Factory(task => Interlocked.Increment(ref creations) == 1
                ? new Thread(task.run) { IsBackground = true } : throw new InvalidOperationException("Unneeded worker")));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.execute(escaping);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            executor.shutdown();
            release.Set();
            Assert.Same(escaping.Error, await Assert.ThrowsAsync<InvalidOperationException>(
                () => escaping.Result.WaitAsync(TimeSpan.FromSeconds(5))));
            await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, creations);
        }
        finally
        {
            release.Set();
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
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
            if (Interlocked.Increment(ref creations) == 1) return new Thread(task.run) { IsBackground = true };
            executor.shutdownNow();
            pendingInsideFactory = !executor.Termination.IsCompleted;
            if (throwAfterClosing) throw expected;
            return null;
        });
        executor = new UnorderedThreadPoolEventExecutor(1, factory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var escaping = new EscapingSubmission(entered, release);
        executor.execute(escaping);
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
            Assert.True(executor.isTerminated());
        }
        finally
        {
            release.Set();
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }
}
