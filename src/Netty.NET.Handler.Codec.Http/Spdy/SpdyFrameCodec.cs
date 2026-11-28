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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * A {@link ChannelHandler} that encodes and decodes SPDY Frames.
 */
// CLR: SpdyFrameDecoderDelegate and ChannelOutboundHandler are ISpdyFrameDecoderDelegate and
// IChannelOutboundHandler. The ChannelOutboundHandler operations are the Task-returning *Async members of
// IChannelOutboundHandler, whose Task replaces the promise, as core HttpClientUpgradeHandler implements them; an
// exception thrown by WriteAsync fails the write's Task in the pipeline, as Java's pipeline fails the promise.
// SocketAddress is EndPoint. The close future listener of handlerAdded is a continuation of IChannel.CloseFuture
// that runs the listener at once on the event loop and otherwise through executor.Execute, the ChunkedWriteHandler
// listener policy of docs/handler-porting.md (Listener timing). Java's protected members are protected (the
// anonymous decoder is nested and reaches the codec's isValidUnknownFrameHeader), and Java methods are virtual
// unless final. List<Object> out is IList<object> @out and ByteBuf in is @in. The Java byte priority and flags are
// CLR bytes (the wire octets).
public class SpdyFrameCodec : ByteToMessageDecoder, ISpdyFrameDecoderDelegate, IChannelOutboundHandler
{

    protected static readonly SpdyProtocolException INVALID_FRAME =
        new SpdyProtocolException("Received invalid frame");

    protected readonly SpdyFrameDecoder spdyFrameDecoder;
    protected readonly SpdyFrameEncoder spdyFrameEncoder;
    private readonly SpdyHeaderBlockDecoder spdyHeaderBlockDecoder;
    private readonly SpdyHeaderBlockEncoder spdyHeaderBlockEncoder;

    private ISpdyHeadersFrame spdyHeadersFrame;
    private ISpdySettingsFrame spdySettingsFrame;

    private IChannelHandlerContext ctx;
    private bool read;
    private readonly bool validateHeaders;
    private readonly bool supportsUnknownFrames;

    /**
     * Creates a new instance with the specified {@code version},
     * {@code validateHeaders (true)}, and
     * the default decoder and encoder options
     * ({@code maxChunkSize (8192)}, {@code maxHeaderSize (16384)},
     * {@code compressionLevel (6)}, {@code windowBits (15)},
     * and {@code memLevel (8)}).
     */
    public SpdyFrameCodec(SpdyVersion version) : this(version, true)
    {
    }

    /**
     * Creates a new instance with the specified {@code version},
     * {@code validateHeaders}, and
     * the default decoder and encoder options
     * ({@code maxChunkSize (8192)}, {@code maxHeaderSize (16384)},
     * {@code compressionLevel (6)}, {@code windowBits (15)},
     * and {@code memLevel (8)}).
     */
    public SpdyFrameCodec(SpdyVersion version, bool validateHeaders)
        : this(version, 8192, 16384, 6, 15, 8, validateHeaders)
    {
    }

    /**
     * Creates a new instance with the specified {@code version}, {@code validateHeaders (true)},
     * decoder and encoder options.
     */
    public SpdyFrameCodec(
            SpdyVersion version, int maxChunkSize, int maxHeaderSize,
            int compressionLevel, int windowBits, int memLevel)
        : this(version, maxChunkSize, maxHeaderSize, compressionLevel, windowBits, memLevel, true)
    {
    }

    /**
     * Creates a new instance with the specified {@code version}, {@code validateHeaders},
     * decoder and encoder options.
     */
    public SpdyFrameCodec(
            SpdyVersion version, int maxChunkSize, int maxHeaderSize,
            int compressionLevel, int windowBits, int memLevel, bool validateHeaders)
        : this(version, maxChunkSize,
                SpdyHeaderBlockDecoder.NewInstance(version, maxHeaderSize),
                SpdyHeaderBlockEncoder.NewInstance(version, compressionLevel, windowBits, memLevel),
                validateHeaders, false)
    {
    }

    /**
     * Creates a new instance with the specified {@code version}, {@code validateHeaders},
     * decoder and encoder options.
     */
    public SpdyFrameCodec(
            SpdyVersion version, int maxChunkSize, int maxHeaderSize,
            int compressionLevel, int windowBits, int memLevel, bool validateHeaders,
            bool supportsUnknownFrames)
        : this(version, maxChunkSize,
                SpdyHeaderBlockDecoder.NewInstance(version, maxHeaderSize),
                SpdyHeaderBlockEncoder.NewInstance(version, compressionLevel, windowBits, memLevel),
                validateHeaders, supportsUnknownFrames)
    {
    }

