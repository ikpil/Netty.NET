using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

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
            var futures = Enumerable.Range(0, 2).Select(_ => executor.SubmitAsync<Thread>(() =>
            {
                Assert.True(executor.inEventLoop());
                entered.Signal();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return Thread.CurrentThread;
            })).ToArray();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, factory.threads.Count);
            Assert.All(factory.threads, thread => Assert.True(executor.inEventLoop(thread)));
            release.Signal();
            Assert.NotSame(futures[0].WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), futures[1].WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
        Assert.True(SpinWait.SpinUntil(() => factory.threads.All(thread => !executor.inEventLoop(thread)), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ShutdownTaskWaitsForAcceptedWorkAfterTheShutdownRequest()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var work = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 17; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var future = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Assert.Same(executor.Termination, future);
            Assert.False(future.IsCompleted);
            Assert.True(executor.isShutdown());
            Assert.True(executor.isShuttingDown());
            Assert.False(executor.isTerminated());
            Assert.False(executor.awaitTermination(TimeSpan.FromMilliseconds(1)));
            Assert.Throws<RejectedExecutionException>(() => executor.execute(Runnables.Empty));
            release.Signal();
            Assert.Equal(17, work.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(executor.awaitTermination(TimeSpan.MaxValue));
            Assert.True(executor.awaitTermination(TimeSpan.MinValue));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ShutdownKeepsDelayedOneShotWorkAndCancelsPeriodicWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            // Keep the one-shot work pending across shutdown independently of
            // machine scheduling; a 100ms deadline can expire before the assertion.
            executor.execute(new AnonymousRunnable(() => { entered.Set(); release.Wait(); }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var delayed = executor.ScheduleAsync(() => 42, TimeSpan.FromMilliseconds(100));
            var periodic = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.FromDays(1), TimeSpan.FromSeconds(1));
            executor.shutdown();
            Assert.True(periodic.IsCanceled);
            Assert.False(delayed.IsCompleted);
            release.Set();
            Assert.Equal(42, delayed.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); stop(executor); }
    }

    [Fact]
    public void ShutdownNowReturnsQueueWorkAndCancelsItsNativeResult()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        try
        {
            var running = executor.SubmitAsync<bool>(() =>
            {
                entered.Signal();
                try { Thread.Sleep(Timeout.Infinite); return false; }
                catch (ThreadInterruptedException) { return true; }
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var delayed = executor.ScheduleAsync(() => 1, TimeSpan.FromDays(1));
            IRunnable reservation = Assert.Single(executor.shutdownNow());
            Assert.IsNotAssignableFrom<System.Threading.Tasks.Task>(reservation);
            reservation.run();
            Assert.True(running.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(delayed.IsCanceled);
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeResultsAndFailuresPreserveClrValueTypes()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Assert.Equal(123, executor.SubmitAsync<int>(() => 123).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(456, executor.SubmitAsync(() => 456).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            var expected = new InvalidOperationException("explicit-result runnable");
            var failure = executor.SubmitAsync(int () => throw expected);
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                failure.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeScheduledActionPublishesItsActualFailure()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var expected = new InvalidOperationException("scheduled action");
            var future = executor.ScheduleAsync((Action)(() => throw expected), TimeSpan.Zero);
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
            Assert.True(future.IsFaulted);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativePeriodicFailureStopsRepetitionAndFaultsTheReservation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        int calls = 0;
        try
        {
            var expected = new InvalidOperationException("periodic callback");
            var future = executor.ScheduleAtFixedRateAsync(() =>
            {
                Interlocked.Increment(ref calls);
                entered.Signal();
                throw expected;
            }, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
            Thread.Sleep(30);
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.True(future.IsFaulted);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeOneShotCancellationRemovesItsQueueEntryImmediately()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            using var cancellation = new CancellationTokenSource();
            var future = executor.ScheduleAsync(int () => throw new InvalidOperationException(),
                TimeSpan.FromDays(1), cancellation.Token);
            Assert.Equal(1, executor.PendingTaskCount);
            cancellation.Cancel();
            Assert.Equal(0, executor.PendingTaskCount);
            cancellation.Cancel();
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.ThrowsAny<OperationCanceledException>(() => future.GetAwaiter().GetResult());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeProgressCanReportInsideItsOwnPoolWithoutCompletingTheSource()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            executor.SubmitAsync(() =>
            {
                var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                long reported = -1;
                using var progress = new ExecutorProgress(executor, source.Task, value => reported = value.Completed);
                progress.Report(new TransferProgress(1, 2));
                Assert.Equal(1, reported);
                Assert.False(source.Task.IsCompleted);
                source.SetResult(2);
            }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
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
            var first = executor.SubmitAsync<Thread>(() => Thread.CurrentThread).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            var second = executor.SubmitAsync<Thread>(() => Thread.CurrentThread).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.NotSame(first, second);
            Assert.Equal(2, factory.threads.Count);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void DiscardingRawRejectionHandlerCannotHideNativeScheduleRejection()
    {
        IRunnable rejected = null;
        UnorderedThreadPoolEventExecutor observed = null;
        var executor = new UnorderedThreadPoolEventExecutor(1, (task, owner) => { rejected = task; observed = owner; });
        stop(executor);
        executor.execute(Runnables.Empty);
        Assert.NotNull(rejected);
        Assert.IsNotAssignableFrom<System.Threading.Tasks.Task>(rejected);
        Assert.Same(executor, observed);
        rejected = null;
        var future = executor.ScheduleAsync(() => 99, TimeSpan.Zero);
        Assert.Throws<RejectedExecutionException>(() =>
            future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        Assert.Null(rejected);
    }

    [Fact]
    public void LargeDelaySaturatesAndNonpositiveInitialDelayRunsImmediately()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var delayed = executor.ScheduleAsync(() => 1, TimeSpan.MaxValue, cancellation.Token);
            Assert.Equal(1, executor.PendingTaskCount);
            Assert.False(delayed.IsCompleted);
            Assert.Equal(2, executor.ScheduleAsync(() => 2, TimeSpan.MinValue).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.False(delayed.IsCompleted);
            cancellation.Cancel();
            Assert.True(delayed.IsCanceled);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void LongFixedRatePeriodDoesNotBlockOrdinaryWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var periodic = executor.ScheduleAtFixedRateAsync(() => entered.Signal(), TimeSpan.Zero, TimeSpan.MaxValue, cancellation.Token);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => executor.PendingTaskCount == 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(42, executor.SubmitAsync<int>(() => 42).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.False(periodic.IsCompleted);
            cancellation.Cancel();
            Assert.True(periodic.IsCanceled);
        }
        finally { stop(executor); }
    }

    [Fact]
    public async Task TimedBatchOwnerCancelsAnAlreadyRunningCooperativeFunction()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var owner = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        Task<int> operation = executor.SubmitAsync(token =>
        {
            entered.Set();
            try
            {
                Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
                token.ThrowIfCancellationRequested();
                return 7;
            }
            finally { finished.Set(); }
        }, owner.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<TimeoutException>(() => Task.WhenAll(operation).WaitAsync(TimeSpan.FromMilliseconds(10)));
            Assert.False(operation.IsCompleted);
            owner.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(finished.IsSet);
            Assert.True(operation.IsCanceled);
        }
        finally { owner.Cancel(); stop(executor); }
    }

    [Fact]
    public async Task FirstCompletionOwnerCancelsTheOtherCooperativeRunningFunction()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var owner = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        Task<int> other = executor.SubmitAsync(token =>
        {
            entered.Set();
            Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
            token.ThrowIfCancellationRequested();
            return -1;
        }, owner.Token);
        Task<int> winner = executor.SubmitAsync(() =>
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            return 42;
        }, owner.Token);
        try
        {
            Task<int> completed = await Task.WhenAny(other, winner).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(winner, completed);
            Assert.Equal(42, await completed);
            Assert.False(other.IsCompleted);
            owner.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(other.IsCanceled);
            Assert.True(winner.IsCompletedSuccessfully);
        }
        finally { owner.Cancel(); stop(executor); }
    }

    [Fact]
    public async Task NativeAsyncBatchYieldsTheOnlyWorkerBeforeQueuedChildRuns()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Task<int> outer = executor.SubmitAsync(async () =>
            {
                Assert.True(executor.inEventLoop());
                Task<int> child = executor.SubmitAsync(() =>
                {
                    Assert.True(executor.inEventLoop());
                    return 7;
                });
                return (await Task.WhenAll(child).ConfigureAwait(false))[0];
            });
            Assert.Equal(7, await outer.WaitAsync(TimeSpan.FromSeconds(5)));
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
            var future = executor.ScheduleAtFixedRateAsync(() =>
            {
                if (executor.isShutdown() && !continued.IsSet)
                {
                    continued.Signal();
                    if (continued.IsSet) release.Wait();
                }
            }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(5));
            var termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Assert.False(termination.IsCompleted);
            Assert.True(continued.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(executor.isTerminated());
            executor.setContinueExistingPeriodicTasksAfterShutdownPolicy(false);
            release.Signal();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.True(future.IsCanceled);
            termination.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void OwnerCancellationPreventsPeriodicReentryAfterShutdown()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        try
        {
            executor.setContinueExistingPeriodicTasksAfterShutdownPolicy(true);
            var future = executor.ScheduleAtFixedRateAsync(() => ++calls,
                TimeSpan.FromDays(1), TimeSpan.FromDays(1), cancellation.Token);
            executor.shutdown();
            Assert.False(future.IsCompleted);
            cancellation.Cancel();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, calls);
            Assert.True(future.IsCanceled);
            Assert.Equal(0, executor.PendingTaskCount);
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
            var running = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 1; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var due = executor.SubmitAsync<int>(() => 2);
            var future = executor.ScheduleAsync(() => 3, TimeSpan.FromDays(1));
            executor.setExecuteExistingDelayedTasksAfterShutdownPolicy(false);
            executor.shutdown();
            Assert.True(future.IsCanceled);
            Assert.False(due.IsCompleted);
            release.Signal();
            Assert.Equal(1, running.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(2, due.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
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
            var future = executor.ScheduleAsync(() => 3, TimeSpan.FromDays(1));
            executor.shutdown();
            Assert.False(future.IsCanceled);
            Assert.True(executor.isTerminating());
            executor.setExecuteExistingDelayedTasksAfterShutdownPolicy(false);
            Assert.True(future.IsCanceled);
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.False(executor.isTerminating());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeOwnerCancellationWithdrawsQueuedWorkWithoutInterruptingItsWorker()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var running = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 1; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            int executions = 0;
            using var submittedCancellation = new CancellationTokenSource();
            Task removed = executor.SubmitAsync(() => ++executions, submittedCancellation.Token);
            submittedCancellation.Cancel();
            Assert.True(removed.IsCanceled);
            Assert.Equal(0, executor.PendingTaskCount);
            using var cancellation = new CancellationTokenSource();
            var scheduled = executor.ScheduleAsync(() => 7, TimeSpan.FromDays(1), cancellation.Token);
            cancellation.Cancel();
            Assert.True(scheduled.IsCanceled);
            Assert.Equal(0, executor.PendingTaskCount);
            release.Signal();
            Assert.Equal(1, running.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
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
            var futures = Enumerable.Range(0, 2).Select(_ => executor.SubmitAsync<int>(() =>
            {
                entered.Signal();
                try { release.Wait(); }
                catch (ThreadInterruptedException) { Interlocked.Increment(ref interrupted); throw; }
                return 7;
            })).ToArray();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            executor.setCorePoolSize(1);
            Assert.Equal(2, executor.getActiveCount());
            release.Signal();
            Assert.All(futures, future => Assert.Equal(7, future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
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
            var delayed = executor.ScheduleAsync(() => { entered.Signal(); release.Wait(); return 7; },
                TimeSpan.FromMilliseconds(50));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(3, factory.threads.Count);
            Assert.Equal(1, executor.getPoolSize());
            release.Signal();
            Assert.Equal(7, delayed.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(SpinWait.SpinUntil(() => executor.getPoolSize() == 0, TimeSpan.FromSeconds(5)));
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ZeroKeepAliveDoesNotHoldTheQueueLockWhileDelayedWorkRemains()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        using var cancellation = new CancellationTokenSource();
        try
        {
            executor.setKeepAliveTime(TimeSpan.Zero);
            var delayed = executor.ScheduleAsync(() => 1, TimeSpan.FromDays(1), cancellation.Token);
            Assert.Equal(7, executor.SubmitAsync<int>(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            cancellation.Cancel();
            Assert.True(delayed.IsCanceled);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void PendingCountTracksCancellationAndAReplacementAdmission()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        try
        {
            var running = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 1; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            using var cancellation = new CancellationTokenSource();
            var removed = executor.ScheduleAsync(() => 7, TimeSpan.Zero, cancellation.Token);
            Assert.Equal(1, executor.PendingTaskCount);
            cancellation.Cancel();
            Assert.True(removed.IsCanceled);
            Assert.Equal(0, executor.PendingTaskCount);
            var queued = executor.ScheduleAsync(() => 7, TimeSpan.Zero);
            Assert.Equal(1, executor.PendingTaskCount);
            release.Signal();
            Assert.Equal(1, running.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(7, queued.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ReplacingFactoryPreservesNativeWorkerIdentity()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        try
        {
            var first = executor.SubmitAsync<Thread>(() =>
            {
                Assert.True(executor.inEventLoop());
                return Thread.CurrentThread;
            }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            var replacement = new Factory();
            executor.setThreadFactory(replacement);
            Assert.Same(replacement, executor.getThreadFactory());
            // CLR accounting belongs to the worker loop, including replacement-factory workers.
            Assert.True(executor.SubmitAsync<bool>(() => executor.inEventLoop()).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
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
            var submitted = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 7; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var queued = executor.SubmitAsync<int>(() => 8);
            Assert.Equal(2, executor.getActiveCount());
            Assert.Equal(3L, executor.getTaskCount());
            Assert.Equal(0L, executor.getCompletedTaskCount());
            release.Signal();
            Assert.Equal(7, submitted.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(8, queued.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(SpinWait.SpinUntil(() => executor.getCompletedTaskCount() == 3, TimeSpan.FromSeconds(5)));
            Assert.Equal(3L, executor.getTaskCount());
            Assert.Equal(0, executor.getActiveCount());
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void ClaimedNativeScheduleKeepsNormalSuccessAfterOwnerCancellation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        using var cancellation = new CancellationTokenSource();
        bool interrupted = false;
        try
        {
            var future = executor.ScheduleAsync(() =>
            {
                entered.Signal();
                try { release.Wait(); }
                catch (ThreadInterruptedException) { interrupted = true; }
                return 7;
            }, TimeSpan.Zero, cancellation.Token);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            Assert.False(future.IsCompleted);
            release.Signal();
            Assert.Equal(8, executor.SubmitAsync<int>(() => 8).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.False(interrupted);
            Assert.Equal(7, future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(future.IsCompletedSuccessfully);
        }
        finally { if (!release.IsSet) release.Signal(); stop(executor); }
    }

    [Fact]
    public void NativeScheduleCancellationDoesNotInjectAnInterruptIntoNextWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            for (int i = 0; i < 256; ++i)
            {
                using var entered = new CountdownEvent(1);
                using var release = new CountdownEvent(1);
                using var cancellation = new CancellationTokenSource();
                var future = executor.ScheduleAsync(() =>
                {
                    entered.Signal();
                    while (!release.IsSet) Thread.SpinWait(64);
                    return 7;
                }, TimeSpan.Zero, cancellation.Token);
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    cancellation.Cancel();
                    Assert.False(future.IsCompleted);
                }
                finally { release.Signal(); }
                var next = executor.SubmitAsync<int>(() => { Thread.Sleep(0); return 8; });
                Assert.Equal(8, next.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
                Assert.Equal(7, future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
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
            Assert.Equal(7, executor.SubmitAsync<int>(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(SpinWait.SpinUntil(() => executor.getCompletedTaskCount() == 2, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, reentrantExecutions);
            Assert.Equal(1, creations);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NativeScheduleFactoryFailureFaultsAndRollsBackAdmission()
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
            var failed = executor.ScheduleAsync(() => ++executions, TimeSpan.Zero);
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                failed.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
            Assert.Equal(0, executor.getPoolSize());
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(7, executor.SubmitAsync<int>(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(2, attempts);
            Assert.Equal(0, executions);
        }
        finally { stop(executor); }
    }

    [Fact]
    public void NullFactoryResultCanRecoverAfterShutdownWhenDelayedWorkRemains()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new LambdaFactory(_ => null));
        try
        {
            var future = executor.SubmitAsync<int>(() => 7);
            Assert.Equal(0, executor.getPoolSize());
            Assert.False(future.IsCompleted);
            executor.shutdown();
            Assert.False(executor.awaitTermination(TimeSpan.FromMilliseconds(1)));
            executor.setThreadFactory(new Factory());
            Assert.True(executor.prestartCoreThread());
            Assert.Equal(7, future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
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
            Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Action)null));
            Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<int>)null));
            Assert.Throws<ArgumentNullException>(() => executor.execute(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(() => { }, TimeSpan.Zero, TimeSpan.FromTicks(-1)));
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally { stop(executor); }
    }
}
