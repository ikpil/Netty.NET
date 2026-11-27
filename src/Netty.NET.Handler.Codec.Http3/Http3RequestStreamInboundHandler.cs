/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * {@link ChannelInboundHandlerAdapter} which makes it easy to handle
 * <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7">HTTP3 request streams</a>.
 */
// CLR: the overloads channelRead(ctx, Http3HeadersFrame)/(ctx, Http3DataFrame)/(ctx, Http3UnknownFrame) keep the Java
// name next to the sealed override of ChannelRead(ctx, object) (the public final channelRead); the frame types are the
// I-prefixed interfaces. QuicException is codec-quic's QuicException (not System.Net.Quic's) and QuicStreamChannel
// IQuicStreamChannel; Throwable is Exception. evt == ChannelInputShutdownEvent.INSTANCE is a reference comparison. The
// logger's trailing exception argument is the cause, as in Java. "throws Exception" has no counterpart; the
// @SuppressWarnings("unused") and @Nullable (org.jetbrains.annotations) annotations have no counterpart. The protected
// final controlStream is a non-virtual protected method.
public abstract class Http3RequestStreamInboundHandler : ChannelInboundHandlerAdapter
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(Http3RequestStreamInboundHandler));

    public sealed override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttp3UnknownFrame)
        {
            ChannelRead(ctx, (IHttp3UnknownFrame) msg);
        }
        else if (msg is IHttp3HeadersFrame)
        {
            ChannelRead(ctx, (IHttp3HeadersFrame) msg);
        }
        else if (msg is IHttp3DataFrame)
        {
            ChannelRead(ctx, (IHttp3DataFrame) msg);
        }
        else
        {
            base.ChannelRead(ctx, msg);
        }
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE))
        {
            ChannelInputClosed(ctx);
        }
        ctx.FireUserEventTriggered(evt);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (cause is QuicException)
        {
            HandleQuicException(ctx, (QuicException) cause);
        }
        else if (cause is Http3Exception)
        {
            HandleHttp3Exception(ctx, (Http3Exception) cause);
        }
        else
        {
            ctx.FireExceptionCaught(cause);
        }
    }

    /**
     * Called once a {@link Http3HeadersFrame} is ready for this stream to process.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @param frame         the {@link Http3HeadersFrame} that was read
     * @throws Exception    thrown if an error happens during processing.
     */
    protected abstract void ChannelRead(IChannelHandlerContext ctx, IHttp3HeadersFrame frame);

    /**
     * Called once a {@link Http3DataFrame} is ready for this stream to process.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @param frame         the {@link Http3DataFrame} that was read
     * @throws Exception    thrown if an error happens during processing.
     */
    protected abstract void ChannelRead(IChannelHandlerContext ctx, IHttp3DataFrame frame);

    /**
     * Called once the input is closed and so no more inbound data is received on it.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @throws Exception    thrown if an error happens during processing.
     */
    protected abstract void ChannelInputClosed(IChannelHandlerContext ctx);

    /**
     * Called once a {@link Http3UnknownFrame} is ready for this stream to process. By default these frames are just
     * released and so dropped on the floor as stated in the RFC. That said you may want to override this method if
     * you use some custom frames which are not part of the main spec.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @param frame         the {@link Http3UnknownFrame} that was read
     */
    protected virtual void ChannelRead(IChannelHandlerContext ctx, IHttp3UnknownFrame frame)
    {
        frame.Release();
    }

    /**
     * Called once a {@link QuicException} should be handled.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @param exception     the {@link QuicException} that caused the error.
     */
    protected virtual void HandleQuicException(IChannelHandlerContext ctx, QuicException exception)
    {
        logger.Debug("Caught QuicException on channel {}", ctx.Channel, exception);
    }

    /**
     * Called once a {@link Http3Exception} should be handled.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @param exception     the {@link Http3Exception} that caused the error.
     */
    protected virtual void HandleHttp3Exception(IChannelHandlerContext ctx,
                                                Http3Exception exception)
    {
        logger.Error("Caught Http3Exception on channel {}", ctx.Channel, exception);
    }

    /**
     * Return the local control stream for this HTTP/3 connection. This can be used to send
     * {@link Http3ControlStreamFrame}s to the remote peer.
     *
     * @param ctx           the {@link ChannelHandlerContext} of this handler.
     * @return              the control stream.
     */
    protected IQuicStreamChannel ControlStream(IChannelHandlerContext ctx)
    {
        return Http3.GetLocalControlStream(ctx.Channel.Parent);
    }
}
