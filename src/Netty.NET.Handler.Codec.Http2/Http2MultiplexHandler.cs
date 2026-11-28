/*
 * Copyright 2019 The Netty Project
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
using System.Security.Authentication;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http2.AbstractHttp2StreamChannel;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * An HTTP/2 handler that creates child channels for each stream. This handler must be used in combination
 * with {@link Http2FrameCodec}.
 *
 * <p>When a new stream is created, a new {@link Http2StreamChannel} is created for it. Applications send and
 * receive {@link Http2StreamFrame}s on the created channel. {@link ByteBuf}s cannot be processed by the channel;
 * all writes that reach the head of the pipeline must be an instance of {@link Http2StreamFrame}. Writes that reach
 * the head of the pipeline are processed directly by this handler and cannot be intercepted.
 *
 * <p>The child channel will be notified of user events that impact the stream, such as {@link
 * Http2GoAwayFrame} and {@link Http2ResetFrame}, as soon as they occur. Although {@code
 * Http2GoAwayFrame} and {@code Http2ResetFrame} signify that the remote is ignoring further
 * communication, closing of the channel is delayed until any inbound queue is drained with {@link
 * Channel#read()}, which follows the default behavior of channels in Netty. Applications are
 * free to close the channel in response to such events if they don't have use for any queued
 * messages. Any connection level events like {@link Http2SettingsFrame} and {@link Http2GoAwayFrame}
 * will be processed internally and also propagated down the pipeline for other handlers to act on.
 *
 * <p>Outbound streams are supported via the {@link Http2StreamChannelBootstrap}.
 *
 * <p>{@link ChannelConfig#setMaxMessagesPerRead(int)} and {@link ChannelConfig#setAutoRead(boolean)} are supported.
 *
 * <h3>Reference Counting</h3>
 *
 * Some {@link Http2StreamFrame}s implement the {@link ReferenceCounted} interface, as they carry
 * reference counted objects (e.g. {@link ByteBuf}s). The multiplex codec will call {@link ReferenceCounted#retain()}
 * before propagating a reference counted object through the pipeline, and thus an application handler needs to release
 * such an object after having consumed it. For more information on reference counting take a look at
 * <a href="https://netty.io/wiki/reference-counted-objects.html">the reference counted docs.</a>
 *
 * <h3>Channel Events</h3>
 *
 * A child channel becomes active as soon as it is registered to an {@link EventLoop}. Therefore, an active channel
 * does not map to an active HTTP/2 stream immediately. Only once a {@link Http2HeadersFrame} has been successfully sent
 * or received, does the channel map to an active HTTP/2 stream. In case it is not possible to open a new HTTP/2 stream
 * (i.e. due to the maximum number of active streams being exceeded), the child channel receives an exception
 * indicating the cause and is closed immediately thereafter.
 *
 * <h3>Writability and Flow Control</h3>
 *
 * A child channel observes outbound/remote flow control via the channel's writability. A channel only becomes writable
 * when it maps to an active HTTP/2 stream . A child channel does not know about the connection-level flow control
 * window. {@link ChannelHandler}s are free to ignore the channel's writability, in which case the excessive writes will
 * be buffered by the parent channel. It's important to note that only {@link Http2DataFrame}s are subject to
 * HTTP/2 flow control.
 *
 * <h3>Closing a {@link Http2StreamChannel}</h3>
 *
 * Once you close a {@link Http2StreamChannel} a {@link Http2ResetFrame} will be sent to the remote peer with
 * {@link Http2Error#CANCEL} if needed. If you want to close the stream with another {@link Http2Error} (due
 * errors / limits) you should propagate a {@link Http2FrameStreamException} through the {@link ChannelPipeline}.
 * Once it reaches the end of the {@link ChannelPipeline} it will automatically close the {@link Http2StreamChannel}
 * and send a {@link Http2ResetFrame} with the unwrapped {@link Http2Error} set. Another possibility is to just
 * directly write a {@link Http2ResetFrame} to the {@link Http2StreamChannel}l.
 */
