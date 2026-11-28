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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: Http3FrameCodecFactory is the nested Http3FrameCodec.IHttp3FrameCodecFactory, LongFunction<ChannelHandler>
// Func<long, IChannelHandler> (apply(x) is the delegate invocation), Supplier<ChannelHandler>
// Func<IChannelHandler> and the lambda __ -> ReleaseHandler.INSTANCE a C# lambda. The Long sentMaxPushId() is
// long?; ids print with the invariant culture. The @Nullable (org.jetbrains.annotations) annotations have no
// counterpart.
internal sealed class Http3UnidirectionalStreamInboundClientHandler : Http3UnidirectionalStreamInboundHandler
{
    private readonly Func<long, IChannelHandler> pushStreamHandlerFactory;

    internal Http3UnidirectionalStreamInboundClientHandler(
            Http3FrameCodec.IHttp3FrameCodecFactory codecFactory,
            Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator,
            Http3ControlStreamInboundHandler localControlStreamHandler,
            Http3ControlStreamOutboundHandler remoteControlStreamHandler,
            Func<long, IChannelHandler> unknownStreamHandlerFactory,
            Func<long, IChannelHandler> pushStreamHandlerFactory,
            Func<IChannelHandler> qpackEncoderHandlerFactory, Func<IChannelHandler> qpackDecoderHandlerFactory)
        : base(codecFactory, nonStandardSettingsValidator,
                localControlStreamHandler, remoteControlStreamHandler, unknownStreamHandlerFactory,
                qpackEncoderHandlerFactory, qpackDecoderHandlerFactory)
    {
        this.pushStreamHandlerFactory = pushStreamHandlerFactory == null ? __ => ReleaseHandler.INSTANCE :
                pushStreamHandlerFactory;
    }

    internal override void InitPushStream(IChannelHandlerContext ctx, long pushId)
    {
        // See https://tools.ietf.org/html/draft-ietf-quic-http-32#section-4.4
        long? maxPushId = remoteControlStreamHandler.SentMaxPushId();
        if (maxPushId == null)
        {
            Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_ID_ERROR,
                    "Received push stream before sending MAX_PUSH_ID frame.", false);
        }
        else if (maxPushId.Value < pushId)
        {
            Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_ID_ERROR,
                    "Received push stream with ID " + pushId.ToString(CultureInfo.InvariantCulture) +
                    " greater than the max push ID " + maxPushId.Value.ToString(CultureInfo.InvariantCulture)
                            + '.', false);
        }
        else
        {
            // Replace this handler with the actual push stream handlers.
            IChannelHandler pushStreamHandler = pushStreamHandlerFactory(pushId);
            ctx.Pipeline.Replace(this, null, pushStreamHandler);
        }
    }
}
