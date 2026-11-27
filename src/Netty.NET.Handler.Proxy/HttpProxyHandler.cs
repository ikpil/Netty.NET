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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Base64Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Proxy;

/**
 * Handler that establishes a blind forwarding proxy tunnel using
 * <a href="https://datatracker.ietf.org/doc/html/rfc7231#section-4.3.6">HTTP/1.1 CONNECT</a> request. It can be used to
 * establish plaintext or secure tunnels.
 * <p>
 * HTTP users who need to connect to a
 * <a href="https://datatracker.ietf.org/doc/html/rfc7230#page-10">message-forwarding HTTP proxy agent</a> instead of a
 * tunneling proxy should not use this handler.
 */
// CLR: SocketAddress is EndPoint and InetSocketAddress is IPEndPoint or, unresolved, DnsEndPoint (see ProxyHandler).
// CharsetUtil.UTF_8/US_ASCII are Encoding.UTF8/ASCII and Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer, as in the
// codec-http port; the HTTP message interfaces take the I prefix (IFullHttpRequest, IHttpResponse, ILastHttpContent).
// CharSequence is ICharSequence. The protected newInitialMessage, which HttpProxyHandlerTest calls through package
// access, has the internal forwarder InvokeNewInitialMessage (docs/codec-http-porting.md, Package access).
public sealed class HttpProxyHandler : ProxyHandler
{
    private const string PROTOCOL = "http";
    private const string AUTH_BASIC = "basic";

    // Wrapper for the HttpClientCodec to prevent it to be removed by other handlers by mistake (for example the
    // WebSocket*Handshaker.
    //
    // See:
    // - https://github.com/netty/netty/issues/5201
    // - https://github.com/netty/netty/issues/5070
    private readonly HttpClientCodecWrapper codecWrapper = new HttpClientCodecWrapper();
    private readonly string username;
    private readonly string password;
    private readonly ICharSequence authorization;
    private readonly HttpHeaders outboundHeaders;
    private readonly bool ignoreDefaultPortsInConnectHostHeader;
    private readonly bool validateInitialHeaders;
    private HttpResponseStatus status;
    private HttpHeaders inboundHeaders;

    public HttpProxyHandler(EndPoint proxyAddress) : this(proxyAddress, (HttpHeaders) null)
    {
    }

    public HttpProxyHandler(EndPoint proxyAddress, HttpHeaders headers) : this(proxyAddress, headers, false)
    {
    }

    public HttpProxyHandler(EndPoint proxyAddress,
                            HttpHeaders headers,
                            bool ignoreDefaultPortsInConnectHostHeader)
        : this(proxyAddress, headers, ignoreDefaultPortsInConnectHostHeader, true)
    {
    }

    public HttpProxyHandler(EndPoint proxyAddress,
                            HttpHeaders headers,
                            bool ignoreDefaultPortsInConnectHostHeader,
                            bool validateInitialHeaders) : base(proxyAddress)
    {
        username = null;
        password = null;
        authorization = null;
        this.outboundHeaders = headers;
        this.ignoreDefaultPortsInConnectHostHeader = ignoreDefaultPortsInConnectHostHeader;
        this.validateInitialHeaders = validateInitialHeaders;
    }

    public HttpProxyHandler(EndPoint proxyAddress, string username, string password)
        : this(proxyAddress, username, password, null)
    {
    }

    public HttpProxyHandler(EndPoint proxyAddress, string username, string password,
                            HttpHeaders headers) : this(proxyAddress, username, password, headers, false, true)
    {
    }

    public HttpProxyHandler(EndPoint proxyAddress,
                            string username,
                            string password,
                            HttpHeaders headers,
                            bool ignoreDefaultPortsInConnectHostHeader)
        : this(proxyAddress, username, password, headers, ignoreDefaultPortsInConnectHostHeader, true)
    {
    }

    public HttpProxyHandler(EndPoint proxyAddress,
                            string username,
                            string password,
                            HttpHeaders headers,
                            bool ignoreDefaultPortsInConnectHostHeader,
                            bool validateInitialHeaders) : base(proxyAddress)
    {
        this.username = ObjectUtil.CheckNotNull(username, "username");
        this.password = ObjectUtil.CheckNotNull(password, "password");

        ByteBuf authz = Unpooled.CopiedBuffer(username + ':' + password, Encoding.UTF8);
        ByteBuf authzBase64;
        try
        {
            authzBase64 = Base64.Encode(authz, false);
        }
        finally
        {
            authz.Release();
        }
        try
        {
            authorization = new AsciiString("Basic " + authzBase64.ToString(Encoding.ASCII));
        }
        finally
        {
            authzBase64.Release();
        }

        this.outboundHeaders = headers;
        this.ignoreDefaultPortsInConnectHostHeader = ignoreDefaultPortsInConnectHostHeader;
        this.validateInitialHeaders = validateInitialHeaders;
    }

    public override string Protocol()
    {
        return PROTOCOL;
    }

    public override string AuthScheme()
    {
        return authorization != null? AUTH_BASIC : AUTH_NONE;
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
        p.AddBefore(name, null, codecWrapper);
    }

    protected override void RemoveEncoder(IChannelHandlerContext ctx)
    {
        codecWrapper.codec.RemoveOutboundHandler();
    }

    protected override void RemoveDecoder(IChannelHandlerContext ctx)
    {
        codecWrapper.codec.RemoveInboundHandler();
    }

