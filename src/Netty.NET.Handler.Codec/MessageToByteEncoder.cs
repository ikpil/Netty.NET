/*
 * Copyright 2012 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

/**
 * {@link ChannelOutboundHandlerAdapter} which encodes message in a stream-like fashion from one message to an
 * {@link ByteBuf}.
 *
 *
 * Example implementation which encodes {@link Integer}s to a {@link ByteBuf}.
 *
 * <pre>
 *     public class IntegerEncoder extends {@link MessageToByteEncoder}&lt;{@link Integer}&gt; {
 *         {@code @Override}
 *         public void encode({@link ChannelHandlerContext} ctx, {@link Integer} msg, {@link ByteBuf} out)
 *                 throws {@link Exception} {
 *             out.writeInt(msg);
 *         }
 *     }
 * </pre>
 */
// CLR: write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose returned Task replaces the promise
// (as in ChannelOutboundHandlerAdapter); ctx.write(m, promise) is returned as ctx.WriteAsync(m, cancellationToken).
public abstract class MessageToByteEncoder<I> : ChannelOutboundHandlerAdapter
{
    // CLR: the reified type argument and Type.IsInstanceOfType replace TypeParameterMatcher.find/get, as in
    // SimpleChannelInboundHandler (https://learn.microsoft.com/dotnet/api/system.type.isinstanceoftype).
    private readonly Type matcher;
    private readonly bool preferDirect;

    /**
     * see {@link #MessageToByteEncoder(boolean)} with {@code true} as boolean parameter.
     */
    protected MessageToByteEncoder() : this(true)
    {
    }

    /**
     * see {@link #MessageToByteEncoder(Class, boolean)} with {@code true} as boolean value.
     */
    protected MessageToByteEncoder(Type outboundMessageType) : this(outboundMessageType, true)
    {
    }

    /**
     * Create a new instance which will try to detect the types to match out of the type parameter of the class.
     *
     * @param preferDirect          {@code true} if a direct {@link ByteBuf} should be tried to be used as target for
     *                              the encoded messages. If {@code false} is used it will allocate a heap
     *                              {@link ByteBuf}, which is backed by an byte array.
     */
    protected MessageToByteEncoder(bool preferDirect)
    {
        matcher = typeof(I);
        this.preferDirect = preferDirect;
    }

    /**
     * Create a new instance
     *
     * @param outboundMessageType   The type of messages to match
     * @param preferDirect          {@code true} if a direct {@link ByteBuf} should be tried to be used as target for
     *                              the encoded messages. If {@code false} is used it will allocate a heap
     *                              {@link ByteBuf}, which is backed by an byte array.
     */
    protected MessageToByteEncoder(Type outboundMessageType, bool preferDirect)
    {
        matcher = CheckMessageType(outboundMessageType, typeof(I), "outboundMessageType");
        this.preferDirect = preferDirect;
    }

    // CLR: Class<? extends I> is checked at run time, as in SimpleChannelInboundHandler.
    internal static Type CheckMessageType(Type messageType, Type bound, string name)
    {
        ArgumentNullException.ThrowIfNull(messageType, name);
        if (!bound.IsAssignableFrom(messageType))
        {
            throw new ArgumentException($"{messageType} is not assignable to {bound}", name);
        }

        return messageType;
    }

    /**
     * Returns {@code true} if the given message should be handled. If {@code false} it will be passed to the next
     * {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     */
    public virtual bool AcceptOutboundMessage(object msg)
    {
        return matcher.IsInstanceOfType(msg);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        ByteBuf buf = null;

        try
        {
            if (AcceptOutboundMessage(msg))
            {
                I cast = (I) msg;
                buf = AllocateBuffer(ctx, cast, preferDirect);

                try
                {
                    Encode(ctx, cast, buf);
                }
                finally
                {
                    ReferenceCountUtil.Release(cast);
                }

                Task future;

                if (buf.IsReadable())
                {
                    future = ctx.WriteAsync(buf, cancellationToken);
                }
                else
                {
                    buf.Release();
                    future = ctx.WriteAsync(Unpooled.EmptyBuffer, cancellationToken);
                }

                buf = null;
                return future;
            }
            else
            {
                return ctx.WriteAsync(msg, cancellationToken);
            }
        }
        catch (EncoderException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new EncoderException(e);
        }
        finally
        {
            if (buf != null)
            {
                buf.Release();
            }
        }
    }

    /**
     * Allocate a {@link ByteBuf} which will be used as argument of {@link #encode(ChannelHandlerContext, I, ByteBuf)}.
     * Sub-classes may override this method to return {@link ByteBuf} with a perfect matching {@code initialCapacity}.
     */
    protected virtual ByteBuf AllocateBuffer(IChannelHandlerContext ctx, I msg,
                               bool preferDirect)
    {
        if (preferDirect)
        {
            return ctx.Alloc().IoBuffer();
        }
        else
        {
            return ctx.Alloc().HeapBuffer();
        }
    }

    /**
     * Encode a message into a {@link ByteBuf}. This method will be called for each written message that can be handled
     * by this encoder.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this {@link MessageToByteEncoder} belongs to
     * @param msg           the message to encode
     * @param out           the {@link ByteBuf} into which the encoded message will be written
     * @throws Exception    is thrown if an error occurs
     */
    protected abstract void Encode(IChannelHandlerContext ctx, I msg, ByteBuf @out);

    protected bool IsPreferDirect()
    {
        return preferDirect;
    }
}
