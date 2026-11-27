/*
 * Copyright 2021 The Netty Project
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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http3.Http3CodecUtils;
using static Netty.NET.Handler.Codec.Http3.Http3ErrorCode;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * A manager for <a href="https://quicwg.org/base-drafts/draft-ietf-quic-http.html#name-push-streams">push streams</a>
 * for a server. New push streams can be initiated using the various {@code newPushStream} methods. It is required to
 * add the {@link ChannelHandler} returned from {@link #controlStreamListener()} to the {@link QuicChannel} associated
 * with this manager.
 */
// CLR: QuicChannel, QuicStreamChannel, QuicStreamChannelBootstrap and QuicStreamType are codec-quic's IQuicChannel,
// IQuicStreamChannel, QuicStreamChannelBootstrap and the C# enum QuicStreamType; ChannelHandler/ChannelInboundHandler
// are IChannelHandler/IChannelInboundHandler. Future<QuicStreamChannel> is Task<IQuicStreamChannel>:
// newPushStream(pushId, handler) and the overload with a Promise<QuicStreamChannel> fold into
// NewPushStreamAsync(pushId, handler), which returns the Task of CreateStreamAsync (the promise overload passed that
// promise to createStream), and the overload with a bootstrap configurator and a promise is NewPushStreamAsync(pushId,
// handler, configurator), which returns the Task of QuicStreamChannelBootstrap.CreateAsync (create(promise)), as
// IQuicChannel.CreateStreamAsync folds its promise overload (docs/codec-classes-quic-porting.md).
// UnaryOperator<QuicStreamChannelBootstrap> is Func<QuicStreamChannelBootstrap, QuicStreamChannelBootstrap>. The
// ConcurrentMap<Long, Object> is a Dictionary<long, object> under a lock on it: computeIfPresent/compute (whose
// function may remove the entry and runs once, with side effects), replace(key, old, new), put and remove are atomic as
// on the ConcurrentHashMap; ConcurrentDictionary has no compute that may remove the entry. The volatile nextId with its
// AtomicLongFieldUpdater is a long field read with Volatile.Read and incremented with Interlocked.Increment
// (getAndIncrement). The anonymous ChannelInboundHandlerAdapter of controlStreamListener, the anonymous
// Http3PushStreamServerInitializer and its anonymous ChannelInboundHandlerAdapter are the private nested classes
// ControlStreamListenerHandler, PushStreamInitializerImpl and PushStreamStateHandler, which take the enclosing instance
// and the captured locals. future.addListener(f -> ...) is an ExecuteSynchronously continuation on Common's
// EventExecutorTaskScheduler over the channel's event loop, where Java notifies the listeners of the event loop
// promise; writeAndFlush/close are WriteAndFlushAsync/CloseAsync, whose Tasks are not awaited. Objects.requireNonNull
// is ArgumentNullException.ThrowIfNull; IllegalStateException is InvalidOperationException and IllegalArgumentException
// ArgumentException; ids print with the invariant culture and an object state with its ToString(). evt ==
// ChannelInputShutdownReadComplete.INSTANCE is a reference comparison; assert is Debug.Assert. The @Nullable
// (org.jetbrains.annotations) annotations have no counterpart.
public sealed class Http3ServerPushStreamManager
{
    private static readonly object CANCELLED_STREAM = new object();
    private static readonly object PUSH_ID_GENERATED = new object();
    private static readonly object AWAITING_STREAM_ESTABLISHMENT = new object();

    private readonly IQuicChannel channel;
    private readonly Dictionary<long, object> pushStreams;
    private readonly IChannelInboundHandler controlStreamListener;

    private long nextId;

    /**
     * Creates a new instance.
     *
     * @param channel for which this manager is created.
     */
    public Http3ServerPushStreamManager(IQuicChannel channel) : this(channel, 8)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param channel for which this manager is created.
     * @param initialPushStreamsCountHint a hint for the number of push streams that may be created.
     */
    public Http3ServerPushStreamManager(IQuicChannel channel, int initialPushStreamsCountHint)
    {
        ArgumentNullException.ThrowIfNull(channel, "channel");
        this.channel = channel;
        pushStreams = new Dictionary<long, object>(initialPushStreamsCountHint);
        controlStreamListener = new ControlStreamListenerHandler(this);
    }

    /**
     * Returns {@code true} if server push is allowed at this point.
     *
     * @return {@code true} if server push is allowed at this point.
     */
    public bool IsPushAllowed()
    {
        return IsPushAllowed(Http3.MaxPushIdReceived(channel));
    }

    /**
     * Reserves a push ID to be used to create a new push stream subsequently. A push ID can only be used to create
     * exactly one push stream.
     *
     * @return Next push ID.
     * @throws IllegalStateException If it is not allowed to create any more push streams on the associated
     * {@link QuicChannel}. Use {@link #isPushAllowed()} to check if server push is allowed.
     */
    public long ReserveNextPushId()
    {
        long maxPushId = Http3.MaxPushIdReceived(channel);
        if (IsPushAllowed(maxPushId))
        {
            return NextPushId();
        }
        throw new InvalidOperationException("MAX allowed push ID: " + maxPushId.ToString(CultureInfo.InvariantCulture) +
                ", next push ID: " + Volatile.Read(ref nextId).ToString(CultureInfo.InvariantCulture));
    }

