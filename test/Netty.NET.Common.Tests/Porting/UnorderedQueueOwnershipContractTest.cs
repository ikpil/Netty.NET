using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedQueueOwnershipContractTest
{
    private sealed class Factory(Func<IRunnable, Thread> create) : IThreadFactory
    {
        public Thread newThread(IRunnable task) => create(task);
    }

    [Fact]
    public void FailedRawAdmissionCannotRunItsCallbackWhenTheFactoryRecovers()
    {
        int attempts = 0, calls = 0;
        var expected = new InvalidOperationException("raw factory failed");
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(task =>
        {
            if (++attempts == 1) throw expected;
            return new Thread(task.run) { IsBackground = true };
        }));
        try
        {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                executor.execute(Runnables.Create(() => ++calls))));
            Assert.Equal(0, executor.getPoolSize());
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(7, executor.ScheduleAsync(() => 7, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(2, attempts);
            Assert.Equal(0, calls);
        }
        finally
        {
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void NativeCancellationWithdrawsOnlyItsOwnersReservation()
    {
        var owner = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        var other = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        using var cancellation = new CancellationTokenSource();
        var result = owner.ScheduleAsync(() => 7, TimeSpan.FromDays(1), cancellation.Token);
        var otherResult = other.ScheduleAsync(() => 8, TimeSpan.FromDays(1));
        try
        {
            Assert.Equal(1, owner.PendingTaskCount);
            Assert.Equal(1, other.PendingTaskCount);
            Assert.False(result.IsCompleted);
            cancellation.Cancel();
            Assert.True(result.IsCanceled);
            Assert.Equal(0, owner.PendingTaskCount);
            Assert.False(otherResult.IsCompleted);
            Assert.Equal(1, other.PendingTaskCount);
        }
        finally
        {
            owner.shutdownNow();
            other.shutdownNow();
            Assert.True(owner.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.True(other.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void ShutdownNowRawHandlesCannotExecuteRemovedCallbacks()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        int calls = 0;
        try
        {
            executor.execute(Runnables.Create(() => ++calls));
            executor.execute(Runnables.Create(() => ++calls));
            var removed = executor.shutdownNow();
            Assert.Equal(2, removed.Count);
            foreach (IRunnable handle in removed)
            {
                Assert.IsNotAssignableFrom<System.Threading.Tasks.Task>(handle);
                handle.run();
                handle.run();
            }
            Assert.Equal(0, calls);
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task CancellationRacingAdmissionAndClaimLeavesNoPendingReservation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            for (int attempt = 0; attempt < 256; ++attempt)
            {
                using var cancellation = new CancellationTokenSource();
                int calls = 0;
                Task<int> operation = null;
                Task admission = Task.Run(() =>
                {
                    operation = executor.SubmitAsync(() => { Interlocked.Increment(ref calls); return 17; }, cancellation.Token);
                });
                await Task.WhenAll(admission, Task.Run(cancellation.Cancel)).WaitAsync(TimeSpan.FromSeconds(5));
                try { Assert.Equal(17, await operation.WaitAsync(TimeSpan.FromSeconds(5))); }
                catch (OperationCanceledException error)
                {
                    Assert.Equal(cancellation.Token, error.CancellationToken);
                    Assert.True(operation.IsCanceled);
                }
                Assert.Equal(operation.IsCanceled ? 0 : 1, calls);
                Assert.Equal(0, executor.PendingTaskCount);
            }
        }
        finally
        {
            executor.shutdownNow();
            await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void KeepingTheCanceledTaskAndTokenDoesNotRetainItsQueueOwner()
    {
        using var cancellation = new CancellationTokenSource();
        var retained = CreateCanceledWorkerlessSubmission(cancellation);
        for (int attempt = 0; attempt < 3 && retained.Pool.IsAlive; ++attempt)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.True(retained.Operation.IsCanceled);
        Assert.False(retained.Pool.IsAlive);
        GC.KeepAlive(cancellation);
        GC.KeepAlive(retained.Operation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Pool, Task Operation) CreateCanceledWorkerlessSubmission(CancellationTokenSource owner)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        Task operation = executor.SubmitAsync(() => Assert.Fail("Canceled callback ran"), owner.Token);
        Assert.Equal(1, executor.PendingTaskCount);
        owner.Cancel();
        Assert.Equal(0, executor.PendingTaskCount);
        return (new WeakReference(executor), operation);
    }
}
