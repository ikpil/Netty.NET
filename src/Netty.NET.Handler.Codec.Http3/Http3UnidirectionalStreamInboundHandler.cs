/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.IHttp3RequestStreamCodecState;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * {@link ByteToMessageDecoder} which helps to detect the type of unidirectional stream.
 */
// CLR: the package-private abstract class is internal; ByteToMessageDecoder is codec-base's ByteToMessageDecoder, whose
// decode(ctx, in, List<Object> out) is Decode(ctx, ByteBuf @in, IList<object> @out) (docs/codec-base-porting.md).
// AttributeKey<Boolean> is AttributeKey<object> holding a boxed bool (AttributeKey<T> needs a reference type; as
// handler-ssl-ocsp and resolver-dns), so setIfAbsent(true) stores the boxed true. Http3FrameCodecFactory is the
// nested Http3FrameCodec.IHttp3FrameCodecFactory and NonStandardHttp3SettingsValidator
// Http3Settings.INonStandardHttp3SettingsValidator. Supplier<ChannelHandler> is Func<IChannelHandler>,
// LongFunction<ChannelHandler> Func<long, IChannelHandler> (get()/apply(x) are delegate invocations) and the lambda
// type -> ReleaseHandler.INSTANCE a C# lambda. in.getByte(in.readerIndex()) is the CLR byte that selects the byte
// overload of numBytesForVariableLengthInteger. pipeline().replace(this, null, handler) is Replace(this, null,
// handler). The package-private nested ReleaseHandler is an internal sealed nested class whose isSharable() overrides
// the IsSharable property. The @Nullable (org.jetbrains.annotations) annotations have no counterpart.
internal abstract class Http3UnidirectionalStreamInboundHandler : ByteToMessageDecoder
{
    private static readonly AttributeKey<object> REMOTE_CONTROL_STREAM =
            AttributeKey<object>.ValueOf("H3_REMOTE_CONTROL_STREAM");
    private static readonly AttributeKey<object> REMOTE_QPACK_DECODER_STREAM =
            AttributeKey<object>.ValueOf("H3_REMOTE_QPACK_DECODER_STREAM");
    private static readonly AttributeKey<object> REMOTE_QPACK_ENCODER_STREAM =
            AttributeKey<object>.ValueOf("H3_REMOTE_QPACK_ENCODER_STREAM");

    internal readonly Http3FrameCodec.IHttp3FrameCodecFactory codecFactory;
    internal readonly Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator;
    internal readonly Http3ControlStreamInboundHandler localControlStreamHandler;
    internal readonly Http3ControlStreamOutboundHandler remoteControlStreamHandler;
    internal readonly Func<IChannelHandler> qpackEncoderHandlerFactory;
    internal readonly Func<IChannelHandler> qpackDecoderHandlerFactory;
    internal readonly Func<long, IChannelHandler> unknownStreamHandlerFactory;

    internal Http3UnidirectionalStreamInboundHandler(
            Http3FrameCodec.IHttp3FrameCodecFactory codecFactory,
            Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator,
            Http3ControlStreamInboundHandler localControlStreamHandler,
            Http3ControlStreamOutboundHandler remoteControlStreamHandler,
            Func<long, IChannelHandler> unknownStreamHandlerFactory,
            Func<IChannelHandler> qpackEncoderHandlerFactory,
            Func<IChannelHandler> qpackDecoderHandlerFactory)
    {
        this.codecFactory = codecFactory;
        this.nonStandardSettingsValidator = nonStandardSettingsValidator;
        this.localControlStreamHandler = localControlStreamHandler;
        this.remoteControlStreamHandler = remoteControlStreamHandler;
        this.qpackEncoderHandlerFactory = qpackEncoderHandlerFactory;
        this.qpackDecoderHandlerFactory = qpackDecoderHandlerFactory;
        if (unknownStreamHandlerFactory == null)
        {
            // If the user did not specify an own factory just drop all bytes on the floor.
            unknownStreamHandlerFactory = type => ReleaseHandler.INSTANCE;
        }
        this.unknownStreamHandlerFactory = unknownStreamHandlerFactory;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (!@in.IsReadable())
        {
            return;
        }
        int len = Http3CodecUtils.NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
        if (@in.ReadableBytes < len)
        {
            return;
        }
        long type = Http3CodecUtils.ReadVariableLengthInteger(@in, len);
        switch ((int) type)
        {
            case HTTP3_CONTROL_STREAM_TYPE:
                InitControlStream(ctx);
                break;
            case HTTP3_PUSH_STREAM_TYPE:
                int pushIdLen = Http3CodecUtils.NumBytesForVariableLengthInteger(@in.GetByte(@in.ReaderIndex));
                if (@in.ReadableBytes < pushIdLen)
                {
                    return;
                }
                long pushId = Http3CodecUtils.ReadVariableLengthInteger(@in, pushIdLen);
                InitPushStream(ctx, pushId);
                break;
            case HTTP3_QPACK_ENCODER_STREAM_TYPE:
                // See https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#enc-dec-stream-def
                InitQpackEncoderStream(ctx);
                break;
            case HTTP3_QPACK_DECODER_STREAM_TYPE:
                // See https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#enc-dec-stream-def
                InitQpackDecoderStream(ctx);
                break;
            default:
                InitUnknownStream(ctx, type);
                break;
        }
    }

