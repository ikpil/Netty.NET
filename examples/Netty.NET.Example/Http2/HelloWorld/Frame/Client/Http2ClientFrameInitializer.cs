/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http2.HelloWorld.Frame.Client;

/**
 * Configures client pipeline to support HTTP/2 frames via {@link Http2FrameCodec} and {@link Http2MultiplexHandler}.
 */
// CLR: Channel is IChannel. The anonymous raw SimpleChannelInboundHandler is the nested InboundStreamHandler, a
// SimpleChannelInboundHandler<object> (the raw type matches every message).
public sealed class Http2ClientFrameInitializer : ChannelInitializer<IChannel>
{

    private readonly SslContext sslCtx;

    public Http2ClientFrameInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(IChannel ch)
    {
        // ensure that our 'trust all' SSL handler is the first in the pipeline if SSL is enabled.
        if (sslCtx != null)
        {
            ch.Pipeline.AddFirst(sslCtx.NewHandler(ch.Alloc()));
        }

        Http2FrameCodec http2FrameCodec = Http2FrameCodecBuilder.ForClient()
            .InitialSettings(Http2Settings.DefaultSettings()) // this is the default, but shows it can be changed.
            .Build();
        ch.Pipeline.AddLast(http2FrameCodec);
        ch.Pipeline.AddLast(new Http2MultiplexHandler(new InboundStreamHandler()));
    }

    private sealed class InboundStreamHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            // NOOP (this is the handler for 'inbound' streams, which is not relevant in this example)
        }
    }

}
