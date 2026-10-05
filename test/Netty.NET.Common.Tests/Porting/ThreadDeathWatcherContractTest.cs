using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread death watcher globals")]
public class ThreadDeathWatcherContractTest : IDisposable
{
    private sealed class LiveThread : IDisposable
    {
        private readonly ManualResetEventSlim stop = new();
        internal readonly Thread Thread;
        internal LiveThread()
        {
            Thread = new Thread(() => stop.Wait()) { IsBackground = true };
            Thread.Start();
        }
        internal void End() { stop.Set(); Assert.True(Thread.Join(TimeSpan.FromSeconds(5))); }
        public void Dispose() { End(); stop.Dispose(); }
    }
    public void Dispose() => Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));

    [Fact]
    public void ValidationRejectsNullAndNonLiveThreadsWhileUnwatchAcceptsUnstartedThreads()
    {
        Action task = static () => { };
        var thread = new Thread(() => { });
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Watch(null, task));
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Watch(thread, null));
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Unwatch(null, task));
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Unwatch(thread, null));
        Assert.Throws<ArgumentException>(() => ThreadDeathWatcher.Watch(thread, task));
        ThreadDeathWatcher.Unwatch(thread, task);
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentException>(() => ThreadDeathWatcher.Watch(thread, task));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromMilliseconds(-2)));
    }

    [Fact]
    public void UnwatchUsesReferenceIdentityAndRemovesOnlyOneDuplicateRegistration()
    {
        using var owner = new LiveThread();
        using var completed = new CountdownEvent(2);
        int firstCount = 0, secondCount = 0;
        Action first = () => { Interlocked.Increment(ref firstCount); completed.Signal(); };
        Action second = () => { Interlocked.Increment(ref secondCount); completed.Signal(); };
        var copy = (Action)first.Clone();
        Assert.NotSame(first, copy);
        Assert.True(first.Equals(copy));
        ThreadDeathWatcher.Watch(owner.Thread, first);
        ThreadDeathWatcher.Watch(owner.Thread, first);
        ThreadDeathWatcher.Watch(owner.Thread, second);
        ThreadDeathWatcher.Unwatch(owner.Thread, copy);
        ThreadDeathWatcher.Unwatch(owner.Thread, first);
        owner.End();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref firstCount));
        Assert.Equal(1, Volatile.Read(ref secondCount));
    }

    [Fact]
    public void CallbackFailureDoesNotPreventLaterCallbacksAndWorkerUsesNativeFactorySettings()
    {
        using var owner = new LiveThread();
        using var completed = new ManualResetEventSlim();
        int failures = 0;
        ThreadPriority priority = ThreadPriority.Normal;
        bool background = false;
        string name = null;
        ThreadDeathWatcher.Watch(owner.Thread, () => { Interlocked.Increment(ref failures); throw new InvalidOperationException("callback failure"); });
        ThreadDeathWatcher.Watch(owner.Thread, () =>
        {
            priority = Thread.CurrentThread.Priority;
            background = Thread.CurrentThread.IsBackground;
            name = Thread.CurrentThread.Name;
            completed.Set();
        });
        owner.End();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref failures));
        Assert.Equal(ThreadPriority.Lowest, priority);
        Assert.True(background);
        Assert.StartsWith((Environment.GetEnvironmentVariable("io.netty.serviceThreadPrefix") ?? "") + "threadDeathWatcher-", name);
    }

    [Fact]
    public void CallbacksCanWatchAndUnwatchWithoutLosingReentrantEntries()
    {
        using var first = new LiveThread();
        using var second = new LiveThread();
        using var registered = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        int cancelled = 0;
        Action cancel = () => Interlocked.Increment(ref cancelled);
        ThreadDeathWatcher.Watch(first.Thread, () =>
        {
            ThreadDeathWatcher.Watch(second.Thread, cancel);
            ThreadDeathWatcher.Watch(second.Thread, completed.Set);
            ThreadDeathWatcher.Unwatch(second.Thread, cancel);
            registered.Set();
        });
        first.End();
        Assert.True(registered.Wait(TimeSpan.FromSeconds(5)));
        second.End();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref cancelled));
    }

    [Fact]
    public void SingletonDoesNotFlowSubmittingExecutionContextAndRestartsAfterInactivity()
    {
        var ambient = new AsyncLocal<object>();
        object marker = new();
        Thread firstWorker = null, secondWorker = null;
        object captured = marker;
        using var firstDone = new ManualResetEventSlim();
        using var secondDone = new ManualResetEventSlim();
        using (var owner = new LiveThread())
        {
            ambient.Value = marker;
            try
            {
                ThreadDeathWatcher.Watch(owner.Thread, () =>
                {
                    captured = ambient.Value; firstWorker = Thread.CurrentThread; firstDone.Set();
                });
                Assert.Same(marker, ambient.Value);
            }
            finally { ambient.Value = null; }
            owner.End();
            Assert.True(firstDone.Wait(TimeSpan.FromSeconds(5)));
        }
        Assert.Null(captured);
        Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        using (var owner = new LiveThread())
        {
            using (ExecutionContext.SuppressFlow())
            {
                ThreadDeathWatcher.Watch(owner.Thread, () => { secondWorker = Thread.CurrentThread; secondDone.Set(); });
                Assert.True(ExecutionContext.IsFlowSuppressed());
            }
            Assert.False(ExecutionContext.IsFlowSuppressed());
            owner.End();
            Assert.True(secondDone.Wait(TimeSpan.FromSeconds(5)));
        }
        Assert.NotSame(firstWorker, secondWorker);
    }

    [Fact]
    public void WorkerInterruptDoesNotAbandonLiveWatcheesAndHugeAwaitRemainsInterruptible()
    {
        using var first = new LiveThread();
        using var second = new LiveThread();
        using var firstDone = new ManualResetEventSlim();
        using var secondDone = new ManualResetEventSlim();
        Thread worker = null;
        ThreadDeathWatcher.Watch(first.Thread, () => { worker = Thread.CurrentThread; firstDone.Set(); });
        ThreadDeathWatcher.Watch(second.Thread, secondDone.Set);
        first.End();
        Assert.True(firstDone.Wait(TimeSpan.FromSeconds(5)));
        worker.Interrupt();
        Assert.False(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromMilliseconds(20)));
        using var entered = new ManualResetEventSlim();
        Exception failure = null;
        var waiter = new Thread(() =>
        {
            entered.Set();
            try { ThreadDeathWatcher.AwaitInactivity(TimeSpan.MaxValue); }
            catch (Exception caught) { failure = caught; }
        }) { IsBackground = true };
        waiter.Start(); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        waiter.Interrupt(); Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<ThreadInterruptedException>(failure);
        second.End();
        Assert.True(secondDone.Wait(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(9999L)]
    [InlineData(-1L)]
    public void InactivityPollsOrBoundsFiniteWaitsAndRejectsNegativeTicks(long ticks)
    {
        using var owner = new LiveThread();
        ThreadDeathWatcher.Watch(owner.Thread, static () => { });
        if (ticks < 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromTicks(ticks)));
            return;
        }
        Exception failure = null;
        bool stopped = false;
        var waiter = new Thread(() =>
        {
            try { stopped = ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromTicks(ticks)); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        waiter.Start();
        try
        {
            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(stopped);
        }
        finally { owner.End(); }
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfiniteInactivityWaitCompletesOrCanBeInterrupted(bool interrupt)
    {
        using var owner = new LiveThread();
        ThreadDeathWatcher.Watch(owner.Thread, static () => { });
        Exception failure = null;
        bool stopped = false;
        var waiter = new Thread(() =>
        {
            try { stopped = ThreadDeathWatcher.AwaitInactivity(Timeout.InfiniteTimeSpan); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        waiter.Start();
        try
        {
            Assert.False(waiter.Join(TimeSpan.FromMilliseconds(50)));
            if (interrupt)
            {
                waiter.Interrupt();
                Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            owner.End();
            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        }
        if (interrupt) Assert.IsType<ThreadInterruptedException>(failure);
        else { Assert.Null(failure); Assert.True(stopped); }
    }

    [Fact]
    public void MulticastFailureStopsThatInvocationButNotOtherRegistrations()
    {
        using var owner = new LiveThread();
        var order = new List<int>();
        Action task = () => order.Add(1);
        task += () => { order.Add(2); throw new InvalidOperationException("multicast failure"); };
        task += () => order.Add(3);
        var copy = (Action)task.Clone();
        Assert.NotSame(task, copy);
        Assert.True(task.Equals(copy));
        ThreadDeathWatcher.Watch(owner.Thread, task);
        ThreadDeathWatcher.Unwatch(owner.Thread, copy);
        ThreadDeathWatcher.Watch(owner.Thread, () => order.Add(4));
        owner.End();
        Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        // The watcher may skip a still-live owner and then observe its death for
        // another registration in the same pass. Registration order therefore
        // does not order separate callbacks; multicast invocation remains ordered.
        Assert.Equal(new[] { 1, 2 }, order.FindAll(value => value != 4));
        Assert.Equal(1, order.FindAll(value => value == 4).Count);
    }

    [Fact]
    public void SharedCallbackCancellationMatchesTheWatchedThread()
    {
        using var first = new LiveThread();
        using var second = new LiveThread();
        using var firstObserved = new ManualResetEventSlim();
        int calls = 0;
        Action task = () => Interlocked.Increment(ref calls);
        ThreadDeathWatcher.Watch(first.Thread, task);
        ThreadDeathWatcher.Watch(second.Thread, task);
        ThreadDeathWatcher.Watch(first.Thread, firstObserved.Set);
        ThreadDeathWatcher.Unwatch(second.Thread, task);
        first.End();
        Assert.True(firstObserved.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref calls));
        second.End();
        Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void ConcurrentRegistrationsAndCancellationsDeliverEachRemainingTaskExactlyOnce()
    {
        using var owner = new LiveThread();
        const int producers = 8, iterations = 1000;
        const int expected = producers * iterations / 2;
        int fired = 0, cancelled = 0;
        using var completed = new CountdownEvent(expected);
        Parallel.For(0, producers, producer =>
        {
            for (int i = 0; i < iterations; i++)
            {
                bool cancel = (i & 1) == 0;
                Action task = cancel ? () => Interlocked.Increment(ref cancelled) :
                    () => { Interlocked.Increment(ref fired); completed.Signal(); };
                ThreadDeathWatcher.Watch(owner.Thread, task);
                if (cancel) ThreadDeathWatcher.Unwatch(owner.Thread, task);
            }
        });
        owner.End();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        Assert.Equal(expected, Volatile.Read(ref fired));
        Assert.Equal(0, Volatile.Read(ref cancelled));
    }
}
