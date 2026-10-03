using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
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
        var task = Runnables.Empty;
        var thread = new Thread(() => { });
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Watch(null, task));
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Watch(thread, null));
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Unwatch(null, task));
        Assert.Throws<ArgumentNullException>(() => ThreadDeathWatcher.Unwatch(thread, null));
        Assert.Throws<ArgumentException>(() => ThreadDeathWatcher.Watch(thread, task));
        ThreadDeathWatcher.Unwatch(thread, task);
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentException>(() => ThreadDeathWatcher.Watch(thread, task));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromMilliseconds(-1)));
    }

    private sealed class EqualRunnable(Action action) : IRunnable
    {
        public void Run() => action();
        public override bool Equals(object value) => value is EqualRunnable;
        public override int GetHashCode() => 0;
    }
    [Fact]
    public void UnwatchUsesReferenceIdentityAndRemovesOnlyOneDuplicateRegistration()
    {
        using var owner = new LiveThread();
        using var completed = new CountdownEvent(2);
        int firstCount = 0, secondCount = 0;
        var first = new EqualRunnable(() => { Interlocked.Increment(ref firstCount); completed.Signal(); });
        var second = new EqualRunnable(() => { Interlocked.Increment(ref secondCount); completed.Signal(); });
        ThreadDeathWatcher.Watch(owner.Thread, first);
        ThreadDeathWatcher.Watch(owner.Thread, first);
        ThreadDeathWatcher.Watch(owner.Thread, second);
        ThreadDeathWatcher.Unwatch(owner.Thread, new EqualRunnable(() => { }));
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
        ThreadDeathWatcher.Watch(owner.Thread, Runnables.Create(() => { Interlocked.Increment(ref failures); throw new InvalidOperationException("callback failure"); }));
        ThreadDeathWatcher.Watch(owner.Thread, Runnables.Create(() =>
        {
            priority = Thread.CurrentThread.Priority;
            background = Thread.CurrentThread.IsBackground;
            name = Thread.CurrentThread.Name;
            completed.Set();
        }));
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
        var cancel = Runnables.Create(() => Interlocked.Increment(ref cancelled));
        ThreadDeathWatcher.Watch(first.Thread, Runnables.Create(() =>
        {
            ThreadDeathWatcher.Watch(second.Thread, cancel);
            ThreadDeathWatcher.Watch(second.Thread, Runnables.Create(completed.Set));
            ThreadDeathWatcher.Unwatch(second.Thread, cancel);
            registered.Set();
        }));
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
                ThreadDeathWatcher.Watch(owner.Thread, Runnables.Create(() =>
                {
                    captured = ambient.Value; firstWorker = Thread.CurrentThread; firstDone.Set();
                }));
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
                ThreadDeathWatcher.Watch(owner.Thread, Runnables.Create(() => { secondWorker = Thread.CurrentThread; secondDone.Set(); }));
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
        ThreadDeathWatcher.Watch(first.Thread, Runnables.Create(() => { worker = Thread.CurrentThread; firstDone.Set(); }));
        ThreadDeathWatcher.Watch(second.Thread, Runnables.Create(secondDone.Set));
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
    public void ZeroAndSubmillisecondAwaitUseJavaUnboundedJoinSemantics(long ticks)
    {
        using var owner = new LiveThread();
        ThreadDeathWatcher.Watch(owner.Thread, Runnables.Empty);
        using var entered = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        bool stopped = false;
        var waiter = new Thread(() => { entered.Set(); stopped = ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromTicks(ticks)); returned.Set(); }) { IsBackground = true };
        waiter.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(returned.Wait(TimeSpan.FromMilliseconds(50)));
        owner.End();
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        Assert.True(stopped);
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
                IRunnable task = cancel ? Runnables.Create(() => Interlocked.Increment(ref cancelled)) :
                    Runnables.Create(() => { Interlocked.Increment(ref fired); completed.Signal(); });
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
