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
        public bool offer(int value) => throw new NotSupportedException();
        public int poll() => throw new NotSupportedException();
        public int drain(int limit, Action<int> consumer) => throw new NotSupportedException();
        public int fill(int limit, Func<int> supplier) => throw new NotSupportedException();
        public bool isEmpty() => throw new NotSupportedException();
        public int size() => throw new NotSupportedException();
    }

    [Fact]
    public void InterfaceDefaultReductionReturnsInitialWithoutUsingOtherOperations()
    {
        IMpscIntQueue queue = new DefaultReductionQueue();
        Assert.Equal(42, queue.weakPeekReduce(-1, 42, null));
        Assert.Equal(42, queue.weakPeekReduce(0, 42, (_, _) => throw new InvalidOperationException()));
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 1, -1)]
    [InlineData(1, 1, int.MinValue)]
    [InlineData(7, 8, -7)]
    [InlineData(17, 32, int.MaxValue)]
    public void CapacityRoundingAndSlotReusePreserveFifo(int requested, int capacity, int empty)
    {
        IMpscIntQueue queue = IMpscIntQueue.create(requested, empty);
        Assert.Equal(empty, queue.poll());
        for (int cycle = 0; cycle < 100; cycle++)
        {
            for (int i = 1; i <= capacity; i++)
            {
                Assert.True(queue.offer(i));
                Assert.Equal(i, queue.size());
            }
            Assert.False(queue.offer(42));
            Assert.False(queue.isEmpty());
            for (int i = 1; i <= capacity; i++)
                Assert.Equal(i, queue.poll());
            Assert.True(queue.isEmpty());
            Assert.Equal(0, queue.size());
        }
    }

    [Fact]
    public void FillDoesNotCallSupplierBeyondReservedCapacityAndDrainUsesFifo()
    {
        IMpscIntQueue queue = IMpscIntQueue.create(3, -1);
        int next = 0;
        Assert.Equal(3, queue.fill(3, () => ++next));
        Assert.Equal(1, queue.fill(10, () => ++next));
        Assert.Equal(0, queue.fill(10, () => ++next));
        Assert.Equal(4, next);
        var drained = new List<int>();
        Assert.Equal(2, queue.drain(2, drained.Add));
        Assert.Equal(new[] { 1, 2 }, drained);
        Assert.Equal(2, queue.fill(10, () => ++next));
        Assert.Equal(4, queue.drain(10, drained.Add));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, drained);
        Assert.Equal(0, queue.drain(10, drained.Add));
    }

    [Fact]
    public void CallbackValidationPrecedesZeroLimitAndLeavesQueueUnchanged()
    {
        IMpscIntQueue queue = IMpscIntQueue.create(4, -1);
        Assert.Throws<ArgumentNullException>(() => queue.fill(0, null));
        Assert.Throws<ArgumentNullException>(() => queue.drain(0, null));
        Assert.Throws<ArgumentNullException>(() => queue.weakPeekReduce(0, 42, null));
        Assert.Throws<ArgumentException>(() => queue.fill(-1, () => 1));
        Assert.Throws<ArgumentException>(() => queue.drain(-1, _ => { }));
        Assert.Throws<ArgumentException>(() => queue.weakPeekReduce(-1, 42, (a, b) => a + b));
        Assert.Throws<ArgumentException>(() => queue.offer(-1));
        Assert.Equal(0, queue.fill(0, () => throw new InvalidOperationException()));
        Assert.Equal(0, queue.drain(0, _ => throw new InvalidOperationException()));
        Assert.Equal(0, queue.weakPeekReduce(0, 42, (_, _) => throw new InvalidOperationException()));
        Assert.True(queue.isEmpty());
        Assert.True(queue.offer(7));
        Assert.Equal(7, queue.poll());
    }

    [Fact]
    public void ReductionPreservesElementsAndPinnedZeroAndWraparoundBehavior()
    {
        IMpscIntQueue queue = IMpscIntQueue.create(4, -1);
        Assert.Equal(42, queue.weakPeekReduce(5, 42, (a, b) => a + b));
        int next = 0;
        queue.fill(4, () => ++next);
        Assert.Equal(123, queue.weakPeekReduce(3, 100, (a, b) => a * 10 + b) % 1000);
        Assert.Equal(110, queue.weakPeekReduce(4, 100, (a, b) => a + b));
        // The pinned implementation traverses the ring again if a full queue's
        // reduction limit exceeds capacity, rather than capping at producerIndex.
        Assert.Equal(23, queue.weakPeekReduce(10, 0, (a, b) => a + b));
        Assert.Equal(4, queue.size());
        var error = new InvalidOperationException();
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            queue.weakPeekReduce(1, 0, (_, _) => throw error)));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            queue.drain(4, _ => throw error)));
        Assert.Equal(3, queue.size());
        Assert.Equal(2, queue.poll());
    }

    [Fact]
    public void ReservedUnpublishedHeadStopsDrainAndPeekUntilProducerPublishes()
    {
        IMpscIntQueue queue = IMpscIntQueue.create(4, -1);
        using var supplierEntered = new ManualResetEventSlim();
        using var releaseSupplier = new ManualResetEventSlim();
        int next = 0;
        Task<int> producer = Task.Run(() => queue.fill(2, () =>
        {
            supplierEntered.Set();
            if (!releaseSupplier.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException();
            return ++next;
        }));
        try
        {
            Assert.True(supplierEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(queue.offer(3));
            Assert.Equal(3, queue.size());
            Assert.False(queue.isEmpty());
            Assert.Equal(0, queue.drain(4, _ => throw new InvalidOperationException()));
            Assert.Equal(42, queue.weakPeekReduce(4, 42, (_, _) => throw new InvalidOperationException()));
        }
        finally
        {
            releaseSupplier.Set();
        }
        Assert.True(producer.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, producer.Result);
        Assert.Equal(1, queue.poll());
        Assert.Equal(2, queue.poll());
        Assert.Equal(3, queue.poll());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentProducersPreserveEachProducerOrderWithoutLoss(bool bulk)
    {
        const int producers = 4;
        const int count = 4000;
        IMpscIntQueue queue = IMpscIntQueue.create(64, -1);
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
                        queue.fill(Math.Min(7, count - next), () => id * count + next++);
                    else if (queue.offer(id * count + next))
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
                int value = queue.poll();
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
        Assert.True(queue.isEmpty());
        Assert.Equal(0, queue.size());
    }

    [Fact]
    public void PollWaitsForReservedHeadAndPreservesPendingThreadInterrupt()
    {
        IMpscIntQueue queue = IMpscIntQueue.create(4, -1);
        using var supplierEntered = new ManualResetEventSlim();
        using var releaseSupplier = new ManualResetEventSlim();
        using var pollEntered = new ManualResetEventSlim();
        using var pollFinished = new ManualResetEventSlim();
        Task<int> producer = Task.Run(() => queue.fill(1, () =>
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
                result = queue.poll();
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
