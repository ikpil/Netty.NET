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
using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: see ProxyServer for the address and helper mappings. Java's byte is the C# byte of the codec-socks port; the
// SOCKS5 message interfaces take the I prefix. Arrays.equals(byte[], byte[]) is true for two null arrays or two arrays
// with the same elements, which is the span SequenceEqual guarded for null. The inner handler classes take this
// server as a constructor argument.
internal sealed class Socks5ProxyServer : ProxyServer
{
    private const string ENCODER = "encoder";
    private const string DECODER = "decoder";
    private byte privateAuthMethod;
    private byte[] privateToken;

    internal Socks5ProxyServer(bool useSsl, TestMode testMode, EndPoint destination)
        : base(useSsl, testMode, destination)
    {
    }

    internal Socks5ProxyServer(
            bool useSsl, TestMode testMode, EndPoint destination, string username, string password)
        : base(useSsl, testMode, destination, username, password)
    {
    }

    internal Socks5ProxyServer(
            bool useSsl, TestMode testMode, EndPoint destination, byte privateAuthMethod,
            byte[] privateToken) : base(useSsl, testMode, destination)
    {
        this.privateAuthMethod = privateAuthMethod;
        this.privateToken = privateToken;
    }

    protected override void Configure(ISocketChannel ch)
    {
        IChannelPipeline p = ch.Pipeline;

        switch (testMode)
        {
        case TestMode.INTERMEDIARY:
            p.AddLast(DECODER, new Socks5InitialRequestDecoder());
            p.AddLast(ENCODER, Socks5ServerEncoder.DEFAULT);
            p.AddLast(new Socks5IntermediaryHandler(this));
            break;
        case TestMode.TERMINAL:
            p.AddLast(DECODER, new Socks5InitialRequestDecoder());
            p.AddLast(ENCODER, Socks5ServerEncoder.DEFAULT);
            p.AddLast(new Socks5TerminalHandler(this));
            break;
        case TestMode.UNRESPONSIVE:
            p.AddLast(UnresponsiveHandler.INSTANCE);
            break;
        }
    }

    internal bool Authenticate(IChannelHandlerContext ctx, object msg)
    {
        if (username == null && privateToken == null)
        {
            ctx.Pipeline.Replace(DECODER, DECODER, new Socks5CommandRequestDecoder());
            _ = ctx.WriteAsync(new DefaultSocks5InitialResponse(Socks5AuthMethod.NO_AUTH));
            return true;
        }

        if (msg is ISocks5InitialRequest)
        {
            ISocks5InitialRequest initialRequest = (ISocks5InitialRequest) msg;

            if (privateToken != null)
            {
                // Check if the client requested our private auth method
                bool hasPrivateMethod = false;

                foreach (Socks5AuthMethod method in initialRequest.AuthMethods())
                {
                    if (method.ByteValue() == privateAuthMethod)
                    {
                        hasPrivateMethod = true;
                        break;
                    }
                }

                if (hasPrivateMethod)
                {
                    ctx.Pipeline.Replace(DECODER, DECODER, new Socks5PrivateAuthRequestDecoder());
                    _ = ctx.WriteAsync(new DefaultSocks5InitialResponse(Socks5AuthMethod.ValueOf(privateAuthMethod)));
                    return false;
                }
            }

            if (username != null)
            {
                ctx.Pipeline.Replace(DECODER, DECODER, new Socks5PasswordAuthRequestDecoder());
                _ = ctx.WriteAsync(new DefaultSocks5InitialResponse(Socks5AuthMethod.PASSWORD));
                return false;
            }

            // Neither private nor password auth was matched
            ctx.Pipeline.Replace(DECODER, DECODER, new Socks5PasswordAuthRequestDecoder());
            _ = ctx.WriteAsync(new DefaultSocks5InitialResponse(Socks5AuthMethod.PASSWORD));
            return false;
        }

        if (msg is ISocks5PasswordAuthRequest)
        {
            ISocks5PasswordAuthRequest req = (ISocks5PasswordAuthRequest) msg;
            if (req.Username().Equals(username) && req.Password().Equals(password))
            {
                ctx.Pipeline.Replace(DECODER, DECODER, new Socks5CommandRequestDecoder());
                _ = ctx.WriteAsync(new DefaultSocks5PasswordAuthResponse(Socks5PasswordAuthStatus.SUCCESS));
                return true;
            }

            ctx.Pipeline.Replace(DECODER, DECODER, new Socks5PasswordAuthRequestDecoder());
            _ = ctx.WriteAsync(new DefaultSocks5PasswordAuthResponse(Socks5PasswordAuthStatus.FAILURE));
            return false;
        }

        if (msg is ISocks5PrivateAuthRequest)
        {
            ISocks5PrivateAuthRequest req = (ISocks5PrivateAuthRequest) msg;
            if (ArraysEquals(req.PrivateToken(), privateToken))
            {
                ctx.Pipeline.Replace(DECODER, DECODER, new Socks5CommandRequestDecoder());
                _ = ctx.WriteAsync(new DefaultSocks5PrivateAuthResponse(Socks5PrivateAuthStatus.SUCCESS));
                return true;
            }

            ctx.Pipeline.Replace(DECODER, DECODER, new Socks5PrivateAuthRequestDecoder());
            _ = ctx.WriteAsync(new DefaultSocks5PrivateAuthResponse(Socks5PrivateAuthStatus.FAILURE));
            return false;
        }

        return false;
    }

