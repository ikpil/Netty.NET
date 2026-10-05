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
        public Thread NewThread(Action task)
        {
            var thread = new Thread(task.Invoke) { IsBackground = true };
            threads.Add(thread);
            return thread;
        }
    }

    private sealed class LambdaFactory : IThreadFactory
    {
        private readonly Func<Action, Thread> create;
        internal LambdaFactory(Func<Action, Thread> create) => this.create = create;
        public Thread NewThread(Action task) => create(task);
    }

    private static void Stop(UnorderedThreadPoolEventExecutor executor)
    {
        _ = executor.StopAsync();
        Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
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
            Assert.Same(executor, executor.Parent());
            Assert.Same(executor, executor.Next());
            Assert.Same(executor, Assert.Single(executor.Iterator()));
            Assert.False(executor.InEventLoop());
            var futures = Enumerable.Range(0, 2).Select(_ => executor.SubmitAsync<Thread>(() =>
            {
                Assert.True(executor.InEventLoop());
                entered.Signal();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return Thread.CurrentThread;
            })).ToArray();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, factory.threads.Count);
            Assert.All(factory.threads, thread => Assert.True(executor.InEventLoop(thread)));
            release.Signal();
            Assert.NotSame(futures[0].WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), futures[1].WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
        Assert.True(SpinWait.SpinUntil(() => factory.threads.All(thread => !executor.InEventLoop(thread)), TimeSpan.FromSeconds(5)));
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
            Assert.True(executor.IsShutdown());
            Assert.True(executor.IsShuttingDown());
            Assert.False(executor.IsTerminated());
            Assert.False(executor.AwaitTermination(TimeSpan.FromMilliseconds(1)));
            Assert.Throws<RejectedExecutionException>(() => executor.Execute(Runnables.Empty));
            release.Signal();
            Assert.Equal(17, work.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(executor.AwaitTermination(TimeSpan.MaxValue));
            Assert.True(executor.AwaitTermination(TimeSpan.MinValue));
        }
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
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
            executor.Execute(new AnonymousRunnable(() => { entered.Set(); release.Wait(); }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var delayed = executor.ScheduleAsync(() => 42, TimeSpan.FromMilliseconds(100));
            var periodic = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.FromDays(1), TimeSpan.FromSeconds(1));
            executor.Shutdown();
            Assert.True(periodic.IsCanceled);
            Assert.False(delayed.IsCompleted);
            release.Set();
            Assert.Equal(42, delayed.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); Stop(executor); }
    }

    [Fact]
    public void StopWithdrawsQueueWorkAndCancelsItsNativeResult()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new ManualResetEventSlim();
        try
        {
            var running = executor.SubmitAsync<bool>(token =>
            {
                entered.Signal();
                release.Wait(TimeSpan.FromSeconds(5), token);
                return false;
            }, executor.StopToken);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var delayed = executor.ScheduleAsync(() => 1, TimeSpan.FromDays(1));
            Assert.Same(executor.Termination, executor.StopAsync());
            Assert.Same(executor.Termination, executor.StopAsync());
            Assert.ThrowsAny<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(running.IsCanceled);
            Assert.True(delayed.IsCanceled);
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); Stop(executor); }
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
        finally { Stop(executor); }
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
        finally { Stop(executor); }
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
        finally { Stop(executor); }
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
        finally { Stop(executor); }
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
        finally { Stop(executor); }
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
        finally { Stop(executor); }
    }

    [Fact]
    public void DiscardingRawRejectionHandlerCannotHideNativeScheduleRejection()
    {
        Action rejected = null;
        UnorderedThreadPoolEventExecutor observed = null;
        var executor = new UnorderedThreadPoolEventExecutor(1, (task, owner) => { rejected = task; observed = owner; });
        Stop(executor);
        executor.Execute(Runnables.Empty);
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
        finally { Stop(executor); }
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
        finally { Stop(executor); }
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
        finally { owner.Cancel(); Stop(executor); }
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
        finally { owner.Cancel(); Stop(executor); }
    }

    [Fact]
    public async Task NativeAsyncBatchYieldsTheOnlyWorkerBeforeQueuedChildRuns()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Task<int> outer = executor.SubmitAsync(async () =>
            {
                Assert.True(executor.InEventLoop());
                Task<int> child = executor.SubmitAsync(() =>
                {
                    Assert.True(executor.InEventLoop());
                    return 7;
                });
                return (await Task.WhenAll(child).ConfigureAwait(false))[0];
            });
            Assert.Equal(7, await outer.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { Stop(executor); }
    }
    [Fact]
    public void ClosingAdmissionStopsPeriodicReentryAfterItsRunningInvocation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        bool interrupted = false;
        try
        {
            Task repeating = executor.ScheduleAtFixedRateAsync(() =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                try { release.Wait(); }
                catch (ThreadInterruptedException) { interrupted = true; }
            }, TimeSpan.Zero, TimeSpan.FromMilliseconds(5));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Assert.False(repeating.IsCompleted);
            Assert.False(termination.IsCompleted);
            Assert.Equal(1, executor.ActiveWorkerCount);
            release.Set();
            termination.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(repeating.IsCanceled);
            Assert.Equal(1, calls);
            Assert.False(interrupted);
        }
        finally { release.Set(); Stop(executor); }
    }

    [Fact]
    public void ClosureCancelsPeriodicReservationsBeforeOwnerCancellation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task repeating = executor.ScheduleAtFixedRateAsync(() => ++calls,
                TimeSpan.FromDays(1), TimeSpan.FromDays(1), cancellation.Token);
            executor.Shutdown();
            Assert.True(repeating.IsCanceled);
            Assert.False(cancellation.IsCancellationRequested);
            cancellation.Cancel();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, calls);
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void OwnerCancellationDropsFutureWorkAndKeepsAcceptedDueWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var running = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 1; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var due = executor.SubmitAsync<int>(() => 2);
            var future = executor.ScheduleAsync(() => 3, TimeSpan.FromDays(1), cancellation.Token);
            cancellation.Cancel();
            executor.Shutdown();
            Assert.True(future.IsCanceled);
            Assert.False(due.IsCompleted);
            release.Signal();
            Assert.Equal(1, running.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(2, due.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        }
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
    }

    [Fact]
    public void OwnerCancellationWithdrawsADelayedReservationAfterAdmissionCloses()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var future = executor.ScheduleAsync(() => 3, TimeSpan.FromDays(1), cancellation.Token);
            executor.Shutdown();
            Assert.False(future.IsCanceled);
            Assert.False(executor.Termination.IsCompleted);
            cancellation.Cancel();
            Assert.True(future.IsCanceled);
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
            Assert.True(executor.Termination.IsCompletedSuccessfully);
        }
        finally { Stop(executor); }
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
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
    }

    [Fact]
    public void ConstructorWorkerLimitBoundsOverlapWithoutInterruptingActiveWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var entered = new CountdownEvent(2);
        using var release = new CountdownEvent(1);
        int interrupted = 0;
        try
        {
            var operations = Enumerable.Range(0, 2).Select(_ => executor.SubmitAsync<int>(() =>
            {
                entered.Signal();
                try { release.Wait(); }
                catch (ThreadInterruptedException) { Interlocked.Increment(ref interrupted); throw; }
                return 7;
            })).ToArray();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var queued = executor.SubmitAsync(() => 8);
            Assert.Equal(2, executor.ActiveWorkerCount);
            Assert.Equal(2, executor.WorkerCount);
            Assert.Equal(1, executor.PendingTaskCount);
            Assert.False(queued.IsCompleted);
            release.Signal();
            Assert.All(operations, operation => Assert.Equal(7, operation.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
            Assert.Equal(8, queued.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(0, interrupted);
        }
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
    }

    [Fact]
    public void PositiveWorkerCountStartsLazilyAndRetainsIdleWorkersUntilShutdown()
    {
        var factory = new Factory();
        var executor = new UnorderedThreadPoolEventExecutor(2, factory);
        try
        {
            Assert.Empty(factory.threads);
            Assert.Equal(0, executor.WorkerCount);
            var delayed = executor.ScheduleAsync(() => 7, TimeSpan.FromMilliseconds(30));
            Assert.Equal(7, delayed.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Single(factory.threads);
            Assert.True(SpinWait.SpinUntil(() => executor.ActiveWorkerCount == 0, TimeSpan.FromSeconds(5)));
            Assert.False(factory.threads.Single().Join(TimeSpan.FromMilliseconds(30)));
            Assert.Equal(1, executor.WorkerCount);
            executor.Shutdown();
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
            Assert.True(factory.threads.Single().Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, executor.WorkerCount);
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void ZeroWorkerCountKeepsFutureDeadlinesAndAllowsImmediateWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var delayed = executor.ScheduleAsync(() => 1, TimeSpan.FromDays(1), cancellation.Token);
            Assert.Equal(7, executor.SubmitAsync<int>(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.False(delayed.IsCompleted);
            Assert.Equal(1, executor.WorkerCount);
            cancellation.Cancel();
            Assert.True(delayed.IsCanceled);
            Assert.True(SpinWait.SpinUntil(() => executor.WorkerCount == 0, TimeSpan.FromSeconds(5)));
            Assert.Equal(8, executor.SubmitAsync(() => 8).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { Stop(executor); }
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
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
    }

    [Fact]
    public void StatefulConstructorFactoryCreatesSuccessiveNativeWorkers()
    {
        int creations = 0;
        var factory = new LambdaFactory(task => new Thread(task.Invoke)
            { IsBackground = true, Name = "native-worker-" + Interlocked.Increment(ref creations) });
        var executor = new UnorderedThreadPoolEventExecutor(0, factory);
        try
        {
            var first = executor.SubmitAsync<Thread>(() =>
            {
                Assert.True(executor.InEventLoop());
                return Thread.CurrentThread;
            }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            var second = executor.SubmitAsync<Thread>(() =>
            {
                Assert.True(executor.InEventLoop());
                return Thread.CurrentThread;
            }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.NotSame(first, second);
            Assert.Equal("native-worker-1", first.Name);
            Assert.Equal("native-worker-2", second.Name);
            Assert.True(second.Join(TimeSpan.FromSeconds(5)));
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void PendingAndActiveCountsDistinguishRawAndNativeInvocations()
    {
        var executor = new UnorderedThreadPoolEventExecutor(2);
        using var entered = new CountdownEvent(2);
        using var release = new CountdownEvent(1);
        try
        {
            executor.Execute(Runnables.Create(() => { entered.Signal(); release.Wait(); }));
            var submitted = executor.SubmitAsync<int>(() => { entered.Signal(); release.Wait(); return 7; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var queued = executor.SubmitAsync<int>(() => 8);
            Assert.Equal(2, executor.ActiveWorkerCount);
            Assert.Equal(1, executor.PendingTaskCount);
            release.Signal();
            Assert.Equal(7, submitted.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(8, queued.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(SpinWait.SpinUntil(() => executor.ActiveWorkerCount == 0, TimeSpan.FromSeconds(5)));
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
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
        finally { if (!release.IsSet) release.Signal(); Stop(executor); }
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
        finally { Stop(executor); }
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
            executor.Execute(Runnables.Create(() => ++reentrantExecutions));
            return new Thread(task.Invoke) { IsBackground = true };
        });
        executor = new UnorderedThreadPoolEventExecutor(1, factory);
        try
        {
            Assert.Equal(7, executor.SubmitAsync<int>(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref reentrantExecutions) == 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, reentrantExecutions);
            Assert.Equal(1, creations);
        }
        finally { Stop(executor); }
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
            return new Thread(task.Invoke) { IsBackground = true };
        });
        var executor = new UnorderedThreadPoolEventExecutor(1, factory);
        try
        {
            var failed = executor.ScheduleAsync(() => ++executions, TimeSpan.Zero);
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                failed.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
            Assert.Equal(0, executor.WorkerCount);
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(7, executor.SubmitAsync<int>(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(2, attempts);
            Assert.Equal(0, executions);
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void NullFactoryRetainedWorkIsCanceledByImmediateShutdown()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new LambdaFactory(_ => null));
        try
        {
            var operation = executor.SubmitAsync<int>(() => 7);
            Assert.Equal(0, executor.WorkerCount);
            Assert.False(operation.IsCompleted);
            executor.Shutdown();
            Assert.False(executor.AwaitTermination(TimeSpan.FromMilliseconds(1)));
            _ = executor.StopAsync();
            Assert.True(operation.IsCanceled);
            Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, executor.WorkerCount);
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void ConstructorRejectsInvalidWorkerCountFactoryAndHandler()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnorderedThreadPoolEventExecutor(-1));
        Assert.Throws<ArgumentNullException>(() => new UnorderedThreadPoolEventExecutor(1, (IThreadFactory)null));
        Assert.Throws<ArgumentNullException>(() => new UnorderedThreadPoolEventExecutor(1,
            (Action<Action, UnorderedThreadPoolEventExecutor>)null));
        Assert.Throws<ArgumentNullException>(() => new UnorderedThreadPoolEventExecutor(1, new Factory(), null));
        var executor = new UnorderedThreadPoolEventExecutor(2);
        try
        {
            Assert.Equal(7, executor.SubmitAsync(() => 7).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void NullTasksAndNonpositivePeriodsAreRejectedBeforeEnqueue()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Action)null));
            Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<int>)null));
            Assert.Throws<ArgumentNullException>(() => executor.Execute(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(() => { }, TimeSpan.Zero, TimeSpan.FromTicks(-1)));
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally { Stop(executor); }
    }
}
