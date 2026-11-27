/*
 * Copyright 2022 The Netty Project
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
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http2.File;

// CLR: SocketChannel is ISocketChannel and ChannelPipeline IChannelPipeline. The public initChannel(SocketChannel) widens
// the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual method, so it
// stays protected.
public class Http2StaticFileServerInitializer : ChannelInitializer<ISocketChannel>
{

    private readonly SslContext sslCtx;

    public Http2StaticFileServerInitializer(SslContext sslCtx)
    {
        this.sslCtx = sslCtx;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;
        pipeline.AddLast(sslCtx.NewHandler(ch.Alloc()));
        pipeline.AddLast(Http2FrameCodecBuilder.ForServer().Build());
        pipeline.AddLast(new ChunkedWriteHandler());
        pipeline.AddLast(new Http2StaticFileServerHandler());
    }
}
