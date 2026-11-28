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
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Proxy;

/**
 * Handler that establishes a blind forwarding proxy tunnel using
 * <a href="https://www.openssh.com/txt/socks4.protocol">SOCKS4</a> protocol.
 */
// CLR: SocketAddress is EndPoint; "InetSocketAddress raddr = destinationAddress()" is
// ProxyHandler.CastToInetSocketAddress, raddr.isUnresolved() is a DnsEndPoint whose Host is getHostString(), and
// raddr.getAddress().getHostAddress() is ProxyHandler.GetHostAddress of the IPEndPoint's Address (the JDK text).
public sealed class Socks4ProxyHandler : ProxyHandler
{
    private const string PROTOCOL = "socks4";
    private const string AUTH_USERNAME = "username";

    private readonly string username;

    private string decoderName;
    private string encoderName;

    public Socks4ProxyHandler(EndPoint proxyAddress) : this(proxyAddress, null)
    {
    }

    public Socks4ProxyHandler(EndPoint proxyAddress, string username) : base(proxyAddress)
    {
        if (username != null && username.Length == 0)
        {
            username = null;
        }

        this.username = username;
    }

    public override string Protocol()
    {
        return PROTOCOL;
    }

    public override string AuthScheme()
    {
        return username != null? AUTH_USERNAME : AUTH_NONE;
    }

    public string Username()
    {
        return username;
    }

    protected override void AddCodec(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        string name = ctx.Name;

        Socks4ClientDecoder decoder = new Socks4ClientDecoder();
        p.AddBefore(name, null, decoder);

        decoderName = p.Context(decoder).Name;
        encoderName = decoderName + ".encoder";

        p.AddBefore(name, encoderName, Socks4ClientEncoder.INSTANCE);
    }

    protected override void RemoveEncoder(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.Remove(encoderName);
    }

    protected override void RemoveDecoder(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        p.Remove(decoderName);
    }

    protected override object NewInitialMessage(IChannelHandlerContext ctx)
    {
        EndPoint raddr = CastToInetSocketAddress(DestinationAddress<EndPoint>());
        string rhost;
        int port;

        if (raddr is DnsEndPoint unresolved)
        {
            rhost = unresolved.Host;
            port = unresolved.Port;
        }
        else
        {
            IPEndPoint resolved = (IPEndPoint) raddr;
            rhost = GetHostAddress(resolved.Address);
            port = resolved.Port;
        }

        return new DefaultSocks4CommandRequest(
                Socks4CommandType.CONNECT, rhost, port, username != null? username : "");
    }

    protected override bool HandleResponse(IChannelHandlerContext ctx, object response)
    {
        ISocks4CommandResponse res = (ISocks4CommandResponse) response;
        Socks4CommandStatus status = res.Status();
        if (status == Socks4CommandStatus.SUCCESS)
        {
            return true;
        }

        throw new ProxyConnectException(ExceptionMessage("status: " + status));
    }
}
