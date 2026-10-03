using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class NativeExecutorTaskContractTest
{
    // This deterministic harness controls when a queued invocation starts.
    // A separate test below uses the real event loop and asynchronous suspension.
    private sealed class QueuedExecutor : AbstractEventExecutor
    {
        private readonly Queue<IRunnable> _queue = new();
        private Thread _executingThread;
        internal Exception Rejection;
        internal int Submissions;
        internal int Pending => _queue.Count;
        internal IRunnable LastSubmission;
        internal Action BeforeAdmission;
        public override void execute(IRunnable command)
        {
            ++Submissions;
            BeforeAdmission?.Invoke();
            if (Rejection != null) throw Rejection;
            LastSubmission = command;
            _queue.Enqueue(command);
        }
        internal void RunNext()
        {
            IRunnable command = _queue.Dequeue();
            _executingThread = Thread.CurrentThread;
            try { command.run(); }
            finally { _executingThread = null; }
        }
        public override bool inEventLoop(Thread thread) => ReferenceEquals(thread, _executingThread);
        public override bool isShuttingDown() => false;
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override void shutdown() => throw new NotSupportedException("Test harness has no worker lifecycle");
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) =>
            throw new NotSupportedException("Test harness has no worker lifecycle");
        public override Task Termination =>
            throw new NotSupportedException("Test harness has no worker lifecycle");
        public override bool awaitTermination(TimeSpan timeout) =>
            throw new NotSupportedException("Test harness has no worker lifecycle");
    }

    private sealed class SelectingGroup : AbstractEventExecutorGroup
    {
        private readonly IEventExecutor[] _children;
        internal int Selections;
        internal Exception SelectionFailure;
        internal SelectingGroup(params IEventExecutor[] children) => _children = children;
        public override IEventExecutor next()
        {
            int selection = Selections++;
            if (SelectionFailure != null) throw SelectionFailure;
            return _children[selection % _children.Length];
        }
        public override IEnumerable<IEventExecutor> iterator() => _children;
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        public override void shutdown() { }
        public override bool isShuttingDown() => false;
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
    }

    [Fact]
    public async Task GroupSubmissionSelectsOneChildForEveryNativeDelegateFamily()
    {
        var first = new QueuedExecutor();
        var second = new QueuedExecutor();
        var group = new SelectingGroup(first, second);
        using var cancellation = new CancellationTokenSource();
        var calls = new List<int>();
        Task action = group.SubmitAsync(() => { calls.Add(0); });
        Task<int> value = group.SubmitAsync(() => { calls.Add(1); return 1; });
        Task tokenAction = group.SubmitAsync((CancellationToken token) =>
        {
            Assert.Equal(cancellation.Token, token);
            calls.Add(2);
        }, cancellation.Token);
        Task<int> tokenValue = group.SubmitAsync(token =>
        {
            Assert.Equal(cancellation.Token, token);
            calls.Add(3);
            return 3;
        }, cancellation.Token);
        Task asyncAction = group.SubmitAsync(() => { calls.Add(4); return Task.CompletedTask; });
        Task<int> asyncValue = group.SubmitAsync(() => { calls.Add(5); return Task.FromResult(5); });
        Task tokenAsyncAction = group.SubmitAsync((CancellationToken token) =>
        {
            Assert.Equal(cancellation.Token, token);
            calls.Add(6);
            return Task.CompletedTask;
        }, cancellation.Token);
        Task<int> tokenAsyncValue = group.SubmitAsync((CancellationToken token) =>
        {
            Assert.Equal(cancellation.Token, token);
            calls.Add(7);
            return Task.FromResult(7);
        }, cancellation.Token);
        Assert.Equal(8, group.Selections);
        Assert.Equal(4, first.Pending);
        Assert.Equal(4, second.Pending);
        Assert.Empty(calls);
        for (int i = 0; i < 4; ++i) { first.RunNext(); second.RunNext(); }
        await Task.WhenAll(action, value, tokenAction, tokenValue, asyncAction, asyncValue,
            tokenAsyncAction, tokenAsyncValue);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, calls);
        Assert.Equal(1, await value);
        Assert.Equal(3, await tokenValue);
        Assert.Equal(5, await asyncValue);
        Assert.Equal(7, await tokenAsyncValue);
        Assert.False((object)first.LastSubmission is System.Threading.Tasks.Task);
        Assert.False((object)second.LastSubmission is System.Threading.Tasks.Task);
    }

    [Fact]
    public async Task PreCanceledAndInvalidGroupSubmissionsDoNotSelectAChild()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var group = new SelectingGroup(new QueuedExecutor());
        Task result = group.SubmitAsync(() => 1, cancellation.Token);
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Throws<ArgumentNullException>(() => group.SubmitAsync((Action)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => group.SubmitAsync((Func<Task>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => EventExecutorExtensions.SubmitAsync((IEventExecutorGroup)null, () => 1));
        Assert.Equal(0, group.Selections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GroupSelectionAndChildAdmissionFailuresRetainTheirOriginalException(bool selectionFails)
    {
        var original = new RejectedExecutionException("closed");
        var child = new QueuedExecutor { Rejection = selectionFails ? null : original };
        var group = new SelectingGroup(child) { SelectionFailure = selectionFails ? original : null };
        Task result = group.SubmitAsync(() => 1);
        Assert.Same(original, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await result));
        Assert.True(result.IsFaulted);
        Assert.Equal(1, group.Selections);
        Assert.Equal(selectionFails ? 0 : 1, child.Submissions);
        Assert.Equal(0, child.Pending);
    }

    [Fact]
    public async Task NonStickyGroupDelegatesSubmissionButItsSelectedChildKeepsItsOrderedRunner()
    {
        var underlying = new QueuedExecutor();
        var source = new SelectingGroup(underlying);
        IEventExecutorGroup group = new NonStickyEventExecutorGroup(source);
        Task<int> first = group.SubmitAsync(() => 1);
        Task<int> second = group.SubmitAsync(() => 2);
        Assert.Equal(2, source.Selections);
        Assert.Equal(2, underlying.Pending);
        Assert.False(underlying.LastSubmission is IOrderedEventExecutor);
        underlying.RunNext();
        underlying.RunNext();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);

        IEventExecutor orderedChild = group.next();
        Task<int> third = orderedChild.SubmitAsync(() => 3);
        Task<int> fourth = orderedChild.SubmitAsync(() => 4);
        Assert.Equal(3, source.Selections);
        Assert.Equal(1, underlying.Pending);
        Assert.True(orderedChild is IOrderedEventExecutor);
        Assert.False((object)underlying.LastSubmission is System.Threading.Tasks.Task);
        underlying.RunNext();
        Assert.Equal(3, await third);
        Assert.Equal(4, await fourth);
    }

    [Fact]
    public async Task NativeGroupConsumerRunsOnSelectedRealEventLoops()
    {
        IEventExecutorGroup group = new DefaultEventExecutorGroup(2);
        try
        {
            var operations = new Task<int>[8];
            for (int i = 0; i < operations.Length; ++i)
                operations[i] = group.SubmitAsync(() => Thread.CurrentThread.ManagedThreadId);
            int[] threadIds = await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(threadIds[0], threadIds[1]);
            for (int i = 2; i < threadIds.Length; ++i)
                Assert.Equal(threadIds[i % 2], threadIds[i]);
        }
        finally
        {
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UnorderedNativeRejectionFaultsEvenWhenTheLegacyHandlerDiscardsWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, (_, _) => { });
        executor.shutdown();
        Task result = executor.SubmitAsync(() => { });
        Assert.True(result.IsFaulted);
        await Assert.ThrowsAsync<RejectedExecutionException>(async () => await result);
        Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task UnorderedShutdownNowCancelsTheNativeSubmissionRemovedFromTheQueue()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running = executor.SubmitAsync(() =>
        {
            started.SetResult();
            try { release.Wait(TimeSpan.FromSeconds(5)); }
            catch (ThreadInterruptedException) { }
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int calls = 0;
            Task queued = executor.SubmitAsync(() => { ++calls; });
            Assert.Equal(1, executor.PendingTaskCount);
            IRunnable queuedWork = Assert.Single(executor.shutdownNow());
            Assert.False((object)queuedWork is System.Threading.Tasks.Task);
            queuedWork.run();
            Assert.True(queued.IsCanceled);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queued);
            Assert.Equal(0, calls);
        }
        finally
        {
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task RejectedNonStickyChildCanAcceptANewSubmissionAfterAdmissionRecovers()
    {
        var original = new RejectedExecutionException("temporary full queue");
        var underlying = new QueuedExecutor { Rejection = original };
        IEventExecutor child = new NonStickyEventExecutorGroup(underlying).next();
        Task first = child.SubmitAsync(() => { });
        Assert.Same(original, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await first));
        underlying.Rejection = null;
        Task<int> second = child.SubmitAsync(() => 2);
        Assert.Equal(1, underlying.Pending);
        underlying.RunNext();
        Assert.Equal(2, await second);
    }

    [Fact]
    public async Task NonStickyNativeAdmissionsShareTheRunnerAdmissionFailure()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var original = new RejectedExecutionException("runner rejected");
        var underlying = new QueuedExecutor
        {
            Rejection = original,
            BeforeAdmission = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
        };
        IEventExecutor child = new NonStickyEventExecutorGroup(underlying).next();
        Task<Task> firstAdmission = Task.Factory.StartNew(() => child.SubmitAsync(() => { }),
            CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task second = child.SubmitAsync(() => { });
            release.Set();
            Task first = await firstAdmission.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(second.IsFaulted);
            Assert.Same(original, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await first));
            Assert.Same(original, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await second));
        }
        finally { release.Set(); await firstAdmission.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task NonStickyNativeChildRejectsShutdownEvenWhenThePoolHandlerDiscards()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, (_, _) => { });
        IEventExecutor child = new NonStickyEventExecutorGroup(executor).next();
        executor.shutdown();
        Task result = child.SubmitAsync(() => { });
        Assert.True(result.IsFaulted);
        await Assert.ThrowsAsync<RejectedExecutionException>(async () => await result);
        Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
    }

    private sealed class ForwardingExecutor(UnorderedThreadPoolEventExecutor underlying) : AbstractEventExecutor
    {
        public override void execute(IRunnable command) => underlying.execute(command);
        public override bool inEventLoop(Thread thread) => underlying.inEventLoop(thread);
        public override bool isShuttingDown() => underlying.isShuttingDown();
        public override bool isShutdown() => underlying.isShutdown();
        public override bool isTerminated() => underlying.isTerminated();
        public override void shutdown() => underlying.shutdown();
        public override Task Termination => underlying.Termination;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) =>
            underlying.ShutdownGracefullyAsync(quietPeriod, timeout);
        public override bool awaitTermination(TimeSpan timeout) => underlying.awaitTermination(timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PoolShutdownNowCancelsEveryNativeSubmissionInsideARemovedNonStickyRunner(bool forwarded)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        IEventExecutorGroup underlying = forwarded ? new ForwardingExecutor(executor) : executor;
        IEventExecutor child = new NonStickyEventExecutorGroup(underlying, 1).next();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running = executor.SubmitAsync(() =>
        {
            started.SetResult();
            try { release.Wait(TimeSpan.FromSeconds(5)); }
            catch (ThreadInterruptedException) { }
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int calls = 0;
            Task first = child.SubmitAsync(() => { ++calls; });
            Task second = child.SubmitAsync(() => { ++calls; });
            Assert.Equal(1, executor.PendingTaskCount);
            IRunnable runner = Assert.Single(executor.shutdownNow());
            Assert.False((object)runner is System.Threading.Tasks.Task);
            runner.run();
            Assert.True(first.IsCanceled);
            Assert.True(second.IsCanceled);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
            Assert.Equal(0, calls);
        }
        finally
        {
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task NonStickyRunningSubmissionFinishesWhileShutdownSettlesItsQueuedWork(bool immediate, bool forwarded)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        IEventExecutorGroup underlying = forwarded ? new ForwardingExecutor(executor) : executor;
        IEventExecutor child = new NonStickyEventExecutorGroup(underlying, 1).next();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<int>();
        Task<int> running = child.SubmitAsync(() =>
        {
            Assert.True(child.inEventLoop());
            started.SetResult();
            try { Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
            catch (ThreadInterruptedException) { }
            Assert.True(child.inEventLoop());
            return 42;
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task first = child.SubmitAsync(() => { Assert.True(child.inEventLoop()); calls.Add(1); });
            Task second = child.SubmitAsync(() => { Assert.True(child.inEventLoop()); calls.Add(2); });
            if (immediate) executor.shutdownNow();
            else executor.shutdown();
            release.Set();
            Assert.Equal(42, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            if (immediate)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.WaitAsync(TimeSpan.FromSeconds(5)));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.True(first.IsCanceled);
                Assert.True(second.IsCanceled);
                Assert.Empty(calls);
            }
            else
            {
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(new[] { 1, 2 }, calls);
            }
        }
        finally
        {
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task NonStickyInlineBatchHandoffsHaveBoundedCallbackDepth()
    {
        IEventExecutor child = new NonStickyEventExecutorGroup(ImmediateEventExecutor.INSTANCE, 1).next();
        var operations = new List<Task>();
        int count = 0, depth = 0, maximumDepth = 0;
        Action invoke = null;
        invoke = () =>
        {
            ++depth;
            maximumDepth = Math.Max(maximumDepth, depth);
            Assert.True(child.inEventLoop());
            if (++count < 10001) operations.Add(child.SubmitAsync(invoke));
            --depth;
        };
        operations.Add(child.SubmitAsync(invoke));
        await Task.WhenAll(operations);
        Assert.Equal(10001, count);
        Assert.Equal(1, maximumDepth);
        Assert.False(child.inEventLoop());
        Assert.False(child.inEventLoop(null));
    }

    [Fact]
    public async Task NonStickyHandoffsCanChangeThreadsWithoutLosingFifoOrAffinity()
    {
        var underlying = new QueuedExecutor();
        IEventExecutor child = new NonStickyEventExecutorGroup(underlying, 1).next();
        var threads = new List<int>();
        Task first = child.SubmitAsync(() => { Assert.True(child.inEventLoop()); threads.Add(Thread.CurrentThread.ManagedThreadId); });
        Task second = child.SubmitAsync(() => { Assert.True(child.inEventLoop()); threads.Add(Thread.CurrentThread.ManagedThreadId); });
        var firstWorker = new Thread(underlying.RunNext);
        firstWorker.Start();
        Assert.True(firstWorker.Join(TimeSpan.FromSeconds(5)));
        Assert.False(child.inEventLoop(firstWorker));
        Assert.Equal(1, underlying.Pending);
        var secondWorker = new Thread(underlying.RunNext);
        secondWorker.Start();
        Assert.True(secondWorker.Join(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { firstWorker.ManagedThreadId, secondWorker.ManagedThreadId }, threads);
        Assert.False(child.inEventLoop(secondWorker));
    }

    private sealed class FailingThreadFactory(Exception error) : IThreadFactory
    {
        public Thread newThread(IRunnable runnable) => throw error;
    }

    [Fact]
    public async Task UnorderedNativeWorkerStartFailureRemovesItsReservation()
    {
        var original = new InvalidOperationException("worker start failed");
        var executor = new UnorderedThreadPoolEventExecutor(1, new FailingThreadFactory(original));
        try
        {
            Task result = executor.SubmitAsync(() => { });
            Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(async () => await result));
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(0, executor.ActiveWorkerCount);
        }
        finally
        {
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task NativeDelegatesReturnTheirOriginalResultAndExecuteOnTheExecutor()
    {
        var executor = new QueuedExecutor();
        var value = new object();
        Task<object> result = executor.SubmitAsync(() =>
        {
            Assert.True(executor.inEventLoop());
            return value;
        });
        Assert.False(result.IsCompleted);
        executor.RunNext();
        Assert.Same(value, await result);

        int calls = 0;
        Task action = executor.SubmitAsync(() => { ++calls; });
        executor.RunNext();
        await action;
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PreCanceledSubmissionDoesNotReachTheExecutor()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executor = new QueuedExecutor { Rejection = new RejectedExecutionException("closed") };
        Func<int> function = () => throw new InvalidOperationException("must not run");
        Task<int> result = executor.SubmitAsync(function, cancellation.Token);
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, executor.Submissions);
    }

    [Fact]
    public async Task CancellationWhileQueuedPreventsInvocation()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueuedExecutor();
        int calls = 0;
        Task result = executor.SubmitAsync(() => { ++calls; }, cancellation.Token);
        cancellation.Cancel();
        Assert.True(result.IsCanceled);
        executor.RunNext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CancellationAfterInvocationStartsAllowsANormalReturn()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueuedExecutor();
        Task<int> result = executor.SubmitAsync(token =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            Assert.True(token.IsCancellationRequested);
            return 42;
        }, cancellation.Token);
        executor.RunNext();
        Assert.Equal(42, await result);
        Assert.True(result.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TokenAwareActionsAndNullResultsCompleteSuccessfully()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueuedExecutor();
        int calls = 0;
        Task action = executor.SubmitAsync((CancellationToken token) =>
        {
            Assert.Equal(cancellation.Token, token);
            ++calls;
        }, cancellation.Token);
        executor.RunNext();
        await action;
        Assert.Equal(1, calls);
        Func<object> function = () => null;
        Task<object> result = executor.SubmitAsync(function);
        executor.RunNext();
        Assert.Null(await result);
        Assert.True(result.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task RunningCancellationRequiresARequestedMatchingToken(bool matching, bool requested)
    {
        using var cancellation = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var executor = new QueuedExecutor();
        var original = new OperationCanceledException(matching ? cancellation.Token : unrelated.Token);
        Func<CancellationToken, int> function = token =>
        {
            if (requested) cancellation.Cancel();
            throw original;
        };
        Task<int> result = executor.SubmitAsync(function, cancellation.Token);
        executor.RunNext();
        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
        if (matching && requested)
        {
            Assert.True(result.IsCanceled);
            Assert.Equal(cancellation.Token, actual.CancellationToken);
        }
        else
        {
            Assert.True(result.IsFaulted);
            Assert.Same(original, actual);
            Assert.Same(original, result.Exception.InnerException);
        }
    }

    [Fact]
    public async Task RejectionIsReportedAsTheOriginalTaskFailure()
    {
        var original = new RejectedExecutionException("closed");
        var executor = new QueuedExecutor { Rejection = original };
        Task<int> result = executor.SubmitAsync(() => 1);
        Assert.True(result.IsFaulted);
        Assert.Same(original, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await result));
        Assert.Equal(0, executor.Pending);
    }

    [Fact]
    public async Task CancellationRacingWithInvocationHasOneOutcome()
    {
        for (int i = 0; i < 1000; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var executor = new QueuedExecutor();
            int calls = 0;
            Task<int> result = executor.SubmitAsync(() => { ++calls; return 7; }, cancellation.Token);
            Parallel.Invoke(cancellation.Cancel, executor.RunNext);
            if (calls == 0)
            {
                Assert.True(result.IsCanceled);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
            }
            else
            {
                Assert.Equal(1, calls);
                Assert.Equal(7, await result);
                Assert.True(result.IsCompletedSuccessfully);
            }
        }
    }

    [Fact]
    public async Task SubmissionFlowsExecutionContextWithoutLeakingInvocationChanges()
    {
        var ambient = new AsyncLocal<string> { Value = "submission" };
        var executor = new QueuedExecutor();
        Task<string> result = executor.SubmitAsync(() =>
        {
            string captured = ambient.Value;
            ambient.Value = "invocation";
            return captured;
        });
        ambient.Value = "executor";
        executor.RunNext();
        Assert.Equal("executor", ambient.Value);
        Assert.Equal("submission", await result);
    }

    [Fact]
    public async Task SuppressedSubmissionUsesExecutorContextAndStillScopesItsChanges()
    {
        var ambient = new AsyncLocal<string> { Value = "submission" };
        var executor = new QueuedExecutor();
        Task<string> result;
        using (ExecutionContext.SuppressFlow())
            result = executor.SubmitAsync(() => { string value = ambient.Value; ambient.Value = "invocation"; return value; });
        ambient.Value = "executor";
        executor.RunNext();
        Assert.Equal("executor", ambient.Value);
        Assert.Equal("executor", await result);
    }

    [Fact]
    public async Task ImmediateExecutionPreservesAnExistingFlowSuppressionScope()
    {
        var ambient = new AsyncLocal<string> { Value = "caller" };
        Task<string> result;
        using (ExecutionContext.SuppressFlow())
        {
            result = ImmediateEventExecutor.INSTANCE.SubmitAsync(() =>
            {
                string value = ambient.Value;
                ambient.Value = "invocation";
                return value;
            });
            Assert.True(ExecutionContext.IsFlowSuppressed());
            Assert.Equal("caller", ambient.Value);
        }
        Assert.False(ExecutionContext.IsFlowSuppressed());
        Assert.Equal("caller", await result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AReturnedNullTaskIsAFailureRatherThanCancellation(int delegateFamily)
    {
        var executor = new QueuedExecutor();
        Task result = delegateFamily switch
        {
            0 => executor.SubmitAsync((Func<Task>)(() => null)),
            1 => executor.SubmitAsync((Func<Task<int>>)(() => null)),
            2 => executor.SubmitAsync((Func<CancellationToken, Task>)(_ => null)),
            _ => executor.SubmitAsync((Func<CancellationToken, Task<int>>)(_ => null))
        };
        Assert.False(result.IsCompleted);
        executor.RunNext();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await result);
        Assert.True(result.IsFaulted);
        Assert.False(result.IsCanceled);
        Assert.Contains("null Task", error.Message);
        Assert.Same(error, result.Exception.InnerException);
    }

    [Fact]
    public async Task AsyncFunctionsAreUnwrappedAndCompleteAfterTheirInnerTask()
    {
        var executor = new QueuedExecutor();
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> result = executor.SubmitAsync(() => completion.Task);
        executor.RunNext();
        Assert.False(result.IsCompleted);
        completion.SetResult(9);
        Assert.Equal(9, await result);
    }

    [Fact]
    public async Task AsyncActionsWithTokensPropagateTheInnerFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueuedExecutor();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task result = executor.SubmitAsync((CancellationToken token) =>
        {
            Assert.Equal(cancellation.Token, token);
            return completion.Task;
        }, cancellation.Token);
        executor.RunNext();
        Assert.False(result.IsCompleted);
        var original = new InvalidOperationException("inner failure");
        completion.SetException(original);
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(async () => await result));
    }

    [Fact]
    public async Task AsyncActionsWithoutTokensObserveTheEntireDelegate()
    {
        var executor = new QueuedExecutor();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool finished = false;
        Task result = executor.SubmitAsync(async () =>
        {
            await completion.Task;
            finished = true;
        });
        executor.RunNext();
        Assert.False(result.IsCompleted);
        completion.SetResult();
        await result;
        Assert.True(finished);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunningAsyncFunctionsCompleteOnlyWhenTheirOperationCompletes(bool cooperative)
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueuedExecutor();
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<int>> function = async token => cooperative
            ? await completion.Task.WaitAsync(token) : await completion.Task;
        Task<int> result = executor.SubmitAsync(function, cancellation.Token);
        executor.RunNext();
        cancellation.Cancel();
        if (cooperative)
        {
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.True(result.IsCanceled);
        }
        else
        {
            Assert.False(result.IsCompleted);
            completion.SetResult(11);
            Assert.Equal(11, await result);
            Assert.True(result.IsCompletedSuccessfully);
        }
    }

    [Fact]
    public async Task AsyncConsumerExplicitlyResubmitsWorkAfterLeavingTheRealEventLoop()
    {
        var executor = new DefaultEventExecutor();
        var continueIo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            int state = 0;
            Task<int> operation = executor.SubmitAsync(async () =>
            {
                Assert.True(executor.inEventLoop());
                state = 1;
                await continueIo.Task.ConfigureAwait(false);
                Assert.False(executor.inEventLoop());
                return await executor.SubmitAsync(() =>
                {
                    Assert.True(executor.inEventLoop());
                    return ++state;
                });
            });
            // The event loop must run the next queued job while the first async
            // delegate awaits I/O; invocation ordering must not become an async lock.
            Assert.Equal(1, await executor.SubmitAsync(() => state).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(operation.IsCompleted);
            continueIo.SetResult();
            Assert.Equal(2, await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            continueIo.TrySetResult();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancellationReleasesTheDelegateAndCapturedContextBeforeTheQueueDrains()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueuedExecutor();
        (WeakReference retained, Task result) = SubmitRetainingAnObject(executor, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
        for (int i = 0; i < 3 && retained.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(retained.IsAlive);
        Assert.Equal(1, executor.Pending);
        executor.RunNext();
        GC.KeepAlive(executor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, Task) SubmitRetainingAnObject(QueuedExecutor executor, CancellationToken cancellationToken)
    {
        var value = new object();
        var retained = new WeakReference(value);
        var ambient = new AsyncLocal<object> { Value = value };
        Task result = executor.SubmitAsync(() => { GC.KeepAlive(value); GC.KeepAlive(ambient.Value); }, cancellationToken);
        ambient.Value = null;
        return (retained, result);
    }

    [Fact]
    public void InvalidArgumentsFailSynchronouslyEvenWithACanceledToken()
    {
        var executor = new QueuedExecutor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Action)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<int>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Action<CancellationToken>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<CancellationToken, int>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<Task>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<Task<int>>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<CancellationToken, Task>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<CancellationToken, Task<int>>)null, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => EventExecutorExtensions.SubmitAsync(null, () => 1, cancellation.Token));
        Assert.Equal(0, executor.Submissions);
    }
}
