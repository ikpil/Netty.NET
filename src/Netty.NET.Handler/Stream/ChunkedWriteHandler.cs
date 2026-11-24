/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Stream;

/**
 * A {@link ChannelHandler} that adds support for writing a large data stream
 * asynchronously neither spending a lot of memory nor getting
 * {@link OutOfMemoryError}.  Large data streaming such as file
 * transfer requires complicated state management in a {@link ChannelHandler}
 * implementation.  {@link ChunkedWriteHandler} manages such complicated states
 * so that you can send a large data stream without difficulties.
 * <p>
 * To use {@link ChunkedWriteHandler} in your application, you have to insert
 * a new {@link ChunkedWriteHandler} instance:
 * <pre>
 * {@link ChannelPipeline} p = ...;
 * p.addLast("streamer", <b>new {@link ChunkedWriteHandler}()</b>);
 * p.addLast("handler", new MyHandler());
 * </pre>
 * Once inserted, you can write a {@link ChunkedInput} so that the
 * {@link ChunkedWriteHandler} can pick it up and fetch the content of the
 * stream chunk by chunk and write the fetched chunk downstream:
 * <pre>
 * {@link Channel} ch = ...;
 * ch.write(new {@link ChunkedFile}(new File("video.mkv"));
 * </pre>
 *
 * <h3>Sending a stream which generates a chunk intermittently</h3>
 *
 * Some {@link ChunkedInput} generates a chunk on a certain event or timing.
 * Such {@link ChunkedInput} implementation often returns {@code null} on
 * {@link ChunkedInput#readChunk(ChannelHandlerContext)}, resulting in the indefinitely suspended
 * transfer.  To resume the transfer when a new chunk is available, you have to
 * call {@link #resumeTransfer()}.
 */
// CLR: ChannelFuture/ChannelPromise are Task/TaskCompletionSource (docs/transport-porting.md): write(ctx, msg, promise)
// is WriteAsync(ctx, msg, cancellationToken), whose returned Task replaces the promise (docs/codec-base-porting.md). A
// queued write gets its own TaskCompletionSource, which PendingWrite completes; the token cancels it as
// ChannelPromise.cancel(...) cancels the promise. ChunkedInput<?> is IChunkedInput<object> (B is covariant, see
// IChunkedInput). The ArrayDeque is Queue<T>: add is Enqueue, remove() is Dequeue(), which also throws when the queue
// is empty ("The Queue<T> is empty",
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1.dequeue?view=net-10.0), and
// peek()/poll(), which return null when the deque is empty
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/ArrayDeque.html), are TryPeek/TryDequeue,
// which leave "the default value of T" (null) when the queue is empty
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1.trypeek?view=net-10.0). Throwable is
// Exception and java.nio.channels.ClosedChannelException is the Transport ClosedChannelException. The @Deprecated
// ChunkedWriteHandler(int maxPendingWrites), whose @deprecated text names ChunkedWriteHandler(), is not ported (owner
// decision 2026-11-16).
public class ChunkedWriteHandler : ChannelDuplexHandler
{

    private static readonly IInternalLogger logger =
        InternalLoggerFactory.GetInstance(typeof(ChunkedWriteHandler));

    private Queue<PendingWrite> queue;
    private volatile IChannelHandlerContext ctx;
    private bool inFlush;
    private bool flushPending;

    public ChunkedWriteHandler()
    {
    }

    private void AllocateQueue()
    {
        if (queue == null)
        {
            queue = new Queue<PendingWrite>();
        }
    }

    private bool QueueIsEmpty()
    {
        return queue == null || queue.Count == 0;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        // Nothing will write the queued messages once this handler was removed, not even channelInactive(),
        // so fail them now and close / release what they hold.
        if (!QueueIsEmpty())
        {
            // Only create the exception if there is something to fail, as creating it is expensive.
            Discard(new ChannelException("Pending write on removal of ChunkedWriteHandler"));
        }
    }

    /**
     * Continues to fetch the chunks from the input.
     */
    public void ResumeTransfer()
    {
        IChannelHandlerContext ctx = this.ctx;
        if (ctx == null)
        {
            return;
        }
        if (ctx.Executor.InEventLoop())
        {
            ResumeTransfer0(ctx);
        }
        else
        {
            // let the transfer resume on the next event loop round
            // CLR: the anonymous Runnable is a lambda.
            ctx.Executor.Execute(() => ResumeTransfer0(ctx));
        }
    }

