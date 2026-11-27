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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/**
 * A queue of write operations which are pending for later execution. It also updates the
 * {@linkplain Channel#isWritable() writability} of the associated {@link Channel}, so that
 * the pending write operations are also considered to determine the writability.
 */
// CLR: producers are native TaskCompletionSource instances, not ChannelPromise wrappers. Add(message)
// returns a consumer Task; Add(message, producer) transfers completion responsibility to the queue until
// Remove returns the producer or RemoveAndWrite hands the message to the invoker. Callers must serialize
// queue access on its executor and must not independently complete a producer after handing off a write.
public sealed class PendingWriteQueue
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(PendingWriteQueue));
    // Assuming a 64-bit JVM:
    //  - 16 bytes object header
    //  - 4 reference fields
    //  - 1 long fields
    // CLR: retain the source's configurable accounting estimate; this is not a CLR object-layout assertion.
    private static readonly int PendingWriteOverhead = SystemPropertyUtil.GetInt("io.netty.transport.pendingWriteSizeOverhead", 64);
    private readonly IChannelOutboundInvoker _invoker;
    private readonly IEventExecutor _executor;
    private readonly PendingBytesTracker _tracker;
    // head and tail pointers for the linked-list structure. If empty head and tail are null.
    private Entry _head, _tail;
    public PendingWriteQueue(IChannelHandlerContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _invoker = ctx;
        _executor = ctx.Executor;
        _tracker = new PendingBytesTracker(ctx.Channel);
    }

    public PendingWriteQueue(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _invoker = channel;
        _executor = channel.EventLoop;
        _tracker = new PendingBytesTracker(channel);
    }
    /**
     * Returns {@code true} if there are no pending write operations left in this queue.
     */
    public bool IsEmpty { get { AssertOwner(); return _head == null; } }
    /**
     * Returns the number of pending write operations.
     */
    public int Size { get; private set; }
    /**
     * Returns the total number of bytes that are pending because of pending messages. This is only an estimate so
     * it should only be treated as a hint.
     */
    public long Bytes { get; private set; }
    /**
     * Return the current message or {@code null} if empty.
     */
    public object Current { get { AssertOwner(); return _head?.Message; } }
    [Conditional("DEBUG")]
    private void AssertOwner()
    {
        Debug.Assert(_executor.InEventLoop());
    }

    public Task Add(object msg, CancellationToken cancellationToken = default)
    {
        var producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Add(msg, producer, cancellationToken);
        return producer.Task;
    }

    /**
     * Add the given {@code msg} and {@link ChannelPromise}.
     */
    public void Add(object msg, TaskCompletionSource producer, CancellationToken cancellationToken = default)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(producer);
        // It is possible for writes to be triggered from removeAndFailAll(). To preserve ordering,
        // we should add them to the queue and let removeAndFailAll() fail them later.
        int estimate = _tracker.Size(msg);
        if (estimate < 0)
        {
            // Size may be unknown so just use 0
            estimate = 0;
        }
        var entry = new Entry(msg, (long)estimate + PendingWriteOverhead, producer, cancellationToken);
        if (_tail == null)
            _head = _tail = entry;
        else
        { _tail.Next = entry; _tail = entry; }
        Size++;
        Bytes += entry.Size;
        _tracker.IncrementPendingOutboundBytes(entry.Size);
        // Touch the message to make it easier to debug buffer leaks.
        // The Java-specific AbstractReferenceCountedByteBuf fast path is represented by the CLR utility call below:
        // this save both checking against the ReferenceCounted interface
        // and makes better use of virtual calls vs interface ones
        // CLR: ReferenceCountUtil dispatches to the native reference-counted object directly.
        ReferenceCountUtil.Touch(msg);
        if (cancellationToken.CanBeCanceled)
        {
            entry.Cancellation = cancellationToken.UnsafeRegister(_ =>
            {
                if (Interlocked.CompareExchange(ref entry.Claim, 2, 0) != 0)
                    return;
                void Cancel()
                {
                    producer.TrySetCanceled(cancellationToken);
                }
                if (_executor.InEventLoop())
                    Cancel();
                else
                {
                    try
                    { _executor.Execute(Cancel); }
                    catch (RejectedExecutionException) { Cancel(); }
                }
            }, null);
            if (Volatile.Read(ref entry.Claim) == 1)
                entry.Cancellation.Dispose();
        }
    }

    /**
     * Remove all pending write operation and performs them via
     * {@link ChannelHandlerContext#write(Object, ChannelPromise)}.
     *
     * @return  {@link ChannelFuture} if something was written and {@code null}
     *          if the {@link PendingWriteQueue} is empty.
     */
    public Task RemoveAndWriteAllAsync()
    {
        AssertOwner();
        if (_head == null)
            return null;
        var tasks = new List<Task>();
        // It is possible for some of the written promises to trigger more writes. The new writes
        // will "revive" the queue, so we need to write them up until the queue is empty.
        while (_head != null)
        {
            var entry = DetachAll();
            while (entry != null)
            {
                var next = entry.Next;
                bool cancelled = StopCancellation(entry);
                _tracker.DecrementPendingOutboundBytes(entry.Size);
                tasks.Add(Write(entry, cancelled));
                entry = next;
            }
        }
        AssertEmpty();
        return Task.WhenAll(tasks);
    }

    /**
     * Remove all pending write operation and fail them with the given {@link Throwable}. The message will be released
     * via {@link ReferenceCountUtil#safeRelease(Object)}.
     */
    public void RemoveAndFailAll(Exception cause)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(cause);
        // It is possible for some of the failed promises to trigger more writes. The new writes
        // will "revive" the queue, so we need to clean them up until the queue is empty.
        while (_head != null)
        {
            var entry = DetachAll();
            while (entry != null)
            {
                var next = entry.Next;
                bool cancelled = StopCancellation(entry);
                ReferenceCountUtil.SafeRelease(entry.Message);
                _tracker.DecrementPendingOutboundBytes(entry.Size);
                if (cancelled)
                    entry.Producer.TrySetCanceled(entry.Token);
                else
                    SafeFail(entry.Producer, cause);
                entry = next;
            }
        }
        AssertEmpty();
    }

    /**
     * Remove a pending write operation and fail it with the given {@link Throwable}. The message will be released via
     * {@link ReferenceCountUtil#safeRelease(Object)}.
     */
    public void RemoveAndFail(Exception cause)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(cause);
        var entry = _head;
        if (entry == null)
            return;
        bool cancelled = StopCancellation(entry);
        ReferenceCountUtil.SafeRelease(entry.Message);
        // Unlink the write before failing the promise, as a listener may use the queue again.
        Unlink(entry);
        if (cancelled)
            entry.Producer.TrySetCanceled(entry.Token);
        else
            SafeFail(entry.Producer, cause);
    }

    /**
     * Removes a pending write operation and performs it via
     * {@link ChannelHandlerContext#write(Object, ChannelPromise)}.
     *
     * @return  {@link ChannelFuture} if something was written and {@code null}
     *          if the {@link PendingWriteQueue} is empty.
     */
    public Task RemoveAndWriteAsync()
    {
        AssertOwner();
        var entry = _head;
        if (entry == null)
            return null;
        bool cancelled = StopCancellation(entry);
        Unlink(entry);
        return Write(entry, cancelled);
    }

    /**
     * Removes a pending write operation and release it's message via {@link ReferenceCountUtil#safeRelease(Object)}.
     *
     * @return  {@link ChannelPromise} of the pending write or {@code null} if the queue is empty.
     *
     */
    public TaskCompletionSource Remove()
    {
        AssertOwner();
        var entry = _head;
        if (entry == null)
            return null;
        bool cancelled = StopCancellation(entry);
        ReferenceCountUtil.SafeRelease(entry.Message);
        Unlink(entry);
        if (cancelled)
            entry.Producer.TrySetCanceled(entry.Token);
        return entry.Producer;
    }

    private Entry DetachAll()
    {
        var entry = _head;
        _head = _tail = null;
        Size = 0;
        Bytes = 0;
        return entry;
    }

    private void Unlink(Entry entry)
    {
        var next = entry.Next;
        if (next == null)
        {
            // Handled last PendingWrite so rest head and tail
            // Guard against re-entrance by directly reset
            _head = _tail = null;
            Size = 0;
            Bytes = 0;
        }
        else
        { _head = next; Size--; Bytes -= entry.Size; Debug.Assert(Size > 0 && Bytes >= 0); }
        _tracker.DecrementPendingOutboundBytes(entry.Size);
    }

    private static bool StopCancellation(Entry entry)
    {
        int claim = Interlocked.Exchange(ref entry.Claim, 1);
        entry.Cancellation.Dispose();
        return claim == 2 || entry.Producer.Task.IsCanceled;
    }

    private Task Write(Entry entry, bool cancelled)
    {
        if (cancelled)
        { ReferenceCountUtil.SafeRelease(entry.Message); entry.Producer.TrySetCanceled(entry.Token); return entry.Producer.Task; }
        Task operation;
        try
        { operation = _invoker.WriteAsync(entry.Message, entry.Token); }
        catch (Exception error) { ReferenceCountUtil.SafeRelease(entry.Message); SafeFail(entry.Producer, error); return entry.Producer.Task; }
        if (operation.IsCompleted)
            Complete(operation, entry.Producer);
        else
            CompleteLater(operation, entry.Producer);
        return entry.Producer.Task;
    }

    private static void Complete(Task operation, TaskCompletionSource producer)
    {
        try
        { operation.GetAwaiter().GetResult(); producer.TrySetResult(); }
        catch (OperationCanceledException error) when (operation.IsCanceled) { producer.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { SafeFail(producer, error); }
    }

    // CLR: Java hands the ChannelPromise itself to write(msg, promise), so it completes when the write does. A write
    // that completes later completes its producer through a continuation on Common's EventExecutorTaskScheduler over
    // the queue's executor: the completing thread queues it to that scheduler before the operation's TrySetResult
    // returns, even though the Transport's operation Tasks run their continuations asynchronously, and the scheduler
    // runs it at once on the event loop and otherwise hands it to the executor (measured on .NET 10; the scheduler's
    // comment cites TaskCreationOptions and TaskScheduler.QueueTask). An awaited continuation would instead run on a
    // thread-pool thread and submit the completion, so the producer would complete later than in Java. When the
    // executor rejects the hand-off, the continuation faults with a TaskSchedulerException whose inner exception is the
    // rejection, without throwing into the completing thread (measured on .NET 10), and the producer fails with that
    // rejection, as the earlier submission did.
    private void CompleteLater(Task operation, TaskCompletionSource producer)
    {
        Task completion = operation.ContinueWith(static (op, p) => Complete(op, (TaskCompletionSource) p), producer,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, CompletionScheduler());
        if (completion.IsCompleted && !completion.IsFaulted)
            return;
        _ = completion.ContinueWith(static (c, p) => SafeFail((TaskCompletionSource) p,
                c.Exception.InnerException is TaskSchedulerException scheduling && scheduling.InnerException != null
                    ? scheduling.InnerException
                    : c.Exception.InnerException), producer,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
    // CLR: created with the first write that completes later; the queue's executor does not change.
    private EventExecutorTaskScheduler _completionScheduler;
    private EventExecutorTaskScheduler CompletionScheduler()
    {
        if (_completionScheduler == null)
        {
            IEventExecutor executor = _executor;
            _completionScheduler = new EventExecutorTaskScheduler(() => executor);
        }
        return _completionScheduler;
    }

    private static void SafeFail(TaskCompletionSource producer, Exception cause)
    {
        if (!producer.TrySetException(cause))
            Logger.Warn("Failed to mark a producer as failure because it's done already: " + producer.Task, cause);
    }

    [Conditional("DEBUG")]
    private void AssertEmpty()
    {
        Debug.Assert(_head == null && _tail == null && Size == 0);
    }
    /**
     * Holds all meta-data and construct the linked-list structure.
     */
    // CLR: ordinary managed entries replace JVM Recycler/ObjectPool handles.
    private sealed class Entry(object msg, long size, TaskCompletionSource producer, CancellationToken token)
    {
        internal readonly object Message = msg;
        internal readonly long Size = size;
        internal readonly TaskCompletionSource Producer = producer;
        internal readonly CancellationToken Token = token;
        internal CancellationTokenRegistration Cancellation;
        internal int Claim; // 0 queued/cancellable, 1 removed or transferred, 2 cancelled.
        internal Entry Next;
    }
}
