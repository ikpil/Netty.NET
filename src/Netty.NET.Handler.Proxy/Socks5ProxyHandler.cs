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
using System.Collections.Generic;
using System.Net;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Proxy;

/**
 * Handler that establishes a blind forwarding proxy tunnel using
 * <a href="https://www.rfc-editor.org/rfc/rfc1928">SOCKS Protocol Version 5</a>.
 */
// CLR: SocketAddress is EndPoint; "InetSocketAddress raddr = destinationAddress()" is
// ProxyHandler.CastToInetSocketAddress, raddr.isUnresolved() is a DnsEndPoint whose Host is getHostString(), and
// raddr.getAddress().getHostAddress() is ProxyHandler.GetHostAddress of the IPEndPoint's Address (the JDK text).
// Java's byte is the C# byte of the codec-socks port (Socks5AuthMethod.ByteValue()). The Socks5AuthMethod
// comparisons stay reference comparisons (the codec-socks types declare no == operator). java.lang.Error for the
// unreachable authMethod branch is InvalidOperationException, as codec-base's AsciiHeadersEncoder maps it.
public sealed class Socks5ProxyHandler : ProxyHandler
{
    private const string PROTOCOL = "socks5";
    private const string AUTH_PASSWORD = "password";
    private const string AUTH_PRIVATE = "private";

    private static readonly byte NO_PRIVATE_AUTH_METHOD =
        Socks5AuthMethod.NO_AUTH.ByteValue();

    private static readonly ISocks5InitialRequest INIT_REQUEST_NO_AUTH =
            new DefaultSocks5InitialRequest(new List<Socks5AuthMethod> { Socks5AuthMethod.NO_AUTH });

    private static readonly ISocks5InitialRequest INIT_REQUEST_PASSWORD =
            new DefaultSocks5InitialRequest(new List<Socks5AuthMethod> { Socks5AuthMethod.NO_AUTH, Socks5AuthMethod.PASSWORD });

    private readonly string username;
    private readonly string password;
    private readonly byte privateAuthMethod;
    private readonly byte[] privateToken;
    private readonly Socks5ClientEncoder clientEncoder;

    private string decoderName;
    private string encoderName;

    public Socks5ProxyHandler(EndPoint proxyAddress) : this(proxyAddress, null, null)
    {
    }

    public Socks5ProxyHandler(EndPoint proxyAddress, string username, string password) : base(proxyAddress)
    {
        if (username != null && username.Length == 0)
        {
            username = null;
        }

        if (password != null && password.Length == 0)
        {
            password = null;
        }

        this.username = username;
        this.password = password;
        this.privateToken = null;
        this.privateAuthMethod = NO_PRIVATE_AUTH_METHOD; // No private authentication method specified
        this.clientEncoder = Socks5ClientEncoder.DEFAULT;
    }

    /**
     * Creates a new SOCKS5 proxy handler with a custom private authentication method.
     *
     * @param proxyAddress     The address of the SOCKS5 proxy server
     * @param privateAuthMethod The private authentication method code (must be in range 0x80-0xFE)
     * @param privateToken     The token to use for private authentication
     * @param customEncoder    The custom encoder to use for encoding SOCKS5 messages, if {@code null} the
     *                         {@link Socks5ClientEncoder#DEFAULT} will be used
     * @throws IllegalArgumentException If privateAuthMethod is not in the valid range
     */
    public Socks5ProxyHandler(EndPoint proxyAddress, byte privateAuthMethod, byte[] privateToken,
                              Socks5ClientEncoder customEncoder) : base(proxyAddress)
    {
        if (!Socks5AuthMethod.IsPrivateMethod(privateAuthMethod))
        {
            throw new ArgumentException(
                    "privateAuthMethod: " + (privateAuthMethod & 0xFF) + " (expected: 0x80-0xFE)");
        }

        this.username = this.password = null;
        this.privateToken = privateToken;
        this.privateAuthMethod = privateAuthMethod;
        this.clientEncoder = customEncoder != null ? customEncoder : Socks5ClientEncoder.DEFAULT;
    }

    public override string Protocol()
    {
        return PROTOCOL;
    }

    public override string AuthScheme()
    {
        Socks5AuthMethod authMethod = SocksAuthMethod();
        if (Socks5AuthMethod.IsPrivateMethod(authMethod.ByteValue()))
        {
            return AUTH_PRIVATE;
        }
        if (authMethod == Socks5AuthMethod.PASSWORD)
        {
            return AUTH_PASSWORD;
        }

        return AUTH_NONE;
    }

    public string Username()
    {
        return username;
    }

    public string Password()
    {
        return password;
    }

    protected override void AddCodec(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        string name = ctx.Name;

        Socks5InitialResponseDecoder decoder = new Socks5InitialResponseDecoder();
        p.AddBefore(name, null, decoder);

        decoderName = p.Context(decoder).Name;
        encoderName = decoderName + ".encoder";

        p.AddBefore(name, encoderName, clientEncoder);
    }

    protected override void RemoveEncoder(IChannelHandlerContext ctx)
    {
        ctx.Pipeline.Remove(encoderName);
    }

    protected override void RemoveDecoder(IChannelHandlerContext ctx)
    {
        IChannelPipeline p = ctx.Pipeline;
        if (p.Context(decoderName) != null)
        {
            p.Remove(decoderName);
        }
    }

