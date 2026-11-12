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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Channels;

/// <summary>Channel lifecycle and queued writes. All transport state changes execute on the assigned event loop.</summary>
/**
 * A skeletal {@link Channel} implementation.
 */
public abstract class AbstractChannel : DefaultAttributeMap, IChannel
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(AbstractChannel));
    private readonly object _registrationGate = new();
    private Exception _writeCloseCause;
    private ChannelOutputShutdownException _outputShutdownCause;
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
    private bool _readBeforeActive;
    private int _connecting;
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
                    if (eventLoop is DefaultEventLoop registry) registry.ChannelRegistered(this);
                    else if (eventLoop is ManualIoEventLoop manual) manual.ChannelRegistered(this);
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
                        if (Config.IsAutoRead() || _readBeforeActive)
                            BeginRead();
                    }
                    ResumeRead();
                }).ConfigureAwait(false);
            }
            catch
            {
                // Registration rejection cannot rely on another task being admitted by the rejected executor.
                if (eventLoop is DefaultEventLoop registry) registry.ChannelUnregistered(this);
                else if (eventLoop is ManualIoEventLoop manual) manual.ChannelUnregistered(this);
                _registered = false;
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
    protected Task OnSocketLoopAsync(Action action) =>
        DispatchSocketAsync(() => { action(); return false; }, false);

    internal virtual bool UsesSocketCompletions => false;

    private Task CompleteOutboundIoAsync(Func<bool> action, bool pending) =>
        pending && UsesSocketCompletions ? DispatchSocketAsync(action, true) : OnLoopAsync(() => action());

    protected void StartSocketWork(Action action)
    {
        if (EventLoop is ManualIoEventLoop)
            _ = DispatchSocketAsync(() => { action(); return false; }, true, captureContext: false);
        else EventLoop.Execute(action);
    }

    protected Task QueueSocketWorkAsync(Action action)
    {
        var loop = EventLoop;
        if (loop is ManualIoEventLoop)
            return DispatchSocketAsync(() => { action(); return false; }, true);
        return loop.SubmitAsync(() =>
        {
            if (!ReferenceEquals(loop, _eventLoop))
                return QueueSocketWorkAsync(action);
            action();
            return Task.CompletedTask;
        });
    }

    private Task DispatchSocketAsync(Func<bool> action, bool queue, bool captureContext = true)
    {
        var loop = EventLoop;
        if (loop is not (ManualIoEventLoop or DefaultEventLoop) || (!queue && loop.InEventLoop()))
            return OnLoopAsync(() => action());

        try
        {
            (Task, bool) Dispatch() =>
                ReferenceEquals(loop, _eventLoop)
                    ? (Task.CompletedTask, action())
                    : (DispatchSocketAsync(action, queue, captureContext), false);
            return loop is ManualIoEventLoop manual
                ? manual.SubmitIoAsync(this, Dispatch, captureContext)
                : ((DefaultEventLoop)loop).SubmitIoAsync(Dispatch, captureContext);
        }
        catch (RejectedExecutionException) when (!ReferenceEquals(loop, _eventLoop))
        {
            return DispatchSocketAsync(action, queue, captureContext);
        }
    }

    protected void ResumeRead() => Interlocked.Exchange(ref _readResumed, null)?.TrySetResult();
    protected virtual void DoClearReadPending() { }
    /**
     * Set read pending to {@code false}.
     */
    internal void ClearReadPending()
    {
        void Clear() { _readBeforeActive = false; DoClearReadPending(); }
        if (!_registered)
        {
            // Best effort if we are not registered yet clear readPending. This happens during channel initialization.
            // NB: We only set the boolean field instead of calling clearReadPending0(), because the SelectionKey is
            // not set yet so it would produce an assertion failure.
            // CLR: there is no SelectionKey; each native backend clears its pending-read flag.
            Clear();
        }
        else
            _ = OnLoopAsync(Clear);
    }
    protected Task DispatchReadAsync(Func<bool> readPending, Action read) =>
        DispatchReadResultAsync(readPending, () => { read(); return true; });

    private protected async Task DispatchReadResultAsync(Func<bool> readPending, Func<bool> read)
    {
        // Unlike readiness-based NIO, a native receive can consume input before AutoRead is disabled.
        // Keep that single completed operation's borrow until registration and read admission resume.
        // Never discard bytes/datagrams or dispatch user callbacks from a stale event-loop owner.
        for (; ; )
        {
            bool finished = false;
            Task resumed = null;
            await DispatchSocketAsync(() =>
            {
                if (!_open)
                { finished = true; return false; }
                if (_registered && (Config.IsAutoRead() || readPending()))
                {
                    finished = true;
                    return read();
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                resumed = (Interlocked.CompareExchange(ref _readResumed, waiter, null) ?? waiter).Task;
                // A forced close may run outside the owner during registration rejection.
                // Recheck after publication so it cannot miss a newly installed waiter.
                if (!_open)
                    ResumeRead();
                return false;
            }, true).ConfigureAwait(false);
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
    { if (!_open) throw NewClosedChannelException(); }
    private IOException NewClosedChannelException() => new("Channel is closed.", _writeCloseCause);
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
            { Pipeline.FireChannelActive(); if (Config.IsAutoRead() || _readBeforeActive) BeginRead(); }
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
            bool connecting = false;
            try
            {
                EnsureOpen();
                if (UsesSocketCompletions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Already a connect in process.
                    if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
                        throw new SocketException((int)SocketError.AlreadyInProgress);
                    connecting = true;
                }

                Task operation = DoConnectAsync(remote, local, cancellationToken);
                bool pending = !operation.IsCompleted;
                try { await operation.ConfigureAwait(false); }
                catch (Exception error) when (pending && UsesSocketCompletions && EventLoop is ManualIoEventLoop or DefaultEventLoop)
                {
                    // Publish the original operation result even if a stopped owner rejects dispatch.
                    try { await CompleteOutboundIoAsync(() => { Fail(error); return true; }, true).ConfigureAwait(false); }
                    catch (RejectedExecutionException) { Fail(error); }
                    return;
                }

                await CompleteOutboundIoAsync(() =>
                {
                    EnsureOpen();
                    ReleaseConnect();
                    // Source connect completion precedes ChannelActive. Keep the native producer's asynchronous
                    // continuation policy; completion itself is committed on this owner.
                    bool canceled = UsesSocketCompletions && cancellationToken.IsCancellationRequested;
                    if (canceled)
                        completion.TrySetCanceled(cancellationToken);
                    else
                        completion.TrySetResult();
                    // Regardless if the connection attempt was cancelled, channelActive() event should be triggered,
                    // because what happened is what happened.
                    if (!wasActive && _active)
                    { Pipeline.FireChannelActive(); if (!canceled && (Config.IsAutoRead() || _readBeforeActive)) BeginRead(); }
                    // If a user cancelled the connection attempt, close the channel, which is followed by channelInactive().
                    if (canceled) _ = CloseTransportAsync();
                    return true;
                }, pending).ConfigureAwait(false);
            }
            catch (Exception error) { Fail(error); }

            void Fail(Exception error)
            {
                ReleaseConnect();
                bool published = error is OperationCanceledException canceled
                    ? completion.TrySetCanceled(canceled.CancellationToken) : completion.TrySetException(error);
                if (!published)
                    OnUnhandledException(error);
            }

            void ReleaseConnect()
            {
                if (!connecting) return;
                connecting = false;
                Volatile.Write(ref _connecting, 0);
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
        var closedCause = NewClosedChannelException();
        OutboundBuffer.Close(_outputShutdownCause ?? _writeCloseCause ?? closedCause, _outputShutdownCause ?? closedCause);
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
        if (EventLoop is DefaultEventLoop registry) registry.ChannelUnregistered(this);
        else if (EventLoop is ManualIoEventLoop manual) manual.ChannelUnregistered(this);
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
        if (!_open || OutboundBuffer.IsClosed)
        {
            // release message now to prevent resource-leak
            ReferenceCountUtil.Release(message);
            // If the outboundBuffer is null we know the channel was closed and so
            // need to fail the future right away. If it is not null the handling of the rest
            // will be done in flush0()
            // See https://github.com/netty/netty/issues/2362
            // CLR keeps a closed buffer object in place of the source's null buffer.
            return Task.FromException(NewClosedChannelException());
        }
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
        if (OutboundBuffer.IsClosed)
            return;
        OutboundBuffer.AddFlush();
        // Avoid re-entrance
        if (_writing || OutboundBuffer.IsEmpty)
            return;
        // Mark all pending write requests as failure if the channel is inactive.
        if (!_active)
        {
            _writing = true;
            try
            {
                // Check if we need to generate the exception at all.
                Exception cause = _open ? new SocketException((int)SocketError.NotConnected) : NewClosedChannelException();
                while (OutboundBuffer.Remove(cause)) { }
            }
            finally { _writing = false; }
            return;
        }
        _writing = true;
        _ = DrainWritesAsync();
    }
    private async Task DrainWritesAsync()
    {
        int gatheredWrites = 0;
        bool handedOff = false;
        try
        {
            // Snapshot the message-channel quantum for this write pass, as in AbstractNioMessageChannel.doWrite.
            // Byte streams, embedded and local transports retain their own write policies.
            int? messagesRemaining = WriteMessageLimit;
            while (_open && OutboundBuffer.Current is { } message)
            {
                bool pending = false;
                try
                {
                    if (TryGatherWriteAsync() is { } gather)
                    {
                        pending = !gather.IsCompleted;
                        int written = await gather.ConfigureAwait(false);
                        await CompleteOutboundIoAsync(() =>
                        {
                            if (!IsOpen || OutboundBuffer.IsClosed) return false;
                            OutboundBuffer.RemoveBytes(written);
                            return true;
                        }, pending).ConfigureAwait(false);
                        gatheredWrites++;
                    }
                    else
                    {
                        // Closing cancels queued ownership; an in-flight native operation must keep its own borrow alive.
                        ReferenceCountUtil.Retain(message);
                        try
                        {
                            Task operation = DoWriteAsync(message);
                            pending = !operation.IsCompleted;
                            await operation.ConfigureAwait(false);
                        }
                        finally { ReferenceCountUtil.Release(message); }
                        await CompleteOutboundIoAsync(() => OutboundBuffer.Remove(), pending).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    // The native operation has finished. Release its borrow before failing the queued Task,
                    // just as ChannelOutboundBuffer releases the message before notifying its promise.
                    await CompleteOutboundIoAsync(() =>
                    {
                        bool handled = !OutboundBuffer.IsClosed;
                        HandleWriteError(error);
                        return handled;
                    }, pending).ConfigureAwait(false);
                }
                // A recoverable message write failure consumes the same quantum as a successful message.
                messagesRemaining--;
                // The next iteration must inspect state on the event loop, including after an asynchronous write.
                bool inEventLoop = EventLoop.InEventLoop();
                bool messageLimitReached = messagesRemaining == 0;
                bool yield = inEventLoop && (messageLimitReached || gatheredWrites != 0 &&
                    gatheredWrites >= Config.GetWriteSpinCount()) && OutboundBuffer.Current != null;
                if (!inEventLoop || yield)
                {
                    void ResumeWrites()
                    {
                        // Resume the existing flush without admitting writes queued after it.
                        // Keep single-writer ownership across the handoff, including a pending native operation.
                        _ = DrainWritesAsync();
                    }
                    handedOff = true;
                    try
                    {
                        // Did not write all messages.
                        // CLR: re-arm message writes in the next I/O phase instead of creating selector operations.
                        Task resume = yield && messageLimitReached ? QueueMessageWriteAsync(ResumeWrites)
                            : yield ? QueueSocketWorkAsync(ResumeWrites)
                            : UsesSocketCompletions ? OnSocketLoopAsync(ResumeWrites) : OnLoopAsync(ResumeWrites);
                        await resume.ConfigureAwait(false);
                    }
                    catch { handedOff = false; throw; }
                    return;
                }
            }
            // Wrote all messages.
        }
        catch (Exception error) { OnUnhandledException(error); }
        finally { if (!handedOff && EventLoop.InEventLoop()) { _writing = false; DoWriteComplete(); } }
    }
    private Task QueueMessageWriteAsync(Action resume)
    {
        if (EventLoop is not (DefaultEventLoop or ManualIoEventLoop))
            return QueueSocketWorkAsync(resume);
        return DispatchSocketAsync(() =>
        {
            bool ready = IsOpen && OutboundBuffer.Current != null;
            resume();
            return ready;
        }, true);
    }
    protected virtual int? WriteMessageLimit => null;
    internal virtual Task<int> TryGatherWriteAsync() => null;
    internal void BeginRead()
    {
        if (!_open)
            return;
        if (!_active)
        {
            // CLR completion-based sockets cannot receive before connect. Preserve the source readPending
            // permission until activation rather than issuing a native receive on an unconnected socket.
            _readBeforeActive = true;
            return;
        }
        _readBeforeActive = false;
        try
        { DoBeginRead(); }
        catch (Exception error) { Pipeline.FireExceptionCaught(error); _ = CloseTransportAsync(); }
    }
    /**
     * Invoked when a new message is added to a {@link ChannelOutboundBuffer} of this {@link AbstractChannel}, so that
     * the {@link Channel} implementation converts the message to another. (e.g. heap buffer -> direct buffer)
     */
    protected virtual object FilterOutboundMessage(object message) => message;
    protected virtual bool ContinueOnWriteError => false;
    protected virtual void HandleWriteError(Exception error)
    {
        if (OutboundBuffer.IsClosed) return;
        if (ContinueOnWriteError)
        {
            OutboundBuffer.Remove(error);
            return;
        }
        // Java SocketException is an IOException; native CLR socket failures use a separate hierarchy.
        if (Config.IsAutoClose() && !ContinueOnWriteError && error is IOException or SocketException)
        {
            CloseOnWriteError(error);
            return;
        }
        try { ShutdownOutput(error); }
        catch (Exception shutdownFailure) { Pipeline.FireExceptionCaught(shutdownFailure); }
    }
    /**
     * Shutdown the output portion of the corresponding {@link Channel}.
     * For example this will clean up the {@link ChannelOutboundBuffer} and not allow any more writes.
     * @param cause The cause which may provide rational for the shutdown.
     */
    protected void ShutdownOutput(Exception cause)
    {
        EnsureOpen();
        if (OutboundBuffer.IsClosed) throw NewClosedChannelException();
        // Disallow adding any messages and flushes to outboundBuffer.
        OutboundBuffer.BeginOutputShutdown();
        _outputShutdownCause = cause == null ? new ChannelOutputShutdownException("Channel output shutdown")
            : new ChannelOutputShutdownException("Channel output shutdown", cause);
        // When a side enables SO_LINGER and calls showdownOutput(...) to start TCP half-closure
        // we can not call doDeregister here because we should ensure this side in fin_wait2 state
        // can still receive and process the data which is send by another side in the close_wait state。
        // See https://github.com/netty/netty/issues/11981
        try
        {
            // The shutdown function does not block regardless of the SO_LINGER setting on the socket
            // so we don't need to use GlobalEventExecutor to execute the shutdown
            DoShutdownOutput();
        }
        finally
        {
            OutboundBuffer.Close(_outputShutdownCause);
            Pipeline.FireUserEventTriggered(ChannelOutputShutdownEvent.INSTANCE);
        }
    }
    /**
     * Called when conditions justify shutting down the output portion of the channel. This may happen if a write
     * operation throws an exception.
     */
    protected virtual void DoShutdownOutput()
    {
        // The source default closes the transport. CLR channel state and deferred lifecycle callbacks
        // are owned here, so use the ordinary close path with the output-shutdown queue cause.
        Task close = CloseTransportAsync();
        if (close.IsFaulted) close.GetAwaiter().GetResult();
    }
    protected void CloseOnWriteError(Exception error)
    {
        /**
         * Just call {@link #close(ChannelPromise, Throwable, boolean)} here which will take care of
         * failing all flushed messages and also ensure the actual close of the underlying transport
         * will happen before the promises are notified.
         *
         * This is needed as otherwise {@link #isActive()} , {@link #isOpen()} and {@link #isWritable()}
         * may still return {@code true} even if the channel should be closed as result of the exception.
         */
        _writeCloseCause = error;
        _ = CloseTransportAsync();
    }
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