    protected SpdyFrameCodec(SpdyVersion version, int maxChunkSize,
                             SpdyHeaderBlockDecoder spdyHeaderBlockDecoder,
                             SpdyHeaderBlockEncoder spdyHeaderBlockEncoder,
                             bool validateHeaders)
        : this(version, maxChunkSize, spdyHeaderBlockDecoder, spdyHeaderBlockEncoder, validateHeaders, false)
    {
    }

    protected SpdyFrameCodec(SpdyVersion version, int maxChunkSize,
            SpdyHeaderBlockDecoder spdyHeaderBlockDecoder, SpdyHeaderBlockEncoder spdyHeaderBlockEncoder,
            bool validateHeaders, bool supportsUnknownFrames)
    {
        this.supportsUnknownFrames = supportsUnknownFrames;
        spdyFrameDecoder = CreateDecoder(version, this, maxChunkSize);
        spdyFrameEncoder = CreateEncoder(version);
        this.spdyHeaderBlockDecoder = spdyHeaderBlockDecoder;
        this.spdyHeaderBlockEncoder = spdyHeaderBlockEncoder;
        this.validateHeaders = validateHeaders;
    }

    protected virtual SpdyFrameDecoder CreateDecoder(SpdyVersion version, ISpdyFrameDecoderDelegate @delegate,
                                                     int maxChunkSize)
    {
        return new SpdyFrameDecoder_(this, version, @delegate, maxChunkSize);
    }

    private sealed class SpdyFrameDecoder_ : SpdyFrameDecoder
    {
        private readonly SpdyFrameCodec codec;

        internal SpdyFrameDecoder_(SpdyFrameCodec codec, SpdyVersion version, ISpdyFrameDecoderDelegate @delegate,
                                   int maxChunkSize)
            : base(version, @delegate, maxChunkSize)
        {
            this.codec = codec;
        }

        protected override bool IsValidUnknownFrameHeader(int streamId, int type, byte flags, int length)
        {
            if (codec.supportsUnknownFrames)
            {
                return codec.IsValidUnknownFrameHeader(streamId, type, flags, length);
            }

            return base.IsValidUnknownFrameHeader(streamId, type, flags, length);
        }
    }

