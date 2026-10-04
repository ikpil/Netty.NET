using System;
using System.Reflection;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Timer globals")]
public class TimerQueuePublicationContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedTimerPublicationKeepsAdmissionAndCancellationObservable(bool cancellation)
    {
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        using var cancellationsProcessed = new ManualResetEventSlim();
        using var timer = new HashedWheelTimer(new DefaultThreadFactory("queue-publication", true), TimeSpan.FromMilliseconds(1));
        int cancelled = 0, runs = 0;
        ITimerTask task = TimerTask.Create(_ => Interlocked.Increment(ref runs), _ =>
        {
            if (Interlocked.Increment(ref cancelled) == 33) cancellationsProcessed.Set();
        });
        var timeouts = new ITimeout[33];
        Exception failure = null;
        bool pendingInterrupt = false, cancelResult = false;
        Thread publisher = null;
        try
        {
            // Keep the worker in an ordinary task so neither handoff queue can drain during publication.
            timer.NewTimeout(TimerTask.Create(_ =>
            {
                entered.Set();
                proceed.Wait(TimeSpan.FromSeconds(15));
            }), TimeSpan.Zero);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            for (int index = 0; index < (cancellation ? 33 : 32); index++)
                timeouts[index] = timer.NewTimeout(task, TimeSpan.FromDays(1));
            if (cancellation)
                for (int index = 0; index < 32; index++) Assert.True(timeouts[index].Cancel());

            object queue = typeof(HashedWheelTimer).GetField(cancellation ? "_cancelledTimeouts" : "_timeouts",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(timer);
            // Force the real net10 segment-growth wait; no production operation is mocked or replaced.
            object gate = queue.GetType().GetField("_crossSegmentLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(queue);
            publisher = new Thread(() =>
            {
                try
                {
                    if (cancellation) cancelResult = timeouts[32].Cancel();
                    else timeouts[32] = timer.NewTimeout(task, TimeSpan.FromDays(1));
                    try { Thread.Sleep(0); }
                    catch (ThreadInterruptedException) { pendingInterrupt = true; }
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            bool waited = false;
            Monitor.Enter(gate);
            try
            {
                publisher.Start();
                waited = SpinWait.SpinUntil(() => (publisher.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5));
                if (waited) publisher.Interrupt();
            }
            finally
            {
                Monitor.Exit(gate);
            }
            Assert.True(publisher.Join(TimeSpan.FromSeconds(5)));
            Assert.True(waited);
            Assert.Null(failure);
            Assert.True(pendingInterrupt);
            Assert.Equal(33, timer.PendingTimeouts());
            if (cancellation) Assert.True(cancelResult);
            else Assert.NotNull(timeouts[32]);
            foreach (ITimeout timeout in timeouts) timeout.Cancel();
            proceed.Set();
            Assert.True(cancellationsProcessed.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(33, cancelled);
            Assert.Equal(0, runs);
            Assert.Equal(0, timer.PendingTimeouts());
            foreach (ITimeout timeout in timeouts)
            {
                Assert.True(timeout.IsCancelled());
                Assert.False(timeout.IsExpired());
                Assert.False(timeout.Cancel());
            }
            Assert.Empty(timer.Stop());
        }
        finally
        {
            proceed.Set();
            if (publisher != null && (publisher.ThreadState & ThreadState.Unstarted) == 0)
                Assert.True(publisher.Join(TimeSpan.FromSeconds(5)));
        }
    }
}
