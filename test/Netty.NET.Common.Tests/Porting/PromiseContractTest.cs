using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class PromiseContractTest
{
    private sealed class Listener<T> : IFutureListener<T>
    {
        private readonly Action<IFuture<T>> action;
        internal Listener(Action<IFuture<T>> action) { this.action = action; }
        public void operationComplete(IFuture<T> future) => action(future);
        // Promise removal must use reference identity even when listeners override equality.
        public override bool Equals(object obj) => obj is Listener<T>;
        public override int GetHashCode() => 0;
    }
    private sealed class ProgressiveListener<T> : IGenericProgressiveFutureListener<IFuture<T>>
    {
        internal readonly List<(long progress, long total)> updates = new List<(long, long)>();
        internal int completions;
        public void operationProgressed(IFuture<T> future, long progress, long total) => updates.Add((progress, total));
        public void operationComplete(IFuture<T> future) => ++completions;
    }
    private sealed class TypedProgressiveListener<T> : IGenericProgressiveFutureListener<IProgressiveFuture<T>>
    {
        internal readonly List<(long progress, long total)> updates = new();
        internal int completions;
        public void operationProgressed(IProgressiveFuture<T> future, long progress, long total) => updates.Add((progress, total));
        public void operationComplete(IProgressiveFuture<T> future) => ++completions;
    }
    private sealed class TypedPromiseListener<T> : IGenericFutureListener<IPromise<T>>
    {
        internal IPromise<T> completed;
        public void operationComplete(IPromise<T> future) => completed = future;
    }
    [Fact]
    public void ListenersBoundToProgressiveFutureRetainProgressIdentityAndFluentReturns()
    {
        IProgressivePromise<object> promise = ImmediateEventExecutor.INSTANCE.newProgressivePromise<object>();
        var listener = new TypedProgressiveListener<object>();
        var removed = new TypedProgressiveListener<object>();
        Assert.Same(promise, promise.addListeners(listener, removed, listener, null, removed));
        Assert.Same(promise, promise.removeListeners(listener, removed));
        promise.setProgress(1, 2);
        Assert.Equal(new[] { (1L, 2L) }, listener.updates);
        Assert.Empty(removed.updates);
        promise.setSuccess(null);
        Assert.Equal(1, listener.completions);
        Assert.Equal(0, removed.completions);
        Assert.Same(promise, promise.addListener(listener));
        Assert.Equal(2, listener.completions);
    }
    [Fact]
    public void ListenersBoundToPromiseReceiveTheOriginalObject()
    {
        IPromise<object> promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var listener = new TypedPromiseListener<object>();
        var removed = new TypedPromiseListener<object>();
        Assert.Same(promise, promise.addListener(listener));
        promise.addListener(removed);
        promise.removeListener(removed);
        promise.setSuccess(null);
        Assert.Same(promise, listener.completed);
        Assert.Null(removed.completed);
        Assert.Same(promise, promise.removeListener(listener));
    }
    [Fact]
    public void UncancellableCanStillSucceedOrFail()
    {
        foreach (bool fail in new[] { false, true })
        {
            var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
            Assert.True(promise.isCancellable());
            Assert.True(promise.setUncancellable());
            Assert.True(promise.setUncancellable());
            Assert.False(promise.cancel(true));
            Assert.False(promise.isCancellable());
            Assert.False(promise.isDone());
            var value = new object();
            var error = new InvalidOperationException("failure");
            if (fail) Assert.True(promise.tryFailure(error));
            else Assert.True(promise.trySuccess(value));
            Assert.True(promise.isDone());
            Assert.True(promise.setUncancellable());
            Assert.False(promise.trySuccess(value));
            Assert.False(promise.tryFailure(error));
            Assert.Throws<InvalidOperationException>(() => promise.setSuccess(value));
            Assert.Throws<InvalidOperationException>(() => promise.setFailure(error));
            Assert.Equal(!fail, promise.isSuccess());
            Assert.Same(fail ? null : value, promise.getNow());
            Assert.Same(fail ? error : null, promise.cause());
        }
    }
    [Fact]
    public void CancellationHasStablePerPromiseCause()
    {
        var a = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var b = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        Assert.True(a.cancel(false));
        Assert.False(a.cancel(false));
        Assert.True(b.cancel(true));
        Assert.Same(a.cause(), a.cause());
        Assert.NotSame(a.cause(), b.cause());
        Assert.Same(a.cause(), Assert.ThrowsAny<OperationCanceledException>(() => a.get()));
        Assert.Same(a.cause(), Assert.ThrowsAny<OperationCanceledException>(() => a.sync()));
        Assert.False(a.setUncancellable());
        Assert.False(a.trySuccess(null));
        Assert.True(a.Task.IsCanceled);
        Assert.Empty(ThrowableUtil.getSuppressed(a.cause()));
    }
    [Fact]
    public void FailureIdentityIsSharedAcrossListenersGetSyncAndTask()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var error = new InvalidOperationException("original");
        Exception listenerCause = null;
        promise.addListener(new Listener<object>(f => listenerCause = f.cause()));
        promise.setFailure(error);
        Assert.Same(error, listenerCause);
        Assert.Same(error, Assert.Throws<AggregateException>(() => promise.get()).InnerException);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => promise.sync()));
        Assert.Single(ThrowableUtil.getSuppressed(error));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => promise.syncUninterruptibly()));
        Assert.Single(ThrowableUtil.getSuppressed(error));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => promise.Task.GetAwaiter().GetResult()));
    }
    [Fact]
    public void NullFailureDoesNotConsumeThePromise()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        Assert.Throws<ArgumentNullException>(() => promise.tryFailure(null));
        Assert.Throws<ArgumentNullException>(() => promise.setFailure(null));
        Assert.False(promise.isDone());
        Assert.True(promise.trySuccess(null));
        Assert.Null(promise.Task.GetAwaiter().GetResult());
        Assert.Contains("(success)", promise.ToString());
    }
    [Fact]
    public void SyncPreservesPreviouslySuppressedExceptions()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var error = new InvalidOperationException("original");
        var diagnostic = new Exception("existing");
        ThrowableUtil.addSuppressed(error, diagnostic);
        promise.setFailure(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => promise.sync()));
        Assert.Equal(new[] { diagnostic }, ThrowableUtil.getSuppressed(error));
        var snapshot = ThrowableUtil.getSuppressed(error);
        snapshot[0] = null;
        Assert.Same(diagnostic, ThrowableUtil.getSuppressed(error)[0]);
        Assert.Throws<ArgumentException>(() => ThrowableUtil.addSuppressed(error, error));
        ThrowableUtil.addSuppressed(error, (Exception)null);
        var others = new List<Exception> { diagnostic, new Exception("another") };
        ThrowableUtil.addSuppressedAndClear(error, others);
        Assert.Empty(others);
        Assert.Equal(3, ThrowableUtil.getSuppressed(error).Length);
    }
    [Fact]
    public void ListenerRemovalUsesIdentityAndRemovesOnlyFirstOccurrence()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var calls = new List<int>();
        var a = new Listener<object>(_ => calls.Add(1));
        var b = new Listener<object>(_ => calls.Add(2));
        promise.addListeners(a, b, a, null, new Listener<object>(_ => calls.Add(99)));
        promise.removeListener(new Listener<object>(_ => calls.Add(99)));
        promise.removeListeners(a, null, b);
        promise.setSuccess(null);
        Assert.Equal(new[] { 2, 1 }, calls);
    }
    [Fact]
    public void ExceptionInAListenerDoesNotSuppressLaterOrReentrantListeners()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var calls = new List<int>();
        promise.addListener(new Listener<object>(f =>
        {
            calls.Add(1);
            f.addListener(new Listener<object>(_ => calls.Add(3)));
            throw new InvalidOperationException("listener failure");
        }));
        promise.addListener(new Listener<object>(_ => calls.Add(2)));
        promise.setSuccess(null);
        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }
    [Fact]
    public void ConcurrentCompletionHasOneWinnerAndOneNotification()
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
            int notifications = 0;
            promise.addListener(new Listener<object>(_ => Interlocked.Increment(ref notifications)));
            var value = new object();
            var error = new InvalidOperationException("race");
            int wins = 0;
            Parallel.Invoke(
                () => { if (promise.trySuccess(value)) Interlocked.Increment(ref wins); },
                () => { if (promise.tryFailure(error)) Interlocked.Increment(ref wins); },
                () => { if (promise.cancel(false)) Interlocked.Increment(ref wins); });
            Assert.Equal(1, wins);
            Assert.Equal(1, notifications);
            Assert.True(promise.Task.IsCompleted);
            if (promise.isSuccess()) Assert.Same(value, promise.Task.GetAwaiter().GetResult());
            else if (promise.isCancelled()) Assert.True(promise.Task.IsCanceled);
            else Assert.Same(error, promise.cause());
        }
    }
    [Fact]
    public void AddingListenersWhileCompletingNotifiesEachExactlyOnce()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        var counts = new int[1000];
        Parallel.Invoke(
            () => Parallel.For(0, counts.Length, i => promise.addListener(new Listener<object>(_ => Interlocked.Increment(ref counts[i])))),
            () => promise.setSuccess(null));
        Assert.All(counts, count => Assert.Equal(1, count));
    }
    [Fact]
    public void MultipleBlockingWaitersAreReleased()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        using var ready = new CountdownEvent(8);
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() => { ready.Signal(); promise.awaitUninterruptibly(); }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        promise.setSuccess(null);
        foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public void TimeoutsDoNotCompletePromiseAndHugeTimeoutDoesNotOverflow()
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        Assert.False(promise.await(0));
        Assert.False(promise.await(-1));
        Assert.False(promise.await(TimeSpan.FromMilliseconds(1)));
        Assert.Throws<TimeoutException>(() => promise.get(TimeSpan.Zero));
        var thread = new Thread(() => { Thread.Sleep(10); promise.setSuccess(null); }) { IsBackground = true };
        thread.Start();
        Assert.True(promise.await(long.MaxValue));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(promise.await(TimeSpan.MinValue));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptibleWaitConsumesPendingInterrupt(bool timed)
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                Assert.Throws<ThreadInterruptedException>(() => { if (timed) promise.await(TimeSpan.FromSeconds(5)); else promise.await(); });
                Thread.Sleep(0);
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.False(promise.isDone());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedPromisePreservesInterruptButCompleteFutureAwaitConsumesIt(bool immutableFuture)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var executor = ImmediateEventExecutor.INSTANCE;
                IFuture<object> future;
                if (immutableFuture) future = executor.newSucceededFuture<object>(null);
                else { var promise = executor.newPromise<object>(); promise.setSuccess(null); future = promise; }
                Thread.CurrentThread.Interrupt();
                if (immutableFuture)
                {
                    Assert.Throws<ThreadInterruptedException>(() => future.await());
                    Thread.Sleep(0);
                }
                else
                {
                    Assert.Same(future, future.await());
                    Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(0));
                }
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UninterruptibleWaitRestoresInterrupt(bool timed)
    {
        var promise = ImmediateEventExecutor.INSTANCE.newPromise<object>();
        using var ready = new CountdownEvent(1);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                ready.Signal();
                if (timed) Assert.False(promise.awaitUninterruptibly(TimeSpan.FromMilliseconds(30)));
                else promise.awaitUninterruptibly();
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(0));
                Thread.Sleep(0);
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        if (!timed)
        {
            // Wait until Monitor.Wait has consumed the pending interrupt before completing.
            Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            promise.setSuccess(null);
        }
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
    [Fact]
    public void BlockingInsideEventLoopFailsWithoutBlockingOtherTasks()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            Exception failure = null;
            using var complete = new CountdownEvent(1);
            executor.execute(Runnables.Create(() =>
            {
                try
                {
                    var promise = executor.newPromise<object>();
                    Assert.Throws<BlockingOperationException>(() => promise.await());
                    Assert.Throws<BlockingOperationException>(() => promise.await(TimeSpan.FromSeconds(1)));
                    Assert.Throws<BlockingOperationException>(() => promise.awaitUninterruptibly());
                    Assert.False(promise.await(0));
                    promise.setSuccess(null);
                    Assert.Same(promise, promise.await());
                }
                catch (Exception error) { failure = error; }
                finally { complete.Signal(); }
            }));
            Assert.True(complete.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
        }
        finally { Assert.True(executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5))); }
    }
    [Fact]
    public void CompletionAndLateListenersUseAssignedExecutor()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var promise = executor.newPromise<object>();
            using var completion = new CountdownEvent(2);
            int correctThread = 0;
            var listener = new Listener<object>(_ => { if (executor.inEventLoop()) Interlocked.Increment(ref correctThread); completion.Signal(); });
            promise.addListener(listener);
            promise.setSuccess(null);
            promise.addListener(listener);
            Assert.True(completion.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, correctThread);
        }
        finally { Assert.True(executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5))); }
    }
    [Fact]
    public void ProgressiveNotificationFiltersListenersAndNormalizesUnknownTotal()
    {
        IProgressivePromise<object> promise = ImmediateEventExecutor.INSTANCE.newProgressivePromise<object>();
        var first = new ProgressiveListener<object>();
        var second = new ProgressiveListener<object>();
        int normalCompletions = 0;
        promise.addListeners(first, new Listener<object>(_ => ++normalCompletions), second);
        Assert.Same(promise, promise.setProgress(0, 10));
        Assert.True(promise.tryProgress(10, 10));
        promise.removeListener(first);
        promise.setProgress(12, -2);
        Assert.Equal(new[] { (0L, 10L), (10L, 10L) }, first.updates);
        Assert.Equal(new[] { (0L, 10L), (10L, 10L), (12L, -1L) }, second.updates);
        Assert.False(promise.tryProgress(-1, -1));
        Assert.False(promise.tryProgress(11, 10));
        Assert.Throws<ArgumentException>(() => promise.setProgress(-1, 10));
        Assert.Throws<ArgumentException>(() => promise.setProgress(11, 10));
        promise.setSuccess(null);
        Assert.False(promise.tryProgress(0, 0));
        Assert.Throws<InvalidOperationException>(() => promise.setProgress(0, 0));
        Assert.Equal(0, first.completions);
        Assert.Equal(1, second.completions);
        Assert.Equal(1, normalCompletions);
        IProgressiveFuture<object> future = promise;
        Assert.Same(promise, future.sync());
        Assert.Same(promise, future.addListener(new Listener<object>(_ => ++normalCompletions)));
        Assert.Equal(2, normalCompletions);
    }
    [Fact]
    public void CompleteFuturesRetainFailureIdentityAndCannotCancel()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        object value = new object();
        var success = executor.newSucceededFuture(value);
        Assert.Same(value, success.getNow());
        Assert.Same(value, success.Task.GetAwaiter().GetResult());
        Assert.True(success.isSuccess());
        Assert.True(success.isDone());
        Assert.False(success.isCancellable());
        Assert.False(success.cancel(true));
        var error = new InvalidOperationException("failure");
        var failure = executor.newFailedFuture<object>(error);
        Assert.Same(error, failure.cause());
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => failure.sync()));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => failure.syncUninterruptibly()));
        Assert.Same(error, Assert.Throws<AggregateException>(() => failure.get()).InnerException);
        Assert.False(failure.isSuccess());
        Assert.True(failure.isDone());
        Assert.False(failure.isCancelled());
        Assert.False(failure.cancel(false));
        Assert.Null(failure.getNow());
    }
    [Fact]
    public void CompleteFutureNotifiesOnlyListenersBeforeNullTerminator()
    {
        var future = ImmediateEventExecutor.INSTANCE.newSucceededFuture<object>(null);
        int calls = 0;
        var listener = new Listener<object>(_ => ++calls);
        future.addListeners(listener, null, listener);
        Assert.Equal(1, calls);
        Assert.Same(future, future.removeListener(null));
        Assert.Same(future, future.removeListeners(null));
        Assert.Throws<ArgumentNullException>(() => future.addListener(null));
        Assert.Throws<ArgumentNullException>(() => future.addListeners(null));
    }
    [Fact]
    public void NotifierClonesItsPromiseArrayAndPropagatesTheSameValue()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var first = executor.newPromise<object>();
        var second = executor.newPromise<object>();
        var replaced = executor.newPromise<object>();
        IPromise<object>[] targets = { first, second };
        var notifier = new PromiseNotifier<object, IFuture<object>>(targets);
        targets[0] = replaced;
        var value = new object();
        notifier.operationComplete(executor.newSucceededFuture(value));
        Assert.Same(value, first.getNow());
        Assert.Same(value, second.getNow());
        Assert.False(replaced.isDone());
    }
    [Fact]
    public void CascadePropagatesTargetCancellationBackToSource()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var source = executor.newPromise<object>();
        var target = executor.newPromise<object>();
        PromiseNotifier<object, IPromise<object>>.cascade(source, target);
        Assert.True(target.cancel(true));
        Assert.True(source.isCancelled());
        Assert.False(source.trySuccess(new object()));
    }
    [Fact]
    public void CascadePreservesFailureIdentityAndDoesNotOverwriteCompletedTargets()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var source = executor.newPromise<object>();
        var target = executor.newPromise<object>();
        PromiseNotifier<object, IPromise<object>>.cascade(source, target);
        var cause = new InvalidOperationException("original");
        source.setFailure(cause);
        Assert.Same(cause, target.cause());
        var alreadyCompleted = executor.newPromise<object>();
        var value = new object();
        alreadyCompleted.setSuccess(value);
        PromiseNotifier<object, IFuture<object>>.cascade(false, executor.newFailedFuture<object>(cause), alreadyCompleted);
        Assert.Same(value, alreadyCompleted.getNow());
        Assert.True(alreadyCompleted.isSuccess());
    }
    [Fact]
    public void CombinerAcceptsDifferentGenericValuesAndAlreadyCompletedFutures()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var combiner = new PromiseCombiner(executor);
        combiner.addAll(executor.newSucceededFuture(123), executor.newSucceededFuture("text"));
        var aggregate = executor.newPromise<Netty.NET.Common.Concurrent.Void>();
        combiner.finish(aggregate);
        Assert.True(aggregate.isSuccess());
        Assert.Null(aggregate.getNow());
    }
    [Fact]
    public void CombinerMarshalsForeignCompletionsAndRetainsFirstFailureIdentity()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var first = ImmediateEventExecutor.INSTANCE.newPromise<object>();
            var second = ImmediateEventExecutor.INSTANCE.newPromise<int>();
            var aggregate = executor.newPromise<Netty.NET.Common.Concurrent.Void>();
            using var ready = new CountdownEvent(1);
            using var notified = new CountdownEvent(1);
            bool correctThread = false;
            aggregate.addListener(new Listener<Netty.NET.Common.Concurrent.Void>(_ => { correctThread = executor.inEventLoop(); notified.Signal(); }));
            executor.execute(Runnables.Create(() =>
            {
                var combiner = new PromiseCombiner(executor);
                combiner.addAll(first, second);
                combiner.finish(aggregate);
                ready.Signal();
            }));
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
            var cause = new InvalidOperationException("first failure");
            first.setFailure(cause);
            Assert.False(aggregate.isDone());
            second.setFailure(new Exception("later failure"));
            Assert.True(notified.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(correctThread);
            Assert.Same(cause, aggregate.cause());
            Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => aggregate.Task.GetAwaiter().GetResult()));
        }
        finally { Assert.True(executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5))); }
    }
}
