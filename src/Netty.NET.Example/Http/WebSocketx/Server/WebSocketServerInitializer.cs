/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http.WebSocketx.Server;

/**
 */
// CLR: SocketChannel is ISocketChannel and ChannelPipeline is IChannelPipeline. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected.
public class WebSocketServerInitializer : ChannelInitializer<ISocketChannel>
{
    private const string WEBSOCKET_PATH = "/websocket";

    private const int MAX_CONTENT_LENGTH = 65536;

    private readonly SslContext sslCtx;

    public WebSocketServerInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;

        if (sslCtx != null)
        {
            pipeline.AddLast(sslCtx.NewHandler(ch.Alloc()));
        }

        pipeline.AddLast(new HttpServerCodec());
        pipeline.AddLast(new HttpObjectAggregator(MAX_CONTENT_LENGTH));
        pipeline.AddLast(new WebSocketIndexPageHandler(WEBSOCKET_PATH));
        pipeline.AddLast(new WebSocketServerCompressionHandler(MAX_CONTENT_LENGTH));
        pipeline.AddLast(new WebSocketServerProtocolHandler(WEBSOCKET_PATH, null, true));
        pipeline.AddLast(new WebSocketFrameHandler());
    }
}