    protected virtual SpdyFrameEncoder CreateEncoder(SpdyVersion version)
    {
        return new SpdyFrameEncoder(version);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        base.HandlerAdded(ctx);
        this.ctx = ctx;
        _ = ctx.Channel.CloseFuture.ContinueWith(static (_, state) =>
        {
            (SpdyFrameCodec codec, IChannelHandlerContext context) = ((SpdyFrameCodec, IChannelHandlerContext)) state;

            if (context.Executor.InEventLoop())
            {
                codec.EndHeaderBlockCodecs();
            }
            else
            {
                context.Executor.Execute(codec.EndHeaderBlockCodecs);
            }
        }, (this, ctx), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CLR: the body of the closeFuture listener of handlerAdded.
    private void EndHeaderBlockCodecs()
    {
        spdyHeaderBlockDecoder.End();
        spdyHeaderBlockEncoder.End();
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        spdyFrameDecoder.Decode(@in);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        if (!read)
        {
            if (!ctx.Channel.Config.IsAutoRead())
            {
                ctx.Read();
            }
        }

        read = false;
        base.ChannelReadComplete(ctx);
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress,
                                  CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                        CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.CloseAsync(cancellationToken);
    }

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual void Read(IChannelHandlerContext ctx)
    {
        ctx.Read();
    }

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        ByteBuf frame;

        if (msg is ISpdyDataFrame)
        {

            ISpdyDataFrame spdyDataFrame = (ISpdyDataFrame) msg;

            try
            {
                frame = spdyFrameEncoder.EncodeDataFrame(
                    ctx.Alloc(),
                    spdyDataFrame.StreamId(),
                    spdyDataFrame.IsLast(),
                    spdyDataFrame.Content
                );
                return ctx.WriteAsync(frame, cancellationToken);
            }
            finally
            {
                spdyDataFrame.Release();
            }

        }
        else if (msg is ISpdySynStreamFrame)
        {

            ISpdySynStreamFrame spdySynStreamFrame = (ISpdySynStreamFrame) msg;
            ByteBuf headerBlock = spdyHeaderBlockEncoder.Encode(ctx.Alloc(), spdySynStreamFrame);

            try
            {
                frame = spdyFrameEncoder.EncodeSynStreamFrame(
                        ctx.Alloc(),
                        spdySynStreamFrame.StreamId(),
                        spdySynStreamFrame.AssociatedStreamId(),
                        spdySynStreamFrame.Priority(),
                        spdySynStreamFrame.IsLast(),
                        spdySynStreamFrame.IsUnidirectional(),
                        headerBlock
                );
            }
            finally
            {
                headerBlock.Release();
            }

            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdySynReplyFrame)
        {

            ISpdySynReplyFrame spdySynReplyFrame = (ISpdySynReplyFrame) msg;
            ByteBuf headerBlock = spdyHeaderBlockEncoder.Encode(ctx.Alloc(), spdySynReplyFrame);

            try
            {
                frame = spdyFrameEncoder.EncodeSynReplyFrame(
                        ctx.Alloc(),
                        spdySynReplyFrame.StreamId(),
                        spdySynReplyFrame.IsLast(),
                        headerBlock
                );
            }
            finally
            {
                headerBlock.Release();
            }

            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdyRstStreamFrame)
        {

            ISpdyRstStreamFrame spdyRstStreamFrame = (ISpdyRstStreamFrame) msg;
            frame = spdyFrameEncoder.EncodeRstStreamFrame(
                    ctx.Alloc(),
                    spdyRstStreamFrame.StreamId(),
                    spdyRstStreamFrame.Status().Code()
            );
            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdySettingsFrame)
        {

            ISpdySettingsFrame spdySettingsFrame = (ISpdySettingsFrame) msg;
            frame = spdyFrameEncoder.EncodeSettingsFrame(
                    ctx.Alloc(),
                    spdySettingsFrame
            );
            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdyPingFrame)
        {

            ISpdyPingFrame spdyPingFrame = (ISpdyPingFrame) msg;
            frame = spdyFrameEncoder.EncodePingFrame(
                    ctx.Alloc(),
                    spdyPingFrame.Id()
            );
            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdyGoAwayFrame)
        {

            ISpdyGoAwayFrame spdyGoAwayFrame = (ISpdyGoAwayFrame) msg;
            frame = spdyFrameEncoder.EncodeGoAwayFrame(
                    ctx.Alloc(),
                    spdyGoAwayFrame.LastGoodStreamId(),
                    spdyGoAwayFrame.Status().Code()
            );
            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdyHeadersFrame)
        {

            ISpdyHeadersFrame spdyHeadersFrame = (ISpdyHeadersFrame) msg;
            ByteBuf headerBlock = spdyHeaderBlockEncoder.Encode(ctx.Alloc(), spdyHeadersFrame);

            try
            {
                frame = spdyFrameEncoder.EncodeHeadersFrame(
                        ctx.Alloc(),
                        spdyHeadersFrame.StreamId(),
                        spdyHeadersFrame.IsLast(),
                        headerBlock
                );
            }
            finally
            {
                headerBlock.Release();
            }

            return ctx.WriteAsync(frame, cancellationToken);

        }
        else if (msg is ISpdyWindowUpdateFrame)
        {

            ISpdyWindowUpdateFrame spdyWindowUpdateFrame = (ISpdyWindowUpdateFrame) msg;
            frame = spdyFrameEncoder.EncodeWindowUpdateFrame(
                    ctx.Alloc(),
                    spdyWindowUpdateFrame.StreamId(),
                    spdyWindowUpdateFrame.DeltaWindowSize()
            );
            return ctx.WriteAsync(frame, cancellationToken);
        }
        else if (msg is ISpdyUnknownFrame)
        {
            ISpdyUnknownFrame spdyUnknownFrame = (ISpdyUnknownFrame) msg;

            try
            {
                frame = spdyFrameEncoder.EncodeUnknownFrame(
                        ctx.Alloc(),
                        spdyUnknownFrame.FrameType(),
                        spdyUnknownFrame.Flags(),
                        spdyUnknownFrame.Content);
                return ctx.WriteAsync(frame, cancellationToken);
            }
            finally
            {
                spdyUnknownFrame.Release();
            }
        }
        else
        {
            throw new UnsupportedMessageTypeException(msg);
        }
    }

    public virtual void ReadDataFrame(int streamId, bool last, ByteBuf data)
    {
        read = true;

        ISpdyDataFrame spdyDataFrame = new DefaultSpdyDataFrame(streamId, data);
        spdyDataFrame.SetLast(last);
        ctx.FireChannelRead(spdyDataFrame);
    }

    public virtual void ReadSynStreamFrame(
            int streamId, int associatedToStreamId, byte priority, bool last, bool unidirectional)
    {
        ISpdySynStreamFrame spdySynStreamFrame =
                new DefaultSpdySynStreamFrame(streamId, associatedToStreamId, priority, validateHeaders);
        spdySynStreamFrame.SetLast(last);
        spdySynStreamFrame.SetUnidirectional(unidirectional);
        spdyHeadersFrame = spdySynStreamFrame;
    }

    public virtual void ReadSynReplyFrame(int streamId, bool last)
    {
        ISpdySynReplyFrame spdySynReplyFrame = new DefaultSpdySynReplyFrame(streamId, validateHeaders);
        spdySynReplyFrame.SetLast(last);
        spdyHeadersFrame = spdySynReplyFrame;
    }

    public virtual void ReadRstStreamFrame(int streamId, int statusCode)
    {
        read = true;

        ISpdyRstStreamFrame spdyRstStreamFrame = new DefaultSpdyRstStreamFrame(streamId, statusCode);
        ctx.FireChannelRead(spdyRstStreamFrame);
    }

    public virtual void ReadSettingsFrame(bool clearPersisted)
    {
        read = true;

        spdySettingsFrame = new DefaultSpdySettingsFrame();
        spdySettingsFrame.SetClearPreviouslyPersistedSettings(clearPersisted);
    }

    public virtual void ReadSetting(int id, int value, bool persistValue, bool persisted)
    {
        spdySettingsFrame.SetValue(id, value, persistValue, persisted);
    }

    public virtual void ReadSettingsEnd()
    {
        read = true;

        object frame = spdySettingsFrame;
        spdySettingsFrame = null;
        ctx.FireChannelRead(frame);
    }

    public virtual void ReadPingFrame(int id)
    {
        read = true;

        ISpdyPingFrame spdyPingFrame = new DefaultSpdyPingFrame(id);
        ctx.FireChannelRead(spdyPingFrame);
    }

    public virtual void ReadGoAwayFrame(int lastGoodStreamId, int statusCode)
    {
        read = true;

        ISpdyGoAwayFrame spdyGoAwayFrame = new DefaultSpdyGoAwayFrame(lastGoodStreamId, statusCode);
        ctx.FireChannelRead(spdyGoAwayFrame);
    }

    public virtual void ReadHeadersFrame(int streamId, bool last)
    {
        spdyHeadersFrame = new DefaultSpdyHeadersFrame(streamId, validateHeaders);
        spdyHeadersFrame.SetLast(last);
    }

    public virtual void ReadWindowUpdateFrame(int streamId, int deltaWindowSize)
    {
        read = true;

        ISpdyWindowUpdateFrame spdyWindowUpdateFrame = new DefaultSpdyWindowUpdateFrame(streamId, deltaWindowSize);
        ctx.FireChannelRead(spdyWindowUpdateFrame);
    }

    public virtual void ReadHeaderBlock(ByteBuf headerBlock)
    {
        try
        {
            spdyHeaderBlockDecoder.Decode(ctx.Alloc(), headerBlock, spdyHeadersFrame);
        }
        catch (Exception e)
        {
            ctx.FireExceptionCaught(e);
        }
        finally
        {
            headerBlock.Release();
        }
    }

    public virtual void ReadHeaderBlockEnd()
    {
        object frame = null;

        try
        {
            spdyHeaderBlockDecoder.EndHeaderBlock(spdyHeadersFrame);
            frame = spdyHeadersFrame;
            spdyHeadersFrame = null;
        }
        catch (Exception e)
        {
            ctx.FireExceptionCaught(e);
        }

        if (frame != null)
        {
            read = true;

            ctx.FireChannelRead(frame);
        }
    }

    public virtual void ReadUnknownFrame(int frameType, byte flags, ByteBuf payload)
    {
        read = true;
        ctx.FireChannelRead(NewSpdyUnknownFrame(frameType, flags, payload));
    }

    /**
     * Create a SpdyUnknownFrame.
     * */
    protected virtual ISpdyFrame NewSpdyUnknownFrame(int frameType, byte flags, ByteBuf payload)
    {
        return new DefaultSpdyUnknownFrame(frameType, flags, payload);
    }

    /**
     * Check whether the unknown frame is valid, if not, the frame will be discarded,
     * otherwise, the frame will be passed to {@link SpdyFrameDecoder#decodeUnknownFrame(int, byte, int, ByteBuf)}.
     * <p>
     * By default this method always returns {@code false}, sub-classes may override this.
     **/
    protected virtual bool IsValidUnknownFrameHeader(int streamId,
                                                     int type,
                                                     byte flags,
                                                     int length)
    {
        return false;
    }

    public virtual void ReadFrameError(string message)
    {
        ctx.FireExceptionCaught(INVALID_FRAME);
    }
}
