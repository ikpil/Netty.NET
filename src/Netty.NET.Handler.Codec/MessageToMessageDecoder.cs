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
using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

/**
 * {@link ChannelInboundHandlerAdapter} which decodes from one message to an other message.
 *
 *
 * For example here is an implementation which decodes a {@link String} to an {@link Integer} which represent
 * the length of the {@link String}.
 *
 * <pre>
 *     public class StringToIntegerDecoder extends
 *             {@link MessageToMessageDecoder}&lt;{@link String}&gt; {
 *
 *         {@code @Override}
 *         public void decode({@link ChannelHandlerContext} ctx, {@link String} message,
 *                            List&lt;Object&gt; out) throws {@link Exception} {
 *             out.add(message.length());
 *         }
 *     }
 * </pre>
 *
 * Be aware that you need to call {@link ReferenceCounted#retain()} on messages that are just passed through if they
 * are of type {@link ReferenceCounted}. This is needed as the {@link MessageToMessageDecoder} will call
 * {@link ReferenceCounted#release()} on decoded messages.
 *
 */
public abstract class MessageToMessageDecoder<I> : ChannelInboundHandlerAdapter
{
    // CLR: the reified type argument and Type.IsInstanceOfType replace TypeParameterMatcher.find/get, as in
    // SimpleChannelInboundHandler (https://learn.microsoft.com/dotnet/api/system.type.isinstanceoftype).
    private readonly Type matcher;
    private bool decodeCalled;
    private bool messageProduced;

    /**
     * Create a new instance which will try to detect the types to match out of the type parameter of the class.
     */
    protected MessageToMessageDecoder()
    {
        matcher = typeof(I);
    }

    /**
     * Create a new instance
     *
     * @param inboundMessageType    The type of messages to match and so decode
     */
    protected MessageToMessageDecoder(Type inboundMessageType)
    {
        matcher = MessageToByteEncoder<I>.CheckMessageType(inboundMessageType, typeof(I), "inboundMessageType");
    }

    /**
     * Returns {@code true} if the given message should be handled. If {@code false} it will be passed to the next
     * {@link ChannelInboundHandler} in the {@link ChannelPipeline}.
     */
    public virtual bool AcceptInboundMessage(object msg)
    {
        return matcher.IsInstanceOfType(msg);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        decodeCalled = true;
        CodecOutputList @out = CodecOutputList.NewInstance();
        try
        {
            if (AcceptInboundMessage(msg))
            {
                I cast = (I) msg;
                try
                {
                    Decode(ctx, cast, @out);
                }
                finally
                {
                    ReferenceCountUtil.Release(cast);
                }
            }
            else
            {
                @out.Add(msg);
            }
        }
        catch (DecoderException)
        {
            throw;
        }
        // CLR: Signal is an Exception here but a java.lang.Error in the source, which catch (Exception) does not
        // wrap; the filter keeps it out, as in ReplayingDecoder.
        catch (Exception e) when (e is not Signal)
        {
            throw new DecoderException(e);
        }
        finally
        {
            try
            {
                int size = @out.Count;
                messageProduced |= size > 0;
                for (int i = 0; i < size; i++)
                {
                    ctx.FireChannelRead(@out.GetUnsafe(i));
                }
            }
            finally
            {
                @out.Recycle();
            }
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        if (!IsSharable)
        {
            // Only use local vars if this decoder is not sharable as otherwise this is not safe to do.
            if (decodeCalled && !messageProduced && !ctx.Channel.Config.IsAutoRead())
            {
                ctx.Read();
            }
            decodeCalled = false;
            messageProduced = false;
        }
        ctx.FireChannelReadComplete();
    }

    /**
     * Decode from one message to an other. This method will be called for each written message that can be handled
     * by this decoder.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this {@link MessageToMessageDecoder} belongs to
     * @param msg           the message to decode to an other one
     * @param out           the {@link List} to which decoded messages should be added
     * @throws Exception    is thrown if an error occurs
     */
    protected abstract void Decode(IChannelHandlerContext ctx, I msg, IList<object> @out);

    // CLR: Java protected also admits the package, and DatagramPacketDecoder calls the wrapped decoder's
    // decode(...) through it. C# protected access needs an instance of the calling class (CS1540), and a protected
    // internal Decode would make every override in a friend assembly repeat protected internal (CS0507), so the
    // package-level call goes through this internal forwarder
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/protected-internal).
    internal void InvokeDecode(IChannelHandlerContext ctx, I msg, IList<object> @out)
    {
        Decode(ctx, msg, @out);
    }
}