// CLR: ChannelFuture is Task and future.addListener is Http2CodecUtil.AddListener (EventExecutorTaskScheduler over
// ctx.Executor, docs/codec-http-porting.md, Listener timing). A Task has no channel(), so registerDone takes the child
// channel and the close listener of the outbound child channels is one lambda per channel that captures it.
// Channel.unsafe().closeForcibly() is AbstractHttp2StreamChannel.Unsafe().CloseForcibly() (the channel's internal
// Http2ChannelUnsafe; Channel.Unsafe is folded into the channel, docs/transport-porting.md). The switch on the stream state (an enum class here, compared by reference) is an
// if/else chain that keeps the fall-through order. SSLException is AuthenticationException (docs/handler-porting.md,
// SSL; src/Netty.NET.Handler/Ssl/ApplicationProtocolNegotiationHandler.cs). ServerChannel is IServerChannel,
// IllegalStateException InvalidOperationException, ArrayDeque Queue (MaxCapacityQueue) and HashSet the BCL HashSet.
// The anonymous Http2FrameStreamVisitor classes are private nested classes and the inner stream channel class takes
// this handler as a constructor argument. The checked "throws" clauses have no counterpart.
public sealed class Http2MultiplexHandler : Http2ChannelDuplexHandler
{

    internal static readonly Action<Task, IChannel> CHILD_CHANNEL_REGISTRATION_LISTENER = RegisterDone;

    private readonly IChannelHandler inboundStreamHandler;
    private readonly IChannelHandler upgradeStreamHandler;
    private readonly MaxCapacityQueue<AbstractHttp2StreamChannel> readCompletePendingQueue =
            new MaxCapacityQueue<AbstractHttp2StreamChannel>(new Queue<AbstractHttp2StreamChannel>(8),
                    // Choose 100 which is what is used most of the times as default.
                    Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS);

    // Outbound child channels opened via Http2StreamChannelBootstrap that were not closed yet. Until their first
    // HEADERS frame is written their stream is unknown to the connection, so closing the connection doesn't close them.
    private readonly HashSet<AbstractHttp2StreamChannel> outboundStreamChannels = new HashSet<AbstractHttp2StreamChannel>();
    // Shared by all outbound child channels: removes a child channel from outboundStreamChannels once it is closed.
    // CLR: outboundStreamChannelCloseListener is the closeFuture listener NewOutboundStream adds (see the type comment).
    private bool parentReadInProgress;
    private int idCount;

    // Need to be volatile as accessed from within the Http2MultiplexHandlerStreamChannel in a multi-threaded fashion.
    private volatile IChannelHandlerContext ctx;

    /**
     * Creates a new instance
     *
     * @param inboundStreamHandler the {@link ChannelHandler} that will be added to the {@link ChannelPipeline} of
     *                             the {@link Channel}s created for new inbound streams.
     */
    public Http2MultiplexHandler(IChannelHandler inboundStreamHandler)
        : this(inboundStreamHandler, null)
    {
    }

    /**
     * Creates a new instance
     *
     * @param inboundStreamHandler the {@link ChannelHandler} that will be added to the {@link ChannelPipeline} of
     *                             the {@link Channel}s created for new inbound streams.
     * @param upgradeStreamHandler the {@link ChannelHandler} that will be added to the {@link ChannelPipeline} of the
     *                             upgraded {@link Channel}.
     */
    public Http2MultiplexHandler(IChannelHandler inboundStreamHandler, IChannelHandler upgradeStreamHandler)
    {
        this.inboundStreamHandler = ObjectUtil.CheckNotNull(inboundStreamHandler, "inboundStreamHandler");
        this.upgradeStreamHandler = upgradeStreamHandler;
    }

    internal static void RegisterDone(Task future, IChannel childChannel)
    {
        // Handle any errors that occurred on the local thread while registering. Even though
        // failures can happen after this point, they will be handled by the channel by closing the
        // childChannel.
        if (!future.IsCompletedSuccessfully)
        {
            if (childChannel.IsRegistered)
            {
                _ = childChannel.CloseAsync();
            }
            else
            {
                ((AbstractHttp2StreamChannel) childChannel).Unsafe().CloseForcibly();
            }
        }
    }

