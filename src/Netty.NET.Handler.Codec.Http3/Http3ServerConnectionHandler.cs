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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Handler that handles <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32">HTTP3</a> for the server-side.
 */
// CLR: ChannelHandler is IChannelHandler, ChannelPipeline IChannelPipeline, QuicStreamChannel codec-quic's
// IQuicStreamChannel, LongFunction<ChannelHandler> Func<long, IChannelHandler>, Http3SettingsFrame IHttp3SettingsFrame
// and QpackSensitivityDetector IQpackSensitivityDetector. The overrides of the package-private abstract methods are
// protected internal overrides of Http3ConnectionHandler's protected internal members (an override keeps the
// accessibility). The suppliers () -> new QpackEncoderHandler(...) and () -> new QpackDecoderHandler(...) are
// Func<IChannelHandler> lambdas. The @Nullable (org.jetbrains.annotations) annotations have no counterpart.
public sealed class Http3ServerConnectionHandler : Http3ConnectionHandler
{
    private readonly IChannelHandler requestStreamHandler;

    /**
     * Create a new instance.
     *
     * @param requestStreamHandler  the {@link ChannelHandler} that is used for each new request stream.
     *                              This handler will receive {@link Http3HeadersFrame} and {@link Http3DataFrame}s.
     */
    public Http3ServerConnectionHandler(IChannelHandler requestStreamHandler)
        : this(requestStreamHandler, null, null, null, true)
    {
    }

    /**
     * Create a new instance.
     * @param requestStreamHandler                  the {@link ChannelHandler} that is used for each new request stream.
     *                                              This handler will receive {@link Http3HeadersFrame} and
     *                                              {@link Http3DataFrame}s.
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     */
    public Http3ServerConnectionHandler(IChannelHandler requestStreamHandler,
                                        IChannelHandler inboundControlStreamHandler,
                                        Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                        IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable)
        : this(requestStreamHandler, inboundControlStreamHandler, unknownInboundStreamHandlerFactory,
                localSettings, disableQpackDynamicTable, null)
    {
    }

    /**
     * Create a new instance.
     * @param requestStreamHandler                  the {@link ChannelHandler} that is used for each new request stream.
     *                                              This handler will receive {@link Http3HeadersFrame} and
     *                                              {@link Http3DataFrame}s.
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     * @param nonStandardSettingsValidator          the {@link Http3Settings.NonStandardHttp3SettingsValidator} to
     *                                              use when validating settings that are non-standard.
     */
    public Http3ServerConnectionHandler(IChannelHandler requestStreamHandler,
                                        IChannelHandler inboundControlStreamHandler,
                                        Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                        IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable,
                                        Http3Settings.INonStandardHttp3SettingsValidator
                                                nonStandardSettingsValidator)
        : this(requestStreamHandler, inboundControlStreamHandler, unknownInboundStreamHandlerFactory,
                localSettings, disableQpackDynamicTable, nonStandardSettingsValidator, null,
                Http3CodecUtils.DEFAULT_MAX_UNKNOWN_FRAME_PAYLOAD_LENGTH)
    {
    }

    /**
     * Create a new instance.
     * @param requestStreamHandler                  the {@link ChannelHandler} that is used for each new request stream.
     *                                              This handler will receive {@link Http3HeadersFrame} and
     *                                              {@link Http3DataFrame}s.
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     * @param nonStandardSettingsValidator          the {@link Http3Settings.NonStandardHttp3SettingsValidator} to
     *                                              use when validating settings that are non-standard.
     * @param sensitivityDetector                   detector that marks sensitive headers for QPACK Never Indexed
     *                                              encoding, or {@code null} to use the historical default.
     * @param maxUnknownFramePayloadLength          the maximum payload size of an unknown frame.
     */
    public Http3ServerConnectionHandler(IChannelHandler requestStreamHandler,
                                        IChannelHandler inboundControlStreamHandler,
                                        Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                        IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable,
                                        Http3Settings.INonStandardHttp3SettingsValidator
                                                nonStandardSettingsValidator,
                                        IQpackSensitivityDetector sensitivityDetector,
                                        int maxUnknownFramePayloadLength)
        : base(true, inboundControlStreamHandler, unknownInboundStreamHandlerFactory, localSettings,
                disableQpackDynamicTable, nonStandardSettingsValidator, sensitivityDetector,
                maxUnknownFramePayloadLength)
    {
        this.requestStreamHandler = ObjectUtil.CheckNotNull(requestStreamHandler, "requestStreamHandler");
    }

    protected internal override void InitBidirectionalStream(IChannelHandlerContext ctx,
                                                             IQuicStreamChannel streamChannel)
    {
        IChannelPipeline pipeline = streamChannel.Pipeline;
        Http3RequestStreamEncodeStateValidator encodeStateValidator = new Http3RequestStreamEncodeStateValidator();
        Http3RequestStreamDecodeStateValidator decodeStateValidator = new Http3RequestStreamDecodeStateValidator();
        // Add the encoder and decoder in the pipeline so we can handle Http3Frames
        pipeline.AddLast(NewCodec(encodeStateValidator, decodeStateValidator));
        pipeline.AddLast(encodeStateValidator);
        pipeline.AddLast(decodeStateValidator);
        pipeline.AddLast(NewRequestStreamValidationHandler(streamChannel, encodeStateValidator, decodeStateValidator));
        pipeline.AddLast(requestStreamHandler);
    }

    protected internal override void InitUnidirectionalStream(IChannelHandlerContext ctx,
                                                              IQuicStreamChannel streamChannel)
    {
        long maxTableCapacity = MaxTableCapacity();
        streamChannel.Pipeline.AddLast(
                new Http3UnidirectionalStreamInboundServerHandler(codecFactory, nonStandardSettingsValidator,
                        localControlStreamHandler, remoteControlStreamHandler,
                        unknownInboundStreamHandlerFactory,
                        () => new QpackEncoderHandler(maxTableCapacity, qpackDecoder),
                        () => new QpackDecoderHandler(qpackEncoder)));
    }
}
