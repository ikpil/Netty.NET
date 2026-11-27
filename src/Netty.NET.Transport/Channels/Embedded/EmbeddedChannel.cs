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
using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels.Embedded;

/// <summary>A real pipeline and outbound queue with in-memory transport endpoints for deterministic tests.</summary>
/**
 * Base class for {@link Channel} implementations that are used in an embedded fashion.
 */
// CLR: AbstractChannel owns the open/active flags that the source keeps in the State enum (OPEN, ACTIVE, CLOSED).
public class EmbeddedChannel : AbstractChannel
{
    private static readonly EndPoint LOCAL_ADDRESS = new EmbeddedSocketAddress();
    private static readonly EndPoint REMOTE_ADDRESS = new EmbeddedSocketAddress();

    private static readonly IChannelHandler[] EMPTY_HANDLERS = Array.Empty<IChannelHandler>();

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(EmbeddedChannel));

    private static readonly ChannelMetadata METADATA_NO_DISCONNECT = new ChannelMetadata(false);
    private static readonly ChannelMetadata METADATA_DISCONNECT = new ChannelMetadata(true);

    private readonly EmbeddedEventLoop _loop;
    // CLR: a Task continuation replaces the ChannelFutureListener
    // (https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.continuewith).
    private readonly Action<Task> _recordExceptionListener;

    private readonly IChannelConfig _config;

    private readonly Queue<object> _inboundMessages = new();
    private readonly Queue<object> _outboundMessages = new();
    private Exception _lastException;
    private int _executingStackCnt;
    private bool _cancelRemainingScheduledTasks;

    /**
     * Create a new instance with an {@link EmbeddedChannelId} and an empty pipeline.
     */
    public EmbeddedChannel() : this(new Builder())
    {
    }

    /**
     * Create a new instance with the specified ID and an empty pipeline.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     */
    public EmbeddedChannel(IChannelId channelId) : this(new Builder().ChannelId(channelId))
    {
    }

    /**
     * Create a new instance with the pipeline initialized with the specified handlers.
     *
     * @param handlers the {@link ChannelHandler}s which will be add in the {@link ChannelPipeline}
     */
    public EmbeddedChannel(params IChannelHandler[] handlers) : this(new Builder().Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the pipeline initialized with the specified handlers.
     *
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(bool hasDisconnect, params IChannelHandler[] handlers)
        : this(new Builder().HasDisconnect(hasDisconnect).Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the pipeline initialized with the specified handlers.
     *
     * @param register {@code true} if this {@link Channel} is registered to the {@link EventLoop} in the
     *                 constructor. If {@code false} the user will need to call {@link #register()}.
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(bool register, bool hasDisconnect, params IChannelHandler[] handlers)
        : this(new Builder().Register(register).HasDisconnect(hasDisconnect).Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(IChannelId channelId, params IChannelHandler[] handlers)
        : this(new Builder().ChannelId(channelId).Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(IChannelId channelId, bool hasDisconnect, params IChannelHandler[] handlers)
        : this(new Builder().ChannelId(channelId).HasDisconnect(hasDisconnect).Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param register {@code true} if this {@link Channel} is registered to the {@link EventLoop} in the
     *                 constructor. If {@code false} the user will need to call {@link #register()}.
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(IChannelId channelId, bool register, bool hasDisconnect,
                           params IChannelHandler[] handlers)
        : this(new Builder().ChannelId(channelId).Register(register).HasDisconnect(hasDisconnect).Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param parent    the parent {@link Channel} of this {@link EmbeddedChannel}.
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param register {@code true} if this {@link Channel} is registered to the {@link EventLoop} in the
     *                 constructor. If {@code false} the user will need to call {@link #register()}.
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(IChannel parent, IChannelId channelId, bool register, bool hasDisconnect,
                           params IChannelHandler[] handlers)
        : this(new Builder()
                .Parent(parent)
                .ChannelId(channelId)
                .Register(register)
                .HasDisconnect(hasDisconnect)
                .Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param config the {@link ChannelConfig} which will be returned by {@link #config()}.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(IChannelId channelId, bool hasDisconnect, IChannelConfig config,
                           params IChannelHandler[] handlers)
        : this(new Builder().ChannelId(channelId).HasDisconnect(hasDisconnect).Config(config).Handlers(handlers))
    {
    }

    /**
     * Create a new instance with the configuration from the given builder. This method is {@code protected} for use by
     * subclasses; Otherwise, please use {@link Builder#build()}.
     *
     * @param builder The builder
     */
    // CLR: the metadata is an AbstractChannel constructor argument; metadata(hasDisconnect) selects it inline.
    protected EmbeddedChannel(Builder builder)
        : base(builder.parent, builder.hasDisconnect ? METADATA_DISCONNECT : METADATA_NO_DISCONNECT, builder.channelId)
    {
        _recordExceptionListener = RecordException;
        _loop = new EmbeddedEventLoop(builder.ticker == null ? new EmbeddedEventLoop.FreezableTicker() : builder.ticker);
        _config = builder.config == null ? new DefaultChannelConfig(this) : builder.config;
        if (builder.handler == null)
        {
            Setup(builder.register, builder.handlers);
        }
        else
        {
            Setup(builder.register, builder.handler);
        }
    }

    private void Setup(bool register, params IChannelHandler[] handlers)
    {
        IChannelPipeline p = Pipeline;
        p.AddLast(new SetupInitializer(handlers));
        if (register)
        {
            Task future = _loop.RegisterAsync(this);
            Debug.Assert(future.IsCompleted);
        }
    }

    private void Setup(bool register, IChannelHandler handler)
    {
        IChannelPipeline p = Pipeline;
        p.AddLast(handler);
        if (register)
        {
            Task future = _loop.RegisterAsync(this);
            Debug.Assert(future.IsCompleted);
        }
    }

    // CLR: the source's anonymous ChannelInitializer<Channel> in setup(boolean, ChannelHandler...).
    private sealed class SetupInitializer : ChannelInitializer<IChannel>
    {
        private readonly IChannelHandler[] _handlers;

        internal SetupInitializer(IChannelHandler[] handlers)
        {
            _handlers = handlers;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;
            foreach (IChannelHandler h in _handlers)
            {
                if (h == null)
                {
                    break;
                }
                pipeline.AddLast(h);
            }
        }
    }

    /**
     * Register this {@code Channel} on its {@link EventLoop}.
     */
    public void Register()
    {
        Task future = _loop.RegisterAsync(this);
        Debug.Assert(future.IsCompleted);
        if (future.IsFaulted)
        {
            ExceptionDispatchInfo.Throw(future.Exception!.InnerException!);
        }
    }

    protected sealed override DefaultChannelPipeline NewChannelPipeline()
    {
        return new EmbeddedChannelPipeline(this);
    }

    // CLR: AbstractChannel.Config reads NewConfig() lazily, after the constructor stored the builder's config.
    protected override IChannelConfig NewConfig()
    {
        return _config;
    }

    /**
     * Returns the {@link Queue} which holds all the {@link Object}s that were received by this {@link Channel}.
     */
    public Queue<object> InboundMessages()
    {
        return _inboundMessages;
    }

    /**
     * Returns the {@link Queue} which holds all the {@link Object}s that were written by this {@link Channel}.
     */
    public Queue<object> OutboundMessages()
    {
        return _outboundMessages;
    }

    /**
     * Return received data from this {@link Channel}
     */
    public T ReadInbound<T>()
    {
        // CLR: an empty queue yields default(T), the null of a reference type T.
        object polled = Poll(_inboundMessages);
        T message = polled == null ? default : (T)polled;
        if (message != null)
        {
            ReferenceCountUtil.Touch(message, "Caller of readInbound() will handle the message from this point");
        }
        return message;
    }

    /**
     * Read data from the outbound. This may return {@code null} if nothing is readable.
     */
    public T ReadOutbound<T>()
    {
        object polled = Poll(_outboundMessages);
        T message = polled == null ? default : (T)polled;
        if (message != null)
        {
            ReferenceCountUtil.Touch(message, "Caller of readOutbound() will handle the message from this point.");
        }
        return message;
    }

    // CLR: the untyped forms of the generic <T> T readInbound()/readOutbound().
    public object ReadInbound()
    {
        return ReadInbound<object>();
    }

    public object ReadOutbound()
    {
        return ReadOutbound<object>();
    }

    /**
     * Write messages to the inbound of this {@link Channel}.
     *
     * @param msgs the messages to be written
     *
     * @return {@code true} if the write operation did add something to the inbound buffer
     */
    public bool WriteInbound(params object[] msgs)
    {
        EnsureOpen();
        if (msgs.Length == 0)
        {
            return IsNotEmpty(_inboundMessages);
        }

        _executingStackCnt++;
        try
        {
            IChannelPipeline p = Pipeline;
            foreach (object m in msgs)
            {
                p.FireChannelRead(m);
            }

            FlushInbound(false);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
        return IsNotEmpty(_inboundMessages);
    }

    /**
     * Writes one message to the inbound of this {@link Channel} and does not flush it. This
     * method is conceptually equivalent to {@link #write(Object)}.
     *
     * @see #writeOneOutbound(Object)
     */
    // CLR: writeOneInbound(msg, newPromise()) inlined; the returned Task carries checkException(promise).
    public Task WriteOneInboundAsync(object msg)
    {
        _executingStackCnt++;
        try
        {
            if (CheckOpen(true))
            {
                Pipeline.FireChannelRead(msg);
            }
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
        return CheckException(false);
    }

    /**
     * Flushes the inbound of this {@link Channel}. This method is conceptually equivalent to {@link #flush()}.
     *
     * @see #flushOutbound()
     */
    public EmbeddedChannel FlushInbound()
    {
        FlushInbound(true);
        return this;
    }

    // CLR: flushInbound(recordException, voidPromise()): every caller passes the void promise, which rethrows.
    private void FlushInbound(bool recordException)
    {
        _executingStackCnt++;
        try
        {
            if (CheckOpen(recordException))
            {
                Pipeline.FireChannelReadComplete();
                RunPendingTasks();
            }
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }

        CheckException(true);
    }

    /**
     * Write messages to the outbound of this {@link Channel}.
     *
     * @param msgs              the messages to be written
     * @return bufferReadable   returns {@code true} if the write operation did add something to the outbound buffer
     */
    public bool WriteOutbound(params object[] msgs)
    {
        EnsureOpen();
        if (msgs.Length == 0)
        {
            return IsNotEmpty(_outboundMessages);
        }

        _executingStackCnt++;
        RecyclableArrayList futures = RecyclableArrayList.NewInstance(msgs.Length);
        try
        {
            try
            {
                foreach (object m in msgs)
                {
                    if (m == null)
                    {
                        break;
                    }
                    futures.Add(WriteAsync(m));
                }

                FlushOutbound0();

                int size = futures.Count;
                for (int i = 0; i < size; i++)
                {
                    Task future = (Task)futures[i];
                    if (future.IsCompleted)
                    {
                        RecordException(future);
                    }
                    else
                    {
                        // The write may be delayed to run later by runPendingTasks()
                        future.ContinueWith(_recordExceptionListener, CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
            }
            finally
            {
                _executingStackCnt--;
                MaybeRunPendingTasks();
            }
            CheckException();
            return IsNotEmpty(_outboundMessages);
        }
        finally
        {
            futures.Recycle();
        }
    }

    /**
     * Writes one message to the outbound of this {@link Channel} and does not flush it. This
     * method is conceptually equivalent to {@link #write(Object)}.
     *
     * @see #writeOneInbound(Object)
     */
    // CLR: writeOneOutbound(msg, newPromise()) inlined; the returned Task is the write or checkException(promise).
    public Task WriteOneOutboundAsync(object msg)
    {
        _executingStackCnt++;
        try
        {
            if (CheckOpen(true))
            {
                return WriteAsync(msg);
            }
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }

        return CheckException(false);
    }

    /**
     * Flushes the outbound of this {@link Channel}. This method is conceptually equivalent to {@link #flush()}.
     *
     * @see #flushInbound()
     */
    public EmbeddedChannel FlushOutbound()
    {
        _executingStackCnt++;
        try
        {
            if (CheckOpen(true))
            {
                FlushOutbound0();
            }
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
        CheckException(true);
        return this;
    }

    private void FlushOutbound0()
    {
        // We need to call runPendingTasks first as a ChannelOutboundHandler may used eventloop.execute(...) to
        // delay the write on the next eventloop run.
        RunPendingTasks();

        Flush();
    }

    /**
     * Mark this {@link Channel} as finished. Any further try to write data to it will fail.
     *
     * @return bufferReadable returns {@code true} if any of the used buffers has something left to read
     */
    public bool Finish()
    {
        return Finish(false);
    }

    /**
     * Mark this {@link Channel} as finished and release all pending message in the inbound and outbound buffer.
     * Any further try to write data to it will fail.
     *
     * @return bufferReadable returns {@code true} if any of the used buffers has something left to read
     */
    // CLR: virtual because the Java method is public and non-final; codec-http2's CompressorHttp2ConnectionEncoderTest
    // overrides it to count the calls (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual).
    public virtual bool FinishAndReleaseAll()
    {
        return Finish(true);
    }

    /**
     * Mark this {@link Channel} as finished. Any further try to write data to it will fail.
     *
     * @param releaseAll if {@code true} all pending message in the inbound and outbound buffer are released.
     * @return bufferReadable returns {@code true} if any of the used buffers has something left to read
     */
    private bool Finish(bool releaseAll)
    {
        _executingStackCnt++;
        try
        {
            _ = CloseAsync();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
        try
        {
            CheckException();
            return IsNotEmpty(_inboundMessages) || IsNotEmpty(_outboundMessages);
        }
        finally
        {
            if (releaseAll)
            {
                ReleaseAll(_inboundMessages);
                ReleaseAll(_outboundMessages);
            }
        }
    }

    /**
     * Release all buffered inbound messages and return {@code true} if any were in the inbound buffer, {@code false}
     * otherwise.
     */
    public bool ReleaseInbound()
    {
        return ReleaseAll(_inboundMessages);
    }

    /**
     * Release all buffered outbound messages and return {@code true} if any were in the outbound buffer, {@code false}
     * otherwise.
     */
    public bool ReleaseOutbound()
    {
        return ReleaseAll(_outboundMessages);
    }

    private static bool ReleaseAll(Queue<object> queue)
    {
        if (IsNotEmpty(queue))
        {
            for (; ; )
            {
                object msg = Poll(queue);
                if (msg == null)
                {
                    break;
                }
                ReferenceCountUtil.Release(msg);
            }
            return true;
        }
        return false;
    }

    // CLR: close() and close(promise) are the one Task-returning CloseAsync.
    public sealed override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        // We need to call runPendingTasks() before calling super.close() as there may be something in the queue
        // that needs to be run before the actual close takes place.
        _executingStackCnt++;
        Task future;
        try
        {
            RunPendingTasks();
            future = base.CloseAsync(cancellationToken);

            _cancelRemainingScheduledTasks = true;
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
        return future;
    }

    public sealed override Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        Task future;
        try
        {
            future = base.DisconnectAsync(cancellationToken);

            if (!Metadata.HasDisconnect())
            {
                _cancelRemainingScheduledTasks = true;
            }
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
        return future;
    }

    public override Task BindAsync(EndPoint address, CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        try
        {
            return base.BindAsync(address, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    public override Task ConnectAsync(EndPoint remote, EndPoint localAddress = null, CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        try
        {
            return base.ConnectAsync(remote, localAddress, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    public override Task DeregisterAsync(CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        try
        {
            return base.DeregisterAsync(cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    public override IChannelOutboundInvoker Flush()
    {
        _executingStackCnt++;
        try
        {
            return base.Flush();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    public override IChannelOutboundInvoker Read()
    {
        _executingStackCnt++;
        try
        {
            return base.Read();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    public override Task WriteAsync(object msg, CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        try
        {
            return base.WriteAsync(msg, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    public override Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        try
        {
            return base.WriteAndFlushAsync(msg, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    private static bool IsNotEmpty(Queue<object> queue)
    {
        return queue != null && queue.Count != 0;
    }

    private static object Poll(Queue<object> queue)
    {
        return queue != null && queue.TryDequeue(out object msg) ? msg : null;
    }

    private void MaybeRunPendingTasks()
    {
        if (_executingStackCnt == 0)
        {
            RunPendingTasks();

            if (_cancelRemainingScheduledTasks)
            {
                // Cancel all scheduled tasks that are left.
                EmbeddedEventLoop().CancelTasks();
            }
        }
    }

    /**
     * Run all tasks (which also includes scheduled tasks) that are pending in the {@link EventLoop}
     * for this {@link Channel}
     */
    public void RunPendingTasks()
    {
        try
        {
            EmbeddedEventLoop().RunTasks();
        }
        catch (Exception e)
        {
            RecordException(e);
        }

        try
        {
            EmbeddedEventLoop().RunScheduledTasks();
        }
        catch (Exception e)
        {
            RecordException(e);
        }
    }

    /**
     * Check whether this channel has any pending tasks that would be executed by a call to {@link #runPendingTasks()}.
     * This includes normal tasks, and scheduled tasks where the deadline has expired. If this method returns
     * {@code false}, a call to {@link #runPendingTasks()} would do nothing.
     *
     * @return {@code true} if there are any pending tasks, {@code false} otherwise.
     */
    public bool HasPendingTasks()
    {
        return EmbeddedEventLoop().HasPendingNormalTasks() ||
                EmbeddedEventLoop().NextScheduledTask() == 0;
    }

    /**
     * Run all pending scheduled tasks in the {@link EventLoop} for this {@link Channel} and return the
     * {@code nanoseconds} when the next scheduled task is ready to run. If no other task was scheduled it will return
     * {@code -1}.
     */
    public long RunScheduledPendingTasks()
    {
        try
        {
            return EmbeddedEventLoop().RunScheduledTasks();
        }
        catch (Exception e)
        {
            RecordException(e);
            return EmbeddedEventLoop().NextScheduledTask();
        }
    }

    private void RecordException(Task future)
    {
        if (!future.IsCompletedSuccessfully)
        {
            // CLR: GetResult() rethrows the cause (or a TaskCanceledException for a cancelled write).
            try
            {
                future.GetAwaiter().GetResult();
            }
            catch (Exception cause)
            {
                RecordException(cause);
            }
        }
    }

    // CLR: Interlocked keeps the first exception when a continuation records it from another thread
    // (https://learn.microsoft.com/dotnet/api/system.threading.interlocked.compareexchange).
    private void RecordException(Exception cause)
    {
        if (Interlocked.CompareExchange(ref _lastException, cause, null) != null)
        {
            logger.Warn(
                    "More than one exception was raised. " +
                            "Will report only the first one and log others.", cause);
        }
    }

    private EmbeddedEventLoop.FreezableTicker FreezableTicker()
    {
        Ticker ticker = EventLoop.Ticker();
        if (ticker is EmbeddedEventLoop.FreezableTicker freezableTicker)
        {
            return freezableTicker;
        }
        else
        {
            throw new InvalidOperationException(
                    "EmbeddedChannel constructed with custom ticker, time manipulation methods are unavailable.");
        }
    }

    /**
     * Advance the clock of the event loop of this channel by the given duration. Any scheduled tasks will execute
     * sooner by the given time (but {@link #runScheduledPendingTasks()} still needs to be called).
     */
    // CLR: a TimeSpan replaces the (long duration, TimeUnit unit) pair (docs/common-clr-design.md, "Monotonic time and
    // duration conversion").
    public void AdvanceTimeBy(TimeSpan amount)
    {
        FreezableTicker().Advance(amount);
    }

    /**
     * Freeze the clock of this channel's event loop. Any scheduled tasks that are not already due will not run on
     * future {@link #runScheduledPendingTasks()} calls. While the event loop is frozen, it is still possible to
     * {@link #advanceTimeBy(long, TimeUnit) advance time} manually so that scheduled tasks execute.
     */
    public void FreezeTime()
    {
        FreezableTicker().FreezeTime();
    }

    /**
     * Unfreeze an event loop that was {@link #freezeTime() frozen}. Time will continue at the point where
     * {@link #freezeTime()} stopped it: if a task was scheduled ten minutes in the future and {@link #freezeTime()}
     * was called, it will run ten minutes after this method is called again (assuming no
     * {@link #advanceTimeBy(long, TimeUnit)} calls, and assuming pending scheduled tasks are run at that time using
     * {@link #runScheduledPendingTasks()}).
     */
    public void UnfreezeTime()
    {
        FreezableTicker().UnfreezeTime();
    }

    /**
     * Checks for the presence of an {@link Exception}.
     */
    // CLR: voidPromise selects promise.isVoid(): the recorded exception is rethrown. Otherwise the returned Task
    // carries it, as promise.setFailure(t)/promise.setSuccess() would.
    private Task CheckException(bool voidPromise)
    {
        Exception t = Interlocked.Exchange(ref _lastException, null);
        if (t != null)
        {
            if (voidPromise)
            {
                ExceptionDispatchInfo.Throw(t);
            }

            return Task.FromException(t);
        }

        return Task.CompletedTask;
    }

    /**
     * Check if there was any {@link Throwable} received and if so rethrow it.
     */
    public void CheckException()
    {
        CheckException(true);
    }

    /**
     * Returns {@code true} if the {@link Channel} is open and records optionally
     * an {@link Exception} if it isn't.
     */
    private bool CheckOpen(bool recordException)
    {
        if (!IsOpen)
        {
            if (recordException)
            {
                RecordException(new ClosedChannelException());
            }
            return false;
        }

        return true;
    }

    private EmbeddedEventLoop EmbeddedEventLoop()
    {
        if (IsRegistered)
        {
            return (EmbeddedEventLoop)base.EventLoop;
        }

        return _loop;
    }

    /**
     * Ensure the {@link Channel} is open and if not throw an exception.
     */
    // CLR: hides AbstractChannel.EnsureOpen(), the folded AbstractUnsafe.ensureOpen(promise), which the source keeps
    // on the unsafe (new modifier, https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/new-modifier).
    protected new void EnsureOpen()
    {
        if (!CheckOpen(true))
        {
            CheckException();
        }
    }

    protected override bool IsCompatible(IEventLoop loop)
    {
        return loop is EmbeddedEventLoop;
    }

    protected override EndPoint LocalAddress0()
    {
        return IsActive ? LOCAL_ADDRESS : null;
    }

    protected override EndPoint RemoteAddress0()
    {
        return IsActive ? REMOTE_ADDRESS : null;
    }

    protected override void DoRegister()
    {
        SetActive(true);
    }

    protected override Task DoBindAsync(EndPoint localAddress, CancellationToken cancellationToken)
    {
        // NOOP
        return Task.CompletedTask;
    }

    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        SetActive(false);
        return Task.CompletedTask;
    }

    protected override void DoClose()
    {
    }

    protected override void DoBeginRead()
    {
        // NOOP
    }

    // CLR: the per-message form of doWrite(ChannelOutboundBuffer): AbstractChannel drains the buffer and removes the
    // entry once this Task completes.
    protected override Task DoWrite(object msg)
    {
        ReferenceCountUtil.Retain(msg);
        HandleOutboundMessage(msg);
        return Task.CompletedTask;
    }

    /**
     * Called for each outbound message.
     *
     * @see #doWrite(ChannelOutboundBuffer)
     */
    protected virtual void HandleOutboundMessage(object msg)
    {
        OutboundMessages().Enqueue(msg);
    }

    /**
     * Called for each inbound message.
     */
    protected virtual void HandleInboundMessage(object msg)
    {
        InboundMessages().Enqueue(msg);
    }

    // CLR: AbstractChannel reports failures of its own Task plumbing (deferred connect or write drains) here; the
    // embedded channel records them as recordException(Throwable) does.
    protected internal override void OnUnhandledException(Exception error)
    {
        RecordException(error);
    }

    // CLR: EmbeddedChannel.builder() cannot be declared beside the nested Builder type (CS0102,
    // https://learn.microsoft.com/dotnet/csharp/misc/cs0102), so new Builder() replaces it.
    public sealed class Builder
    {
        internal IChannel parent;
        internal IChannelId channelId = EmbeddedChannelId.Instance;
        internal bool register = true;
        internal bool hasDisconnect;
        //you should use either handlers or handler variable, but not both.
        internal IChannelHandler[] handlers = EMPTY_HANDLERS;
        internal IChannelHandler handler;
        internal IChannelConfig config;
        internal Ticker ticker;

        public Builder()
        {
        }

        /**
         * The parent {@link Channel} of this {@link EmbeddedChannel}.
         *
         * @param parent the parent {@link Channel} of this {@link EmbeddedChannel}.
         * @return This builder
         */
        public Builder Parent(IChannel parent)
        {
            this.parent = parent;
            return this;
        }

        /**
         * The {@link ChannelId} that will be used to identify this channel.
         *
         * @param channelId the {@link ChannelId} that will be used to identify this channel
         * @return This builder
         */
        public Builder ChannelId(IChannelId channelId)
        {
            this.channelId = ObjectUtil.CheckNotNull(channelId, "channelId");
            return this;
        }

        /**
         * {@code true} if this {@link Channel} is registered to the {@link EventLoop} in the constructor. If
         * {@code false} the user will need to call {@link #register()}.
         *
         * @param register {@code true} if this {@link Channel} is registered to the {@link EventLoop} in the
         *                 constructor. If {@code false} the user will need to call {@link #register()}.
         * @return This builder
         */
        public Builder Register(bool register)
        {
            this.register = register;
            return this;
        }

        /**
         * {@code false} if this {@link Channel} will delegate {@link #disconnect()} to {@link #close()}, {@code true}
         * otherwise.
         *
         * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()} to
         *                      {@link #close()}, {@code true} otherwise
         * @return This builder
         */
        public Builder HasDisconnect(bool hasDisconnect)
        {
            this.hasDisconnect = hasDisconnect;
            return this;
        }

        /**
         * The {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}.
         *
         * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
         * @return This builder
         */
        public Builder Handlers(params IChannelHandler[] handlers)
        {
            this.handlers = ObjectUtil.CheckNotNull(handlers, "handlers");
            this.handler = null;
            return this;
        }

        /**
         * The {@link ChannelHandler} which will be added to the {@link ChannelPipeline}.
         *
         * @param handler the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
         * @return This builder
         */
        public Builder Handlers(IChannelHandler handler)
        {
            this.handler = ObjectUtil.CheckNotNull(handler, "handler");
            this.handlers = null;
            return this;
        }

        /**
         * The {@link ChannelConfig} which will be returned by {@link #config()}.
         *
         * @param config the {@link ChannelConfig} which will be returned by {@link #config()}
         * @return This builder
         */
        public Builder Config(IChannelConfig config)
        {
            this.config = ObjectUtil.CheckNotNull(config, "config");
            return this;
        }

        /**
         * Configure a custom ticker for this event loop.
         *
         * @param ticker The custom ticker
         * @return This builder
         */
        public Builder Ticker(Ticker ticker)
        {
            this.ticker = ticker;
            return this;
        }

        /**
         * Create the channel. If you wish to extend {@link EmbeddedChannel}, please use the
         * {@link #EmbeddedChannel(Builder)} constructor instead.
         *
         * @return The channel
         */
        public EmbeddedChannel Build()
        {
            return new EmbeddedChannel(this);
        }
    }

    // EmbeddedUnsafe: Channel.Unsafe is folded into AbstractChannel (2026-11-16 owner decision), so the wrapped
    // unsafe is this set of overrides of the folded transport operations.
    // Delegates to the EmbeddedUnsafe instance but ensures runPendingTasks() is called after each operation
    // that may change the state of the Channel and may schedule tasks for later execution.
    public override Task RegisterAsync(IEventLoop eventLoop, CancellationToken cancellationToken = default)
    {
        _executingStackCnt++;
        try
        {
            return base.RegisterAsync(eventLoop, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override Task BindTransportAsync(EndPoint address, CancellationToken cancellationToken)
    {
        _executingStackCnt++;
        try
        {
            return base.BindTransportAsync(address, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override Task ConnectTransportAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        _executingStackCnt++;
        try
        {
            return base.ConnectTransportAsync(remote, local, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override Task DisconnectTransportAsync(CancellationToken cancellationToken)
    {
        _executingStackCnt++;
        try
        {
            return base.DisconnectTransportAsync(cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override Task Close()
    {
        _executingStackCnt++;
        try
        {
            return base.Close();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override void CloseForcibly()
    {
        _executingStackCnt++;
        try
        {
            base.CloseForcibly();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override Task DeregisterTransportAsync()
    {
        _executingStackCnt++;
        try
        {
            return base.DeregisterTransportAsync();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override void BeginRead()
    {
        _executingStackCnt++;
        try
        {
            base.BeginRead();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override Task WriteTransportAsync(object msg, CancellationToken cancellationToken)
    {
        _executingStackCnt++;
        try
        {
            return base.WriteTransportAsync(msg, cancellationToken);
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    protected internal override void FlushTransport()
    {
        _executingStackCnt++;
        try
        {
            base.FlushTransport();
        }
        finally
        {
            _executingStackCnt--;
            MaybeRunPendingTasks();
        }
    }

    // CLR: the source's inner class reaches the channel through EmbeddedChannel.this.
    private sealed class EmbeddedChannelPipeline : DefaultChannelPipeline
    {
        private readonly EmbeddedChannel _channel;

        internal EmbeddedChannelPipeline(EmbeddedChannel channel) : base(channel)
        {
            _channel = channel;
        }

        protected override void OnUnhandledInboundException(Exception cause)
        {
            _channel.RecordException(cause);
        }

        protected override void OnUnhandledInboundMessage(IChannelHandlerContext ctx, object msg)
        {
            _channel.HandleInboundMessage(msg);
        }
    }
}