    protected override void HandlerAdded0(IChannelHandlerContext ctx)
    {
        if (ctx.Executor != ctx.Channel.EventLoop)
        {
            throw new InvalidOperationException("EventExecutor must be EventLoop of Channel");
        }

        this.ctx = ctx;
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        readCompletePendingQueue.Clear();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (outboundStreamChannels.Count != 0)
        {
            // The streams known to the connection were closed already. Close the outbound child channels whose
            // stream was never created as well, so they see channelInactive() and their closeFuture() completes.
            // Iterate over a copy, as closing a child channel removes it from outboundStreamChannels (see
            // outboundStreamChannelCloseListener).
            foreach (AbstractHttp2StreamChannel childChannel in
                    new List<AbstractHttp2StreamChannel>(outboundStreamChannels))
            {
                if (childChannel.Stream().State() == IHttp2Stream.State.IDLE)
                {
                    childChannel.Unsafe().CloseForcibly();
                }
            }
        }

        ctx.FireChannelInactive();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        parentReadInProgress = true;

        if (msg is IHttp2StreamFrame)
        {
            if (msg is IHttp2WindowUpdateFrame)
            {
                // We dont want to propagate update frames to the user
                return;
            }

            IHttp2StreamFrame streamFrame = (IHttp2StreamFrame) msg;
            Http2FrameCodec.DefaultHttp2FrameStream s =
                    (Http2FrameCodec.DefaultHttp2FrameStream) streamFrame.Stream();

            AbstractHttp2StreamChannel channel = (AbstractHttp2StreamChannel) s.attachment;

            if (msg is IHttp2ResetFrame || msg is IHttp2PriorityFrame)
            {
                // Reset and Priority frames needs to be propagated via user events as these are not flow-controlled and
                // so must not be controlled by suppressing channel.read() on the child channel.
                channel.FireChildUserEventTriggered(msg);

                // RST frames will also trigger closing of the streams which then will call
                // AbstractHttp2StreamChannel.streamClosed()
            }
            else
            {
                channel.FireChildRead(streamFrame);
            }

            return;
        }

        if (msg is IHttp2GoAwayFrame)
        {
            // goaway frames will also trigger closing of the streams which then will call
            // AbstractHttp2StreamChannel.streamClosed()
            OnHttp2GoAwayFrame(ctx, (IHttp2GoAwayFrame) msg);
        }

        // Send everything down the pipeline
        ctx.FireChannelRead(msg);
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.IsWritable)
        {
            // While the writability state may change during iterating of the streams we just set all of the streams
            // to writable to not affect fairness. These will be "limited" by their own watermarks in any case.
            ForEachActiveStream(AbstractHttp2StreamChannel.WRITABLE_VISITOR);
        }