    /**
     * Called if the current {@link Channel} is a
     * <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.1">control stream</a>.
     */
    private void InitControlStream(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.Parent.Attr(REMOTE_CONTROL_STREAM).SetIfAbsent(true) == null)
        {
            ctx.Pipeline.AddLast(localControlStreamHandler);
            // Replace this handler with the codec now.
            ctx.Pipeline.Replace(this, null,
                    codecFactory.NewCodec(Http3ControlStreamFrameTypeValidator.INSTANCE, NO_STATE,
                            NO_STATE, nonStandardSettingsValidator));
        }
        else
        {
            // Only one control stream is allowed.
            // See https://quicwg.org/base-drafts/draft-ietf-quic-http.html#section-6.2.1
            Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_STREAM_CREATION_ERROR,
                    "Received multiple control streams.", false);
        }
    }

    private bool EnsureStreamNotExistsYet(IChannelHandlerContext ctx, AttributeKey<object> key)
    {
        return ctx.Channel.Parent.Attr(key).SetIfAbsent(true) == null;
    }

    /**
     * Called if the current {@link Channel} is a
     * <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.2">push stream</a>.
     */
    internal abstract void InitPushStream(IChannelHandlerContext ctx, long id);

    /**
     * Called if the current {@link Channel} is a
     * <a href="https://www.ietf.org/archive/id/draft-ietf-quic-qpack-19.html#name-encoder-and-decoder-streams">
     *     QPACK encoder stream</a>.
     */
    private void InitQpackEncoderStream(IChannelHandlerContext ctx)
    {
        if (EnsureStreamNotExistsYet(ctx, REMOTE_QPACK_ENCODER_STREAM))
        {
            // Just drop stuff on the floor as we dont support dynamic table atm.
            ctx.Pipeline.Replace(this, null, qpackEncoderHandlerFactory());
        }
        else
        {
            // Only one stream is allowed.
            // See https://www.ietf.org/archive/id/draft-ietf-quic-qpack-19.html#section-4.2
            Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_STREAM_CREATION_ERROR,
                    "Received multiple QPACK encoder streams.", false);
        }
    }
    /**
     * Called if the current {@link Channel} is a
     * <a href="https://www.ietf.org/archive/id/draft-ietf-quic-qpack-19.html#name-encoder-and-decoder-streams">
     *     QPACK decoder stream</a>.
     */
    private void InitQpackDecoderStream(IChannelHandlerContext ctx)
    {
        if (EnsureStreamNotExistsYet(ctx, REMOTE_QPACK_DECODER_STREAM))
        {
            ctx.Pipeline.Replace(this, null, qpackDecoderHandlerFactory());
        }
        else
        {
            // Only one stream is allowed.
            // See https://www.ietf.org/archive/id/draft-ietf-quic-qpack-19.html#section-4.2
            Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_STREAM_CREATION_ERROR,
                    "Received multiple QPACK decoder streams.", false);
        }
    }

    /**
     * Called if we couldn't detect the stream type of the current {@link Channel}. Let's release everything that
     * we receive on this stream.
     */
    private void InitUnknownStream(IChannelHandlerContext ctx, long streamType)
    {
        ctx.Pipeline.Replace(this, null, unknownStreamHandlerFactory(streamType));
    }

    internal sealed class ReleaseHandler : ChannelInboundHandlerAdapter
    {
        internal static readonly ReleaseHandler INSTANCE = new ReleaseHandler();

        public override bool IsSharable => true;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
        }
    }
}
