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
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: ChannelHandler is IChannelHandler, QuicStreamChannel codec-quic's IQuicStreamChannel,
// LongFunction<ChannelHandler> Func<long, IChannelHandler>, Http3SettingsFrame IHttp3SettingsFrame and
// QpackSensitivityDetector IQpackSensitivityDetector. The protected overrides of the package-private abstract methods
// are protected internal overrides of Http3ConnectionHandler's protected internal members (an override keeps the
// accessibility). The suppliers () -> new QpackEncoderHandler(...) and () -> new QpackDecoderHandler(...) are
// Func<IChannelHandler> lambdas. The @Nullable (org.jetbrains.annotations) annotations have no counterpart.
public class Http3ClientConnectionHandler : Http3ConnectionHandler
{

    private readonly Func<long, IChannelHandler> pushStreamHandlerFactory;

    /**
     * Create a new instance.
     */
    public Http3ClientConnectionHandler() : this(null, null, null, null, true)
    {
    }

    /**
     * Create a new instance.
     *
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param pushStreamHandlerFactory              the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for push streams {@code null} if no special
     *                                              handling should be done. When present, push ID will be passed as an
     *                                              argument to the {@link LongFunction}.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     */
    public Http3ClientConnectionHandler(IChannelHandler inboundControlStreamHandler,
                                        Func<long, IChannelHandler> pushStreamHandlerFactory,
                                        Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                        IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable)
        : this(inboundControlStreamHandler, pushStreamHandlerFactory, unknownInboundStreamHandlerFactory, localSettings,
                disableQpackDynamicTable, null)
    {
    }

    /**
     * Create a new instance.
     *
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param pushStreamHandlerFactory              the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for push streams {@code null} if no special
     *                                              handling should be done. When present, push ID will be passed as an
     *                                              argument to the {@link LongFunction}.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     * @param nonStandardSettingsValidator          the {@link Http3Settings.NonStandardHttp3SettingsValidator} to use
     *                                              when validating settings that are non-standard.
     */
    public Http3ClientConnectionHandler(IChannelHandler inboundControlStreamHandler,
                                        Func<long, IChannelHandler> pushStreamHandlerFactory,
                                        Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                        IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable,
                                        Http3Settings.INonStandardHttp3SettingsValidator
                                                nonStandardSettingsValidator)
        : this(inboundControlStreamHandler, pushStreamHandlerFactory, unknownInboundStreamHandlerFactory,
                localSettings, disableQpackDynamicTable, nonStandardSettingsValidator, null,
                Http3CodecUtils.DEFAULT_MAX_UNKNOWN_FRAME_PAYLOAD_LENGTH)
    {
    }

    /**
     * Create a new instance.
     *
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param pushStreamHandlerFactory              the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for push streams {@code null} if no special
     *                                              handling should be done. When present, push ID will be passed as an
     *                                              argument to the {@link LongFunction}.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     * @param nonStandardSettingsValidator          the {@link Http3Settings.NonStandardHttp3SettingsValidator} to use
     *                                              when validating settings that are non-standard.
     * @param sensitivityDetector                   detector that marks sensitive headers for QPACK Never Indexed
     *                                              encoding, or {@code null} to use the historical default.
     * @param maxUnknownFramePayloadLength          the maximum payload size of an unknown frame.
     */
    public Http3ClientConnectionHandler(IChannelHandler inboundControlStreamHandler,
                                        Func<long, IChannelHandler> pushStreamHandlerFactory,
                                        Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                        IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable,
                                        Http3Settings.INonStandardHttp3SettingsValidator
                                                nonStandardSettingsValidator,
                                        IQpackSensitivityDetector sensitivityDetector,
                                        int maxUnknownFramePayloadLength)
        : base(false, inboundControlStreamHandler, unknownInboundStreamHandlerFactory, localSettings,
                disableQpackDynamicTable, nonStandardSettingsValidator, sensitivityDetector,
                maxUnknownFramePayloadLength)
    {
        this.pushStreamHandlerFactory = pushStreamHandlerFactory;
    }

    protected internal override void InitBidirectionalStream(IChannelHandlerContext ctx, IQuicStreamChannel channel)
    {
        // See https://datatracker.ietf.org/doc/html/rfc9114#name-bidirectional-streams
        // https://datatracker.ietf.org/doc/html/rfc9114#section-6.1-3
        Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_STREAM_CREATION_ERROR,
                "Server initiated bidirectional streams are not allowed", true);
    }

    protected internal override void InitUnidirectionalStream(IChannelHandlerContext ctx,
                                                              IQuicStreamChannel streamChannel)
    {
        long maxTableCapacity = MaxTableCapacity();
        streamChannel.Pipeline.AddLast(
                new Http3UnidirectionalStreamInboundClientHandler(codecFactory, nonStandardSettingsValidator,
                        localControlStreamHandler, remoteControlStreamHandler,
                        unknownInboundStreamHandlerFactory, pushStreamHandlerFactory,
                        () => new QpackEncoderHandler(maxTableCapacity, qpackDecoder),
                        () => new QpackDecoderHandler(qpackEncoder)));
    }
}
