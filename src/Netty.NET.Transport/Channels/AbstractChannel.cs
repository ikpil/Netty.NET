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
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/// <summary>Channel lifecycle and queued writes. All transport state changes execute on the assigned event loop.</summary>
/**
 * A skeletal {@link Channel} implementation.
 */
public abstract class AbstractChannel : DefaultAttributeMap, IChannel
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(AbstractChannel));
    private readonly object _registrationGate = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lazy<IChannelConfig> _config;
    private volatile IEventLoop _eventLoop;
    private bool _registering;
    private int _registrationEpoch;
    private TaskCompletionSource _readResumed;
    private volatile bool _open = true;
    private volatile bool _active;
    private volatile bool _registered;
    private bool _everRegistered;
    private bool _writing;
    private IRecvByteBufAllocator.IHandle _receiveHandle;
    /**
     * Creates a new instance.
     *
     * @param parent
     *        the parent of this channel. {@code null} if there's no parent.
     */
    protected AbstractChannel(IChannel parent, ChannelMetadata metadata, IChannelId id = null)
    {
        Parent = parent;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Id = id ?? DefaultChannelId.NewInstance();
        _config = new Lazy<IChannelConfig>(NewConfig);
        Pipeline = NewChannelPipeline();
        OutboundBuffer = new ChannelOutboundBuffer(this);
    }
    public IChannelId Id { get; }
    public IChannel Parent { get; }
    public IEventLoop EventLoop => _eventLoop ?? throw new InvalidOperationException("Channel is not registered to an event loop.");
    protected IEventLoop AssignedEventLoop => _eventLoop;
    public IChannelConfig Config => _config.Value;
    public ChannelMetadata Metadata { get; }
    public DefaultChannelPipeline Pipeline { get; }
    public IByteBufAllocator Allocator => Config.GetAllocator();
    public bool IsOpen => _open;
    public bool IsRegistered => _registered;
    public bool IsActive => _active;
    public abstract EndPoint LocalAddress { get; }
    public abstract EndPoint RemoteAddress { get; }
    public Task CloseCompletion => _closed.Task;
    public ChannelOutboundBuffer OutboundBuffer { get; }
    public bool IsWritable => _open && !OutboundBuffer.IsClosed && OutboundBuffer.IsWritable;
    public long BytesBeforeUnwritable => _open && !OutboundBuffer.IsClosed ? OutboundBuffer.BytesBeforeUnwritable : 0;
    public long BytesBeforeWritable => _open && !OutboundBuffer.IsClosed ? OutboundBuffer.BytesBeforeWritable : long.MaxValue;
    protected virtual IChannelConfig NewConfig() => new DefaultChannelConfig(this);
    /**
     * Returns a new {@link DefaultChannelPipeline} instance.
     */
    protected virtual DefaultChannelPipeline NewChannelPipeline() => new(this);
    protected IRecvByteBufAllocator.IHandle ReceiveHandle => _receiveHandle ??= Config.GetRecvByteBufAllocator().NewHandle();
    protected void SetActive(bool active) => _active = active;
    /**
     * Return {@code true} if the given {@link EventLoop} is compatible with this instance.
     */
    protected virtual bool IsCompatible(IEventLoop eventLoop) => true;
    public Task RegisterAsync(IEventLoop eventLoop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventLoop);
        int epoch;
        lock (_registrationGate)
        {
            if (_registered || _registering)
                return Task.FromException(new InvalidOperationException("Channel is already registered."));
            if (!IsCompatible(eventLoop))
                return Task.FromException(new InvalidOperationException("Incompatible event loop."));
            _eventLoop = eventLoop;
            _registering = true;
            epoch = ++_registrationEpoch;
        }
        return RegisterCoreAsync();
        async Task RegisterCoreAsync()
        {
            try
            {
                await OnLoopAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureOpen();
                    DoRegister();
                    bool firstRegistration = !_everRegistered;
                    _everRegistered = true;
                    _registered = true;
                    // Registration is committed before user lifecycle callbacks; those callbacks may
                    // deregister and register this channel again, as in AbstractChannel.register0.
                    lock (_registrationGate)
                        _registering = false;
                    if (eventLoop is SingleThreadEventExecutor worker)
                        worker.AddShutdownHook(OnLoopShutdown);
                    Pipeline.Registered();
                    Pipeline.FireChannelRegistered();
                    if (_active)
                    {
                        if (firstRegistration)
                            Pipeline.FireChannelActive();
                        if (Config.IsAutoRead())
                            BeginRead();
                    }
                    ResumeRead();
                }).ConfigureAwait(false);
            }
            catch
            {
                // Registration rejection cannot rely on another task being admitted by the rejected executor.
                _open = _active = false;
                ResumeRead();
                try
                { DoClose(); }
                finally { OutboundBuffer.Close(new IOException("Channel registration failed.")); _closed.TrySetResult(); }
                throw;
            }
            finally
            {
                lock (_registrationGate)
                    if (epoch == _registrationEpoch)
                        _registering = false;
            }
        }
    }
    protected internal Task OnLoopAsync(Action action)
    {
        var loop = EventLoop;
        if (loop.InEventLoop())
        {
            try
            { action(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }
        return loop.SubmitAsync(() =>
        {
            // A native completion queued before deregistration may reach the old worker after
            // registration has moved. Follow the current owner before touching transport state.
            if (!ReferenceEquals(loop, _eventLoop))
                return OnLoopAsync(action);
            action();
            return Task.CompletedTask;
        });
    }
    protected void ResumeRead() => Interlocked.Exchange(ref _readResumed, null)?.TrySetResult();
    protected virtual void DoClearReadPending() { }
    /**
     * Set read pending to {@code false}.
     */
    internal void ClearReadPending()
    {
        if (!_registered)
        {
            // Best effort if we are not registered yet clear readPending. This happens during channel initialization.
            // NB: We only set the boolean field instead of calling clearReadPending0(), because the SelectionKey is
            // not set yet so it would produce an assertion failure.
            // CLR: there is no SelectionKey; each native backend clears its pending-read flag.
            DoClearReadPending();
        }
        else
            _ = OnLoopAsync(DoClearReadPending);
    }
    protected async Task DispatchReadAsync(Func<bool> readPending, Action read)
    {
        // Unlike readiness-based NIO, a native receive can consume input before AutoRead is disabled.
        // Keep that single completed operation's borrow until registration and read admission resume.
        // Never discard bytes/datagrams or dispatch user callbacks from a stale event-loop owner.
        for (; ; )
        {
            bool finished = false;
            Task resumed = null;
            await OnLoopAsync(() =>
            {
                if (!_open)
                { finished = true; return; }
                if (_registered && (Config.IsAutoRead() || readPending()))
                { read(); finished = true; }
                else
                {
                    var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    resumed = (Interlocked.CompareExchange(ref _readResumed, waiter, null) ?? waiter).Task;
                    // A forced close may run outside the owner during registration rejection.
                    // Recheck after publication so it cannot miss a newly installed waiter.
                    if (!_open)
                        ResumeRead();
                }
            }).ConfigureAwait(false);
            if (finished)
                return;
            await resumed.ConfigureAwait(false);
        }
    }
    protected internal void ExecuteOnLoop(Action action)
    { if (EventLoop.InEventLoop()) action(); else EventLoop.Execute(action); }
    private void OnLoopShutdown() => _ = CloseTransportAsync();
    internal void CloseForcibly()
    {
        _open = _active = false;
        ResumeRead();
        try
        { DoClose(); }
        finally { OutboundBuffer.Close(new IOException("Channel is closed.")); _closed.TrySetResult(); }
    }
    protected void EnsureOpen()
    { if (!_open) throw new IOException("Channel is closed."); }
    private Task Operation(Func<Task> operation, Action success)
    {
        Task task;
        try
        { EnsureOpen(); task = operation(); }
        catch (Exception error) { return Task.FromException(error); }
        if (task.IsCompleted)
        {
            try
            { task.GetAwaiter().GetResult(); success(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }
        return FinishAsync();
        async Task FinishAsync()
        {
            await task.ConfigureAwait(false);
            await OnLoopAsync(() => { EnsureOpen(); success(); }).ConfigureAwait(false);
        }
    }
    internal Task BindTransportAsync(EndPoint address, CancellationToken cancellationToken)
    {
        bool wasActive = _active;
        return Operation(() => DoBindAsync(address, cancellationToken), () =>
        {
            if (!wasActive && _active)
            { Pipeline.FireChannelActive(); if (Config.IsAutoRead()) BeginRead(); }
        });
    }
    internal Task ConnectTransportAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        bool wasActive = _active;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ConnectCoreAsync();
        return completion.Task;
        async Task ConnectCoreAsync()
        {
            try
            {
                EnsureOpen();
                await DoConnectAsync(remote, local, cancellationToken).ConfigureAwait(false);
                await OnLoopAsync(() =>
                {
                    EnsureOpen();
                    // Source connect completion precedes ChannelActive. Keep the native producer's asynchronous
                    // continuation policy; completion itself is committed on this owner.
                    completion.TrySetResult();
                    if (!wasActive && _active)
                    { Pipeline.FireChannelActive(); if (Config.IsAutoRead()) BeginRead(); }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled)
            {
                if (!completion.TrySetCanceled(canceled.CancellationToken))
                    OnUnhandledException(canceled);
            }
            catch (Exception error)
            {
                if (!completion.TrySetException(error))
                    OnUnhandledException(error);
            }
        }
    }
    internal virtual Task DisconnectTransportAsync(CancellationToken cancellationToken)
    {
        if (!Metadata.HasDisconnect())
            return CloseTransportAsync();
        bool wasActive = _active;
        return Operation(() => DoDisconnectAsync(cancellationToken), () =>
        { if (wasActive && !_active) Pipeline.FireChannelInactive(); });
    }
    internal virtual Task CloseTransportAsync()
    {
        if (!_open)
            return Task.CompletedTask;
        bool wasActive = _active;
        _open = _active = false;
        ResumeRead();
        Exception failure = null;
        try
        { DoClose(); }
        catch (Exception error) { failure = error; }
        OutboundBuffer.Close(new IOException("Channel is closed."));
        void CompleteClose()
        {
            try
            {
                if (wasActive)
                    Pipeline.FireChannelInactive();
                DeregisterTransportAsync().GetAwaiter().GetResult();
            }
            finally { _closed.TrySetResult(); }
        }
        // A handler closing its own channel must finish its current callback before handlerRemoved runs.
        // Shutdown hooks already run outside user callbacks and cannot submit after shutdown admission closes.
        if (EventLoop.IsShuttingDown())
            CompleteClose();
        else
            EventLoop.Execute(CompleteClose);
        return failure == null ? _closed.Task : Task.FromException(failure);
    }
    internal Task DeregisterTransportAsync()
    {
        if (!_registered)
            return Task.CompletedTask;
        _registered = false;
        if (EventLoop is SingleThreadEventExecutor worker)
            worker.RemoveShutdownHook(OnLoopShutdown);
        DoDeregister();
        Pipeline.FireChannelUnregistered();
        if (!_open)
            Pipeline.Destroy();
        return Task.CompletedTask;
    }
    internal Task WriteTransportAsync(object message, CancellationToken cancellationToken)
    {
        if (!_open)
        { ReferenceCountUtil.Release(message); return Task.FromException(new IOException("Channel is closed.")); }
        try
        {
            message = FilterOutboundMessage(message);
            int size = Math.Max(0, Pipeline.EstimatorHandle.Size(message));
            return OutboundBuffer.AddMessage(message, size, cancellationToken);
        }
        catch (Exception error) { ReferenceCountUtil.Release(message); return Task.FromException(error); }
    }
    internal void FlushTransport()
    {
        OutboundBuffer.AddFlush();
        if (_writing)
            return;
        if (!_active)
        {
            while (OutboundBuffer.Remove(new IOException(_open ? "Channel is not connected." : "Channel is closed.")))
            { }
            return;
        }
        _writing = true;
        _ = DrainWritesAsync();
    }
    private async Task DrainWritesAsync()
    {
        try
        {
            while (_open && OutboundBuffer.Current is { } message)
            {
                // Closing cancels queued ownership; an in-flight native operation must keep its own borrow alive.
                ReferenceCountUtil.Retain(message);
                bool borrowed = true;
                try
                {
                    await DoWriteAsync(message).ConfigureAwait(false);
                    ReferenceCountUtil.Release(message);
                    borrowed = false;
                    await OnLoopAsync(() => OutboundBuffer.Remove()).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    await OnLoopAsync(() =>
                    {
                        OutboundBuffer.Remove(error);
                        if (Config.IsAutoClose() && !OutboundBuffer.IsClosed)
                            _ = CloseTransportAsync();
                    }).ConfigureAwait(false);
                }
                finally { if (borrowed) ReferenceCountUtil.Release(message); }
                // The next iteration must inspect state on the event loop, including after an asynchronous write.
                if (!EventLoop.InEventLoop())
                {
                    await OnLoopAsync(() => { _writing = false; if (OutboundBuffer.Current != null) FlushTransport(); }).ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (Exception error) { OnUnhandledException(error); }
        finally { if (EventLoop.InEventLoop()) { _writing = false; DoWriteComplete(); } }
    }
    internal void BeginRead()
    {
        if (!_open || !_active)
            return;
        try
        { DoBeginRead(); }
        catch (Exception error) { Pipeline.FireExceptionCaught(error); _ = CloseTransportAsync(); }
    }
    /**
     * Invoked when a new message is added to a {@link ChannelOutboundBuffer} of this {@link AbstractChannel}, so that
     * the {@link Channel} implementation converts the message to another. (e.g. heap buffer -> direct buffer)
     */
    protected virtual object FilterOutboundMessage(object message) => message;
    /**
     * Is called after the {@link Channel} is registered with its {@link EventLoop} as part of the register process.
     * Subclasses may override this method
     *
     * @deprecated use {@link #doRegister(ChannelPromise)}
     */
    /**
     * Is called after the {@link Channel} is registered with its {@link EventLoop} as part of the register process.
     * Subclasses may override this method
     *
     * @param promise {@link ChannelPromise} that must be notified once done to continue the registration.
     */
    protected virtual void DoRegister() { }
    /**
     * Deregister the {@link Channel} from its {@link EventLoop}.
     *
     * Sub-classes may override this method
     */
    protected virtual void DoDeregister() { }
    protected abstract Task DoBindAsync(EndPoint address, CancellationToken cancellationToken);
    protected abstract Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken);
    protected virtual Task DoDisconnectAsync(CancellationToken cancellationToken) { DoClose(); _active = false; return Task.CompletedTask; }
    /**
     * Close the {@link Channel}
     */
    protected abstract void DoClose();
    /**
     * Schedule a read operation.
     */
    protected abstract void DoBeginRead();
    protected abstract Task DoWriteAsync(object message);
    protected virtual void DoWriteComplete() { }
    protected internal virtual void OnUnhandledInboundMessage(object message) => ReferenceCountUtil.Release(message);
    protected internal virtual void OnUnhandledUserEvent(object evt) => ReferenceCountUtil.Release(evt);
    protected internal virtual void OnUnhandledException(Exception error) => Logger.Warn("Unhandled channel exception.", error);
    public Task BindAsync(EndPoint address, CancellationToken cancellationToken = default) => Pipeline.BindAsync(address, cancellationToken);
    public Task ConnectAsync(EndPoint remote, EndPoint localAddress = null, CancellationToken cancellationToken = default) => Pipeline.ConnectAsync(remote, localAddress, cancellationToken);
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Pipeline.DisconnectAsync(cancellationToken);
    public virtual Task CloseAsync(CancellationToken cancellationToken = default) => Pipeline.CloseAsync(cancellationToken);
    public Task DeregisterAsync(CancellationToken cancellationToken = default) => Pipeline.DeregisterAsync(cancellationToken);
    public Task WriteAsync(object message, CancellationToken cancellationToken = default) => Pipeline.WriteAsync(message, cancellationToken);
    public Task WriteAndFlushAsync(object message, CancellationToken cancellationToken = default) => Pipeline.WriteAndFlushAsync(message, cancellationToken);
    public IChannelOutboundInvoker Read() { Pipeline.Read(); return this; }
    public IChannelOutboundInvoker Flush() { Pipeline.Flush(); return this; }
    public int CompareTo(IChannel other) => other == null ? 1 : Id.CompareTo(other.Id);
    /**
     * Returns the {@link String} representation of this channel.  The returned
     * string contains the {@linkplain #hashCode() ID}, {@linkplain #localAddress() local address},
     * and {@linkplain #remoteAddress() remote address} of this channel for
     * easier identification.
     */
    public override string ToString() => $"[id: {Id.AsShortText()}, L:{LocalAddress} - R:{RemoteAddress}]";
}
