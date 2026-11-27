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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * An abstract {@link ChannelHandler} that aggregates a series of message objects into a single aggregated message.
 * <p>
 * 'A series of messages' is composed of the following:
 * <ul>
 * <li>a single start message which optionally contains the first part of the content, and</li>
 * <li>1 or more content messages.</li>
 * </ul>
 * The content of the aggregated message will be the merged content of the start message and its following content
 * messages. If this aggregator encounters a content message where {@link #isLastContentMessage(ByteBufHolder)}
 * return {@code true} for, the aggregator will finish the aggregation and produce the aggregated message and expect
 * another start message.
 * </p>
 *
 * @param <I> the type that covers both start message and content message
 * @param <S> the type of the start message
 * @param <C> the type of the content message (must be a subtype of {@link ByteBufHolder})
 * @param <O> the type of the aggregated message (must be a subtype of {@code S} and {@link ByteBufHolder})
 */
// CLR: ByteBufHolder is IByteBufHolder. O also has the class constraint because Java type arguments are reference types
// and currentMessage is reset to null; C# does not convert a type parameter to an unrelated type parameter, so the
// unchecked (S)/(C) casts go through object.
public abstract class MessageAggregator<I, S, C, O> : MessageToMessageDecoder<I>
    where C : IByteBufHolder
    where O : class, IByteBufHolder
{
    private const int DEFAULT_MAX_COMPOSITEBUFFER_COMPONENTS = 1024;

    private readonly int maxContentLength;
    private O currentMessage;
    private bool handlingOversizedMessage;

    private int maxCumulationBufferComponents = DEFAULT_MAX_COMPOSITEBUFFER_COMPONENTS;
    private IChannelHandlerContext ctx;
    // CLR: a Task continuation replaces the ChannelFutureListener
    // (https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.continuewith).
    private Action<Task> continueResponseWriteListener;

    private bool aggregating;
    private bool handleIncompleteAggregateDuringClose = true;

    /**
     * Creates a new instance.
     *
     * @param maxContentLength
     *        the maximum length of the aggregated content.
     *        If the length of the aggregated content exceeds this value,
     *        {@link #handleOversizedMessage(ChannelHandlerContext, Object)} will be called.
     */
    protected MessageAggregator(int maxContentLength)
    {
        ValidateMaxContentLength(maxContentLength);
        this.maxContentLength = maxContentLength;
    }

    protected MessageAggregator(int maxContentLength, Type inboundMessageType) : base(inboundMessageType)
    {
        ValidateMaxContentLength(maxContentLength);
        this.maxContentLength = maxContentLength;
    }

    private static void ValidateMaxContentLength(int maxContentLength)
    {
        CheckPositiveOrZero(maxContentLength, "maxContentLength");
    }

    public override bool AcceptInboundMessage(object msg)
    {
        // No need to match last and full types because they are subset of first and middle types.
        if (!base.AcceptInboundMessage(msg))
        {
            return false;
        }

        I @in = (I) msg;

        if (IsAggregated(@in))
        {
            return false;
        }

        // NOTE: It's tempting to make this check only if aggregating is false. There are however
        // side conditions in decode(...) in respect to large messages.
        if (IsStartMessage(@in))
        {
            return true;
        }
        else
        {
            return aggregating && IsContentMessage(@in);
        }
    }

    /**
     * Returns {@code true} if and only if the specified message is a start message. Typically, this method is
     * implemented as a single {@code return} statement with {@code instanceof}:
     * <pre>
     * return msg instanceof MyStartMessage;
     * </pre>
     */
    protected abstract bool IsStartMessage(I msg);

    /**
     * Returns {@code true} if and only if the specified message is a content message. Typically, this method is
     * implemented as a single {@code return} statement with {@code instanceof}:
     * <pre>
     * return msg instanceof MyContentMessage;
     * </pre>
     */
    protected abstract bool IsContentMessage(I msg);

    /**
     * Returns {@code true} if and only if the specified message is the last content message. Typically, this method is
     * implemented as a single {@code return} statement with {@code instanceof}:
     * <pre>
     * return msg instanceof MyLastContentMessage;
     * </pre>
     * or with {@code instanceof} and boolean field check:
     * <pre>
     * return msg instanceof MyContentMessage && msg.isLastFragment();
     * </pre>
     */
    protected abstract bool IsLastContentMessage(C msg);

    /**
     * Returns {@code true} if and only if the specified message is already aggregated.  If this method returns
     * {@code true}, this handler will simply forward the message to the next handler as-is.
     */
    protected abstract bool IsAggregated(I msg);

    /**
     * Returns the maximum allowed length of the aggregated message in bytes.
     */
    public int MaxContentLength()
    {
        return maxContentLength;
    }

    /**
     * Returns the maximum number of components in the cumulation buffer.  If the number of
     * the components in the cumulation buffer exceeds this value, the components of the
     * cumulation buffer are consolidated into a single component, involving memory copies.
     * The default value of this property is {@value #DEFAULT_MAX_COMPOSITEBUFFER_COMPONENTS}.
     */
    public int MaxCumulationBufferComponents()
    {
        return maxCumulationBufferComponents;
    }

    /**
     * Sets the maximum number of components in the cumulation buffer.  If the number of
     * the components in the cumulation buffer exceeds this value, the components of the
     * cumulation buffer are consolidated into a single component, involving memory copies.
     * The default value of this property is {@value #DEFAULT_MAX_COMPOSITEBUFFER_COMPONENTS}
     * and its minimum allowed value is {@code 2}.
     */
    public void SetMaxCumulationBufferComponents(int maxCumulationBufferComponents)
    {
        if (maxCumulationBufferComponents < 2)
        {
            throw new ArgumentException(
                    "maxCumulationBufferComponents: " +
                    maxCumulationBufferComponents.ToString(CultureInfo.InvariantCulture) +
                    " (expected: >= 2)");
        }

        if (ctx == null)
        {
            this.maxCumulationBufferComponents = maxCumulationBufferComponents;
        }
        else
        {
            throw new InvalidOperationException(
                    "decoder properties cannot be changed once the decoder is added to a pipeline.");
        }
    }

    /**
     * @deprecated This method will be removed in future releases.
     */
    [Obsolete("This method will be removed in future releases.")]
    public bool IsHandlingOversizedMessage()
    {
        return handlingOversizedMessage;
    }

    protected IChannelHandlerContext Ctx()
    {
        if (ctx == null)
        {
            throw new InvalidOperationException("not added to a pipeline yet");
        }
        return ctx;
    }

    protected override void Decode(IChannelHandlerContext ctx, I msg, IList<object> @out)
    {
        if (IsStartMessage(msg))
        {
            aggregating = true;
            handlingOversizedMessage = false;
            if (currentMessage != null)
            {
                currentMessage.Release();
                currentMessage = null;
                throw new MessageAggregationException();
            }

            S m = (S) (object) msg;

            // Send the continue response if necessary (e.g. 'Expect: 100-continue' header)
            // Check before content length. Failing an expectation may result in a different response being sent.
            object continueResponse = NewContinueResponse(m, maxContentLength, ctx.Pipeline);
            if (continueResponse != null)
            {
                // Cache the write listener for reuse.
                Action<Task> listener = continueResponseWriteListener;
                if (listener == null)
                {
                    // CLR: future.cause() of an unsuccessful write is the Task's exception, or a TaskCanceledException
                    // (an OperationCanceledException, as the cause of a cancelled Java future is a CancellationException).
                    continueResponseWriteListener = listener = future =>
                    {
                        if (!future.IsCompletedSuccessfully)
                        {
                            ctx.FireExceptionCaught(
                                    future.IsFaulted ? future.Exception.InnerException : new TaskCanceledException(future));
                        }
                    };
                }

                // Make sure to call this before writing, otherwise reference counts may be invalid.
                bool closeAfterWrite = CloseAfterContinueResponse(continueResponse);
                handlingOversizedMessage = IgnoreContentAfterContinueResponse(continueResponse);

                // CLR: addListener(listener) is a synchronous continuation, which runs at once when the write has
                // already completed, as a listener added to a completed future does
                // (https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcontinuationoptions).
                Task future = ctx.WriteAndFlushAsync(continueResponse);
                _ = future.ContinueWith(listener, CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                if (closeAfterWrite)
                {
                    handleIncompleteAggregateDuringClose = false;
                    // CLR: ChannelFutureListener.CLOSE closes future.channel(), which is ctx.Channel; a Task has no
                    // channel. It is registered after the listener above, as in the source.
                    _ = future.ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    return;
                }
                if (handlingOversizedMessage)
                {
                    return;
                }
            }
            else if (IsContentLengthInvalid(m, maxContentLength))
            {
                // if content length is set, preemptively close if it's too large
                InvokeHandleOversizedMessage(ctx, m);
                return;
            }

            if (m is IDecoderResultProvider && !((IDecoderResultProvider) m).DecoderResult().IsSuccess())
            {
                O aggregated;
                if (m is IByteBufHolder)
                {
                    aggregated = BeginAggregation(m, ((IByteBufHolder) m).Content.Retain());
                }
                else
                {
                    aggregated = BeginAggregation(m, Unpooled.EmptyBuffer);
                }
                FinishAggregation0(aggregated);
                @out.Add(aggregated);
                return;
            }

            // A streamed message - initialize the cumulative buffer, and wait for incoming chunks.
            CompositeByteBuf content = ctx.Alloc().CompositeBuffer(maxCumulationBufferComponents);
            if (m is IByteBufHolder)
            {
                AppendPartialContent(content, ((IByteBufHolder) m).Content);
            }
            currentMessage = BeginAggregation(m, content);
        }
        else if (IsContentMessage(msg))
        {
            if (currentMessage == null)
            {
                // it is possible that a TooLongFrameException was already thrown but we can still discard data
                // until the begging of the next request/response.
                return;
            }

            // Merge the received chunk into the content of the current message.
            CompositeByteBuf content = (CompositeByteBuf) currentMessage.Content;

            C m = (C) (object) msg;
            // Handle oversized message.
            if (content.ReadableBytes > maxContentLength - m.Content.ReadableBytes)
            {
                // By convention, full message type extends first message type.
                S s = (S) (object) currentMessage;
                InvokeHandleOversizedMessage(ctx, s);
                return;
            }

            // Append the content of the chunk.
            AppendPartialContent(content, m.Content);

            // Give the subtypes a chance to merge additional information such as trailing headers.
            Aggregate(currentMessage, m);

            bool last;
            if (m is IDecoderResultProvider)
            {
                DecoderResult decoderResult = ((IDecoderResultProvider) m).DecoderResult();
                if (!decoderResult.IsSuccess())
                {
                    if (currentMessage is IDecoderResultProvider)
                    {
                        ((IDecoderResultProvider) currentMessage).SetDecoderResult(
                                DecoderResult.Failure(decoderResult.Cause()));
                    }
                    last = true;
                }
                else
                {
                    last = IsLastContentMessage(m);
                }
            }
            else
            {
                last = IsLastContentMessage(m);
            }

            if (last)
            {
                FinishAggregation0(currentMessage);

                // All done
                @out.Add(currentMessage);
                currentMessage = null;
            }
        }
        else
        {
            throw new MessageAggregationException();
        }
    }

    // CLR: CompositeByteBuf.AddComponent takes the buffer before the increaseWriterIndex flag.
    private static void AppendPartialContent(CompositeByteBuf content, ByteBuf partialContent)
    {
        if (partialContent.IsReadable())
        {
            content.AddComponent(partialContent.Retain(), true);
        }
    }

    /**
     * Determine if the message {@code start}'s content length is known, and if it greater than
     * {@code maxContentLength}.
     * @param start The message which may indicate the content length.
     * @param maxContentLength The maximum allowed content length.
     * @return {@code true} if the message {@code start}'s content length is known, and if it greater than
     * {@code maxContentLength}. {@code false} otherwise.
     */
    protected abstract bool IsContentLengthInvalid(S start, int maxContentLength);

    /**
     * Returns the 'continue response' for the specified start message if necessary. For example, this method is
     * useful to handle an HTTP 100-continue header.
     *
     * @return the 'continue response', or {@code null} if there's no message to send
     */
    protected abstract object NewContinueResponse(S start, int maxContentLength, IChannelPipeline pipeline);

    /**
     * Determine if the channel should be closed after the result of
     * {@link #newContinueResponse(Object, int, ChannelPipeline)} is written.
     * @param msg The return value from {@link #newContinueResponse(Object, int, ChannelPipeline)}.
     * @return {@code true} if the channel should be closed after the result of
     * {@link #newContinueResponse(Object, int, ChannelPipeline)} is written. {@code false} otherwise.
     */
    protected abstract bool CloseAfterContinueResponse(object msg);

    /**
     * Determine if all objects for the current request/response should be ignored or not.
     * Messages will stop being ignored the next time {@link #isContentMessage(Object)} returns {@code true}.
     *
     * @param msg The return value from {@link #newContinueResponse(Object, int, ChannelPipeline)}.
     * @return {@code true} if all objects for the current request/response should be ignored or not.
     * {@code false} otherwise.
     */
    protected abstract bool IgnoreContentAfterContinueResponse(object msg);

    /**
     * Creates a new aggregated message from the specified start message and the specified content.  If the start
     * message implements {@link ByteBufHolder}, its content is appended to the specified {@code content}.
     * This aggregator will continue to append the received content to the specified {@code content}.
     */
    protected abstract O BeginAggregation(S start, ByteBuf content);

    /**
     * Transfers the information provided by the specified content message to the specified aggregated message.
     * Note that the content of the specified content message has been appended to the content of the specified
     * aggregated message already, so that you don't need to.  Use this method to transfer the additional information
     * that the content message provides to {@code aggregated}.
     */
    protected virtual void Aggregate(O aggregated, C content)
    {
    }

    private void FinishAggregation0(O aggregated)
    {
        aggregating = false;
        FinishAggregation(aggregated);
    }

    /**
     * Invoked when the specified {@code aggregated} message is about to be passed to the next handler in the pipeline.
     */
    protected virtual void FinishAggregation(O aggregated)
    {
    }

    private void InvokeHandleOversizedMessage(IChannelHandlerContext ctx, S oversized)
    {
        handlingOversizedMessage = true;
        currentMessage = null;
        handleIncompleteAggregateDuringClose = false;
        try
        {
            HandleOversizedMessage(ctx, oversized);
        }
        finally
        {
            // Release the message in case it is a full one.
            ReferenceCountUtil.Release(oversized);
        }
    }

    /**
     * Invoked when an incoming request exceeds the maximum content length.  The default behvaior is to trigger an
     * {@code exceptionCaught()} event with a {@link TooLongFrameException}.
     *
     * @param ctx the {@link ChannelHandlerContext}
     * @param oversized the accumulated message up to this point, whose type is {@code S} or {@code O}
     */
    protected virtual void HandleOversizedMessage(IChannelHandlerContext ctx, S oversized)
    {
        ctx.FireExceptionCaught(
                new TooLongFrameException("content length exceeded " +
                        MaxContentLength().ToString(CultureInfo.InvariantCulture) + " bytes."));
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        // We might need keep reading the channel until the full message is aggregated.
        //
        // See https://github.com/netty/netty/issues/6583
        if (currentMessage != null && !ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }
        ctx.FireChannelReadComplete();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (aggregating && handleIncompleteAggregateDuringClose)
        {
            ctx.FireExceptionCaught(
                    new PrematureChannelClosureException("Channel closed while still aggregating message"));
        }
        try
        {
            // release current message if it is not null as it may be a left-over
            base.ChannelInactive(ctx);
        }
        finally
        {
            ReleaseCurrentMessage();
        }
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        try
        {
            base.HandlerRemoved(ctx);
        }
        finally
        {
            // release current message if it is not null as it may be a left-over as there is not much more we can do in
            // this case
            ReleaseCurrentMessage();
        }
    }

    protected void ReleaseCurrentMessage()
    {
        if (currentMessage != null)
        {
            currentMessage.Release();
            currentMessage = null;
        }
        handlingOversizedMessage = false;
        aggregating = false;
    }
}
