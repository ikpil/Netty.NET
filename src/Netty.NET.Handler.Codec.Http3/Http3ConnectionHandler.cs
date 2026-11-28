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
using System.Diagnostics;
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.IHttp3RequestStreamCodecState;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Handler that handles <a href="https://datatracker.ietf.org/doc/html/rfc9114">HTTP3</a> connections.
 */
// CLR: QuicChannel, QuicStreamChannel and QuicStreamType are codec-quic's IQuicChannel, IQuicStreamChannel and the C#
// enum QuicStreamType (docs/codec-classes-quic-porting.md, API shapes); ChannelHandler is IChannelHandler. The
// package-private fields and members are internal (Http3, the initializers and the subclasses reach them); the
// package-private abstract initBidirectionalStream/initUnidirectionalStream are protected internal, because
// Http3ClientConnectionHandler widens them to protected and an override cannot change the accessibility. LongFunction
// <ChannelHandler> is Func<long, IChannelHandler>; the lambda (id, value) -> false is the private nested class
// IgnoreNonStandardSettings and localControlStreamHandler::isGoAwayReceived the method group (Func<bool>). Long
// maxFieldSectionSize is a long (getOrDefault returns a long); toIntExact is a checked conversion (OverflowException
// where Java throws ArithmeticException). createStream(...).addListener(f -> ...) is Http3CodecUtils.AddListener over
// the Task<IQuicStreamChannel> of CreateStreamAsync; f.cause() is Http3CodecUtils.Cause and f.getNow() the Task's
// Result. ctx.close() is CloseAsync, whose Task is not awaited. java.lang.Error is InvalidOperationException; assert is
// Debug.Assert. isSharable() overrides the IsSharable property. The @Nullable (org.jetbrains.annotations) annotations
// have no counterpart.
public abstract class Http3ConnectionHandler : ChannelInboundHandlerAdapter
{

    internal readonly Http3FrameCodec.IHttp3FrameCodecFactory codecFactory;
    internal readonly Func<long, IChannelHandler> unknownInboundStreamHandlerFactory;
    internal readonly bool disableQpackDynamicTable;
    internal readonly Http3ControlStreamInboundHandler localControlStreamHandler;
    internal readonly Http3ControlStreamOutboundHandler remoteControlStreamHandler;
    internal readonly QpackDecoder qpackDecoder;
    internal readonly QpackEncoder qpackEncoder;
    internal readonly Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator;
    private bool controlStreamCreationInProgress;

    internal readonly long maxTableCapacity;

    /**
     * Create a new instance.
     * @param server                                {@code true} if server-side, {@code false} otherwise.
     * @param inboundControlStreamHandler           the {@link ChannelHandler} which will be notified about
     *                                              {@link Http3RequestStreamFrame}s or {@code null} if the user is not
     *                                              interested in these.
     * @param unknownInboundStreamHandlerFactory    the {@link LongFunction} that will provide a custom
     *                                              {@link ChannelHandler} for unknown inbound stream types or
     *                                              {@code null} if no special handling should be done.
     * @param localSettings                         the local {@link Http3SettingsFrame} that should be sent to the
     *                                              remote peer or {@code null} if the default settings should be used.
     * @param disableQpackDynamicTable              If QPACK dynamic table should be disabled.
     * @param maxUnknownFramePayloadLength          the maximum payload size of an unknown frame.
     */
    internal Http3ConnectionHandler(bool server, IChannelHandler inboundControlStreamHandler,
                                    Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                    IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable,
                                    Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator,
                                    int maxUnknownFramePayloadLength)
        : this(server, inboundControlStreamHandler, unknownInboundStreamHandlerFactory, localSettings,
                disableQpackDynamicTable, nonStandardSettingsValidator, null, maxUnknownFramePayloadLength)
    {
    }

    internal Http3ConnectionHandler(bool server, IChannelHandler inboundControlStreamHandler,
                                    Func<long, IChannelHandler> unknownInboundStreamHandlerFactory,
                                    IHttp3SettingsFrame localSettings, bool disableQpackDynamicTable,
                                    Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator,
                                    IQpackSensitivityDetector sensitivityDetector,
                                    int maxUnknownFramePayloadLength)
    {
        this.unknownInboundStreamHandlerFactory = unknownInboundStreamHandlerFactory;
        this.disableQpackDynamicTable = disableQpackDynamicTable;

        if (nonStandardSettingsValidator != null)
        {
            this.nonStandardSettingsValidator = nonStandardSettingsValidator;
        }
        else
        {
            this.nonStandardSettingsValidator = new IgnoreNonStandardSettings();
        }

        if (localSettings == null)
        {
            localSettings = new DefaultHttp3SettingsFrame(Http3Settings.DefaultSettings());
        }
        else
        {
            localSettings = DefaultHttp3SettingsFrame.CopyOf(localSettings);
        }

        long maxFieldSectionSize = localSettings
                .Settings()
                .GetOrDefault(
                        Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(),
                        Http3CodecUtils.DEFAULT_MAX_FIELD_SECTION_SIZE
                );
        this.maxTableCapacity = localSettings
                .Settings()
                .GetOrDefault(
                        Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(),
                        0L
                );
        int maxBlockedStreams = checked((int) localSettings
                .Settings()
                .GetOrDefault(
                        Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(),
                        0L)
        );
        qpackDecoder = new QpackDecoder(maxTableCapacity, maxBlockedStreams);
        qpackEncoder = new QpackEncoder(sensitivityDetector);
        codecFactory = Http3FrameCodec.NewFactory(
                qpackDecoder, maxFieldSectionSize, maxUnknownFramePayloadLength, qpackEncoder);
        remoteControlStreamHandler =  new Http3ControlStreamOutboundHandler(server, localSettings,
                codecFactory.NewCodec(IHttp3FrameTypeValidator.NO_VALIDATION, NO_STATE, NO_STATE,
                        this.nonStandardSettingsValidator));
        localControlStreamHandler = new Http3ControlStreamInboundHandler(server, inboundControlStreamHandler,
                qpackEncoder, remoteControlStreamHandler);
    }

