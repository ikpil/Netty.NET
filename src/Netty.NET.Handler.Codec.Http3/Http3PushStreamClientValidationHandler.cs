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

using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http3.Http3RequestStreamValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is an internal sealed class. channelRead(ctx, frame) overrides the internal
// virtual member of Http3FrameTypeInboundValidationHandler; Http3RequestStreamFrame.class is
// typeof(IHttp3RequestStreamFrame). The isSharable() override is the IsSharable property of ChannelHandlerAdapter.
internal sealed class Http3PushStreamClientValidationHandler
        : Http3FrameTypeInboundValidationHandler<IHttp3RequestStreamFrame>
{
    private readonly QpackAttributes qpackAttributes;
    private readonly QpackDecoder qpackDecoder;
    private readonly IHttp3RequestStreamCodecState decodeState;

    private long expectedLength = -1;
    private long seenLength;

    internal Http3PushStreamClientValidationHandler(QpackAttributes qpackAttributes, QpackDecoder qpackDecoder,
                                                    IHttp3RequestStreamCodecState decodeState)
        : base(typeof(IHttp3RequestStreamFrame))
    {
        this.qpackAttributes = qpackAttributes;
        this.qpackDecoder = qpackDecoder;
        this.decodeState = decodeState;
    }

    internal override void ChannelRead(IChannelHandlerContext ctx, IHttp3RequestStreamFrame frame)
    {
        if (frame is IHttp3PushPromiseFrame)
        {
            ctx.FireChannelRead(frame);
            return;
        }

        if (frame is IHttp3HeadersFrame)
        {
            IHttp3HeadersFrame headersFrame = (IHttp3HeadersFrame) frame;
            long maybeContentLength = ValidateHeaderFrameRead(headersFrame, ctx, decodeState);
            if (maybeContentLength >= 0)
            {
                expectedLength = maybeContentLength;
            }
            else if (maybeContentLength == INVALID_FRAME_READ)
            {
                return;
            }
        }

        if (frame is IHttp3DataFrame)
        {
            IHttp3DataFrame dataFrame = (IHttp3DataFrame) frame;
            long maybeContentLength = ValidateDataFrameRead(dataFrame, ctx, expectedLength, seenLength, false);
            if (maybeContentLength >= 0)
            {
                seenLength = maybeContentLength;
            }
            else if (maybeContentLength == INVALID_FRAME_READ)
            {
                return;
            }
        }

        ctx.FireChannelRead(frame);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt == ChannelInputShutdownReadComplete.INSTANCE)
        {
            SendStreamAbandonedIfRequired(ctx, qpackAttributes, qpackDecoder, decodeState);
            if (!ValidateOnStreamClosure(ctx, expectedLength, seenLength, false))
            {
                return;
            }
        }

        ctx.FireUserEventTriggered(evt);
    }

    // This handle keeps state so we can't share it.
    public override bool IsSharable => false;
}