        ctx.FireChannelWritabilityChanged();
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is Http2FrameStreamEvent)
        {
            Http2FrameStreamEvent @event = (Http2FrameStreamEvent) evt;
            Http2FrameCodec.DefaultHttp2FrameStream stream = (Http2FrameCodec.DefaultHttp2FrameStream) @event.Stream();

            if (@event.Type_() == Http2FrameStreamEvent.Type.State)
            {
                IHttp2Stream.State state = stream.State();
                if (state == IHttp2Stream.State.HALF_CLOSED_LOCAL && stream.Id() != Http2CodecUtil.HTTP_UPGRADE_STREAM_ID)
                {
                    // Ignore everything which was not caused by an upgrade
                }
                else if (state == IHttp2Stream.State.HALF_CLOSED_LOCAL || state == IHttp2Stream.State.HALF_CLOSED_REMOTE ||
                        state == IHttp2Stream.State.OPEN)
                {
                    // fall-through
                    if (stream.attachment != null)
                    {
                        // ignore if child channel was already created.
                    }
                    else
                    {
                        AbstractHttp2StreamChannel ch;

                        // We need to handle upgrades special when on the client side.
                        if (stream.Id() == Http2CodecUtil.HTTP_UPGRADE_STREAM_ID && !IsServer(ctx))
                        {
                            // We must have an upgrade handler or else we can't handle the stream
                            if (upgradeStreamHandler == null)
                            {
                                throw ConnectionError(INTERNAL_ERROR,
                                        "Client is misconfigured for upgrade requests");
                            }

                            ch = new Http2MultiplexHandlerStreamChannel(this, stream, upgradeStreamHandler);
                            ch.CloseOutbound();
                        }
                        else
                        {
                            ch = new Http2MultiplexHandlerStreamChannel(this, stream, inboundStreamHandler);
                        }

                        Task future = ctx.Channel.EventLoop.RegisterAsync(ch);
                        if (future.IsCompleted)
                        {
                            RegisterDone(future, ch);
                        }
                        else
                        {
                            AddListener(ctx, future, f => RegisterDone(f, ch));
                        }
                    }
                }
                else if (state == IHttp2Stream.State.CLOSED)
                {
                    AbstractHttp2StreamChannel channel = (AbstractHttp2StreamChannel) stream.attachment;
                    if (channel != null)
                    {
                        channel.StreamClosed();
                    }
                }
                else
                {
                    // ignore for now
                }
            }

            return;
        }

        if (evt == ChannelInputShutdownReadComplete.INSTANCE)
        {
            ForEachActiveStream(CHANNEL_INPUT_SHUTDOWN_READ_COMPLETE_VISITOR);
        }
        else if (evt == ChannelOutputShutdownEvent.INSTANCE)
        {
            ForEachActiveStream(CHANNEL_OUTPUT_SHUTDOWN_EVENT_VISITOR);
        }
        else if (evt == SslCloseCompletionEvent.SUCCESS)
        {
            ForEachActiveStream(SSL_CLOSE_COMPLETION_EVENT_VISITOR);
        }

        ctx.FireUserEventTriggered(evt);
    }

    // TODO: This is most likely not the best way to expose this, need to think more about it.
    internal IHttp2StreamChannel NewOutboundStream()
    {
        Http2MultiplexHandlerStreamChannel childChannel =
                new Http2MultiplexHandlerStreamChannel(this, (Http2FrameCodec.DefaultHttp2FrameStream) NewStream(), null);
        outboundStreamChannels.Add(childChannel);
        AddListener(ctx, childChannel.CloseFuture, _ => outboundStreamChannels.Remove(childChannel));
        return childChannel;
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (cause is Http2FrameStreamException)
        {
            Http2FrameStreamException exception = (Http2FrameStreamException) cause;
            IHttp2FrameStream stream = exception.Stream();
            AbstractHttp2StreamChannel childChannel = (AbstractHttp2StreamChannel)
                    ((Http2FrameCodec.DefaultHttp2FrameStream) stream).attachment;

            try
            {
                childChannel.FireChildExceptionCaught(cause.InnerException);
            }
            finally
            {
                // Close with the correct error that causes this stream exception.
                // See https://github.com/netty/netty/issues/13235#issuecomment-1441994672
                childChannel.CloseWithError(exception.Error());
            }

            return;
        }

        if (cause is Http2MultiplexActiveStreamsException)
        {
            // Unwrap the cause that was used to create it and fire it for all the active streams.
            FireExceptionCaughtForActiveStream(cause.InnerException);
            return;
        }

        if (cause.InnerException is AuthenticationException)
        {
            FireExceptionCaughtForActiveStream(cause);
        }

        ctx.FireExceptionCaught(cause);
    }

    private void FireExceptionCaughtForActiveStream(Exception cause)
    {
        ForEachActiveStream(new ExceptionCaughtVisitor(cause));
    }

    // CLR: the anonymous Http2FrameStreamVisitor of fireExceptionCaughtForActiveStream.
    private sealed class ExceptionCaughtVisitor : IHttp2FrameStreamVisitor
    {
        private readonly Exception cause;

        internal ExceptionCaughtVisitor(Exception cause)
        {
            this.cause = cause;
        }

        public bool Visit(IHttp2FrameStream stream)
        {
            AbstractHttp2StreamChannel childChannel = (AbstractHttp2StreamChannel)
                    ((Http2FrameCodec.DefaultHttp2FrameStream) stream).attachment;
            childChannel.FireChildExceptionCaught(cause);
            return true;
        }
    }

    private static bool IsServer(IChannelHandlerContext ctx)
    {
        return ctx.Channel.Parent is IServerChannel;
    }

    private void OnHttp2GoAwayFrame(IChannelHandlerContext ctx, IHttp2GoAwayFrame goAwayFrame)
    {
        if (goAwayFrame.LastStreamId() == int.MaxValue)
        {
            // None of the streams can have an id greater than Integer.MAX_VALUE
            return;
        }

        // Notify which streams were not processed by the remote peer and are safe to retry on another connection:
        try
        {
            bool server = IsServer(ctx);
            ForEachActiveStream(new GoAwayVisitor(goAwayFrame, server));
        }
        catch (Http2Exception e)
        {
            ctx.FireExceptionCaught(e);
            _ = ctx.CloseAsync();
        }
    }

    // CLR: the anonymous Http2FrameStreamVisitor of onHttp2GoAwayFrame.
    private sealed class GoAwayVisitor : IHttp2FrameStreamVisitor
    {
        private readonly IHttp2GoAwayFrame goAwayFrame;
        private readonly bool server;

        internal GoAwayVisitor(IHttp2GoAwayFrame goAwayFrame, bool server)
        {
            this.goAwayFrame = goAwayFrame;
            this.server = server;
        }

        public bool Visit(IHttp2FrameStream stream)
        {
            int streamId = stream.Id();
            if (streamId > goAwayFrame.LastStreamId() && Http2CodecUtil.IsStreamIdValid(streamId, server))
            {
                AbstractHttp2StreamChannel childChannel = (AbstractHttp2StreamChannel)
                        ((Http2FrameCodec.DefaultHttp2FrameStream) stream).attachment;
                childChannel.FireChildUserEventTriggered(goAwayFrame.RetainedDuplicate());
            }

            return true;
        }
    }

    /**
     * Notifies any child streams of the read completion.
     */
    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ProcessPendingReadCompleteQueue();
        ctx.FireChannelReadComplete();
    }

    private void ProcessPendingReadCompleteQueue()
    {
        parentReadInProgress = true;
        // If we have many child channel we can optimize for the case when multiple call flush() in
        // channelReadComplete(...) callbacks and only do it once as otherwise we will end-up with multiple
        // write calls on the socket which is expensive.
        AbstractHttp2StreamChannel childChannel = readCompletePendingQueue.Poll();
        if (childChannel != null)
        {
            try
            {
                do
                {
                    childChannel.FireChildReadComplete();
                    childChannel = readCompletePendingQueue.Poll();
                } while (childChannel != null);
            }
            finally
            {
                parentReadInProgress = false;
                readCompletePendingQueue.Clear();
                ctx.Flush();
            }
        }
        else
        {
            parentReadInProgress = false;
        }
    }

    private sealed class Http2MultiplexHandlerStreamChannel : AbstractHttp2StreamChannel
    {
        private readonly Http2MultiplexHandler handler;

        internal Http2MultiplexHandlerStreamChannel(Http2MultiplexHandler handler,
                Http2FrameCodec.DefaultHttp2FrameStream stream, IChannelHandler inboundHandler)
            : base(handler.ctx.Channel, stream, ++handler.idCount, inboundHandler)
        {
            this.handler = handler;
        }

        protected override bool IsParentReadInProgress()
        {
            return handler.parentReadInProgress;
        }

        protected override void AddChannelToReadCompletePendingQueue()
        {
            // If there is no space left in the queue, just keep on processing everything that is already
            // stored there and try again.
            while (!handler.readCompletePendingQueue.Offer(this))
            {
                handler.ProcessPendingReadCompleteQueue();
            }
        }

        protected override IChannelHandlerContext ParentContext()
        {
            return handler.ctx;
        }
    }
}
