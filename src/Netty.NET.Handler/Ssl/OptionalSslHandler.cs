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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl;

/**
 * {@link OptionalSslHandler} is a utility decoder to support both SSL and non-SSL handlers
 * based on the first message received.
 */
// CLR: List<Object> out is IList<object> @out (codec-base). The SSLEngine is the handler's SslStream (owner decision
// 2026-11-24, docs/handler-ssl-design.md); the SSLParameters/SSLContext of the Javadoc are the options SslContext builds
// per handler.
public class OptionalSslHandler : ByteToMessageDecoder
{

    private readonly SslContext sslContext;

    public OptionalSslHandler(SslContext sslContext)
    {
        this.sslContext = ObjectUtil.CheckNotNull(sslContext, "sslContext");
    }

    protected override void Decode(IChannelHandlerContext context, ByteBuf @in, IList<object> @out)
    {
        if (@in.ReadableBytes < SslUtils.SSL_RECORD_HEADER_LENGTH)
        {
            return;
        }

        if (SslHandler.IsEncrypted(@in, false))
        {
            HandleSsl(context);
        }
        else
        {
            HandleNonSsl(context);
        }
    }

    // CLR: ReferenceCountUtil.safeRelease(sslHandler.engine()) disposes the SslStream engine of the handler (see
    // SniHandler.ReplaceHandler).
    private void HandleSsl(IChannelHandlerContext context)
    {
        SslHandler sslHandler = null;

        try
        {
            sslHandler = NewSslHandler(context, sslContext);
            context.Pipeline.Replace(this, NewSslHandlerName(), sslHandler);
            sslHandler = null;
        }
        finally
        {
            // Since the SslHandler was not inserted into the pipeline the ownership of the SSLEngine was not
            // transferred to the SslHandler.
            if (sslHandler != null)
            {
                sslHandler.Engine().Dispose();
            }
        }
    }

    private void HandleNonSsl(IChannelHandlerContext context)
    {
        IChannelHandler handler = NewNonSslHandler(context);
        if (handler != null)
        {
            context.Pipeline.Replace(this, NewNonSslHandlerName(), handler);
        }
        else
        {
            context.Pipeline.Remove(this);
        }
    }

    /**
     * Optionally specify the SSL handler name, this method may return {@code null}.
     * @return the name of the SSL handler.
     */
    protected virtual string NewSslHandlerName()
    {
        return null;
    }

    /**
     * Override to configure the SslHandler eg. {@link SSLParameters#setEndpointIdentificationAlgorithm(String)}.
     * The hostname and port is not known by this method so servers may want to override this method and use the
     * {@link SslContext#newHandler(ByteBufAllocator, String, int)} variant.
     *
     * @param context the {@link ChannelHandlerContext} to use.
     * @param sslContext the {@link SSLContext} to use.
     * @return the {@link SslHandler} which will replace the {@link OptionalSslHandler} in the pipeline if the
     * traffic is SSL.
     */
    protected virtual SslHandler NewSslHandler(IChannelHandlerContext context, SslContext sslContext)
    {
        return sslContext.NewHandler(context.Alloc());
    }

    /**
     * Optionally specify the non-SSL handler name, this method may return {@code null}.
     * @return the name of the non-SSL handler.
     */
    protected virtual string NewNonSslHandlerName()
    {
        return null;
    }

    /**
     * Override to configure the ChannelHandler.
     * @param context the {@link ChannelHandlerContext} to use.
     * @return the {@link ChannelHandler} which will replace the {@link OptionalSslHandler} in the pipeline
     * or {@code null} to simply remove the {@link OptionalSslHandler} if the traffic is non-SSL.
     */
    protected virtual IChannelHandler NewNonSslHandler(IChannelHandlerContext context)
    {
        return null;
    }
}