    private void CreateControlStreamIfNeeded(IChannelHandlerContext ctx)
    {
        if (!controlStreamCreationInProgress && Http3.GetLocalControlStream(ctx.Channel) == null)
        {
            controlStreamCreationInProgress = true;
            IQuicChannel channel = (IQuicChannel) ctx.Channel;
            // Once the channel became active we need to create a unidirectional stream and write the
            // Http3SettingsFrame to it. This needs to be the first frame on this stream.
            // https://datatracker.ietf.org/doc/html/rfc9114#name-control-streams
            Task<IQuicStreamChannel> future =
                    channel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL, remoteControlStreamHandler);
            Http3CodecUtils.AddListener(ctx, future, f =>
            {
                if (!f.IsCompletedSuccessfully)
                {
                    ctx.FireExceptionCaught(new Http3Exception(Http3ErrorCode.H3_STREAM_CREATION_ERROR,
                            "Unable to open control stream", Http3CodecUtils.Cause(f)));
                    _ = ctx.CloseAsync();
                }
                else
                {
                    Http3.SetLocalControlStream(channel, future.Result);
                }
            });
        }
    }

    /**
     * Returns {@code true} if we received a GOAWAY frame from the remote peer.
     * @return {@code true} if we received the frame, {@code false} otherwise.
     */
    public bool IsGoAwayReceived()
    {
        return localControlStreamHandler.IsGoAwayReceived();
    }

    /**
     * Returns a new codec that will encode and decode {@link Http3Frame}s for this HTTP/3 connection.
     *
     * @return a new codec.
     */
    internal IChannelHandler NewCodec(IHttp3RequestStreamCodecState encodeState,
                                      IHttp3RequestStreamCodecState decodeState)
    {
        return codecFactory.NewCodec(
                Http3RequestStreamFrameTypeValidator.INSTANCE, encodeState, decodeState, nonStandardSettingsValidator);
    }

    internal IChannelHandler NewRequestStreamValidationHandler(
            IQuicStreamChannel forStream, IHttp3RequestStreamCodecState encodeState,
            IHttp3RequestStreamCodecState decodeState)
    {
        QpackAttributes qpackAttributes = Http3.GetQpackAttributes(forStream.Parent);
        Debug.Assert(qpackAttributes != null);
        if (localControlStreamHandler.IsServer())
        {
            return Http3RequestStreamValidationHandler.NewServerValidator(qpackAttributes, qpackDecoder,
                    encodeState, decodeState);
        }

        return Http3RequestStreamValidationHandler.NewClientValidator(localControlStreamHandler.IsGoAwayReceived,
                qpackAttributes, qpackDecoder, encodeState, decodeState);
    }

    internal IChannelHandler NewPushStreamValidationHandler(IQuicStreamChannel forStream,
                                                            IHttp3RequestStreamCodecState decodeState)
    {
        if (localControlStreamHandler.IsServer())
        {
            return Http3PushStreamServerValidationHandler.INSTANCE;
        }

        QpackAttributes qpackAttributes = Http3.GetQpackAttributes(forStream.Parent);
        Debug.Assert(qpackAttributes != null);

        return new Http3PushStreamClientValidationHandler(qpackAttributes, qpackDecoder, decodeState);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        IQuicChannel channel = (IQuicChannel) ctx.Channel;
        Http3.SetQpackAttributes(channel, new QpackAttributes(channel, disableQpackDynamicTable));

        if (ctx.Channel.IsActive)
        {
            CreateControlStreamIfNeeded(ctx);
        }
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        CreateControlStreamIfNeeded(ctx);

        ctx.FireChannelActive();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IQuicStreamChannel)
        {
            IQuicStreamChannel channel = (IQuicStreamChannel) msg;

            switch (channel.Type())
            {
                case QuicStreamType.BIDIRECTIONAL:
                    InitBidirectionalStream(ctx, channel);
                    break;
                case QuicStreamType.UNIDIRECTIONAL:
                    InitUnidirectionalStream(ctx, channel);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected channel type: " + channel.Type());
            }
        }

        ctx.FireChannelRead(msg);
    }

    /**
     * Called when an bidirectional stream is opened from the remote-peer.
     *
     * @param ctx           the {@link ChannelHandlerContext} of the parent {@link QuicChannel}.
     * @param streamChannel the {@link QuicStreamChannel}.
     */
    protected internal abstract void InitBidirectionalStream(IChannelHandlerContext ctx,
                                                             IQuicStreamChannel streamChannel);

    /**
     * Called when an unidirectional stream is opened from the remote-peer.
     *
     * @param ctx           the {@link ChannelHandlerContext} of the parent {@link QuicChannel}.
     * @param streamChannel the {@link QuicStreamChannel}.
     */
    protected internal abstract void InitUnidirectionalStream(IChannelHandlerContext ctx,
                                                              IQuicStreamChannel streamChannel);

    internal long MaxTableCapacity()
    {
        return maxTableCapacity;
    }

    /**
     * Always returns {@code false} as it keeps state.
     */
    public override bool IsSharable => false;

    // CLR: the lambda (id, value) -> false of the constructor.
    private sealed class IgnoreNonStandardSettings : Http3Settings.INonStandardHttp3SettingsValidator
    {
        public bool Validate(long id, long value)
        {
            return false;
        }
    }
}
