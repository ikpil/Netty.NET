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

namespace Netty.NET.Transport.Channels.Nio;

// CLR: the underlying channel is a non-blocking System.Net.Sockets.Socket, registered with NioIoHandler through
// IIoEventLoop.RegisterAsync(IIoHandle) exactly as the Java channel registers its AbstractNioUnsafe. Interest is
// submitted as NioIoOps and readiness arrives as INioIoEvent, so the read, write and connect paths keep the Java
// non-blocking shape: Socket.Blocking = false and SocketError.WouldBlock stands for Java's 0/null results.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.blocking
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socketerror
/**
 * Abstract base class for {@link Channel} implementations which use a Selector based approach.
 */
public abstract class AbstractNioChannel : AbstractChannel
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(AbstractNioChannel));

    private readonly Socket _ch;
    protected readonly int ReadInterestOp;
    protected readonly NioIoOps ReadOps;
    internal volatile IIoRegistration _registration;
    internal bool ReadPending;
    private readonly AbstractNioUnsafe _unsafe;

    /**
     * The future of the current connection attempt.  If not null, subsequent
     * connection attempts will fail.
     */
    private TaskCompletionSource _connectPromise;
    private CancellationTokenSource _connectTimeoutFuture;
    private EndPoint _requestedRemoteAddress;

    /**
     * Create a new instance
     *
     * @param parent            the parent {@link Channel} by which this instance was created. May be {@code null}
     * @param ch                the underlying {@link SelectableChannel} on which it operates
     * @param readOps           the ops to set to receive data from the {@link SelectableChannel}
     */
    // CLR: the metadata argument replaces the metadata() override of each Java subclass.
    protected AbstractNioChannel(IChannel parent, Socket ch, int readOps, ChannelMetadata metadata)
        : this(parent, ch, NioIoOps.ValueOf(readOps), metadata)
    {
    }

    // CLR: ChannelException wraps the SocketException of Socket.Blocking as it wraps the IOException of
    // configureBlocking(false).
    protected AbstractNioChannel(IChannel parent, Socket ch, NioIoOps readOps, ChannelMetadata metadata)
        : base(parent, metadata)
    {
        _ch = ch ?? throw new ArgumentNullException(nameof(ch));
        ReadInterestOp = ObjectUtil.CheckNotNull(readOps, "readOps").Value();
        ReadOps = readOps;

        try
        {
            ch.Blocking = false;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            try
            {
                ch.Close();
            }
            catch (SocketException e2)
            {
                logger.Warn(
                            "Failed to close a partially initialized socket.", e2);
            }

            throw new ChannelException("Failed to enter non-blocking mode.", e);
        }

        _unsafe = NewUnsafe();
    }

    protected void AddAndSubmit(NioIoOps addOps)
    {
        int interestOps = Registration().Attachment<NioIoOps>().Value();
        if (!addOps.IsIncludedIn(interestOps))
        {
            try
            {
                Registration().Submit(NioIoOps.ValueOf(interestOps).With(addOps));
            }
            catch (Exception e)
            {
                throw new ChannelException(e);
            }
        }
    }

    protected void RemoveAndSubmit(NioIoOps removeOps)
    {
        int interestOps = Registration().Attachment<NioIoOps>().Value();
        if (removeOps.IsIncludedIn(interestOps))
        {
            try
            {
                Registration().Submit(NioIoOps.ValueOf(interestOps).Without(removeOps));
            }
            catch (Exception e)
            {
                throw new ChannelException(e);
            }
        }
    }

    // CLR: the transport-owned open flag also follows the socket, as ch.isOpen() does.
    public override bool IsOpen => base.IsOpen && NioIoHandler.SocketSelector.IsOpen(_ch);

    // CLR: AbstractUnsafe is folded into AbstractChannel; the NIO unsafe is the registered IoHandle.
    protected AbstractNioUnsafe Unsafe()
    {
        return _unsafe;
    }

    protected abstract AbstractNioUnsafe NewUnsafe();

    protected Socket JavaChannel()
    {
        return _ch;
    }

    protected IIoRegistration Registration()
    {
        Debug.Assert(_registration != null);
        return _registration;
    }

    /**
     * Set read pending to {@code false}.
     */
    /**
     * @deprecated No longer supported.
     * No longer supported.
     */
    // CLR: ported although @Deprecated because its @deprecated text names no replacement.
    protected bool IsReadPending()
    {
        return ReadPending;
    }

    // CLR: AbstractChannel.ClearReadPending dispatches to the event loop, or clears the flag directly while the
    // channel is unregistered; this is clearReadPending0().
    protected override void DoClearReadPending()
    {
        ReadPending = false;
        _unsafe.RemoveReadOp();
    }

    /**
     * Special {@link Unsafe} sub-type which allows to access the underlying {@link SelectableChannel}
     */
    // CLR: the NioUnsafe interface members (ch(), finishConnect(), read(), forceFlush()) are the public members of
    // AbstractNioUnsafe; channel-level unsafe operations stay on AbstractChannel.
    protected abstract class AbstractNioUnsafe : INioIoHandle
    {
        private readonly AbstractNioChannel _channel;

        protected AbstractNioUnsafe(AbstractNioChannel channel)
        {
            _channel = channel;
        }

        public void Close()
        {
            _ = _channel.Close();
        }

        public Socket SelectableChannel()
        {
            return Ch();
        }

        internal IChannel Channel()
        {
            return _channel;
        }

        protected internal void RemoveReadOp()
        {
            // Read the field directly as the channel may have been deregistered concurrently (setting
            // registration to null) after the isRegistered() check in clearReadPending() but before the
            // clearReadPendingRunnable is executed on the EventLoop. See https://github.com/netty/netty/issues/17103
            IIoRegistration registration = _channel._registration;
            // Check first if the key is still valid as it may be canceled as part of the deregistration
            // from the EventLoop
            // See https://github.com/netty/netty/issues/2104
            if (registration == null || !registration.IsValid())
            {
                return;
            }

            _channel.RemoveAndSubmit(_channel.ReadOps);
        }

        /**
         * Return underlying {@link SelectableChannel}
         */
        public Socket Ch()
        {
            return _channel.JavaChannel();
        }

        /**
         * Finish connect
         */
        public void FinishConnect()
        {
            _channel.FinishConnect();
        }

        /**
         * Read from underlying {@link SelectableChannel}
         */
        public abstract void Read();

        public void ForceFlush()
        {
            // directly call super.flush0() to force a flush now
            _channel.ForceFlush0();
        }

        public void Handle(IIoRegistration registration, IIoEvent @event)
        {
            try
            {
                INioIoEvent nioEvent = (INioIoEvent)@event;
                NioIoOps nioReadyOps = nioEvent.Ops();
                // We first need to call finishConnect() before try to trigger a read(...) or write(...) as otherwise
                // the NIO JDK channel implementation may throw a NotYetConnectedException.
                if (nioReadyOps.Contains(NioIoOps.CONNECT))
                {
                    // remove OP_CONNECT as otherwise Selector.select(..) will always return without blocking
                    // See https://github.com/netty/netty/issues/924
                    _channel.RemoveAndSubmit(NioIoOps.CONNECT);

                    FinishConnect();
                }

                // Process OP_WRITE first as we may be able to write some queued buffers and so free memory.
                if (nioReadyOps.Contains(NioIoOps.WRITE))
                {
                    // Call forceFlush which will also take care of clear the OP_WRITE once there is nothing left to
                    // write
                    ForceFlush();
                }

                // Also check for readOps of 0 to workaround possible JDK bug which may otherwise lead
                // to a spin loop
                if (nioReadyOps.Contains(NioIoOps.READ_AND_ACCEPT) || nioReadyOps.Equals(NioIoOps.NONE))
                {
                    Read();
                }
            }
            // CLR: an ObjectDisposedException from the closed socket stands for CancelledKeyException.
            catch (ObjectDisposedException)
            {
                Close();
            }
        }
    }

    // CLR: AbstractNioUnsafe.connect(SocketAddress, SocketAddress, ChannelPromise); the returned Task is the
    // promise and the CancellationToken replaces promise cancellation.
    protected internal override Task ConnectTransportAsync(EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
    {
        var promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Don't mark the connect promise as uncancellable as in fact we can cancel it as it is using
        // non-blocking io.
        if (cancellationToken.IsCancellationRequested)
        {
            promise.TrySetCanceled(cancellationToken);
            return promise.Task;
        }

        try
        {
            EnsureOpen();
        }
        catch (Exception e)
        {
            promise.TrySetException(e);
            return promise.Task;
        }

        try
        {
            if (_connectPromise != null)
            {
                // Already a connect in process.
                // CLR: SocketError.AlreadyInProgress stands for ConnectionPendingException.
                throw new SocketException((int)SocketError.AlreadyInProgress);
            }

            bool wasActive = IsActive;

            if (DoConnect(remoteAddress, localAddress))
            {
                FulfillConnectPromise(promise, wasActive, cancellationToken);
            }
            else
            {
                _connectPromise = promise;
                _requestedRemoteAddress = remoteAddress;

                // Schedule connect timeout.
                int connectTimeoutMillis = Config.GetConnectTimeoutMillis();
                if (connectTimeoutMillis > 0)
                {
                    var connectTimeoutFuture = new CancellationTokenSource();
                    _connectTimeoutFuture = connectTimeoutFuture;
                    _ = EventLoop.ScheduleAsync(() =>
                    {
                        TaskCompletionSource connectPromise = _connectPromise;
                        if (connectPromise != null && !connectPromise.Task.IsCompleted
                                && connectPromise.TrySetException(new ConnectTimeoutException(
                                        "connection timed out after " + connectTimeoutMillis + " ms: " +
                                                remoteAddress)))
                        {
                            _ = Close();
                        }
                    }, TimeSpan.FromMilliseconds(connectTimeoutMillis), connectTimeoutFuture.Token);
                }

                if (cancellationToken.CanBeCanceled)
                {
                    CancellationTokenRegistration listener = cancellationToken.Register(() => ExecuteOnLoop(() =>
                    {
                        // If the connect future is cancelled we also cancel the timeout and close the
                        // underlying socket.
                        if (promise.TrySetCanceled(cancellationToken))
                        {
                            _connectTimeoutFuture?.Cancel();
                            _connectPromise = null;
                            _ = Close();
                        }
                    }));
                    _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state).Dispose(),
                        listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
        catch (Exception t)
        {
            promise.TrySetException(AnnotateConnectException(t, remoteAddress));
            CloseIfClosed();
        }

        return promise.Task;
    }

    private void FulfillConnectPromise(TaskCompletionSource promise, bool wasActive, CancellationToken cancellationToken = default)
    {
        if (promise == null)
        {
            // Closed via cancellation and the promise has been notified already.
            return;
        }

        // Get the state as trySuccess() may trigger an ChannelFutureListener that will close the Channel.
        // We still need to ensure we call fireChannelActive() in this case.
        bool active = IsActive;

        // trySuccess() will return false if a user cancelled the connection attempt.
        bool promiseSet = !cancellationToken.IsCancellationRequested && promise.TrySetResult();
        if (!promiseSet && cancellationToken.IsCancellationRequested)
        {
            promise.TrySetCanceled(cancellationToken);
        }

        // Regardless if the connection attempt was cancelled, channelActive() event should be triggered,
        // because what happened is what happened.
        if (!wasActive && active)
        {
            Pipeline.FireChannelActive();

            // CLR: BeginRead holds a read() issued before activation until the channel is active.
            if (promiseSet)
            {
                BeginReadIfRequestedBeforeActive();
            }
        }

        // If a user cancelled the connection attempt, close the channel, which is followed by channelInactive().
        if (!promiseSet)
        {
            _ = Close();
        }
    }

    private void FulfillConnectPromise(TaskCompletionSource promise, Exception cause)
    {
        if (promise == null)
        {
            // Closed via cancellation and the promise has been notified already.
            return;
        }

        // Use tryFailure() instead of setFailure() to avoid the race against cancel().
        promise.TrySetException(cause);
        CloseIfClosed();
    }

    private void FinishConnect()
    {
        // Note this method is invoked by the event loop only if the connection attempt was
        // neither cancelled nor timed out.

        Debug.Assert(EventLoop.InEventLoop());

        try
        {
            bool wasActive = IsActive;
            DoFinishConnect();
            FulfillConnectPromise(_connectPromise, wasActive);
        }
        catch (Exception t)
        {
            FulfillConnectPromise(_connectPromise, AnnotateConnectException(t, _requestedRemoteAddress));
        }
        finally
        {
            // Check for null as the connectTimeoutFuture is only created if a connectTimeoutMillis > 0 is used
            // See https://github.com/netty/netty/issues/1770
            if (_connectTimeoutFuture != null)
            {
                _connectTimeoutFuture.Cancel();
            }

            _connectPromise = null;
        }
    }

    protected sealed override void Flush0()
    {
        // Flush immediately only when there's no pending flush.
        // If there's a pending flush operation, event loop will call forceFlush() later,
        // and thus there's no need to call it now.
        if (!IsFlushPending())
        {
            base.Flush0();
        }
    }

    internal void ForceFlush0()
    {
        base.Flush0();
    }

    private bool IsFlushPending()
    {
        IIoRegistration registration = Registration();
        return registration.IsValid() && NioIoOps.WRITE.IsIncludedIn(
                registration.Attachment<NioIoOps>().Value());
    }

    internal sealed override bool HasDoWriteLoop => true;

    protected override bool IsCompatible(IEventLoop loop)
    {
        return loop is IIoEventLoop ioEventLoop && ioEventLoop.IsCompatible(typeof(AbstractNioUnsafe));
    }

    // CLR: doRegister(ChannelPromise) runs on the event loop, where IoEventLoop.register(IoHandle) completes
    // synchronously; the completed Task replaces the future listener.
    protected override void DoRegister()
    {
        Debug.Assert(_registration == null);
        _registration = ((IIoEventLoop)EventLoop).RegisterAsync(_unsafe).GetAwaiter().GetResult();
    }

    protected override void DoDeregister()
    {
        IIoRegistration registration = _registration;
        if (registration != null)
        {
            _registration = null;
            registration.Cancel();
        }
    }

    protected override void DoBeginRead()
    {
        // Channel.read() or ChannelHandlerContext.read() was called
        IIoRegistration registration = _registration;
        if (registration == null || !registration.IsValid())
        {
            return;
        }

        ReadPending = true;

        AddAndSubmit(ReadOps);
    }

    /**
     * Connect to the remote peer
     */
    protected abstract bool DoConnect(EndPoint remoteAddress, EndPoint localAddress);

    /**
     * Finish the connect
     */
    protected abstract void DoFinishConnect();

    /**
     * Returns an off-heap copy of the specified {@link ByteBuf}, and releases the original one.
     * Note that this method does not create an off-heap copy if the allocation / deallocation cost is too high,
     * but just returns the original {@link ByteBuf}..
     */
    protected ByteBuf NewDirectBuffer(ByteBuf buf)
    {
        int readableBytes = buf.ReadableBytes;
        if (readableBytes == 0)
        {
            ReferenceCountUtil.SafeRelease(buf);
            return Unpooled.EmptyBuffer;
        }

        IByteBufAllocator alloc = Alloc();
        if (alloc.IsDirectBufferPooled)
        {
            ByteBuf directBuf = alloc.DirectBuffer(readableBytes);
            directBuf.WriteBytes(buf, buf.ReaderIndex, readableBytes);
            ReferenceCountUtil.SafeRelease(buf);
            return directBuf;
        }

        ByteBuf threadLocalDirectBuf = ByteBufUtil.ThreadLocalDirectBuffer();
        if (threadLocalDirectBuf != null)
        {
            threadLocalDirectBuf.WriteBytes(buf, buf.ReaderIndex, readableBytes);
            ReferenceCountUtil.SafeRelease(buf);
            return threadLocalDirectBuf;
        }

        // Allocating and deallocating an unpooled direct buffer is very expensive; give up.
        return buf;
    }

    /**
     * Returns an off-heap copy of the specified {@link ByteBuf}, and releases the specified holder.
     * The caller must ensure that the holder releases the original {@link ByteBuf} when the holder is released by
     * this method.  Note that this method does not create an off-heap copy if the allocation / deallocation cost is
     * too high, but just returns the original {@link ByteBuf}..
     */
    protected ByteBuf NewDirectBuffer(IReferenceCounted holder, ByteBuf buf)
    {
        int readableBytes = buf.ReadableBytes;
        if (readableBytes == 0)
        {
            ReferenceCountUtil.SafeRelease(holder);
            return Unpooled.EmptyBuffer;
        }

        IByteBufAllocator alloc = Alloc();
        if (alloc.IsDirectBufferPooled)
        {
            ByteBuf directBuf = alloc.DirectBuffer(readableBytes);
            directBuf.WriteBytes(buf, buf.ReaderIndex, readableBytes);
            ReferenceCountUtil.SafeRelease(holder);
            return directBuf;
        }

        ByteBuf threadLocalDirectBuf = ByteBufUtil.ThreadLocalDirectBuffer();
        if (threadLocalDirectBuf != null)
        {
            threadLocalDirectBuf.WriteBytes(buf, buf.ReaderIndex, readableBytes);
            ReferenceCountUtil.SafeRelease(holder);
            return threadLocalDirectBuf;
        }

        // Allocating and deallocating an unpooled direct buffer is very expensive; give up.
        if (holder != buf)
        {
            // Ensure to call holder.release() to give the holder a chance to release other resources than its content.
            buf.Retain();
            ReferenceCountUtil.SafeRelease(holder);
        }

        return buf;
    }

    // CLR: the Task-returning connect hook of AbstractChannel is replaced by doConnect/doFinishConnect.
    protected sealed override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override void DoClose()
    {
        TaskCompletionSource promise = _connectPromise;
        if (promise != null)
        {
            // Use tryFailure() instead of setFailure() to avoid the race against cancel().
            promise.TrySetException(new ClosedChannelException());
            _connectPromise = null;
        }

        CancellationTokenSource future = _connectTimeoutFuture;
        if (future != null)
        {
            future.Cancel();
            _connectTimeoutFuture = null;
        }
    }

    // CLR: an IPv6 dual-mode socket reports IPv4 peers as IPv4-mapped IPv6 addresses; Java reports the IPv4 form.
    internal static EndPoint Normalize(EndPoint endpoint)
    {
        return endpoint is IPEndPoint ip && ip.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(ip.Address.MapToIPv4(), ip.Port) : endpoint;
    }
}