    private void ResumeTransfer0(IChannelHandlerContext ctx)
    {
        try
        {
            DoFlush(ctx);
        }
        catch (Exception e)
        {
            logger.Warn("Unexpected exception while sending chunks.", e);
        }
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (!QueueIsEmpty() || msg is IChunkedInput<object>)
        {
            AllocateQueue();
            // CLR: the promise of the queued write; its continuations run asynchronously, as the Transport
            // ChannelOutboundBuffer's write promises do, so no caller continuation runs inside this handler.
            TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(new PendingWrite(msg, promise, ctx.Channel, cancellationToken));
            return promise.Task;
        }
        else
        {
            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    public override void Flush(IChannelHandlerContext ctx)
    {
        DoFlush(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        DoFlush(ctx);
        ctx.FireChannelInactive();
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.IsWritable)
        {
            // channel is writable again try to continue flushing
            DoFlush(ctx);
        }
        ctx.FireChannelWritabilityChanged();
    }

    private void Discard(Exception cause)
    {
        if (QueueIsEmpty())
        {
            return;
        }
        for (;;)
        {
            queue.TryDequeue(out PendingWrite currentWrite);

            if (currentWrite == null)
            {
                break;
            }
            object message = currentWrite.msg;
            if (message is IChunkedInput<object> @in)
            {
                bool endOfInput;
                long inputLength;
                try
                {
                    endOfInput = @in.IsEndOfInput();
                    inputLength = @in.Length();
                    CloseInput(@in);
                }
                catch (Exception e)
                {
                    CloseInput(@in);
                    currentWrite.Fail(e);
                    logger.Warn("ChunkedInput failed", e);
                    continue;
                }

                if (!endOfInput)
                {
                    if (cause == null)
                    {
                        cause = new ClosedChannelException();
                    }
                    currentWrite.Fail(cause);
                }
                else
                {
                    currentWrite.Success(inputLength);
                }
            }
            else
            {
                if (cause == null)
                {
                    cause = new ClosedChannelException();
                }
                currentWrite.Fail(cause);
            }
        }
    }

    private void DoFlush(IChannelHandlerContext ctx)
    {
        if (inFlush)
        {
            // doFlush() must not re-enter itself: ChunkedInput.readChunk(...) and isEndOfInput() run
            // between queue.peek() and queue.remove(), and user code executed there may
            // synchronously trigger flush(), resumeTransfer(), channelInactive(...) or
            // channelWritabilityChanged(...) on this very handler. A nested doFlush() would peek
            // and consume the same queue entry again, which made the outer invocation fail with
            // NoSuchElementException and let both invocations read from the same (already closed)
            // input. Remember the nested call instead: the outer invocation re-runs the drain
            // loop before returning, so a resumeTransfer(...) that arrives while it is about to
            // suspend is not lost (see the old lock-based guard and its flushRequired follow-up).
            flushPending = true;
            return;
        }
        inFlush = true;
        try
        {
            do
            {
                flushPending = false;
                DoFlush0(ctx);
            } while (flushPending);
        }
        finally
        {
            inFlush = false;
        }
    }

    private void DoFlush0(IChannelHandlerContext ctx)
    {
        IChannel channel = ctx.Channel;
        if (!channel.IsActive)
        {
            // Even after discarding all previous queued objects we should propagate the flush through
            // to ensure previous written objects via writeAndFlush(...) that were not queued will be flushed and
            // so eventually fail the promise.
            Discard(null);
            ctx.Flush();
            return;
        }

        if (QueueIsEmpty())
        {
            ctx.Flush();
            return;
        }

        bool requiresFlush = true;
        IByteBufAllocator allocator = ctx.Alloc();
        while (channel.IsWritable)
        {
            queue.TryPeek(out PendingWrite currentWrite);

            if (currentWrite == null)
            {
                break;
            }

            if (currentWrite.promise.Task.IsCompleted)
            {
                // This might happen e.g. in the case when a write operation
                // failed, but there are still unconsumed chunks left.
                // Most chunked input sources would stop generating chunks
                // and report end of input, but this doesn't work with any
                // source wrapped in HttpChunkedInput.
                // Note, that we're not trying to release the message/chunks
                // as this had to be done already by someone who resolved the
                // promise (using ChunkedInput.close method).
                // See https://github.com/netty/netty/issues/8700.
                // A promise that was cancelled by the user is different: nobody closed or released the message.
                queue.Dequeue();
                if (currentWrite.promise.Task.IsCanceled)
                {
                    if (currentWrite.msg is IChunkedInput<object> cancelledInput)
                    {
                        CloseInput(cancelledInput);
                    }
                    else
                    {
                        ReferenceCountUtil.Release(currentWrite.msg);
                    }
                }
                continue;
            }

            object pendingMessage = currentWrite.msg;

            if (pendingMessage is IChunkedInput<object> chunks)
            {
                bool endOfInput;
                bool suspend;
                object message = null;
                try
                {
                    message = chunks.ReadChunk(allocator);
                    endOfInput = chunks.IsEndOfInput();
                    // No need to suspend when reached at the end.
                    suspend = message == null && !endOfInput;

                }
                catch (Exception t)
                {
                    queue.Dequeue();

                    if (message != null)
                    {
                        ReferenceCountUtil.Release(message);
                    }

                    CloseInput(chunks);
                    currentWrite.Fail(t);
                    break;
                }

                if (suspend)
                {
                    // ChunkedInput.nextChunk() returned null and it has
                    // not reached at the end of input. Let's wait until
                    // more chunks arrive. Nothing to write or notify.
                    break;
                }

                if (message == null)
                {
                    // If message is null write an empty ByteBuf.
                    // See https://github.com/netty/netty/issues/1671
                    message = Unpooled.EmptyBuffer;
                }

                if (endOfInput)
                {
                    // We need to remove the element from the queue before we call writeAndFlush() as this operation
                    // may cause an action that also touches the queue.
                    queue.Dequeue();
                }
                // Flush each chunk to conserve memory
                Task f = ctx.WriteAndFlushAsync(message);
                if (endOfInput)
                {
                    if (f.IsCompleted)
                    {
                        HandleEndOfInputFuture(f, chunks, currentWrite);
                    }
                    else
                    {
                        // Register a listener which will close the input once the write is complete.
                        // This is needed because the Chunk may have some resource bound that can not
                        // be closed before it's not written.
                        //
                        // See https://github.com/netty/netty/issues/303
                        AddListener(ctx, f, future =>
                                HandleEndOfInputFuture(future, chunks, currentWrite));
                    }
                }
                else
                {
                    bool resume = !channel.IsWritable;
                    if (f.IsCompleted)
                    {
                        HandleFuture(f, chunks, currentWrite, resume);
                    }
                    else
                    {
                        AddListener(ctx, f, future =>
                                HandleFuture(future, chunks, currentWrite, resume));
                    }
                }
                requiresFlush = false;
            }
            else
            {
                queue.Dequeue();
                // CLR: ctx.write(pendingMessage, currentWrite.promise) completes the promise with the outcome of the
                // write: the returned Task is copied into it (TrySetFromTask, as codec-compression's EncoderUtil,
                // docs/codec-compression-porting.md), and the write carries the token of the queued write.
                _ = ctx.WriteAsync(pendingMessage, currentWrite.cancellationToken).ContinueWith(
                        static (write, state) => ((TaskCompletionSource) state).TrySetFromTask(write),
                        currentWrite.promise, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                requiresFlush = true;
            }

            if (!channel.IsActive)
            {
                Discard(new ClosedChannelException());
                break;
            }
        }

        if (requiresFlush)
        {
            ctx.Flush();
        }
    }

    // CLR: future.isSuccess() is Task.IsCompletedSuccessfully and future.cause() is the Task's exception, or a
    // TaskCanceledException for a cancelled write (as codec-base MessageAggregator and the Transport
    // ChannelOutboundBuffer map them).
    private static void HandleEndOfInputFuture(Task future, IChunkedInput<object> input, PendingWrite currentWrite)
    {
        if (!future.IsCompletedSuccessfully)
        {
            CloseInput(input);
            currentWrite.Fail(future.IsFaulted ? future.Exception.InnerException : new TaskCanceledException(future));
        }
        else
        {
            // read state of the input in local variables before closing it
            long inputProgress = input.Progress();
            long inputLength = input.Length();
            CloseInput(input);
            currentWrite.Progress(inputProgress, inputLength);
            currentWrite.Success(inputLength);
        }
    }

    // CLR: future.channel() is the channel of this handler's context, because a Task has no channel
    // (docs/transport-porting.md lists channel() among the ChannelFuture members without a counterpart).
    private void HandleFuture(Task future, IChunkedInput<object> input, PendingWrite currentWrite, bool resume)
    {
        if (!future.IsCompletedSuccessfully)
        {
            CloseInput(input);
            currentWrite.Fail(future.IsFaulted ? future.Exception.InnerException : new TaskCanceledException(future));
        }
        else
        {
            currentWrite.Progress(input.Progress(), input.Length());
            if (resume && ctx.Channel.IsWritable)
            {
                ResumeTransfer();
            }
        }
    }

    // CLR: f.addListener(listener) is a continuation of the write Task (future listeners are Task continuations,
    // docs/codec-base-porting.md). DefaultPromise notifies a listener on its channel's executor: inline when the
    // promise completes in the event loop, otherwise through executor.execute(...), logging a rejected submission
    // (docs/common-native-progress.md); the continuation runs the listener the same way, so the listeners touch the
    // input and the promise in the event loop as in the source. Only a write that is not done yet gets a listener.
    private static void AddListener(IChannelHandlerContext ctx, Task future, Action<Task> listener)
    {
        _ = future.ContinueWith(f =>
        {
            IEventExecutor executor = ctx.Executor;
            if (executor.InEventLoop())
            {
                listener(f);
            }
            else
            {
                try
                {
                    executor.Execute(() => listener(f));
                }
                catch (Exception t)
                {
                    logger.Error("Failed to submit a listener notification task. Event loop shut down?", t);
                }
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void CloseInput(IChunkedInput<object> chunks)
    {
        try
        {
            chunks.Close();
        }
        catch (Exception t)
        {
            logger.Warn("Failed to close a ChunkedInput.", t);
        }
    }

    // CLR: the fields are internal because C# does not open a nested class's private members to the enclosing class.
    // The ChannelPromise is the TaskCompletionSource of the queued write; the CLR-only fields keep what the promise
    // carries in Java: its CancellationToken (ChannelPromise.cancel(...) cancels the promise, so the token's callback
    // cancels the TaskCompletionSource, as the Transport ChannelOutboundBuffer's write promise does) and its channel
    // with the channel's write-progress observer (a ChannelProgressivePromise is IChannelConfig.WriteProgress,
    // docs/transport-porting.md).
    private sealed class PendingWrite
    {
        internal readonly object msg;
        internal readonly TaskCompletionSource promise;
        internal readonly CancellationToken cancellationToken;
        private readonly IChannel channel;
        private readonly IProgress<ChannelWriteProgress> progressObserver;

        internal PendingWrite(object msg, TaskCompletionSource promise, IChannel channel,
                              CancellationToken cancellationToken)
        {
            this.msg = msg;
            this.promise = promise;
            this.channel = channel;
            this.cancellationToken = cancellationToken;
            progressObserver = channel.Config.WriteProgress;
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                        static (state, token) => ((TaskCompletionSource) state).TrySetCanceled(token), promise);
                _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration) state).Dispose(),
                        registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
            }
        }

        internal void Fail(Exception cause)
        {
            ReferenceCountUtil.Release(msg);
            promise.TrySetException(cause);
        }

        internal void Success(long total)
        {
            if (promise.Task.IsCompleted)
            {
                // No need to notify the progress or fulfill the promise because it's done already.
                return;
            }
            Progress(total, total);
            promise.TrySetResult();
        }

        // CLR: promise instanceof ChannelProgressivePromise is a channel with a write-progress observer, and
        // tryProgress(progress, total) keeps DefaultProgressivePromise's checks (a negative total is unknown; a
        // negative progress, a progress above a known total or a done promise reports nothing,
        // docs/common-native-progress.md) before it reports the progress of this write's Task.
        internal void Progress(long progress, long total)
        {
            if (progressObserver != null)
            {
                if (total < 0)
                {
                    total = -1;
                    if (progress < 0 || promise.Task.IsCompleted)
                    {
                        return;
                    }
                }
                else if (progress < 0 || progress > total || promise.Task.IsCompleted)
                {
                    return;
                }
                try
                {
                    progressObserver.Report(
                            new ChannelWriteProgress(channel, promise.Task, new TransferProgress(progress, total)));
                }
                catch (Exception t)
                {
                    logger.Warn("An exception was thrown by a write progress observer.", t);
                }
            }
        }
    }
}
