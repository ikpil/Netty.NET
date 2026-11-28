/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */
using System;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Logging.LogLevel;

namespace Netty.NET.Example.Http2.HelloWorld.Client;

/**
 * Configures the client pipeline to support HTTP/2 frames.
 */
// CLR: SocketChannel is ISocketChannel, Http2Connection IHttp2Connection, ChannelPipeline IChannelPipeline, as in
// codec-http2. The public initChannel(SocketChannel) widens the protected ChannelInitializer.initChannel; a C# override
// cannot change the accessibility of the virtual method, so it stays protected. ch.newPromise() is a TaskCompletionSource
// whose continuations run asynchronously, so the awaiting Main does not continue on the event loop when the settings
// arrive (a ChannelPromise notifies its listeners on the channel's executor). The anonymous
// ApplicationProtocolNegotiationHandler is the nested Http2NegotiationHandler, which reaches the initializer's
// connectionHandler and configureEndOfPipeline as the anonymous class does through its enclosing instance; ctx.close()
// is CloseAsync(), whose Task is discarded, and IllegalStateException is InvalidOperationException. In
// UpgradeRequestHandler, InetSocketAddress is the EndPoint of the channel: getHostString() is the host of a DnsEndPoint
// or null, and getAddress().getHostAddress()/getPort() are the IPEndPoint's Address and Port.
// System.out.println is Console.WriteLine.
public class Http2ClientInitializer : ChannelInitializer<ISocketChannel>
{
    private static readonly Http2FrameLogger logger = new Http2FrameLogger(INFO, typeof(Http2ClientInitializer));

    private readonly SslContext sslCtx;
    private readonly int maxContentLength;
    private HttpToHttp2ConnectionHandler connectionHandler;
    private HttpResponseHandler responseHandler;
    private Http2SettingsHandler settingsHandler;

    public Http2ClientInitializer(SslContext sslCtx, int maxContentLength)
    {
        this.sslCtx = sslCtx;
        this.maxContentLength = maxContentLength;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        IHttp2Connection connection = new DefaultHttp2Connection(false);
        connectionHandler = new HttpToHttp2ConnectionHandlerBuilder()
                .FrameListener(new DelegatingDecompressorFrameListener(
                        connection,
                        new InboundHttp2ToHttpAdapterBuilder(connection)
                                .MaxContentLength(maxContentLength)
                                .PropagateSettings(true)
                                .Build(),
                        maxContentLength))
                .FrameLogger(logger)
                .Connection(connection)
                .Build();
        responseHandler = new HttpResponseHandler();
        settingsHandler = new Http2SettingsHandler(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        if (sslCtx != null)
        {
            ConfigureSsl(ch);
        }
        else
        {
            ConfigureClearText(ch);
        }
    }

    public HttpResponseHandler ResponseHandler()
    {
        return responseHandler;
    }

    public Http2SettingsHandler SettingsHandler()
    {
        return settingsHandler;
    }

    protected void ConfigureEndOfPipeline(IChannelPipeline pipeline)
    {
        pipeline.AddLast(settingsHandler, responseHandler);
    }

    /**
     * Configure the pipeline for TLS NPN negotiation to HTTP/2.
     */
    private void ConfigureSsl(ISocketChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;
        // Specify Host in SSLContext New Handler to add TLS SNI Extension
        pipeline.AddLast(sslCtx.NewHandler(ch.Alloc(), Http2Client.HOST, Http2Client.PORT));
        // We must wait for the handshake to finish and the protocol to be negotiated before configuring
        // the HTTP/2 components of the pipeline.
        pipeline.AddLast(new Http2NegotiationHandler(this));
    }

    private sealed class Http2NegotiationHandler : ApplicationProtocolNegotiationHandler
    {
        private readonly Http2ClientInitializer initializer;

        internal Http2NegotiationHandler(Http2ClientInitializer initializer)
            : base("")
        {
            this.initializer = initializer;
        }

        protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
        {
            if (ApplicationProtocolNames.HTTP_2.Equals(protocol))
            {
                IChannelPipeline p = ctx.Pipeline;
                p.AddLast(initializer.connectionHandler);
                initializer.ConfigureEndOfPipeline(p);
                return;
            }

            _ = ctx.CloseAsync();
            throw new InvalidOperationException("unknown protocol: " + protocol);
        }
    }

    /**
     * Configure the pipeline for a cleartext upgrade from HTTP to HTTP/2.
     */
    private void ConfigureClearText(ISocketChannel ch)
    {
        HttpClientCodec sourceCodec = new HttpClientCodec();
        Http2ClientUpgradeCodec upgradeCodec = new Http2ClientUpgradeCodec(connectionHandler);
        HttpClientUpgradeHandler upgradeHandler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec,
                maxContentLength);

        ch.Pipeline.AddLast(sourceCodec,
                              upgradeHandler,
                              new UpgradeRequestHandler(this),
                              new UserEventLogger());
    }

    /**
     * A handler that triggers the cleartext upgrade to HTTP/2 by sending an initial HTTP request.
     */
    // CLR: the inner (non-static) class takes its enclosing initializer explicitly.
    private sealed class UpgradeRequestHandler : ChannelInboundHandlerAdapter
    {
        private readonly Http2ClientInitializer initializer;

        internal UpgradeRequestHandler(Http2ClientInitializer initializer)
        {
            this.initializer = initializer;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            DefaultFullHttpRequest upgradeRequest =
                    new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/", Unpooled.EmptyBuffer);

            // Set HOST header as the remote peer may require it.
            System.Net.EndPoint remote = ctx.Channel.RemoteAddress;
            string hostString = (remote as System.Net.DnsEndPoint)?.Host;
            if (hostString == null)
            {
                hostString = ((System.Net.IPEndPoint) remote).Address.ToString();
            }

            int port = remote is System.Net.DnsEndPoint dnsEndPoint ? dnsEndPoint.Port
                    : ((System.Net.IPEndPoint) remote).Port;
            upgradeRequest.Headers().Set(HttpHeaderNames.HOST, hostString + ':' + port);

            _ = ctx.WriteAndFlushAsync(upgradeRequest);

            ctx.FireChannelActive();

            // Done with this handler, remove it from the pipeline.
            ctx.Pipeline.Remove(this);

            initializer.ConfigureEndOfPipeline(ctx.Pipeline);
        }
    }

    /**
     * Class that logs any User Events triggered on this channel.
     */
    private class UserEventLogger : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            Console.WriteLine("User Event Triggered: " + evt);
            ctx.FireUserEventTriggered(evt);
        }
    }
}
