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

using System.Globalization;
using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Base64Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: see ProxyServer for the address and helper mappings. The HTTP message interfaces take the I prefix;
// pipeline.remove/get(Class) are Remove<T>()/Get<T>(); String.split(" ", 2) is Split(' ', 2) (the pattern is a single
// literal space); Integer.parseInt is int.Parse with the invariant culture; assertThat(x).isGreaterThan(0) is
// Assert.True(x > 0). The inner handler classes take this server as a constructor argument.
internal sealed class HttpProxyServer : ProxyServer
{
    internal HttpProxyServer(bool useSsl, TestMode testMode, EndPoint destination)
        : base(useSsl, testMode, destination)
    {
    }

    internal HttpProxyServer(
            bool useSsl, TestMode testMode, EndPoint destination, string username, string password)
        : base(useSsl, testMode, destination, username, password)
    {
    }

    protected override void Configure(ISocketChannel ch)
    {
        IChannelPipeline p = ch.Pipeline;

        switch (testMode)
        {
        case TestMode.INTERMEDIARY:
            p.AddLast(new HttpServerCodec());
            p.AddLast(new HttpObjectAggregator(1));
            p.AddLast(new HttpIntermediaryHandler(this));
            break;
        case TestMode.TERMINAL:
            p.AddLast(new HttpServerCodec());
            p.AddLast(new HttpObjectAggregator(1));
            p.AddLast(new HttpTerminalHandler(this));
            break;
        case TestMode.UNRESPONSIVE:
            p.AddLast(UnresponsiveHandler.INSTANCE);
            break;
        }
    }

    private bool Authenticate(IChannelHandlerContext ctx, IFullHttpRequest req)
    {
        Assert.Equal(HttpMethod.CONNECT, req.Method());

        if (testMode != TestMode.INTERMEDIARY)
        {
            ctx.Pipeline.AddBefore(ctx.Name, "lineDecoder", new LineBasedFrameDecoder(64, false, true));
        }

        ctx.Pipeline.Remove<HttpObjectAggregator>();
        ctx.Pipeline.Get<HttpServerCodec>().RemoveInboundHandler();

        bool authzSuccess = false;

        if (username != null)
        {
            string authz = req.Headers().Get(HttpHeaderNames.PROXY_AUTHORIZATION);
            if (authz != null)
            {
                string[] authzParts = authz.ToString().Split(' ', 2);
                ByteBuf authzBuf64 = Unpooled.CopiedBuffer(authzParts[1], Encoding.ASCII);
                ByteBuf authzBuf = Base64.Decode(authzBuf64);

                string expectedAuthz = username + ':' + password;
                authzSuccess = "Basic".Equals(authzParts[0]) &&
                               expectedAuthz.Equals(authzBuf.ToString(Encoding.ASCII));

                authzBuf64.Release();
                authzBuf.Release();
            }
        }
        else
        {
            authzSuccess = true;
        }

        return authzSuccess;
    }

    private sealed class HttpIntermediaryHandler : IntermediaryHandler
    {
        private readonly HttpProxyServer server;
        private EndPoint intermediaryDestination;

        internal HttpIntermediaryHandler(HttpProxyServer server) : base(server)
        {
            this.server = server;
        }

        protected override bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg)
        {
            IFullHttpRequest req = (IFullHttpRequest) msg;
            IFullHttpResponse res;

            if (!server.Authenticate(ctx, req))
            {
                res = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.UNAUTHORIZED);
                res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, 0);
            }
            else
            {
                res = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
                string uri = req.Uri();
                int lastColonPos = uri.LastIndexOf(':');
                Assert.True(lastColonPos > 0);
                intermediaryDestination = SocketAddress(
                        uri.Substring(0, lastColonPos),
                        int.Parse(uri.Substring(lastColonPos + 1), CultureInfo.InvariantCulture));
            }

            _ = ctx.WriteAsync(res);
            ctx.Pipeline.Get<HttpServerCodec>().RemoveOutboundHandler();
            return true;
        }

        protected override EndPoint IntermediaryDestination()
        {
            return intermediaryDestination;
        }
    }

    private sealed class HttpTerminalHandler : TerminalHandler
    {
        private readonly HttpProxyServer server;

        internal HttpTerminalHandler(HttpProxyServer server) : base(server)
        {
            this.server = server;
        }

        protected override bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg)
        {
            IFullHttpRequest req = (IFullHttpRequest) msg;
            IFullHttpResponse res;
            bool sendGreeting = false;

            if (!server.Authenticate(ctx, req))
            {
                res = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.UNAUTHORIZED);
                res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, 0);
            }
            else if (!req.Uri().Equals(GetHostString(server.destination) + ':' + GetPort(server.destination)))
            {
                res = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.FORBIDDEN);
                res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, 0);
            }
            else
            {
                res = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
                sendGreeting = true;
            }

            _ = ctx.WriteAsync(res);
            ctx.Pipeline.Get<HttpServerCodec>().RemoveOutboundHandler();

            if (sendGreeting)
            {
                _ = ctx.WriteAsync(Unpooled.CopiedBuffer("0\n", Encoding.ASCII));
            }

            return true;
        }
    }
}
