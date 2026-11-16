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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

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
    // Assuming a 64-bit JVM:
    //  - 16 bytes object header
    //  - 6 reference fields
    //  - 2 long fields
    //  - 2 int fields
    //  - 1 boolean field
    //  - padding
    // CLR: the default keeps the source's estimate, so water marks and pending-byte accounting match the source;
    // the system property overrides it as in the source.
    internal static readonly int CHANNEL_OUTBOUND_BUFFER_ENTRY_OVERHEAD =
            SystemPropertyUtil.GetInt("io.netty.transport.outboundBufferEntrySizeOverhead", 96);

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ChannelOutboundBuffer));

    // CLR: borrowed ReadOnlyMemory<byte> segments replace ByteBuffer views; the Nio channels gather them into
    // Socket.Send(IList<ArraySegment<byte>>). A segment does not retain its buffer, so the thread-local array must
    // not escape the transport's write iteration, exactly as the source array.
    // https://learn.microsoft.com/dotnet/api/system.readonlymemory-1
    // https://learn.microsoft.com/dotnet/api/system.buffers.readonlysequence-1
    private static readonly FastThreadLocal<ReadOnlyMemory<byte>[]> NIO_BUFFERS =
            new FastThreadLocalFunc<ReadOnlyMemory<byte>[]>(() => new ReadOnlyMemory<byte>[1024]);

    private readonly IChannel channel;

    // Entry(flushedEntry) --> ... Entry(unflushedEntry) --> ... Entry(tailEntry)
    //
    // The Entry that is the first in the linked-list structure that was flushed
    private Entry flushedEntry;
    // The Entry which is the first unflushed in the linked-list structure
    private Entry unflushedEntry;
    // The Entry which represents the tail of the buffer
    private Entry tailEntry;
    // The number of flushed entries that are not written yet
    private int flushed;

    private int nioBufferCount;
    private long nioBufferSize;

    private bool inFail;

    // CLR: Interlocked/Volatile on the field replace TOTAL_PENDING_SIZE_UPDATER.
    private long totalPendingSize;

    // CLR: Interlocked/Volatile on the field replace UNWRITABLE_UPDATER.
    private int unwritable;

    private volatile Action fireChannelWritabilityChangedTask;

    // CLR: the channel keeps this object after close, where the source drops its outboundBuffer reference. This
    // flag stands for that null reference: no message or flush is admitted once output shutdown or close began.
    private volatile bool _closed;

    // CLR: public for transports and fixtures outside this assembly; the source constructor is package-private.
    public ChannelOutboundBuffer(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        this.channel = channel;
    }

    internal bool IsClosed => _closed;

    // CLR: AbstractUnsafe.close(...) and shutdownOutput(...) set the source's outboundBuffer field to null first.
    internal void BeginOutputShutdown() => _closed = true;

    /**
     * Add given message to this {@link ChannelOutboundBuffer}. The given {@link ChannelPromise} will be notified once
     * the message was written.
     */
    // CLR: the returned Task is the promise; a token cancelled before the write is queued fails it at once and
    // releases the message, as AbstractChannelHandlerContext rejects a cancelled promise.
    public Task AddMessage(object msg, int size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(msg);
        if (cancellationToken.IsCancellationRequested)
        {
            ReferenceCountUtil.SafeRelease(msg);
            return Task.FromCanceled(cancellationToken);
        }
        var promise = new WritePromise(channel, cancellationToken, channel.Config.WriteProgress);
        Entry entry = Entry.NewInstance(msg, size, Total(msg), promise);
        if (tailEntry == null)
        {
            flushedEntry = null;
        }
        else
        {
            Entry tail = tailEntry;
            tail.next = entry;
        }
        tailEntry = entry;
        if (unflushedEntry == null)
        {
            unflushedEntry = entry;
        }

        // Touch the message to make it easier to debug buffer leaks.

        // this save both checking against the ReferenceCounted interface
        // and makes better use of virtual calls vs interface ones
        if (msg is AbstractReferenceCountedByteBuf buf)
        {
            buf.Touch();
        }
        else
        {
            ReferenceCountUtil.Touch(msg);
        }

        // increment pending bytes after adding message to the unflushed arrays.
        // See https://github.com/netty/netty/issues/1619
        IncrementPendingOutboundBytes(entry.pendingSize, false);

        // CLR: the caller's token can cancel the write from now on, until addFlush() makes it uncancellable.
        promise.RegisterCancellation();
        return promise.Task;
    }

    /**
     * Add a flush to this {@link ChannelOutboundBuffer}. This means all previous added messages are marked as flushed
     * and so you will be able to handle them.
     */
    public void AddFlush()
    {
        // There is no need to process all entries if there was already a flush before and no new messages
        // where added in the meantime.
        //
        // See https://github.com/netty/netty/issues/2577
        Entry entry = unflushedEntry;
        if (entry != null)
        {
            if (flushedEntry == null)
            {
                // there is no flushedEntry yet, so start with the entry
                flushedEntry = entry;
            }
            long cancelledBytes = 0;
            List<object> cancelledMsgs = null;
            do
            {
                flushed++;
                if (!entry.promise.SetUncancellable())
                {
                    // Was cancelled so make sure we free up memory. The message is released only once this buffer is
                    // consistent again, as releasing it may run arbitrary user code.
                    if (cancelledMsgs == null)
                    {
                        cancelledMsgs = new List<object>(2);
                    }
                    cancelledMsgs.Add(entry.msg);
                    cancelledBytes += entry.Cancel();
                }
                entry = entry.next;
            } while (entry != null);

            // All flushed so reset unflushedEntry
            unflushedEntry = null;

            if (cancelledMsgs != null)
            {
                for (int i = 0; i < cancelledMsgs.Count; i++)
                {
                    ReferenceCountUtil.SafeRelease(cancelledMsgs[i]);
                }
                // Notify about the freed bytes only now. This may fire channelWritabilityChanged(...) synchronously,
                // and a handler may write, flush or close the channel from there, so this buffer must be consistent
                // first.
                DecrementPendingOutboundBytes(cancelledBytes, false, true);
            }
        }
    }

    /**
     * Increment the pending bytes which will be written at some point.
     * This method is thread-safe!
     */
    // CLR: public for PendingBytesTracker and tests in other assemblies; the source method is package-private.
    public void IncrementPendingOutboundBytes(long size)
    {
        IncrementPendingOutboundBytes(size, true);
    }

    // CLR: internal for AbstractChannelHandlerContext's write task; the source method is private.
    internal void IncrementPendingOutboundBytes(long size, bool invokeLater)
    {
        if (size == 0)
        {
            return;
        }

        long newWriteBufferSize = Interlocked.Add(ref totalPendingSize, size);
        if (newWriteBufferSize > channel.Config.GetWriteBufferHighWaterMark())
        {
            SetUnwritable(invokeLater);
        }
    }

    /**
     * Decrement the pending bytes which will be written at some point.
     * This method is thread-safe!
     */
    public void DecrementPendingOutboundBytes(long size)
    {
        DecrementPendingOutboundBytes(size, true, true);
    }

    internal void DecrementPendingOutboundBytes(long size, bool invokeLater, bool notifyWritability)
    {
        if (size == 0)
        {
            return;
        }

        long newWriteBufferSize = Interlocked.Add(ref totalPendingSize, -size);
        if (notifyWritability && newWriteBufferSize < channel.Config.GetWriteBufferLowWaterMark())
        {
            SetWritable(invokeLater);
        }
    }

    private static long Total(object msg)
    {
        if (msg is ByteBuf buf)
        {
            return buf.ReadableBytes;
        }
        if (msg is IFileRegion region)
        {
            return region.Count();
        }
        if (msg is IByteBufHolder holder)
        {
            return holder.Content.ReadableBytes;
        }
        return -1;
    }

    /**
     * Return the current message to write or {@code null} if nothing was flushed before and so is ready to be written.
     */
    public object Current
    {
        get
        {
            Entry entry = flushedEntry;
            if (entry == null)
            {
                return null;
            }

            return entry.msg;
        }
    }

    /**
     * Return the current message flush progress.
     * @return {@code 0} if nothing was flushed before for the current message or there is no current message
     */
    public long CurrentProgress
    {
        get
        {
            Entry entry = flushedEntry;
            if (entry == null)
            {
                return 0;
            }
            return entry.progress;
        }
    }

    /**
     * Notify the {@link ChannelPromise} of the current message about writing progress.
     */
    public void Progress(long amount)
    {
        Entry e = flushedEntry;
        Debug.Assert(e != null);
        WritePromise p = e.promise;
        // CLR: unchecked keeps the source's long wraparound in checked builds.
        long progress = unchecked(e.progress + amount);
        e.progress = progress;
        Debug.Assert(p != null);
        // fast-path to save O(n) ChannelProgressivePromise's type check on OpenJDK
        // CLR: a write queued without an IProgress observer is the DefaultChannelPromise/VoidChannelPromise case;
        // there is no promise class to inspect.
        if (!p.IsProgressive)
        {
            return;
        }
        // this is going to save from type pollution due to https://bugs.openjdk.org/browse/JDK-8180450
        p.TryProgress(progress, e.total);
    }

    /**
     * Will remove the current message, mark its {@link ChannelPromise} as success and return {@code true}. If no
     * flushed message exists at the time this method is called it will return {@code false} to signal that no more
     * messages are ready to be handled.
     */
    public bool Remove()
    {
        Entry e = flushedEntry;
        if (e == null)
        {
            ClearNioBuffers();
            return false;
        }
        object msg = e.msg;

        WritePromise promise = e.promise;
        int size = e.pendingSize;

        RemoveEntry(e);

        // only release message, notify and decrement if it was not canceled before.
        if (!e.cancelled)
        {
            // this save both checking against the ReferenceCounted interface
            // and makes better use of virtual calls vs interface ones
            if (msg is AbstractReferenceCountedByteBuf buf)
            {
                try
                {
                    // release now as it is flushed.
                    buf.Release();
                }
                catch (Exception t)
                {
                    logger.Warn("Failed to release a ByteBuf: {}", msg, t);
                }
            }
            else
            {
                ReferenceCountUtil.SafeRelease(msg);
            }
            SafeSuccess(promise);
            DecrementPendingOutboundBytes(size, false, true);
        }

        // recycle the entry
        e.UnguardedRecycle();

        return true;
    }

    /**
     * Will remove the current message, mark its {@link ChannelPromise} as failure using the given {@link Throwable}
     * and return {@code true}. If no   flushed message exists at the time this method is called it will return
     * {@code false} to signal that no more messages are ready to be handled.
     */
    public bool Remove(Exception cause)
    {
        return Remove0(cause, true);
    }

    private bool Remove0(Exception cause, bool notifyWritability)
    {
        Entry e = flushedEntry;
        if (e == null)
        {
            ClearNioBuffers();
            return false;
        }
        object msg = e.msg;

        WritePromise promise = e.promise;
        int size = e.pendingSize;

        RemoveEntry(e);

        if (!e.cancelled)
        {
            // only release message, fail and decrement if it was not canceled before.
            ReferenceCountUtil.SafeRelease(msg);

            SafeFail(promise, cause);
            DecrementPendingOutboundBytes(size, false, notifyWritability);
        }

        // recycle the entry
        e.UnguardedRecycle();

        return true;
    }

    private void RemoveEntry(Entry e)
    {
        if (--flushed == 0)
        {
            // processed everything
            flushedEntry = null;
            if (e == tailEntry)
            {
                tailEntry = null;
                unflushedEntry = null;
            }
        }
        else
        {
            flushedEntry = e.next;
        }
    }

    /**
     * Removes the fully written entries and update the reader index of the partially written entry.
     * This operation assumes all messages in this buffer is {@link ByteBuf}.
     */
    public void RemoveBytes(long writtenBytes)
    {
        for (;;)
        {
            object msg = Current;
            if (msg is not ByteBuf buf)
            {
                // CLR: the source asserts writtenBytes == 0. A handler notified synchronously by remove() may close
                // this buffer, which leaves no flushed entry for the bytes already written.
                if (writtenBytes != 0 && !_closed)
                {
                    throw new InvalidOperationException("Written bytes extend beyond flushed byte buffers.");
                }
                break;
            }

            int readerIndex = buf.ReaderIndex;
            int readableBytes = buf.WriterIndex - readerIndex;

            if (readableBytes <= writtenBytes)
            {
                if (writtenBytes != 0)
                {
                    Progress(readableBytes);
                    writtenBytes -= readableBytes;
                }
                Remove();
            }
            else // readableBytes > writtenBytes
            {
                if (writtenBytes != 0)
                {
                    buf.ReaderIndex = readerIndex + (int)writtenBytes;
                    Progress(writtenBytes);
                }
                break;
            }
        }
        ClearNioBuffers();
    }

    // Clear all ByteBuffer from the array so these can be GC'ed.
    // See https://github.com/netty/netty/issues/3837
    private void ClearNioBuffers()
    {
        int count = nioBufferCount;
        if (count > 0)
        {
            nioBufferCount = 0;
            Array.Clear(NIO_BUFFERS.Get(), 0, count);
        }
    }

    /**
     * Returns an array of direct NIO buffers if the currently pending messages are made of {@link ByteBuf} only.
     * {@link #nioBufferCount()} and {@link #nioBufferSize()} will return the number of NIO buffers in the returned
     * array and the total number of readable bytes of the NIO buffers respectively.
     * <p>
     * Note that the returned array is reused and thus should not escape
     * {@link AbstractChannel#doWrite(ChannelOutboundBuffer)}.
     * Refer to {@link NioSocketChannel#doWrite(ChannelOutboundBuffer)} for an example.
     * </p>
     */
    public ReadOnlyMemory<byte>[] NioBuffers()
    {
        return NioBuffers(int.MaxValue, int.MaxValue);
    }

    /**
     * Returns an array of direct NIO buffers if the currently pending messages are made of {@link ByteBuf} only.
     * {@link #nioBufferCount()} and {@link #nioBufferSize()} will return the number of NIO buffers in the returned
     * array and the total number of readable bytes of the NIO buffers respectively.
     * <p>
     * Note that the returned array is reused and thus should not escape
     * {@link AbstractChannel#doWrite(ChannelOutboundBuffer)}.
     * Refer to {@link NioSocketChannel#doWrite(ChannelOutboundBuffer)} for an example.
     * </p>
     * @param maxCount The maximum amount of buffers that will be added to the return value.
     * @param maxBytes A hint toward the maximum number of bytes to include as part of the return value. Note that this
     *                 value maybe exceeded because we make a best effort to include at least 1 {@link ByteBuffer}
     *                 in the return value to ensure write progress is made.
     */
    public ReadOnlyMemory<byte>[] NioBuffers(int maxCount, long maxBytes)
    {
        Debug.Assert(maxCount > 0);
        Debug.Assert(maxBytes > 0);
        long nioBufferSize = 0;
        int nioBufferCount = 0;
        InternalThreadLocalMap threadLocalMap = InternalThreadLocalMap.Get();
        ReadOnlyMemory<byte>[] nioBuffers = NIO_BUFFERS.Get(threadLocalMap);
        Entry entry = flushedEntry;
        while (IsFlushedEntry(entry) && entry.msg is ByteBuf)
        {
            if (!entry.cancelled)
            {
                ByteBuf buf = (ByteBuf)entry.msg;
                int readerIndex = buf.ReaderIndex;
                int readableBytes = buf.WriterIndex - readerIndex;

                if (readableBytes > 0)
                {
                    if (maxBytes - readableBytes < nioBufferSize && nioBufferCount != 0)
                    {
                        // If the nioBufferSize + readableBytes will overflow maxBytes, and there is at least one entry
                        // we stop populate the ByteBuffer array. This is done for 2 reasons:
                        // 1. bsd/osx don't allow to write more bytes then Integer.MAX_VALUE with one writev(...) call
                        // and so will return 'EINVAL', which will raise an IOException. On Linux it may work depending
                        // on the architecture and kernel but to be safe we also enforce the limit here.
                        // 2. There is no sense in putting more data in the array than is likely to be accepted by the
                        // OS.
                        //
                        // See also:
                        // - https://www.freebsd.org/cgi/man.cgi?query=write&sektion=2
                        // - https://linux.die.net//man/2/writev
                        break;
                    }
                    nioBufferSize += readableBytes;
                    int count = entry.count;
                    if (count == -1)
                    {
                        //noinspection ConstantValueVariableUse
                        entry.count = count = NioBufferCountOf(buf, readerIndex, readableBytes);
                    }
                    int neededSpace = Math.Min(maxCount, nioBufferCount + count);
                    if (neededSpace > nioBuffers.Length)
                    {
                        nioBuffers = ExpandNioBufferArray(nioBuffers, neededSpace, nioBufferCount);
                        NIO_BUFFERS.Set(threadLocalMap, nioBuffers);
                    }
                    if (count == 1)
                    {
                        ReadOnlyMemory<byte> nioBuf = entry.buf;
                        // CLR: an empty cached segment stands for the source's null reference (readableBytes > 0).
                        if (nioBuf.IsEmpty || nioBuf.Length < readableBytes)
                        {
                            // cache ByteBuffer as it may need to create a new ByteBuffer instance if its a
                            // derived buffer
                            entry.buf = nioBuf = buf.AsReadOnlySequence(readerIndex, readableBytes).First;
                        }
                        else
                        {
                            // CLR: a ByteBuffer's position advances as the socket writes it, in step with
                            // removeBytes(...); ReadOnlyMemory has no position, so the cached view keeps its unwritten
                            // tail, which is the buffer's readable bytes.
                            nioBuf = nioBuf.Slice(nioBuf.Length - readableBytes);
                        }
                        nioBuffers[nioBufferCount++] = nioBuf;
                    }
                    else
                    {
                        // The code exists in an extra method to ensure the method is not too big to inline as this
                        // branch is not very likely to get hit very frequently.
                        nioBufferCount = NioBuffers(entry, buf, nioBuffers, nioBufferCount, maxCount);
                    }
                    if (nioBufferCount >= maxCount)
                    {
                        break;
                    }
                }
            }
            entry = entry.next;
        }
        this.nioBufferCount = nioBufferCount;
        this.nioBufferSize = nioBufferSize;

        return nioBuffers;
    }

    private static int NioBuffers(Entry entry, ByteBuf buf, ReadOnlyMemory<byte>[] nioBuffers, int nioBufferCount, int maxCount)
    {
        ReadOnlyMemory<byte>[] nioBufs = entry.bufs;
        if (nioBufs == null)
        {
            // cached ByteBuffers as they may be expensive to create in terms
            // of Object allocation
            entry.bufs = nioBufs = NioBuffers(buf.ReadableSequence);
        }
        // CLR: ByteBuffers written by an earlier gathering write have no remaining bytes; ReadOnlyMemory has no
        // position, so the bytes written since the segments were cached are skipped from the front instead.
        long written = -buf.ReadableBytes;
        for (int i = 0; i < nioBufs.Length; i++)
        {
            written += nioBufs[i].Length;
        }
        for (int i = 0; i < nioBufs.Length && nioBufferCount < maxCount; ++i)
        {
            ReadOnlyMemory<byte> nioBuf = nioBufs[i];
            if (written >= nioBuf.Length)
            {
                written -= nioBuf.Length;
                continue;
            }
            if (written > 0)
            {
                nioBuf = nioBuf.Slice((int)written);
                written = 0;
            }
            nioBuffers[nioBufferCount++] = nioBuf;
        }
        return nioBufferCount;
    }

    // CLR: ByteBuf.nioBufferCount() is the number of segments of the borrowed readable sequence; a contiguous
    // buffer has one.
    private static int NioBufferCountOf(ByteBuf buf, int index, int length)
    {
        if (buf.IsContiguous)
        {
            return 1;
        }
        ReadOnlySequence<byte> sequence = buf.AsReadOnlySequence(index, length);
        if (sequence.IsSingleSegment)
        {
            return 1;
        }
        int count = 0;
        foreach (ReadOnlyMemory<byte> _ in sequence)
        {
            count++;
        }
        return count;
    }

    // CLR: ByteBuf.nioBuffers(): the segments of the borrowed readable sequence.
    private static ReadOnlyMemory<byte>[] NioBuffers(ReadOnlySequence<byte> sequence)
    {
        var segments = new List<ReadOnlyMemory<byte>>();
        foreach (ReadOnlyMemory<byte> segment in sequence)
        {
            segments.Add(segment);
        }
        return segments.ToArray();
    }

    private static ReadOnlyMemory<byte>[] ExpandNioBufferArray(ReadOnlyMemory<byte>[] array, int neededSpace, int size)
    {
        int newCapacity = array.Length;
        do
        {
            // double capacity until it is big enough
            // See https://github.com/netty/netty/issues/1890
            newCapacity <<= 1;

            if (newCapacity < 0)
            {
                throw new InvalidOperationException();
            }

        } while (neededSpace > newCapacity);

        var newArray = new ReadOnlyMemory<byte>[newCapacity];
        Array.Copy(array, 0, newArray, 0, size);

        return newArray;
    }

    /**
     * Returns the number of {@link ByteBuffer} that can be written out of the {@link ByteBuffer} array that was
     * obtained via {@link #nioBuffers()}. This method <strong>MUST</strong> be called after {@link #nioBuffers()}
     * was called.
     */
    public int NioBufferCount => nioBufferCount;

    /**
     * Returns the number of bytes that can be written out of the {@link ByteBuffer} array that was
     * obtained via {@link #nioBuffers()}. This method <strong>MUST</strong> be called after {@link #nioBuffers()}
     * was called.
     */
    public long NioBufferSize => nioBufferSize;

    /**
     * Returns {@code true} if and only if {@linkplain #totalPendingWriteBytes() the total number of pending bytes} did
     * not exceed the write watermark of the {@link Channel} and
     * no {@linkplain #setUserDefinedWritability(int, boolean) user-defined writability flag} has been set to
     * {@code false}.
     */
    public bool IsWritable => Volatile.Read(ref unwritable) == 0;

    /**
     * Returns {@code true} if and only if the user-defined writability flag at the specified index is set to
     * {@code true}.
     */
    public bool GetUserDefinedWritability(int index)
    {
        return (Volatile.Read(ref unwritable) & WritabilityMask(index)) == 0;
    }

    /**
     * Sets a user-defined writability flag at the specified index.
     */
    public void SetUserDefinedWritability(int index, bool writable)
    {
        if (writable)
        {
            SetUserDefinedWritability(index);
        }
        else
        {
            ClearUserDefinedWritability(index);
        }
    }

    private void SetUserDefinedWritability(int index)
    {
        int mask = ~WritabilityMask(index);
        for (;;)
        {
            int oldValue = Volatile.Read(ref unwritable);
            int newValue = oldValue & mask;
            if (Interlocked.CompareExchange(ref unwritable, newValue, oldValue) == oldValue)
            {
                if (oldValue != 0 && newValue == 0)
                {
                    FireChannelWritabilityChanged(true);
                }
                break;
            }
        }
    }

    private void ClearUserDefinedWritability(int index)
    {
        int mask = WritabilityMask(index);
        for (;;)
        {
            int oldValue = Volatile.Read(ref unwritable);
            int newValue = oldValue | mask;
            if (Interlocked.CompareExchange(ref unwritable, newValue, oldValue) == oldValue)
            {
                if (oldValue == 0 && newValue != 0)
                {
                    FireChannelWritabilityChanged(true);
                }
                break;
            }
        }
    }

    private static int WritabilityMask(int index)
    {
        if (index < 1 || index > 31)
        {
            throw new ArgumentException("index: " + index.ToString(CultureInfo.InvariantCulture) + " (expected: 1~31)");
        }
        return 1 << index;
    }

    private void SetWritable(bool invokeLater)
    {
        for (;;)
        {
            int oldValue = Volatile.Read(ref unwritable);
            int newValue = oldValue & ~1;
            if (Interlocked.CompareExchange(ref unwritable, newValue, oldValue) == oldValue)
            {
                if (oldValue != 0 && newValue == 0)
                {
                    FireChannelWritabilityChanged(invokeLater);
                }
                break;
            }
        }
    }

    private void SetUnwritable(bool invokeLater)
    {
        for (;;)
        {
            int oldValue = Volatile.Read(ref unwritable);
            int newValue = oldValue | 1;
            if (Interlocked.CompareExchange(ref unwritable, newValue, oldValue) == oldValue)
            {
                if (oldValue == 0)
                {
                    FireChannelWritabilityChanged(invokeLater);
                }
                break;
            }
        }
    }

    private void FireChannelWritabilityChanged(bool invokeLater)
    {
        IChannelPipeline pipeline = channel.Pipeline;
        if (invokeLater)
        {
            // CLR: the source channel drops its buffer reference on close, so later tracker updates cannot notify.
            if (_closed)
            {
                return;
            }
            Action task = fireChannelWritabilityChangedTask;
            if (task == null)
            {
                fireChannelWritabilityChangedTask = task = () => pipeline.FireChannelWritabilityChanged();
            }
            try
            {
                channel.EventLoop.Execute(task);
            }
            catch (RejectedExecutionException)
            {
                // CLR: queued byte accounting must still balance during shutdown.
            }
        }
        else if (channel.EventLoop.InEventLoop())
        {
            pipeline.FireChannelWritabilityChanged();
        }
        else
        {
            // CLR: a native completion may reach the buffer before its owner dispatch; keep the event on the owner.
            channel.EventLoop.Execute(() => pipeline.FireChannelWritabilityChanged());
        }
    }

    /**
     * Returns the number of flushed messages in this {@link ChannelOutboundBuffer}.
     */
    public int Size => flushed;

    /**
     * Returns {@code true} if there are flushed messages in this {@link ChannelOutboundBuffer} or {@code false}
     * otherwise.
     */
    public bool IsEmpty => flushed == 0;

    internal void FailFlushed(Exception cause, bool notify)
    {
        // Make sure that this method does not reenter.  A listener added to the current promise can be notified by the
        // current thread in the tryFailure() call of the loop below, and the listener can trigger another fail() call
        // indirectly (usually by closing the channel.)
        //
        // See https://github.com/netty/netty/issues/1501
        if (inFail)
        {
            return;
        }

        try
        {
            inFail = true;
            for (;;)
            {
                if (!Remove0(cause, notify))
                {
                    break;
                }
            }
        }
        finally
        {
            inFail = false;
        }
    }

    // CLR: InvalidOperationException replaces IllegalStateException.
    internal void Close(Exception cause, bool allowChannelOpen)
    {
        if (inFail)
        {
            channel.EventLoop.Execute(() => Close(cause, allowChannelOpen));
            return;
        }

        inFail = true;

        if (!allowChannelOpen && channel.IsOpen)
        {
            throw new InvalidOperationException("close() must be invoked after the channel is closed.");
        }

        if (!IsEmpty)
        {
            throw new InvalidOperationException("close() must be invoked after all flushed writes are handled.");
        }

        _closed = true;

        // Release all unflushed messages.
        try
        {
            Entry e = unflushedEntry;
            // CLR: this object outlives close (see _closed), so it must not keep the recycled entries.
            unflushedEntry = null;
            tailEntry = null;
            while (e != null)
            {
                // Just decrease; do not trigger any events via decrementPendingOutboundBytes()
                int size = e.pendingSize;
                Interlocked.Add(ref totalPendingSize, -size);

                if (!e.cancelled)
                {
                    ReferenceCountUtil.SafeRelease(e.msg);
                    SafeFail(e.promise, cause);
                }
                e = e.UnguardedRecycleAndGetNext();
            }
        }
        finally
        {
            inFail = false;
        }
        ClearNioBuffers();
    }

    internal void Close(ClosedChannelException cause)
    {
        Close(cause, false);
    }

    // CLR: PromiseNotificationUtil.trySuccess(promise, null, logger) with the source log messages.
    private static void SafeSuccess(WritePromise promise)
    {
        // Only log if the given promise is not of type VoidChannelPromise as trySuccess(...) is expected to return
        // false.
        // CLR: there is no VoidChannelPromise; every queued write owns its Task, so the logger is always used.
        if (!promise.TrySuccess())
        {
            Exception err = promise.Cause;
            if (err == null)
            {
                logger.Warn("Failed to mark a promise as success because it has succeeded already: {}", promise.Task);
            }
            else
            {
                logger.Warn(
                        "Failed to mark a promise as success because it has failed already: {}, unnotified cause:",
                        promise.Task, err);
            }
        }
    }

    // CLR: PromiseNotificationUtil.tryFailure(promise, cause, logger) with the source log messages.
    private static void SafeFail(WritePromise promise, Exception cause)
    {
        // Only log if the given promise is not of type VoidChannelPromise as tryFailure(...) is expected to return
        // false.
        // CLR: there is no VoidChannelPromise; every queued write owns its Task, so the logger is always used.
        if (!promise.TryFailure(cause))
        {
            Exception err = promise.Cause;
            if (err == null)
            {
                logger.Warn("Failed to mark a promise as failure because it has succeeded already: {}", promise.Task, cause);
            }
            else if (logger.IsWarnEnabled())
            {
                logger.Warn(
                        "Failed to mark a promise as failure because it has failed already: {}, unnotified cause:",
                        promise.Task, cause);
            }
        }
    }

    [Obsolete]
    public void Recycle()
    {
        // NOOP
    }

    public long TotalPendingWriteBytes => Volatile.Read(ref totalPendingSize);

    /**
     * Get how many bytes can be written until {@link #isWritable()} returns {@code false}.
     * This quantity will always be non-negative. If {@link #isWritable()} is {@code false} then 0.
     */
    public long BytesBeforeUnwritable
    {
        get
        {
            // +1 because writability doesn't change until the threshold is crossed (not equal to).
            long bytes = channel.Config.GetWriteBufferHighWaterMark() - Volatile.Read(ref totalPendingSize) + 1;
            // If bytes is negative we know we are not writable, but if bytes is non-negative we have to check writability.
            // Note that totalPendingSize and isWritable() use different volatile variables that are not synchronized
            // together. totalPendingSize will be updated before isWritable().
            return bytes > 0 && IsWritable ? bytes : 0;
        }
    }

    /**
     * Get how many bytes must be drained from the underlying buffer until {@link #isWritable()} returns {@code true}.
     * This quantity will always be non-negative. If {@link #isWritable()} is {@code true} then 0.
     */
    public long BytesBeforeWritable
    {
        get
        {
            // +1 because writability doesn't change until the threshold is crossed (not equal to).
            long bytes = Volatile.Read(ref totalPendingSize) - channel.Config.GetWriteBufferLowWaterMark() + 1;
            // If bytes is negative we know we are writable, but if bytes is non-negative we have to check writability.
            // Note that totalPendingSize and isWritable() use different volatile variables that are not synchronized
            // together. totalPendingSize will be updated before isWritable().
            return bytes <= 0 || IsWritable ? 0 : bytes;
        }
    }

    /**
     * Call {@link MessageProcessor#processMessage(Object)} for each flushed message
     * in this {@link ChannelOutboundBuffer} until {@link MessageProcessor#processMessage(Object)}
     * returns {@code false} or there are no more flushed messages to process.
     */
    public void ForEachFlushedMessage(IMessageProcessor processor)
    {
        ObjectUtil.CheckNotNull(processor, "processor");

        Entry entry = flushedEntry;
        if (entry == null)
        {
            return;
        }

        do
        {
            if (!entry.cancelled)
            {
                if (!processor.ProcessMessage(entry.msg))
                {
                    return;
                }
            }
            entry = entry.next;
        } while (IsFlushedEntry(entry));
    }

    private bool IsFlushedEntry(Entry e)
    {
        return e != null && e != unflushedEntry;
    }

    public interface IMessageProcessor
    {
        /**
         * Will be called for each flushed message until it either there are no more flushed messages or this
         * method returns {@code false}.
         */
        bool ProcessMessage(object msg);
    }

    private sealed class Entry
    {
        private static readonly Recycler<Entry> RECYCLER = Recycler.Create<Entry>(handle => new Entry(handle));

        private readonly RecyclerEnhancedHandle<Entry> handle;
        internal Entry next;
        internal object msg;
        internal ReadOnlyMemory<byte>[] bufs;
        internal ReadOnlyMemory<byte> buf;
        internal WritePromise promise;
        internal long progress;
        internal long total;
        internal int pendingSize;
        internal int count = -1;
        internal bool cancelled;

        private Entry(IRecyclerHandle<Entry> handle)
        {
            this.handle = (RecyclerEnhancedHandle<Entry>)handle;
        }

        internal static Entry NewInstance(object msg, int size, long total, WritePromise promise)
        {
            Entry entry = RECYCLER.Get();
            entry.msg = msg;
            // CLR: unchecked keeps the source's int wraparound in checked builds.
            entry.pendingSize = unchecked(size + CHANNEL_OUTBOUND_BUFFER_ENTRY_OVERHEAD);
            entry.total = total;
            entry.promise = promise;
            return entry;
        }

        internal int Cancel()
        {
            if (!cancelled)
            {
                cancelled = true;
                int pSize = pendingSize;

                // replace the message with an empty buffer, the caller is responsible for releasing it
                msg = Unpooled.EmptyBuffer;

                pendingSize = 0;
                total = 0;
                progress = 0;
                bufs = null;
                buf = default;
                return pSize;
            }
            return 0;
        }

        internal void UnguardedRecycle()
        {
            next = null;
            bufs = null;
            buf = default;
            msg = null;
            promise = null;
            progress = 0;
            total = 0;
            pendingSize = 0;
            count = -1;
            cancelled = false;
            handle.UnguardedRecycle(this);
        }

        internal Entry UnguardedRecycleAndGetNext()
        {
            Entry next = this.next;
            UnguardedRecycle();
            return next;
        }
    }

    // CLR: ChannelPromise is replaced by the queued write's Task (Future/Promise -> Task/TaskCompletionSource). This
    // producer is that promise: the caller's CancellationToken stands for promise.cancel(...) until setUncancellable(),
    // which addFlush() calls, and the IProgress observer captured with the write stands for a
    // ChannelProgressivePromise. Continuations run asynchronously, so no listener runs inside this buffer.
    // https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcompletionsource
    // https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken.unsaferegister
    private sealed class WritePromise : TaskCompletionSource
    {
        private const int CANCELLABLE = 0;
        private const int UNCANCELLABLE = 1;
        private const int CANCELLED = 2;

        private readonly IChannel _channel;
        private readonly CancellationToken _token;
        private readonly IProgress<ChannelWriteProgress> _observer;
        private CancellationTokenRegistration _registration;
        private int _state;

        internal WritePromise(IChannel channel, CancellationToken token, IProgress<ChannelWriteProgress> observer)
            : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            _channel = channel;
            _token = token;
            _observer = observer;
        }

        internal bool IsProgressive => _observer != null;

        // The promise's failure, or null: DefaultPromise.cause() of a cancelled promise is its CancellationException.
        internal Exception Cause => Task.IsFaulted ? Task.Exception.InnerException
            : Task.IsCanceled ? new TaskCanceledException(Task) : null;

        internal void RegisterCancellation()
        {
            if (_token.CanBeCanceled)
            {
                _registration = _token.UnsafeRegister(static state => ((WritePromise)state).Cancel(), this);
            }
        }

        private void Cancel()
        {
            // Cancellation claims the result, not the queue. Queue ownership is released by flush/close.
            if (Interlocked.CompareExchange(ref _state, CANCELLED, CANCELLABLE) == CANCELLABLE)
            {
                TrySetCanceled(_token);
            }
        }

        internal bool SetUncancellable()
        {
            int state = Interlocked.CompareExchange(ref _state, UNCANCELLABLE, CANCELLABLE);
            _registration.Dispose();
            return state != CANCELLED;
        }

        internal bool TrySuccess()
        {
            _registration.Dispose();
            return TrySetResult();
        }

        internal bool TryFailure(Exception cause)
        {
            _registration.Dispose();
            return TrySetException(cause);
        }

        // CLR: DefaultChannelProgressivePromise.tryProgress(progress, total).
        internal void TryProgress(long progress, long total)
        {
            if (total < 0)
            {
                total = -1;
                if (progress < 0 || Task.IsCompleted)
                {
                    return;
                }
            }
            else if (progress < 0 || progress > total || Task.IsCompleted)
            {
                return;
            }
            // Native IProgress replaces a producer-owned ChannelProgressivePromise. Report before
            // completing the queue Task; observer failure must not fail or interrupt the socket write.
            try
            {
                _observer.Report(new ChannelWriteProgress(_channel, Task, new TransferProgress(progress, total)));
            }
            catch (Exception error)
            {
                logger.Warn("An exception was thrown by a write progress observer.", error);
            }
        }
    }
}
