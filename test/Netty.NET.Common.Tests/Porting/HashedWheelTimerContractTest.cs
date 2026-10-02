using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Timer globals")]
public class HashedWheelTimerContractTest
{
    private static IThreadFactory backgroundFactory() => new DefaultThreadFactory("wheel-contract", true);

    private static int instanceCount() => (int)typeof(HashedWheelTimer)
        .GetField("INSTANCE_COUNTER", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    [Fact]
    public void FailedConstructionFinalizesWithoutCorruptingTheInstanceCount()
    {
        int initial = instanceCount();
        Assert.Throws<ArgumentNullException>(() => new HashedWheelTimer((IThreadFactory)null));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(backgroundFactory(), TimeSpan.MaxValue, 1));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(1), 0));
        Assert.Throws<ArgumentNullException>(() => new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(1),
            1, true, -1, null));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal(initial, instanceCount());
        using var timer = new HashedWheelTimer(backgroundFactory());
        Assert.Equal(initial + 1, instanceCount());
        timer.Dispose(); timer.Dispose();
        Assert.Equal(initial, instanceCount());
    }

    [Fact]
    public void NativeDefaultFactoryCapturesItsGroupWithoutFastThreadLocalOwnership()
    {
        var group = new ThreadGroup("timer-factory-creator");
        IThreadFactory factory = null;
        Thread creator = group.newThread(Runnables.Create(() => factory = Executors.defaultThreadFactory()));
        creator.Start(); Assert.True(creator.Join(TimeSpan.FromSeconds(5)));
        bool fastCleanup = true;
        Thread worker = factory.newThread(Runnables.Create(() =>
            fastCleanup = FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals()));
        Assert.Same(group, ThreadGroup.getThreadGroup(worker));
        Assert.False(worker.IsBackground);
        Assert.Equal(ThreadPriority.Normal, worker.Priority);
        Assert.Matches("^pool-[0-9]+-thread-1$", worker.Name);
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.False(fastCleanup);
        Thread next = factory.newThread(Runnables.Empty);
        Assert.EndsWith("-thread-2", next.Name);
    }

    [Fact]
    public void PendingCountRollsBackWhenStoppedOrWorkerStartupFails()
    {
        using var stopped = new HashedWheelTimer(backgroundFactory());
        Assert.Empty(stopped.stop());
        Assert.Throws<ArgumentNullException>(() => stopped.newTimeout(null, TimeSpan.Zero));
        for (int i = 0; i < 3; i++)
        {
            Assert.Throws<InvalidOperationException>(() => stopped.newTimeout(TimerTask.Create(_ => { }), TimeSpan.Zero));
            Assert.Equal(0, stopped.pendingTimeouts());
        }

        var deadThread = new Thread(() => { });
        deadThread.Start(); Assert.True(deadThread.Join(TimeSpan.FromSeconds(5)));
        using var failed = new HashedWheelTimer(new AnonymousThreadFactory(_ => deadThread));
        Assert.Throws<ThreadStateException>(() => failed.newTimeout(TimerTask.Create(_ => { }), TimeSpan.Zero));
        Assert.Equal(0, failed.pendingTimeouts());
    }

    private sealed class PausingTimer : HashedWheelTimer
    {
        internal readonly ManualResetEventSlim Started = new();
        internal readonly ManualResetEventSlim Continue = new();
        internal PausingTimer() : base(backgroundFactory(), TimeSpan.FromMilliseconds(1), 4) { }
        public override void start()
        {
            base.start();
            Started.Set();
            if (!Continue.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Scheduling gate was not released.");
        }
    }

    [Fact]
    public void ShutdownBetweenStartupAndEnqueueRejectsTheUndrainedTimeout()
    {
        using var timer = new PausingTimer();
        Exception failure = null;
        int runs = 0;
        Thread submitter = new(() =>
        {
            try { timer.newTimeout(TimerTask.Create(_ => Interlocked.Increment(ref runs)), TimeSpan.Zero); }
            catch (Exception caught) { failure = caught; }
        }) { IsBackground = true };
        submitter.Start();
        try
        {
            Assert.True(timer.Started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Empty(timer.stop());
        }
        finally { timer.Continue.Set(); Assert.True(submitter.Join(TimeSpan.FromSeconds(5))); }
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("cannot be started once stopped", failure.Message);
        Assert.Equal(0, timer.pendingTimeouts());
        Assert.Equal(0, runs);
        timer.Started.Dispose(); timer.Continue.Dispose();
    }

    private sealed class RunOnlyTask(Action<ITimeout> action) : ITimerTask
    {
        public void run(ITimeout timeout) => action(timeout);
    }

    [Fact]
    public void NativeDurationSaturationDoesNotExpireMaximumDelayAndClampsSubmillisecondTicks()
    {
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromTicks(1), 3);
        Assert.Equal(1_000_000, timer._tickDuration);
        Assert.Equal(4, timer._wheel.Length);
        int maximumRuns = 0;
        using var ready = new CountdownEvent(2);
        ITimeout maximum = timer.newTimeout(TimerTask.Create(_ => Interlocked.Increment(ref maximumRuns)), TimeSpan.MaxValue);
        ITimeout minimum = timer.newTimeout(new RunOnlyTask(_ => ready.Signal()), TimeSpan.MinValue);
        ITimeout immediate = timer.newTimeout(new RunOnlyTask(_ => ready.Signal()), TimeSpan.Zero);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(minimum.isExpired()); Assert.True(immediate.isExpired());
        Assert.False(maximum.isExpired()); Assert.Equal(0, maximumRuns);
        Assert.True(maximum.cancel());
        Assert.False(maximum.cancel());
    }

    [Fact]
    public void CancellationCallbacksRunOnceOnTheWorkerAndFailuresDoNotStopTheDrain()
    {
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(10));
        using var ready = new ManualResetEventSlim();
        Thread callbackThread = null;
        int firstCalls = 0, runs = 0;
        ITimeout first = timer.newTimeout(TimerTask.Create(_ => Interlocked.Increment(ref runs), _ =>
        {
            Interlocked.Increment(ref firstCalls);
            throw new InvalidOperationException("cancel failure");
        }), TimeSpan.FromMinutes(1));
        ITimerTask secondTask = TimerTask.Create(_ => Interlocked.Increment(ref runs), _ =>
        {
            callbackThread = Thread.CurrentThread;
            ready.Set();
        });
        ITimeout second = timer.newTimeout(secondTask, TimeSpan.FromMinutes(1));
        Assert.Same(timer, second.timer()); Assert.Same(secondTask, second.task());
        Assert.True(first.cancel()); Assert.False(first.cancel());
        Assert.True(second.cancel());
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, firstCalls); Assert.Equal(0, runs);
        Assert.NotSame(Thread.CurrentThread, callbackThread);
        Assert.True(first.isCancelled()); Assert.False(first.isExpired());
        Assert.Equal(0, timer.pendingTimeouts());
    }

    [Fact]
    public void WorkerStopIsRejectedAndTaskFailureDoesNotPreventTheNextTimeout()
    {
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(1));
        using var ready = new ManualResetEventSlim();
        Exception stopFailure = null;
        ITimeout first = timer.newTimeout(TimerTask.Create(_ => throw new InvalidOperationException("run failure")), TimeSpan.Zero);
        ITimeout second = timer.newTimeout(TimerTask.Create(_ =>
        {
            try { timer.stop(); }
            catch (Exception caught) { stopFailure = caught; }
            ready.Set();
        }), TimeSpan.Zero);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(stopFailure);
        Assert.True(first.isExpired()); Assert.True(second.isExpired());
        Assert.False(first.cancel()); Assert.Equal(0, timer.pendingTimeouts());
    }

    [Fact]
    public void ExpirationAndPendingCountPrecedeExecutionOnTheSuppliedExecutor()
    {
        using var submitted = new BlockingCollection<IRunnable>();
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(1), 4, true, 1,
            new AnonymousExecutor(command => submitted.Add(command)));
        int runs = 0;
        ITimeout timeout = timer.newTimeout(TimerTask.Create(_ => Interlocked.Increment(ref runs)), TimeSpan.Zero);
        Assert.True(submitted.TryTake(out IRunnable command, TimeSpan.FromSeconds(5)));
        Assert.True(timeout.isExpired()); Assert.False(timeout.cancel());
        Assert.Equal(0, timer.pendingTimeouts()); Assert.Equal(0, runs);
        Assert.Empty(timer.stop());
        command.run();
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ExecutorSubmissionFailureStillExpiresAndRemovesTheTimeout()
    {
        int submissions = 0, rejectedRuns = 0;
        using var ready = new ManualResetEventSlim();
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(1), 4, true, 2,
            new AnonymousExecutor(command =>
            {
                if (Interlocked.Increment(ref submissions) == 1) throw new RejectedExecutionException("executor rejected");
                command.run();
            }));
        ITimeout rejected = timer.newTimeout(TimerTask.Create(_ => Interlocked.Increment(ref rejectedRuns)), TimeSpan.Zero);
        timer.newTimeout(TimerTask.Create(_ => ready.Set()), TimeSpan.Zero);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(rejected.isExpired()); Assert.False(rejected.cancel());
        Assert.Equal(0, rejectedRuns); Assert.Equal(0, timer.pendingTimeouts());
    }

    [Fact]
    public void StopRetainsThePinnedUnprocessedCountAndDoesNotDispatchPostShutdownCancellationCallbacks()
    {
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(10));
        int cancellations = 0, runs = 0;
        ITimerTask task = TimerTask.Create(_ => Interlocked.Increment(ref runs), _ => Interlocked.Increment(ref cancellations));
        ITimeout first = timer.newTimeout(task, TimeSpan.FromMinutes(1));
        ITimeout second = timer.newTimeout(task, TimeSpan.FromMinutes(1));
        var pending = timer.stop();
        Assert.Equal(2, pending.Count);
        Assert.Contains(first, pending); Assert.Contains(second, pending);
        Assert.All(pending, timeout => Assert.True(timeout.isCancelled()));
        Assert.Equal(2, timer.pendingTimeouts());
        Assert.Equal(0, runs); Assert.Equal(0, cancellations);
        Assert.Empty(timer.stop());
    }

    [Fact]
    public void ThirtyDayTickWaitIsChunkedAndStopRemainsPrompt()
    {
        Thread worker = null;
        var factory = new AnonymousThreadFactory(runnable => worker = new Thread(runnable.run) { IsBackground = true });
        using var timer = new HashedWheelTimer(factory, TimeSpan.FromDays(30), 1);
        timer.newTimeout(new RunOnlyTask(_ => throw new InvalidOperationException("early expiration")), TimeSpan.FromDays(31));
        Assert.True(SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
        Assert.Single(timer.stop());
        Assert.Throws<InvalidOperationException>(timer.start);
    }

    [Fact]
    public void StopConsumesInterruptWhileJoiningAndRestoresItToTheCaller()
    {
        using var timer = new HashedWheelTimer(backgroundFactory(), TimeSpan.FromMilliseconds(1));
        using var entered = new ManualResetEventSlim();
        using var stopping = new ManualResetEventSlim();
        int release = 0;
        timer.newTimeout(TimerTask.Create(_ =>
        {
            entered.Set();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Volatile.Read(ref release) == 0 && DateTime.UtcNow < deadline) Thread.SpinWait(100);
        }), TimeSpan.Zero);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        bool restored = false;
        Exception failure = null;
        Thread stopper = new(() =>
        {
            try
            {
                stopping.Set();
                timer.stop();
                try { Thread.Sleep(0); }
                catch (ThreadInterruptedException) { restored = true; }
            }
            catch (Exception caught) { failure = caught; }
        }) { IsBackground = true };
        stopper.Start();
        try
        {
            Assert.True(stopping.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => (stopper.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            stopper.Interrupt();
        }
        finally { Volatile.Write(ref release, 1); Assert.True(stopper.Join(TimeSpan.FromSeconds(5))); }
        Assert.Null(failure); Assert.True(restored);
    }
}
