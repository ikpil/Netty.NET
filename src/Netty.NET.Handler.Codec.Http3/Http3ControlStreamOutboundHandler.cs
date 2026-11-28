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

using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed; it derives from
// Http3FrameTypeDuplexValidationHandler<IHttp3ControlStreamFrame>, whose Class<T> constructor argument is the Type
// typeof(IHttp3ControlStreamFrame), and overrides its internal virtual write(ctx, T, promise), whose ChannelPromise is
// the TaskCompletionSource that the public WriteAsync returns the Task of. promise.setFailure is SetException;
// ctx.write(msg, promise) is Http3CodecUtils.Write. ctx.write(buffer) is WriteAsync, whose Task is not awaited, and
// closeOnFailure(ctx.writeAndFlush(localSettings)) is Http3CodecUtils.CloseOnFailure(ctx.Channel, ...). The Long fields
// sentMaxPushId/sendGoAwayId are long?; ids print with the invariant culture. ChannelHandler is IChannelHandler and
// Http3SettingsFrame IHttp3SettingsFrame. assert is Debug.Assert; isSharable() overrides the IsSharable property. The
// @Nullable (org.jetbrains.annotations) annotations have no counterpart.
internal sealed class Http3ControlStreamOutboundHandler
        : Http3FrameTypeDuplexValidationHandler<IHttp3ControlStreamFrame>
{
    private readonly bool server;
    private readonly IChannelHandler codec;
    private long? sentMaxPushId;
    private long? sendGoAwayId;
    private IHttp3SettingsFrame localSettings;

    internal Http3ControlStreamOutboundHandler(bool server, IHttp3SettingsFrame localSettings, IChannelHandler codec)
        : base(typeof(IHttp3ControlStreamFrame))
    {
        this.server = server;
        this.localSettings = ObjectUtil.CheckNotNull(localSettings, "localSettings");
        this.codec = ObjectUtil.CheckNotNull(codec, "codec");
    }

    /**
     * Returns the last id that was sent in a MAX_PUSH_ID frame or {@code null} if none was sent yet.
     *
     * @return the id.
     */
    internal long? SentMaxPushId()
    {
        return sentMaxPushId;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        // We need to write 0x00 into the stream before doing anything else.
        // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.1
        // Just allocate 8 bytes which would be the max needed.
        ByteBuf buffer = ctx.Alloc().Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_CONTROL_STREAM_TYPE);
        _ = ctx.WriteAsync(buffer);
        // Add the encoder and decoder in the pipeline so we can handle Http3Frames. This needs to happen after
        // we did write the type via a ByteBuf.
        ctx.Pipeline.AddFirst(codec);

        Debug.Assert(localSettings != null);
        // If writing of the local settings fails let's just teardown the connection.
        CloseOnFailure(ctx.Channel, ctx.WriteAndFlushAsync(localSettings));

        // Let the GC collect localSettings.
        localSettings = null;

        ctx.FireChannelActive();
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is ChannelInputShutdownEvent)
        {
            // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.1
            Http3CodecUtils.CriticalStreamClosed(ctx);
        }

        ctx.FireUserEventTriggered(evt);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2.1
        Http3CodecUtils.CriticalStreamClosed(ctx);
        ctx.FireChannelInactive();
    }

    internal override void Write(IChannelHandlerContext ctx, IHttp3ControlStreamFrame msg, TaskCompletionSource promise)
    {
        if (msg is IHttp3MaxPushIdFrame && !HandleHttp3MaxPushIdFrame(promise, (IHttp3MaxPushIdFrame) msg))
        {
            ReferenceCountUtil.Release(msg);
            return;
        }
        else if (msg is IHttp3GoAwayFrame && !HandleHttp3GoAwayFrame(promise, (IHttp3GoAwayFrame) msg))
        {
            ReferenceCountUtil.Release(msg);
            return;
        }

        Http3CodecUtils.Write(ctx, msg, promise);
    }

    private bool HandleHttp3MaxPushIdFrame(TaskCompletionSource promise, IHttp3MaxPushIdFrame maxPushIdFrame)
    {
        long id = maxPushIdFrame.Id();

        // See https://datatracker.ietf.org/doc/html/draft-ietf-quic-http-32#section-7.2.7
        if (sentMaxPushId != null && id < sentMaxPushId.Value)
        {
            promise.SetException(new Http3Exception(Http3ErrorCode.H3_ID_ERROR, "MAX_PUSH_ID reduced limit."));
            return false;
        }

        sentMaxPushId = maxPushIdFrame.Id();
        return true;
    }

    private bool HandleHttp3GoAwayFrame(TaskCompletionSource promise, IHttp3GoAwayFrame goAwayFrame)
    {
        long id = goAwayFrame.Id();

        // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-5.2
        if (server && id % 4 != 0)
        {
            promise.SetException(new Http3Exception(Http3ErrorCode.H3_ID_ERROR,
                    "GOAWAY id not valid : " + id.ToString(CultureInfo.InvariantCulture)));
            return false;
        }

        if (sendGoAwayId != null && id > sendGoAwayId.Value)
        {
            promise.SetException(new Http3Exception(Http3ErrorCode.H3_ID_ERROR,
                    "GOAWAY id is bigger then the last sent: " + id.ToString(CultureInfo.InvariantCulture) + " > " +
                    sendGoAwayId.Value.ToString(CultureInfo.InvariantCulture)));
            return false;
        }

        sendGoAwayId = id;
        return true;
    }

    // This handle keeps state so we cant reuse it.
    public override bool IsSharable => false;
}
