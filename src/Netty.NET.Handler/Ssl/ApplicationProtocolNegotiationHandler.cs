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
using System.Security.Authentication;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Ssl;

/**
 * Configures a {@link ChannelPipeline} depending on the application-level protocol negotiation result of
 * {@link SslHandler}.  For example, you could configure your HTTP pipeline depending on the result of ALPN:
 * <pre>
 * public class MyInitializer extends {@link ChannelInitializer}&lt;{@link Channel}&gt; {
 *     private final {@link SslContext} sslCtx;
 *
 *     public MyInitializer({@link SslContext} sslCtx) {
 *         this.sslCtx = sslCtx;
 *     }
 *
 *     protected void initChannel({@link Channel} ch) {
 *         {@link ChannelPipeline} p = ch.pipeline();
 *         p.addLast(sslCtx.newHandler(...)); // Adds {@link SslHandler}
 *         p.addLast(new MyNegotiationHandler());
 *     }
 * }
 *
 * public class MyNegotiationHandler extends {@link ApplicationProtocolNegotiationHandler} {
 *     public MyNegotiationHandler() {
 *         super({@link ApplicationProtocolNames}.HTTP_1_1);
 *     }
 *
 *     protected void configurePipeline({@link ChannelHandlerContext} ctx, String protocol) {
 *         if ({@link ApplicationProtocolNames}.HTTP_2.equals(protocol) {
 *             configureHttp2(ctx);
 *         } else if ({@link ApplicationProtocolNames}.HTTP_1_1.equals(protocol)) {
 *             configureHttp1(ctx);
 *         } else {
 *             throw new IllegalStateException("unknown protocol: " + protocol);
 *         }
 *     }
 * }
 * </pre>
 */
// CLR: SSLException is AuthenticationException (ssl design decision 3, docs/handler-ssl-design.md), so
// exceptionCaught(...) checks a DecoderException whose InnerException is an AuthenticationException. Throwable is
// Exception and IllegalStateException is InvalidOperationException (docs/codec-base-porting.md); ctx.close() discards the
// Task of CloseAsync(); RecyclableArrayList is the Common port, whose recycle() is Recycle(). The checked
// "throws Exception" clauses have no counterpart.
public abstract class ApplicationProtocolNegotiationHandler : ChannelInboundHandlerAdapter
{

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(ApplicationProtocolNegotiationHandler));

    private readonly string fallbackProtocol;
    private readonly RecyclableArrayList bufferedMessages = RecyclableArrayList.NewInstance();
    private IChannelHandlerContext ctx;
    private bool sslHandlerChecked;

    /**
     * Creates a new instance with the specified fallback protocol name.
     *
     * @param fallbackProtocol the name of the protocol to use when
     *                         ALPN/NPN negotiation fails or the client does not support ALPN/NPN
     */
    protected ApplicationProtocolNegotiationHandler(string fallbackProtocol)
    {
        this.fallbackProtocol = ObjectUtil.CheckNotNull(fallbackProtocol, "fallbackProtocol");
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        base.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        FireBufferedMessages();
        bufferedMessages.Recycle();
        base.HandlerRemoved(ctx);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        // Let's buffer all data until this handler will be removed from the pipeline.
        bufferedMessages.Add(msg);
        if (!sslHandlerChecked)
        {
            sslHandlerChecked = true;
            if (ctx.Pipeline.Get<SslHandler>() == null)
            {
                // Just remove ourself if there is no SslHandler in the pipeline and so we would otherwise
                // buffer forever.
                RemoveSelfIfPresent(ctx);
            }
        }
    }

    /**
     * Process all backlog into pipeline from List.
     */
    private void FireBufferedMessages()
    {
        if (bufferedMessages.Count != 0)
        {
            for (int i = 0; i < bufferedMessages.Count; i++)
            {
                ctx.FireChannelRead(bufferedMessages[i]);
            }
            ctx.FireChannelReadComplete();
            bufferedMessages.Clear();
        }
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is SslHandshakeCompletionEvent)
        {
            SslHandshakeCompletionEvent handshakeEvent = (SslHandshakeCompletionEvent) evt;
            try
            {
                if (handshakeEvent.IsSuccess())
                {
                    SslHandler sslHandler = ctx.Pipeline.Get<SslHandler>();
                    if (sslHandler == null)
                    {
                        throw new InvalidOperationException("cannot find an SslHandler in the pipeline (required for "
                                + "application-level protocol negotiation)");
                    }
                    string protocol = sslHandler.ApplicationProtocol();
                    ConfigurePipeline(ctx, protocol != null ? protocol : fallbackProtocol);
                }
                else
                {
                    // if the event is not produced because of an successful handshake we will receive the same
                    // exception in exceptionCaught(...) and handle it there. This will allow us more fine-grained
                    // control over which exception we propagate down the ChannelPipeline.
                    //
                    // See https://github.com/netty/netty/issues/10342
                }
            }
            catch (Exception cause)
            {
                ExceptionCaught(ctx, cause);
            }
            finally
            {
                // Handshake failures are handled in exceptionCaught(...).
                if (handshakeEvent.IsSuccess())
                {
                    RemoveSelfIfPresent(ctx);
                }
            }
        }

        if (evt is ChannelInputShutdownEvent)
        {
            FireBufferedMessages();
        }

        ctx.FireUserEventTriggered(evt);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        FireBufferedMessages();
        base.ChannelInactive(ctx);
    }

    private void RemoveSelfIfPresent(IChannelHandlerContext ctx)
    {
        IChannelPipeline pipeline = ctx.Pipeline;
        if (!ctx.IsRemoved)
        {
            pipeline.Remove(this);
        }
    }

    /**
     * Invoked on successful initial SSL/TLS handshake. Implement this method to configure your pipeline
     * for the negotiated application-level protocol.
     *
     * @param protocol the name of the negotiated application-level protocol, or
     *                 the fallback protocol name specified in the constructor call if negotiation failed or the client
     *                 isn't aware of ALPN/NPN extension
     */
    protected abstract void ConfigurePipeline(IChannelHandlerContext ctx, string protocol);

    /**
     * Invoked on failed initial SSL/TLS handshake.
     */
    protected virtual void HandshakeFailure(IChannelHandlerContext ctx, Exception cause)
    {
        logger.Warn("{} TLS handshake failed:", ctx.Channel, cause);
        _ = ctx.CloseAsync();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Exception wrapped;
        if (cause is DecoderException && ((wrapped = cause.InnerException) is AuthenticationException))
        {
            try
            {
                HandshakeFailure(ctx, wrapped);
                return;
            }
            finally
            {
                RemoveSelfIfPresent(ctx);
            }
        }
        logger.Warn("{} Failed to select the application-level protocol:", ctx.Channel, cause);
        ctx.FireExceptionCaught(cause);
        _ = ctx.CloseAsync();
    }
}
