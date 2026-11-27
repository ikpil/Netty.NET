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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.IHttp3RequestStreamCodecState;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Abstract base class that users can extend to init HTTP/3 push-streams for servers. This initializer
 * will automatically add HTTP/3 codecs etc to the {@link ChannelPipeline} as well.
 */
// CLR: ChannelInitializer<QuicStreamChannel> is Transport's ChannelInitializer<IQuicStreamChannel>; the protected final
// override of initChannel is a protected sealed override. initPushStream is protected internal abstract, because
// Http3ServerPushStreamManager calls it on another initializer from the package (C# protected access needs an instance
// of the calling class, CS1540); a subclass in another assembly overrides it as protected. ch.parent() is the Parent
// property cast to IQuicChannel (QuicStreamChannel.parent() narrows the return type, which the C# interface inherits
// unchanged; docs/codec-classes-quic-porting.md). ch.write(buffer) is WriteAsync, whose Task is not awaited.
// IllegalArgumentException is ArgumentException; the stream id prints with the invariant culture.
public abstract class Http3PushStreamServerInitializer : ChannelInitializer<IQuicStreamChannel>
{

    private readonly long pushId;

    protected Http3PushStreamServerInitializer(long pushId)
    {
        this.pushId = CheckPositiveOrZero(pushId, "pushId");
    }

    protected sealed override void InitChannel(IQuicStreamChannel ch)
    {
        if (!IsServerInitiatedQuicStream(ch))
        {
            throw new ArgumentException("Using server push stream initializer for client stream: " +
                    ch.StreamId().ToString(CultureInfo.InvariantCulture));
        }
        Http3CodecUtils.VerifyIsUnidirectional(ch);

        // We need to write stream type into the stream before doing anything else.
        // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.1
        // Just allocate 16 bytes which would be the max needed to write 2 variable length ints.
        ByteBuf buffer = ch.Alloc().Buffer(16);
        WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_PUSH_STREAM_TYPE);
        WriteVariableLengthInteger(buffer, pushId);
        _ = ch.WriteAsync(buffer);

        Http3ConnectionHandler connectionHandler =
                Http3CodecUtils.GetConnectionHandlerOrClose((IQuicChannel) ch.Parent);
        if (connectionHandler == null)
        {
            // connection should have been closed
            return;
        }

        IChannelPipeline pipeline = ch.Pipeline;
        Http3RequestStreamEncodeStateValidator encodeStateValidator = new Http3RequestStreamEncodeStateValidator();
        // Add the encoder and decoder in the pipeline so we can handle Http3Frames
        pipeline.AddLast(connectionHandler.NewCodec(encodeStateValidator, NO_STATE));
        pipeline.AddLast(encodeStateValidator);
        // Add the handler that will validate what we write and receive on this stream.
        pipeline.AddLast(connectionHandler.NewPushStreamValidationHandler(ch, NO_STATE));
        InitPushStream(ch);
    }

    /**
     * Initialize the {@link QuicStreamChannel} to handle {@link Http3PushStreamFrame}s. At the point of calling this
     * method it is already valid to write {@link Http3PushStreamFrame}s as the codec is already in the pipeline.
     *
     * @param ch the {QuicStreamChannel} for the push stream.
     */
    protected internal abstract void InitPushStream(IQuicStreamChannel ch);
}
