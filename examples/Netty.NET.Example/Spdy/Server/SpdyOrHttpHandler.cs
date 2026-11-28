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
using System;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Spdy.Server;

/**
 * Negotiates with the browser if SPDY or HTTP is going to be used. Once decided, the Netty pipeline is setup with
 * the correct handlers for the selected protocol.
 */
// CLR: the protected constructor is protected internal: Java's protected also grants package access, which
// SpdyServerInitializer (same package) uses; C# protected alone would not admit it. ChannelPipeline is IChannelPipeline,
// String.equals is the ordinal string.Equals and IllegalStateException is InvalidOperationException.
public class SpdyOrHttpHandler : ApplicationProtocolNegotiationHandler
{
    private const int MAX_CONTENT_LENGTH = 1024 * 100;

    protected internal SpdyOrHttpHandler() : base(ApplicationProtocolNames.HTTP_1_1)
    {
    }

    protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
    {
        if (ApplicationProtocolNames.SPDY_3_1.Equals(protocol))
        {
            ConfigureSpdy(ctx, SpdyVersion.SPDY_3_1);
            return;
        }

        if (ApplicationProtocolNames.HTTP_1_1.Equals(protocol))
        {
            ConfigureHttp1(ctx);
            return;
        }

        throw new InvalidOperationException("unknown protocol: " + protocol);
    }

    private static void ConfigureSpdy(IChannelHandlerContext ctx, SpdyVersion version)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.AddLast(new SpdyFrameCodec(version));
        p.AddLast(new SpdySessionHandler(version, true));
        p.AddLast(new SpdyHttpEncoder(version));
        p.AddLast(new SpdyHttpDecoder(version, MAX_CONTENT_LENGTH));
        p.AddLast(new SpdyHttpResponseStreamIdHandler());
        p.AddLast(new SpdyServerHandler());
    }

    private static void ConfigureHttp1(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.AddLast(new HttpServerCodec());
        p.AddLast(new HttpObjectAggregator(MAX_CONTENT_LENGTH));
        p.AddLast(new SpdyServerHandler());
    }
}
