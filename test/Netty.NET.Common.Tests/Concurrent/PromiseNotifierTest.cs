/*
 * Copyright 2014 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

// CLR replacement: share an existing Task, or use the producer's native
// TrySetFromTask for completion transfer. Cancellation requests require an
// explicitly owned token source, not a writable consumer Future.
public class TaskCompletionTransferPortTest
{
    [Fact]
    public async Task SuccessIsForwardedToEveryProducerWithTheSameResultIdentity()
    {
        var value = new object();
        Task<object> source = Task.FromResult(value);
        var first = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var producer in new[] { first, second }) Assert.True(producer.TrySetFromTask(source));
        Assert.Same(value, await first.Task);
        Assert.Same(value, await second.Task);
    }

    [Fact]
    public async Task FailureIsForwardedToEveryProducerWithItsOriginalIdentity()
    {
        var error = new InvalidOperationException("original");
        Task<object> source = Task.FromException<object>(error);
        var first = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var producer in new[] { first, second }) Assert.True(producer.TrySetFromTask(source));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await first.Task));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await second.Task));
    }

    [Fact]
    public async Task SourceCancellationIsForwardedWithItsToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<object> source = Task.FromCanceled<object>(cancellation.Token);
        var target = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(target.TrySetFromTask(source));
        Assert.True(target.Task.IsCanceled);
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await target.Task);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        // Consumers can simply return the same read-only Task when no distinct
        // producer is needed; no Notifier/cascade object is allocated.
        Task<object> shared = source;
        Assert.Same(source, shared);
    }

    [Fact]
    public async Task ACompletedTargetIsNeverOverwritten()
    {
        var value = new object();
        var target = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        target.SetResult(value);
        Task<object> failed = Task.FromException<object>(new InvalidOperationException("late"));
        Assert.False(target.TrySetFromTask(failed));
        Assert.Same(value, await target.Task);
        // Observe the discarded source failure independently of the target.
        Assert.NotNull(failed.Exception);
    }

    [Fact]
    public async Task AFailureWithCancellationExceptionRemainsFaulted()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = new OperationCanceledException(cancellation.Token);
        Task<object> source = Task.FromException<object>(error);
        var target = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(target.TrySetFromTask(source));
        Assert.True(target.Task.IsFaulted);
        Assert.False(target.Task.IsCanceled);
        Assert.Same(error, await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await target.Task));
    }

    [Fact]
    public async Task AllFailureDetailsSurviveNonGenericCompletionTransfer()
    {
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");
        Task source = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        var target = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(target.TrySetFromTask(source));
        await Assert.ThrowsAnyAsync<Exception>(async () => await target.Task);
        Assert.Equal(2, target.Task.Exception.InnerExceptions.Count);
        Assert.Contains(first, target.Task.Exception.InnerExceptions);
        Assert.Contains(second, target.Task.Exception.InnerExceptions);
    }

    [Fact]
    public async Task NullSuccessAndNestedAggregateErrorsRetainTheirMeaning()
    {
        var success = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(success.TrySetFromTask(Task.FromResult<object>(null)));
        Assert.Null(await success.Task);
        var error = new AggregateException(new InvalidOperationException("one"), new ArgumentException("two"));
        var failure = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(failure.TrySetFromTask(Task.FromException<object>(error)));
        Assert.Same(error, await Assert.ThrowsAsync<AggregateException>(async () => await failure.Task));
    }

    [Fact]
    public void InvalidOrIncompleteSourcesDoNotConsumeTheProducer()
    {
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Throws<ArgumentNullException>(() => target.TrySetFromTask(null));
        Assert.Throws<ArgumentException>(() => target.TrySetFromTask(source.Task));
        Assert.False(target.Task.IsCompleted);
        source.SetResult(new object());
        Assert.True(target.TrySetFromTask(source.Task));
    }

    [Fact]
    public async Task CancelingAWaiterDoesNotCancelTheSharedOperation()
    {
        using var waiterCancellation = new CancellationTokenSource();
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> waiter = source.Task.WaitAsync(waiterCancellation.Token);
        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiter);
        Assert.False(source.Task.IsCompleted);
        source.SetResult(17);
        Assert.Equal(17, await source.Task);
    }

    [Fact]
    public async Task AnExplicitLinkedRequestCancelsAnOwnedCooperativeOperation()
    {
        using var operationCancellation = new CancellationTokenSource();
        using var downstreamRequest = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationCancellation.Token, downstreamRequest.Token);
        var executor = new DefaultEventExecutor();
        var pendingIo = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> operation = executor.SubmitAsync(async token =>
        {
            started.SetResult();
            return await pendingIo.Task.WaitAsync(token).ConfigureAwait(false);
        }, linked.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            downstreamRequest.Cancel();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(linked.Token, error.CancellationToken);
            Assert.True(operation.IsCanceled);
            Assert.False(pendingIo.Task.IsCompleted);
        }
        finally
        {
            pendingIo.TrySetResult(0);
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
