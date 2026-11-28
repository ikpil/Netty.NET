/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Testsuite.Http2;

/**
 * Sets up the Netty pipeline for the example server. Depending on the endpoint config, sets up the
 * pipeline for cleartext HTTP upgrade to HTTP/2.
 */
// CLR: SocketChannel is ISocketChannel, HttpMessage IHttpMessage and UpgradeCodecFactory/UpgradeCodec the nested
// HttpServerUpgradeHandler.IUpgradeCodecFactory/IUpgradeCodec, as in codec-http. The public initChannel(SocketChannel)
// widens the protected ChannelInitializer.initChannel; a C# override cannot change the accessibility of the virtual
// method, so it stays protected. The anonymous UpgradeCodecFactory is the nested CleartextUpgradeCodecFactory and the
// anonymous SimpleChannelInboundHandler<HttpMessage> is the nested NoUpgradeAttemptedHandler, which takes
// maxHttpContentLength from the initializer (an anonymous class reads the field of the enclosing instance).
// System.err.println/System.out.println are Console.Error.WriteLine/Console.WriteLine. The package-private
// constructors are internal.
public class Http2ServerInitializer : ChannelInitializer<ISocketChannel>
{

    private static readonly HttpServerUpgradeHandler.IUpgradeCodecFactory upgradeCodecFactory =
            new CleartextUpgradeCodecFactory();

    private readonly int maxHttpContentLength;

    internal Http2ServerInitializer()
        : this(16 * 1024)
    {
    }

    internal Http2ServerInitializer(int maxHttpContentLength)
    {
        CheckPositiveOrZero(maxHttpContentLength, "maxHttpContentLength");

        this.maxHttpContentLength = maxHttpContentLength;
    }

    protected override void InitChannel(ISocketChannel ch)
    {
        ConfigureClearText(ch);
    }

    /**
     * Configure the pipeline for a cleartext upgrade from HTTP to HTTP/2.0
     */
    private void ConfigureClearText(ISocketChannel ch)
    {
        IChannelPipeline p = ch.Pipeline;
        HttpServerCodec sourceCodec = new HttpServerCodec();
        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(sourceCodec, upgradeCodecFactory);
        CleartextHttp2ServerUpgradeHandler cleartextHttp2ServerUpgradeHandler =
                new CleartextHttp2ServerUpgradeHandler(sourceCodec, upgradeHandler,
                                                       new HelloWorldHttp2HandlerBuilder().Build());

        p.AddLast(cleartextHttp2ServerUpgradeHandler);
        p.AddLast(new NoUpgradeAttemptedHandler(maxHttpContentLength));

        p.AddLast(new UserEventLogger());
    }

    private sealed class CleartextUpgradeCodecFactory : HttpServerUpgradeHandler.IUpgradeCodecFactory
    {
        public HttpServerUpgradeHandler.IUpgradeCodec NewUpgradeCodec(ICharSequence protocol)
        {
            if (AsciiString.ContentEquals(Http2CodecUtil.HTTP_UPGRADE_PROTOCOL_NAME, protocol))
            {
                return new Http2ServerUpgradeCodec(new HelloWorldHttp2HandlerBuilder().Build());
            }
            else
            {
                return null;
            }
        }
    }

    private sealed class NoUpgradeAttemptedHandler : SimpleChannelInboundHandler<IHttpMessage>
    {
        private readonly int maxHttpContentLength;

        internal NoUpgradeAttemptedHandler(int maxHttpContentLength)
        {
            this.maxHttpContentLength = maxHttpContentLength;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpMessage msg)
        {
            // If this handler is hit then no upgrade has been attempted and the client is just talking HTTP.
            Console.Error.WriteLine("Directly talking: " + msg.ProtocolVersion() + " (no upgrade was attempted)");
            IChannelPipeline pipeline = ctx.Pipeline;
            IChannelHandlerContext thisCtx = pipeline.Context(this);
            pipeline.AddAfter(thisCtx.Name, null, new HelloWorldHttp1Handler("Direct. No Upgrade Attempted."));
            pipeline.Replace(this, null, new HttpObjectAggregator(maxHttpContentLength));
            ctx.FireChannelRead(ReferenceCountUtil.Retain(msg));
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