    // CLR: "InetSocketAddress raddr = destinationAddress()" is ProxyHandler.CastToInetSocketAddress, and raddr.getPort()
    // is the Port of the IPEndPoint or DnsEndPoint.
    protected override object NewInitialMessage(IChannelHandlerContext ctx)
    {
        EndPoint raddr = CastToInetSocketAddress(DestinationAddress<EndPoint>());

        string hostString = HttpUtil.FormatHostnameForHttp(raddr);
        int port = raddr is DnsEndPoint unresolved ? unresolved.Port : ((IPEndPoint) raddr).Port;
        string url = hostString + ":" + port;
        string hostHeader = (ignoreDefaultPortsInConnectHostHeader && (port == 80 || port == 443)) ?
                hostString :
                url;

        IHttpHeadersFactory headersFactory = DefaultHttpHeadersFactory.HeadersFactory()
                .WithValidation(validateInitialHeaders);
        IFullHttpRequest req = new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.CONNECT,
                url,
                Unpooled.EmptyBuffer, headersFactory, headersFactory);

        req.Headers().Set(HttpHeaderNames.HOST, hostHeader);

        if (authorization != null)
        {
            req.Headers().Set(HttpHeaderNames.PROXY_AUTHORIZATION, authorization);
        }

        if (outboundHeaders != null)
        {
            req.Headers().Add(outboundHeaders);
        }

        return req;
    }

    // CLR: package access to the protected newInitialMessage(ctx) for HttpProxyHandlerTest (see the class comment).
    internal object InvokeNewInitialMessage(IChannelHandlerContext ctx)
    {
        return NewInitialMessage(ctx);
    }

    protected override bool HandleResponse(IChannelHandlerContext ctx, object response)
    {
        if (response is IHttpResponse)
        {
            if (status != null)
            {
                throw new HttpProxyConnectException(ExceptionMessage("too many responses"), /*headers=*/ null);
            }
            IHttpResponse res = (IHttpResponse) response;
            status = res.Status();
            inboundHeaders = res.Headers();
        }

        bool finished = response is ILastHttpContent;
        if (finished)
        {
            if (status == null)
            {
                throw new HttpProxyConnectException(ExceptionMessage("missing response"), inboundHeaders);
            }
            if (status.Code() != 200)
            {
                throw new HttpProxyConnectException(ExceptionMessage("status: " + status), inboundHeaders);
            }
        }

        return finished;
    }

    /**
     * Specific case of a connection failure, which may include headers from the proxy.
     */
    // CLR: serialVersionUID is JVM serialization metadata and has no counterpart.
    public sealed class HttpProxyConnectException : ProxyConnectException
    {
        private readonly HttpHeaders headers;

        /**
         * @param message The failure message.
         * @param headers Header associated with the connection failure.  May be {@code null}.
         */
        public HttpProxyConnectException(string message, HttpHeaders headers) : base(message)
        {
            this.headers = headers;
        }

        /**
         * Returns headers, if any.  May be {@code null}.
         */
        public HttpHeaders Headers()
        {
            return headers;
        }
    }

    // CLR: ChannelInboundHandler and ChannelOutboundHandler are IChannelInboundHandler and IChannelOutboundHandler; the
    // ChannelPromise parameters are the Tasks the codec returns (see ProxyHandler).
    private sealed class HttpClientCodecWrapper : IChannelInboundHandler, IChannelOutboundHandler
    {
        internal readonly HttpClientCodec codec = new HttpClientCodec();

        public void HandlerAdded(IChannelHandlerContext ctx)
        {
            codec.HandlerAdded(ctx);
        }

        public void HandlerRemoved(IChannelHandlerContext ctx)
        {
            codec.HandlerRemoved(ctx);
        }

        public void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            codec.ExceptionCaught(ctx, cause);
        }

        public void ChannelRegistered(IChannelHandlerContext ctx)
        {
            codec.ChannelRegistered(ctx);
        }

        public void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            codec.ChannelUnregistered(ctx);
        }

        public void ChannelActive(IChannelHandlerContext ctx)
        {
            codec.ChannelActive(ctx);
        }

        public void ChannelInactive(IChannelHandlerContext ctx)
        {
            codec.ChannelInactive(ctx);
        }

        public void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            codec.ChannelRead(ctx, msg);
        }

        public void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            codec.ChannelReadComplete(ctx);
        }

        public void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            codec.UserEventTriggered(ctx, evt);
        }

        public void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            codec.ChannelWritabilityChanged(ctx);
        }

        public Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress,
                         CancellationToken cancellationToken)
        {
            return codec.BindAsync(ctx, localAddress, cancellationToken);
        }

        public Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                            CancellationToken cancellationToken)
        {
            return codec.ConnectAsync(ctx, remoteAddress, localAddress, cancellationToken);
        }

        public Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
        {
            return codec.DisconnectAsync(ctx, cancellationToken);
        }

        public Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
        {
            return codec.CloseAsync(ctx, cancellationToken);
        }

        public Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
        {
            return codec.DeregisterAsync(ctx, cancellationToken);
        }

        public void Read(IChannelHandlerContext ctx)
        {
            codec.Read(ctx);
        }

        public Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            return codec.WriteAsync(ctx, msg, cancellationToken);
        }

        public void Flush(IChannelHandlerContext ctx)
        {
            codec.Flush(ctx);
        }
    }
}
