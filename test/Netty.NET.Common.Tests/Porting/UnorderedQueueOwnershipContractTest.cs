using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedQueueOwnershipContractTest
{
    private sealed class Factory(Func<Action, Thread> create) : IThreadFactory
    {
        public Thread NewThread(Action task) => create(task);
    }

    [Fact]
    public void FailedRawAdmissionCannotRunItsCallbackWhenTheFactoryRecovers()
    {
        int attempts = 0, calls = 0;
        var expected = new InvalidOperationException("raw factory failed");
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(task =>
        {
            if (++attempts == 1) throw expected;
            return new Thread(task.Invoke) { IsBackground = true };
        }));
        try
        {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                executor.Execute(Runnables.Create(() => ++calls))));
            Assert.Equal(0, executor.WorkerCount);
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(7, executor.ScheduleAsync(() => 7, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(2, attempts);
            Assert.Equal(0, calls);
        }
        finally
        {
            executor.ShutdownNow();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
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
            owner.ShutdownNow();
            other.ShutdownNow();
            Assert.True(owner.AwaitTermination(TimeSpan.FromSeconds(5)));
            Assert.True(other.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void ShutdownNowRawHandlesCannotExecuteRemovedCallbacks()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        int calls = 0;
        try
        {
            executor.Execute(Runnables.Create(() => ++calls));
            executor.Execute(Runnables.Create(() => ++calls));
            var removed = executor.ShutdownNow();
            Assert.Equal(2, removed.Count);
            foreach (Action handle in removed)
            {
                Assert.IsNotAssignableFrom<System.Threading.Tasks.Task>(handle);
                handle();
                handle();
            }
            Assert.Equal(0, calls);
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            executor.ShutdownNow();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
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
            executor.ShutdownNow();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScatteredOwnerCancellationPreservesEveryOtherWaitingResult(bool scheduled)
    {
        const int count = 128;
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        var owners = new CancellationTokenSource[count];
        var operations = new Task[count];
        var canceled = new bool[count];
        try
        {
            for (int i = 0; i < count; ++i)
            {
                owners[i] = new CancellationTokenSource();
                operations[i] = scheduled
                    ? executor.ScheduleAsync(() => Assert.Fail("Workerless schedule ran"), TimeSpan.FromHours(1 + i % 7), owners[i].Token)
                    : executor.SubmitAsync(() => Assert.Fail("Workerless submission ran"), owners[i].Token);
            }
            Assert.Equal(count, executor.PendingTaskCount);
            for (int step = 0; step < count / 2; ++step)
            {
                // A permutation crosses heap positions and equal delay groups;
                // results are checked by original operation identity, not heap layout.
                int index = step * 73 % count;
                owners[index].Cancel();
                owners[index].Cancel();
                canceled[index] = true;
                Assert.Equal(count - step - 1, executor.PendingTaskCount);
                for (int i = 0; i < count; ++i)
                    Assert.Equal(canceled[i], operations[i].IsCompleted);
                OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations[index]);
                Assert.Equal(owners[index].Token, error.CancellationToken);
            }
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(operations, task => Assert.True(task.IsCanceled));
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var owner in owners) owner?.Dispose();
        }
    }

    [Fact]
    public async Task RemovingQueuedReservationsRetainsSingleWorkerDeadlineOrderAndSuccessfulResults()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owners = new CancellationTokenSource[32];
        var operations = new Task<int>[32];
        var executed = new ConcurrentQueue<int>();
        Task blocker = executor.SubmitAsync(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            for (int i = 0; i < operations.Length; ++i)
            {
                int index = i;
                owners[i] = new CancellationTokenSource();
                Func<int> action = () => { executed.Enqueue(index); return index; };
                operations[i] = i % 2 == 0
                    ? executor.ScheduleAsync(action, TimeSpan.Zero, owners[i].Token)
                    : executor.SubmitAsync(action, owners[i].Token);
            }
            int[] canceled = { 0, 31, 13, 7, 24, 16 };
            foreach (int i in canceled) owners[i].Cancel();
            Assert.Equal(operations.Length - canceled.Length, executor.PendingTaskCount);
            release.Set();
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
            int[] remaining = Enumerable.Range(0, operations.Length).Except(canceled).ToArray();
            foreach (int i in remaining) Assert.Equal(i, await operations[i].WaitAsync(TimeSpan.FromSeconds(5)));
            foreach (int i in canceled) Assert.True(operations[i].IsCanceled);
            // All deadlines were admitted sequentially with zero delay. This checks
            // the existing deadline order on one worker, not ordering across a pool.
            Assert.Equal(remaining, executed.ToArray());
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            release.Set();
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var owner in owners) owner?.Dispose();
        }
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
