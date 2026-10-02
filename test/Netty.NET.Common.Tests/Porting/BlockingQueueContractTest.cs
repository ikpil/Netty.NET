using System;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Netty.NET.Common.Collections;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class BlockingQueueContractTest
{
    [Fact]
    public void PeekRetainsTheHeadAndRemovalPreservesRemainingFifoOrder()
    {
        var queue = new LinkedBlockingQueue<string>(3);
        Assert.True(queue.tryEnqueue("first"));
        Assert.True(queue.tryEnqueue("middle"));
        Assert.True(queue.tryEnqueue("last"));
        Assert.True(queue.tryPeek(out var head));
        Assert.Equal("first", head);
        Assert.Equal(3, queue.Count);
        Assert.True(queue.tryRemove("middle"));
        Assert.False(queue.tryRemove("missing"));
        Assert.Equal("first", queue.take());
        Assert.Equal("last", queue.take());
        Assert.True(queue.isEmpty());
    }

    [Fact]
    public void RemovingAnElementFreesCapacityAndRemovesOnlyItsFirstOccurrence()
    {
        var queue = new LinkedBlockingQueue<int>(2);
        Assert.True(queue.tryEnqueue(1));
        Assert.True(queue.tryEnqueue(1));
        Assert.False(queue.tryEnqueue(2));
        Assert.True(queue.tryRemove(1));
        Assert.True(queue.tryEnqueue(2));
        Assert.Equal(1, queue.take());
        Assert.Equal(2, queue.take());
    }

    [Fact]
    public void VeryLargeTimedWaitCanBeInterruptedWithoutDurationOverflow()
    {
        var queue = new LinkedBlockingQueue<object>(1);
        Exception failure = null;
        var waiter = new Thread(() =>
        {
            try { queue.tryTake(out _, TimeSpan.MaxValue); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        waiter.Start();
        if (SpinWait.SpinUntil(() => !waiter.IsAlive || (waiter.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
            TimeSpan.FromSeconds(5)) && waiter.IsAlive) waiter.Interrupt();
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<ThreadInterruptedException>(failure);
    }

    [Fact]
    public void ConcurrentProducersRetainTheirOrderAndEveryItemIsConsumedOnce()
    {
        const int producers = 4, items = 128;
        var queue = new LinkedBlockingQueue<(int producer, int sequence)>(producers * items);
        var consumer = Task.Run(() =>
        {
            var next = new int[producers];
            for (int i = 0; i < producers * items; i++)
            {
                var item = queue.take();
                Assert.Equal(next[item.producer]++, item.sequence);
            }
            Assert.All(next, count => Assert.Equal(items, count));
        });
        Parallel.For(0, producers, producer =>
        {
            for (int sequence = 0; sequence < items; sequence++) Assert.True(queue.tryEnqueue((producer, sequence)));
        });
        consumer.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Assert.True(queue.isEmpty());
    }

    [Fact]
    public void InterruptDuringNonblockingLockContentionIsRestoredAfterAcquisition()
    {
        var queue = new LinkedBlockingQueue<string>(1);
        Assert.True(queue.tryEnqueue("head"));
        object gate = typeof(LinkedBlockingQueue<string>).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(queue);
        string head = null;
        Exception failure = null;
        bool restored = false;
        var waiter = new Thread(() =>
        {
            try
            {
                Assert.True(queue.tryPeek(out head));
                try { Thread.Sleep(0); }
                catch (ThreadInterruptedException) { restored = true; }
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        lock (gate)
        {
            waiter.Start();
            Assert.True(SpinWait.SpinUntil(() => (waiter.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));
            waiter.Interrupt();
        }
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Equal("head", head);
        Assert.True(restored);
        Assert.Equal(1, queue.Count);
    }
}
