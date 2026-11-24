/*
 * Copyright 2023 The Netty Project
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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression;

// CLR: the package-private final class is an internal sealed class with the private constructor. ChannelFuture,
// ChannelPromise and Future are Task (docs/transport-porting.md): the promise argument is the returned Task, which a
// TaskCompletionSource completes with the outcome of the close (TrySetFromTask copies "the completion status
// (including exception or cancellation information)",
// https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0,
// as docs/common-task-composition.md records for PromiseNotifier), and cancellationToken is the token the encoder's
// CloseAsync received. The anonymous Runnable is a lambda; executor().schedule(...) is ScheduleAsync with the token of a
// CancellationTokenSource, whose cancellation before invocation "prevents execution" (EventExecutorSchedulingExtensions,
// as AbstractNioChannel cancels its connect timeout), so future.cancel(true) is Cancel(). addListener is a continuation
// that runs synchronously on the thread that completes finishFuture.
internal sealed class EncoderUtil
{
    private const int THREAD_POOL_DELAY_SECONDS = 10;

    internal static Task CloseAfterFinishEncode(IChannelHandlerContext ctx, Task finishFuture,
                                                CancellationToken cancellationToken)
    {
        if (!finishFuture.IsCompleted)
        {
            TaskCompletionSource promise = new TaskCompletionSource();
            // Ensure the channel is closed even if the write operation completes in time.
            CancellationTokenSource future = new CancellationTokenSource();
            _ = ctx.Executor.ScheduleAsync(() =>
            {
                Close(ctx, promise, cancellationToken);
            }, TimeSpan.FromSeconds(THREAD_POOL_DELAY_SECONDS), future.Token);

            _ = finishFuture.ContinueWith(f =>
            {
                // Cancel the scheduled timeout.
                future.Cancel();
                if (!promise.Task.IsCompleted)
                {
                    Close(ctx, promise, cancellationToken);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return promise.Task;
        }
        else
        {
            return ctx.CloseAsync(cancellationToken);
        }
    }

    // CLR: ctx.close(promise) completes the promise with the outcome of the close; here the returned close Task is copied
    // into the TaskCompletionSource (TrySetFromTask, see the class comment).
    private static void Close(IChannelHandlerContext ctx, TaskCompletionSource promise,
                              CancellationToken cancellationToken)
    {
        _ = ctx.CloseAsync(cancellationToken).ContinueWith(static (close, state) =>
                ((TaskCompletionSource) state).TrySetFromTask(close), promise,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private EncoderUtil() { }
}
