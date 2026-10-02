using System;
using System.Threading;
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
            Assert.True(executor.getQueue().isEmpty());
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
    public void AnotherPoolCannotAdoptAReservationWithDifferentCancellationOwnership()
    {
        var owner = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        var other = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        using var cancellation = new CancellationTokenSource();
        var result = owner.ScheduleAsync(() => 7, TimeSpan.FromDays(1), cancellation.Token);
        try
        {
            Assert.True(owner.getQueue().tryPeek(out IRunnable reservation));
            Assert.True(owner.remove(reservation));
            Assert.Throws<ArgumentException>(() => other.getQueue().tryEnqueue(reservation));
            Assert.True(other.getQueue().isEmpty());
            Assert.False(result.IsCompleted);
            Assert.True(owner.getQueue().tryEnqueue(reservation));
            cancellation.Cancel();
            Assert.True(result.IsCanceled);
            Assert.True(owner.getQueue().isEmpty());
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
    public void ClearedRawHandleCannotExecuteAndAClaimedHandleCannotExecuteTwice()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        int calls = 0;
        try
        {
            executor.execute(Runnables.Create(() => ++calls));
            Assert.True(executor.getQueue().tryPeek(out IRunnable discarded));
            Assert.IsNotAssignableFrom<System.Threading.Tasks.Task>(discarded);
            executor.getQueue().clear();
            discarded.run();
            Assert.Equal(0, calls);
            executor.execute(Runnables.Create(() => ++calls));
            Assert.True(executor.getQueue().tryDequeue(out IRunnable claimed));
            claimed.run();
            claimed.run();
            Assert.Equal(1, calls);
            Assert.True(executor.getQueue().isEmpty());
        }
        finally
        {
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }
}
