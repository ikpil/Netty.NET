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
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

// The deprecated Java aggregator can complete other producers on failure.
// Native consumers aggregate read-only Tasks and explicitly own cancellation
// requests. See the per-scenario migration map in common-task-composition.md.
public class TaskAggregationOwnershipPortTest
{
    [Fact]
    public async Task TestNullAggregatePromise()
    {
        // CLR: no required aggregate-producer argument exists. An optional owner uses
        // TrySetFromTask and cannot overwrite its result.
        Task result = Task.WhenAll(Task.CompletedTask);
        var owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(owner.TrySetFromTask(result));
        await owner.Task;
        Assert.False(owner.TrySetFromTask(result));
    }

    [Fact]
    public void TestAddNullFuture()
    {
        Assert.Throws<ArgumentNullException>(() => Task.WhenAll((Task[])null));
    }

    [Fact]
    public async Task TestSuccessfulNoPending()
    {
        await Task.WhenAll(Array.Empty<Task>());
    }

    [Fact]
    public async Task TestSuccessfulPending()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // CLR: the source add(p1, null, p2) silently skips the null entry; the native aggregation rejects it
        // instead.
        Assert.Throws<ArgumentException>(() => Task.WhenAll(first.Task, null, second.Task));
        Task result = Task.WhenAll(first.Task, second.Task);
        first.SetResult();
        Assert.False(result.IsCompleted);
        second.SetResult();
        await result;
    }

    [Fact]
    public async Task TestFailedFutureFailPending()
    {
        // CLR: a failure cannot manufacture sibling producer outcomes; WhenAll waits for
        // their real completion and explicit cancellation remains the owner's choice.
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("original");
        Task result = Task.WhenAll(first.Task, second.Task);
        first.SetException(error);
        Assert.False(second.Task.IsCompleted);
        Assert.False(result.IsCompleted);
        second.SetResult();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await result));
        Assert.True(second.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TestFailedFutureNoFailPending()
    {
        // CLR: siblings remain unaffected; WhenAny supplies early observation and WhenAll
        // retains the eventual aggregate failure.
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task all = Task.WhenAll(new Task[] { first.Task, second.Task });
        var error = new InvalidOperationException("original");
        first.SetException(error);
        Task failed = await Task.WhenAny(first.Task, second.Task);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await failed));
        Assert.False(second.Task.IsCompleted);
        second.SetResult(23);
        Assert.Equal(23, await second.Task);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await all);
    }
}
