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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.Http3ErrorCode;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed; it derives from
// Http3FrameTypeInboundValidationHandler<IHttp3ControlStreamFrame>, whose Class<T> constructor argument is the Type
// typeof(IHttp3ControlStreamFrame), and overrides its internal virtual channelRead(ctx, T)/readFrameDiscarded.
// QuicChannel, QuicStreamChannel and QuicStreamType are codec-quic's IQuicChannel, IQuicStreamChannel and the C# enum
// QuicStreamType; ChannelHandler is IChannelHandler. The Long fields receivedGoawayId/receivedMaxPushId are long? (null
// until a frame was received). The GenericFutureListener closeOnFailure is an Action<Task> added with
// Http3CodecUtils.AddListener to the Task<IQuicStreamChannel> of CreateStreamAsync (createStream(...).addListener);
// closeOnFailure(ctx.writeAndFlush) is Http3CodecUtils.CloseOnFailure(ctx.Channel, ...).
// java.nio.channels.ClosedChannelException is Transport's ClosedChannelException (an IOException) and
// ThrowableUtil.unknownStackTrace Common's UnknownStackTrace. The private static nested classes are private nested
// classes; the public final channelActive/userEventTriggered of AbstractQPackStreamInitializer are sealed overrides.
// "throws QpackException"/"throws Exception" have no counterpart; assert is Debug.Assert; isSharable() overrides the
// IsSharable property. The @Nullable (org.jetbrains.annotations) annotations have no counterpart.
internal sealed class Http3ControlStreamInboundHandler
        : Http3FrameTypeInboundValidationHandler<IHttp3ControlStreamFrame>
{
    internal readonly bool server;
    private readonly IChannelHandler controlFrameHandler;
    private readonly QpackEncoder qpackEncoder;
    private readonly Http3ControlStreamOutboundHandler remoteControlStreamHandler;
    private bool firstFrameRead;
    private long? receivedGoawayId;
    private long? receivedMaxPushId;

    internal Http3ControlStreamInboundHandler(bool server, IChannelHandler controlFrameHandler,
                                              QpackEncoder qpackEncoder,
                                              Http3ControlStreamOutboundHandler remoteControlStreamHandler)
        : base(typeof(IHttp3ControlStreamFrame))
    {
        this.server = server;
        this.controlFrameHandler = controlFrameHandler;
        this.qpackEncoder = qpackEncoder;
        this.remoteControlStreamHandler = remoteControlStreamHandler;
    }

    internal bool IsServer()
    {
        return server;
    }

    internal bool IsGoAwayReceived()
    {
        return receivedGoawayId != null;
    }

    internal long MaxPushIdReceived()
    {
        return receivedMaxPushId == null ? -1 : receivedMaxPushId.Value;
    }

    private bool ForwardControlFrames()
    {
        return controlFrameHandler != null;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        base.HandlerAdded(ctx);

        // The user want's to be notified about control frames, add the handler to the pipeline.
        if (controlFrameHandler != null)
        {
            ctx.Pipeline.AddLast(controlFrameHandler);
        }
    }

    internal override void ReadFrameDiscarded(IChannelHandlerContext ctx, object discardedFrame)
    {
        if (!firstFrameRead && !(discardedFrame is IHttp3SettingsFrame))
        {
            ConnectionError(ctx, Http3ErrorCode.H3_MISSING_SETTINGS, "Missing settings frame.", ForwardControlFrames());
        }
    }

    internal override void ChannelRead(IChannelHandlerContext ctx, IHttp3ControlStreamFrame frame)
    {
        bool isSettingsFrame = frame is IHttp3SettingsFrame;
        if (!firstFrameRead && !isSettingsFrame)
        {
            ConnectionError(ctx, H3_MISSING_SETTINGS, "Missing settings frame.", ForwardControlFrames());
            ReferenceCountUtil.Release(frame);
            return;
        }

        if (firstFrameRead && isSettingsFrame)
        {
            ConnectionError(ctx, H3_FRAME_UNEXPECTED, "Second settings frame received.", ForwardControlFrames());
            ReferenceCountUtil.Release(frame);
            return;
        }

        firstFrameRead = true;

        bool valid;

        if (isSettingsFrame)
        {
            valid = HandleHttp3SettingsFrame(ctx, (IHttp3SettingsFrame) frame);
        }
        else if (frame is IHttp3GoAwayFrame)
        {
            valid = HandleHttp3GoAwayFrame(ctx, (IHttp3GoAwayFrame) frame);
        }
        else if (frame is IHttp3MaxPushIdFrame)
        {
            valid = HandleHttp3MaxPushIdFrame(ctx, (IHttp3MaxPushIdFrame) frame);
        }
        else if (frame is IHttp3CancelPushFrame)
        {
            valid = HandleHttp3CancelPushFrame(ctx, (IHttp3CancelPushFrame) frame);
        }
        else
        {
            // We don't need to do any special handling for Http3UnknownFrames as we either pass these to the next#
            // handler or release these directly.
            Debug.Assert(frame is IHttp3UnknownFrame);
            valid = true;
        }

        if (!valid || controlFrameHandler == null)
        {
            ReferenceCountUtil.Release(frame);
            return;
        }

        // The user did specify ChannelHandler that should be notified about control stream frames.
        // Let's forward the frame so the user can do something with it.
        ctx.FireChannelRead(frame);
    }

    private bool HandleHttp3SettingsFrame(IChannelHandlerContext ctx, IHttp3SettingsFrame settingsFrame)
    {
        IQuicChannel quicChannel = (IQuicChannel) ctx.Channel.Parent;
        QpackAttributes qpackAttributes = Http3.GetQpackAttributes(quicChannel);
        Debug.Assert(qpackAttributes != null);
        Action<Task> closeOnFailure = future =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                CriticalStreamClosed(ctx);
            }
        };

        if (qpackAttributes.DynamicTableDisabled())
        {
            qpackEncoder.ConfigureDynamicTable(qpackAttributes, 0, 0);
            return true;
        }

        AddListener(ctx, quicChannel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new QPackEncoderStreamInitializer(qpackEncoder, qpackAttributes,
                        settingsFrame
                                .Settings()
                                .GetOrDefault(
                                        Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(),
                                        0L
                                ),
                        settingsFrame
                                .Settings()
                                .GetOrDefault(
                                        Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(),
                                        0L
                                )
                        )
                ),
                closeOnFailure);
        AddListener(ctx, quicChannel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new QPackDecoderStreamInitializer(qpackAttributes)),
                closeOnFailure);
        return true;
    }

    private bool HandleHttp3GoAwayFrame(IChannelHandlerContext ctx, IHttp3GoAwayFrame goAwayFrame)
    {
        long id = goAwayFrame.Id();
        if (!server && id % 4 != 0)
        {
            ConnectionError(ctx, H3_FRAME_UNEXPECTED, "GOAWAY received with ID of non-request stream.",
                    ForwardControlFrames());
            return false;
        }

        if (receivedGoawayId != null && id > receivedGoawayId.Value)
        {
            ConnectionError(ctx, H3_ID_ERROR,
                    "GOAWAY received with ID larger than previously received.", ForwardControlFrames());
            return false;
        }

        receivedGoawayId = id;
        return true;
    }

    private bool HandleHttp3MaxPushIdFrame(IChannelHandlerContext ctx, IHttp3MaxPushIdFrame frame)
    {
        long id = frame.Id();

        if (!server)
        {
            ConnectionError(ctx, H3_FRAME_UNEXPECTED, "MAX_PUSH_ID received by client.",
                    ForwardControlFrames());
            return false;
        }

        if (receivedMaxPushId != null && id < receivedMaxPushId.Value)
        {
            ConnectionError(ctx, H3_ID_ERROR, "MAX_PUSH_ID reduced limit.", ForwardControlFrames());
            return false;
        }

        receivedMaxPushId = id;
        return true;
    }

    private bool HandleHttp3CancelPushFrame(IChannelHandlerContext ctx, IHttp3CancelPushFrame cancelPushFrame)
    {
        long? maxPushId = server ? receivedMaxPushId : remoteControlStreamHandler.SentMaxPushId();
        if (maxPushId == null || maxPushId.Value < cancelPushFrame.Id())
        {
            ConnectionError(ctx, H3_ID_ERROR, "CANCEL_PUSH received with an ID greater than MAX_PUSH_ID.",
                    ForwardControlFrames());
            return false;
        }

        return true;
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.FireChannelReadComplete();

        // control streams should always be processed, no matter what the user is doing in terms of
        // configuration and AUTO_READ.
        Http3CodecUtils.ReadIfNoAutoRead(ctx);
    }

    // Not sharable as it keeps state.
    public override bool IsSharable => false;

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is ChannelInputShutdownEvent)
        {
            // See https://www.ietf.org/archive/id/draft-ietf-quic-qpack-19.html#section-4.2
            CriticalStreamClosed(ctx);
        }

        ctx.FireUserEventTriggered(evt);
    }

    private abstract class AbstractQPackStreamInitializer : ChannelInboundHandlerAdapter
    {
        private readonly int streamType;
        protected readonly QpackAttributes attributes;

        internal AbstractQPackStreamInitializer(int streamType, QpackAttributes attributes)
        {
            this.streamType = streamType;
            this.attributes = attributes;
        }

        public sealed override void ChannelActive(IChannelHandlerContext ctx)
        {
            // We need to write the streamType into the stream before doing anything else.
            // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.1
            // Just allocate 8 bytes which would be the max needed.
            ByteBuf buffer = ctx.Alloc().Buffer(8);
            Http3CodecUtils.WriteVariableLengthInteger(buffer, streamType);
            CloseOnFailure(ctx.Channel, ctx.WriteAndFlushAsync(buffer));
            StreamAvailable(ctx);
            ctx.FireChannelActive();
        }

        public sealed override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            StreamClosed(ctx);

            if (evt is ChannelInputShutdownEvent)
            {
                // See https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#section-4.2
                CriticalStreamClosed(ctx);
            }

            ctx.FireUserEventTriggered(evt);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            StreamClosed(ctx);
            // See https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#section-4.2
            CriticalStreamClosed(ctx);
            ctx.FireChannelInactive();
        }

        protected abstract void StreamAvailable(IChannelHandlerContext ctx);

        protected abstract void StreamClosed(IChannelHandlerContext ctx);
    }

    private sealed class QPackEncoderStreamInitializer : AbstractQPackStreamInitializer
    {
        private static readonly ClosedChannelException ENCODER_STREAM_INACTIVE =
                ThrowableUtil.UnknownStackTrace(new ClosedChannelException(), typeof(ClosedChannelException),
                        "streamClosed()");
        private readonly QpackEncoder encoder;
        private readonly long maxTableCapacity;
        private readonly long blockedStreams;

        internal QPackEncoderStreamInitializer(QpackEncoder encoder, QpackAttributes attributes, long maxTableCapacity,
                                               long blockedStreams)
            : base(Http3CodecUtils.HTTP3_QPACK_ENCODER_STREAM_TYPE, attributes)
        {
            this.encoder = encoder;
            this.maxTableCapacity = maxTableCapacity;
            this.blockedStreams = blockedStreams;
        }

        protected override void StreamAvailable(IChannelHandlerContext ctx)
        {
            IQuicStreamChannel stream = (IQuicStreamChannel) ctx.Channel;
            attributes.EncoderStream(stream);

            try
            {
                encoder.ConfigureDynamicTable(attributes, maxTableCapacity, ToIntOrThrow(blockedStreams));
            }
            catch (QpackException e)
            {
                ConnectionError(ctx, new Http3Exception(QPACK_ENCODER_STREAM_ERROR,
                        "Dynamic table configuration failed.", e), true);
            }
        }

        protected override void StreamClosed(IChannelHandlerContext ctx)
        {
            attributes.EncoderStreamInactive(ENCODER_STREAM_INACTIVE);
        }
    }

    private sealed class QPackDecoderStreamInitializer : AbstractQPackStreamInitializer
    {
        private static readonly ClosedChannelException DECODER_STREAM_INACTIVE =
                ThrowableUtil.UnknownStackTrace(new ClosedChannelException(), typeof(ClosedChannelException),
                        "streamClosed()");
        internal QPackDecoderStreamInitializer(QpackAttributes attributes)
            : base(Http3CodecUtils.HTTP3_QPACK_DECODER_STREAM_TYPE, attributes)
        {
        }

        protected override void StreamAvailable(IChannelHandlerContext ctx)
        {
            attributes.DecoderStream((IQuicStreamChannel) ctx.Channel);
        }

        protected override void StreamClosed(IChannelHandlerContext ctx)
        {
            attributes.DecoderStreamInactive(DECODER_STREAM_INACTIVE);
        }
    }
}
