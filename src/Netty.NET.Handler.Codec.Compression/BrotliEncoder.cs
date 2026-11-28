/*
 * Copyright 2021 The Netty Project
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Compress a {@link ByteBuf} with the Brotli compression.
 * <p>
 * See <a href="https://github.com/google/brotli">brotli</a>.
 */
// CLR: owner decision 2026-11-23: brotli4j's BrotliEncoderChannel is replaced by the BCL
// System.IO.Compression.BrotliEncoder struct (written with its namespace, because this class has the same simple name).
// brotli4j's Encoder.Parameters is BrotliOptions, which holds the quality, window and mode (see BrotliOptions): the
// constructor BrotliEncoder(Encoder.Parameters parameters, boolean isSharable) takes the BrotliOptions that holds the
// parameters (Java's brotliOptions.parameters()), so a non-sharable encoder can still be created, and
// BrotliEncoder(Encoder.Parameters) is not ported: with that type it would be BrotliEncoder(BrotliOptions), which
// already exists with the same behaviour. @ChannelHandler.Sharable is [Sharable] and isSharable() is the Transport
// port's IsSharable property, as codec-base's DatagramPacketDecoder overrides it. ChannelFuture/ChannelPromise are Task
// and TaskCompletionSource, close(ctx, promise) is CloseAsync(ctx, cancellationToken) returning the promise's Task, and
// ctx.newSucceededFuture() is a completed Task (docs/transport-porting.md, EncoderUtil). AttributeKey and the
// channel attributes are the Common AttributeKey<T>/IAttribute<T>, as in codec-mqtt's MqttCodecUtil. C# has no throws
// clauses.
[Sharable]
public sealed class BrotliEncoder : MessageToByteEncoder<ByteBuf>
{

    private static readonly AttributeKey<Writer> ATTR = AttributeKey<Writer>.ValueOf("BrotliEncoderWriter");

    private readonly BrotliOptions parameters;
    private readonly bool isSharable;
    private Writer writer;

    /**
     * Create a new {@link BrotliEncoder} Instance with {@link BrotliOptions#DEFAULT}
     * and {@link #isSharable()} set to {@code true}
     */
    public BrotliEncoder() : this(BrotliOptions.DEFAULT)
    {
    }

    /**
     * Create a new {@link BrotliEncoder} Instance
     *
     * @param brotliOptions {@link BrotliOptions} to use and
     *                      {@link #isSharable()} set to {@code true}
     */
    // CLR: Java's this(brotliOptions.parameters()) goes through BrotliEncoder(Encoder.Parameters) to this(parameters,
    // true); here it calls BrotliEncoder(BrotliOptions, bool) directly (see the class comment).
    public BrotliEncoder(BrotliOptions brotliOptions) : this(brotliOptions, true)
    {
    }

    /**
     * <p>
     * Create a new {@link BrotliEncoder} Instance and specify
     * whether this instance will be shared with multiple pipelines or not.
     * </p>
     *
     * If {@link #isSharable()} is true then on {@link #handlerAdded(ChannelHandlerContext)} call,
     * a new {@link Writer} will create, and it will be mapped using {@link Channel#attr(AttributeKey)}
     * so {@link BrotliEncoder} can be shared with multiple pipelines. This works fine but there on every
     * {@link #encode(ChannelHandlerContext, ByteBuf, ByteBuf)} call, we have to get the {@link Writer} associated
     * with the appropriate channel. And this will add a overhead. So it is recommended to set {@link #isSharable()}
     * to {@code false} and create new {@link BrotliEncoder} instance for every pipeline.
     *
     * @param parameters {@link Encoder.Parameters} to use
     * @param isSharable Set to {@code true} if this instance is shared else set to {@code false}
     */
    // CLR: parameters is the BrotliOptions that holds the encoder parameters (see the class comment); the Java name is
    // kept. checkNotNull's NullPointerException is ArgumentNullException (docs/codec-base-porting.md).
    public BrotliEncoder(BrotliOptions parameters, bool isSharable) : base(typeof(ByteBuf))
    {
        this.parameters = ObjectUtil.CheckNotNull(parameters, "Parameters");
        this.isSharable = isSharable;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        Writer writer = new Writer(parameters, ctx);

        if (isSharable)
        {
            ctx.Channel.Attr(ATTR).Set(writer);
        }
        else
        {
            this.writer = writer;
        }

        base.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        Finish(ctx);
        base.HandlerRemoved(ctx);
    }

    protected override void Encode(IChannelHandlerContext ctx, ByteBuf msg, ByteBuf @out)
    {
        // NO-OP
    }

