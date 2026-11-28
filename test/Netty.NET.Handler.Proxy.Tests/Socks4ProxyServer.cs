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

using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: see ProxyServer for the address and helper mappings. The SOCKS4 message interfaces take the I prefix;
// pipeline.remove(Class) is Remove<T>(). The inner handler classes take this server as a constructor argument.
internal sealed class Socks4ProxyServer : ProxyServer
{
    internal Socks4ProxyServer(bool useSsl, TestMode testMode, EndPoint destination)
        : base(useSsl, testMode, destination)
    {
    }

    internal Socks4ProxyServer(bool useSsl, TestMode testMode, EndPoint destination, string username)
        : base(useSsl, testMode, destination, username, null)
    {
    }

    protected override void Configure(ISocketChannel ch)
    {
        IChannelPipeline p = ch.Pipeline;

        switch (testMode)
        {
        case TestMode.INTERMEDIARY:
            p.AddLast(new Socks4ServerDecoder());
            p.AddLast(Socks4ServerEncoder.INSTANCE);
            p.AddLast(new Socks4IntermediaryHandler(this));
            break;
        case TestMode.TERMINAL:
            p.AddLast(new Socks4ServerDecoder());
            p.AddLast(Socks4ServerEncoder.INSTANCE);
            p.AddLast(new Socks4TerminalHandler(this));
            break;
        case TestMode.UNRESPONSIVE:
            p.AddLast(UnresponsiveHandler.INSTANCE);
            break;
        }
    }

    private bool Authenticate(IChannelHandlerContext ctx, ISocks4CommandRequest req)
    {
        Assert.Equal(Socks4CommandType.CONNECT, req.Type());

        if (testMode != TestMode.INTERMEDIARY)
        {
            ctx.Pipeline.AddBefore(ctx.Name, "lineDecoder", new LineBasedFrameDecoder(64, false, true));
        }

        bool authzSuccess;

        if (username != null)
        {
            authzSuccess = username.Equals(req.UserId());
        }
        else
        {
            authzSuccess = true;
        }

        return authzSuccess;
    }

    private sealed class Socks4IntermediaryHandler : IntermediaryHandler
    {
        private readonly Socks4ProxyServer server;
        private EndPoint intermediaryDestination;

        internal Socks4IntermediaryHandler(Socks4ProxyServer server) : base(server)
        {
            this.server = server;
        }

        protected override bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg)
        {
            ISocks4CommandRequest req = (ISocks4CommandRequest) msg;
            ISocks4CommandResponse res;

            if (!server.Authenticate(ctx, req))
            {
                res = new DefaultSocks4CommandResponse(Socks4CommandStatus.IDENTD_AUTH_FAILURE);
            }
            else
            {
                res = new DefaultSocks4CommandResponse(Socks4CommandStatus.SUCCESS);
                intermediaryDestination = SocketAddress(req.DstAddr(), req.DstPort());
            }

            _ = ctx.WriteAsync(res);

            ctx.Pipeline.Remove<Socks4ServerDecoder>();
            ctx.Pipeline.Remove<Socks4ServerEncoder>();

            return true;
        }

        protected override EndPoint IntermediaryDestination()
        {
            return intermediaryDestination;
        }
    }

    private sealed class Socks4TerminalHandler : TerminalHandler
    {
        private readonly Socks4ProxyServer server;

        internal Socks4TerminalHandler(Socks4ProxyServer server) : base(server)
        {
            this.server = server;
        }

        protected override bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg)
        {
            ISocks4CommandRequest req = (ISocks4CommandRequest) msg;
            bool authzSuccess = server.Authenticate(ctx, req);

            ISocks4CommandResponse res;
            bool sendGreeting = false;

            if (!authzSuccess)
            {
                res = new DefaultSocks4CommandResponse(Socks4CommandStatus.IDENTD_AUTH_FAILURE);
            }
            else if (!req.DstAddr().Equals(GetHostString(server.destination)) ||
                       req.DstPort() != GetPort(server.destination))
            {
                res = new DefaultSocks4CommandResponse(Socks4CommandStatus.REJECTED_OR_FAILED);
            }
            else
            {
                res = new DefaultSocks4CommandResponse(Socks4CommandStatus.SUCCESS);
                sendGreeting = true;
            }

            _ = ctx.WriteAsync(res);

            ctx.Pipeline.Remove<Socks4ServerDecoder>();
            ctx.Pipeline.Remove<Socks4ServerEncoder>();

            if (sendGreeting)
            {
                _ = ctx.WriteAsync(Unpooled.CopiedBuffer("0\n", Encoding.ASCII));
            }

            return true;
        }
    }
}
