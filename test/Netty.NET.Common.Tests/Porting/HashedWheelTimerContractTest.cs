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
    private static IThreadFactory BackgroundFactory() => new DefaultThreadFactory("wheel-contract", true);

    private static int InstanceCount() => (int)typeof(HashedWheelTimer)
        .GetField("INSTANCE_COUNTER", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    [Fact]
    public void FailedConstructionFinalizesWithoutCorruptingTheInstanceCount()
    {
        int initial = InstanceCount();
        Assert.Throws<ArgumentNullException>(() => new HashedWheelTimer((IThreadFactory)null));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(BackgroundFactory(), TimeSpan.MaxValue, 1));
        Assert.Throws<ArgumentException>(() => new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(1), 0));
        Assert.Throws<ArgumentNullException>(() => new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(1),
            1, true, -1, null));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal(initial, InstanceCount());
        using var timer = new HashedWheelTimer(BackgroundFactory());
        Assert.Equal(initial + 1, InstanceCount());
        timer.Dispose(); timer.Dispose();
        Assert.Equal(initial, InstanceCount());
    }

    [Fact]
    public void NativeDefaultFactoryCapturesItsGroupWithoutFastThreadLocalOwnership()
    {
        var group = new ThreadGroup("timer-factory-creator");
        IThreadFactory factory = null;
        Thread creator = group.NewThread(() => factory = Executors.DefaultThreadFactory());
        creator.Start(); Assert.True(creator.Join(TimeSpan.FromSeconds(5)));
        bool fastCleanup = true;
        Thread worker = factory.NewThread(() =>
            fastCleanup = FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
        Assert.Same(group, ThreadGroup.GetThreadGroup(worker));
        Assert.False(worker.IsBackground);
        Assert.Equal(ThreadPriority.Normal, worker.Priority);
        Assert.Matches("^pool-[0-9]+-thread-1$", worker.Name);
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.False(fastCleanup);
        Thread next = factory.NewThread(() => { });
        Assert.EndsWith("-thread-2", next.Name);
    }

    [Fact]
    public void PendingCountRollsBackWhenStoppedOrWorkerStartupFails()
    {
        using var stopped = new HashedWheelTimer(BackgroundFactory());
        Assert.Empty(stopped.Stop());
        Assert.Throws<ArgumentNullException>(() => stopped.NewTimeout(null, TimeSpan.Zero));
        for (int i = 0; i < 3; i++)
        {
            Assert.Throws<InvalidOperationException>(() => stopped.NewTimeout(TimerTask.Create(_ => { }), TimeSpan.Zero));
            Assert.Equal(0, stopped.PendingTimeouts());
        }

        var deadThread = new Thread(() => { });
        deadThread.Start(); Assert.True(deadThread.Join(TimeSpan.FromSeconds(5)));
        using var failed = new HashedWheelTimer(new AnonymousThreadFactory(_ => deadThread));
        Assert.Throws<ThreadStateException>(() => failed.NewTimeout(TimerTask.Create(_ => { }), TimeSpan.Zero));
        Assert.Equal(0, failed.PendingTimeouts());
    }

    private sealed class PausingTimer : HashedWheelTimer
    {
        internal readonly ManualResetEventSlim Started = new();
        internal readonly ManualResetEventSlim Continue = new();
        internal PausingTimer() : base(BackgroundFactory(), TimeSpan.FromMilliseconds(1), 4) { }
        public override void Start()
        {
            base.Start();
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
            try { timer.NewTimeout(TimerTask.Create(_ => Interlocked.Increment(ref runs)), TimeSpan.Zero); }
            catch (Exception caught) { failure = caught; }
        }) { IsBackground = true };
        submitter.Start();
        try
        {
            Assert.True(timer.Started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Empty(timer.Stop());
        }
        finally { timer.Continue.Set(); Assert.True(submitter.Join(TimeSpan.FromSeconds(5))); }
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("cannot be started once stopped", failure.Message);
        Assert.Equal(0, timer.PendingTimeouts());
        Assert.Equal(0, runs);
        timer.Started.Dispose(); timer.Continue.Dispose();
    }

    private sealed class RunOnlyTask(Action<ITimeout> action) : ITimerTask
    {
        public void Run(ITimeout timeout) => action(timeout);
    }

    [Fact]
    public void NativeDurationSaturationDoesNotExpireMaximumDelayAndClampsSubmillisecondTicks()
    {
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromTicks(1), 3);
        Assert.Equal(1_000_000, timer._tickDuration);
        Assert.Equal(4, timer._wheel.Length);
        int maximumRuns = 0;
        using var ready = new CountdownEvent(2);
        ITimeout maximum = timer.NewTimeout(TimerTask.Create(_ => Interlocked.Increment(ref maximumRuns)), TimeSpan.MaxValue);
        ITimeout minimum = timer.NewTimeout(new RunOnlyTask(_ => ready.Signal()), TimeSpan.MinValue);
        ITimeout immediate = timer.NewTimeout(new RunOnlyTask(_ => ready.Signal()), TimeSpan.Zero);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(minimum.IsExpired()); Assert.True(immediate.IsExpired());
        Assert.False(maximum.IsExpired()); Assert.Equal(0, maximumRuns);
        Assert.True(maximum.Cancel());
        Assert.False(maximum.Cancel());
    }

    [Fact]
    public void CancellationCallbacksRunOnceOnTheWorkerAndFailuresDoNotStopTheDrain()
    {
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(10));
        using var ready = new ManualResetEventSlim();
        Thread callbackThread = null;
        int firstCalls = 0, runs = 0;
        ITimeout first = timer.NewTimeout(TimerTask.Create(_ => Interlocked.Increment(ref runs), _ =>
        {
            Interlocked.Increment(ref firstCalls);
            throw new InvalidOperationException("cancel failure");
        }), TimeSpan.FromMinutes(1));
        ITimerTask secondTask = TimerTask.Create(_ => Interlocked.Increment(ref runs), _ =>
        {
            callbackThread = Thread.CurrentThread;
            ready.Set();
        });
        ITimeout second = timer.NewTimeout(secondTask, TimeSpan.FromMinutes(1));
        Assert.Same(timer, second.Timer()); Assert.Same(secondTask, second.Task());
        Assert.True(first.Cancel()); Assert.False(first.Cancel());
        Assert.True(second.Cancel());
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, firstCalls); Assert.Equal(0, runs);
        Assert.NotSame(Thread.CurrentThread, callbackThread);
        Assert.True(first.IsCancelled()); Assert.False(first.IsExpired());
        Assert.Equal(0, timer.PendingTimeouts());
    }

    [Fact]
    public void WorkerStopIsRejectedAndTaskFailureDoesNotPreventTheNextTimeout()
    {
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(1));
        using var ready = new ManualResetEventSlim();
        Exception stopFailure = null;
        ITimeout first = timer.NewTimeout(TimerTask.Create(_ => throw new InvalidOperationException("run failure")), TimeSpan.Zero);
        ITimeout second = timer.NewTimeout(TimerTask.Create(_ =>
        {
            try { timer.Stop(); }
            catch (Exception caught) { stopFailure = caught; }
            ready.Set();
        }), TimeSpan.Zero);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(stopFailure);
        Assert.True(first.IsExpired()); Assert.True(second.IsExpired());
        Assert.False(first.Cancel()); Assert.Equal(0, timer.PendingTimeouts());
    }

    [Fact]
    public void ExpirationAndPendingCountPrecedeExecutionOnTheSuppliedExecutor()
    {
        using var submitted = new BlockingCollection<IRunnable>();
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(1), 4, true, 1,
            new AnonymousExecutor(command => submitted.Add(command)));
        int runs = 0;
        ITimeout timeout = timer.NewTimeout(TimerTask.Create(_ => Interlocked.Increment(ref runs)), TimeSpan.Zero);
        Assert.True(submitted.TryTake(out IRunnable command, TimeSpan.FromSeconds(5)));
        Assert.True(timeout.IsExpired()); Assert.False(timeout.Cancel());
        Assert.Equal(0, timer.PendingTimeouts()); Assert.Equal(0, runs);
        Assert.Empty(timer.Stop());
        command.Run();
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ExecutorSubmissionFailureStillExpiresAndRemovesTheTimeout()
    {
        int submissions = 0, rejectedRuns = 0;
        using var ready = new ManualResetEventSlim();
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(1), 4, true, 2,
            new AnonymousExecutor(command =>
            {
                if (Interlocked.Increment(ref submissions) == 1) throw new RejectedExecutionException("executor rejected");
                command.Run();
            }));
        ITimeout rejected = timer.NewTimeout(TimerTask.Create(_ => Interlocked.Increment(ref rejectedRuns)), TimeSpan.Zero);
        timer.NewTimeout(TimerTask.Create(_ => ready.Set()), TimeSpan.Zero);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(rejected.IsExpired()); Assert.False(rejected.Cancel());
        Assert.Equal(0, rejectedRuns); Assert.Equal(0, timer.PendingTimeouts());
    }

    [Fact]
    public void StopRetainsThePinnedUnprocessedCountAndDoesNotDispatchPostShutdownCancellationCallbacks()
    {
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(10));
        int cancellations = 0, runs = 0;
        ITimerTask task = TimerTask.Create(_ => Interlocked.Increment(ref runs), _ => Interlocked.Increment(ref cancellations));
        ITimeout first = timer.NewTimeout(task, TimeSpan.FromMinutes(1));
        ITimeout second = timer.NewTimeout(task, TimeSpan.FromMinutes(1));
        var pending = timer.Stop();
        Assert.Equal(2, pending.Count);
        Assert.Contains(first, pending); Assert.Contains(second, pending);
        Assert.All(pending, timeout => Assert.True(timeout.IsCancelled()));
        Assert.Equal(2, timer.PendingTimeouts());
        Assert.Equal(0, runs); Assert.Equal(0, cancellations);
        Assert.Empty(timer.Stop());
    }

    [Fact]
    public void ThirtyDayTickWaitIsChunkedAndStopRemainsPrompt()
    {
        Thread worker = null;
        var factory = new AnonymousThreadFactory(runnable => worker = new Thread(runnable.Invoke) { IsBackground = true });
        using var timer = new HashedWheelTimer(factory, TimeSpan.FromDays(30), 1);
        timer.NewTimeout(new RunOnlyTask(_ => throw new InvalidOperationException("early expiration")), TimeSpan.FromDays(31));
        Assert.True(SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
        Assert.Single(timer.Stop());
        Assert.Throws<InvalidOperationException>(timer.Start);
    }

    [Fact]
    public void StopConsumesInterruptWhileJoiningAndRestoresItToTheCaller()
    {
        using var timer = new HashedWheelTimer(BackgroundFactory(), TimeSpan.FromMilliseconds(1));
        using var entered = new ManualResetEventSlim();
        using var stopping = new ManualResetEventSlim();
        int release = 0;
        timer.NewTimeout(TimerTask.Create(_ =>
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
                timer.Stop();
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