    protected override ByteBuf AllocateBuffer(IChannelHandlerContext ctx, ByteBuf msg, bool preferDirect)
    {
        if (!msg.IsReadable())
        {
            return Unpooled.EmptyBuffer;
        }

        Writer writer;

        if (isSharable)
        {
            writer = ctx.Channel.Attr(ATTR).Get();
        }
        else
        {
            writer = this.writer;
        }

        // If Writer is 'null' then Writer is not open.
        if (writer == null)
        {
            return Unpooled.EmptyBuffer;
        }
        else
        {
            writer.Encode(msg, preferDirect);
            return writer.writableBuffer;
        }
    }

    public override bool IsSharable => isSharable;

    /**
     * Finish the encoding, close streams and write final {@link ByteBuf} to the channel.
     *
     * @param ctx {@link ChannelHandlerContext} which we want to close
     * @throws IOException If an error occurred during closure
     */
    public void Finish(IChannelHandlerContext ctx)
    {
        _ = FinishEncode(ctx);
    }

    private Task FinishEncode(IChannelHandlerContext ctx)
    {
        Writer writer;

        if (isSharable)
        {
            writer = ctx.Channel.Attr(ATTR).GetAndSet(null);
        }
        else
        {
            writer = this.writer;
        }

        if (writer != null)
        {
            writer.Close();
            this.writer = null;
            return writer.closeFuture.Task;
        }

        return Task.CompletedTask;
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        Task f = FinishEncode(ctx);
        return EncoderUtil.CloseAfterFinishEncode(ctx, f, cancellationToken);
    }

    /**
     * {@link Writer} is the implementation of {@link WritableByteChannel} which encodes
     * Brotli data and stores it into {@link ByteBuf}.
     */
    // CLR: java.nio.channels.WritableByteChannel has no BCL counterpart; Writer keeps its members write(ByteBuffer),
    // isOpen() and close() without the interface, and java.nio.ByteBuffer is ReadOnlySpan<byte>. brotli4j's
    // BrotliEncoderChannel compresses what write(ByteBuffer) receives and hands every compressed chunk to the
    // destination channel, this Writer
    // (https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli4j/src/main/java/com/aayushatharva/brotli4j/encoder/Encoder.java,
    // pushOutput); here the BCL encoder compresses into outputBuffer and each chunk is passed to Write in the same way.
    // The BCL encoder is a mutable struct, so the field is not readonly (a readonly field would hand each call a copy).
    // ClosedChannelException is the Transport ClosedChannelException; the private constructor and the fields that the
    // enclosing class reads are internal, because a C# enclosing class cannot read the private members of its nested
    // class.
    private sealed class Writer
    {

        internal ByteBuf writableBuffer;
        private System.IO.Compression.BrotliEncoder brotliEncoderChannel;
        // CLR: the buffer that receives the BCL encoder's output, 16384 bytes, the intermediate buffer size of brotli4j's
        // BrotliEncoderChannel (DEFAULT_BUFFER_SIZE,
        // https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli4j/src/main/java/com/aayushatharva/brotli4j/encoder/BrotliEncoderChannel.java).
        private readonly byte[] outputBuffer = new byte[16384];
        private readonly IChannelHandlerContext ctx;
        internal readonly TaskCompletionSource closeFuture;
        private bool closeInitiated;
        private bool isClosed;

        // CLR: new BrotliEncoderChannel(this, parameters) is new System.IO.Compression.BrotliEncoder(quality, window),
        // which throws ArgumentOutOfRangeException for a quality outside 0..11 or a window outside 10..24 and IOException
        // when the native encoder cannot be created
        // (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.-ctor?view=net-10.0). The
        // mode is not passed (see BrotliMode). ctx.newPromise() is a TaskCompletionSource (see EncoderUtil).
        internal Writer(BrotliOptions parameters, IChannelHandlerContext ctx)
        {
            brotliEncoderChannel = new System.IO.Compression.BrotliEncoder(parameters.quality, parameters.window);
            this.ctx = ctx;
            this.closeFuture = new TaskCompletionSource();
        }

