/*
 * Copyright 2017 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

// CLR: LinkedList supplies deque operations. The alternating entries are ByteBuf and native
// TaskCompletionSource/Action<Task> completion metadata; no Promise or listener wrapper is exported.
public abstract class AbstractCoalescingBufferQueue
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(AbstractCoalescingBufferQueue));
    private readonly LinkedList<object> _pairs = new();
    private readonly PendingBytesTracker _tracker;
    private int _readableBytes;
    /**
     * Create a new instance.
     *
     * @param channel the {@link Channel} which will have the {@link Channel#isWritable()} reflect the amount of queued
     *                buffers or {@code null} if there is no writability state updated.
     * @param initSize the initial size of the underlying queue.
     */
    protected AbstractCoalescingBufferQueue(IChannel channel, int initialSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialSize);
        // BCL LinkedList has no capacity reservation; the source's initSize is only a sizing hint.
        _tracker = channel == null ? null : new PendingBytesTracker(channel);
    }
    /**
     * The number of readable bytes.
     */
    public int ReadableBytes => _readableBytes;
    /**
     * Are there pending buffers in the queue.
     */
    public bool IsEmpty => _pairs.Count == 0;
    /**
     * Get the number of elements in this queue added via one of the {@link #add(ByteBuf)} methods.
     * @return the number of elements in this queue.
     */
    protected internal int Size => _pairs.Count;
    /**
     * Add a buffer to the front of the queue and associate a promise with it that should be completed when
     * all the buffer's bytes have been consumed from the queue and written.
     * @param buf to add to the head of the queue
     * @param promise to complete when all the bytes have been consumed and written, can be void.
     */
    public void AddFirst(ByteBuf buffer, TaskCompletionSource producer = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        // Touch the message to make it easier to debug buffer leaks.
        buffer.Touch();
        if (producer != null)
            _pairs.AddFirst(producer);
        _pairs.AddFirst(buffer);
        IncrementReadableBytes(buffer.ReadableBytes);
    }
    /**
     * Add a buffer to the end of the queue.
     */
    public void Add(ByteBuf buffer) => AddCore(buffer, null);
    /**
     * Add a buffer to the end of the queue and associate a promise with it that should be completed when
     * all the buffer's bytes have been consumed from the queue and written.
     * @param buf to add to the tail of the queue
     * @param promise to complete when all the bytes have been consumed and written, can be void.
     */
    public void Add(ByteBuf buffer, TaskCompletionSource producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        // buffers are added before promises so that we naturally 'consume' the entire buffer during removal
        // before we complete it's promise.
        AddCore(buffer, producer);
    }
    /**
     * Add a buffer to the end of the queue and associate a listener with it that should be completed when
     * all the buffers  bytes have been consumed from the queue and written.
     * @param buf to add to the tail of the queue
     * @param listener to notify when all the bytes have been consumed and written, can be {@code null}.
     */
    public void Add(ByteBuf buffer, Action<Task> completion) => AddCore(buffer, completion);
    private void AddCore(ByteBuf buffer, object completion)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        // Touch the message to make it easier to debug buffer leaks.
        buffer.Touch();
        // buffers are added before promises so that we naturally 'consume' the entire buffer during removal
        // before we complete it's promise.
        _pairs.AddLast(buffer);
        if (completion != null)
            _pairs.AddLast(completion);
        IncrementReadableBytes(buffer.ReadableBytes);
    }
    private object Poll()
    { var first = _pairs.First; if (first == null) return null; _pairs.RemoveFirst(); return first.Value; }
    /**
     * Remove the first {@link ByteBuf} from the queue.
     * @param aggregatePromise used to aggregate the promises and listeners for the returned buffer.
     * @return the first {@link ByteBuf} from the queue.
     */
    public ByteBuf RemoveFirst(TaskCompletionSource aggregateProducer)
    {
        ArgumentNullException.ThrowIfNull(aggregateProducer);
        if (Poll() is not ByteBuf result)
            return null;
        DecrementReadableBytes(result.ReadableBytes);
        if (_pairs.First?.Value is TaskCompletionSource or Action<Task>)
            Attach(aggregateProducer, Poll());
        ReconcileReadableBytes();
        return result;
    }
    /**
     * Remove a {@link ByteBuf} from the queue with the specified number of bytes. Any added buffer who's bytes are
     * fully consumed during removal will have it's promise completed when the passed aggregate {@link ChannelPromise}
     * completes.
     *
     * @param alloc The allocator used if a new {@link ByteBuf} is generated during the aggregation process.
     * @param bytes the maximum number of readable bytes in the returned {@link ByteBuf}, if {@code bytes} is greater
     *              than {@link #readableBytes} then a buffer of length {@link #readableBytes} is returned.
     * @param aggregatePromise used to aggregate the promises and listeners for the constituent buffers.
     * @return a {@link ByteBuf} composed of the enqueued buffers.
     */
    public ByteBuf Remove(IByteBufAllocator allocator, int bytes, TaskCompletionSource aggregateProducer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentNullException.ThrowIfNull(aggregateProducer);
        // Use isEmpty rather than readableBytes==0 as we may have a promise associated with an empty buffer.
        if (_pairs.Count == 0)
        { ReconcileReadableBytes(); return RemoveEmptyValue(); }
        bytes = Math.Min(bytes, _readableBytes);
        ByteBuf result = null, entryBuffer = null;
        int originalBytes = bytes;
        try
        {
            for (; ; )
            {
                object entry = Poll();
                if (entry == null)
                    break;
                // fast-path vs abstract type
                if (entry is ByteBuf buffer)
                {
                    entryBuffer = buffer;
                    int bufferBytes = buffer.ReadableBytes;
                    if (bufferBytes > bytes)
                    {
                        // Add the buffer back to the queue as we can't consume all of it.
                        _pairs.AddFirst(buffer);
                        if (bytes > 0)
                        {
                            // Take a slice of what we can consume and retain it.
                            entryBuffer = buffer.ReadRetainedSlice(bytes);
                            // we end here, so if this is the only buffer to return, skip composing
                            result = result == null ? entryBuffer : Compose(allocator, result, entryBuffer);
                            bytes = 0;
                        }
                        break;
                    }
                    bytes -= bufferBytes;
                    if (result == null)
                    {
                        // if there are no more bytes to read, there's no reason to compose
                        result = bytes == 0 ? buffer : ComposeFirst(allocator, buffer, bufferBytes + bytes);
                    }
                    else
                        result = Compose(allocator, result, buffer);
                    entryBuffer = null;
                }
                else
                    Attach(aggregateProducer, entry);
            }
        }
        catch (Exception error)
        {
            // Always decrement to keep things consistent. We decrement directly here and not in a finally-block
            // to ensure that the state is consistent even if it would be accessed via a listener that is
            // attached to the promise that we fail below.
            DecrementReadableBytes(originalBytes - bytes);
            // Poll the next element if it's a listener that belongs to the ByteBuf.
            if (_pairs.First?.Value is TaskCompletionSource or Action<Task>)
                Attach(aggregateProducer, Poll());
            ReferenceCountUtil.SafeRelease(entryBuffer);
            ReferenceCountUtil.SafeRelease(result);
            aggregateProducer.SetException(error);
            throw;
        }
        DecrementReadableBytes(originalBytes - bytes);
        ReconcileReadableBytes();
        return result;
    }
    private static void Notify(object completion, Task operation)
    {
        if (completion is Action<Task> callback)
        { callback(operation); return; }
        var producer = (TaskCompletionSource)completion;
        try
        { operation.GetAwaiter().GetResult(); producer.TrySetResult(); }
        catch (OperationCanceledException error) when (operation.IsCanceled) { producer.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { producer.TrySetException(error); }
    }
    private static void NotifySafely(object completion, Task operation)
    {
        if (completion == null)
            return;
        try
        { Notify(completion, operation); }
        catch (Exception error) { Logger.Warn("A buffer completion callback threw an exception.", error); }
    }
    private static void Attach(TaskCompletionSource aggregate, object completion)
    {
        // Default TCS producers and ExecuteSynchronously preserve the source's inline listener reentrance.
        // Callers choosing asynchronous continuations must explicitly await/dispatch their callbacks.
        _ = aggregate.Task.ContinueWith(task => NotifySafely(completion, task), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    /**
     *  Release all buffers in the queue and complete all listeners and promises.
     */
    public void ReleaseAndFailAll(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        Task failed = Task.FromException(cause);
        Exception pending = null;
        for (; ; )
        {
            object entry = Poll();
            if (entry == null)
                break;
            try
            {
                if (entry is ByteBuf buffer)
                { DecrementReadableBytes(buffer.ReadableBytes); ReferenceCountUtil.SafeRelease(buffer); }
                else
                    Notify(entry, failed);
            }
            catch (Exception error)
            {
                if (pending == null)
                    pending = error;
                else
                    Logger.Info("Throwable being suppressed because another Throwable is already pending", error);
            }
        }
        _ = failed.Exception;
        ReconcileReadableBytes();
        if (pending != null)
            throw new InvalidOperationException("A buffer completion callback failed.", pending);
    }
    /**
     * Copy all pending entries in this queue into the destination queue.
     * @param dest to copy pending buffers to.
     */
    public void CopyTo(AbstractCoalescingBufferQueue destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var entry in _pairs)
            destination._pairs.AddLast(entry);
        destination.IncrementReadableBytes(_readableBytes);
    }
    /**
     * Writes all remaining elements in this queue.
     * @param ctx The context to write all elements to.
     */
    public Task WriteAndRemoveAllAsync(ChannelHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Exception pending = null;
        ByteBuf previous = null;
        var writes = new List<Task>();
        for (; ; )
        {
            object entry = Poll();
            try
            {
                if (entry == null)
                {
                    if (previous != null)
                    { DecrementReadableBytes(previous.ReadableBytes); writes.Add(Write(context, previous, null)); }
                    break;
                }
                if (entry is ByteBuf buffer)
                {
                    if (previous != null)
                    { DecrementReadableBytes(previous.ReadableBytes); writes.Add(Write(context, previous, null)); }
                    previous = buffer;
                }
                else
                {
                    DecrementReadableBytes(previous.ReadableBytes);
                    writes.Add(Write(context, previous, entry));
                    previous = null;
                }
            }
            catch (Exception error)
            {
                if (pending == null)
                    pending = error;
                else
                    Logger.Info("Throwable being suppressed because another Throwable is already pending", error);
            }
        }
        ReconcileReadableBytes();
        if (pending != null)
            throw new InvalidOperationException("Writing queued buffers failed.", pending);
        return Task.WhenAll(writes);
    }
    private static Task Write(ChannelHandlerContext context, ByteBuf buffer, object completion)
    {
        if (completion is TaskCompletionSource producer && producer.Task.IsCanceled)
        { ReferenceCountUtil.SafeRelease(buffer); return producer.Task; }
        Task operation = context.WriteAsync(buffer);
        if (operation.IsCompleted)
        { NotifySafely(completion, operation); return operation; }
        return CompleteWriteAsync();
        async Task CompleteWriteAsync()
        {
            try
            { await operation.ConfigureAwait(false); }
            catch { /* Relay the original result below. */ }
            if (completion != null)
            {
                if (context.Executor.InEventLoop())
                    NotifySafely(completion, operation);
                else
                {
                    try
                    { await context.Executor.SubmitAsync(() => NotifySafely(completion, operation)).ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        // A completion bridge must never leave a native producer pending if its executor
                        // has already stopped. The returned write task also reports the dispatch failure.
                        if (completion is TaskCompletionSource producer)
                            producer.TrySetException(error);
                        throw;
                    }
                }
            }
            await operation.ConfigureAwait(false);
        }
    }
    /**
     * Calculate the result of {@code current + next}.
     */
    protected abstract ByteBuf Compose(IByteBufAllocator allocator, ByteBuf cumulation, ByteBuf next);
    /**
     * Compose {@code cumulation} and {@code next} into a new {@link CompositeByteBuf}.
     */
    protected ByteBuf ComposeIntoComposite(IByteBufAllocator allocator, ByteBuf cumulation, ByteBuf next)
    {
        // Create a composite buffer to accumulate this pair and potentially all the buffers
        // in the queue. Using +2 as we have already dequeued current and next.
        var composite = allocator.CompositeBuffer(Size + 2);
        try
        { composite.AddComponent(cumulation, true); composite.AddComponent(next, true); }
        catch { composite.Release(); ReferenceCountUtil.SafeRelease(next); throw; }
        return composite;
    }
    /**
     * Compose {@code cumulation} and {@code next} into a new {@link ByteBufAllocator#ioBuffer()}.
     * @param alloc The allocator to use to allocate the new buffer.
     * @param cumulation The current cumulation.
     * @param next The next buffer.
     * @return The result of {@code cumulation + next}.
     */
    protected ByteBuf CopyAndCompose(IByteBufAllocator allocator, ByteBuf cumulation, ByteBuf next)
    {
        var result = allocator.IoBuffer(checked(cumulation.ReadableBytes + next.ReadableBytes));
        try
        { result.WriteBytes(cumulation).WriteBytes(next); }
        catch { result.Release(); ReferenceCountUtil.SafeRelease(next); throw; }
        cumulation.Release();
        next.Release();
        return result;
    }
#pragma warning disable CS0618 // Preserve the original protected override bridge.
    /**
     * Calculate the first {@link ByteBuf} which will be used in subsequent calls to
     * {@link #compose(ByteBufAllocator, ByteBuf, ByteBuf)}.
     * @param bufferSize the optimal size of the buffer needed for cumulation
     * @return the first buffer
     */
    protected virtual ByteBuf ComposeFirst(IByteBufAllocator allocator, ByteBuf first, int bufferSize) => ComposeFirst(allocator, first);
#pragma warning restore CS0618
    [Obsolete("Override ComposeFirst(IByteBufAllocator, ByteBuf, int) instead.")]
    /**
     * Calculate the first {@link ByteBuf} which will be used in subsequent calls to
     * {@link #compose(ByteBufAllocator, ByteBuf, ByteBuf)}.
     * This method is deprecated and will be removed in the future. Implementing classes should
     * override {@link #composeFirst(ByteBufAllocator, ByteBuf, int)} instead.
     * @deprecated Use {AbstractCoalescingBufferQueue#composeFirst(ByteBufAllocator, ByteBuf, int)}
     */
    protected virtual ByteBuf ComposeFirst(IByteBufAllocator allocator, ByteBuf first) => first;
    /**
     * The value to return when {@link #remove(ByteBufAllocator, int, ChannelPromise)} is called but the queue is empty.
     * @return the {@link ByteBuf} which represents an empty queue.
     */
    protected abstract ByteBuf RemoveEmptyValue();
    private void IncrementReadableBytes(int increment)
    {
        long next = (long)_readableBytes + increment;
        if (next > int.MaxValue)
            throw new InvalidOperationException("buffer queue length overflow: " + _readableBytes + " + " + increment);
        _readableBytes = (int)next;
        _tracker?.IncrementPendingOutboundBytes(increment);
    }
    private void DecrementReadableBytes(int decrement)
    { _readableBytes -= decrement; Debug.Assert(_readableBytes >= 0); _tracker?.DecrementPendingOutboundBytes(decrement); }
    /**
     * Resets readableBytes to 0 when the queue is empty. They can only diverge if a queued buffer was released
     * or consumed while still referenced by the queue (similar to a reference-counting bug) after it was added,
     * which would otherwise make remove(...) return empty buffers forever. Logged at error level because it
     * always indicates a bug that needs to be found.
     * See https://github.com/netty/netty/issues/16946
     */
    private void ReconcileReadableBytes()
    {
        if (_readableBytes != 0 && _pairs.Count == 0)
        {
            Logger.Error("readableBytes is " + _readableBytes + " but the queue is empty: a queued buffer was released or consumed " +
                "while still referenced by the queue. This indicates a bug in the code that produced the buffer. Resetting readableBytes to 0.");
            DecrementReadableBytes(_readableBytes);
        }
    }
    public override string ToString() => "bytes: " + _readableBytes + " buffers: " + (Size >> 1);
}
