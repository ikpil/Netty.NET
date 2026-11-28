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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels.Local;

/// <summary>In-process peer transport with separate event-loop ownership and queued message reference counts.</summary>
/**
 * A {@link Channel} for the local transport.
 */
public class LocalChannel : AbstractChannel
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(LocalChannel));
    // CLR: Interlocked on the finishReadFuture and state fields replaces FINISH_READ_FUTURE_UPDATER and STATE_UPDATER.
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false);
    private const int MAX_READER_STACK_DEPTH = 8;

    // CLR: the Java enum is a reference type whose field starts as null (neither bound nor connected). Reference
    // constants keep that null and let Interlocked.Exchange/CompareExchange update the field as STATE_UPDATER does.
    // https://learn.microsoft.com/dotnet/api/system.threading.interlocked.compareexchange
    private sealed class State
    {
        internal static readonly State OPEN = new State(nameof(OPEN));
        internal static readonly State BOUND = new State(nameof(BOUND));
        internal static readonly State CONNECTED = new State(nameof(CONNECTED));
        internal static readonly State CLOSED = new State(nameof(CLOSED));

        private readonly string _name;

        private State(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }
    }

    // CLR: the configuration is created by NewConfig() below.
    // To further optimize this we could write our own SPSC queue.
    // CLR: ConcurrentQueue replaces PlatformDependent.newSpscQueue(); it is safe for one producer and one consumer.
    // https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentqueue-1
    internal readonly ConcurrentQueue<object> inboundBuffer = new ConcurrentQueue<object>();
    // CLR: The constructors cache their delegates.
    private readonly Action readTask;

    private readonly Action finishReadTask;

    // CLR: orders inboundBuffer.add(...) of a writing peer with doClose()'s state change (see AddInbound).
    private readonly object inboundGate = new object();

    private IIoRegistration registration;

    private volatile State state;
    private volatile LocalChannel peer;
    private volatile LocalAddress localAddress;
    private volatile LocalAddress remoteAddress;
    // CLR: the connect operation's TaskCompletionSource is the ChannelPromise.
    private volatile TaskCompletionSource connectPromise;
    private volatile bool readInProgress;
    private volatile bool writeInProgress;
    private volatile Task finishReadFuture;

    // CLR: AbstractUnsafe is folded into AbstractChannel; LocalUnsafe is the registered LocalIoHandle.
    private readonly LocalUnsafe _unsafe;

    public LocalChannel() : base(null, METADATA)
    {
        readTask = ReadTask;
        finishReadTask = FinishReadTask;
        _unsafe = new LocalUnsafe(this);
    }

    protected internal LocalChannel(LocalServerChannel parent, LocalChannel peer) : base(parent, METADATA)
    {
        readTask = ReadTask;
        finishReadTask = FinishReadTask;
        _unsafe = new LocalUnsafe(this);
        this.peer = peer;
        localAddress = (LocalAddress)parent.LocalAddress;
        remoteAddress = (LocalAddress)peer.LocalAddress;
    }

    private void ReadTask()
    {
        // Ensure the inboundBuffer is not empty as readInbound() will always call fireChannelReadComplete()
        if (!inboundBuffer.IsEmpty)
        {
            ReadInbound();
        }
    }

    private void FinishReadTask()
    {
        FinishPeerRead0(this);
    }

    // CLR: AbstractChannel creates the configuration lazily, so config().setAllocator(new
    // PreferHeapByteBufAllocator(config.getAllocator())) of the constructors runs here.
    protected override IChannelConfig NewConfig()
    {
        var config = new DefaultChannelConfig(this);
        config.SetAllocator(new PreferHeapByteBufAllocator(config.GetAllocator()));
        return config;
    }

    // CLR: AbstractChannel keeps its own lifecycle flags for the shared close/registration paths; the source state
    // field is combined with them.
    public override bool IsOpen => state != State.CLOSED && base.IsOpen;

    public override bool IsActive => state == State.CONNECTED && base.IsActive;

    protected override bool IsCompatible(IEventLoop loop)
    {
        return loop is SingleThreadEventLoop ||
                (loop is IIoEventLoop ioEventLoop && ioEventLoop.IsCompatible(typeof(LocalUnsafe)));
    }

    protected override EndPoint LocalAddress0()
    {
        return localAddress;
    }

    protected override EndPoint RemoteAddress0()
    {
        return remoteAddress;
    }

    // CLR: doRegister(ChannelPromise) runs on the event loop, where IoEventLoop.register(IoHandle) completes
    // synchronously; the completed Task replaces the future listener.
    protected override void DoRegister()
    {
        IEventLoop loop = EventLoop;
        if (loop is IIoEventLoop ioEventLoop)
        {
            Debug.Assert(registration == null);
            registration = ioEventLoop.RegisterAsync(_unsafe).GetAwaiter().GetResult();
        }
        else
        {
            _unsafe.Registered();
        }
    }

    protected override void DoDeregister()
    {
        IEventLoop loop = EventLoop;
        if (loop is IIoEventLoop)
        {
            IIoRegistration registration = this.registration;
            if (registration != null)
            {
                this.registration = null;
                registration.Cancel();
            }
        }
        else
        {
            _unsafe.Unregistered();
        }
    }

    // CLR: the bind operation Task; doBind(SocketAddress) itself is synchronous.
    protected override Task DoBindAsync(EndPoint localAddress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DoBind(localAddress);
        return Task.CompletedTask;
    }

    private void DoBind(EndPoint localAddress)
    {
        this.localAddress =
                LocalChannelRegistry.Register(this, this.localAddress,
                        localAddress);
        state = State.BOUND;
    }

    // CLR: LocalUnsafe.connect(...) replaces the whole connect operation, so no transport connect step is used.
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // CLR: LocalUnsafe.connect(...) overrides it.
    protected internal override Task ConnectTransportAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return _unsafe.Connect(remote, local, cancellationToken);
    }

    protected override Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        DoClose();
        return Task.CompletedTask;
    }

    protected override void DoClose()
    {
        LocalChannel peer = this.peer;
        State oldState = state;
        State closedFrom = oldState;

        try
        {
            if (oldState != State.CLOSED)
            {
                // Update all internal state before the closeFuture is notified.
                if (localAddress != null)
                {
                    if (Parent == null)
                    {
                        LocalChannelRegistry.Unregister(localAddress);
                    }

                    localAddress = null;
                }

                // State change must happen before finishPeerRead to ensure writes are released either in doWrite or
                // channelRead.
                // Use getAndSet() as an accepted channel may concurrently move this channel from BOUND to CONNECTED
                // when it is registered. See LocalUnsafe.registered().
                // CLR: under the gate, so a writing peer cannot publish into the queue drained below (see AddInbound).
                lock (inboundGate)
                {
#pragma warning disable CS0420 // Interlocked operations are full fences.
                    closedFrom = Interlocked.Exchange(ref state, State.CLOSED);
#pragma warning restore CS0420
                }

                // Preserve order of event and force a read operation now before the close operation is processed.
                if (writeInProgress && peer != null)
                {
                    FinishPeerRead(peer);
                }

                TaskCompletionSource promise = connectPromise;
                if (promise != null)
                {
                    // Use tryFailure() instead of setFailure() to avoid the race against cancel().
                    promise.TrySetException(new ClosedChannelException());
                    connectPromise = null;
                }
            }

            if (peer != null && Parent == null && closedFrom != State.CONNECTED)
            {
                // The accepted channel did not complete the connection. Don't touch it: it may not have an event
                // loop yet and is owned by the server channel or its own event loop. When it is registered it sees
                // that this channel is closed and closes itself. If it was closed already, its close has notified
                // this channel or will do so.
                this.peer = null;
            }
            else if (peer != null)
            {
                this.peer = null;
                // Always call peer.eventLoop().execute() even if peer.eventLoop().inEventLoop() is true.
                // This ensures that if both channels are on the same event loop, the peer's channelInActive
                // event is triggered *after* this peer's channelInActive event
                IEventLoop peerEventLoop = peer.EventLoop;
                bool peerIsActive = peer.IsActive;

                try
                {
                    peerEventLoop.Execute(() => peer.TryClose(peerIsActive));
                }
                catch (Exception cause)
                {
                    logger.Warn("Releasing Inbound Queues for channels {}-{} because exception occurred!",
                            this, peer, cause);

                    if (peerEventLoop.InEventLoop())
                    {
                        peer.ReleaseInboundBuffers();
                    }
                    else
                    {
                        // inboundBuffers is a SPSC so we may leak if the event loop is shutdown prematurely or
                        // rejects the close Runnable but give a best effort.
                        _ = peer.CloseAsync();
                    }

                    throw;
                }
            }
        }
        finally
        {
            // Release all buffers if the Channel was already registered in the past and if it was not closed before.
            if (oldState != null && oldState != State.CLOSED)
            {
                // We need to release all the buffers that may be put into our inbound queue since we closed the Channel
                // to ensure we not leak any memory. This is fine as it basically gives the same guarantees as TCP which
                // means even if the promise was notified before its not really guaranteed that the "remote peer" will
                // see the buffer at all.
                ReleaseInboundBuffers();
            }
        }
    }

    private void TryClose(bool isActive)
    {
        if (isActive)
        {
            _ = Close();
        }
        else if (connectPromise != null)
        {
            // The accepted channel was closed before the connection was established, for example because the server
            // channel was closed. It is closed already, so don't notify it again. Fail the connect like a refused
            // connection and close this channel.
            TaskCompletionSource promise = connectPromise;
            connectPromise = null;
            peer = null;
            // CLR: SocketError.ConnectionRefused stands for ConnectException
            // (https://learn.microsoft.com/dotnet/api/system.net.sockets.socketexception.-ctor).
            promise.TrySetException(new SocketException((int)SocketError.ConnectionRefused,
                    "connection refused: the accepted channel was closed"));
            _ = Close();
        }
        else
        {
            ReleaseInboundBuffers();
        }
    }

    private void ReadInbound()
    {
        IRecvByteBufAllocator.IHandle handle = RecvBufAllocHandle();
        handle.Reset(Config);
        IChannelPipeline pipeline = Pipeline;

        do
        {
            // CLR: TryDequeue returns false where poll() returns null.
            if (!inboundBuffer.TryDequeue(out object received))
            {
                break;
            }

            if (received is ByteBuf && inboundBuffer.TryPeek(out object next) && next is ByteBuf)
            {
                ByteBuf msg = (ByteBuf)received;
                ByteBuf output = handle.Allocate(Alloc());
                if (msg.ReadableBytes < output.WritableBytes)
                {
                    // We have an opportunity to coalesce buffers.
                    // CLR: indexed transfer handles segmented storage and preserves the source reader index.
                    output.WriteBytes(msg, msg.ReaderIndex, msg.ReadableBytes);
                    msg.Release();

                    while (inboundBuffer.TryPeek(out received) && received is ByteBuf peeked &&
                            peeked.ReadableBytes < output.WritableBytes)
                    {
                        inboundBuffer.TryDequeue(out _);
                        msg = peeked;
                        output.WriteBytes(msg, msg.ReaderIndex, msg.ReadableBytes);
                        msg.Release();
                    }

                    handle.LastBytesRead(output.ReadableBytes);
                    received = output; // Send the coalesced buffer down the pipeline.
                }
                else
                {
                    // It won't be profitable to coalesce buffers this time around.
                    handle.LastBytesRead(output.Capacity);
                    output.Release();
                }
            }

            handle.IncMessagesRead(1);
            pipeline.FireChannelRead(received);
        } while (handle.ContinueReading());

        handle.ReadComplete();
        pipeline.FireChannelReadComplete();
    }

    protected override void DoBeginRead()
    {
        if (readInProgress)
        {
            return;
        }

        ConcurrentQueue<object> inboundBuffer = this.inboundBuffer;
        if (inboundBuffer.IsEmpty)
        {
            readInProgress = true;
            return;
        }

        InternalThreadLocalMap threadLocals = InternalThreadLocalMap.Get();
        int stackDepth = threadLocals.LocalChannelReaderStackDepth();
        if (stackDepth < MAX_READER_STACK_DEPTH)
        {
            threadLocals.SetLocalChannelReaderStackDepth(stackDepth + 1);

            try
            {
                ReadInbound();
            }
            finally
            {
                threadLocals.SetLocalChannelReaderStackDepth(stackDepth);
            }
        }
        else
        {
            try
            {
                EventLoop.Execute(readTask);
            }
            catch (Exception cause)
            {
                logger.Warn("Closing Local channels {}-{} because exception occurred!", this, peer, cause);
                _ = CloseAsync();
                _ = peer.CloseAsync();
                throw;
            }
        }
    }

    // CLR: flush0() calls doWrite(ChannelOutboundBuffer) for this transport.
    internal override bool HasDoWriteLoop => true;

    protected override void DoWrite(ChannelOutboundBuffer @in)
    {
        State state = this.state;
        if (state == State.OPEN || state == State.BOUND || state == null)
        {
            // CLR: SocketError.NotConnected stands for NotYetConnectedException; the source switch has no case for
            // the initial null state, which is not connected either.
            throw new SocketException((int)SocketError.NotConnected);
        }
        if (state == State.CLOSED)
        {
            throw new ClosedChannelException();
        }

        LocalChannel peer = this.peer;

        writeInProgress = true;

        try
        {
            ClosedChannelException exception = null;

            for (;;)
            {
                object msg = @in.Current;
                if (msg == null)
                {
                    break;
                }

                try
                {
                    // It is possible the peer could have closed while we are writing, and in this case we should
                    // simulate real socket behavior and ensure the write operation is failed.
                    if (peer.state == State.CONNECTED && peer.AddInbound(msg))
                    {
                        @in.Remove();
                    }
                    else
                    {
                        if (exception == null)
                        {
                            exception = new ClosedChannelException();
                        }

                        @in.Remove(exception);
                    }
                }
                catch (Exception cause)
                {
                    @in.Remove(cause);
                }
            }
        }
        finally
        {
            // The following situation may cause trouble:
            // 1. Write (with promise X)
            // 2. promise X is completed when in.remove() is called, and a listener on this promise calls close()
            // 3. Then the close event will be executed for the peer before the write events, when the write events
            // actually happened before the close event.
            writeInProgress = false;
        }

        FinishPeerRead(peer);
    }

    // CLR: peer.inboundBuffer.add(ReferenceCountUtil.retain(msg)). retain may run user code, so it stays outside the
    // gate; the gate orders the add with doClose()'s state change, so a retained reference is either drained by
    // releaseInboundBuffers() or never published.
    private bool AddInbound(object msg)
    {
        object retained = ReferenceCountUtil.Retain(msg);

        lock (inboundGate)
        {
            if (state == State.CONNECTED)
            {
                inboundBuffer.Enqueue(retained);
                return true;
            }
        }

        ReferenceCountUtil.Release(retained);
        return false;
    }

    private void FinishPeerRead(LocalChannel peer)
    {
        // If the peer is also writing, then we must schedule the event on the event loop to preserve read order.
        if (peer.EventLoop == EventLoop && !peer.writeInProgress)
        {
            FinishPeerRead0(peer);
        }
        else
        {
            RunFinishPeerReadTask(peer);
        }
    }

    private void RunFinishTask0()
    {
        // If the peer is writing, we must wait until after reads are completed for that peer before we can read. So
        // we keep track of the task, and coordinate later that our read can't happen until the peer is done.
        if (writeInProgress)
        {
            Task future = EventLoop.SubmitAsync(finishReadTask);
            // CLR: SubmitAsync reports a rejected submission through its Task, where submit(...) throws.
            if (future.IsFaulted && future.Exception.InnerException is RejectedExecutionException)
            {
                future.GetAwaiter().GetResult();
            }

            finishReadFuture = future;
        }
        else
        {
            EventLoop.Execute(finishReadTask);
        }
    }

    private void RunFinishPeerReadTask(LocalChannel peer)
    {
        try
        {
            peer.RunFinishTask0();
        }
        catch (Exception cause)
        {
            logger.Warn("Closing Local channels {}-{} because exception occurred!", this, peer, cause);
            _ = CloseAsync();
            _ = peer.CloseAsync();
            throw;
        }
    }

    private void ReleaseInboundBuffers()
    {
        // CLR: AssignedEventLoop is null before registration, where eventLoop() throws.
        Debug.Assert(AssignedEventLoop == null || AssignedEventLoop.InEventLoop());
        readInProgress = false;
        ConcurrentQueue<object> inboundBuffer = this.inboundBuffer;

        while (inboundBuffer.TryDequeue(out object msg))
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    private void FinishPeerRead0(LocalChannel peer)
    {
        Task peerFinishReadFuture = peer.finishReadFuture;
        if (peerFinishReadFuture != null)
        {
            if (!peerFinishReadFuture.IsCompleted)
            {
                RunFinishPeerReadTask(peer);
                return;
            }
            else
            {
                // Lazy unset to make sure we don't prematurely unset it while scheduling a new task.
#pragma warning disable CS0420 // Interlocked operations are full fences.
                Interlocked.CompareExchange(ref peer.finishReadFuture, null, peerFinishReadFuture);
#pragma warning restore CS0420
            }
        }

        // We should only set readInProgress to false if there is any data that was read as otherwise we may miss to
        // forward data later on.
        if (peer.readInProgress && !peer.inboundBuffer.IsEmpty)
        {
            peer.readInProgress = false;
            peer.ReadInbound();
        }
    }

    // CLR: AbstractUnsafe is folded into AbstractChannel; LocalUnsafe is the registered LocalIoHandle.
    private sealed class LocalUnsafe : ILocalIoHandle
    {
        private readonly LocalChannel _channel;
        private readonly Action shutdownHook;

        internal LocalUnsafe(LocalChannel channel)
        {
            _channel = channel;
            shutdownHook = CloseNow;
        }

        public void Close()
        {
            _ = _channel.Close();
        }

        public void Handle(IIoRegistration registration, IIoEvent @event)
        {
            // NOOP
        }

        public void Registered()
        {
            // Check if both peer and parent are non-null because this channel was created by a LocalServerChannel.
            // This is needed as a peer may not be null also if a LocalChannel was connected before and
            // deregistered / registered later again.
            //
            // See https://github.com/netty/netty/issues/2400
            //
            // Store the peer in a local variable as it may be set to null if doClose() is called.
            // See https://github.com/netty/netty/issues/2144
            LocalChannel peer = _channel.peer;
            if (peer != null && _channel.Parent != null)
            {
                // Set our state before the peer's, so the peer sees this channel as active once it is connected.
                State oldState = _channel.state;
                _channel.state = State.CONNECTED;
                // CLR: AbstractChannel's active flag follows the state (see IsActive).
                _channel.SetActive(true);

                // Move the peer from BOUND to CONNECTED atomically, as it may be closed concurrently on its own
                // event loop. If this channel was connected before and is registered again, the peer is CONNECTED
                // already.
#pragma warning disable CS0420 // Interlocked operations are full fences.
                if (Interlocked.CompareExchange(ref peer.state, State.CONNECTED, State.BOUND) == State.BOUND ||
                        peer.state == State.CONNECTED)
#pragma warning restore CS0420
                {
                    peer.SetActive(true);
                    peer.remoteAddress = (LocalAddress)_channel.Parent.LocalAddress;

                    // Always call peer.eventLoop().execute() even if peer.eventLoop().inEventLoop() is true.
                    // This ensures that if both channels are on the same event loop, the peer's channelActive
                    // event is triggered *after* this channel's channelRegistered event, so that this channel's
                    // pipeline is fully initialized by ChannelInitializer before any channelRead events.
                    peer.EventLoop.Execute(() =>
                    {
                        TaskCompletionSource promise = peer.connectPromise;

                        // Only trigger fireChannelActive() if the promise was not null and was not completed
                        // yet. connectPromise may be set to null if doClose() was called in the meantime.
                        if (promise != null && promise.TrySetResult())
                        {
                            peer.Pipeline.FireChannelActive();
                            // CLR: AbstractChannel holds a read() issued before activation until the channel is active.
                            peer.BeginReadIfRequestedBeforeActive();
                        }
                    });
                }
                else
                {
                    // The peer was closed before this channel was registered, so it never uses this channel.
                    // Close this channel once the registration is complete.
                    _channel.peer = null;
                    _channel.state = oldState;
                    _channel.SetActive(false);
                    _channel.EventLoop.Execute(CloseNow);
                }
            }

            IEventLoop loop = _channel.EventLoop;
            if (!(loop is IIoEventLoop) && loop is SingleThreadEventExecutor executor)
            {
                executor.AddShutdownHook(shutdownHook);
            }
        }

        public void Unregistered()
        {
            IEventLoop loop = _channel.EventLoop;
            if (!(loop is IIoEventLoop) && loop is SingleThreadEventExecutor executor)
            {
                executor.RemoveShutdownHook(shutdownHook);
            }
        }

        public void CloseNow()
        {
            _ = _channel.Close();
        }

        // CLR: the returned Task is the connect promise.
        internal Task Connect(EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
        {
            LocalChannel channel = _channel;
            // CLR: setUncancellable() fails for a promise cancelled already; so does a token cancelled before the
            // connect starts.
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!EnsureOpen(promise))
            {
                return promise.Task;
            }

            if (channel.state == State.CONNECTED)
            {
                // CLR: SocketError.IsConnected stands for AlreadyConnectedException.
                Exception cause = new SocketException((int)SocketError.IsConnected);
                promise.TrySetException(cause);
                return promise.Task;
            }

            if (channel.connectPromise != null)
            {
                // CLR: SocketError.AlreadyInProgress stands for ConnectionPendingException; the thrown exception fails
                // the operation as AbstractChannelHandlerContext fails the promise of a throwing connect(...).
                return Task.FromException(new SocketException((int)SocketError.AlreadyInProgress));
            }

            channel.connectPromise = promise;

            if (channel.state != State.BOUND)
            {
                // Not bound yet and no localAddress specified - get one.
                if (localAddress == null)
                {
                    localAddress = new LocalAddress(channel);
                }
            }

            if (localAddress != null)
            {
                try
                {
                    channel.DoBind(localAddress);
                }
                catch (Exception t)
                {
                    promise.TrySetException(t);
                    _ = channel.Close();
                    return promise.Task;
                }
            }

            IChannel boundChannel = LocalChannelRegistry.Get(remoteAddress);
            if (!(boundChannel is LocalServerChannel))
            {
                // CLR: SocketError.ConnectionRefused stands for ConnectException.
                Exception cause = new SocketException((int)SocketError.ConnectionRefused, "connection refused: " + remoteAddress);
                promise.TrySetException(cause);
                _ = channel.Close();
                return promise.Task;
            }

            LocalServerChannel serverChannel = (LocalServerChannel)boundChannel;
            channel.peer = serverChannel.Serve(channel);
            RegisterCancellation(promise, cancellationToken);
            return promise.Task;
        }

        // CLR: ensureOpen(ChannelPromise).
        private bool EnsureOpen(TaskCompletionSource promise)
        {
            try
            {
                _channel.EnsureOpen();

                return true;
            }
            catch (ClosedChannelException e)
            {
                promise.TrySetException(e);
                return false;
            }
        }

        // CLR: the source connect promise is uncancellable once connect(...) starts. A CancellationToken still aborts
        // the pending connect: on the owner, the connect is cancelled and this channel closed, so the queued accepted
        // channel sees it CLOSED when it is registered and closes itself.
        private void RegisterCancellation(TaskCompletionSource promise, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || promise.Task.IsCompleted)
            {
                return;
            }

            LocalChannel channel = _channel;
            CancellationTokenRegistration cancellation = cancellationToken.UnsafeRegister(_ =>
            {
                void Cancel()
                {
                    if (channel.connectPromise == promise && !promise.Task.IsCompleted)
                    {
                        channel.connectPromise = null;
                        _ = channel.Close();
                    }

                    promise.TrySetCanceled(cancellationToken);
                }

                try
                {
                    channel.ExecuteOnLoop(Cancel);
                }
                catch (Exception)
                {
                    // The event loop rejected the task: the connect can no longer complete on it.
                    promise.TrySetCanceled(cancellationToken);
                }
            }, null);
            _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state).Dispose(),
                    cancellation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