        // CLR: brotliEncoderChannel.write(nioBuffer) is Compress(source, outputBuffer, ..., isFinalBlock: false), which
        // lets "the encoder to postpone the production of output until it has processed enough input"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.compress?view=net-10.0), and
        // brotliEncoderChannel.flush() is Flush(outputBuffer, ...), which "ensures that output is produced for all the
        // processed input" (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.flush?view=net-10.0).
        // Both are repeated while they return DestinationTooSmall: "The input is partially processed, up to what could
        // fit into the destination buffer" (https://learn.microsoft.com/en-us/dotnet/api/system.buffers.operationstatus?view=net-10.0).
        // Compress returns Done once it consumed the whole source and has no output left, and InvalidData when the
        // native encoder fails
        // (https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Compression.Brotli/src/System/IO/Compression/enc/BrotliEncoder.cs),
        // where brotli4j throws IOException("encoding failed"). The position of the borrowed ByteBuffer is the number of
        // consumed bytes (CompressionUtil.SafeReadableNioBuffer returns a ReadOnlyMemory<byte>). The rethrow of
        // catch (Exception e) { ...; throw e; } is throw;.
        internal void Encode(ByteBuf msg, bool preferDirect)
        {
            try
            {
                Allocate(preferDirect);

                // Compress data and flush it into Buffer.
                //
                // As soon as we call flush, Encoder will be triggered to write encoded
                // data into WritableByteChannel.
                //
                // A race condition will not arise because one flush call to encoder will result
                // in only 1 call at `write(ByteBuffer)`.
                ReadOnlyMemory<byte> nioBuffer = CompressionUtil.SafeReadableNioBuffer(msg);
                int position = 0;
                OperationStatus status;

                do
                {
                    status = brotliEncoderChannel.Compress(nioBuffer.Span.Slice(position), outputBuffer,
                            out int bytesConsumed, out int bytesWritten, false);
                    position += bytesConsumed;
                    Write(outputBuffer.AsSpan(0, bytesWritten));
                } while (status == OperationStatus.DestinationTooSmall);

                if (status == OperationStatus.InvalidData)
                {
                    throw new IOException("encoding failed");
                }

                msg.SkipBytes(position);

                do
                {
                    status = brotliEncoderChannel.Flush(outputBuffer, out int bytesWritten);
                    Write(outputBuffer.AsSpan(0, bytesWritten));
                } while (status == OperationStatus.DestinationTooSmall);

                if (status == OperationStatus.InvalidData)
                {
                    throw new IOException("encoding failed");
                }
            }
            catch (Exception)
            {
                ReferenceCountUtil.Release(msg);
                throw;
            }
        }

        private void Allocate(bool preferDirect)
        {
            if (preferDirect)
            {
                writableBuffer = ctx.Alloc().IoBuffer();
            }
            else
            {
                writableBuffer = ctx.Alloc().Buffer();
            }
        }

        public int Write(ReadOnlySpan<byte> src)
        {
            if (!IsOpen())
            {
                throw new ClosedChannelException();
            }

            return writableBuffer.WriteBytes(src).ReadableBytes;
        }

        public bool IsOpen()
        {
            return !isClosed;
        }

        // CLR: the anonymous Runnable is a lambda passed to IExecutor.Execute(Action), and IllegalStateException is
        // InvalidOperationException (docs/codec-base-porting.md); closeFuture.setFailure(...) is SetException(...).
        public void Close()
        {
            if (closeInitiated)
            {
                return;
            }

            closeInitiated = true;
            ctx.Executor.Execute(() =>
            {
                try
                {
                    Finish(closeFuture);
                }
                catch (IOException ex)
                {
                    closeFuture.SetException(new InvalidOperationException("Failed to finish encoding", ex));
                }
            });
        }

        // CLR: brotliEncoderChannel.close() is Compress(empty source, outputBuffer, ..., isFinalBlock: true), which
        // finalizes "the internal stream" (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.compress?view=net-10.0),
        // repeated while it returns DestinationTooSmall, followed by Dispose(), as brotli4j's close() runs
        // encode(FINISH) and then destroys the native encoder in a finally block (Encoder.java, see the class comment);
        // its destination.close() call reaches Writer.close(), which returns at once because closeInitiated is set.
        // promise.setFailure(ex) is SetException(ex), and ctx.writeAndFlush(writableBuffer, promise) copies the
        // outcome of WriteAndFlushAsync into the promise with TrySetFromTask, as EncoderUtil completes its promise.
        public void Finish(TaskCompletionSource promise)
        {
            if (!isClosed)
            {
                // Allocate a buffer and write last pending data.
                Allocate(true);

                try
                {
                    try
                    {
                        OperationStatus status;

                        do
                        {
                            status = brotliEncoderChannel.Compress(ReadOnlySpan<byte>.Empty, outputBuffer,
                                    out _, out int bytesWritten, true);
                            Write(outputBuffer.AsSpan(0, bytesWritten));
                        } while (status == OperationStatus.DestinationTooSmall);

                        if (status == OperationStatus.InvalidData)
                        {
                            throw new IOException("encoding failed");
                        }
                    }
                    finally
                    {
                        brotliEncoderChannel.Dispose();
                    }

                    isClosed = true;
                }
                catch (Exception ex)
                {
                    promise.SetException(ex);

                    // Since we have already allocated Buffer for close operation,
                    // we will release that buffer to prevent memory leak.
                    ReferenceCountUtil.Release(writableBuffer);
                    return;
                }

                _ = ctx.WriteAndFlushAsync(writableBuffer).ContinueWith(static (write, state) =>
                        ((TaskCompletionSource) state).TrySetFromTask(write), promise,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
