using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class MpscIntQueueContractTest
{
    private sealed class DefaultReductionQueue : IMpscIntQueue
    {
        public bool Offer(int value) => throw new NotSupportedException();
        public int Poll() => throw new NotSupportedException();
        public int Drain(int limit, Action<int> consumer) => throw new NotSupportedException();
        public int Fill(int limit, Func<int> supplier) => throw new NotSupportedException();
        public bool IsEmpty() => throw new NotSupportedException();
        public int Size() => throw new NotSupportedException();
    }

    [Fact]
    public void InterfaceDefaultReductionReturnsInitialWithoutUsingOtherOperations()
    {
        IMpscIntQueue queue = new DefaultReductionQueue();
        Assert.Equal(42, queue.WeakPeekReduce(-1, 42, null));
        Assert.Equal(42, queue.WeakPeekReduce(0, 42, (_, _) => throw new InvalidOperationException()));
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 1, -1)]
    [InlineData(1, 1, int.MinValue)]
    [InlineData(7, 8, -7)]
    [InlineData(17, 32, int.MaxValue)]
    public void CapacityRoundingAndSlotReusePreserveFifo(int requested, int capacity, int empty)
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(requested, empty);
        Assert.Equal(empty, queue.Poll());
        for (int cycle = 0; cycle < 100; cycle++)
        {
            for (int i = 1; i <= capacity; i++)
            {
                Assert.True(queue.Offer(i));
                Assert.Equal(i, queue.Size());
            }
            Assert.False(queue.Offer(42));
            Assert.False(queue.IsEmpty());
            for (int i = 1; i <= capacity; i++)
                Assert.Equal(i, queue.Poll());
            Assert.True(queue.IsEmpty());
            Assert.Equal(0, queue.Size());
        }
    }

    [Fact]
    public void FillDoesNotCallSupplierBeyondReservedCapacityAndDrainUsesFifo()
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(3, -1);
        int next = 0;
        Assert.Equal(3, queue.Fill(3, () => ++next));
        Assert.Equal(1, queue.Fill(10, () => ++next));
        Assert.Equal(0, queue.Fill(10, () => ++next));
        Assert.Equal(4, next);
        var drained = new List<int>();
        Assert.Equal(2, queue.Drain(2, drained.Add));
        Assert.Equal(new[] { 1, 2 }, drained);
        Assert.Equal(2, queue.Fill(10, () => ++next));
        Assert.Equal(4, queue.Drain(10, drained.Add));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, drained);
        Assert.Equal(0, queue.Drain(10, drained.Add));
    }

    [Fact]
    public void CallbackValidationPrecedesZeroLimitAndLeavesQueueUnchanged()
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(4, -1);
        Assert.Throws<ArgumentNullException>(() => queue.Fill(0, null));
        Assert.Throws<ArgumentNullException>(() => queue.Drain(0, null));
        Assert.Throws<ArgumentNullException>(() => queue.WeakPeekReduce(0, 42, null));
        Assert.Throws<ArgumentException>(() => queue.Fill(-1, () => 1));
        Assert.Throws<ArgumentException>(() => queue.Drain(-1, _ => { }));
        Assert.Throws<ArgumentException>(() => queue.WeakPeekReduce(-1, 42, (a, b) => a + b));
        Assert.Throws<ArgumentException>(() => queue.Offer(-1));
        Assert.Equal(0, queue.Fill(0, () => throw new InvalidOperationException()));
        Assert.Equal(0, queue.Drain(0, _ => throw new InvalidOperationException()));
        Assert.Equal(0, queue.WeakPeekReduce(0, 42, (_, _) => throw new InvalidOperationException()));
        Assert.True(queue.IsEmpty());
        Assert.True(queue.Offer(7));
        Assert.Equal(7, queue.Poll());
    }

    [Fact]
    public void ReductionPreservesElementsAndPinnedZeroAndWraparoundBehavior()
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(4, -1);
        Assert.Equal(42, queue.WeakPeekReduce(5, 42, (a, b) => a + b));
        int next = 0;
        queue.Fill(4, () => ++next);
        Assert.Equal(123, queue.WeakPeekReduce(3, 100, (a, b) => a * 10 + b) % 1000);
        Assert.Equal(110, queue.WeakPeekReduce(4, 100, (a, b) => a + b));
        // The pinned implementation traverses the ring again if a full queue's
        // reduction limit exceeds capacity, rather than capping at producerIndex.
        Assert.Equal(23, queue.WeakPeekReduce(10, 0, (a, b) => a + b));
        Assert.Equal(4, queue.Size());
        var error = new InvalidOperationException();
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            queue.WeakPeekReduce(1, 0, (_, _) => throw error)));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            queue.Drain(4, _ => throw error)));
        Assert.Equal(3, queue.Size());
        Assert.Equal(2, queue.Poll());
    }

    [Fact]
    public void ReservedUnpublishedHeadStopsDrainAndPeekUntilProducerPublishes()
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(4, -1);
        using var supplierEntered = new ManualResetEventSlim();
        using var releaseSupplier = new ManualResetEventSlim();
        int next = 0;
        Task<int> producer = Task.Run(() => queue.Fill(2, () =>
        {
            supplierEntered.Set();
            if (!releaseSupplier.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException();
            return ++next;
        }));
        try
        {
            Assert.True(supplierEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(queue.Offer(3));
            Assert.Equal(3, queue.Size());
            Assert.False(queue.IsEmpty());
            Assert.Equal(0, queue.Drain(4, _ => throw new InvalidOperationException()));
            Assert.Equal(42, queue.WeakPeekReduce(4, 42, (_, _) => throw new InvalidOperationException()));
        }
        finally
        {
            releaseSupplier.Set();
        }
        Assert.True(producer.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, producer.Result);
        Assert.Equal(1, queue.Poll());
        Assert.Equal(2, queue.Poll());
        Assert.Equal(3, queue.Poll());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentProducersPreserveEachProducerOrderWithoutLoss(bool bulk)
    {
        const int producers = 4;
        const int count = 4000;
        IMpscIntQueue queue = IMpscIntQueue.Create(64, -1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var workers = new Task[producers + 1];
        for (int producer = 0; producer < producers; producer++)
        {
            int id = producer;
            workers[producer] = Task.Run(() =>
            {
                int next = 0;
                while (next < count)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (bulk)
                        queue.Fill(Math.Min(7, count - next), () => id * count + next++);
                    else if (queue.Offer(id * count + next))
                        next++;
                    Thread.Yield();
                }
            });
        }
        workers[producers] = Task.Run(() =>
        {
            int[] next = new int[producers];
            for (int consumed = 0; consumed < producers * count;)
            {
                deadline.Token.ThrowIfCancellationRequested();
                int value = queue.Poll();
                if (value == -1)
                {
                    Thread.Yield();
                    continue;
                }
                int id = value / count;
                Assert.InRange(id, 0, producers - 1);
                Assert.Equal(id * count + next[id]++, value);
                consumed++;
            }
            Assert.All(next, n => Assert.Equal(count, n));
        });
        Assert.True(Task.WaitAll(workers, TimeSpan.FromSeconds(15)));
        Assert.True(queue.IsEmpty());
        Assert.Equal(0, queue.Size());
    }

    [Fact]
    public void PollWaitsForReservedHeadAndPreservesPendingThreadInterrupt()
    {
        IMpscIntQueue queue = IMpscIntQueue.Create(4, -1);
        using var supplierEntered = new ManualResetEventSlim();
        using var releaseSupplier = new ManualResetEventSlim();
        using var pollEntered = new ManualResetEventSlim();
        using var pollFinished = new ManualResetEventSlim();
        Task<int> producer = Task.Run(() => queue.Fill(1, () =>
        {
            supplierEntered.Set();
            releaseSupplier.Wait();
            return 7;
        }));
        int result = 0;
        Exception failure = null;
        bool interruptPreserved = false;
        var consumer = new Thread(() =>
        {
            try
            {
                pollEntered.Set();
                result = queue.Poll();
                try { Thread.Sleep(0); }
                catch (ThreadInterruptedException) { interruptPreserved = true; }
            }
            catch (Exception error) { failure = error; }
            finally { pollFinished.Set(); }
        }) { IsBackground = true };
        try
        {
            Assert.True(supplierEntered.Wait(TimeSpan.FromSeconds(5)));
            consumer.Start();
            Assert.True(pollEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(pollFinished.Wait(TimeSpan.FromMilliseconds(20)));
            consumer.Interrupt();
        }
        finally
        {
            releaseSupplier.Set();
            Assert.True(producer.Wait(TimeSpan.FromSeconds(5)));
            if ((consumer.ThreadState & ThreadState.Unstarted) == 0)
                Assert.True(consumer.Join(TimeSpan.FromSeconds(5)));
        }
        Assert.Null(failure);
        Assert.Equal(7, result);
        Assert.True(interruptPreserved);
    }
}
