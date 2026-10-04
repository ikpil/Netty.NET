/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class SingleThreadEventExecutorTest
{
    // CLR Thread is sealed. The wrapper observes native start and signals before
    // invoking the executor runnable, preserving the original race barriers.
    private sealed class TestThread
    {
        internal readonly Thread thread;
        private readonly CountdownEvent runLatch = new(1);
        internal TestThread(Action task)
        {
            thread = new Thread(() => { runLatch.Signal(); task(); }) { IsBackground = true };
        }
        internal void AwaitStarted() =>
            Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & System.Threading.ThreadState.Unstarted) == 0,
                TimeSpan.FromSeconds(5)));
        internal void AwaitRunnableExecution() => Assert.True(runLatch.Wait(TimeSpan.FromSeconds(5)));
        internal void Join() => Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
    private sealed class TestThreadFactory : IThreadFactory
    {
        internal readonly BlockingCollection<TestThread> threads = new();
        public Thread NewThread(Action runnable)
        {
            var wrapper = new TestThread(runnable);
            threads.Add(wrapper);
            return wrapper.thread;
        }
        internal TestThread Take()
        {
            Assert.True(threads.TryTake(out var thread, TimeSpan.FromSeconds(5)));
            return thread;
        }
        internal void Drain() { while (threads.TryTake(out var thread)) thread.Join(); }
    }
    private class LoopExecutor : SingleThreadEventExecutor
    {
        internal Action started;
        internal LoopExecutor(IThreadFactory factory, bool wake = true) : base(null, factory, wake) { }
        internal LoopExecutor(Action<Action> executor, bool wake = true) : base(null, executor, wake) { }
        internal LoopExecutor(Action<Action> executor, IQueue<IRunnable> queue)
            : base(null, executor, false, queue, RejectedExecutionHandlers.Reject()) { }
        protected override void Run()
        {
            started?.Invoke();
            while (!ConfirmShutdown())
            {
                IRunnable task = TakeTask();
                task?.Run();
            }
        }
    }
    private sealed class SuspendingSingleThreadEventExecutor : SingleThreadEventExecutor
    {
        internal SuspendingSingleThreadEventExecutor(IThreadFactory factory)
            : base(null, factory, false, true, int.MaxValue, RejectedExecutionHandlers.Reject()) { }
        protected override void Run()
        {
            while (!ConfirmShutdown() && !CanSuspend()) TakeTask()?.Run();
        }
        public override void Wakeup(bool inEventLoop) => InterruptThread();
    }
    private sealed class CountingExecutor : SingleThreadEventExecutor
    {
        internal CountingExecutor(Action<Action> executor)
            : base(null, executor, false, true, int.MaxValue, RejectedExecutionHandlers.Reject()) { }
        protected override void Run() => throw new Exception("must not run");
    }
    private class LatchTask : IRunnable
    {
        internal readonly CountdownEvent latch = new(1);
        public void Run() => latch.Signal();
        internal void Await() => Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
    }
    private sealed class LazyLatchTask : LatchTask { }
    private static void Shutdown(IEventExecutor executor) =>
        executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    private static void Sync(Task future)
    {
        future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
    private static void Suspend(SingleThreadEventExecutor executor, int sleep = 50)
    {
        var time = Stopwatch.StartNew();
        while (!executor.TrySuspend())
        {
            Assert.True(time.Elapsed < TimeSpan.FromSeconds(2), "executor did not suspend");
            Thread.Sleep(sleep);
        }
    }

    [Fact]
    public void TestSuspension()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        try
        {
            var task1 = new LatchTask();
            executor.Execute(task1);
            var currentThread = factory.Take();
            Assert.True(executor.TrySuspend());
            task1.Await();

            // Let's wait till the current Thread did die....
            currentThread.Join();

            // Should be suspended now, we should be able to also call trySuspend() again.
            Assert.True(executor.IsSuspended());
            // There was no thread created as we did not try to execute something yet.
            Assert.Equal(0, factory.threads.Count);

            var task2 = new LatchTask();
            executor.Execute(task2);
            // Suspendion was reset as a task was executed.
            Assert.False(executor.IsSuspended());
            currentThread = factory.Take();
            task2.Await();

            Shutdown(executor);
            currentThread.Join();
            Assert.False(executor.IsSuspended());
            Assert.True(executor.IsShutdown());

            // Guarantee that al tasks were able to die...
            factory.Drain();
        }
        finally { Shutdown(executor); }
    }

    [Fact]
    public void TestSuspensionWhenExecutorIsNotStarted()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        try
        {
            // suspend when executor is not started yet
            Assert.True(executor.TrySuspend());
            Assert.True(executor.IsSuspended());

            // recover from suspension by executing a task
            var task1 = new LatchTask();
            executor.Execute(task1);
            var currentThread = factory.Take();
            Assert.False(executor.IsSuspended());
            task1.Await();

            Shutdown(executor);
            currentThread.Join();
            Assert.False(executor.IsSuspended());
            Assert.True(executor.IsShutdown());

            // Guarantee that all threads were able to die...
            factory.Drain();
        }
        finally { Shutdown(executor); }
    }

    [Fact]
    public void TestExecuteRacingTrySuspendWhenExecutorIsNotStarted()
    {
        // execute() on a never started executor races with trySuspend(), which moves the executor from
        // ST_NOT_STARTED to ST_SUSPENDED. Whatever the interleaving, execute() must request a thread, otherwise the
        // task it just added is stranded. The race is timing dependent, so repeat it many times.
        SingleThreadEventExecutor executorRef = null;
        int suspended = 0;
        bool done = false;
        var suspender = new Thread(() =>
        {
            while (!Volatile.Read(ref done))
            {
                var executor = Interlocked.Exchange(ref executorRef, null);
                if (executor == null) Thread.Yield();
                else { executor.TrySuspend(); Interlocked.Increment(ref suspended); }
            }
        }) { IsBackground = true };
        suspender.Start();
        try
        {
            for (int i = 1; i <= 10000; i++)
            {
                int threadStarts = 0;
                // Only count the requests to start a thread, no thread is ever started.
                var executor = new CountingExecutor(_ => Interlocked.Increment(ref threadStarts));
                Volatile.Write(ref executorRef, executor);
                executor.Execute(Runnables.Empty);
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref suspended) == i, TimeSpan.FromSeconds(2)));
                Assert.Equal(1, Volatile.Read(ref threadStarts));
            }
        }
        finally
        {
            Volatile.Write(ref done, true);
            Assert.True(suspender.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void TestNotSuspendedUntilScheduledTaskIsCancelled()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        try
        {
            // Schedule a task which is so far in the future that we are sure it will not run at all.
            using var cancellation = new CancellationTokenSource();
            var future = executor.ScheduleAsync(() => { }, TimeSpan.FromDays(1), cancellation.Token);
            var currentThread = factory.Take();
            // Let's wait until the thread is started
            currentThread.AwaitStarted();
            currentThread.AwaitRunnableExecution();
            Assert.True(executor.TrySuspend());

            // Now cancel the task which should allow the suspension to let the thread die once we call trySuspend() again
            cancellation.Cancel();
            Assert.True(future.IsCanceled);

            // Call in a loop as removal of scheduled tasks from task queue might be lazy
            Suspend(executor);
            currentThread.Join();

            // Should be suspended now, we should be able to also call trySuspend() again.
            Assert.True(executor.TrySuspend());
            Assert.True(executor.IsSuspended());

            Shutdown(executor);
            Assert.False(executor.IsSuspended());
            Assert.True(executor.IsShutdown());

            // Guarantee that al tasks were able to die...
            factory.Drain();
        }
        finally { Shutdown(executor); }
    }

    [Fact]
    public void TestSuspendDoesNotRaceWithCancelledScheduledTask()
    {
        // Regression test: cancelling a scheduled task right as the executor is about to suspend used to
        // be able to race with the suspend confirmation in doStartThread(). The cancelled task schedules
        // its own removal via scheduleRemoveScheduled(), which can land in the task queue in the tiny
        // window between run() deciding it can suspend and doStartThread() re-checking canSuspend().
        // That caused the executor to treat the situation as if run() returned without confirming
        // shutdown, spuriously shutting itself down instead of suspending (or continuing to run).
        // The race is timing dependent, so we repeat the scenario many times to make a regression likely
        // to be caught.
        for (int i = 0; i < 2000; i++)
        {
            var factory = new TestThreadFactory();
            var executor = new SuspendingSingleThreadEventExecutor(factory);
            try
            {
                using var cancellation = new CancellationTokenSource();
                var future = executor.ScheduleAsync(() => { }, TimeSpan.FromDays(1), cancellation.Token);
                var currentThread = factory.Take();
                currentThread.AwaitStarted();
                currentThread.AwaitRunnableExecution();
                Assert.True(executor.TrySuspend(), "iteration " + i);

                cancellation.Cancel();
                Assert.True(future.IsCanceled, "iteration " + i);
                Suspend(executor, 1);
                Assert.True(currentThread.thread.Join(TimeSpan.FromSeconds(2)), "worker did not terminate: " + i);
                Assert.False(currentThread.thread.IsAlive);
                Assert.True(executor.TrySuspend(), "iteration " + i);
                Assert.True(executor.IsSuspended(), "iteration " + i);

                Shutdown(executor);
                factory.Drain();
            }
            finally { Shutdown(executor); }
        }
    }

    [Fact]
    public void TestNotSuspendedUntilScheduledTaskDidRun()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        using var latch = new CountdownEvent(1);
        try
        {
            // Schedule a task which is so far in the future that we are sure it will not run at all.
            var future = executor.ScheduleAsync(() =>
            {
                try { latch.Wait(); }
                catch (ThreadInterruptedException) { /* ignore */ }
                // ignore
            }, TimeSpan.FromMilliseconds(100));
            var currentThread = factory.Take();
            // Let's wait until the thread is started
            currentThread.AwaitStarted();
            currentThread.AwaitRunnableExecution();
            latch.Signal();
            Assert.True(executor.TrySuspend());

            // Now wait till the scheduled task was run
            Sync(future);
            currentThread.Join();

            // Should be suspended now, we should be able to also call trySuspend() again.
            Assert.True(executor.TrySuspend());
            Assert.True(executor.IsSuspended());

            Shutdown(executor);
            Assert.False(executor.IsSuspended());
            Assert.True(executor.IsShutdown());

            // Guarantee that al tasks were able to die...
            factory.Drain();
        }
        finally
        {
            if (!latch.IsSet) latch.Signal();
            Shutdown(executor);
        }
    }

    private sealed class ClosedExecutor
    {
        public void Execute(Action command) => throw new RejectedExecutionException();
    }
    [Fact]
    public void TestWrappedExecutorIsShutdown()
    {
        // CLR: a closed backing executor reproduces ExecutorService.shutdownNow rejection.
        var executor = new LoopExecutor(new ClosedExecutor().Execute, false);
        ExecuteShouldFail(executor);
        ExecuteShouldFail(executor);
        Assert.Throws<RejectedExecutionException>(() =>
            executor.ShutdownGracefullyAsync().GetAwaiter().GetResult());
        Assert.True(executor.IsShutdown());
    }
    private static void ExecuteShouldFail(IExecutor executor) =>
        Assert.Throws<RejectedExecutionException>(() => executor.Execute(Runnables.Create(() =>
        {
            // Noop.
        })));

    [Fact]
    public void TestThreadProperties()
    {
        Thread threadRef = null;
        var executor = new LoopExecutor(new DefaultThreadFactory("test"), false);
        executor.started = () => threadRef = Thread.CurrentThread;
        try
        {
            var properties = executor.ThreadProperties();
            Assert.Equal(threadRef.ManagedThreadId, properties.Id());
            Assert.Equal(threadRef.Name, properties.Name());
            Assert.Equal(threadRef.Priority, properties.Priority());
            Assert.Equal(threadRef.IsAlive, properties.IsAlive());
            Assert.Equal(threadRef.IsBackground, properties.IsDaemon());
            // CLR cannot inspect another managed thread's stack. Verify native owner
            // stack capture in its loop and explicit failure of a foreign query.
            Assert.Throws<NotSupportedException>(() => properties.StackTrace());
            var trace = executor.SubmitAsync<StackFrame[]>(properties.StackTrace);
            Assert.NotEmpty(trace.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { Shutdown(executor); }
    }

    // The pinned four JDK blocking-guard tests have no retained bulk method.
    // Native async composition yields the owner and explicitly dispatches owned
    // state after await. The design record maps these replacement scenarios.
    [Fact] public Task NativeWhenAnyYieldsInEventLoop() => TestNativeCompositionInEventLoop(true, false);
    [Fact] public Task NativeWhenAnyWithObserverTimeoutYieldsInEventLoop() => TestNativeCompositionInEventLoop(true, true);
    [Fact] public Task NativeWhenAllYieldsInEventLoop() => TestNativeCompositionInEventLoop(false, false);
    [Fact] public Task NativeWhenAllWithObserverTimeoutYieldsInEventLoop() => TestNativeCompositionInEventLoop(false, true);
    private static async Task TestNativeCompositionInEventLoop(bool any, bool timeout)
    {
        var executor = new LoopExecutor(new DefaultThreadFactory("compose"));
        int calls = 0;
        try
        {
            Task operation = executor.SubmitAsync(async () =>
            {
                Assert.True(executor.InEventLoop());
                Task<int>[] children =
                {
                    executor.SubmitAsync(() => { Assert.True(executor.InEventLoop()); return ++calls; }),
                    executor.SubmitAsync(() => { Assert.True(executor.InEventLoop()); return ++calls; })
                };
                Task composition = any ? Task.WhenAny(children) : Task.WhenAll(children);
                if (timeout) await composition.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                else await composition.ConfigureAwait(false);
                await executor.SubmitAsync(() =>
                {
                    Assert.True(executor.InEventLoop());
                    Assert.Equal(2, calls);
                }).ConfigureAwait(false);
                Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(children).ConfigureAwait(false));
            });
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { Shutdown(executor); }
    }
    private sealed class LazyExecutor : LoopExecutor
    {
        internal LazyExecutor() : base(new DefaultThreadFactory("lazy"), false) { }
        protected override bool WakesUpForTask(IRunnable task) => task is not LazyLatchTask;
        protected override void Run()
        {
            while (!ConfirmShutdown())
            {
                lock (this) { if (!HasTasks()) Monitor.Wait(this); }
                RunAllTasks();
            }
        }
        public override void Wakeup(bool inEventLoop)
        {
            if (!inEventLoop) { lock (this) Monitor.PulseAll(this); }
        }
    }
    [Fact]
    public void TestLazyExecution()
    {
        var executor = new LazyExecutor();
        try
        {
            // Ensure event loop is started
            var latch0 = new LatchTask();
            executor.Execute(latch0);
            Assert.True(latch0.latch.Wait(100));
            // Pause to ensure it enters waiting state
            Thread.Sleep(100);

            // Submit task via lazyExecute
            var latch1 = new LatchTask();
            executor.LazyExecute(latch1);
            // Sumbit lazy task via regular execute
            var latch2 = new LazyLatchTask();
            executor.Execute(latch2);

            // Neither should run yet
            Assert.False(latch1.latch.Wait(100));
            Assert.False(latch2.latch.Wait(100));

            // Submit regular task via regular execute
            var latch3 = new LatchTask();
            executor.Execute(latch3);

            // Should flush latch1 and latch2 and then run latch3 immediately
            Assert.True(latch3.latch.Wait(100));
            Assert.Equal(0, latch1.latch.CurrentCount);
            Assert.Equal(0, latch2.latch.CurrentCount);
        }
        finally { Shutdown(executor); }
    }

    private sealed class NoRemoveQueue : LinkedBlockingQueue<IRunnable>
    {
        internal NoRemoveQueue() : base(int.MaxValue) { }
        public override bool TryRemove(IRunnable task) => throw new NotSupportedException();
    }
    private sealed class AfterShutdownExecutor : LoopExecutor
    {
        internal int attempts;
        internal int rejects;
        internal readonly ConcurrentQueue<Task> submittedTasks = new();
        internal AfterShutdownExecutor(IQueue<IRunnable> queue)
            : base(new ThreadPerTaskExecutor(new DefaultThreadFactory("after-shutdown")).Execute, queue) { }
        protected override bool ConfirmShutdown()
        {
            bool result = base.ConfirmShutdown();
            // After shutdown is confirmed, scheduled one more task and record it
            if (result)
            {
                ++attempts;
                var submission = this.SubmitAsync(() => { });
                if (submission.IsFaulted && submission.Exception.InnerException is RejectedExecutionException)
                {
                    // ignore, tasks are either accepted or rejected
                    ++rejects;
                }
                else submittedTasks.Enqueue(submission);
            }
            return result;
        }
    }
    [Fact]
    public void TestTaskAddedAfterShutdownNotAbandoned()
    {
        // A queue that doesn't support remove, so tasks once added cannot be rejected anymore
        var taskQueue = new NoRemoveQueue();
        var executor = new AfterShutdownExecutor(taskQueue);
        try
        {
            // Start the loop
            executor.SubmitAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            // Shutdown without any quiet period
            executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(100))
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            // Ensure there are no user-tasks left.
            Assert.Equal(0, executor.DrainTasks());
            // Verify that queue is empty and all attempts either succeeded or were rejected
            Assert.True(taskQueue.IsEmpty());
            Assert.True(executor.attempts > 0);
            Assert.Equal(executor.attempts, executor.submittedTasks.Count + executor.rejects);
            foreach (Task future in executor.submittedTasks) Assert.True(future.IsCompletedSuccessfully);
        }
        finally { Shutdown(executor); }
    }

    private sealed class TestRunnable : IRunnable
    {
        internal volatile bool ran;
        public void Run() => ran = true;
    }
    [Fact]
    public void TestTakeTask()
    {
        var executor = new LoopExecutor(new DefaultThreadFactory("take"));
        try
        {
            //add task
            var beforeTask = new TestRunnable();
            executor.Execute(beforeTask);
            //add scheduled task
            var scheduledTask = new TestRunnable();
            var f = executor.ScheduleAsync(scheduledTask.Run, TimeSpan.FromMilliseconds(1500));
            //add task
            var afterTask = new TestRunnable();
            executor.Execute(afterTask);
            Sync(f);
            Assert.True(beforeTask.ran);
            Assert.True(scheduledTask.ran);
            Assert.True(afterTask.ran);
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void TestTakeTaskAlwaysHasTask()
    {
        //for https://github.com/netty/netty/issues/1614
        var executor = new LoopExecutor(new DefaultThreadFactory("busy-take"));
        try
        {
            //add scheduled task
            var t = new TestRunnable();
            var f = executor.ScheduleAsync(t.Run, TimeSpan.FromMilliseconds(1500));
            //ensure always has at least one task in taskQueue
            //check if scheduled tasks are triggered
            IRunnable repeat = null;
            repeat = Runnables.Create(() => { if (!f.IsCompleted) executor.Execute(repeat); });
            executor.Execute(repeat);
            Sync(f);
            Assert.True(t.ran);
        }
        finally { Shutdown(executor); }
    }
    private sealed class ThrowingExecutor : LoopExecutor
    {
        private readonly Exception exception;
        internal ThrowingExecutor(Exception exception) : base(new DefaultThreadFactory("throwing")) => this.exception = exception;
        protected override void Run() => throw exception;
    }
    [Fact]
    public async Task TestExceptionIsPropagatedToTerminationFuture()
    {
        var exception = new InvalidOperationException();
        var executor = new ThrowingExecutor(exception);
        // Schedule something so we are sure the run() method will be called.
        executor.Execute(Runnables.Create(() =>
        {
            // Noop.
        }));
        var future = executor.Termination;
        var task = future;
        Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            task.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Same(exception, task.Exception.InnerException);
    }
}
