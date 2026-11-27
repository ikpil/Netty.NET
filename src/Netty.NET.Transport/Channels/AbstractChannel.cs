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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
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
    private Exception _initialCloseCause;
    private ChannelOutputShutdownException _outputShutdownCause;
    private readonly TaskCompletionSource _closeFuture = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lazy<IChannelConfig> _config;
    private readonly DefaultChannelPipeline _pipeline;
    private volatile IEventLoop _eventLoop;
    private bool _closeInitiated;
    private bool _closePending;
    private bool _registering;
    private int _registrationEpoch;
    private volatile bool _open = true;
    private volatile bool _active;
    private volatile bool _registered;
    /** true if the channel has never been registered, false otherwise */
    private bool neverRegistered = true;
    private bool _writing;
    private bool _readBeforeActive;
    private IRecvByteBufAllocator.IHandle _receiveHandle;
    private volatile EndPoint localAddress;
    private volatile EndPoint remoteAddress;

    /** Cache for the string representation of this channel */
    private bool _strValActive;
    private string _strVal;
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
        Id = id ?? NewId();
        _config = new Lazy<IChannelConfig>(NewConfig);
        _pipeline = NewChannelPipeline();
        OutboundBuffer = new ChannelOutboundBuffer(this);
    }
    public IChannelId Id { get; }

    /**
     * Returns a new {@link DefaultChannelId} instance. Subclasses may override this method to assign custom
     * {@link ChannelId}s to {@link Channel}s that use the {@link AbstractChannel#AbstractChannel(Channel)} constructor.
     */
    protected virtual IChannelId NewId()
    {
        return DefaultChannelId.NewInstance();
    }
    public IChannel Parent { get; }
    public IEventLoop EventLoop => _eventLoop ?? throw new InvalidOperationException("Channel is not registered to an event loop.");
    protected IEventLoop AssignedEventLoop => _eventLoop;
    public IChannelConfig Config => _config.Value;
    public ChannelMetadata Metadata { get; }
    public IChannelPipeline Pipeline => _pipeline;
    public IByteBufAllocator Alloc()
    {
        return Config.GetAllocator();
    }
    // CLR: isOpen()/isActive() are abstract in Java; the transport-owned flags are the defaults that transports
    // override. The lifecycle checks of register, bind, connect, disconnect, close and flush0 read IsActive where the
    // source calls isActive() (AbstractChannel.AbstractUnsafe, AbstractNioChannel.connect), so an override, such as
    // the EmbeddedChannel subclass of the handler-proxy tests that reports an inactive channel, decides them as in
    // Java; the BeginRead check reads the flag, because it holds a read for the CLR socket (4.2's beginRead() has no
    // isActive() check).
    public virtual bool IsOpen => _open;
    public bool IsRegistered => _registered;
    public virtual bool IsActive => _active;
    public virtual EndPoint LocalAddress
    {
        get
        {
            EndPoint localAddress = this.localAddress;
            if (localAddress == null)
            {
                try
                {
                    this.localAddress = localAddress = LocalAddress0();
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Sometimes fails on a closed socket in Windows.
                    return null;
                }
            }
            return localAddress;
        }
    }

    public virtual EndPoint RemoteAddress
    {
        get
        {
            EndPoint remoteAddress = this.remoteAddress;
            if (remoteAddress == null)
            {
                try
                {
                    this.remoteAddress = remoteAddress = RemoteAddress0();
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Sometimes fails on a closed socket in Windows.
                    return null;
                }
            }
            return remoteAddress;
        }
    }

    /**
     * Returns the {@link SocketAddress} which is bound locally.
     */
    protected abstract EndPoint LocalAddress0();

    /**
     * Return the {@link SocketAddress} which the {@link Channel} is connected to.
     */
    protected abstract EndPoint RemoteAddress0();
    public Task CloseFuture => _closeFuture.Task;
    /**
     * Returns the {@link ChannelOutboundBuffer} of the {@link Channel} where the pending write requests are stored.
     */
    // CLR: Channel.Unsafe is folded into AbstractChannel (2026-11-16 owner decision); this member is Unsafe.outboundBuffer().
    public ChannelOutboundBuffer OutboundBuffer { get; }
    // CLR: IsWritable, BytesBeforeUnwritable and BytesBeforeWritable are virtual (owner decision 2026-11-25) because a
    // channel whose writes bypass the ChannelOutboundBuffer computes them itself, as Java's AbstractHttp2StreamChannel
    // does (codec-http2, src/Netty.NET.Handler.Codec.Http2/AbstractHttp2StreamChannel.cs).
    public virtual bool IsWritable => _open && !OutboundBuffer.IsClosed && OutboundBuffer.IsWritable;
    // isWritable() is currently assuming if there is no outboundBuffer then the channel is not writable.
    // We should be consistent with that here.
    // CLR: a closed ChannelOutboundBuffer stands for the null buffer of a closed channel.
    public virtual long BytesBeforeUnwritable => _open && !OutboundBuffer.IsClosed ? OutboundBuffer.BytesBeforeUnwritable : 0;
    public virtual long BytesBeforeWritable => _open && !OutboundBuffer.IsClosed ? OutboundBuffer.BytesBeforeWritable : long.MaxValue;
    protected virtual IChannelConfig NewConfig()
    {
        return new DefaultChannelConfig(this);
    }
    /**
     * Returns a new {@link DefaultChannelPipeline} instance.
     */
    protected virtual DefaultChannelPipeline NewChannelPipeline()
    {
        return new(this);
    }
    /**
     * Return the assigned {@link RecvByteBufAllocator.Handle} which will be used to allocate {@link ByteBuf}'s when
     * receiving data.
     */
    // CLR: Unsafe.recvBufAllocHandle() (Channel.Unsafe is folded into AbstractChannel, 2026-11-16 owner decision).
    protected IRecvByteBufAllocator.IHandle RecvBufAllocHandle()
    {
        return _receiveHandle ??= Config.GetRecvByteBufAllocator().NewHandle();
    }
    protected void SetActive(bool active)
    {
        _active = active;
    }
    /**
     * Return {@code true} if the given {@link EventLoop} is compatible with this instance.
     */
    protected virtual bool IsCompatible(IEventLoop eventLoop)
    {
        return true;
    }
    public virtual Task RegisterAsync(IEventLoop eventLoop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventLoop);
        int epoch;
        lock (_registrationGate)
        {
            if (_registered || _registering)
                return Task.FromException(new InvalidOperationException("registered to an event loop already"));
            if (!IsCompatible(eventLoop))
                return Task.FromException(
                        new InvalidOperationException("incompatible event loop type: " + eventLoop.GetType().FullName));
            _eventLoop = eventLoop;
            // Clear any cached executors from prior event loop registrations.
            AbstractChannelHandlerContext context = _pipeline.Tail;
            do
            {
                context.contextExecutor = null;
                context = context.Prev;
            } while (context != null);
            _registering = true;
            epoch = ++_registrationEpoch;
        }
        return RegisterCoreAsync();
        async Task RegisterCoreAsync()
        {
            // CLR: tells the rejected hand-off (the source's catch around eventLoop.execute) from a failure of the
            // admitted registration task (register0), because both fault the same Task.
            bool admitted = false;
            try
            {
                await OnLoopAsync(() =>
                {
                    admitted = true;
                    cancellationToken.ThrowIfCancellationRequested();
                    // check if the channel is still open as it could be closed in the mean time when the register
                    // call was outside of the eventLoop
                    EnsureOpen();
                    DoRegister();
                    bool firstRegistration = neverRegistered;
                    neverRegistered = false;
                    _registered = true;
                    // Registration is committed before user lifecycle callbacks; those callbacks may
                    // deregister and register this channel again, as in AbstractChannel.register0.
                    lock (_registrationGate)
                        _registering = false;
                    // Ensure we call handlerAdded(...) before we actually notify the promise. This is needed as the
                    // user may already fire events through the pipeline in the ChannelFutureListener.
                    _pipeline.InvokeHandlerAddedIfNeeded();
                    Pipeline.FireChannelRegistered();
                    // Only fire a channelActive if the channel has never been registered. This prevents firing
                    // multiple channel actives if the channel is deregistered and re-registered.
                    if (IsActive)
                    {
                        if (firstRegistration)
                        {
                            Pipeline.FireChannelActive();
                        }
                        else if (Config.IsAutoRead())
                        {
                            // This channel was registered before and autoRead() is set. This means we need to
                            // begin read again so that we process inbound data.
                            //
                            // See https://github.com/netty/netty/issues/4805
                            BeginRead();
                        }
                        // CLR: BeginRead holds a read() issued before activation until the channel is active.
                        if (_readBeforeActive)
                            BeginRead();
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (!admitted && e is RejectedExecutionException)
                {
                    Logger.Warn(
                            "Force-closing a channel whose registration task was not accepted by an event loop: {}",
                            this, e);
                }
                // Registration rejection cannot rely on another task being admitted by the rejected executor.
                // Close the channel directly to avoid FD leak.
                _registered = false;
                _open = _active = false;
                try
                { DoClose(); }
                finally
                {
                    var cause = new IOException("Channel registration failed.");
                    OutboundBuffer.FailFlushed(cause, false);
                    OutboundBuffer.Close(cause, false);
                    _closeFuture.TrySetResult();
                }
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
    protected internal void ExecuteOnLoop(Action action)
    { if (EventLoop.InEventLoop()) action(); else EventLoop.Execute(action); }
    /**
     * Closes the {@link Channel} immediately without firing any events.  Probably only useful
     * when registration attempt failed.
     */
    // CLR: Unsafe.closeForcibly() (Channel.Unsafe is folded into AbstractChannel, 2026-11-16 owner decision). The folded
    // Unsafe operations (closeForcibly, bind, connect, disconnect, close, deregister, write, flush, beginRead) are
    // protected internal virtual (owner decision 2026-11-25): a Java subclass in another package overrides them by
    // returning its own AbstractUnsafe from the protected newUnsafe() (codec-http2's Http2ChannelUnsafe); "You can
    // access a protected internal member from the current assembly or from types that are derived from the
    // containing class", and "If you define the derived class in a different assembly from the base class,
    // overridden members have protected access" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal).
    protected internal virtual void CloseForcibly()
    {
        // An offloaded native close already owns disposal; keep its queue and completion ownership intact.
        if (_closePending) return;
        _open = _active = false;
        try
        { DoClose(); }
        finally
        {
            ClosedChannelException cause = StacklessClosedChannelException.NewInstance(typeof(AbstractChannel), "closeForcibly()");
            OutboundBuffer.FailFlushed(cause, false);
            OutboundBuffer.Close(cause);
            _closeFuture.TrySetResult();
        }
    }
    protected void EnsureOpen()
    { if (!_open) throw NewClosedChannelException(_initialCloseCause, "ensureOpen(ChannelPromise)"); }
    private static ClosedChannelException NewClosedChannelException(Exception cause, string method)
    {
        // CLR: AbstractUnsafe is folded into AbstractChannel, so the origin class is AbstractChannel.
        ClosedChannelException exception = cause == null
            ? StacklessClosedChannelException.NewInstance(typeof(AbstractChannel), method)
            : StacklessClosedChannelException.NewInstance(typeof(AbstractChannel), method, cause);
        return exception;
    }
    // CLR: bind/disconnect: run the operation, then success on the owner; a failure calls closeIfClosed() as the
    // source catch blocks do (safeSetFailure(promise, t); closeIfClosed();).
    private Task Operation(Func<Task> operation, Action success)
    {
        Task task;
        try
        { EnsureOpen(); task = operation(); }
        catch (Exception error) { CloseIfClosed(); return Task.FromException(error); }
        if (task.IsCompleted)
        {
            try
            { task.GetAwaiter().GetResult(); success(); return Task.CompletedTask; }
            catch (Exception error) { CloseIfClosed(); return Task.FromException(error); }
        }
        return FinishAsync();
        async Task FinishAsync()
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                await OnLoopAsync(CloseIfClosed).ConfigureAwait(false);
                throw;
            }
            await OnLoopAsync(() => { EnsureOpen(); success(); }).ConfigureAwait(false);
        }
    }
    /**
     * Bind the {@link SocketAddress} to the {@link Channel} of the {@link ChannelPromise} and notify
     * it once its done.
     */
    // CLR: Unsafe.bind(localAddress, promise) (Channel.Unsafe is folded into AbstractChannel, 2026-11-16 owner decision).
    protected internal virtual Task BindTransportAsync(EndPoint address, CancellationToken cancellationToken)
    {
        // See: https://github.com/netty/netty/issues/576
        if (Config.TryGetOption(ChannelOption.SO_BROADCAST, out bool broadcast) && broadcast &&
            address is IPEndPoint inetAddress &&
            !IsAnyLocalAddress(inetAddress.Address) &&
            !PlatformDependent.IsWindows() && !MaybeSuperUser())
        {
            // Warn a user about the fact that a non-root user can't receive a
            // broadcast packet on *nix if the socket is bound on non-wildcard address.
            Logger.Warn(
                    "A non-root user can't receive a broadcast packet if the socket " +
                    "is not bound to a wildcard address; binding to a non-wildcard " +
                    "address (" + address + ") anyway as requested.");
        }

        bool wasActive = IsActive;
        return Operation(() => DoBindAsync(address, cancellationToken), () =>
        {
            if (!wasActive && IsActive)
            {
                InvokeLater(() =>
                {
                    Pipeline.FireChannelActive();
                    // CLR: BeginRead holds a read() issued before activation until the channel is active.
                    if (_readBeforeActive) BeginRead();
                });
            }
        });
    }
    /**
     * Connect the {@link Channel} of the given {@link ChannelFuture} with the given remote {@link SocketAddress}.
     * If a specific local {@link SocketAddress} should be used it need to be given as argument. Otherwise just
     * pass {@code null} to it.
     *
     * The {@link ChannelPromise} will get notified once the connect operation was complete.
     */
    // CLR: Unsafe.connect(remoteAddress, localAddress, promise) (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual Task ConnectTransportAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        bool wasActive = IsActive;
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
                    completion.TrySetResult();
                    if (!wasActive && IsActive)
                    { Pipeline.FireChannelActive(); if (_readBeforeActive) BeginRead(); }
                }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                bool published = error is OperationCanceledException canceled
                    ? completion.TrySetCanceled(canceled.CancellationToken) : completion.TrySetException(error);
                if (!published)
                    OnUnhandledException(error);
            }
        }
    }
    /**
     * Disconnect the {@link Channel} of the {@link ChannelFuture} and notify the {@link ChannelPromise} once the
     * operation was complete.
     */
    // CLR: Unsafe.disconnect(promise) (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual Task DisconnectTransportAsync(CancellationToken cancellationToken)
    {
        if (!Metadata.HasDisconnect())
            return Close();
        bool wasActive = IsActive;
        return Operation(() => DoDisconnectAsync(cancellationToken), () =>
        {
            // Reset remoteAddress and localAddress
            remoteAddress = null;
            localAddress = null;
            if (wasActive && !IsActive)
            {
                InvokeLater(() => Pipeline.FireChannelInactive());
            }
            CloseIfClosed(); // doDisconnect() might have closed the channel
        });
    }
    /**
     * Close the {@link Channel} of the {@link ChannelPromise} and notify the {@link ChannelPromise} once the
     * operation was complete.
     */
    // CLR: Unsafe.close(promise) (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual Task Close()
    {
        if (_closeInitiated || !_open)
        {
            if (_closeFuture.Task.IsCompleted)
            {
                // Closed already.
                return Task.CompletedTask;
            }
            // This means close() was called before so we just register a listener and return
            // CLR: repeat callers join the existing Task rather than completing before native close.
            return _closeFuture.Task;
        }
        _closeInitiated = true;
        bool wasActive = IsActive;
        ChannelOutboundBuffer outboundBuffer = OutboundBuffer;
        // Disallow adding any messages and flushes to outboundBuffer.
        outboundBuffer.BeginOutputShutdown();
        // CLR socket completions replace selection keys. Quiesce logical read/write admission before offloading disposal.
        _open = _active = false;
        TaskScheduler closeExecutor = PrepareToClose();
        var promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (closeExecutor != null)
        {
            _closePending = true;
            // Reserve the owner callback before starting native close, including during immediate shutdown.
            // This is CLR completion admission, not a Java Executor/Promise facade.
            Action<Action> invokeLater = EventLoop switch
            {
                SingleThreadEventLoop loop => loop.ReserveCloseCompletion(),
                ManualIoEventLoop loop => loop.ReserveCloseCompletion(),
                _ => EventLoop.Execute
            };
            try
            {
                _ = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        // Execute the close.
                        DoClose0(promise);
                    }
                    finally
                    {
                        // Call invokeLater so closeAndDeregister is executed in the EventLoop again!
                        invokeLater(CloseAndDeregister);
                    }
                }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, closeExecutor);
            }
            catch (Exception t)
            {
                // CLR: a rejected TaskScheduler must not leave the native resource and the reserved owner callback
                // stranded. Close here and report the rejection unless the close itself failed.
                Exception failure = t;
                try { DoClose(); }
                catch (Exception e) { failure = e; }
                _closeFuture.TrySetResult();
                promise.TrySetException(failure);
                invokeLater(CloseAndDeregister);
            }
        }
        else
        {
            try
            {
                // Close the channel and fail the queued messages in all cases.
                DoClose0(promise);
            }
            finally
            {
                // Fail all the queued messages.
                FailQueuedMessages();
            }
            // CLR: _writing spans the asynchronous write drain that replaces the source flush0()/inFlush0 window.
            if (_writing)
            {
                InvokeLater(() => FireChannelInactiveAndDeregister(wasActive));
            }
            else
            {
                FireChannelInactiveAndDeregister(wasActive);
            }
        }
        return promise.Task;

        void CloseAndDeregister()
        {
            _closePending = false;
            // Fail all the queued messages
            FailQueuedMessages();
            FireChannelInactiveAndDeregister(wasActive);
        }

        void FailQueuedMessages()
        {
            // CLR: one close path serves close(ChannelPromise) and the flush0() write-failure close; the latter
            // attaches its cause as newClosedChannelException(t, "flush0()") does.
            ClosedChannelException closeCause = _initialCloseCause == null
                ? NewClosedChannelException(null, "close(ChannelPromise)")
                : NewClosedChannelException(_initialCloseCause, "flush0()");
            outboundBuffer.FailFlushed(_outputShutdownCause ?? _initialCloseCause ?? closeCause, false);
            outboundBuffer.Close(_outputShutdownCause ?? (IOException)closeCause, false);
        }
    }
    private void DoClose0(TaskCompletionSource promise)
    {
        try
        {
            DoClose();
            _closeFuture.TrySetResult();
            promise.TrySetResult();
        }
        catch (Exception t)
        {
            _closeFuture.TrySetResult();
            promise.TrySetException(t);
        }
    }
    private void FireChannelInactiveAndDeregister(bool wasActive)
    {
        if (!_registered && !_open && !_closePending)
        {
            // CLR: an explicit deregistration may precede completion of the native close. The source then never
            // destroys the pipeline; the closed channel still releases its handlers here.
            _pipeline.Destroy();
            return;
        }
        _ = Deregister(wasActive && !IsActive);
    }
    /**
     * Prepares to close the {@link Channel}. If this method returns an {@link Executor}, the
     * caller must call the {@link Executor#execute(Runnable)} method with a task that calls
     * {@link #doClose()} on the returned {@link Executor}. If this method returns {@code null},
     * {@link #doClose()} must be called from the caller thread. (i.e. {@link EventLoop})
     */
    // CLR: TaskScheduler selects the native close worker; no Java executor abstraction is required.
    protected virtual TaskScheduler PrepareToClose()
    {
        return null;
    }
    // CLR: true while an offloaded native close runs; HeadContext.channelUnregistered then leaves the pipeline to
    // the close completion, as the source's isOpen() still reports the open socket.
    internal bool ClosePending => _closePending;
    // CLR: Unsafe.deregister(ChannelPromise) returns the Task that replaces its promise.
    /**
     * Deregister the {@link Channel} of the {@link ChannelPromise} from {@link EventLoop} and notify the
     * {@link ChannelPromise} once the operation was complete.
     */
    // CLR: Unsafe.deregister(promise) (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual Task DeregisterTransportAsync()
    {
        return Deregister(false);
    }

    private Task Deregister(bool fireChannelInactive)
    {
        if (!_registered)
        {
            return Task.CompletedTask;
        }

        var promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // As a user may call deregister() from within any method while doing processing in the ChannelPipeline,
        // we need to ensure we do the actual deregister operation later. This is needed as for example,
        // we may be in the ByteToMessageDecoder.callDecode(...) method and so still try to do processing in
        // the old EventLoop while the user already registered the Channel to a new EventLoop. Without delay,
        // the deregister operation this could lead to have a handler invoked by different EventLoop and so
        // threads.
        //
        // See:
        // https://github.com/netty/netty/issues/4435
        InvokeLater(() =>
        {
            try
            {
                if (_registered)
                {
                    // CLR: event-loop channel bookkeeping and the shutdown hook belong to the doDeregister step.
                }
                DoDeregister();
            }
            catch (Exception t)
            {
                Logger.Warn("Unexpected exception occurred while deregistering a channel.", t);
            }
            finally
            {
                if (fireChannelInactive)
                {
                    Pipeline.FireChannelInactive();
                }
                // Some transports like local and AIO does not allow the deregistration of
                // an open channel.  Their doDeregister() calls close(). Consequently,
                // close() calls deregister() again - no need to fire channelUnregistered, so check
                // if it was registered.
                if (_registered)
                {
                    _registered = false;
                    Pipeline.FireChannelUnregistered();
                }
                promise.TrySetResult();
            }
        });
        return promise.Task;
    }

    private void InvokeLater(Action task)
    {
        try
        {
            // This method is used by outbound operation implementations to trigger an inbound event later.
            // They do not trigger an inbound event immediately because an outbound operation might have been
            // triggered by another inbound event handler method.  If fired immediately, the call stack
            // will look like this for example:
            //
            //   handlerA.inboundBufferUpdated() - (1) an inbound handler method closes a connection.
            //   -> handlerA.ctx.close()
            //      -> channel.unsafe.close()
            //         -> handlerA.channelInactive() - (2) another inbound handler method called while in (1) yet
            //
            // which means the execution of two inbound handler methods of the same handler overlap undesirably.
            IEventLoop loop = EventLoop;
            if (loop.IsShutdown() && loop.InEventLoop())
            {
                // CLR: StopAsync closes ordinary task admission before shutdown hooks close channels. The owner
                // still drains reserved close callbacks before termination, which keeps the later invocation.
                Action<Action> reserved = loop switch
                {
                    SingleThreadEventLoop singleThread => singleThread.ReserveCloseCompletion(),
                    ManualIoEventLoop manual => manual.ReserveCloseCompletion(),
                    _ => null
                };
                if (reserved != null)
                {
                    reserved(task);
                    return;
                }
            }
            loop.Execute(task);
        }
        catch (RejectedExecutionException e)
        {
            Logger.Warn("Can't invoke task later as EventLoop rejected it", e);
        }
    }
    /**
     * Schedules a write operation.
     */
    // CLR: Unsafe.write(msg, promise) (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual Task WriteTransportAsync(object msg, CancellationToken cancellationToken)
    {
        if (!_open || OutboundBuffer.IsClosed)
        {
            // release message now to prevent resource-leak
            ReferenceCountUtil.Release(msg);
            // If the outboundBuffer is null we know the channel was closed and so
            // need to fail the future right away. If it is not null the handling of the rest
            // will be done in flush0()
            // See https://github.com/netty/netty/issues/2362
            // CLR keeps a closed buffer object in place of the source's null buffer.
            return Task.FromException(NewClosedChannelException(_initialCloseCause, "write(Object, ChannelPromise)"));
        }
        try
        {
            msg = FilterOutboundMessage(msg);
            int size = Math.Max(0, _pipeline.EstimatorHandle().Size(msg));
            return OutboundBuffer.AddMessage(msg, size, cancellationToken);
        }
        catch (Exception error) { ReferenceCountUtil.Release(msg); return Task.FromException(error); }
    }
    /**
     * Flush out all write operations scheduled via {@link #write(Object, ChannelPromise)}.
     */
    // CLR: Unsafe.flush() (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual void FlushTransport()
    {
        if (OutboundBuffer.IsClosed)
            return;
        OutboundBuffer.AddFlush();
        Flush0();
    }
    // CLR: AbstractUnsafe.flush0(); _writing is the inFlush0 flag.
    protected virtual void Flush0()
    {
        // Avoid re-entrance
        if (_writing || OutboundBuffer.IsEmpty)
            return;
        _writing = true;
        // Mark all pending write requests as failure if the channel is inactive.
        if (!IsActive)
        {
            try
            {
                // Check if we need to generate the exception at all.
                if (!OutboundBuffer.IsEmpty)
                {
                    if (_open)
                    {
                        // CLR: SocketError.NotConnected stands for NotYetConnectedException.
                        OutboundBuffer.FailFlushed(new SocketException((int)SocketError.NotConnected), true);
                    }
                    else
                    {
                        // Do not trigger channelWritabilityChanged because the channel is closed already.
                        OutboundBuffer.FailFlushed(NewClosedChannelException(_initialCloseCause, "flush0()"), false);
                    }
                }
            }
            finally { _writing = false; }
            return;
        }
        if (!HasDoWriteLoop)
        {
            // CLR: _writing spans the asynchronous per-message drain until it completes.
            _ = DrainWritesAsync();
            return;
        }
        try
        {
            DoWrite(OutboundBuffer);
        }
        catch (Exception t)
        {
            HandleWriteError(t);
        }
        finally
        {
            _writing = false;
        }
    }
    // CLR: true for transports that implement Java's doWrite(ChannelOutboundBuffer) loop (Nio and Local); the
    // Embedded transport and Task-returning test channels drain per-message Task writes instead.
    internal virtual bool HasDoWriteLoop => false;
    /**
     * Flush the content of the given buffer to the remote peer.
     */
    protected virtual void DoWrite(ChannelOutboundBuffer @in)
    {
        throw new NotSupportedException();
    }
    private async Task DrainWritesAsync()
    {
        bool handedOff = false;
        try
        {
            while (_open && OutboundBuffer.Current is { } message)
            {
                bool pending = false;
                try
                {
                    // Closing cancels queued ownership; an in-flight native operation must keep its own borrow alive.
                    ReferenceCountUtil.Retain(message);
                    try
                    {
                        Task operation = DoWrite(message);
                        pending = !operation.IsCompleted;
                        await operation.ConfigureAwait(false);
                    }
                    finally { ReferenceCountUtil.Release(message); }
                    await OnLoopAsync(() => OutboundBuffer.Remove()).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    // The native operation has finished. Release its borrow before failing the queued Task,
                    // just as ChannelOutboundBuffer releases the message before notifying its promise.
                    await OnLoopAsync(() => HandleWriteError(error)).ConfigureAwait(false);
                }
                // The next iteration must inspect state on the event loop, including after an asynchronous write.
                if (!EventLoop.InEventLoop())
                {
                    handedOff = true;
                    try
                    {
                        // Resume the existing flush without admitting writes queued after it.
                        await OnLoopAsync(() => _ = DrainWritesAsync()).ConfigureAwait(false);
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
    // CLR: BeginRead holds a read() issued before activation until the channel is active.
    private protected void BeginReadIfRequestedBeforeActive()
    {
        if (_readBeforeActive) BeginRead();
    }
    /**
     * Schedules a read operation that fills the inbound buffer of the first {@link ChannelInboundHandler} in the
     * {@link ChannelPipeline}.  If there's already a pending read operation, this method does nothing.
     */
    // CLR: Unsafe.beginRead() (Channel.Unsafe is folded into AbstractChannel).
    protected internal virtual void BeginRead()
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
        {
            DoBeginRead();
        }
        catch (Exception e)
        {
            InvokeLater(() => Pipeline.FireExceptionCaught(e));
            _ = Close();
        }
    }
    /**
     * Invoked when a new message is added to a {@link ChannelOutboundBuffer} of this {@link AbstractChannel}, so that
     * the {@link Channel} implementation converts the message to another. (e.g. heap buffer -> direct buffer)
     */
    protected virtual object FilterOutboundMessage(object msg)
    {
        return msg;
    }
    protected virtual bool ContinueOnWriteError => false;
    protected virtual void HandleWriteError(Exception t)
    {
        if (OutboundBuffer.IsClosed) return;
        if (ContinueOnWriteError)
        {
            OutboundBuffer.Remove(t);
            return;
        }
        // Java SocketException is an IOException; native CLR socket failures use a separate hierarchy.
        if (Config.IsAutoClose() && !ContinueOnWriteError && t is IOException or SocketException)
        {
            CloseOnWriteError(t);
            return;
        }
        try { ShutdownOutput(t); }
        catch (Exception shutdownFailure) { Pipeline.FireExceptionCaught(shutdownFailure); }
    }
    /**
     * Shutdown the output portion of the corresponding {@link Channel}.
     * For example this will clean up the {@link ChannelOutboundBuffer} and not allow any more writes.
     * @param cause The cause which may provide rational for the shutdown.
     */
    /**
     * Shutdown the output portion of the corresponding {@link Channel}.
     * For example this will clean up the {@link ChannelOutboundBuffer} and not allow any more writes.
     */
    // CLR: AbstractUnsafe.shutdownOutput(promise) and shutdownOutput(promise, cause) folded into the channel.
    protected void ShutdownOutput(Exception cause)
    {
        EnsureOpen();
        if (OutboundBuffer.IsClosed) throw new ClosedChannelException();
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
            // CLR: closeOutboundBufferForShutdown(pipeline, outboundBuffer, shutdownCause).
            OutboundBuffer.FailFlushed(_outputShutdownCause, false);
            OutboundBuffer.Close(_outputShutdownCause, true);
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
        Task close = Close();
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
        _initialCloseCause = error;
        _ = Close();
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
    /**
     * Bind the {@link Channel} to the {@link SocketAddress}
     */
    protected abstract Task DoBindAsync(EndPoint address, CancellationToken cancellationToken);
    protected abstract Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken);
    /**
     * Disconnect this {@link Channel} from its remote peer
     */
    protected virtual Task DoDisconnectAsync(CancellationToken cancellationToken) { DoClose(); _active = false; return Task.CompletedTask; }
    /**
     * Close the {@link Channel}
     */
    protected abstract void DoClose();
    /**
     * Schedule a read operation.
     */
    protected abstract void DoBeginRead();
    // CLR: per-message Task write used by transports without a doWrite(ChannelOutboundBuffer) loop.
    protected virtual Task DoWrite(object msg)
    {
        return Task.FromException(new NotSupportedException());
    }
    protected virtual void DoWriteComplete() { }
    protected internal virtual void OnUnhandledException(Exception error)
    {
        Logger.Warn("Unhandled channel exception.", error);
    }
    public virtual Task BindAsync(EndPoint address, CancellationToken cancellationToken = default)
    {
        return Pipeline.BindAsync(address, cancellationToken);
    }
    public virtual Task ConnectAsync(EndPoint remote, EndPoint localAddress = null, CancellationToken cancellationToken = default)
    {
        return Pipeline.ConnectAsync(remote, localAddress, cancellationToken);
    }
    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Pipeline.DisconnectAsync(cancellationToken);
    }
    public virtual Task CloseAsync(CancellationToken cancellationToken = default)
    {
        return Pipeline.CloseAsync(cancellationToken);
    }
    public virtual Task DeregisterAsync(CancellationToken cancellationToken = default)
    {
        return Pipeline.DeregisterAsync(cancellationToken);
    }
    public virtual Task WriteAsync(object msg, CancellationToken cancellationToken = default)
    {
        return Pipeline.WriteAsync(msg, cancellationToken);
    }
    public virtual Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
    {
        return Pipeline.WriteAndFlushAsync(msg, cancellationToken);
    }
    public virtual IChannelOutboundInvoker Read() { Pipeline.Read(); return this; }
    public virtual IChannelOutboundInvoker Flush() { Pipeline.Flush(); return this; }
    /**
     * Returns the ID of this channel.
     */
    public sealed override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    /**
     * Returns {@code true} if and only if the specified object is identical
     * with this channel (i.e: {@code this == o}).
     */
    public sealed override bool Equals(object o)
    {
        return ReferenceEquals(this, o);
    }

    public int CompareTo(IChannel other)
    {
        return other == null ? 1 : Id.CompareTo(other.Id);
    }

    // CLR: AbstractUnsafe.closeIfClosed() folded into the channel.
    protected void CloseIfClosed()
    {
        if (IsOpen)
        {
            return;
        }
        _ = Close();
    }

    /**
     * Appends the remote address to the message of the exceptions caused by connection attempt failure.
     */
    // CLR: ConnectException, NoRouteToHostException and SocketException are all SocketException here. The annotated
    // exception is a new SocketException with the same error code and the source message (SocketException cannot take
    // an inner exception). IsConnected and AlreadyInProgress stand for Java's AlreadyConnectedException and
    // ConnectionPendingException, which are not SocketExceptions, so they are not annotated
    // (https://learn.microsoft.com/dotnet/api/system.net.sockets.socketexception.-ctor).
    protected Exception AnnotateConnectException(Exception cause, EndPoint remoteAddress)
    {
        if (cause is SocketException socketException &&
            socketException.SocketErrorCode is not (SocketError.IsConnected or SocketError.AlreadyInProgress))
        {
            return new SocketException(socketException.ErrorCode, socketException.Message + ": " + remoteAddress);
        }

        return cause;
    }

    // CLR: InetAddress.isAnyLocalAddress().
    internal static bool IsAnyLocalAddress(IPAddress address)
    {
        return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
    }

    // CLR: PlatformDependent.maybeSuperUser() compares the user.name system property; Environment.UserName is the
    // process user (https://learn.microsoft.com/dotnet/api/system.environment.username).
    internal static bool MaybeSuperUser()
    {
        string username = Environment.UserName;
        if (PlatformDependent.IsWindows())
        {
            return "Administrator".Equals(username, StringComparison.Ordinal);
        }
        // Check for root and toor as some BSDs have a toor user that is basically the same as root.
        return "root".Equals(username, StringComparison.Ordinal) || "toor".Equals(username, StringComparison.Ordinal);
    }
    /**
     * Returns the {@link String} representation of this channel.  The returned
     * string contains the {@linkplain #hashCode() ID}, {@linkplain #localAddress() local address},
     * and {@linkplain #remoteAddress() remote address} of this channel for
     * easier identification.
     */
    public override string ToString()
    {
        bool active = IsActive;
        if (_strValActive == active && _strVal != null)
        {
            return _strVal;
        }

        // CLR: LocalAddress/RemoteAddress are live transport properties rather than the source's cached fields.
        EndPoint remoteAddr = RemoteAddress;
        EndPoint localAddr = LocalAddress;
        if (remoteAddr != null)
        {
            StringBuilder buf = new StringBuilder(96)
                .Append("[id: 0x")
                .Append(Id.AsShortText())
                .Append(", L:")
                // CLR: String.valueOf renders null as "null"; StringBuilder.Append(null) would append nothing.
                .Append(localAddr?.ToString() ?? "null")
                .Append(active ? " - " : " ! ")
                .Append("R:")
                .Append(remoteAddr)
                .Append(']');
            _strVal = buf.ToString();
        }
        else if (localAddr != null)
        {
            StringBuilder buf = new StringBuilder(64)
                .Append("[id: 0x")
                .Append(Id.AsShortText())
                .Append(", L:")
                .Append(localAddr)
                .Append(']');
            _strVal = buf.ToString();
        }
        else
        {
            StringBuilder buf = new StringBuilder(16)
                .Append("[id: 0x")
                .Append(Id.AsShortText())
                .Append(']');
            _strVal = buf.ToString();
        }

        _strValActive = active;
        return _strVal;
    }
}
