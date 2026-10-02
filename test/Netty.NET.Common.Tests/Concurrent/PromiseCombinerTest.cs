/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

// CLR replacement: PromiseCombiner is List<Task> plus Task.WhenAll. The original
// scenarios and the intentionally different native ownership/error policies are
// mapped in docs/common-task-composition.md; there is no Java builder facade.
public class TaskWhenAllPortTest
{
    [Fact]
    public void NullTaskArrayIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => Task.WhenAll((Task[])null));

    [Fact]
    public void NullTaskEntryIsRejected() =>
        Assert.Throws<ArgumentException>(() => Task.WhenAll(new Task[] { Task.CompletedTask, null }));

    [Fact]
    public async Task AnEmptyBatchSucceedsWithoutAnAggregateProducer()
    {
        Task result = Task.WhenAll(Array.Empty<Task>());
        Assert.True(result.IsCompletedSuccessfully);
        await result;
    }

    [Fact]
    public async Task AllSuccessWaitsForEveryOperation()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task result = Task.WhenAll(first.Task, second.Task);
        first.SetResult();
        Assert.False(result.IsCompleted);
        second.SetResult();
        await result;
        Assert.True(result.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task IncrementallyCollectedAlreadyCompletedTasksSucceed()
    {
        var tasks = new List<Task> { Task.FromResult(123) };
        tasks.Add(Task.FromResult("text"));
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task AllFailuresWaitAndRetainEachOriginalException()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstError = new InvalidOperationException("first");
        var secondError = new ArgumentException("second");
        Task result = Task.WhenAll(first.Task, second.Task);
        first.SetException(firstError);
        Assert.False(result.IsCompleted);
        second.SetException(secondError);
        Exception observed = await Assert.ThrowsAnyAsync<Exception>(async () => await result);
        Assert.Contains(observed, new Exception[] { firstError, secondError });
        Assert.Equal(2, result.Exception.InnerExceptions.Count);
        Assert.Contains(firstError, result.Exception.InnerExceptions);
        Assert.Contains(secondError, result.Exception.InnerExceptions);
    }

    [Fact]
    public async Task AlreadyCompletedFailuresRetainTheirOriginalIdentity()
    {
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");
        Task result = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        await Assert.ThrowsAnyAsync<Exception>(async () => await result);
        Assert.Contains(first, result.Exception.InnerExceptions);
        Assert.Contains(second, result.Exception.InnerExceptions);
    }

    [Fact]
    public async Task AddingToTheCollectionAfterWhenAllDoesNotChangeThatBatch()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task> { first.Task };
        Task result = Task.WhenAll(tasks);
        tasks.Add(later.Task);
        first.SetResult();
        await result;
        Assert.False(later.Task.IsCompleted);
        later.SetResult();
    }

    [Fact]
    public async Task MultipleObserversCanAggregateTheSameTasks()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = Task.WhenAll(source.Task);
        Task second = Task.WhenAll(source.Task);
        source.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DuplicateInputsDoNotDuplicateOrChangeTheOperation()
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int[]> result = Task.WhenAll(source.Task, source.Task);
        source.SetResult(9);
        Assert.Equal(new[] { 9, 9 }, await result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARealFaultTakesPrecedenceOverCancellation(bool cancelFirst)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("write failed");
        Task result = Task.WhenAll(canceled.Task, failed.Task);
        if (cancelFirst) canceled.SetCanceled(cancellation.Token);
        failed.SetException(error);
        if (!cancelFirst) canceled.SetCanceled(cancellation.Token);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await result));
        Assert.True(result.IsFaulted);
    }

    [Fact]
    public async Task CancellationWithoutFailureWaitsForRemainingSuccessfulWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task result = Task.WhenAll(first.Task, second.Task);
        first.SetCanceled(cancellation.Token);
        Assert.False(result.IsCompleted);
        second.SetResult();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(result.IsCanceled);
    }

    [Fact]
    public async Task ForeignCompletionsAreTransferredOnTheRealEventLoop()
    {
        var executor = new DefaultEventExecutor();
        var first = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aggregate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task forwarding = executor.SubmitAsync(async () =>
        {
            Assert.True(executor.inEventLoop());
            Task all = Task.WhenAll(new Task[] { first.Task, second.Task });
            ready.SetResult();
            try { await all.ConfigureAwait(false); }
            catch (Exception) { /* Transfer the complete native status, not the await exception alone. */ }
            await executor.SubmitAsync(() =>
            {
                Assert.True(executor.inEventLoop());
                Assert.True(aggregate.TrySetFromTask(all));
            });
        });
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var firstError = new InvalidOperationException("foreign write one");
            var secondError = new ArgumentException("foreign write two");
            first.SetException(firstError);
            Assert.False(aggregate.Task.IsCompleted);
            second.SetException(secondError);
            await forwarding.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<Exception>(async () => await aggregate.Task);
            Assert.Contains(firstError, aggregate.Task.Exception.InnerExceptions);
            Assert.Contains(secondError, aggregate.Task.Exception.InnerExceptions);
        }
        finally
        {
            first.TrySetResult(null);
            second.TrySetResult(0);
            await forwarding.WaitAsync(TimeSpan.FromSeconds(5));
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ReentrantWriteCollectionIncludesNewWorkBeforeAggregation()
    {
        // PendingWriteQueue.removeAndWriteAll can revive the queue synchronously
        // while writing. Collect native Tasks until it drains, then call WhenAll.
        var executor = new DefaultEventExecutor();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Task batch = await executor.SubmitAsync<Task>(() =>
            {
                var queue = new Queue<Func<Task>>();
                var writes = new List<Task>();
                queue.Enqueue(() => { queue.Enqueue(() => revived.Task); return first.Task; });
                while (queue.TryDequeue(out Func<Task> write)) writes.Add(write());
                Assert.Equal(2, writes.Count);
                return Task.WhenAll(writes);
            });
            first.SetResult();
            Assert.False(batch.IsCompleted);
            revived.SetResult();
            await batch.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            first.TrySetResult();
            revived.TrySetResult();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
