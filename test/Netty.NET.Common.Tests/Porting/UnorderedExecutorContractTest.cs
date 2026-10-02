using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedExecutorContractTest
{
    private sealed class Factory : IThreadFactory
    {
        internal readonly ConcurrentBag<Thread> threads = new();
        public Thread newThread(IRunnable task)
        {
            var thread = new Thread(task.run) { IsBackground = true };
            threads.Add(thread);
            return thread;
        }
    }

    private sealed class LambdaFactory : IThreadFactory
    {
        private readonly Func<IRunnable, Thread> create;
        internal LambdaFactory(Func<IRunnable, Thread> create) => this.create = create;
        public Thread newThread(IRunnable task) => create(task);
    }

    private static void stop(UnorderedThreadPoolEventExecutor executor)
    {
        executor.shutdownNow();
        Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void IndependentWorkersOverlapAndTrackAllExecutorThreads()
    {
        var factory = new Factory();
        var executor = new UnorderedThreadPoolEventExecutor(2, factory);
        using var entered = new CountdownEvent(2);
        using var release = new CountdownEvent(1);
        try
        {
            Assert.Same(executor, executor.parent());
            Assert.Same(executor, executor.next());
            Assert.Same(executor, Assert.Single(executor.iterator()));
            Assert.False(executor.inEventLoop());
            var futures = Enumerable.Range(0, 2).Select(_ => executor.submit(new AnonymousCallable<Thread>(() =>
            {
                Assert.True(executor.inEventLoop());
                entered.Signal();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return Thread.CurrentThread;
            }))).ToArray();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, factory.threads.Count);
            Assert.All(factory.threads, thread => Assert.True(executor.inEventLoop(thread)));
            release.Signal();
            Assert.NotSame(futures[0].get(TimeSpan.FromSeconds(5)), futures[1].get(TimeSpan.FromSeconds(5)));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
        Assert.True(SpinWait.SpinUntil(() => factory.threads.All(thread => !executor.inEventLoop(thread)), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ShutdownFutureCompletesAtRequestWhileActiveWorkStillRuns()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var work = executor.submit(new AnonymousCallable<int>(() => { entered.Signal(); release.Wait(); return 17; }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var future = executor.shutdownGracefully(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(-2));
            Assert.Same(executor.terminationFuture(), future);
            Assert.True(future.isSuccess());
            Assert.Null(future.get());
            Assert.True(executor.isShutdown());
            Assert.True(executor.isShuttingDown());
            Assert.False(executor.isTerminated());
            Assert.False(executor.awaitTermination(TimeSpan.FromMilliseconds(1)));
            Assert.Throws<RejectedExecutionException>(() => executor.execute(Runnables.Empty));
            release.Signal();
            Assert.Equal(17, work.get(TimeSpan.FromSeconds(5)));
            Assert.True(executor.awaitTermination(TimeSpan.MaxValue));
            Assert.True(executor.awaitTermination(TimeSpan.MinValue));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ShutdownKeepsDelayedOneShotWorkAndCancelsPeriodicWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var delayed = executor.schedule(new AnonymousCallable<int>(() => 42), TimeSpan.FromMilliseconds(100));
            var periodic = executor.scheduleAtFixedRate(Runnables.Empty, TimeSpan.FromDays(1), TimeSpan.FromSeconds(1));
            executor.shutdown();
            Assert.True(periodic.isCancelled());
            Assert.False(delayed.isDone());
            Assert.Equal(42, delayed.get(TimeSpan.FromSeconds(5)));
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void ShutdownNowReturnsQueuedFuturesAndInterruptsRunningWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        try
        {
            var running = executor.submit(new AnonymousCallable<bool>(() =>
            {
                entered.Signal();
                try { Thread.Sleep(Timeout.Infinite); return false; }
                catch (ThreadInterruptedException) { return true; }
            }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var delayed = executor.schedule(new AnonymousCallable<int>(() => 1), TimeSpan.FromDays(1));
            Assert.Same(delayed, Assert.Single(executor.shutdownNow()));
            Assert.True(running.get(TimeSpan.FromSeconds(5)));
            Assert.False(delayed.isDone());
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            delayed.cancel(false);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void CallableResultsAndFailuresSurviveDecorationForClrValueTypes()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Assert.Equal(123, executor.submit(new AnonymousCallable<int>(() => 123)).get(TimeSpan.FromSeconds(5)));
            Assert.Equal(456, executor.submit(Runnables.Empty, 456).get(TimeSpan.FromSeconds(5)));
            var expected = new InvalidOperationException("explicit-result runnable");
            var failure = executor.submit(Runnables.Create(() => throw expected), 456);
            Assert.True(failure.await(TimeSpan.FromSeconds(5)));
            Assert.Same(expected, failure.cause());
            Assert.Same(expected, Assert.Throws<AggregateException>(() => failure.get()).InnerException);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void RunnableDecorationRetainsThePinnedBackendFailureSemantics()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            // Netty's wasCallable=false wrapper does not query JDK FutureTask.get().
            // The backend captures a Runnable failure; the outer one-shot promise still succeeds.
            var future = executor.submit(Runnables.Create(() => throw new InvalidOperationException("captured by backend")));
            Assert.Null(future.get(TimeSpan.FromSeconds(5)));
            Assert.True(future.isSuccess());
            Assert.Null(future.cause());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void PeriodicBackendFailureStopsRepetitionWithoutCompletingOuterPromise()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        int calls = 0;
        try
        {
            // JDK FutureTask.runAndReset captures the failure. Pinned Netty's outer periodic promise remains pending.
            var future = executor.scheduleAtFixedRate(Runnables.Create(() =>
            {
                Interlocked.Increment(ref calls);
                entered.Signal();
                throw new InvalidOperationException("periodic backend");
            }), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(((IFuture<Void>)future).await(TimeSpan.FromMilliseconds(30)));
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.True(executor.getQueue().isEmpty());
            Assert.True(future.cancel(false));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void CancelledOneShotRetainsQueueEntryUntilItsDeadlineOrQueueRemoval()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var future = executor.schedule(new AnonymousCallable<int>(() => throw new InvalidOperationException()), TimeSpan.FromDays(1));
            Assert.True(future.cancel(true));
            Assert.Equal(1, executor.getQueue().Count);
            Assert.Same(future, peek(executor));
            Assert.True(executor.getQueue().tryRemove(future));
            Assert.True(executor.getQueue().isEmpty());
            Assert.ThrowsAny<OperationCanceledException>(() => future.get());
        }
        finally { stop(executor); }
    }

    private static IRunnable peek(UnorderedThreadPoolEventExecutor executor)
    {
        Assert.True(executor.getQueue().tryPeek(out var task));
        return task;
    }

    [Fact]
    public void WorkerPromisesDetectBlockingOnTheirOwnPool()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            executor.submit(Runnables.Create(() =>
            {
                Assert.Throws<BlockingOperationException>(() => executor.newPromise<int>().await(TimeSpan.FromSeconds(1)));
                Assert.Throws<BlockingOperationException>(() => executor.newProgressivePromise<int>().await(TimeSpan.FromSeconds(1)));
            }), true).get(TimeSpan.FromSeconds(5));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void ZeroCorePoolRetiresIdleWorkerAndStartsAnotherForNewWork()
    {
        var factory = new Factory();
        var executor = new UnorderedThreadPoolEventExecutor(0, factory);
        try
        {
            var first = executor.submit(new AnonymousCallable<Thread>(() => Thread.CurrentThread)).get(TimeSpan.FromSeconds(5));
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            var second = executor.submit(new AnonymousCallable<Thread>(() => Thread.CurrentThread)).get(TimeSpan.FromSeconds(5));
            Assert.NotSame(first, second);
            Assert.Equal(2, factory.threads.Count);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void CustomRejectionHandlerReceivesScheduledDecoration()
    {
        IRunnable rejected = null;
        UnorderedThreadPoolEventExecutor observed = null;
        var executor = new UnorderedThreadPoolEventExecutor(1, (task, owner) => { rejected = task; observed = owner; });
        stop(executor);
        var future = executor.submit(new AnonymousCallable<int>(() => 99));
        Assert.Same(future, rejected);
        Assert.Same(executor, observed);
        Assert.False(future.isDone());
        future.cancel(false);
    }

    [Fact]
    public void LargeDelaySaturatesAndNonpositiveInitialDelayRunsImmediately()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var delayed = executor.schedule(new AnonymousCallable<int>(() => 1), TimeSpan.MaxValue);
            Assert.True(delayed.delayNanos() > 1_000_000_000);
            Assert.False(executor.getQueue().tryDequeue(out _));
            Assert.Equal(2, executor.schedule(new AnonymousCallable<int>(() => 2), TimeSpan.MinValue).get(TimeSpan.FromSeconds(5)));
            Assert.True(delayed.delayNanos() > long.MaxValue / 2);
            delayed.cancel(false);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void LongFixedRatePeriodDoesNotBlockOrdinaryWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        try
        {
            var periodic = executor.scheduleAtFixedRate(Runnables.Create(() => entered.Signal()), TimeSpan.Zero, TimeSpan.MaxValue);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => executor.getQueue().Count == 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(42, executor.submit(new AnonymousCallable<int>(() => 42)).get(TimeSpan.FromSeconds(5)));
            Assert.True(periodic.delayNanos() > 1_000_000_000);
            periodic.cancel(false);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void TimedInvokeAllInterruptsAlreadyRunningCallable()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var interrupted = new CountdownEvent(1);
        try
        {
            var tasks = new ICallable<int>[] { new AnonymousCallable<int>(() =>
            {
                entered.Signal();
                try { Thread.Sleep(Timeout.Infinite); }
                catch (ThreadInterruptedException) { interrupted.Signal(); }
                return 7;
            }) };
            var futures = executor.invokeAll(tasks, TimeSpan.FromMilliseconds(200));
            Assert.True(entered.IsSet);
            Assert.True(Assert.Single(futures).isCancelled());
            Assert.True(interrupted.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void InvokeAnyInterruptsTheOtherRunningCallableAfterSuccess()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var entered = new CountdownEvent(1);
        using var interrupted = new CountdownEvent(1);
        try
        {
            var tasks = new ICallable<int>[]
            {
                new AnonymousCallable<int>(() =>
                {
                    entered.Signal();
                    try { Thread.Sleep(Timeout.Infinite); }
                    catch (ThreadInterruptedException) { interrupted.Signal(); }
                    return -1;
                }),
                new AnonymousCallable<int>(() => { Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); return 42; })
            };
            Assert.Equal(42, executor.invokeAny(tasks, TimeSpan.FromSeconds(5)));
            Assert.True(interrupted.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void BulkInvocationUsesJdkWaitingWithoutNettyDeadlockCheck()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var outer = executor.submit(new AnonymousCallable<bool>(() =>
            {
                var futures = executor.invokeAll(new ICallable<int>[] { new AnonymousCallable<int>(() => 7) },
                    TimeSpan.FromMilliseconds(10));
                return Assert.Single(futures).isCancelled();
            }));
            Assert.True(outer.get(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void ContinuedPeriodicPolicyRunsAfterShutdownAndCanBeDisabled()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var continued = new CountdownEvent(3);
        using var release = new CountdownEvent(1);
        try
        {
            executor.setContinueExistingPeriodicTasksAfterShutdownPolicy(true);
            var future = executor.scheduleAtFixedRate(Runnables.Create(() =>
            {
                if (executor.isShutdown() && !continued.IsSet)
                {
                    continued.Signal();
                    if (continued.IsSet) release.Wait();
                }
            }), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(5));
            Assert.True(executor.shutdownGracefully().isSuccess());
            Assert.True(continued.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(executor.isTerminated());
            executor.setContinueExistingPeriodicTasksAfterShutdownPolicy(false);
            release.Signal();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.True(future.isCancelled());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ADequeuedPeriodicTaskCanCancelItsBackendWhileTheOuterPromiseStaysPending()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        int calls = 0;
        try
        {
            executor.setContinueExistingPeriodicTasksAfterShutdownPolicy(true);
            var future = executor.scheduleAtFixedRate(Runnables.Create(() => ++calls),
                TimeSpan.FromDays(1), TimeSpan.FromDays(1));
            Assert.True(executor.remove((IRunnable)future));
            executor.shutdown();
            executor.setContinueExistingPeriodicTasksAfterShutdownPolicy(false);
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            // Model the task claimed before the policy changed: JDK run cancels the
            // inner FutureTask, and Netty's Runnable decorator never observes it.
            ((IRunnable)future).run();
            Assert.Equal(0, calls);
            Assert.False(future.isDone());
            Assert.False(future.isCancelled());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void DisablingDelayedPolicyDropsFutureWorkButKeepsAlreadyDueWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var running = executor.submit(new AnonymousCallable<int>(() => { entered.Signal(); release.Wait(); return 1; }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var due = executor.submit(new AnonymousCallable<int>(() => 2));
            var future = executor.schedule(new AnonymousCallable<int>(() => 3), TimeSpan.FromDays(1));
            executor.setExecuteExistingDelayedTasksAfterShutdownPolicy(false);
            executor.shutdown();
            Assert.True(future.isCancelled());
            Assert.False(due.isDone());
            release.Signal();
            Assert.Equal(1, running.get(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, due.get(TimeSpan.FromSeconds(5)));
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void DelayedPolicyCanChangeAfterShutdownHasAlreadyStarted()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var future = executor.schedule(new AnonymousCallable<int>(() => 3), TimeSpan.FromDays(1));
            executor.shutdown();
            Assert.False(future.isCancelled());
            Assert.True(executor.isTerminating());
            executor.setExecuteExistingDelayedTasksAfterShutdownPolicy(false);
            Assert.True(future.isCancelled());
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.False(executor.isTerminating());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void RemoveOnCancelAffectsRawBackendButNotDecoratedNettyFuture()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var running = executor.submit(new AnonymousCallable<int>(() => { entered.Signal(); release.Wait(); return 1; }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            executor.setRemoveOnCancelPolicy(true);
            int executions = 0;
            executor.execute(Runnables.Create(() => ++executions));
            var raw = Assert.IsAssignableFrom<IFuture<Void>>(peek(executor));
            Assert.True(raw.cancel(false));
            Assert.True(executor.getQueue().isEmpty());
            var decorated = executor.schedule(new AnonymousCallable<int>(() => 7), TimeSpan.FromDays(1));
            Assert.True(decorated.cancel(false));
            Assert.Same(decorated, peek(executor));
            executor.purge();
            Assert.True(executor.getQueue().isEmpty());
            release.Signal();
            Assert.Equal(1, running.get(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, executions);
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void CoreResizeRetiresIdleWorkersWithoutInterruptingActiveWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var entered = new CountdownEvent(2);
        using var release = new CountdownEvent(1);
        int interrupted = 0;
        try
        {
            Assert.Equal(2, executor.prestartAllCoreThreads());
            Assert.False(executor.prestartCoreThread());
            var futures = Enumerable.Range(0, 2).Select(_ => executor.submit(new AnonymousCallable<int>(() =>
            {
                entered.Signal();
                try { release.Wait(); }
                catch (ThreadInterruptedException) { Interlocked.Increment(ref interrupted); throw; }
                return 7;
            }))).ToArray();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            executor.setCorePoolSize(1);
            Assert.Equal(2, executor.getActiveCount());
            release.Signal();
            Assert.All(futures, future => Assert.Equal(7, future.get(TimeSpan.FromSeconds(5))));
            Assert.True(SpinWait.SpinUntil(() => executor.getPoolSize() == 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(0, interrupted);
            executor.setCorePoolSize(3);
            Assert.Equal(2, executor.prestartAllCoreThreads());
            Assert.Equal(3, executor.getPoolSize());
            Assert.Equal(3, executor.getLargestPoolSize());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void CoreTimeoutRetiresIdleWorkersAndKeepsOneForDelayedWork()
    {
        var factory = new Factory();
        var executor = new UnorderedThreadPoolEventExecutor(2, factory);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            executor.setKeepAliveTime(TimeSpan.FromMilliseconds(5));
            executor.allowCoreThreadTimeOut(true);
            Assert.Equal(2, executor.prestartAllCoreThreads());
            Assert.True(SpinWait.SpinUntil(() => executor.getPoolSize() == 0, TimeSpan.FromSeconds(5)));
            var delayed = executor.schedule(new AnonymousCallable<int>(() => { entered.Signal(); release.Wait(); return 7; }),
                TimeSpan.FromMilliseconds(50));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(3, factory.threads.Count);
            Assert.Equal(1, executor.getPoolSize());
            release.Signal();
            Assert.Equal(7, delayed.get(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => executor.getPoolSize() == 0, TimeSpan.FromSeconds(5)));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ZeroKeepAliveDoesNotHoldTheQueueLockWhileDelayedWorkRemains()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        try
        {
            executor.setKeepAliveTime(TimeSpan.Zero);
            var delayed = executor.schedule(new AnonymousCallable<int>(() => 1), TimeSpan.FromDays(1));
            Assert.Equal(7, executor.submit(new AnonymousCallable<int>(() => 7)).get(TimeSpan.FromSeconds(5)));
            delayed.cancel(false);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeQueueCanRemoveAndReinsertScheduledFutureBeforeItExecutes()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var running = executor.submit(new AnonymousCallable<int>(() => { entered.Signal(); release.Wait(); return 1; }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var queued = executor.schedule(new AnonymousCallable<int>(() => 7), TimeSpan.Zero);
            Assert.True(executor.remove(queued));
            Assert.True(executor.getQueue().isEmpty());
            Assert.True(executor.getQueue().tryEnqueue(queued));
            Assert.Same(queued, peek(executor));
            release.Signal();
            Assert.Equal(1, running.get(TimeSpan.FromSeconds(5)));
            Assert.Equal(7, queued.get(TimeSpan.FromSeconds(5)));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ReplacingFactoryUsesTheInheritedSetterWithoutReapplyingAccounting()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        try
        {
            var first = executor.submit(new AnonymousCallable<Thread>(() =>
            {
                Assert.True(executor.inEventLoop());
                return Thread.CurrentThread;
            })).get(TimeSpan.FromSeconds(5));
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            var replacement = new Factory();
            executor.setThreadFactory(replacement);
            Assert.Same(replacement, executor.getThreadFactory());
            // Pinned Netty wraps only the factory installed by its constructor.
            Assert.False(executor.submit(new AnonymousCallable<bool>(() => executor.inEventLoop())).get(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void CompletedTaskStatisticsIncludeRawExecuteAndScheduledSubmissions()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var entered = new CountdownEvent(2);
        using var release = new CountdownEvent(1);
        try
        {
            executor.execute(Runnables.Create(() => { entered.Signal(); release.Wait(); }));
            var submitted = executor.submit(new AnonymousCallable<int>(() => { entered.Signal(); release.Wait(); return 7; }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var queued = executor.submit(new AnonymousCallable<int>(() => 8));
            Assert.Equal(2, executor.getActiveCount());
            Assert.Equal(3L, executor.getTaskCount());
            Assert.Equal(0L, executor.getCompletedTaskCount());
            release.Signal();
            Assert.Equal(7, submitted.get(TimeSpan.FromSeconds(5)));
            Assert.Equal(8, queued.get(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => executor.getCompletedTaskCount() == 3, TimeSpan.FromSeconds(5)));
            Assert.Equal(3L, executor.getTaskCount());
            Assert.Equal(0, executor.getActiveCount());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void RunningBulkFutureCanCancelWithoutInterruptingItsCallable()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        bool interrupted = false;
        try
        {
            var future = new JdkFutureTask<int>(executor, new AnonymousCallable<int>(() =>
            {
                entered.Signal();
                try { release.Wait(); }
                catch (ThreadInterruptedException) { interrupted = true; }
                return 7;
            }));
            executor.execute(future);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(future.isCancellable());
            Assert.True(future.cancel(false));
            release.Signal();
            Assert.Equal(8, executor.submit(new AnonymousCallable<int>(() => 8)).get(TimeSpan.FromSeconds(5)));
            Assert.False(interrupted);
            Assert.True(future.isCancelled());
            Assert.ThrowsAny<OperationCanceledException>(() => future.get());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void RunningBulkCancellationDoesNotLeakAnInterruptIntoNextWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            for (int i = 0; i < 256; ++i)
            {
                using var entered = new CountdownEvent(1);
                using var release = new CountdownEvent(1);
                var future = new JdkFutureTask<int>(executor, new AnonymousCallable<int>(() =>
                {
                    entered.Signal();
                    while (!release.IsSet) Thread.SpinWait(64);
                    return 7;
                }));
                executor.execute(future);
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    Assert.True(future.cancel(true));
                }
                finally { release.Signal(); }
                var next = executor.submit(new AnonymousCallable<int>(() => { Thread.Sleep(0); return 8; }));
                Assert.Equal(8, next.get(TimeSpan.FromSeconds(5)));
            }
        }
        finally { stop(executor); }
    }

    [Fact]
    public void FactoryReservationPreventsRecursiveStartWhenFactorySubmitsWork()
    {
        UnorderedThreadPoolEventExecutor executor = null;
        int creations = 0;
        int reentrantExecutions = 0;
        var factory = new LambdaFactory(task =>
        {
            Assert.Equal(1, ++creations);
            executor.execute(Runnables.Create(() => ++reentrantExecutions));
            return new Thread(task.run) { IsBackground = true };
        });
        executor = new UnorderedThreadPoolEventExecutor(1, factory);
        try
        {
            Assert.Equal(7, executor.submit(new AnonymousCallable<int>(() => 7)).get(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => executor.getCompletedTaskCount() == 2, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, reentrantExecutions);
            Assert.Equal(1, creations);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void FactoryFailureReleasesReservationAndRetainsAcceptedQueueWork()
    {
        int attempts = 0;
        int executions = 0;
        var expected = new InvalidOperationException("factory failed");
        var factory = new LambdaFactory(task =>
        {
            if (++attempts == 1) throw expected;
            return new Thread(task.run) { IsBackground = true };
        });
        var executor = new UnorderedThreadPoolEventExecutor(1, factory);
        try
        {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => executor.submit(new AnonymousCallable<int>(() => ++executions))));
            Assert.Equal(0, executor.getPoolSize());
            var accepted = Assert.IsAssignableFrom<IFuture<int>>(peek(executor));
            Assert.False(accepted.isDone());
            Assert.Equal(7, executor.submit(new AnonymousCallable<int>(() => 7)).get(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, accepted.get(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, attempts);
            Assert.Equal(1, executions);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NullFactoryResultCanRecoverAfterShutdownWhenDelayedWorkRemains()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new LambdaFactory(_ => null));
        try
        {
            var future = executor.submit(new AnonymousCallable<int>(() => 7));
            Assert.Equal(0, executor.getPoolSize());
            Assert.False(future.isDone());
            executor.shutdown();
            Assert.False(executor.awaitTermination(TimeSpan.FromMilliseconds(1)));
            executor.setThreadFactory(new Factory());
            Assert.True(executor.prestartCoreThread());
            Assert.Equal(7, future.get(TimeSpan.FromSeconds(5)));
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void InvalidConfigurationPreservesTheExistingPoolSettings()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.setCorePoolSize(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.setMaximumPoolSize(1));
            executor.setMaximumPoolSize(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.setCorePoolSize(4));
            Assert.Throws<ArgumentException>(() => executor.setKeepAliveTime(TimeSpan.FromTicks(-1)));
            executor.setKeepAliveTime(TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => executor.allowCoreThreadTimeOut(true));
            executor.setKeepAliveTime(TimeSpan.FromSeconds(1));
            executor.allowCoreThreadTimeOut(true);
            Assert.Throws<ArgumentException>(() => executor.setKeepAliveTime(TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => executor.setThreadFactory(null));
            Assert.Throws<ArgumentNullException>(() => executor.setRejectedExecutionHandler(null));
            Assert.Equal(2, executor.getCorePoolSize());
            Assert.Equal(3, executor.getMaximumPoolSize());
            Assert.Equal(TimeSpan.FromSeconds(1), executor.getKeepAliveTime());
            Assert.True(executor.allowsCoreThreadTimeOut());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NullTasksAndNonpositivePeriodsAreRejectedBeforeEnqueue()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Assert.Throws<ArgumentNullException>(() => executor.submit((IRunnable)null));
            Assert.Throws<ArgumentNullException>(() => executor.submit((ICallable<int>)null));
            Assert.Throws<ArgumentNullException>(() => executor.execute(null));
            Assert.Throws<ArgumentException>(() => executor.scheduleAtFixedRate(Runnables.Empty, TimeSpan.Zero, TimeSpan.Zero));
            Assert.Throws<ArgumentException>(() => executor.scheduleWithFixedDelay(Runnables.Empty, TimeSpan.Zero, TimeSpan.FromTicks(-1)));
            Assert.True(executor.getQueue().isEmpty());
        }
        finally { stop(executor); }
    }
}
