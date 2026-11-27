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

// CLR: a CLR-only helper with no Java file of its own. It is new PromiseCombiner(ctx.executor()) with add(future) for
// each future and finish(promise), the use of MessageToMessageEncoder, codec-http HttpObjectEncoder, codec-http2
// CompressorHttp2ConnectionEncoder and codec-http3 Http3FrameToHttpObjectCodec; those assemblies see it through
// InternalsVisibleTo (Netty.NET.Handler.Codec.csproj).
//
// Common maps PromiseCombiner to Task.WhenAll (docs/common-task-composition.md), whose own completion runs where the
// last input Task completes. The Transport's write Tasks run their continuations asynchronously
// (RunContinuationsAsynchronously, src/Netty.NET.Transport/Channels/AbstractChannelHandlerContext.cs), so a
// Task.WhenAll of them completed on a thread-pool thread after the write call had returned: a combined write was not
// complete when WriteAndFlushAsync returned on an EmbeddedChannel, and a listener of it ran late (measured 2026-11-27:
// MessageToMessageEncoderTest.TestCombinedWritesCompleteWhenFlushed,
// HttpResponseEncoderTest.TestChunkedContentWriteCompletesWhenFlushed and ten Http3FrameToHttpObjectCodecTest cases
// failed). The Java combiner listens on its executor, the one it is created with, and completes the aggregate promise
// when the last future completes:
// https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/java/io/netty/util/concurrent/PromiseCombiner.java
// Here each future gets a continuation on Common's EventExecutorTaskScheduler over ctx.Executor, read once, which runs
// it at once when the future completes on the event loop and otherwise hands it to the executor; the continuations
// count the completed futures there, one at a time, and the last one completes the promise.
//
// The outcome stays Common's Task.WhenAll mapping: once every future has completed, Task.WhenAll of them is itself
// complete, and TrySetFromTask copies it into the promise (faulted with every child exception when a child faulted,
// canceled when a child was canceled and none faulted, successful otherwise; docs/common-task-composition.md,
// Deliberate native differences), where Java fails the promise with the first unsuccessful completion. The futures are
// copied when combining, as add() and finish() fix them, and an empty list completes the promise at once, as finish()
// does when no future was added. PromiseCombiner's checkInEventLoop() guard has no counterpart: the counting runs on
// the executor wherever Combine is called from.
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

    // CLR: completes promise with the combined outcome of futures; see the class comment.
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
