/*
 * Copyright 2026 The Netty Project
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

// Each write completion is counted on the channel executor so an embedded flush completes the aggregate before
// returning. A direct Task.WhenAll continuation can run later on the thread pool.
// Copy the input tasks before registration. Once all writes finish, transfer their combined result, including
// every fault, into the aggregate promise.
internal static class ExecutorPromiseCombiner
{
    // CLR: the combined Task of a new promise for the WriteAsync overrides, whose returned Task replaces the caller's
    // promise. The promise runs its continuations asynchronously, as the Transport's write Tasks it stands for do.
    internal static Task Combine(IChannelHandlerContext ctx, IReadOnlyList<Task> futures)
    {
        TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Combine(ctx, futures, promise);
        return promise.Task;
    }

    // CLR: completes promise with the combined outcome of futures.
    internal static void Combine(IChannelHandlerContext ctx, IReadOnlyList<Task> futures, TaskCompletionSource promise)
    {
        Task[] snapshot = [.. futures];
        if (snapshot.Length == 0)
        {
            promise.TrySetResult();
            return;
        }

        IEventExecutor executor = ctx.Executor;
        TaskScheduler scheduler = new EventExecutorTaskScheduler(() => executor);
        Combiner combiner = new Combiner(snapshot, promise);

        foreach (Task future in snapshot)
        {
            _ = future.ContinueWith(static (_, state) => ((Combiner) state).OperationComplete(), combiner,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);
        }
    }

    private sealed class Combiner
    {
        private readonly Task[] futures;
        private readonly TaskCompletionSource promise;
        private int doneCount;

        internal Combiner(Task[] futures, TaskCompletionSource promise)
        {
            this.futures = futures;
            this.promise = promise;
        }

        // CLR: runs on the executor, one completion at a time.
        internal void OperationComplete()
        {
            if (++doneCount == futures.Length)
            {
                // CLR: every future has completed, so Task.WhenAll returns a completed Task.
                promise.TrySetFromTask(Task.WhenAll(futures));
            }
        }
    }
}
