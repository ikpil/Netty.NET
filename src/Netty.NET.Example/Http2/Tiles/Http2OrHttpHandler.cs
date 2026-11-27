/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Used during protocol negotiation, the main function of this handler is to
 * return the HTTP/1.1 or HTTP/2 handler once the protocol has been negotiated.
 */
// CLR: the protected constructor is also reached from Http2Server in the same Java package (Java's protected includes
// package access), so it is protected internal. IllegalStateException is InvalidOperationException.
public class Http2OrHttpHandler : ApplicationProtocolNegotiationHandler
{

    private const int MAX_CONTENT_LENGTH = 1024 * 100;

    protected internal Http2OrHttpHandler()
        : base(ApplicationProtocolNames.HTTP_1_1)
    {
    }

    protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
    {
        if (ApplicationProtocolNames.HTTP_2.Equals(protocol))
        {
            ConfigureHttp2(ctx);
            return;
        }

        if (ApplicationProtocolNames.HTTP_1_1.Equals(protocol))
        {
            ConfigureHttp1(ctx);
            return;
        }

        throw new InvalidOperationException("unknown protocol: " + protocol);
    }

    private static void ConfigureHttp2(IChannelHandlerContext ctx)
    {
        DefaultHttp2Connection connection = new DefaultHttp2Connection(true);
        InboundHttp2ToHttpAdapter listener = new InboundHttp2ToHttpAdapterBuilder(connection)
                .PropagateSettings(true).ValidateHttpHeaders(false)
                .MaxContentLength(MAX_CONTENT_LENGTH).Build();

        ctx.Pipeline.AddLast(new HttpToHttp2ConnectionHandlerBuilder()
                .FrameListener(listener)
                // .frameLogger(TilesHttp2ToHttpHandler.logger)
                .Connection(connection).Build());

        ctx.Pipeline.AddLast(new Http2RequestHandler());
    }

    private static void ConfigureHttp1(IChannelHandlerContext ctx)
    {
        ctx.Pipeline.AddLast(new HttpServerCodec(),
                               new HttpObjectAggregator(MAX_CONTENT_LENGTH),
                               new FallbackRequestHandler());
    }
}
