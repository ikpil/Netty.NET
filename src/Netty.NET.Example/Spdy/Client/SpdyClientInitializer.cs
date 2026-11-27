/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.Logging.InternalLogLevel;
using static Netty.NET.Handler.Codec.Spdy.SpdyVersion;

namespace Netty.NET.Example.Spdy.Client;

// CLR: SocketChannel is ISocketChannel and ChannelPipeline is IChannelPipeline. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected. The codec-http spdy package is Netty.NET.Handler.Codec.Spdy, as in Java.
public class SpdyClientInitializer : ChannelInitializer<ISocketChannel>
{
    private const int MAX_SPDY_CONTENT_LENGTH = 1024 * 1024; // 1 MB

    private readonly SslContext sslCtx;
    private readonly HttpResponseClientHandler httpResponseHandler;

    public SpdyClientInitializer(SslContext sslCtx, HttpResponseClientHandler httpResponseHandler)
    {
        this.sslCtx = sslCtx;
        this.httpResponseHandler = httpResponseHandler;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;
        pipeline.AddLast("ssl", sslCtx.NewHandler(ch.Alloc()));
        pipeline.AddLast("spdyFrameCodec", new SpdyFrameCodec(SPDY_3_1));
        pipeline.AddLast("spdyFrameLogger", new SpdyFrameLogger(INFO));
        pipeline.AddLast("spdySessionHandler", new SpdySessionHandler(SPDY_3_1, false));
        pipeline.AddLast("spdyHttpEncoder", new SpdyHttpEncoder(SPDY_3_1));
        pipeline.AddLast("spdyHttpDecoder", new SpdyHttpDecoder(SPDY_3_1, MAX_SPDY_CONTENT_LENGTH));
        pipeline.AddLast("spdyStreamIdHandler", new SpdyClientStreamIdHandler());
        pipeline.AddLast("httpHandler", httpResponseHandler);
    }
}
