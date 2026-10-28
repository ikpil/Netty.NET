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
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels.Embedded;

/// <summary>A real pipeline and outbound queue with in-memory transport endpoints for deterministic tests.</summary>
/**
 * Base class for {@link Channel} implementations that are used in an embedded fashion.
 */
public class EmbeddedChannel : AbstractChannel
{
    private readonly EmbeddedEventLoop _loop;
    private readonly Queue<object> _inbound = new();
    private readonly Queue<object> _outbound = new();
    private Exception _lastException;
    private int _executingStack;
    private readonly EndPoint _local = new EmbeddedSocketAddress("embedded");
    private readonly EndPoint _remote = new EmbeddedSocketAddress("embedded");
    /**
     * Create a new instance with the specified ID and an empty pipeline.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     */
    /**
     * Create a new instance with the pipeline initialized with the specified handlers.
     *
     * @param handlers the {@link ChannelHandler}s which will be add in the {@link ChannelPipeline}
     */
    /**
     * Create a new instance with the configuration from the given builder. This method is {@code protected} for use by
     * subclasses; Otherwise, please use {@link Builder#build()}.
     *
     * @param builder The builder
     */
    public EmbeddedChannel(params IChannelHandler[] handlers) : this(true, false, handlers) { }
    /**
     * Create a new instance with the pipeline initialized with the specified handlers.
     *
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(bool hasDisconnect, params IChannelHandler[] handlers) : this(true, hasDisconnect, handlers) { }
    /**
     * Create a new instance with the pipeline initialized with the specified handlers.
     *
     * @param register {@code true} if this {@link Channel} is registered to the {@link EventLoop} in the
     *                 constructor. If {@code false} the user will need to call {@link #register()}.
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param hasDisconnect {@code false} if this {@link Channel} will delegate {@link #disconnect()}
     *                      to {@link #close()}, {@code true} otherwise.
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(bool register, bool hasDisconnect, params IChannelHandler[] handlers)
        : this(null, null, register, hasDisconnect, null, handlers) { }
    /**
     * Create a new instance with the channel ID set to the given ID and the pipeline
     * initialized with the specified handlers.
     *
     * @param channelId the {@link ChannelId} that will be used to identify this channel
     * @param handlers the {@link ChannelHandler}s which will be added to the {@link ChannelPipeline}
     */
    public EmbeddedChannel(IChannelId id, params IChannelHandler[] handlers) : this(null, id, true, false, null, handlers) { }
    public EmbeddedChannel(Ticker ticker, params IChannelHandler[] handlers) : this(null, null, true, false, ticker, handlers) { }
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
    public EmbeddedChannel(IChannel parent, IChannelId id, bool register, bool hasDisconnect, params IChannelHandler[] handlers)
        : this(parent, id, register, hasDisconnect, null, handlers) { }
    private EmbeddedChannel(IChannel parent, IChannelId id, bool register, bool hasDisconnect, Ticker ticker, IChannelHandler[] handlers)
        : base(parent, new ChannelMetadata(hasDisconnect), id)
    {
        _loop = new EmbeddedEventLoop(ticker);
        Pipeline.AddLast(handlers);
        if (register)
            Register();
    }
    public override EndPoint LocalAddress => IsActive ? _local : null;
    public override EndPoint RemoteAddress => IsActive ? _remote : null;
    protected override void DoRegister() => SetActive(true);
    protected override bool IsCompatible(IEventLoop eventLoop) => eventLoop is EmbeddedEventLoop;
    public void Register() { RegisterAsync(_loop).GetAwaiter().GetResult(); }
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken) => Task.CompletedTask;
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken) => Task.CompletedTask;
    protected override Task DoDisconnectAsync(CancellationToken cancellationToken) { SetActive(false); return Task.CompletedTask; }
    protected override void DoClose() { }
    protected override void DoBeginRead() { }
    protected override Task DoWriteAsync(object message)
    {
        ReferenceCountUtil.Retain(message);
        HandleOutboundMessage(message);
        return Task.CompletedTask;
    }
    /**
     * Called for each outbound message.
     *
     * @see #doWrite(ChannelOutboundBuffer)
     */
    protected virtual void HandleOutboundMessage(object message) => _outbound.Enqueue(message);
    /**
     * Called for each inbound message.
     */
    protected virtual void HandleInboundMessage(object message) => _inbound.Enqueue(message);
    protected internal override void OnUnhandledInboundMessage(object message) => HandleInboundMessage(message);
    protected internal override void OnUnhandledException(Exception error) => Interlocked.CompareExchange(ref _lastException, error, null);
    /**
     * Returns the {@link Queue} which holds all the {@link Object}s that were received by this {@link Channel}.
     */
    public Queue<object> InboundMessages => _inbound;
    /**
     * Returns the {@link Queue} which holds all the {@link Object}s that were written by this {@link Channel}.
     */
    public Queue<object> OutboundMessages => _outbound;
    /**
     * Return received data from this {@link Channel}
     */
    public T ReadInbound<T>() => _inbound.TryDequeue(out var value) ? (T)value : default;
    /**
     * Read data from the outbound. This may return {@code null} if nothing is readable.
     */
    public T ReadOutbound<T>() => _outbound.TryDequeue(out var value) ? (T)value : default;
    public object ReadInbound() => ReadInbound<object>();
    public object ReadOutbound() => ReadOutbound<object>();
    /**
     * Write messages to the inbound of this {@link Channel}.
     *
     * @param msgs the messages to be written
     *
     * @return {@code true} if the write operation did add something to the inbound buffer
     */
    public bool WriteInbound(params object[] messages)
    {
        EnsureOpen();
        _executingStack++;
        try
        { foreach (var message in messages) { if (message == null) break; Pipeline.FireChannelRead(message); } Pipeline.FireChannelReadComplete(); }
        finally { _executingStack--; MaybeRunPendingTasks(); }
        CheckException();
        return _inbound.Count != 0;
    }
    /**
     * Write messages to the outbound of this {@link Channel}.
     *
     * @param msgs              the messages to be written
     * @return bufferReadable   returns {@code true} if the write operation did add something to the outbound buffer
     */
    public bool WriteOutbound(params object[] messages)
    {
        EnsureOpen();
        var writes = new List<Task>();
        _executingStack++;
        try
        {
            foreach (var message in messages)
            { if (message == null) break; writes.Add(WriteAsync(message)); }
            RunPendingTasks();
            Flush();
            foreach (var task in writes)
            {
                if (task.IsCompleted)
                { try { task.GetAwaiter().GetResult(); } catch (Exception error) { OnUnhandledException(error); } }
                else
                    _ = ObserveAsync(task);
            }
        }
        finally { _executingStack--; MaybeRunPendingTasks(); }
        CheckException();
        return _outbound.Count != 0;
    }
    private async Task ObserveAsync(Task operation)
    { try { await operation.ConfigureAwait(false); } catch (Exception error) { OnUnhandledException(error); } }
    private EmbeddedEventLoop Loop => AssignedEventLoop as EmbeddedEventLoop ?? _loop;
    private void MaybeRunPendingTasks() { if (_executingStack == 0) RunPendingTasks(); }
    /**
     * Run all tasks (which also includes scheduled tasks) that are pending in the {@link EventLoop}
     * for this {@link Channel}
     */
    public void RunPendingTasks()
    {
        try
        { Loop.RunTasks(); }
        catch (Exception error) { OnUnhandledException(error); }
        try
        { Loop.RunScheduledTasks(); }
        catch (Exception error) { OnUnhandledException(error); }
    }
    /**
     * Run all pending scheduled tasks in the {@link EventLoop} for this {@link Channel} and return the
     * {@code nanoseconds} when the next scheduled task is ready to run. If no other task was scheduled it will return
     * {@code -1}.
     */
    public long RunScheduledPendingTasks()
    { try { return Loop.RunScheduledTasks(); } catch (Exception error) { OnUnhandledException(error); return Loop.NextScheduledTask(); } }
    /**
     * Check whether this channel has any pending tasks that would be executed by a call to {@link #runPendingTasks()}.
     * This includes normal tasks, and scheduled tasks where the deadline has expired. If this method returns
     * {@code false}, a call to {@link #runPendingTasks()} would do nothing.
     *
     * @return {@code true} if there are any pending tasks, {@code false} otherwise.
     */
    public bool HasPendingTasks => Loop.HasPendingTasks || Loop.NextScheduledTask() == 0;
    public void AdvanceTimeBy(TimeSpan amount) { if (Loop.Ticker() is not MockTicker ticker) throw new NotSupportedException(); ticker.Advance(amount); }
    /**
     * Freeze the clock of this channel's event loop. Any scheduled tasks that are not already due will not run on
     * future {@link #runScheduledPendingTasks()} calls. While the event loop is frozen, it is still possible to
     * {@link #advanceTimeBy(long, TimeUnit) advance time} manually so that scheduled tasks execute.
     */
    public void FreezeTime() { if (Loop.Ticker() is not EmbeddedEventLoop.FreezableTicker ticker) throw new NotSupportedException(); ticker.Freeze(); }
    /**
     * Unfreeze an event loop that was {@link #freezeTime() frozen}. Time will continue at the point where
     * {@link #freezeTime()} stopped it: if a task was scheduled ten minutes in the future and {@link #freezeTime()}
     * was called, it will run ten minutes after this method is called again (assuming no
     * {@link #advanceTimeBy(long, TimeUnit)} calls, and assuming pending scheduled tasks are run at that time using
     * {@link #runScheduledPendingTasks()}).
     */
    public void UnfreezeTime() { if (Loop.Ticker() is not EmbeddedEventLoop.FreezableTicker ticker) throw new NotSupportedException(); ticker.Unfreeze(); }
    public Task WriteOneInboundAsync(object message)
    {
        EnsureOpen();
        _executingStack++;
        try
        { Pipeline.FireChannelRead(message); }
        finally { _executingStack--; MaybeRunPendingTasks(); }
        var error = Interlocked.Exchange(ref _lastException, null);
        return error == null ? Task.CompletedTask : Task.FromException(error);
    }
    public Task WriteOneOutboundAsync(object message)
    {
        EnsureOpen();
        _executingStack++;
        try
        { return WriteAsync(message); }
        finally { _executingStack--; MaybeRunPendingTasks(); }
    }
    /**
     * Flushes the inbound of this {@link Channel}. This method is conceptually equivalent to {@link #flush()}.
     *
     * @see #flushOutbound()
     */
    public EmbeddedChannel FlushInbound() { EnsureOpen(); Pipeline.FireChannelReadComplete(); MaybeRunPendingTasks(); CheckException(); return this; }
    /**
     * Flushes the outbound of this {@link Channel}. This method is conceptually equivalent to {@link #flush()}.
     *
     * @see #flushInbound()
     */
    public EmbeddedChannel FlushOutbound() { EnsureOpen(); RunPendingTasks(); Flush(); MaybeRunPendingTasks(); CheckException(); return this; }
    public override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_executingStack == 0)
            RunPendingTasks();
        _executingStack++;
        try
        { return base.CloseAsync(cancellationToken); }
        finally { _executingStack--; MaybeRunPendingTasks(); Loop.CancelTasks(); }
    }
    internal override Task CloseTransportAsync()
    {
        _executingStack++;
        try
        { return base.CloseTransportAsync(); }
        finally { _executingStack--; MaybeRunPendingTasks(); }
    }
    internal override Task DisconnectTransportAsync(CancellationToken cancellationToken)
    {
        _executingStack++;
        try
        { return base.DisconnectTransportAsync(cancellationToken); }
        finally { _executingStack--; MaybeRunPendingTasks(); }
    }
    /**
     * Checks for the presence of an {@link Exception}.
     */
    /**
     * Check if there was any {@link Throwable} received and if so rethrow it.
     */
    public void CheckException()
    {
        var error = Interlocked.Exchange(ref _lastException, null);
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
    /**
     * Mark this {@link Channel} as finished. Any further try to write data to it will fail.
     *
     * @return bufferReadable returns {@code true} if any of the used buffers has something left to read
     */
    public bool Finish()
    {
        CloseAsync().GetAwaiter().GetResult();
        RunPendingTasks();
        Loop.CancelTasks();
        CheckException();
        return _inbound.Count != 0 || _outbound.Count != 0;
    }
    /**
     * Mark this {@link Channel} as finished and release all pending message in the inbound and outbound buffer.
     * Any further try to write data to it will fail.
     *
     * @return bufferReadable returns {@code true} if any of the used buffers has something left to read
     */
    public bool FinishAndReleaseAll()
    {
        try
        { return Finish(); }
        finally { ReleaseInbound(); ReleaseOutbound(); }
    }
    /**
     * Release all buffered inbound messages and return {@code true} if any were in the inbound buffer, {@code false}
     * otherwise.
     */
    public bool ReleaseInbound() => ReleaseAll(_inbound);
    /**
     * Release all buffered outbound messages and return {@code true} if any were in the outbound buffer, {@code false}
     * otherwise.
     */
    public bool ReleaseOutbound() => ReleaseAll(_outbound);
    private static bool ReleaseAll(Queue<object> messages)
    {
        bool any = messages.Count != 0;
        while (messages.TryDequeue(out var message))
            ReferenceCountUtil.Release(message);
        return any;
    }
}