    protected override object NewInitialMessage(IChannelHandlerContext ctx)
    {
        Socks5AuthMethod authMethod = SocksAuthMethod();
        if (authMethod == Socks5AuthMethod.PASSWORD)
        {
            return INIT_REQUEST_PASSWORD;
        }
        if (Socks5AuthMethod.IsPrivateMethod(authMethod.ByteValue()))
        {
            return new DefaultSocks5InitialRequest(new List<Socks5AuthMethod> { Socks5AuthMethod.NO_AUTH,
                authMethod });
        }

        return INIT_REQUEST_NO_AUTH;
    }

    protected override bool HandleResponse(IChannelHandlerContext ctx, object response)
    {
        if (response is ISocks5InitialResponse)
        {
            ISocks5InitialResponse res = (ISocks5InitialResponse) response;
            Socks5AuthMethod authMethod = SocksAuthMethod();
            Socks5AuthMethod resAuthMethod = res.AuthMethod();
            if (resAuthMethod != Socks5AuthMethod.NO_AUTH && resAuthMethod != authMethod
                && !Socks5AuthMethod.IsPrivateMethod(resAuthMethod.ByteValue()))
            {
                // Server did not allow unauthenticated access nor accept the requested authentication scheme.
                throw new ProxyConnectException(ExceptionMessage("unexpected authMethod: " + res.AuthMethod()));
            }

            if (resAuthMethod == Socks5AuthMethod.NO_AUTH)
            {
                SendConnectCommand(ctx);
            }
            else if (resAuthMethod == Socks5AuthMethod.PASSWORD)
            {
                // In case of password authentication, send an authentication request.
                ctx.Pipeline.Replace(decoderName, decoderName, new Socks5PasswordAuthResponseDecoder());
                SendToProxyServer(new DefaultSocks5PasswordAuthRequest(
                        username != null? username : "", password != null? password : ""));
            }
            else if (Socks5AuthMethod.IsPrivateMethod(resAuthMethod.ByteValue()))
            {
                ctx.Pipeline.Replace(decoderName, decoderName, new Socks5PrivateAuthResponseDecoder());
                SendToProxyServer(new DefaultSocks5PrivateAuthRequest(privateToken));
            }
            else
            {
                // Should never reach here.
                throw new InvalidOperationException("Unexpected authMethod: " + resAuthMethod);
            }

            return false;
        }

        if (response is ISocks5PasswordAuthResponse)
        {
            // Received an authentication response from the server.
            ISocks5PasswordAuthResponse res = (ISocks5PasswordAuthResponse) response;
            if (res.Status() != Socks5PasswordAuthStatus.SUCCESS)
            {
                throw new ProxyConnectException(ExceptionMessage("authStatus: " + res.Status()));
            }

            SendConnectCommand(ctx);
            return false;
        }

        if (response is ISocks5PrivateAuthResponse)
        {
            ISocks5PrivateAuthResponse res = (ISocks5PrivateAuthResponse) response;
            if (res.Status() != Socks5PrivateAuthStatus.SUCCESS)
            {
                throw new ProxyConnectException(ExceptionMessage("privateAuthStatus: " + res.Status()));
            }

            SendConnectCommand(ctx);
            return false;
        }

        // This should be the last message from the server.
        // CLR: the block keeps this res apart from the res locals of the branches above, which the compiler would
        // otherwise reject with CS0136 (a local of the same name in an enclosing scope).
        {
            ISocks5CommandResponse res = (ISocks5CommandResponse) response;
            if (res.Status() != Socks5CommandStatus.SUCCESS)
            {
                throw new ProxyConnectException(ExceptionMessage("status: " + res.Status()));
            }
        }

        return true;
    }

    private Socks5AuthMethod SocksAuthMethod()
    {
        Socks5AuthMethod authMethod;

        if (privateToken != null && privateToken.Length > 0)
        {
            authMethod = new Socks5AuthMethod(privateAuthMethod & 0xFF, "PRIVATE_" + (privateAuthMethod & 0xFF));
        }
        else if (username == null && password == null)
        {
            authMethod = Socks5AuthMethod.NO_AUTH;
        }
        else
        {
            authMethod = Socks5AuthMethod.PASSWORD;
        }

        return authMethod;
    }

    private void SendConnectCommand(IChannelHandlerContext ctx)
    {
        EndPoint raddr = CastToInetSocketAddress(DestinationAddress<EndPoint>());
        Socks5AddressType addrType;
        string rhost;
        int port;

        if (raddr is DnsEndPoint unresolved)
        {
            addrType = Socks5AddressType.DOMAIN;
            rhost = unresolved.Host;
            port = unresolved.Port;
        }
        else
        {
            IPEndPoint resolved = (IPEndPoint) raddr;
            rhost = GetHostAddress(resolved.Address);
            port = resolved.Port;

            if (NetUtil.IsValidIpV4Address(rhost))
            {
                addrType = Socks5AddressType.IPv4;
            }
            else if (NetUtil.IsValidIpV6Address(rhost))
            {
                addrType = Socks5AddressType.IPv6;
            }
            else
            {
                throw new ProxyConnectException(
                        ExceptionMessage("unknown address type: " + StringUtil.SimpleClassName(rhost)));
            }
        }

        ctx.Pipeline.Replace(decoderName, decoderName, new Socks5CommandResponseDecoder());
        SendToProxyServer(new DefaultSocks5CommandRequest(Socks5CommandType.CONNECT, addrType, rhost, port));
    }
}
