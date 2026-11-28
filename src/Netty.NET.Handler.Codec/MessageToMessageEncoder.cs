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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

/**
 * {@link ChannelOutboundHandlerAdapter} which encodes from one message to an other message
 *
 * For example here is an implementation which decodes an {@link Integer} to an {@link String}.
 *
 * <pre>
 *     public class IntegerToStringEncoder extends
 *             {@link MessageToMessageEncoder}&lt;{@link Integer}&gt; {
 *
 *         {@code @Override}
 *         public void encode({@link ChannelHandlerContext} ctx, {@link Integer} message, List&lt;Object&gt; out)
 *                 throws {@link Exception} {
 *             out.add(message.toString());
 *         }
 *     }
 * </pre>
 *
 * Be aware that you need to call {@link ReferenceCounted#retain()} on messages that are just passed through if they
 * are of type {@link ReferenceCounted}. This is needed as the {@link MessageToMessageEncoder} will call
 * {@link ReferenceCounted#release()} on encoded messages.
 */
// CLR: write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose returned Task replaces the promise.
public abstract class MessageToMessageEncoder<I> : ChannelOutboundHandlerAdapter
{
    // CLR: the reified type argument and Type.IsInstanceOfType replace TypeParameterMatcher.find/get, as in
    // SimpleChannelInboundHandler (https://learn.microsoft.com/dotnet/api/system.type.isinstanceoftype).
    private readonly Type matcher;

    /**
     * Create a new instance which will try to detect the types to match out of the type parameter of the class.
     */
    protected MessageToMessageEncoder()
    {
        matcher = typeof(I);
    }

    /**
     * Create a new instance
     *
     * @param outboundMessageType   The type of messages to match and so encode
     */
    protected MessageToMessageEncoder(Type outboundMessageType)
    {
        matcher = MessageToByteEncoder<I>.CheckMessageType(outboundMessageType, typeof(I), "outboundMessageType");
    }

    /**
     * Returns {@code true} if the given message should be handled. If {@code false} it will be passed to the next
     * {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     */
    public virtual bool AcceptOutboundMessage(object msg)
    {
        return matcher.IsInstanceOfType(msg);
    }

    // CLR: as in the source, messages already added to 'out' are written in the finally block even when encoding then
    // fails; the exception propagates and the pipeline fails the returned Task, while those writes' own Tasks are
    // dropped (the source writes them with the promise that the pipeline then fails).
    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        CodecOutputList @out = null;
        Task future = null;

        try
        {
            if (AcceptOutboundMessage(msg))
            {
                @out = CodecOutputList.NewInstance();
                I cast = (I) msg;

                try
                {
                    Encode(ctx, cast, @out);
                }
                catch (Exception)
                {
                    ReferenceCountUtil.SafeRelease(cast);
                    // CLR: PlatformDependent.throwException(th) rethrows the original exception.
                    throw;
                }

                ReferenceCountUtil.Release(cast);
                if (@out.Count == 0)
                {
                    throw new EncoderException(
                            StringUtil.SimpleClassName(this) + " must produce at least one message.");
                }
            }
            else
            {
                future = ctx.WriteAsync(msg, cancellationToken);
            }
        }
        catch (EncoderException)
        {
            throw;
        }
        catch (Exception t)
        {
            throw new EncoderException(t);
        }
        finally
        {
            if (@out != null)
            {
                try
                {
                    int sizeMinusOne = @out.Count - 1;
                    if (sizeMinusOne == 0)
                    {
                        future = ctx.WriteAsync(@out.GetUnsafe(0), cancellationToken);
                    }
                    else if (sizeMinusOne > 0)
                    {
                        // Check if we can use a voidPromise for our extra writes to reduce GC-Pressure
                        // See https://github.com/netty/netty/issues/2525
                        // CLR: there is no void promise (a caller that ignores the result discards the Task), so
                        // writeVoidPromise has no counterpart and the combined write is always used.
                        future = WritePromiseCombiner(ctx, @out);
                    }
                }
                finally
                {
                    @out.Recycle();
                }
            }
        }

        return future;
    }

    // CLR: new PromiseCombiner(ctx.executor()) with add(...) and finish(promise) is ExecutorPromiseCombiner.Combine,
    // whose returned Task replaces the promise: it completes on the event loop when the last write completes, as the
    // Java combiner does (a Task.WhenAll of the Transport write Tasks completed later on a thread-pool thread). As with
    // ctx.write(msg), each write gets its own Task and no cancellation token.
    private static Task WritePromiseCombiner(IChannelHandlerContext ctx, CodecOutputList @out)
    {
        Task[] futures = new Task[@out.Count];

        for (int i = 0; i < @out.Count; i++)
        {
            futures[i] = ctx.WriteAsync(@out.GetUnsafe(i));
        }

        return ExecutorPromiseCombiner.Combine(ctx, futures);
    }

    /**
     * Encode from one message to an other. This method will be called for each written message that can be handled
     * by this encoder.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this {@link MessageToMessageEncoder} belongs to
     * @param msg           the message to encode to an other one
     * @param out           the {@link List} into which the encoded msg should be added
     *                      needs to do some kind of aggregation
     * @throws Exception    is thrown if an error occurs
     */
    protected abstract void Encode(IChannelHandlerContext ctx, I msg, IList<object> @out);

    // CLR: Java protected also admits the package, and DatagramPacketEncoder calls the wrapped encoder's
    // encode(...) through it. C# protected access needs an instance of the calling class (CS1540), and a protected
    // internal Encode would make every override in a friend assembly repeat protected internal (CS0507), so the
    // package-level call goes through this internal forwarder
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/protected-internal).
    internal void InvokeEncode(IChannelHandlerContext ctx, I msg, IList<object> @out)
    {
        Encode(ctx, msg, @out);
    }
}
