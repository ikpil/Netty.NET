using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class PromiseContractTest
{
    [Fact]
    public async Task ProgressRegistrationsRetainSourceIdentityAndIndependentRemoval()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, source.Task);
        var updates = new List<TransferProgress>();
        var removedUpdates = new List<TransferProgress>();
        int completions = 0, removedCompletions = 0;
        Task observed = null;
        Action<TransferProgress> progress = updates.Add;
        Action<Task> completed = task => { observed = task; ++completions; };
        using var first = reporter.Register(progress, completed);
        using var removed = reporter.Register(removedUpdates.Add, _ => ++removedCompletions);
        using var duplicate = reporter.Register(progress, completed);
        first.Dispose();
        removed.Dispose();
        reporter.Report(new TransferProgress(1, 2));
        Assert.Equal(new[] { new TransferProgress(1, 2) }, updates);
        Assert.Empty(removedUpdates);
        source.SetResult(null);
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        await duplicate.NotificationsCompleted;
        Assert.True(first.NotificationsCompleted.IsCanceled);
        Assert.True(removed.NotificationsCompleted.IsCanceled);
        Assert.Same(source.Task, observed);
        Assert.Equal(1, completions);
        Assert.Equal(0, removedCompletions);
        using var late = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        using var registration = late.Register(completed);
        await registration.NotificationCompleted;
        Assert.Equal(2, completions);
    }
    [Fact]
    public async Task CompletionRegistrationsReceiveTheOriginalSourceTask()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        Task observed = null, removedTask = null;
        using var listener = observation.Register(task => observed = task);
        using var removed = observation.Register(task => removedTask = task);
        removed.Dispose();
        source.SetResult(null);
        await listener.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(source.Task, observed);
        Assert.Null(removedTask);
        Assert.True(removed.NotificationCompleted.IsCanceled);
    }
    [Fact]
    public async Task CommittedNativeSubmissionCanStillSucceedOrFailAfterCancellationRequest()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            foreach (bool fail in new[] { false, true })
            {
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                using var cancellation = new CancellationTokenSource();
                var value = new object();
                var error = new InvalidOperationException("failure");
                Task<object> operation = executor.SubmitAsync(_ =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("producer was not released");
                    if (fail) throw error;
                    return value;
                }, cancellation.Token);
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    cancellation.Cancel();
                    Assert.False(operation.IsCompleted);
                }
                finally { release.Set(); }
                if (fail)
                    Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await operation.WaitAsync(TimeSpan.FromSeconds(5))));
                else Assert.Same(value, await operation.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.False(operation.IsCanceled);
            }
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }
    [Fact]
    public async Task CancellationRetainsProducerTokenWithoutInventingStableExceptionIdentity()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var first = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(first.TrySetCanceled(cancellation.Token));
        Assert.False(first.TrySetCanceled(cancellation.Token));
        Assert.True(second.TrySetCanceled(cancellation.Token));
        foreach (var task in new[] { first.Task, second.Task })
        {
            var awaited = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            var blocking = Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.Equal(cancellation.Token, awaited.CancellationToken);
            Assert.Equal(cancellation.Token, blocking.CancellationToken);
            Assert.True(task.IsCanceled);
        }
        Assert.False(first.TrySetResult(null));
        Assert.False(first.TrySetException(new Exception("late failure")));
    }
    [Fact]
    public async Task FailureIdentityIsSharedAcrossObservationAwaitAndSynchronousTaskViews()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("original");
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        Exception listenerCause = null;
        using var listener = observation.Register(task => listenerCause = task.Exception.InnerException);
        source.SetException(error);
        await listener.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(error, listenerCause);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.Task));
        Assert.Same(error, Assert.Throws<AggregateException>(() => source.Task.Result).InnerException);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => source.Task.GetAwaiter().GetResult()));
        Assert.Empty(ThrowableUtil.getSuppressed(error));
    }
    [Fact]
    public void NullFailureDoesNotConsumeTheProducer()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Throws<ArgumentNullException>(() => source.TrySetException((Exception)null));
        Assert.Throws<ArgumentNullException>(() => source.SetException((Exception)null));
        Assert.False(source.Task.IsCompleted);
        Assert.True(source.TrySetResult(null));
        Assert.Null(source.Task.GetAwaiter().GetResult());
        Assert.False(source.TrySetResult(new object()));
        Assert.Throws<InvalidOperationException>(() => source.SetResult(null));
    }
    [Fact]
    public void TaskFailurePreservesPreviouslyAttachedDiagnosticsWithoutAddingJavaSuppression()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("original");
        var diagnostic = new Exception("existing");
        ThrowableUtil.addSuppressed(error, diagnostic);
        source.SetException(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => source.Task.GetAwaiter().GetResult()));
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
    public async Task RegistrationRemovalUsesItsHandleAndRetainsOtherDuplicateCallbacks()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        var calls = new List<int>();
        Action<Task> callback = _ => calls.Add(1);
        using var first = observation.Register(callback);
        using var other = observation.Register(_ => calls.Add(2));
        using var duplicate = observation.Register(callback);
        first.Dispose();
        source.SetResult(null);
        await Task.WhenAll(other.NotificationCompleted, duplicate.NotificationCompleted).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 2, 1 }, calls);
        Assert.True(first.NotificationCompleted.IsCanceled);
    }
    [Fact]
    public async Task CallbackFailureDoesNotSuppressLaterOrReentrantRegistrations()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        var calls = new List<int>();
        CompletionRegistration added = null;
        using var first = observation.Register(_ =>
        {
            calls.Add(1);
            added = observation.Register(_ => calls.Add(3));
            throw new InvalidOperationException("listener failure");
        });
        using var second = observation.Register(_ => calls.Add(2));
        source.SetResult(null);
        await Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted).WaitAsync(TimeSpan.FromSeconds(5));
        await added.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        added.Dispose();
        Assert.Equal(new[] { 1, 2, 3 }, calls);
        Assert.True(source.Task.IsCompletedSuccessfully);
    }
    [Fact]
    public async Task ConcurrentProducerCompletionHasOneWinnerAndOneNotification()
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
            int notifications = 0;
            using var listener = observation.Register(_ => Interlocked.Increment(ref notifications));
            var value = new object();
            var error = new InvalidOperationException("race");
            int wins = 0;
            Parallel.Invoke(
                () => { if (source.TrySetResult(value)) Interlocked.Increment(ref wins); },
                () => { if (source.TrySetException(error)) Interlocked.Increment(ref wins); },
                () => { if (source.TrySetCanceled()) Interlocked.Increment(ref wins); });
            await listener.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, wins);
            Assert.Equal(1, notifications);
            Assert.True(source.Task.IsCompleted);
            if (source.Task.IsCompletedSuccessfully) Assert.Same(value, await source.Task);
            else if (source.Task.IsCanceled)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.Task);
            else Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.Task));
        }
    }
    [Fact]
    public async Task RegisteringWhileCompletingNotifiesEachExactlyOnce()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        var counts = new int[1000];
        var registrations = new CompletionRegistration[counts.Length];
        Parallel.Invoke(
            () => Parallel.For(0, counts.Length, i =>
                registrations[i] = observation.Register(_ => Interlocked.Increment(ref counts[i]))),
            () => source.SetResult(null));
        await Task.WhenAll(registrations.Select(registration => registration.NotificationCompleted)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(counts, count => Assert.Equal(1, count));
        foreach (var registration in registrations) registration.Dispose();
    }
    [Fact]
    public async Task MultipleNativeAwaitersAreReleased()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ready = new CountdownEvent(8);
        async Task<object> Wait()
        {
            ready.Signal();
            return await source.Task;
        }
        var waiters = Enumerable.Range(0, 8).Select(_ => Wait()).ToArray();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        var value = new object();
        source.SetResult(value);
        var results = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.Same(value, result));
    }
    [Fact]
    public async Task ObserverTimeoutDoesNotCompleteTheSourceAndNativeTimeoutBoundsAreExplicit()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.ThrowsAsync<TimeoutException>(async () => await source.Task.WaitAsync(TimeSpan.Zero));
        await Assert.ThrowsAsync<TimeoutException>(async () => await source.Task.WaitAsync(TimeSpan.FromMilliseconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Task.WaitAsync(TimeSpan.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Task.WaitAsync(TimeSpan.MinValue));
        Assert.False(source.Task.IsCompleted);
        Task<object> infinite = source.Task.WaitAsync(Timeout.InfiniteTimeSpan);
        var value = new object();
        source.SetResult(value);
        Assert.Same(value, await infinite.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBlockingWaitObservesThreadInterruptWithoutCompletingTheSource(bool timed)
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                Assert.Throws<ThreadInterruptedException>(() =>
                {
                    if (timed) source.Task.Wait(TimeSpan.FromSeconds(5));
                    else source.Task.GetAwaiter().GetResult();
                });
                Thread.Sleep(0);
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.False(source.Task.IsCompleted);
        source.SetResult(null);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedTasksPreservePendingThreadInterrupts(bool completedProducer)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Task<object> result;
                if (completedProducer)
                {
                    var producer = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    producer.SetResult(null);
                    result = producer.Task;
                }
                else result = Task.FromResult<object>(null);
                Thread.CurrentThread.Interrupt();
                Assert.Null(result.GetAwaiter().GetResult());
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(0));
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
    public async Task ObserverCancellationLeavesTheProducerPending(bool timed)
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        Task<object> waiting = timed
            ? source.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token)
            : source.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(source.Task.IsCompleted);
        var value = new object();
        source.SetResult(value);
        Assert.Same(value, await source.Task);
    }
    [Fact]
    public async Task AwaitingInsideNativeSubmissionAllowsTheLoopToCompleteTheProducer()
    {
        var executor = new DefaultEventExecutor();
        using var entered = new ManualResetEventSlim();
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            bool invokedOnLoop = false;
            Task<bool> consumer = executor.SubmitAsync(async () =>
            {
                invokedOnLoop = executor.inEventLoop();
                entered.Set();
                await source.Task.ConfigureAwait(false);
                return await executor.SubmitAsync(() => executor.inEventLoop());
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(consumer.IsCompleted);
            await executor.SubmitAsync(() => source.SetResult(new object())).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await consumer.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(invokedOnLoop);
        }
        finally
        {
            source.TrySetResult(null);
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
    }
    [Fact]
    public async Task EarlyAndLateCompletionRegistrationsUseTheAssignedExecutor()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observation = new ExecutorCompletion(executor, source.Task);
            int correctThread = 0;
            Action<Task> callback = _ => { if (executor.inEventLoop()) Interlocked.Increment(ref correctThread); };
            using var early = observation.Register(callback);
            source.SetResult(null);
            using var late = observation.Register(callback);
            await Task.WhenAll(early.NotificationCompleted, late.NotificationCompleted).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, correctThread);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }
    [Fact]
    public async Task ProgressSubscriptionsFilterReportsAndNormalizeUnknownTotal()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, source.Task);
        var firstUpdates = new List<TransferProgress>();
        var secondUpdates = new List<TransferProgress>();
        int firstCompletions = 0, secondCompletions = 0, normalCompletions = 0;
        using var first = reporter.Register(firstUpdates.Add, _ => ++firstCompletions);
        using var normal = reporter.Register(null, _ => ++normalCompletions);
        using var second = reporter.Register(secondUpdates.Add, _ => ++secondCompletions);
        reporter.Report(new TransferProgress(0, 10));
        Assert.True(reporter.TryReport(10, 10));
        first.Dispose();
        reporter.Report(new TransferProgress(12, -2));
        Assert.Equal(new[] { new TransferProgress(0, 10), new TransferProgress(10, 10) }, firstUpdates);
        Assert.Equal(new[] { new TransferProgress(0, 10), new TransferProgress(10, 10), new TransferProgress(12) }, secondUpdates);
        Assert.False(reporter.TryReport(-1, -1));
        Assert.False(reporter.TryReport(11, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(11, 10));
        source.SetResult(null);
        Assert.False(reporter.TryReport(0, 0));
        Assert.Throws<InvalidOperationException>(() => reporter.Report(new TransferProgress(0, 0)));
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.NotificationsCompleted.IsCanceled);
        Assert.True(second.NotificationsCompleted.IsCompletedSuccessfully);
        Assert.True(normal.NotificationsCompleted.IsCompletedSuccessfully);
        Assert.Equal(0, firstCompletions);
        Assert.Equal(1, secondCompletions);
        Assert.Equal(1, normalCompletions);
        Assert.Null(await source.Task);
        using var completion = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        using var late = completion.Register(_ => ++normalCompletions);
        await late.NotificationCompleted;
        Assert.Equal(2, normalCompletions);
    }
    [Fact]
    public void CompletedTasksRetainResultAndFailureIdentityAndCannotBeChangedByObserverCancellation()
    {
        object value = new object();
        var success = Task.FromResult(value);
        Assert.Same(value, success.GetAwaiter().GetResult());
        Assert.True(success.IsCompletedSuccessfully);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Same(success, success.WaitAsync(cancellation.Token));
        Assert.Same(value, success.WaitAsync(cancellation.Token).GetAwaiter().GetResult());
        var error = new InvalidOperationException("failure");
        var failure = Task.FromException<object>(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => failure.GetAwaiter().GetResult()));
        Assert.Same(error, Assert.Throws<AggregateException>(() => failure.Result).InnerException);
        Assert.Same(error, failure.Exception.InnerException);
        Assert.True(failure.IsFaulted);
        Assert.False(failure.IsCanceled);
        Assert.Same(failure, failure.WaitAsync(cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => Task.FromException<object>(null));
    }
    [Fact]
    public void CompletedTaskUsesExplicitRegistrationsAndRejectsNullCallbacks()
    {
        var result = Task.FromResult<object>(null);
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, result);
        int calls = 0;
        Action<Task> callback = task => { Assert.Same(result, task); ++calls; };
        using var first = observation.Register(callback);
        Assert.True(first.NotificationCompleted.IsCompletedSuccessfully);
        Assert.Throws<ArgumentNullException>(() => observation.Register(null));
        using var second = observation.Register(callback);
        Assert.Equal(2, calls);
        first.Dispose();
        second.Dispose();
        Assert.Equal(2, calls);
    }
}
