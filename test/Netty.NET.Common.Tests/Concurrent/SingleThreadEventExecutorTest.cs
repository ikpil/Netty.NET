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
        internal TestThread(IRunnable task)
        {
            thread = new Thread(() => { runLatch.Signal(); task.run(); }) { IsBackground = true };
        }
        internal void awaitStarted() =>
            Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & System.Threading.ThreadState.Unstarted) == 0,
                TimeSpan.FromSeconds(5)));
        internal void awaitRunnableExecution() => Assert.True(runLatch.Wait(TimeSpan.FromSeconds(5)));
        internal void join() => Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
    private sealed class TestThreadFactory : IThreadFactory
    {
        internal readonly BlockingCollection<TestThread> threads = new();
        public Thread newThread(IRunnable runnable)
        {
            var wrapper = new TestThread(runnable);
            threads.Add(wrapper);
            return wrapper.thread;
        }
        internal TestThread take()
        {
            Assert.True(threads.TryTake(out var thread, TimeSpan.FromSeconds(5)));
            return thread;
        }
        internal void drain() { while (threads.TryTake(out var thread)) thread.join(); }
    }
    private class LoopExecutor : SingleThreadEventExecutor
    {
        internal Action started;
        internal LoopExecutor(IThreadFactory factory, bool wake = true) : base(null, factory, wake) { }
        internal LoopExecutor(IExecutor executor, bool wake = true) : base(null, executor, wake) { }
        internal LoopExecutor(IExecutor executor, IQueue<IRunnable> queue)
            : base(null, executor, false, queue, RejectedExecutionHandlers.reject()) { }
        protected override void run()
        {
            started?.Invoke();
            while (!confirmShutdown())
            {
                IRunnable task = takeTask();
                task?.run();
            }
        }
    }
    private sealed class SuspendingSingleThreadEventExecutor : SingleThreadEventExecutor
    {
        internal SuspendingSingleThreadEventExecutor(IThreadFactory factory)
            : base(null, factory, false, true, int.MaxValue, RejectedExecutionHandlers.reject()) { }
        protected override void run()
        {
            while (!confirmShutdown() && !canSuspend()) takeTask()?.run();
        }
        public override void wakeup(bool inEventLoop) => interruptThread();
    }
    private sealed class CountingExecutor : SingleThreadEventExecutor
    {
        internal CountingExecutor(IExecutor executor)
            : base(null, executor, false, true, int.MaxValue, RejectedExecutionHandlers.reject()) { }
        protected override void run() => throw new Exception("must not run");
    }
    private class LatchTask : IRunnable
    {
        internal readonly CountdownEvent latch = new(1);
        public void run() => latch.Signal();
        internal void await() => Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
    }
    private sealed class LazyLatchTask : LatchTask { }
    private static void shutdown(IEventExecutor executor) =>
        executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    private static void sync(IFuture<Void> future)
    {
        Assert.True(future.await(TimeSpan.FromSeconds(5)));
        future.sync();
    }
    private static void suspend(SingleThreadEventExecutor executor, int sleep = 50)
    {
        var time = Stopwatch.StartNew();
        while (!executor.trySuspend())
        {
            Assert.True(time.Elapsed < TimeSpan.FromSeconds(2), "executor did not suspend");
            Thread.Sleep(sleep);
        }
    }

    [Fact]
    public void testSuspension()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        try
        {
            var task1 = new LatchTask();
            executor.execute(task1);
            var currentThread = factory.take();
            Assert.True(executor.trySuspend());
            task1.await();

            // Let's wait till the current Thread did die....
            currentThread.join();

            // Should be suspended now, we should be able to also call trySuspend() again.
            Assert.True(executor.isSuspended());
            // There was no thread created as we did not try to execute something yet.
            Assert.Equal(0, factory.threads.Count);

            var task2 = new LatchTask();
            executor.execute(task2);
            // Suspendion was reset as a task was executed.
            Assert.False(executor.isSuspended());
            currentThread = factory.take();
            task2.await();

            shutdown(executor);
            currentThread.join();
            Assert.False(executor.isSuspended());
            Assert.True(executor.isShutdown());

            // Guarantee that al tasks were able to die...
            factory.drain();
        }
        finally { shutdown(executor); }
    }

    [Fact]
    public void testSuspensionWhenExecutorIsNotStarted()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        try
        {
            // suspend when executor is not started yet
            Assert.True(executor.trySuspend());
            Assert.True(executor.isSuspended());

            // recover from suspension by executing a task
            var task1 = new LatchTask();
            executor.execute(task1);
            var currentThread = factory.take();
            Assert.False(executor.isSuspended());
            task1.await();

            shutdown(executor);
            currentThread.join();
            Assert.False(executor.isSuspended());
            Assert.True(executor.isShutdown());

            // Guarantee that all threads were able to die...
            factory.drain();
        }
        finally { shutdown(executor); }
    }

    [Fact]
    public void testExecuteRacingTrySuspendWhenExecutorIsNotStarted()
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
                else { executor.trySuspend(); Interlocked.Increment(ref suspended); }
            }
        }) { IsBackground = true };
        suspender.Start();
        try
        {
            for (int i = 1; i <= 10000; i++)
            {
                int threadStarts = 0;
                // Only count the requests to start a thread, no thread is ever started.
                var executor = new CountingExecutor(new AnonymousExecutor(_ => Interlocked.Increment(ref threadStarts)));
                Volatile.Write(ref executorRef, executor);
                executor.execute(Runnables.Empty);
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
    public void testNotSuspendedUntilScheduledTaskIsCancelled()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        try
        {
            // Schedule a task which is so far in the future that we are sure it will not run at all.
            var future = (IFuture<Void>)executor.schedule(Runnables.Empty, TimeSpan.FromDays(1));
            var currentThread = factory.take();
            // Let's wait until the thread is started
            currentThread.awaitStarted();
            currentThread.awaitRunnableExecution();
            Assert.True(executor.trySuspend());

            // Now cancel the task which should allow the suspension to let the thread die once we call trySuspend() again
            Assert.True(future.cancel(false));
            Assert.True(future.await(TimeSpan.FromSeconds(5)));

            // Call in a loop as removal of scheduled tasks from task queue might be lazy
            suspend(executor);
            currentThread.join();

            // Should be suspended now, we should be able to also call trySuspend() again.
            Assert.True(executor.trySuspend());
            Assert.True(executor.isSuspended());

            shutdown(executor);
            Assert.False(executor.isSuspended());
            Assert.True(executor.isShutdown());

            // Guarantee that al tasks were able to die...
            factory.drain();
        }
        finally { shutdown(executor); }
    }

    [Fact]
    public void testSuspendDoesNotRaceWithCancelledScheduledTask()
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
                var future = (IFuture<Void>)executor.schedule(Runnables.Empty, TimeSpan.FromDays(1));
                var currentThread = factory.take();
                currentThread.awaitStarted();
                currentThread.awaitRunnableExecution();
                Assert.True(executor.trySuspend(), "iteration " + i);

                Assert.True(future.cancel(false), "iteration " + i);
                Assert.True(future.await(TimeSpan.FromSeconds(5)));
                suspend(executor, 1);
                Assert.True(currentThread.thread.Join(TimeSpan.FromSeconds(2)), "worker did not terminate: " + i);
                Assert.False(currentThread.thread.IsAlive);
                Assert.True(executor.trySuspend(), "iteration " + i);
                Assert.True(executor.isSuspended(), "iteration " + i);

                shutdown(executor);
                factory.drain();
            }
            finally { shutdown(executor); }
        }
    }

    [Fact]
    public void testNotSuspendedUntilScheduledTaskDidRun()
    {
        var factory = new TestThreadFactory();
        var executor = new SuspendingSingleThreadEventExecutor(factory);
        using var latch = new CountdownEvent(1);
        try
        {
            // Schedule a task which is so far in the future that we are sure it will not run at all.
            var future = (IFuture<Void>)executor.schedule(Runnables.Create(() =>
            {
                try { latch.Wait(); }
                catch (ThreadInterruptedException) { /* ignore */ }
                // ignore
            }), TimeSpan.FromMilliseconds(100));
            var currentThread = factory.take();
            // Let's wait until the thread is started
            currentThread.awaitStarted();
            currentThread.awaitRunnableExecution();
            latch.Signal();
            Assert.True(executor.trySuspend());

            // Now wait till the scheduled task was run
            sync(future);
            currentThread.join();

            // Should be suspended now, we should be able to also call trySuspend() again.
            Assert.True(executor.trySuspend());
            Assert.True(executor.isSuspended());

            shutdown(executor);
            Assert.False(executor.isSuspended());
            Assert.True(executor.isShutdown());

            // Guarantee that al tasks were able to die...
            factory.drain();
        }
        finally
        {
            if (!latch.IsSet) latch.Signal();
            shutdown(executor);
        }
    }

    private sealed class ClosedExecutor : IExecutor
    {
        public void execute(IRunnable command) => throw new RejectedExecutionException();
    }
    [Fact]
    public void testWrappedExecutorIsShutdown()
    {
        // CLR: a closed backing executor reproduces ExecutorService.shutdownNow rejection.
        var executor = new LoopExecutor(new ClosedExecutor(), false);
        executeShouldFail(executor);
        executeShouldFail(executor);
        Assert.Throws<RejectedExecutionException>(() =>
            executor.shutdownGracefullyAsync().GetAwaiter().GetResult());
        Assert.True(executor.isShutdown());
    }
    private static void executeShouldFail(IExecutor executor) =>
        Assert.Throws<RejectedExecutionException>(() => executor.execute(Runnables.Create(() =>
        {
            // Noop.
        })));

    [Fact]
    public void testThreadProperties()
    {
        Thread threadRef = null;
        var executor = new LoopExecutor(new DefaultThreadFactory("test"), false);
        executor.started = () => threadRef = Thread.CurrentThread;
        try
        {
            var properties = executor.threadProperties();
            Assert.Equal(threadRef.ManagedThreadId, properties.id());
            Assert.Equal(threadRef.Name, properties.name());
            Assert.Equal(threadRef.Priority, properties.priority());
            Assert.Equal(threadRef.IsAlive, properties.isAlive());
            Assert.Equal(threadRef.IsBackground, properties.isDaemon());
            // CLR cannot inspect another managed thread's stack. Verify native owner
            // stack capture in its loop and explicit failure of a foreign query.
            Assert.Throws<NotSupportedException>(() => properties.stackTrace());
            var trace = executor.submit(new AnonymousCallable<StackFrame[]>(properties.stackTrace));
            Assert.NotEmpty(trace.get(TimeSpan.FromSeconds(5)));
        }
        finally { shutdown(executor); }
    }

    [Fact] public void testInvokeAnyInEventLoop() => testInvokeInEventLoop(true, false);
    [Fact] public void testInvokeAnyInEventLoopWithTimeout() => testInvokeInEventLoop(true, true);
    [Fact] public void testInvokeAllInEventLoop() => testInvokeInEventLoop(false, false);
    [Fact] public void testInvokeAllInEventLoopWithTimeout() => testInvokeInEventLoop(false, true);
    private static void testInvokeInEventLoop(bool any, bool timeout)
    {
        var executor = new LoopExecutor(new DefaultThreadFactory("invoke"));
        try
        {
            var promise = executor.newPromise<Void>();
            executor.execute(Runnables.Create(() =>
            {
                try
                {
                    ICallable<bool>[] set = { new AnonymousCallable<bool>(() =>
                    {
                        promise.setFailure(new Exception("Should never execute the Callable"));
                        return true;
                    }) };
                    if (any)
                    {
                        if (timeout) executor.invokeAny(set, TimeSpan.FromSeconds(10));
                        else executor.invokeAny(set);
                    }
                    else
                    {
                        if (timeout) executor.invokeAll(set, TimeSpan.FromSeconds(10));
                        else executor.invokeAll(set);
                    }
                    promise.setFailure(new Exception("Should never reach here"));
                }
                catch (Exception cause) { promise.setFailure(cause); }
            }));
            Assert.True(promise.await(TimeSpan.FromSeconds(3)));
            Assert.Throws<RejectedExecutionException>(() => promise.syncUninterruptibly());
        }
        finally { shutdown(executor); }
    }

    private sealed class LazyExecutor : LoopExecutor
    {
        internal LazyExecutor() : base(new DefaultThreadFactory("lazy"), false) { }
        protected override bool wakesUpForTask(IRunnable task) => task is not LazyLatchTask;
        protected override void run()
        {
            while (!confirmShutdown())
            {
                lock (this) { if (!hasTasks()) Monitor.Wait(this); }
                runAllTasks();
            }
        }
        public override void wakeup(bool inEventLoop)
        {
            if (!inEventLoop) { lock (this) Monitor.PulseAll(this); }
        }
    }
    [Fact]
    public void testLazyExecution()
    {
        var executor = new LazyExecutor();
        try
        {
            // Ensure event loop is started
            var latch0 = new LatchTask();
            executor.execute(latch0);
            Assert.True(latch0.latch.Wait(100));
            // Pause to ensure it enters waiting state
            Thread.Sleep(100);

            // Submit task via lazyExecute
            var latch1 = new LatchTask();
            executor.lazyExecute(latch1);
            // Sumbit lazy task via regular execute
            var latch2 = new LazyLatchTask();
            executor.execute(latch2);

            // Neither should run yet
            Assert.False(latch1.latch.Wait(100));
            Assert.False(latch2.latch.Wait(100));

            // Submit regular task via regular execute
            var latch3 = new LatchTask();
            executor.execute(latch3);

            // Should flush latch1 and latch2 and then run latch3 immediately
            Assert.True(latch3.latch.Wait(100));
            Assert.Equal(0, latch1.latch.CurrentCount);
            Assert.Equal(0, latch2.latch.CurrentCount);
        }
        finally { shutdown(executor); }
    }

    private sealed class NoRemoveQueue : LinkedBlockingQueue<IRunnable>
    {
        internal NoRemoveQueue() : base(int.MaxValue) { }
        public override bool tryRemove(IRunnable task) => throw new NotSupportedException();
    }
    private sealed class AfterShutdownExecutor : LoopExecutor
    {
        internal int attempts;
        internal int rejects;
        internal readonly ConcurrentQueue<IFuture<Void>> submittedTasks = new();
        internal AfterShutdownExecutor(IQueue<IRunnable> queue)
            : base(new ThreadPerTaskExecutor(new DefaultThreadFactory("after-shutdown")), queue) { }
        protected override bool confirmShutdown()
        {
            bool result = base.confirmShutdown();
            // After shutdown is confirmed, scheduled one more task and record it
            if (result)
            {
                ++attempts;
                try { submittedTasks.Enqueue(submit(Runnables.Empty)); }
                catch (RejectedExecutionException)
                {
                    // ignore, tasks are either accepted or rejected
                    ++rejects;
                }
            }
            return result;
        }
    }
    [Fact]
    public void testTaskAddedAfterShutdownNotAbandoned()
    {
        // A queue that doesn't support remove, so tasks once added cannot be rejected anymore
        var taskQueue = new NoRemoveQueue();
        var executor = new AfterShutdownExecutor(taskQueue);
        try
        {
            // Start the loop
            sync(executor.submit(Runnables.Empty));
            // Shutdown without any quiet period
            executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(100))
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            // Ensure there are no user-tasks left.
            Assert.Equal(0, executor.drainTasks());
            // Verify that queue is empty and all attempts either succeeded or were rejected
            Assert.True(taskQueue.isEmpty());
            Assert.True(executor.attempts > 0);
            Assert.Equal(executor.attempts, executor.submittedTasks.Count + executor.rejects);
            foreach (IFuture<Void> future in executor.submittedTasks) Assert.True(future.isSuccess());
        }
        finally { shutdown(executor); }
    }

    private sealed class TestRunnable : IRunnable
    {
        internal volatile bool ran;
        public void run() => ran = true;
    }
    [Fact]
    public void testTakeTask()
    {
        var executor = new LoopExecutor(new DefaultThreadFactory("take"));
        try
        {
            //add task
            var beforeTask = new TestRunnable();
            executor.execute(beforeTask);
            //add scheduled task
            var scheduledTask = new TestRunnable();
            var f = (IFuture<Void>)executor.schedule(scheduledTask, TimeSpan.FromMilliseconds(1500));
            //add task
            var afterTask = new TestRunnable();
            executor.execute(afterTask);
            sync(f);
            Assert.True(beforeTask.ran);
            Assert.True(scheduledTask.ran);
            Assert.True(afterTask.ran);
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void testTakeTaskAlwaysHasTask()
    {
        //for https://github.com/netty/netty/issues/1614
        var executor = new LoopExecutor(new DefaultThreadFactory("busy-take"));
        try
        {
            //add scheduled task
            var t = new TestRunnable();
            var f = (IFuture<Void>)executor.schedule(t, TimeSpan.FromMilliseconds(1500));
            //ensure always has at least one task in taskQueue
            //check if scheduled tasks are triggered
            IRunnable repeat = null;
            repeat = Runnables.Create(() => { if (!f.isDone()) executor.execute(repeat); });
            executor.execute(repeat);
            sync(f);
            Assert.True(t.ran);
        }
        finally { shutdown(executor); }
    }
    private sealed class ThrowingExecutor : LoopExecutor
    {
        private readonly Exception exception;
        internal ThrowingExecutor(Exception exception) : base(new DefaultThreadFactory("throwing")) => this.exception = exception;
        protected override void run() => throw exception;
    }
    [Fact]
    public void testExceptionIsPropagatedToTerminationFuture()
    {
        var exception = new InvalidOperationException();
        var executor = new ThrowingExecutor(exception);
        // Schedule something so we are sure the run() method will be called.
        executor.execute(Runnables.Create(() =>
        {
            // Noop.
        }));
        var future = executor.terminationFuture();
        Assert.True(future.await(TimeSpan.FromSeconds(5)));
        Assert.Same(exception, future.cause());
        var task = future.Task;
        Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
            task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
        Assert.Same(exception, task.Exception.InnerException);
    }
}
