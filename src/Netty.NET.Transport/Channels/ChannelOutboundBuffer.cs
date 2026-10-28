/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/// <summary>Event-loop-owned pending writes, flush boundaries and write-buffer water marks.</summary>
/**
 * (Transport implementors only) an internal data structure used by {@link AbstractChannel} to store its pending
 * outbound write requests.
 * <p>
 * All methods must be called by a transport implementation from an I/O thread, except the following ones:
 * <ul>
 * <li>{@link #isWritable()}</li>
 * <li>{@link #getUserDefinedWritability(int)} and {@link #setUserDefinedWritability(int, boolean)}</li>
 * </ul>
 * </p>
 */
public sealed class ChannelOutboundBuffer
{
    private readonly IChannel _channel;
    private readonly LinkedList<Entry> _entries = new();
    public static readonly int EntryOverhead = Math.Max(0, SystemPropertyUtil.GetInt("io.netty.transport.outboundBufferEntrySizeOverhead", 96));
    private ReadOnlyMemory<byte>[] _memories = new ReadOnlyMemory<byte>[1024];
    /**
     * Returns the number of {@link ByteBuffer} that can be written out of the {@link ByteBuffer} array that was
     * obtained via {@link #nioBuffers()}. This method <strong>MUST</strong> be called after {@link #nioBuffers()}
     * was called.
     */
    public int MemoryCount { get; private set; }
    /**
     * Returns the number of bytes that can be written out of the {@link ByteBuffer} array that was
     * obtained via {@link #nioBuffers()}. This method <strong>MUST</strong> be called after {@link #nioBuffers()}
     * was called.
     */
    public long MemorySize { get; private set; }
    private long _pendingBytes;
    private int _unwritable;
    private int _flushed;
    public ChannelOutboundBuffer(IChannel channel) { ArgumentNullException.ThrowIfNull(channel); _channel = channel; }
    /**
     * Returns {@code true} if and only if {@linkplain #totalPendingWriteBytes() the total number of pending bytes} did
     * not exceed the write watermark of the {@link Channel} and
     * no {@linkplain #setUserDefinedWritability(int, boolean) user-defined writability flag} has been set to
     * {@code false}.
     */
    public bool IsWritable => Volatile.Read(ref _unwritable) == 0;
    public long TotalPendingWriteBytes => Interlocked.Read(ref _pendingBytes);
    public int Size => _flushed;
    /**
     * Returns {@code true} if there are flushed messages in this {@link ChannelOutboundBuffer} or {@code false}
     * otherwise.
     */
    public bool IsEmpty => _flushed == 0;
    /**
     * Return the current message to write or {@code null} if nothing was flushed before and so is ready to be written.
     */
    public object Current => _entries.First?.Value.Flushed == true ? _entries.First.Value.Message : null;
    /**
     * Return the current message flush progress.
     * @return {@code 0} if nothing was flushed before for the current message or there is no current message
     */
    public long CurrentProgress => _entries.First?.Value.Flushed == true ? _entries.First.Value.Progress : 0;
    /**
     * Get how many bytes can be written until {@link #isWritable()} returns {@code false}.
     * This quantity will always be non-negative. If {@link #isWritable()} is {@code false} then 0.
     */
    public long BytesBeforeUnwritable => IsWritable ? Math.Max(0, (long)_channel.Config.GetWriteBufferHighWaterMark() - TotalPendingWriteBytes + 1) : 0;
    /**
     * Get how many bytes must be drained from the underlying buffer until {@link #isWritable()} returns {@code true}.
     * This quantity will always be non-negative. If {@link #isWritable()} is {@code true} then 0.
     */
    public long BytesBeforeWritable => !IsWritable ? Math.Max(0, TotalPendingWriteBytes - _channel.Config.GetWriteBufferLowWaterMark() + 1) : 0;
    /**
     * Add given message to this {@link ChannelOutboundBuffer}. The given {@link ChannelPromise} will be notified once
     * the message was written.
     */
    public Task AddMessage(object message, int size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (cancellationToken.IsCancellationRequested)
        { ReferenceCountUtil.SafeRelease(message); return Task.FromCanceled(cancellationToken); }
        var entry = new Entry(message, (long)size + EntryOverhead, cancellationToken);
        entry.Node = _entries.AddLast(entry);
        IncrementPendingOutboundBytes(entry.Size);
        if (cancellationToken.CanBeCanceled)
        {
            entry.Cancellation = cancellationToken.UnsafeRegister(_ =>
            {
                // Cancellation claims the result, not the queue. Queue ownership is released by flush/close.
                if (Interlocked.CompareExchange(ref entry.Claim, 2, 0) == 0)
                    entry.Completion.TrySetCanceled(entry.Token);
            }, null);
            if (entry.Node == null)
                entry.Cancellation.Dispose();
        }
        return entry.Completion.Task;
    }
    /**
     * Add a flush to this {@link ChannelOutboundBuffer}. This means all previous added messages are marked as flushed
     * and so you will be able to handle them.
     */
    public void AddFlush()
    {
        long cancelledBytes = 0;
        List<object> cancelledMessages = null;
        for (var node = _entries.First; node != null; node = node.Next)
        {
            var entry = node.Value;
            if (!entry.Flushed)
            {
                entry.Flushed = true;
                _flushed++;
                int claim = Interlocked.CompareExchange(ref entry.Claim, 1, 0);
                entry.Cancellation.Dispose();
                if (claim == 2)
                {
                    // Was cancelled so make sure we free up memory. The message is released only once this buffer is
                    // consistent again, as releasing it may run arbitrary user code.
                    (cancelledMessages ??= new List<object>(2)).Add(entry.Message);
                    cancelledBytes += entry.Size;
                    entry.Message = Unpooled.EmptyBuffer;
                    entry.Size = 0;
                    entry.Progress = 0;
                }
            }
        }
        if (cancelledMessages == null)
            return;
        foreach (var message in cancelledMessages)
            ReferenceCountUtil.SafeRelease(message);
        // Notify about the freed bytes only now. This may fire channelWritabilityChanged(...) synchronously,
        // and a handler may write, flush or close the channel from there, so this buffer must be consistent
        // first.
        DecrementPendingOutboundBytes(cancelledBytes);
    }
    /**
     * Will remove the current message, mark its {@link ChannelPromise} as failure using the given {@link Throwable}
     * and return {@code true}. If no   flushed message exists at the time this method is called it will return
     * {@code false} to signal that no more messages are ready to be handled.
     */
    public bool Remove(Exception cause = null)
    {
        if (_flushed == 0)
        {
            ClearMemories();
            return false;
        }
        var entry = _entries.First.Value;
        _entries.RemoveFirst();
        entry.Node = null;
        _flushed--;
        Interlocked.CompareExchange(ref entry.Claim, 1, 0);
        entry.Cancellation.Dispose();
        ReferenceCountUtil.SafeRelease(entry.Message);
        entry.Message = null;
        DecrementPendingOutboundBytes(entry.Size);
        if (cause == null)
            entry.Completion.TrySetResult();
        else
            entry.Completion.TrySetException(cause);
        return true;
    }
    /**
     * Removes the fully written entries and update the reader index of the partially written entry.
     * This operation assumes all messages in this buffer is {@link ByteBuf}.
     */
    public void RemoveBytes(long writtenBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(writtenBytes);
        for (; ; )
        {
            if (Current is not ByteBuf buffer)
            {
                if (writtenBytes != 0)
                    throw new InvalidOperationException("Written bytes extend beyond flushed byte buffers.");
                break;
            }
            int readable = buffer.ReadableBytes;
            if (readable <= writtenBytes)
            {
                if (writtenBytes != 0)
                { Progress(readable); writtenBytes -= readable; }
                Remove();
            }
            else
            {
                if (writtenBytes != 0)
                { buffer.ReaderIndex += (int)writtenBytes; Progress(writtenBytes); }
                break;
            }
        }
        ClearMemories();
    }
    // CLR borrowed ReadOnlyMemory segments replace NIO ByteBuffer views. The reusable array must not
    // escape the transport's write iteration; it does not retain any of the underlying buffers.
    public ReadOnlyMemory<byte>[] GetReadableMemories(int maxCount = int.MaxValue, long maxBytes = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ClearMemories();
        for (var node = _entries.First; node != null && node.Value.Flushed && node.Value.Message is ByteBuf; node = node.Next)
        {
            var buffer = (ByteBuf)node.Value.Message;
            int readable = buffer.ReadableBytes;
            if (readable == 0)
                continue;
            if (maxBytes - readable < MemorySize && MemoryCount != 0)
                break;
            MemorySize += readable;
            foreach (var memory in buffer.ReadableSequence)
            {
                if (memory.IsEmpty)
                    continue;
                if (MemoryCount == _memories.Length)
                    Array.Resize(ref _memories, checked(_memories.Length * 2));
                _memories[MemoryCount++] = memory;
                if (MemoryCount == maxCount)
                    return _memories;
            }
        }
        return _memories;
    }
    // Clear all ByteBuffer from the array so these can be GC'ed.
    // See https://github.com/netty/netty/issues/3837
    private void ClearMemories()
    {
        Array.Clear(_memories, 0, MemoryCount);
        MemoryCount = 0;
        MemorySize = 0;
    }
    /**
     * Call {@link MessageProcessor#processMessage(Object)} for each flushed message
     * in this {@link ChannelOutboundBuffer} until {@link MessageProcessor#processMessage(Object)}
     * returns {@code false} or there are no more flushed messages to process.
     */
    public void ForEachFlushedMessage(Func<object, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        for (var node = _entries.First; node != null && node.Value.Flushed; node = node.Next)
            if (node.Value.Claim != 2 && !processor(node.Value.Message))
                break;
    }
    /**
     * Notify the {@link ChannelPromise} of the current message about writing progress.
     */
    public void Progress(long amount)
    {
        if (_flushed > 0)
            _entries.First.Value.Progress += amount;
    }
    /**
     * Increment the pending bytes which will be written at some point.
     * This method is thread-safe!
     */
    public void IncrementPendingOutboundBytes(long amount)
    {
        if (amount == 0)
            return;
        long size = Interlocked.Add(ref _pendingBytes, amount);
        if (size > _channel.Config.GetWriteBufferHighWaterMark())
            ChangeMask(1, true);
    }
    /**
     * Decrement the pending bytes which will be written at some point.
     * This method is thread-safe!
     */
    public void DecrementPendingOutboundBytes(long amount)
    {
        if (amount == 0)
            return;
        long size = Interlocked.Add(ref _pendingBytes, -amount);
        if (size < _channel.Config.GetWriteBufferLowWaterMark())
            ChangeMask(1, false);
    }
    /**
     * Returns {@code true} if and only if the user-defined writability flag at the specified index is set to
     * {@code true}.
     */
    public bool GetUserDefinedWritability(int index) { ValidateIndex(index); return (Volatile.Read(ref _unwritable) & (1 << index)) == 0; }
    /**
     * Sets a user-defined writability flag at the specified index.
     */
    public void SetUserDefinedWritability(int index, bool writable) { ValidateIndex(index); ChangeMask(1 << index, !writable); }
    private static void ValidateIndex(int index) { if (index is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(index)); }
    private void ChangeMask(int mask, bool set)
    {
        for (; ; )
        {
            int old = Volatile.Read(ref _unwritable), next = set ? old | mask : old & ~mask;
            if (Interlocked.CompareExchange(ref _unwritable, next, old) != old)
                continue;
            if ((old == 0) != (next == 0))
            {
                if (_channel.EventLoop.InEventLoop())
                    _channel.Pipeline.FireChannelWritabilityChanged();
                else
                    _channel.EventLoop.Execute(() => _channel.Pipeline.FireChannelWritabilityChanged());
            }
            return;
        }
    }
    internal void Close(Exception cause)
    {
        var entries = new List<Entry>(_entries);
        _entries.Clear();
        _flushed = 0;
        ClearMemories();
        long size = 0;
        foreach (var entry in entries)
        { entry.Node = null; size += entry.Size; }
        // Cancelled bytes may still await AddFlush's final notification. Subtract only entries' current
        // pending sizes, rather than resetting that in-flight accounting during a reentrant close.
        Interlocked.Add(ref _pendingBytes, -size);
        foreach (var entry in entries)
        {
            Interlocked.CompareExchange(ref entry.Claim, 1, 0);
            entry.Cancellation.Dispose();
            ReferenceCountUtil.SafeRelease(entry.Message);
            entry.Message = null;
            entry.Completion.TrySetException(cause);
        }
    }
    private sealed class Entry(object message, long size, CancellationToken token)
    {
        internal object Message = message;
        internal long Size = size;
        internal int Claim; // 0 cancellable, 1 flushed/closed, 2 cancelled.
        internal readonly CancellationToken Token = token;
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Cancellation;
        internal LinkedListNode<Entry> Node;
        internal long Progress;
        internal bool Flushed;
    }
}
