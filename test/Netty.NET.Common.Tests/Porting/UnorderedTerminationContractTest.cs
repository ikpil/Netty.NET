using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedTerminationContractTest
{
    private sealed class Factory(Func<Action, Thread> create) : IThreadFactory
    {
        public Thread NewThread(Action task) => create(task);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownSignalWaitsForEveryAcceptedInvocation(bool immediate)
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var entered = new CountdownEvent(2);
        using var firstRelease = new ManualResetEventSlim();
        using var lastRelease = new ManualResetEventSlim();
        Task Invoke(ManualResetEventSlim release) => executor.SubmitAsync(() =>
        {
            entered.Signal();
            // Immediate shutdown can request interruption, but cannot force user
            // code which deliberately defers cancellation to finish.
            while (!release.IsSet)
            {
                try { release.Wait(); }
                catch (ThreadInterruptedException) { }
            }
        });
        Task first = Invoke(firstRelease), last = Invoke(lastRelease);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task termination = executor.Termination;
            if (immediate) Assert.Same(executor.Termination, executor.StopAsync());
            else Assert.Same(termination, executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
            Assert.True(executor.IsShutdown());
            Assert.False(termination.IsCompleted);
            firstRelease.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(termination.IsCompleted);
            Assert.False(last.IsCompleted);
            using var observer = new CancellationTokenSource();
            observer.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => termination.WaitAsync(observer.Token));
            Assert.False(termination.IsCompleted);
            lastRelease.Set();
            await last.WaitAsync(TimeSpan.FromSeconds(5));
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(executor.IsTerminated());
            Assert.Equal(0, executor.WorkerCount);
            Assert.Same(termination, executor.ShutdownGracefullyAsync());
        }
        finally
        {
            firstRelease.Set();
            lastRelease.Set();
            _ = executor.StopAsync();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task RemovingTheLastNativeDeadlineCompletesAWorkerlessShutdown()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        using var cancellation = new CancellationTokenSource();
        Task work = executor.ScheduleAsync(() => Assert.Fail("Canceled deadline ran"),
            TimeSpan.FromDays(1), cancellation.Token);
        try
        {
            Assert.Equal(0, executor.WorkerCount);
            Assert.Equal(1, executor.PendingTaskCount);
            Task termination = executor.ShutdownGracefullyAsync();
            Assert.False(termination.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(executor.IsTerminated());
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public async Task AReentrantShutdownWaitsForTheThreadCreationReservationToBeReleased()
    {
        UnorderedThreadPoolEventExecutor executor = null;
        bool completedInsideFactory = false;
        executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ =>
        {
            Assert.Same(executor.Termination, executor.StopAsync());
            completedInsideFactory = executor.Termination.IsCompleted;
            return null;
        }));
        Task work = executor.SubmitAsync(() => Assert.Fail("Removed invocation ran"));
        Assert.False(completedInsideFactory);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(executor.IsTerminated());
    }

    [Fact]
    public async Task ImmediateShutdownSettlesAWorkerlessDeadlineWithoutCreatingAWorker()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        Task work = executor.ScheduleAsync(() => Assert.Fail("Removed deadline ran"), TimeSpan.FromDays(1));
        try
        {
            Task termination = executor.ShutdownGracefullyAsync();
            Assert.False(termination.IsCompleted);
            _ = executor.StopAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(executor.IsTerminated());
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public async Task OwnerCancellationSettlesWorkerlessWorkBeforeCompletingShutdown()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        using var cancellation = new CancellationTokenSource();
        Task submitted = executor.SubmitAsync(() => Assert.Fail("Canceled invocation ran"), cancellation.Token);
        Task scheduled = executor.ScheduleAsync(() => Assert.Fail("Canceled deadline ran"), TimeSpan.FromDays(1), cancellation.Token);
        try
        {
            Task termination = executor.ShutdownGracefullyAsync();
            Assert.False(termination.IsCompleted);
            cancellation.Cancel();
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(submitted.IsCanceled);
            Assert.True(scheduled.IsCanceled);
            Assert.True(executor.IsTerminated());
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public void ACompletedLifecycleRejectsNewWorkAndRepeatedStopCannotRestartIt()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        var scheduled = executor.ScheduleAsync(() => { }, TimeSpan.FromDays(1));
        Assert.Same(executor.Termination, executor.StopAsync());
        Assert.True(executor.Termination.IsCompletedSuccessfully);
        Assert.Throws<RejectedExecutionException>(() => executor.Execute(Runnables.Empty));
        Assert.Same(executor.Termination, executor.StopAsync());
        Assert.True(executor.IsTerminated());
        Assert.Equal(0, executor.PendingTaskCount);
        Assert.True(scheduled.IsCanceled);
    }
}
