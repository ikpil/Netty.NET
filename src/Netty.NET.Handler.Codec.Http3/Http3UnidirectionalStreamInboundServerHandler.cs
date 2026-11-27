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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed. Http3FrameCodecFactory is the nested
// Http3FrameCodec.IHttp3FrameCodecFactory, NonStandardHttp3SettingsValidator
// Http3Settings.INonStandardHttp3SettingsValidator, LongFunction<ChannelHandler> Func<long, IChannelHandler> and
// Supplier<ChannelHandler> Func<IChannelHandler>. The package-private override of initPushStream is internal. The
// @Nullable (org.jetbrains.annotations) annotations have no counterpart.
internal sealed class Http3UnidirectionalStreamInboundServerHandler : Http3UnidirectionalStreamInboundHandler
{

    internal Http3UnidirectionalStreamInboundServerHandler(
            Http3FrameCodec.IHttp3FrameCodecFactory codecFactory,
            Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator,
            Http3ControlStreamInboundHandler localControlStreamHandler,
            Http3ControlStreamOutboundHandler remoteControlStreamHandler,
            Func<long, IChannelHandler> unknownStreamHandlerFactory,
            Func<IChannelHandler> qpackEncoderHandlerFactory,
            Func<IChannelHandler> qpackDecoderHandlerFactory)
        : base(codecFactory, nonStandardSettingsValidator, localControlStreamHandler, remoteControlStreamHandler,
                unknownStreamHandlerFactory, qpackEncoderHandlerFactory, qpackDecoderHandlerFactory)
    {
    }

    internal override void InitPushStream(IChannelHandlerContext ctx, long id)
    {
        Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_STREAM_CREATION_ERROR,
                "Server received push stream.", false);
    }
}
