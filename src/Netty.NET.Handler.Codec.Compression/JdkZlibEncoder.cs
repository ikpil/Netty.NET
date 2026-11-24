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
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Compresses a {@link ByteBuf} using the deflate algorithm.
 */
// CLR: java.util.zip.Deflater is the internal stand-in Deflater (Deflater.cs, owner decision 2026-11-23), which keeps
// the Java calls and constants over the BCL DeflateStream/ZLibStream; its compressed bytes can differ from Java's zlib,
// the inflated data are the same. java.util.zip.CRC32 is System.IO.Hashing.Crc32 (owner decision 2026-11-23; written
// in full because the bzip2 Crc32 of this module shares the namespace): update(b, off, len) is Append over the range
// and getValue() is GetCurrentHashAsUInt32(), "the current computed hash value"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32.getcurrenthashasuint32?view=net-10.0-pp).
// ChannelFuture/ChannelPromise are Task (docs/transport-porting.md, see ZlibEncoder): close() and close(ChannelPromise)
// are the one CloseAsync(CancellationToken = default), ctx.newPromise() has no Task counterpart and is the default
// token, ctx.write(footer) and ctx.writeAndFlush(footer, promise) return their Tasks, close(ctx, promise) is
// CloseAsync(ctx, cancellationToken), and PromiseNotifier.cascade(f, promise) copies the outcome
// of f into the returned Task with TaskCompletionSource.TrySetFromTask, which copies "the completion status (including
// exception or cancellation information)"
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0,
// docs/common-task-composition.md). The ObjectUtil checks are the Common ports (IllegalArgumentException is
// ArgumentException); checkNotNull(wrapper) has no counterpart because ZlibWrapper is a C# enum (a value type). The
// Buffer port has no hasArray()/array()/arrayOffset(); borrowed memory and MemoryMarshal.TryGetArray replace them
// (docs/buffer-porting.md, Array extraction), see HasArray and ArrayOf below. (int) Math.ceil(...) saturates in Java and,
// since .NET 9, in C# ("if the converted value is too small or large for the target type, the value is set to the
// minimum or maximum value", https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer), so the
// overflow check of allocateBuffer sees the same values. EmptyArrays.EMPTY_BYTES is the Common EmptyArrays.
public class JdkZlibEncoder : ZlibEncoder
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(JdkZlibEncoder));

    /**
     * Maximum initial size for temporary heap buffers used for the compressed output. Buffer may still grow beyond
     * this if necessary.
     */
    private static readonly int MAX_INITIAL_OUTPUT_BUFFER_SIZE;
    /**
     * Max size for temporary heap buffers used to copy input data to heap.
     */
    private static readonly int MAX_INPUT_BUFFER_SIZE;

    private readonly ZlibWrapper wrapper;
    private readonly Deflater deflater;
    private volatile bool finished;
    private volatile IChannelHandlerContext ctx;

    /*
     * GZIP support
     */
    private readonly System.IO.Hashing.Crc32 crc;
    private static readonly byte[] gzipHeader = {0x1f, 0x8b, Deflater.DEFLATED, 0, 0, 0, 0, 0, 0, 0};
    private bool writeHeader = true;

    static JdkZlibEncoder()
    {
        MAX_INITIAL_OUTPUT_BUFFER_SIZE = SystemPropertyUtil.GetInt(
                "io.netty.jdkzlib.encoder.maxInitialOutputBufferSize",
                65536);
        MAX_INPUT_BUFFER_SIZE = SystemPropertyUtil.GetInt(
                "io.netty.jdkzlib.encoder.maxInputBufferSize",
                65536);

        if (logger.IsDebugEnabled())
        {
            logger.Debug("-Dio.netty.jdkzlib.encoder.maxInitialOutputBufferSize={}", MAX_INITIAL_OUTPUT_BUFFER_SIZE);
            logger.Debug("-Dio.netty.jdkzlib.encoder.maxInputBufferSize={}", MAX_INPUT_BUFFER_SIZE);
        }
    }

    /**
     * Creates a new zlib encoder with a compression level of ({@code 6})
     * and the default wrapper ({@link ZlibWrapper#ZLIB}).
     *
     * @throws CompressionException if failed to initialize zlib
     */
    public JdkZlibEncoder() : this(6)
    {
    }

    /**
     * Creates a new zlib encoder with the specified {@code compressionLevel}
     * and the default wrapper ({@link ZlibWrapper#ZLIB}).
     *
     * @param compressionLevel
     *        {@code 1} yields the fastest compression and {@code 9} yields the
     *        best compression.  {@code 0} means no compression.  The default
     *        compression level is {@code 6}.
     *
     * @throws CompressionException if failed to initialize zlib
     */
    public JdkZlibEncoder(int compressionLevel) : this(ZlibWrapper.ZLIB, compressionLevel)
    {
    }

    /**
     * Creates a new zlib encoder with a compression level of ({@code 6})
     * and the specified wrapper.
     *
     * @throws CompressionException if failed to initialize zlib
     */
    public JdkZlibEncoder(ZlibWrapper wrapper) : this(wrapper, 6)
    {
    }

    /**
     * Creates a new zlib encoder with the specified {@code compressionLevel}
     * and the specified wrapper.
     *
     * @param compressionLevel
     *        {@code 1} yields the fastest compression and {@code 9} yields the
     *        best compression.  {@code 0} means no compression.  The default
     *        compression level can be set as {@code -1} which correlates to the underlying
     *        {@link Deflater#DEFAULT_COMPRESSION} level.
     *
     * @throws CompressionException if failed to initialize zlib
     */
    public JdkZlibEncoder(ZlibWrapper wrapper, int compressionLevel)
    {
        ObjectUtil.CheckInRange(compressionLevel, Deflater.DEFAULT_COMPRESSION, Deflater.BEST_COMPRESSION,
                "compressionLevel");

        if (wrapper == ZlibWrapper.ZLIB_OR_NONE)
        {
            throw new ArgumentException(
                    "wrapper '" + ZlibWrapper.ZLIB_OR_NONE + "' is not " +
                    "allowed for compression.");
        }

        this.wrapper = wrapper;
        deflater = new Deflater(compressionLevel, wrapper != ZlibWrapper.ZLIB);
        this.crc = wrapper == ZlibWrapper.GZIP ? new System.IO.Hashing.Crc32() : null;
    }

    /**
     * Creates a new zlib encoder with a compression level of ({@code 6})
     * and the specified preset dictionary.  The wrapper is always
     * {@link ZlibWrapper#ZLIB} because it is the only format that supports
     * the preset dictionary.
     *
     * @param dictionary  the preset dictionary
     *
     * @throws CompressionException if failed to initialize zlib
     */
    // CLR: kept as public API, but the BCL streams have no dictionary API, so Deflater.SetDictionary throws
    // NotSupportedException and so do these two constructors (see Deflater.cs).
    public JdkZlibEncoder(byte[] dictionary) : this(6, dictionary)
    {
    }

    /**
     * Creates a new zlib encoder with the specified {@code compressionLevel}
     * and the specified preset dictionary.  The wrapper is always
     * {@link ZlibWrapper#ZLIB} because it is the only format that supports
     * the preset dictionary.
     *
     * @param compressionLevel
     *        {@code 1} yields the fastest compression and {@code 9} yields the
     *        best compression.  {@code 0} means no compression.  The default
     *        compression level can be set as {@code -1} which correlates to the underlying
     *        {@link Deflater#DEFAULT_COMPRESSION} level.
     * @param dictionary  the preset dictionary
     *
     * @throws CompressionException if failed to initialize zlib
     */
    public JdkZlibEncoder(int compressionLevel, byte[] dictionary)
    {
        ObjectUtil.CheckInRange(compressionLevel, Deflater.DEFAULT_COMPRESSION, Deflater.BEST_COMPRESSION,
                "compressionLevel");
        ObjectUtil.CheckNotNull(dictionary, "dictionary");

        wrapper = ZlibWrapper.ZLIB;
        deflater = new Deflater(compressionLevel);
        deflater.SetDictionary(dictionary);
        crc = null;
    }

    // CLR: close() and close(promise) are the one CloseAsync (see ZlibEncoder); close() passed ctx().newPromise(), so a
    // call without a token is close(). Off the event loop a TaskCompletionSource stands for the promise and the
    // anonymous Runnable is a lambda.
    public override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        IChannelHandlerContext ctx = Ctx();
        IEventExecutor executor = ctx.Executor;
        if (executor.InEventLoop())
        {
            return FinishEncode(ctx, cancellationToken);
        }
        else
        {
            TaskCompletionSource p = new TaskCompletionSource();
            executor.Execute(() =>
            {
                Task f = FinishEncode(Ctx(), cancellationToken);
                _ = f.ContinueWith(static (task, state) => ((TaskCompletionSource) state).TrySetFromTask(task), p,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            });
            return p.Task;
        }
    }

    private IChannelHandlerContext Ctx()
    {
        IChannelHandlerContext ctx = this.ctx;
        if (ctx == null)
        {
            throw new InvalidOperationException("not added to a pipeline");
        }
        return ctx;
    }

    public override bool IsClosed()
    {
        return finished;
    }

    protected override void Encode(IChannelHandlerContext ctx, ByteBuf uncompressed, ByteBuf @out)
    {
        if (finished)
        {
            @out.WriteBytes(uncompressed);
            return;
        }

        int len = uncompressed.ReadableBytes;
        if (len == 0)
        {
            return;
        }

        if (HasArray(uncompressed))
        {
            // if it is backed by an array we not need to do a copy at all
            EncodeSome(uncompressed, @out);
        }
        else
        {
            int heapBufferSize = Math.Min(len, MAX_INPUT_BUFFER_SIZE);
            ByteBuf heapBuf = ctx.Alloc().HeapBuffer(heapBufferSize, heapBufferSize);
            try
            {
                while (uncompressed.IsReadable())
                {
                    uncompressed.ReadBytes(heapBuf, Math.Min(heapBuf.WritableBytes, uncompressed.ReadableBytes));
                    EncodeSome(heapBuf, @out);
                    heapBuf.Clear();
                }
            }
            finally
            {
                heapBuf.Release();
            }
        }
        // clear input so that we don't keep an unnecessary reference to the input array
        deflater.SetInput(EmptyArrays.EMPTY_BYTES);
    }

    private void EncodeSome(ByteBuf @in, ByteBuf @out)
    {
        // both in and out are heap buffers, here

        ArraySegment<byte> inArray = ArrayOf(@in, @in.ReaderIndex, @in.ReadableBytes);
        byte[] inAry = inArray.Array;
        int offset = inArray.Offset;

        if (writeHeader)
        {
            writeHeader = false;
            if (wrapper == ZlibWrapper.GZIP)
            {
                @out.WriteBytes(gzipHeader);
            }
        }

        int len = @in.ReadableBytes;
        if (wrapper == ZlibWrapper.GZIP)
        {
            crc.Append(new ReadOnlySpan<byte>(inAry, offset, len));
        }

        deflater.SetInput(inAry, offset, len);
        for (;;)
        {
            Deflate(@out);
            if (!@out.IsWritable())
            {
                // The buffer is not writable anymore. Increase the capacity to make more room.
                // Can't rely on needsInput here, it might return true even if there's still data to be written.
                @out.EnsureWritable(@out.WriterIndex);
            }
            else if (deflater.NeedsInput())
            {
                // Consumed everything
                break;
            }
        }
        @in.SkipBytes(len);
    }

    protected sealed override ByteBuf AllocateBuffer(IChannelHandlerContext ctx, ByteBuf msg,
                                                     bool preferDirect)
    {
        int sizeEstimate = (int) Math.Ceiling(msg.ReadableBytes * 1.001) + 12;
        if (writeHeader)
        {
            switch (wrapper)
            {
                case ZlibWrapper.GZIP:
                    sizeEstimate += gzipHeader.Length;
                    break;
                case ZlibWrapper.ZLIB:
                    sizeEstimate += 2; // first two magic bytes
                    break;
                default:
                    // no op
                    break;
            }
        }
        // sizeEstimate might overflow if close to 2G
        if (sizeEstimate < 0 || sizeEstimate > MAX_INITIAL_OUTPUT_BUFFER_SIZE)
        {
            // can always expand later
            return ctx.Alloc().HeapBuffer(MAX_INITIAL_OUTPUT_BUFFER_SIZE);
        }
        return ctx.Alloc().HeapBuffer(sizeEstimate);
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        Task f = FinishEncode(ctx, CancellationToken.None);
        return EncoderUtil.CloseAfterFinishEncode(ctx, f, cancellationToken);
    }

    private Task FinishEncode(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (finished)
        {
            return Task.CompletedTask;
        }

        finished = true;
        ByteBuf footer = ctx.Alloc().HeapBuffer();
        if (writeHeader && wrapper == ZlibWrapper.GZIP)
        {
            // Write the GZIP header first if not written yet. (i.e. user wrote nothing.)
            writeHeader = false;
            footer.WriteBytes(gzipHeader);
        }

        deflater.Finish();

        while (!deflater.Finished())
        {
            Deflate(footer);
            if (!footer.IsWritable())
            {
                // no more space so write it to the channel and continue
                _ = ctx.WriteAsync(footer);
                footer = ctx.Alloc().HeapBuffer();
            }
        }
        if (wrapper == ZlibWrapper.GZIP)
        {
            int crcValue = unchecked((int) crc.GetCurrentHashAsUInt32());
            int uncBytes = deflater.GetTotalIn();
            footer.WriteIntLE(crcValue);
            footer.WriteIntLE(uncBytes);
        }
        deflater.End();
        return ctx.WriteAndFlushAsync(footer, cancellationToken);
    }

    private void Deflate(ByteBuf @out)
    {
        int numBytes;
        do
        {
            int writerIndex = @out.WriterIndex;
            ArraySegment<byte> outArray = ArrayOf(@out, writerIndex, @out.WritableBytes);
            numBytes = deflater.Deflate(
                    outArray.Array, outArray.Offset, @out.WritableBytes, Deflater.SYNC_FLUSH);
            @out.WriterIndex = writerIndex + numBytes;
        } while (numBytes > 0);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
    }

    // CLR: hasArray(): the readable range is one segment backed by a managed array (AsReadOnlySequence and
    // MemoryMarshal.TryGetArray, docs/buffer-porting.md, Array extraction). As the Buffer port records, a range in one
    // array of a composite buffer also counts as array-backed where Java's hasArray() is false; both paths compress the
    // same bytes.
    private static bool HasArray(ByteBuf buf)
    {
        ReadOnlySequence<byte> range = buf.AsReadOnlySequence(buf.ReaderIndex, buf.ReadableBytes);
        return range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out ArraySegment<byte> _);
    }

    // CLR: array() and arrayOffset() + index of an array-backed buffer: the array segment that starts at buffer index
    // index (the readable range of the input, the writable range of the heap output buffers). Like array(), it throws
    // NotSupportedException (Java: UnsupportedOperationException) when the range is not backed by one managed array.
    private static ArraySegment<byte> ArrayOf(ByteBuf buf, int index, int length)
    {
        ReadOnlySequence<byte> range = buf.AsReadOnlySequence(index, length);
        if (range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out ArraySegment<byte> array))
        {
            return array;
        }
        throw new NotSupportedException("the buffer range is not backed by one managed array");
    }
}
