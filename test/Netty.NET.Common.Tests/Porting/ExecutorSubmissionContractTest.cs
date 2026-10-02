using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Porting;

public class ExecutorSubmissionContractTest
{
    private sealed class ManualExecutor : AbstractEventExecutor
    {
        internal readonly BlockingCollection<IRunnable> tasks = new();
        private bool running;
        public override bool inEventLoop(Thread thread) => running && thread == Thread.CurrentThread;
        public override void execute(IRunnable task) => tasks.Add(task);
        internal IRunnable take()
        {
            Assert.True(tasks.TryTake(out var task, TimeSpan.FromSeconds(5)));
            return task;
        }
        internal void run(IRunnable task)
        {
            running = true;
            try { task.run(); }
            finally { running = false; }
        }
        public override void shutdown() { }
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override bool isShuttingDown() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
        public override IFuture<Netty.NET.Common.Concurrent.Void> terminationFuture() => ImmediateEventExecutor.INSTANCE.newSucceededFuture<Netty.NET.Common.Concurrent.Void>(null);
        public override IFuture<Netty.NET.Common.Concurrent.Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout) => ImmediateEventExecutor.INSTANCE.newSucceededFuture<Netty.NET.Common.Concurrent.Void>(null);
    }
    private sealed class Listener<T> : IGenericFutureListener<IFuture<T>>
    {
        private readonly Action<IFuture<T>> callback;
        internal Listener(Action<IFuture<T>> callback) => this.callback = callback;
        public void operationComplete(IFuture<T> future) => callback(future);
    }

    [Fact]
    public void SubmissionReturnsTheNettyPromiseTaskAndNotifiesOnItsExecutor()
    {
        var executor = new ManualExecutor();
        object result = new object();
        IFuture<object> future = executor.submit(new AnonymousCallable<object>(() => result));
        Assert.IsType<PromiseTask<object>>(future);
        bool notified = false;
        future.addListener(new Listener<object>(f => { notified = executor.inEventLoop() && ReferenceEquals(f, future); }));
        Assert.False(future.isDone());
        Assert.False(((IPromise<object>)future).trySuccess(null));
        executor.run(executor.take());
        Assert.Same(result, future.get());
        Assert.Same(result, future.Task.GetAwaiter().GetResult());
        Assert.True(notified);
        Assert.False(future.cancel(true));
    }

    [Fact]
    public void RunnableSubmissionsRetainExplicitResultAndNullVoidResult()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        var result = new object();
        var supplied = executor.submit(Runnables.Create(() => ++calls), result);
        var empty = executor.submit(Runnables.Create(() => ++calls));
        executor.run(executor.take());
        executor.run(executor.take());
        Assert.Same(result, supplied.get());
        Assert.Null(empty.get());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CancellationBeforeExecutionPreventsUserCodeAndCompletesImmediately()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        var future = executor.submit(new AnonymousCallable<int>(() => ++calls));
        Assert.True(future.cancel(false));
        Assert.True(future.isDone());
        Assert.True(future.Task.IsCanceled);
        executor.run(executor.take());
        Assert.Equal(0, calls);
        Assert.ThrowsAny<OperationCanceledException>(() => future.get());
    }

    [Fact]
    public void RunningPromiseTaskIsUncancellableAndRetainsOriginalFailure()
    {
        var executor = new ManualExecutor();
        IFuture<int> future = null;
        var cause = new InvalidOperationException("original");
        future = executor.submit(new AnonymousCallable<int>(() =>
        {
            Assert.False(future.cancel(true));
            throw cause;
        }));
        executor.run(executor.take());
        Assert.Same(cause, future.cause());
        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => future.sync()));
        Assert.Same(cause, Assert.Throws<AggregateException>(() => future.get()).InnerException);
    }

    [Fact]
    public void SubmissionRejectsNullBeforeQueuing()
    {
        var executor = new ManualExecutor();
        Assert.Throws<ArgumentNullException>(() => executor.submit((IRunnable)null));
        Assert.Throws<ArgumentNullException>(() => executor.submit((IRunnable)null, 1));
        Assert.Throws<ArgumentNullException>(() => executor.submit((ICallable<int>)null));
        Assert.Equal(0, executor.tasks.Count);
    }

    [Fact]
    public void InvokeAllReturnsInputOrderAndRetainsIndividualFailures()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var cause = new InvalidOperationException("second");
        ICallable<int>[] tasks = { new AnonymousCallable<int>(() => 1), new AnonymousCallable<int>(() => throw cause),
            new AnonymousCallable<int>(() => 3) };
        var futures = executor.invokeAll(tasks);
        Assert.Equal(3, futures.Count);
        Assert.All(futures, f => Assert.True(f.isDone()));
        Assert.Equal(1, futures[0].get());
        Assert.Same(cause, futures[1].cause());
        Assert.Equal(3, futures[2].get());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveInvokeAllTimeoutCancelsEveryFutureWithoutSubmission(int ticks)
    {
        var executor = new ManualExecutor();
        ICallable<int>[] tasks = { new AnonymousCallable<int>(() => 1), new AnonymousCallable<int>(() => 2) };
        var futures = executor.invokeAll(tasks, TimeSpan.FromTicks(ticks));
        Assert.Equal(2, futures.Count);
        Assert.All(futures, f => Assert.True(f.isCancelled()));
        Assert.Equal(0, executor.tasks.Count);
    }

    [Fact]
    public void InvokeAllTimeoutCancelsQueuedTasksAndTheyNeverExecute()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        ICallable<int>[] tasks = { new AnonymousCallable<int>(() => ++calls), new AnonymousCallable<int>(() => ++calls) };
        var futures = executor.invokeAll(tasks, TimeSpan.FromMilliseconds(10));
        Assert.All(futures, f => Assert.True(f.isCancelled()));
        while (executor.tasks.TryTake(out var task)) executor.run(task);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void NullElementDuringInvokeAllCancelsTasksAlreadySubmitted()
    {
        var executor = new ManualExecutor();
        ICallable<int>[] tasks = { new AnonymousCallable<int>(() => 1), null };
        Assert.Throws<ArgumentNullException>(() => executor.invokeAll(tasks));
        var first = Assert.IsAssignableFrom<IFuture<int>>(executor.take());
        Assert.True(first.isCancelled());
    }

    [Fact]
    public void InvokeAnyReturnsFirstSuccessAndCancelsOtherQueuedTasks()
    {
        var executor = new ManualExecutor();
        var cause = new InvalidOperationException("first failure");
        int lateCalls = 0;
        ICallable<int>[] tasks = { new AnonymousCallable<int>(() => throw cause), new AnonymousCallable<int>(() => 42),
            new AnonymousCallable<int>(() => ++lateCalls) };
        int result = 0;
        Exception failure = null;
        using var done = new CountdownEvent(1);
        var caller = new Thread(() =>
        {
            try { result = executor.invokeAny(tasks); }
            catch (Exception error) { failure = error; }
            finally { done.Signal(); }
        }) { IsBackground = true };
        caller.Start();
        Assert.True(SpinWait.SpinUntil(() => executor.tasks.Count == 3, TimeSpan.FromSeconds(5)));
        executor.run(executor.take());
        executor.run(executor.take());
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Equal(42, result);
        executor.run(executor.take());
        Assert.Equal(0, lateCalls);
    }

    [Fact]
    public void InvokeAnyReportsLastFailureAndChecksOnlyTasksSubjectToExecution()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var first = new Exception("first");
        var last = new InvalidOperationException("last");
        ICallable<int>[] failed = { new AnonymousCallable<int>(() => throw first), new AnonymousCallable<int>(() => throw last) };
        Assert.Same(last, Assert.Throws<AggregateException>(() => executor.invokeAny(failed)).InnerException);
        ICallable<int>[] withUnusedNull = { new AnonymousCallable<int>(() => 42), null };
        Assert.Equal(42, executor.invokeAny(withUnusedNull));
    }

    [Fact]
    public void InvokeAnyTimeoutCancelsUnstartedWork()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        ICallable<int>[] tasks = { new AnonymousCallable<int>(() => ++calls) };
        Assert.Throws<TimeoutException>(() => executor.invokeAny(tasks, TimeSpan.FromMilliseconds(10)));
        executor.run(executor.take());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void EmptyAndNullCollectionsRetainInvocationContracts()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        Assert.Empty(executor.invokeAll(Array.Empty<ICallable<int>>()));
        Assert.Throws<ArgumentException>(() => executor.invokeAny(Array.Empty<ICallable<int>>()));
        Assert.Throws<ArgumentNullException>(() => executor.invokeAll((ICollection<ICallable<int>>)null));
        Assert.Throws<ArgumentNullException>(() => executor.invokeAny((ICollection<ICallable<int>>)null));
    }

    [Fact]
    public void InterruptingInvokeAllCancelsItsPendingFutures()
    {
        var executor = new ManualExecutor();
        Exception failure = null;
        var caller = new Thread(() =>
        {
            try { executor.invokeAll(new ICallable<int>[] { new AnonymousCallable<int>(() => 1) }); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        caller.Start();
        var queued = Assert.IsAssignableFrom<IFuture<int>>(executor.take());
        Assert.True(SpinWait.SpinUntil(() => (caller.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
            TimeSpan.FromSeconds(5)));
        caller.Interrupt();
        Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<ThreadInterruptedException>(failure);
        Assert.True(queued.isCancelled());
    }

    [Fact]
    public void InterruptingInvokeAnyCancelsUnexecutedUserCode()
    {
        var executor = new ManualExecutor();
        Exception failure = null;
        int calls = 0;
        var caller = new Thread(() =>
        {
            try { executor.invokeAny(new ICallable<int>[] { new AnonymousCallable<int>(() => ++calls) }); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        caller.Start();
        var queued = executor.take();
        Assert.True(SpinWait.SpinUntil(() => (caller.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
            TimeSpan.FromSeconds(5)));
        caller.Interrupt();
        Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<ThreadInterruptedException>(failure);
        executor.run(queued);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void PromiseTaskDescriptionsPreserveRunnableResultsAndCompletionSentinels()
    {
        var executor = new ManualExecutor();
        var runnable = Runnables.Empty;
        var plain = executor.submit(runnable);
        var withResult = executor.submit(runnable, true);
        Assert.Contains("task: " + runnable, plain.ToString());
        Assert.Contains("Callable(task: " + runnable + ", result: true)", withResult.ToString());
        executor.run(executor.take());
        Assert.Contains("task: COMPLETED", plain.ToString());
        Assert.True(withResult.cancel(false));
        Assert.Contains("task: CANCELLED", withResult.ToString());
    }
}