    // CLR: Arrays.equals(byte[], byte[]) (see the class comment).
    private static bool ArraysEquals(byte[] a, byte[] a2)
    {
        if (a == a2)
        {
            return true;
        }

        return a != null && a2 != null && a.AsSpan().SequenceEqual(a2);
    }

    private sealed class Socks5IntermediaryHandler : IntermediaryHandler
    {
        private readonly Socks5ProxyServer server;
        private bool authenticated;
        private EndPoint intermediaryDestination;

        internal Socks5IntermediaryHandler(Socks5ProxyServer server) : base(server)
        {
            this.server = server;
        }

        protected override bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg)
        {
            if (!authenticated)
            {
                authenticated = server.Authenticate(ctx, msg);
                return false;
            }

            ISocks5CommandRequest req = (ISocks5CommandRequest) msg;
            Assert.Equal(Socks5CommandType.CONNECT, req.Type());

            ISocks5CommandResponse res =
                    new DefaultSocks5CommandResponse(Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4);
            intermediaryDestination = SocketAddress(req.DstAddr(), req.DstPort());

            _ = ctx.WriteAsync(res);

            ctx.Pipeline.Remove(ENCODER);
            ctx.Pipeline.Remove(DECODER);

            return true;
        }

        protected override EndPoint IntermediaryDestination()
        {
            return intermediaryDestination;
        }
    }

    private sealed class Socks5TerminalHandler : TerminalHandler
    {
        private readonly Socks5ProxyServer server;
        private bool authenticated;

        internal Socks5TerminalHandler(Socks5ProxyServer server) : base(server)
        {
            this.server = server;
        }

        protected override bool HandleProxyProtocol(IChannelHandlerContext ctx, object msg)
        {
            if (!authenticated)
            {
                authenticated = server.Authenticate(ctx, msg);
                return false;
            }

            ISocks5CommandRequest req = (ISocks5CommandRequest) msg;
            Assert.Equal(Socks5CommandType.CONNECT, req.Type());

            ctx.Pipeline.AddBefore(ctx.Name, "lineDecoder", new LineBasedFrameDecoder(64, false, true));

            ISocks5CommandResponse res;
            bool sendGreeting = false;

            if (!req.DstAddr().Equals(GetHostString(server.destination)) ||
                       req.DstPort() != GetPort(server.destination))
            {
                res = new DefaultSocks5CommandResponse(Socks5CommandStatus.FORBIDDEN, Socks5AddressType.IPv4);
            }
            else
            {
                res = new DefaultSocks5CommandResponse(Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4);
                sendGreeting = true;
            }

            _ = ctx.WriteAsync(res);

            ctx.Pipeline.Remove(ENCODER);
            ctx.Pipeline.Remove(DECODER);

            if (sendGreeting)
            {
                _ = ctx.WriteAsync(Unpooled.CopiedBuffer("0\n", Encoding.ASCII));
            }

            return true;
        }
    }
}