    /**
     * Returns a new HTTP/3 push-stream that will use the given {@link ChannelHandler}
     * to dispatch {@link Http3PushStreamFrame}s too. The needed HTTP/3 codecs are automatically added to the
     * pipeline as well.
     *
     * @param pushId for the push stream. This MUST be obtained using {@link #reserveNextPushId()}.
     * @param handler the {@link ChannelHandler} to add. Can be {@code null}.
     * @return the {@link Future} that will be notified once the push-stream was opened.
     */
    /**
     * Returns a new HTTP/3 push-stream that will use the given {@link ChannelHandler}
     * to dispatch {@link Http3PushStreamFrame}s too. The needed HTTP/3 codecs are automatically added to the
     * pipeline as well.
     *
     * @param pushId for the push stream. This MUST be obtained using {@link #reserveNextPushId()}.
     * @param handler the {@link ChannelHandler} to add. Can be {@code null}.
     * @param promise to indicate creation of the push stream.
     */
    // CLR: newPushStream(pushId, handler) created channel.eventLoop().newPromise() and called the promise overload,
    // which is folded in here (see the type comment).
    public Task<IQuicStreamChannel> NewPushStreamAsync(long pushId, IChannelHandler handler)
    {
        ValidatePushId(pushId);
        Task<IQuicStreamChannel> future =
                channel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL, PushStreamInitializer(pushId, handler));
        SetupCancelPushIfStreamCreationFails(pushId, future, channel);
        return future;
    }

    /**
     * Returns a new HTTP/3 push-stream that will use the given {@link ChannelHandler}
     * to dispatch {@link Http3PushStreamFrame}s too. The needed HTTP/3 codecs are automatically added to the
     * pipeline as well.
     *
     * @param pushId for the push stream. This MUST be obtained using {@link #reserveNextPushId()}.
     * @param handler the {@link ChannelHandler} to add. Can be {@code null}.
     * @param bootstrapConfigurator {@link UnaryOperator} to configure the {@link QuicStreamChannelBootstrap} used.
     * @param promise to indicate creation of the push stream.
     */
    public Task<IQuicStreamChannel> NewPushStreamAsync(long pushId, IChannelHandler handler,
                              Func<QuicStreamChannelBootstrap, QuicStreamChannelBootstrap> bootstrapConfigurator)
    {
        ValidatePushId(pushId);
        QuicStreamChannelBootstrap bootstrap = bootstrapConfigurator(channel.NewStreamBootstrap());
        Task<IQuicStreamChannel> future = bootstrap.Type(QuicStreamType.UNIDIRECTIONAL)
                .Handler(PushStreamInitializer(pushId, handler))
                .CreateAsync();
        SetupCancelPushIfStreamCreationFails(pushId, future, channel);
        return future;
    }

    /**
     * A {@link ChannelInboundHandler} to be added to the {@link QuicChannel} associated with this
     * {@link Http3ServerPushStreamManager} to listen to control stream frames.
     *
     * @return {@link ChannelInboundHandler} to be added to the {@link QuicChannel} associated with this
     * {@link Http3ServerPushStreamManager} to listen to control stream frames.
     */
    public IChannelInboundHandler ControlStreamListener()
    {
        return controlStreamListener;
    }

    private bool IsPushAllowed(long maxPushId)
    {
        return Volatile.Read(ref nextId) <= maxPushId;
    }

    private long NextPushId()
    {
        long pushId = Interlocked.Increment(ref nextId) - 1;
        lock (pushStreams)
        {
            pushStreams[pushId] = PUSH_ID_GENERATED;
        }
        return pushId;
    }

    private void ValidatePushId(long pushId)
    {
        bool replaced;
        lock (pushStreams)
        {
            replaced = pushStreams.TryGetValue(pushId, out object existing) && existing == PUSH_ID_GENERATED;
            if (replaced)
            {
                pushStreams[pushId] = AWAITING_STREAM_ESTABLISHMENT;
            }
        }
        if (!replaced)
        {
            throw new ArgumentException("Unknown push ID: " + pushId.ToString(CultureInfo.InvariantCulture));
        }
    }

    private Http3PushStreamServerInitializer PushStreamInitializer(long pushId, IChannelHandler handler)
    {
        Http3PushStreamServerInitializer initializer;
        if (handler is Http3PushStreamServerInitializer)
        {
            initializer = (Http3PushStreamServerInitializer) handler;
        }
        else
        {
            initializer = null;
        }
        return new PushStreamInitializerImpl(this, pushId, initializer, handler);
    }

    private static void SetupCancelPushIfStreamCreationFails(long pushId, Task<IQuicStreamChannel> future,
                                                             IQuicChannel channel)
    {
        if (future.IsCompleted)
        {
            SendCancelPushIfFailed(future, pushId, channel);
        }
        else
        {
            _ = future.ContinueWith(f => SendCancelPushIfFailed(future, pushId, channel), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    private static void SendCancelPushIfFailed(Task<IQuicStreamChannel> future, long pushId, IQuicChannel channel)
    {
        // https://quicwg.org/base-drafts/draft-ietf-quic-http.html#name-cancel_push
        // If we can not establish the stream, we can not send the promised push response, so send a CANCEL_PUSH
        if (!future.IsCompletedSuccessfully)
        {
            IQuicStreamChannel localControlStream = Http3.GetLocalControlStream(channel);
            Debug.Assert(localControlStream != null);
            _ = localControlStream.WriteAndFlushAsync(new DefaultHttp3CancelPushFrame(pushId));
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of controlStreamListener.
    private sealed class ControlStreamListenerHandler : ChannelInboundHandlerAdapter
    {
        private readonly Http3ServerPushStreamManager manager;

        internal ControlStreamListenerHandler(Http3ServerPushStreamManager manager)
        {
            this.manager = manager;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp3CancelPushFrame)
            {
                long pushId = ((IHttp3CancelPushFrame) msg).Id();
                if (pushId >= Volatile.Read(ref manager.nextId))
                {
                    ConnectionError(ctx, H3_ID_ERROR, "CANCEL_PUSH id greater than the last known id", true);
                    return;
                }

                // CLR: pushStreams.computeIfPresent(pushId, (id, existing) -> ...) under the lock (see the type
                // comment).
                lock (manager.pushStreams)
                {
                    if (manager.pushStreams.TryGetValue(pushId, out object existing))
                    {
                        long id = pushId;
                        if (existing == AWAITING_STREAM_ESTABLISHMENT)
                        {
                            manager.pushStreams[id] = CANCELLED_STREAM;
                        }
                        else
                        {
                            if (existing == PUSH_ID_GENERATED)
                            {
                                throw new InvalidOperationException("Unexpected push stream state " + existing +
                                        " for pushId: " + id.ToString(CultureInfo.InvariantCulture));
                            }
                            Debug.Assert(existing is IQuicStreamChannel);
                            _ = ((IQuicStreamChannel) existing).CloseAsync();
                            // remove the push stream from the map.
                            manager.pushStreams.Remove(id);
                        }
                    }
                }
            }
            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: the anonymous Http3PushStreamServerInitializer of pushStreamInitializer(pushId, handler).
    private sealed class PushStreamInitializerImpl : Http3PushStreamServerInitializer
    {
        private readonly Http3ServerPushStreamManager manager;
        private readonly long pushId;
        private readonly Http3PushStreamServerInitializer initializer;
        private readonly IChannelHandler handler;

        internal PushStreamInitializerImpl(Http3ServerPushStreamManager manager, long pushId,
                                           Http3PushStreamServerInitializer initializer, IChannelHandler handler)
            : base(pushId)
        {
            this.manager = manager;
            this.pushId = pushId;
            this.initializer = initializer;
            this.handler = handler;
        }

        protected internal override void InitPushStream(IQuicStreamChannel ch)
        {
            ch.Pipeline.AddLast(new PushStreamStateHandler(manager, pushId, ch));
            if (initializer != null)
            {
                initializer.InitPushStream(ch);
            }
            else if (handler != null)
            {
                ch.Pipeline.AddLast(handler);
            }
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter that PushStreamInitializerImpl.InitPushStream adds.
    private sealed class PushStreamStateHandler : ChannelInboundHandlerAdapter
    {
        private readonly Http3ServerPushStreamManager manager;
        private readonly long pushId;
        private readonly IQuicStreamChannel ch;
        private bool stateUpdated;

        internal PushStreamStateHandler(Http3ServerPushStreamManager manager, long pushId, IQuicStreamChannel ch)
        {
            this.manager = manager;
            this.pushId = pushId;
            this.ch = ch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!stateUpdated)
            {
                UpdatePushStreamsMap();
            }
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            if (!stateUpdated && ctx.Channel.IsActive)
            {
                UpdatePushStreamsMap();
            }
        }

        private void UpdatePushStreamsMap()
        {
            Debug.Assert(!stateUpdated);
            stateUpdated = true;
            // CLR: pushStreams.compute(pushId, (id, existing) -> ...) under the lock (see the type comment).
            lock (manager.pushStreams)
            {
                manager.pushStreams.TryGetValue(pushId, out object existing);
                long id = pushId;
                if (existing == AWAITING_STREAM_ESTABLISHMENT)
                {
                    manager.pushStreams[id] = ch;
                    return;
                }
                if (existing == CANCELLED_STREAM)
                {
                    _ = ch.CloseAsync();
                    manager.pushStreams.Remove(id);
                    return; // remove push stream.
                }
                throw new InvalidOperationException("Unexpected push stream state " +
                        (existing == null ? "null" : existing.ToString()) + " for pushId: " +
                        id.ToString(CultureInfo.InvariantCulture));
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownReadComplete.INSTANCE))
            {
                lock (manager.pushStreams)
                {
                    manager.pushStreams.Remove(pushId);
                }
            }
            ctx.FireUserEventTriggered(evt);
        }
    }
}
