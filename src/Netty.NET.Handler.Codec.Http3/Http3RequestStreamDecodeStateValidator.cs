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
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;
using static Netty.NET.Handler.Codec.Http3.Http3RequestStreamEncodeStateValidator;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: The State enum and the static helpers of Http3RequestStreamEncodeStateValidator are imported with using
// static; evaluateFrame returns State? (null when the frame is unexpected). super.channelRead(ctx, msg) is
// ChannelInboundHandlerAdapter.ChannelRead (ctx.FireChannelRead). throws Exception has no counterpart.
internal sealed class Http3RequestStreamDecodeStateValidator : ChannelInboundHandlerAdapter,
        IHttp3RequestStreamCodecState
{
    private State state = State.None;

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (!(msg is IHttp3RequestStreamFrame))
        {
            base.ChannelRead(ctx, msg);
            return;
        }

        IHttp3RequestStreamFrame frame = (IHttp3RequestStreamFrame) msg;
        State? nextState = EvaluateFrame(state, frame);
        if (nextState == null)
        {
            FrameTypeUnexpected(ctx, msg);
            return;
        }

        state = nextState.Value;
        base.ChannelRead(ctx, msg);
    }

    public bool Started()
    {
        return IsStreamStarted(state);
    }

    public bool ReceivedFinalHeaders()
    {
        return IsFinalHeadersReceived(state);
    }

    public bool Terminated()
    {
        return IsTrailersReceived(state);
    }
}
