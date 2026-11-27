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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: the C# DefaultChannelPipeline only runs on the Transport's AbstractChannel, which folds Channel.Unsafe into the
// channel (owner decision 2026-11-16, docs/transport-porting.md), so this class derives from AbstractChannel instead of
// implementing Channel next to its own Unsafe. The folded Unsafe operations are protected internal virtual (owner
// decision 2026-11-25, docs/transport-porting.md "Overridable Unsafe"), and "If you define the derived class in a
// different assembly from the base class, overridden members have protected access"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal): the Java
// Http2ChannelUnsafe is the nested class with the Java code, and this class overrides AbstractChannel's
// WriteTransportAsync (write), FlushTransport (flush), Close (close), CloseForcibly (closeForcibly),
// DisconnectTransportAsync (disconnect), DeregisterTransportAsync (deregister), BeginRead (beginRead),
// BindTransportAsync (bind) and ConnectTransportAsync (connect) with its members, and IsWritable,
// BytesBeforeUnwritable and BytesBeforeWritable with Java's totalPendingSize/unwritable fields. The writes never reach
// AbstractChannel's ChannelOutboundBuffer, as Java's outboundBuffer() returns null. ChannelPromise is
// TaskCompletionSource and ChannelFuture Task (docs/codec-http-porting.md, Shared CLR adaptations): each write, close
// and deregistration completes a promise of its own (Http2CodecUtil.NewPromise) and returns its Task; a void promise is
// a promise nobody observes (docs/transport-porting.md, VoidChannelPromise). AbstractChannel keeps three things:
// - registration: AbstractChannel's RegisterAsync owns the registered flag (IsRegistered) and the event loop
//   (EventLoop, the parent's loop here), so register(...) checks the re-registration and then lets RegisterAsync set
//   them and fire channelRegistered and, as the channel is active from its construction (SetActive(true)),
//   channelActive; its Task completes after these events, where Java completes the promise before them.
// - closePromise: AbstractChannel's CloseFuture (a TaskCompletionSource created with RunContinuationsAsynchronously, so
//   its listeners run asynchronously). Java's closePromise.setSuccess() and fireChannelInactiveAndDeregister(...) are
//   AbstractChannel's close (see Http2ChannelUnsafe.close).
// - the registered flag cleared by a deregistration: deregister(promise) is AbstractChannel's deregistration.
// The package-private class is internal.
internal abstract class AbstractHttp2StreamChannel : AbstractChannel, IHttp2StreamChannel
{

    internal static readonly IHttp2FrameStreamVisitor WRITABLE_VISITOR = new WritableVisitor();

    // CLR: the anonymous Http2FrameStreamVisitor of WRITABLE_VISITOR.
    private sealed class WritableVisitor : IHttp2FrameStreamVisitor
    {
        public bool Visit(IHttp2FrameStream stream)
        {
            AbstractHttp2StreamChannel childChannel = (AbstractHttp2StreamChannel)
                    ((Http2FrameCodec.DefaultHttp2FrameStream) stream).attachment;
            childChannel.TrySetWritable();
            return true;
        }
    }

    internal static readonly IHttp2FrameStreamVisitor CHANNEL_INPUT_SHUTDOWN_READ_COMPLETE_VISITOR =
            new UserEventStreamVisitor(ChannelInputShutdownReadComplete.INSTANCE);

    internal static readonly IHttp2FrameStreamVisitor CHANNEL_OUTPUT_SHUTDOWN_EVENT_VISITOR =
            new UserEventStreamVisitor(ChannelOutputShutdownEvent.INSTANCE);

    internal static readonly IHttp2FrameStreamVisitor SSL_CLOSE_COMPLETION_EVENT_VISITOR =
            new UserEventStreamVisitor(SslCloseCompletionEvent.SUCCESS);

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(AbstractHttp2StreamChannel));

    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);

    /**
     * Number of bytes to consider non-payload messages. 9 is arbitrary, but also the minimum size of an HTTP/2 frame.
     * Primarily is non-zero.
     */
    private const int MIN_HTTP2_FRAME_SIZE = 9;

    /**
     * {@link Http2FrameStreamVisitor} that fires the user event for every active stream pipeline.
     */
    private sealed class UserEventStreamVisitor : IHttp2FrameStreamVisitor
    {

        private readonly object @event;

        internal UserEventStreamVisitor(object @event)
        {
            this.@event = CheckNotNull(@event, "event");
        }

        public bool Visit(IHttp2FrameStream stream)
        {
            AbstractHttp2StreamChannel childChannel = (AbstractHttp2StreamChannel)
                    ((Http2FrameCodec.DefaultHttp2FrameStream) stream).attachment;
            childChannel.Pipeline.FireUserEventTriggered(@event);
            return true;
        }
    }

    /**
     * Returns the flow-control size for DATA frames, and {@value MIN_HTTP2_FRAME_SIZE} for all other frames.
     */
    private sealed class FlowControlledFrameSizeEstimator : IMessageSizeEstimator
    {

        internal static readonly FlowControlledFrameSizeEstimator INSTANCE = new FlowControlledFrameSizeEstimator();

        internal static readonly IMessageSizeEstimator.IHandle HANDLE_INSTANCE = new FlowControlledFrameSizeHandle();

        // CLR: the anonymous Handle of HANDLE_INSTANCE.
        private sealed class FlowControlledFrameSizeHandle : IMessageSizeEstimator.IHandle
        {
            public int Size(object msg)
            {
                return msg is IHttp2DataFrame ?
                        // Guard against overflow.
                        (int) Math.Min(int.MaxValue, ((IHttp2DataFrame) msg).InitialFlowControlledBytes() +
                                (long) MIN_HTTP2_FRAME_SIZE) : MIN_HTTP2_FRAME_SIZE;
            }
        }

        public IMessageSizeEstimator.IHandle NewHandle()
        {
            return HANDLE_INSTANCE;
        }
    }

    // CLR: TOTAL_PENDING_SIZE_UPDATER and UNWRITABLE_UPDATER (AtomicLongFieldUpdater/AtomicIntegerFieldUpdater) are
    // Interlocked operations on the totalPendingSize and unwritable fields.

    private static void WindowUpdateFrameWriteComplete(Task future, AbstractHttp2StreamChannel streamChannel)
    {
        Exception cause = Cause(future);
        if (cause != null)
        {
            Exception unwrappedCause;
            // Unwrap if needed
            if (cause is Http2FrameStreamException && (unwrappedCause = cause.InnerException) != null)
            {
                cause = unwrappedCause;
            }

            // Notify the child-channel and close it.
            streamChannel.Pipeline.FireExceptionCaught(cause);
            streamChannel.Unsafe().Close(streamChannel.Unsafe().VoidPromise());
        }
    }

    private readonly Action<Task> windowUpdateFrameWriteListener;

    /**
     * The current status of the read-processing for a {@link AbstractHttp2StreamChannel}.
     */
    private enum ReadStatus
    {
        /**
         * No read in progress and no read was requested (yet)
         */
        IDLE,

        /**
         * Reading in progress
         */
        IN_PROGRESS,

        /**
         * A read operation was requested.
         */
        REQUESTED
    }

    private readonly Http2StreamChannelConfig config;
    private readonly Http2ChannelUnsafe @unsafe;
    private readonly Http2FrameCodec.DefaultHttp2FrameStream stream;

    // CLR: the channelId, pipeline, closePromise and registered fields are AbstractChannel's Id, Pipeline, CloseFuture
    // and IsRegistered (see the type comment).

    private long totalPendingSize;
    private int unwritable;

    // Cached to reduce GC
    private Action fireChannelWritabilityChangedTask;

    private bool outboundClosed;
    private int flowControlledBytes;

    /**
     * This variable represents if a read is in progress for the current channel or was requested.
     * Note that depending upon the {@link RecvByteBufAllocator} behavior a read may extend beyond the
     * {@link Http2ChannelUnsafe#beginRead()} method scope. The {@link Http2ChannelUnsafe#beginRead()} loop may
     * drain all pending data, and then if the parent channel is reading this channel may still accept frames.
     */
    private ReadStatus readStatus = ReadStatus.IDLE;

    private Queue<object> inboundBuffer;

    /** {@code true} after the first HEADERS frame has been written **/
    private bool firstFrameWritten;
    private bool readCompletePending;

    // CLR: AbstractChannel's constructor takes the parent channel and the id, which Java computes from parentContext().
    protected AbstractHttp2StreamChannel(IChannel parent, Http2FrameCodec.DefaultHttp2FrameStream stream, int id,
            IChannelHandler inboundHandler)
        : base(parent, METADATA, new Http2StreamChannelId(parent.Id, id))
    {
        this.stream = stream;
        stream.attachment = this;
        // CLR: new DefaultChannelPipeline(this) { ... } is NewChannelPipeline(); closePromise = pipeline.newPromise() is
        // AbstractChannel's CloseFuture and channelId = new Http2StreamChannelId(parent().id(), id) its Id.
        windowUpdateFrameWriteListener = future => WindowUpdateFrameWriteComplete(future, this);
        config = (Http2StreamChannelConfig) Config;
        @unsafe = new Http2ChannelUnsafe(this);
        // CLR: Java's isActive() is isOpen(); AbstractChannel's flag follows it for RegisterAsync's channelActive.
        SetActive(true);

        if (inboundHandler != null)
        {
            // Add the handler to the pipeline now that we are registered.
            Pipeline.AddLast(inboundHandler);
        }
    }

    protected override IChannelConfig NewConfig()
    {
        return new Http2StreamChannelConfig(this);
    }

    protected override DefaultChannelPipeline NewChannelPipeline()
    {
        return new Http2StreamChannelPipeline(this);
    }

    // CLR: the anonymous DefaultChannelPipeline subclass of the constructor. The protected internal members of the
    // Transport's DefaultChannelPipeline are protected overrides here, as overrides "in a different assembly"
    // are (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal).
    private sealed class Http2StreamChannelPipeline : DefaultChannelPipeline
    {
        private readonly AbstractHttp2StreamChannel channel;

        internal Http2StreamChannelPipeline(AbstractHttp2StreamChannel channel) : base(channel)
        {
            this.channel = channel;
        }

        protected override void IncrementPendingOutboundBytes(long size)
        {
            channel.IncrementPendingOutboundBytes(size, true);
        }

        protected override void DecrementPendingOutboundBytes(long size)
        {
            channel.DecrementPendingOutboundBytes(size, true);
        }

        protected override void OnUnhandledInboundException(Exception cause)
        {
            // Ensure we use the correct Http2Error to close the channel.
            if (cause is Http2FrameStreamException)
            {
                channel.CloseWithError(((Http2FrameStreamException) cause).Error());
                return;
            }
            else
            {
                Http2Exception exception = GetEmbeddedHttp2Exception(cause);
                if (exception != null)
                {
                    channel.CloseWithError(exception.Error());
                    return;
                }
            }
            base.OnUnhandledInboundException(cause);
        }
    }

    private void IncrementPendingOutboundBytes(long size, bool invokeLater)
    {
        if (size == 0)
        {
            return;
        }

        long newWriteBufferSize = Interlocked.Add(ref totalPendingSize, size);
        if (newWriteBufferSize > config.GetWriteBufferHighWaterMark())
        {
            SetUnwritable(invokeLater);
        }
    }

    private void DecrementPendingOutboundBytes(long size, bool invokeLater)
    {
        if (size == 0)
        {
            return;
        }

        long newWriteBufferSize = Interlocked.Add(ref totalPendingSize, -size);
        // Once the totalPendingSize dropped below the low water-mark we can mark the child channel
        // as writable again. Before doing so we also need to ensure the parent channel is writable to
        // prevent excessive buffering in the parent outbound buffer. If the parent is not writable
        // we will mark the child channel as writable once the parent becomes writable by calling
        // trySetWritable() later.
        if (newWriteBufferSize < config.GetWriteBufferLowWaterMark() && Parent.IsWritable)
        {
            SetWritable(invokeLater);
        }
    }

    internal void TrySetWritable()
    {
        // The parent is writable again but the child channel itself may still not be writable.
        // Lets try to set the child channel writable to match the state of the parent channel
        // if (and only if) the totalPendingSize is smaller then the low water-mark.
        // If this is not the case we will try again later once we drop under it.
        if (Volatile.Read(ref totalPendingSize) < config.GetWriteBufferLowWaterMark())
        {
            SetWritable(false);
        }
    }

    private void SetWritable(bool invokeLater)
    {
        for (;;)
        {
            int oldValue = Volatile.Read(ref unwritable);
            int newValue = oldValue & ~1;
            if (Interlocked.CompareExchange(ref unwritable, newValue, oldValue) == oldValue)
            {
                if (oldValue != 0 && newValue == 0)
                {
                    FireChannelWritabilityChanged(invokeLater);
                }
                break;
            }
        }
    }

    private void SetUnwritable(bool invokeLater)
    {
        for (;;)
        {
            int oldValue = Volatile.Read(ref unwritable);
            int newValue = oldValue | 1;
            if (Interlocked.CompareExchange(ref unwritable, newValue, oldValue) == oldValue)
            {
                if (oldValue == 0)
                {
                    FireChannelWritabilityChanged(invokeLater);
                }
                break;
            }
        }
    }

    private void FireChannelWritabilityChanged(bool invokeLater)
    {
        IChannelPipeline pipeline = Pipeline;
        if (invokeLater)
        {
            Action task = fireChannelWritabilityChangedTask;
            if (task == null)
            {
                fireChannelWritabilityChangedTask = task = () => pipeline.FireChannelWritabilityChanged();
            }
            EventLoop.Execute(task);
        }
        else
        {
            pipeline.FireChannelWritabilityChanged();
        }
    }

    public IHttp2FrameStream Stream()
    {
        return stream;
    }

    internal void CloseOutbound()
    {
        outboundClosed = true;
    }

    internal void StreamClosed()
    {
        @unsafe.ReadEOS();
        // Attempt to drain any queued data from the queue and deliver it to the application before closing this
        // channel.
        @unsafe.DoBeginRead();
    }

    public override bool IsOpen => !CloseFuture.IsCompleted;

    public override bool IsActive => IsOpen;

    public override bool IsWritable => Volatile.Read(ref unwritable) == 0;

    public override EndPoint LocalAddress => Parent.LocalAddress;

    public override EndPoint RemoteAddress => Parent.RemoteAddress;

    // CLR: AbstractChannel's abstract localAddress0()/remoteAddress0(); Http2ChannelUnsafe.localAddress() and
    // remoteAddress() return the parent's.
    protected override EndPoint LocalAddress0()
    {
        return Parent.LocalAddress;
    }

    protected override EndPoint RemoteAddress0()
    {
        return Parent.RemoteAddress;
    }

    public override long BytesBeforeUnwritable
    {
        get
        {
            // +1 because writability doesn't change until the threshold is crossed (not equal to).
            long bytes = config.GetWriteBufferHighWaterMark() - Volatile.Read(ref totalPendingSize) + 1;
            // If bytes is negative we know we are not writable, but if bytes is non-negative we have to check
            // writability. Note that totalPendingSize and isWritable() use different volatile variables that are not
            // synchronized together. totalPendingSize will be updated before isWritable().
            return bytes > 0 && IsWritable ? bytes : 0;
        }
    }

    public override long BytesBeforeWritable
    {
        get
        {
            // +1 because writability doesn't change until the threshold is crossed (not equal to).
            long bytes = Volatile.Read(ref totalPendingSize) - config.GetWriteBufferLowWaterMark() + 1;
            // If bytes is negative we know we are writable, but if bytes is non-negative we have to check writability.
            // Note that totalPendingSize and isWritable() use different volatile variables that are not synchronized
            // together. totalPendingSize will be updated before isWritable().
            return bytes <= 0 || IsWritable ? 0 : bytes;
        }
    }

    // CLR: Channel.unsafe() of the folded Unsafe; internal for Http2MultiplexHandler, Http2StreamChannelBootstrap and the
    // tests, which call its closeForcibly()/close(promise) as Java does.
    internal Http2ChannelUnsafe Unsafe()
    {
        return @unsafe;
    }

    public override Task RegisterAsync(IEventLoop eventLoop, CancellationToken cancellationToken = default)
    {
        return @unsafe.Register(eventLoop, cancellationToken);
    }

    private Task RegisterChannel(IEventLoop eventLoop, CancellationToken cancellationToken)
    {
        return base.RegisterAsync(eventLoop, cancellationToken);
    }

    // CLR: the folded Unsafe operations of AbstractChannel, overridden with the members of Http2ChannelUnsafe (see the
    // type comment).
    protected override Task BindTransportAsync(EndPoint address, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = NewPromise();
        @unsafe.Bind(address, promise);
        return promise.Task;
    }

    protected override Task ConnectTransportAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = NewPromise();
        @unsafe.Connect(remote, local, promise);
        return promise.Task;
    }

    protected override Task DisconnectTransportAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = NewPromise();
        @unsafe.Disconnect(promise);
        return promise.Task;
    }

    protected override Task Close()
    {
        TaskCompletionSource promise = NewPromise();
        @unsafe.Close(promise);
        return promise.Task;
    }

    protected override void CloseForcibly()
    {
        @unsafe.CloseForcibly();
    }

    protected override Task DeregisterTransportAsync()
    {
        TaskCompletionSource promise = NewPromise();
        @unsafe.Deregister(promise);
        return promise.Task;
    }

    protected override Task WriteTransportAsync(object msg, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = NewPromise();
        @unsafe.Write(msg, promise, cancellationToken);
        return promise.Task;
    }

    protected override void FlushTransport()
    {
        @unsafe.Flush();
    }

    protected override void BeginRead()
    {
        @unsafe.BeginRead();
    }

    // CLR: AbstractChannel's abstract transport hooks. bind, connect and beginRead are the overrides above, so the
    // first two are never reached and fail like them; close and deregistration reach AbstractChannel's own close and
    // deregistration only through Http2ChannelUnsafe (closePromise and the registered flag), where nothing is left
    // to release.
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override void DoClose()
    {
    }

    protected override void DoBeginRead()
    {
    }

    // CLR: Java's closePromise.setSuccess() followed by fireChannelInactiveAndDeregister(voidPromise(), wasActive):
    // AbstractChannel's close completes CloseFuture and then runs, in one event-loop task (invokeLater),
    // fireChannelInactive() (the channel was active) and, while registered, registered = false and
    // fireChannelUnregistered(), as Java's fireChannelInactiveAndDeregister does. DoClose() is empty.
    private Task CloseChannel()
    {
        return base.Close();
    }

    // CLR: closePromise.setSuccess() for a channel that was never registered, where Java's
    // fireChannelInactiveAndDeregister completes at once: AbstractChannel's closeForcibly completes CloseFuture without
    // firing events (DoClose() is empty).
    private void CompleteClosePromise()
    {
        base.CloseForcibly();
    }

    // CLR: fireChannelInactiveAndDeregister(promise, false) of deregister(promise): AbstractChannel's deregistration runs
    // registered = false and fireChannelUnregistered() in one later event-loop task while registered (see Close).
    private Task DeregisterChannel()
    {
        return base.DeregisterTransportAsync();
    }

    public override string ToString()
    {
        return Parent.ToString() + '/' + ((Http2StreamChannelId) Id).GetSequenceId().ToString(CultureInfo.InvariantCulture) +
                " (H2 - " + stream + ')';
    }

    internal void FireChildExceptionCaught(Exception cause)
    {
        Pipeline.FireExceptionCaught(cause);
    }

    internal void FireChildUserEventTriggered(object evt)
    {
        Pipeline.FireUserEventTriggered(evt);
    }

    /**
     * Receive a read message. This does not notify handlers unless a read is in progress on the
     * channel.
     */
    internal void FireChildRead(IHttp2Frame frame)
    {
        Debug.Assert(EventLoop.InEventLoop());
        if (!IsActive)
        {
            ReferenceCountUtil.Release(frame);
        }
        else if (readStatus != ReadStatus.IDLE && IsInboundBufferEmpty())
        {
            // A read is in progress or has been requested and nothing is queued, so dispatch directly. If frames are
            // still queued (e.g. this frame was delivered re-entrantly while doBeginRead() is running pipeline
            // callbacks after it stopped early) we must queue behind them below to preserve ordering; the read loop
            // that is still on the stack will drain them.
            IRecvByteBufAllocator.IHandle allocHandle = @unsafe.RecvBufAllocHandle();

            @unsafe.DoRead0(frame, allocHandle);
            // We currently don't need to check for readEOS because the parent channel and child channel are limited
            // to the same EventLoop thread. There are a limited number of frame types that may come after EOS is
            // read (unknown, reset) and the trade off is less conditionals for the hot path (headers/data) at the
            // cost of additional readComplete notifications on the rare path.
            if (allocHandle.ContinueReading())
            {
                MaybeAddChannelToReadCompletePendingQueue();
            }
            else
            {
                @unsafe.NotifyReadComplete(allocHandle, true, false);
            }
        }
        else
        {
            if (inboundBuffer == null)
            {
                inboundBuffer = new Queue<object>(4);
            }
            inboundBuffer.Enqueue(frame);
        }
    }

    private bool IsInboundBufferEmpty()
    {
        return inboundBuffer == null || inboundBuffer.Count == 0;
    }

    internal void FireChildReadComplete()
    {
        Debug.Assert(EventLoop.InEventLoop());
        Debug.Assert(readStatus != ReadStatus.IDLE || !readCompletePending);
        @unsafe.NotifyReadComplete(@unsafe.RecvBufAllocHandle(), false, false);
    }

    internal void CloseWithError(Http2Error error)
    {
        Debug.Assert(EventLoop.InEventLoop());
        @unsafe.Close(@unsafe.VoidPromise(), error);
    }

    // CLR: the inner class reaches the channel through the constructor argument; it is internal (not private) because
    // Unsafe() returns it to the package. promise.setUncancellable() is a check of the write's CancellationToken (an
    // operation whose token is cancelled is failed with that cancellation, as the Transport's
    // AbstractChannelHandlerContext rejects it, docs/transport-porting.md); the other promises cannot be cancelled.
    internal sealed class Http2ChannelUnsafe
    {
        private readonly AbstractHttp2StreamChannel channel;
        // CLR: unsafeVoidPromise is VoidPromise(), a new promise nobody observes (docs/transport-porting.md,
        // VoidChannelPromise).
        private IRecvByteBufAllocator.IHandle recvHandle;
        private bool writeDoneAndNoFlush;
        private bool closeInitiated;
        private bool readEOS;

        private bool receivedEndOfStream;
        private bool sentEndOfStream;

        internal Http2ChannelUnsafe(AbstractHttp2StreamChannel channel)
        {
            this.channel = channel;
        }

        internal void Connect(EndPoint remoteAddress,
                            EndPoint localAddress, TaskCompletionSource promise)
        {
            promise.SetException(new NotSupportedException());
        }

        internal IRecvByteBufAllocator.IHandle RecvBufAllocHandle()
        {
            if (recvHandle == null)
            {
                recvHandle = channel.config.GetRecvByteBufAllocator().NewHandle();
                recvHandle.Reset(channel.config);
            }
            return recvHandle;
        }

        // CLR: localAddress() and remoteAddress() are the channel's LocalAddress0()/RemoteAddress0().

        // CLR: register(eventLoop, promise). After the re-registration check, AbstractChannel.RegisterAsync sets the
        // registered flag (registered = true), fires channelRegistered and channelActive, and completes its Task (see the
        // type comment).
        internal Task Register(IEventLoop eventLoop, CancellationToken cancellationToken)
        {
            if (channel.IsRegistered)
            {
                return Task.FromException(new NotSupportedException("Re-register is not supported"));
            }

            return channel.RegisterChannel(eventLoop, cancellationToken);
        }

        internal void Bind(EndPoint localAddress, TaskCompletionSource promise)
        {
            promise.SetException(new NotSupportedException());
        }

        internal void Disconnect(TaskCompletionSource promise)
        {
            Close(promise);
        }

        internal void Close(TaskCompletionSource promise)
        {
            Close(promise, null);
        }

        internal void Close(TaskCompletionSource promise, Http2Error error)
        {
            if (closeInitiated)
            {
                if (channel.CloseFuture.IsCompleted)
                {
                    // Closed already.
                    promise.SetResult();
                }
                else
                { // Only needed if no VoidChannelPromise.
                    // This means close() was called before so we just register a listener and return
                    AddListener(channel.ParentContext(), channel.CloseFuture, future => promise.SetResult());
                }
                return;
            }
            closeInitiated = true;
            // Just set to false as removing from an underlying queue would even be more expensive.
            channel.readCompletePending = false;

            // CLR: wasActive is isActive(), true here (the close was not initiated); AbstractChannel's close fires
            // channelInactive with it below.

            // There is no need to update the local window as once the stream is closed all the pending bytes will be
            // given back to the connection window by the controller itself.

            // Only ever send a reset frame if the connection is still alive and if the stream was created before
            // as otherwise we may send a RST on a stream in an invalid state and cause a connection error.
            if (channel.Parent.IsActive && IsStreamIdValid(channel.stream.Id()))
            {
                // If error is null we know that the close was not triggered by an error and so we should only
                // try to send a RST frame if we didn't signal the end of the stream before.
                if (error == null)
                {
                    if (!readEOS && !(receivedEndOfStream && sentEndOfStream))
                    {
                        IHttp2StreamFrame resetFrame = new DefaultHttp2ResetFrame(Http2Error.CANCEL).Stream(channel.Stream());
                        Write(resetFrame, VoidPromise(), CancellationToken.None);
                        Flush();
                    }
                }
                else
                {
                    // Close was triggered by a stream error, in this case we always want to send a RST frame.
                    IHttp2StreamFrame resetFrame = new DefaultHttp2ResetFrame(error).Stream(channel.Stream());
                    Write(resetFrame, VoidPromise(), CancellationToken.None);
                    Flush();
                }
            }

            if (channel.inboundBuffer != null)
            {
                for (;;)
                {
                    if (!channel.inboundBuffer.TryDequeue(out object msg))
                    {
                        break;
                    }
                    ReferenceCountUtil.Release(msg);
                }
                channel.inboundBuffer = null;
            }

            // The promise should be notified before we call fireChannelInactive().
            channel.outboundClosed = true;
            // CLR: closePromise.setSuccess(), promise.setSuccess() and fireChannelInactiveAndDeregister(voidPromise(),
            // wasActive): for a registered channel AbstractChannel's close completes CloseFuture and queues the
            // deregistration task; for an unregistered one CloseFuture completes and fireChannelInactiveAndDeregister
            // has nothing to do (see CloseChannel and CompleteClosePromise). The promise completes after CloseFuture and
            // before the queued task runs, as in Java.
            if (channel.IsRegistered)
            {
                _ = channel.CloseChannel();
            }
            else
            {
                channel.CompleteClosePromise();
            }
            promise.SetResult();
        }

        internal void CloseForcibly()
        {
            Close(VoidPromise());
        }

        internal void Deregister(TaskCompletionSource promise)
        {
            FireChannelInactiveAndDeregister(promise, false);
        }

        // CLR: only deregister(promise) calls it here (close reaches the same AbstractChannel deregistration with
        // fireChannelInactive set, see Close). The registered flag is AbstractChannel's, so the invokeLater task is
        // AbstractChannel's deregistration, which runs the same steps; safeSetSuccess(promise) completes the promise
        // with its Task, and AbstractChannel logs a task the EventLoop rejects.
        private void FireChannelInactiveAndDeregister(TaskCompletionSource promise,
                                                      bool fireChannelInactive)
        {
            Debug.Assert(!fireChannelInactive);
            if (!channel.IsRegistered)
            {
                promise.SetResult();
                return;
            }

            // As a user may call deregister() from within any method while doing processing in the ChannelPipeline,
            // we need to ensure we do the actual deregister operation later. This is necessary to preserve the
            // behavior of the AbstractChannel, which always invokes channelUnregistered and channelInactive
            // events 'later' to ensure the current events in the handler are completed before these events.
            //
            // See:
            // https://github.com/netty/netty/issues/4435
            // The user can fire `deregister` events multiple times but we only want to fire the pipeline
            // event if the channel was actually registered.
            Task deregistered = channel.DeregisterChannel();
            if (deregistered.IsCompleted)
            {
                SafeSetSuccess(promise);
            }
            else
            {
                AddListener(channel.ParentContext(), deregistered, future => SafeSetSuccess(promise));
            }
        }

        private void SafeSetSuccess(TaskCompletionSource promise)
        {
            if (!promise.TrySetResult())
            {
                logger.Warn("{} Failed to mark a promise as success because it is done already: {}",
                        channel, promise);
            }
        }

        // CLR: invokeLater(channel, task) is AbstractChannel's InvokeLater, which the deregistration above runs. Its
        // original comment:
        // This method is used by outbound operation implementations to trigger an inbound event later.
        // They do not trigger an inbound event immediately because an outbound operation might have been
        // triggered by another inbound event handler method.  If fired immediately, the call stack
        // will look like this for example:
        //
        //   handlerA.inboundBufferUpdated() - (1) an inbound handler method closes a connection.
        //   -> handlerA.ctx.close()
        //     -> channel.unsafe.close()
        //       -> handlerA.channelInactive() - (2) another inbound handler method called while in (1) yet
        //
        // which means the execution of two inbound handler methods of the same handler overlap undesirably.

        internal void BeginRead()
        {
            if (!channel.IsActive)
            {
                return;
            }
            bool windowUpdated = UpdateLocalWindowIfNeeded();

            // CLR: the switch on the enum is an if/else chain; REQUESTED is the default branch.
            if (channel.readStatus == ReadStatus.IDLE)
            {
                channel.readStatus = ReadStatus.IN_PROGRESS;
                // doBeginRead() always flushes (or defers to the parent's read-complete flush).
                DoBeginRead();
                return;
            }
            else if (channel.readStatus == ReadStatus.IN_PROGRESS)
            {
                channel.readStatus = ReadStatus.REQUESTED;
            }
            if (windowUpdated)
            {
                // A WINDOW_UPDATE was just written above, but no read loop runs in this branch to flush it, so an
                // unflushed frame here can stall the remote peer forever (netty/netty#17600).
                //
                // flush() alone is not enough: it no-ops while isParentReadInProgress(), since
                // Http2MultiplexHandler batches all children's flushes into one ctx.flush() at the end of its read
                // loop (processPendingReadCompleteQueue()) -- but that batched flush only runs if some channel
                // registered via maybeAddChannelToReadCompletePendingQueue(), which doBeginRead()/fireChildRead()
                // do but this branch doesn't. If a cross-thread setAutoRead(true) lands here as the only activity
                // in the parent's read loop, the queue stays empty, the end-of-loop flush is skipped, and this
                // WINDOW_UPDATE (flowControlledBytes already zeroed) is never retried -- permanent stall.
                //
                // So register into that queue while the parent is reading; otherwise flush immediately.
                if (channel.IsParentReadInProgress())
                {
                    channel.MaybeAddChannelToReadCompletePendingQueue();
                }
                else
                {
                    Flush();
                }
            }
        }

        private object PollQueuedMessage()
        {
            return channel.inboundBuffer == null ? null :
                    channel.inboundBuffer.TryDequeue(out object message) ? message : null;
        }

        internal void DoBeginRead()
        {
            if (channel.readStatus == ReadStatus.IDLE)
            {
                // Don't wait for the user to request a read to notify of channel closure.
                if (readEOS && channel.IsInboundBufferEmpty())
                {
                    // Double check there is nothing left to flush such as a window update frame.
                    Flush();
                    CloseIfReadEOSAndDrained();
                }
            }
            else
            {
                do
                { // Process messages until there are none left (or the user stopped requesting) and also handle EOS.
                    object message = PollQueuedMessage();
                    if (message == null)
                    {
                        // Double check there is nothing left to flush such as a window update frame.
                        Flush();
                        if (!channel.IsInboundBufferEmpty())
                        {
                            // The flush re-entrantly delivered more frames which got queued; keep reading if we
                            // still should.
                            continue;
                        }
                        CloseIfReadEOSAndDrained();
                        break;
                    }
                    IRecvByteBufAllocator.IHandle allocHandle = RecvBufAllocHandle();
                    allocHandle.Reset(channel.config);
                    bool continueReading = false;
                    do
                    {
                        DoRead0((IHttp2Frame) message, allocHandle);
                    } while ((readEOS || (continueReading = allocHandle.ContinueReading()))
                            && (message = PollQueuedMessage()) != null);

                    if (continueReading && channel.IsParentReadInProgress() && !readEOS)
                    {
                        // Currently the parent and child channel are on the same EventLoop thread. If the parent is
                        // currently reading it is possible that more frames will be delivered to this child channel. In
                        // the case that this child channel still wants to read we delay the channelReadComplete on this
                        // child channel until the parent is done reading.
                        channel.MaybeAddChannelToReadCompletePendingQueue();
                    }
                    else
                    {
                        NotifyReadComplete(allocHandle, true, true);

                        // While in the read loop reset the readState AFTER calling readComplete (or other pipeline
                        // callbacks) to prevents re-entry into this method (if autoRead is disabled and the user calls
                        // read on each readComplete) and StackOverflowException.
                        ResetReadStatus();
                    }
                } while (channel.readStatus != ReadStatus.IDLE);
            }
        }

        internal void ReadEOS()
        {
            readEOS = true;
        }

        private bool UpdateLocalWindowIfNeeded()
        {
            if (channel.flowControlledBytes != 0 && !channel.ParentContext().IsRemoved && channel.config.autoStreamFlowControl)
            {
                int bytes = channel.flowControlledBytes;
                channel.flowControlledBytes = 0;
                WriteWindowUpdateFrame((IHttp2WindowUpdateFrame) new DefaultHttp2WindowUpdateFrame(bytes).Stream(channel.stream));
                return true;
            }
            return false;
        }

        internal void UpdateLocalWindowIfNeededAndFlush()
        {
            if (UpdateLocalWindowIfNeeded())
            {
                Flush();
            }
        }

        private void ResetReadStatus()
        {
            channel.readStatus = channel.readStatus == ReadStatus.REQUESTED ? ReadStatus.IN_PROGRESS : ReadStatus.IDLE;
        }

        internal void NotifyReadComplete(IRecvByteBufAllocator.IHandle allocHandle, bool forceReadComplete,
                                bool inReadLoop)
        {
            if (!channel.readCompletePending && !forceReadComplete)
            {
                return;
            }
            // Set to false just in case we added the channel multiple times before.
            channel.readCompletePending = false;

            if (!inReadLoop)
            {
                // While in the read loop we reset the state after calling pipeline methods to prevent StackOverflow.
                ResetReadStatus();
            }

            allocHandle.ReadComplete();
            channel.Pipeline.FireChannelReadComplete();
            // Reading data may result in frames being written (e.g. WINDOW_UPDATE, RST, etc..). If the parent
            // channel is not currently reading we need to force a flush at the child channel, because we cannot
            // rely upon flush occurring in channelReadComplete on the parent channel.
            Flush();
            CloseIfReadEOSAndDrained();
        }

        private void CloseIfReadEOSAndDrained()
        {
            // Pipeline callbacks and flush() can re-entrantly deliver more frames, including the one that ends the
            // stream, and queue them if the user stopped reading. Only close once all of them have been read,
            // otherwise the queued frames would be released and the stream silently truncated.
            if (readEOS && channel.IsInboundBufferEmpty())
            {
                CloseForcibly();
            }
        }

        internal void DoRead0(IHttp2Frame frame, IRecvByteBufAllocator.IHandle allocHandle)
        {
            int bytes;
            if (frame is IHttp2DataFrame)
            {
                bytes = ((IHttp2DataFrame) frame).InitialFlowControlledBytes();
                // It is important that we increment the flowControlledBytes before we call fireChannelRead(...)
                // as it may cause a read() that will call updateLocalWindowIfNeeded() and we need to ensure
                // in this case that we accounted for it.
                //
                // See https://github.com/netty/netty/issues/9663
                channel.flowControlledBytes += bytes;
            }
            else
            {
                bytes = MIN_HTTP2_FRAME_SIZE;
            }

            // Let's keep track of what we received as the stream state itself will only be updated once the frame
            // was dispatched for reading which might cause problems if we try to close the channel in a write future.
            receivedEndOfStream |= IsEndOfStream(frame);

            // Update before firing event through the pipeline to be consistent with other Channel implementation.
            allocHandle.AttemptedBytesRead(bytes);
            allocHandle.LastBytesRead(bytes);
            allocHandle.IncMessagesRead(1);

            channel.Pipeline.FireChannelRead(frame);
        }

        private Task WriteWindowUpdateFrame(IHttp2WindowUpdateFrame windowUpdateFrame)
        {
            Task future = channel.Write0(channel.ParentContext(), windowUpdateFrame);
            // window update frames are commonly swallowed by the Http2FrameCodec and the promise is synchronously
            // completed but the flow controller _may_ have generated a wire level WINDOW_UPDATE. Therefore we need,
            // to assume there was a write done that needs to be flushed or we risk flow control starvation.
            writeDoneAndNoFlush = true;
            // Add a listener which will notify and teardown the stream
            // when a window update fails if needed or check the result of the future directly if it was completed
            // already.
            // See https://github.com/netty/netty/issues/9663
            if (future.IsCompleted)
            {
                WindowUpdateFrameWriteComplete(future, channel);
            }
            else
            {
                AddListener(channel.ParentContext(), future, channel.windowUpdateFrameWriteListener);
            }
            return future;
        }

        internal void Write(object msg, TaskCompletionSource promise, CancellationToken cancellationToken)
        {
            // After this point its not possible to cancel a write anymore.
            if (cancellationToken.IsCancellationRequested)
            {
                ReferenceCountUtil.Release(msg);
                promise.TrySetCanceled(cancellationToken);
                return;
            }

            if (!channel.IsActive ||
                    // Once the outbound side was closed we should not allow header / data frames
                    channel.outboundClosed && (msg is IHttp2HeadersFrame || msg is IHttp2DataFrame))
            {
                ReferenceCountUtil.Release(msg);
                promise.SetException(new ClosedChannelException());
                return;
            }

            try
            {
                if (msg is IHttp2StreamFrame)
                {
                    IHttp2StreamFrame frame = ValidateStreamFrame((IHttp2StreamFrame) msg).Stream(channel.Stream());
                    if (msg is IHttp2WindowUpdateFrame)
                    {
                        IHttp2WindowUpdateFrame updateFrame = (IHttp2WindowUpdateFrame) msg;
                        if (channel.config.autoStreamFlowControl)
                        {
                            ReferenceCountUtil.Release(msg);
                            promise.SetException(new NotSupportedException(
                                    Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL + " is set to false"));
                            return;
                        }
                        try
                        {
                            CheckInRange(updateFrame.WindowSizeIncrement(), 0,
                                    channel.flowControlledBytes, "windowSizeIncrement");
                        }
                        catch (Exception e)
                        {
                            ReferenceCountUtil.Release(updateFrame);
                            promise.SetException(e);
                            return;
                        }
                        channel.flowControlledBytes -= updateFrame.WindowSizeIncrement();
                        if (channel.ParentContext().IsRemoved)
                        {
                            ReferenceCountUtil.Release(msg);
                            promise.SetException(new ClosedChannelException());
                            return;
                        }
                        Task f = WriteWindowUpdateFrame(updateFrame);
                        if (f.IsCompleted)
                        {
                            WriteComplete(f, promise);
                        }
                        else
                        {
                            AddListener(channel.ParentContext(), f, future => WriteComplete(future, promise));
                        }
                    }
                    else
                    {
                        WriteHttp2StreamFrame(frame, promise);
                    }
                }
                else
                {
                    string msgStr = msg.ToString();
                    ReferenceCountUtil.Release(msg);
                    promise.SetException(new ArgumentException(
                            "Message must be an " + StringUtil.SimpleClassName(typeof(IHttp2StreamFrame)) +
                                    ": " + msgStr));
                }
            }
            catch (Exception t)
            {
                promise.TrySetException(t);
            }
        }

        private bool IsEndOfStream(IHttp2Frame frame)
        {
            if (frame is IHttp2HeadersFrame)
            {
                return ((IHttp2HeadersFrame) frame).IsEndStream();
            }
            if (frame is IHttp2DataFrame)
            {
                return ((IHttp2DataFrame) frame).IsEndStream();
            }
            return false;
        }

        private void WriteHttp2StreamFrame(IHttp2StreamFrame frame, TaskCompletionSource promise)
        {
            if (!channel.firstFrameWritten && !IsStreamIdValid(channel.Stream().Id()) && !(frame is IHttp2HeadersFrame))
            {
                ReferenceCountUtil.Release(frame);
                promise.SetException(
                    new ArgumentException("The first frame must be a headers frame. Was: "
                        + frame.Name()));
                return;
            }

            bool firstWrite;
            if (channel.firstFrameWritten)
            {
                firstWrite = false;
            }
            else
            {
                firstWrite = channel.firstFrameWritten = true;
            }

            // Let's keep track of what we send as the stream state itself will only be updated once the frame
            // was written which might cause problems if we try to close the channel in a write future.
            sentEndOfStream |= IsEndOfStream(frame);
            Task f = channel.Write0(channel.ParentContext(), frame);
            if (f.IsCompleted)
            {
                if (firstWrite)
                {
                    FirstWriteComplete(f, promise);
                }
                else
                {
                    WriteComplete(f, promise);
                }
            }
            else
            {
                long bytes = FlowControlledFrameSizeEstimator.HANDLE_INSTANCE.Size(frame);
                channel.IncrementPendingOutboundBytes(bytes, false);
                AddListener(channel.ParentContext(), f, future =>
                {
                    if (firstWrite)
                    {
                        FirstWriteComplete(future, promise);
                    }
                    else
                    {
                        WriteComplete(future, promise);
                    }
                    channel.DecrementPendingOutboundBytes(bytes, false);
                });
                writeDoneAndNoFlush = true;
            }
        }

        private void FirstWriteComplete(Task future, TaskCompletionSource promise)
        {
            Exception cause = Cause(future);
            if (cause == null)
            {
                promise.SetResult();
            }
            else
            {
                // If the first write fails there is not much we can do, just close
                CloseForcibly();
                promise.SetException(WrapStreamClosedError(cause));
            }
        }

        private void WriteComplete(Task future, TaskCompletionSource promise)
        {
            Exception cause = Cause(future);
            if (cause == null)
            {
                promise.SetResult();
            }
            else
            {
                Exception error = WrapStreamClosedError(cause);
                // To make it more consistent with AbstractChannel we handle all IOExceptions here.
                // CLR: a SocketException counts as an IOException, as in AbstractChannel's write-error handling
                // (src/Netty.NET.Transport/Channels/AbstractChannel.cs: "Java SocketException is an IOException").
                if (error is IOException || error is SocketException)
                {
                    if (channel.config.IsAutoClose())
                    {
                        // Close channel if needed.
                        CloseForcibly();
                    }
                    else
                    {
                        // TODO: Once Http2StreamChannel extends DuplexChannel we should call shutdownOutput(...)
                        channel.outboundClosed = true;
                    }
                }
                promise.SetException(error);
            }
        }

        private Exception WrapStreamClosedError(Exception cause)
        {
            // If the error was caused by STREAM_CLOSED we should use a ClosedChannelException to better
            // mimic other transports and make it easier to reason about what exceptions to expect.
            if (cause is Http2Exception && ((Http2Exception) cause).Error() == Http2Error.STREAM_CLOSED)
            {
                return new StreamClosedChannelException(cause);
            }
            return cause;
        }

        private IHttp2StreamFrame ValidateStreamFrame(IHttp2StreamFrame frame)
        {
            if (frame.Stream() != null && frame.Stream() != channel.stream)
            {
                string msgString = frame.ToString();
                ReferenceCountUtil.Release(frame);
                throw new ArgumentException(
                        "Stream " + frame.Stream() + " must not be set on the frame: " + msgString);
            }
            return frame;
        }

        internal void Flush()
        {
            // If we are currently in the parent channel's read loop we should just ignore the flush.
            // We will ensure we trigger ctx.flush() after we processed all Channels later on and
            // so aggregate the flushes. This is done as ctx.flush() is expensive when as it may trigger an
            // write(...) or writev(...) operation on the socket.
            if (!writeDoneAndNoFlush || channel.IsParentReadInProgress())
            {
                // There is nothing to flush so this is a NOOP.
                return;
            }
            // We need to set this to false before we call flush0(...) as ChannelFutureListener may produce more data
            // that are explicit flushed.
            writeDoneAndNoFlush = false;
            channel.Flush0(channel.ParentContext());
        }

        internal TaskCompletionSource VoidPromise()
        {
            return NewPromise();
        }

        // CLR: outboundBuffer() returns null in Java; AbstractChannel's OutboundBuffer exists but this channel's writes
        // never reach it (see the type comment).
        // Always return null as we not use the ChannelOutboundBuffer and not even support it.
    }

    // CLR: new ClosedChannelException().initCause(cause): Transport's ClosedChannelException takes a cause only through
    // its protected constructor (src/Netty.NET.Transport/Channels/ClosedChannelException.cs).
    private sealed class StreamClosedChannelException : ClosedChannelException
    {
        internal StreamClosedChannelException(Exception cause) : base(cause)
        {
        }
    }

    /**
     * {@link ChannelConfig} so that the high and low writebuffer watermarks can reflect the outbound flow control
     * window, without having to create a new {@link WriteBufferWaterMark} object whenever the flow control window
     * changes.
     */
    // CLR: getMessageSizeEstimator() is not virtual in the Transport's DefaultChannelConfig, so the constructor sets
    // FlowControlledFrameSizeEstimator.INSTANCE once and setMessageSizeEstimator(...) still throws. The option dispatch
    // of the Transport's DefaultChannelConfig is TryGetValue/SetValue (getOption/setOption; SetOption<T> validates the
    // value before it calls SetValue); getOptions(super.getOptions(), option) is a copy of the base options with the
    // option added. RecvByteBufAllocator.ExtendedHandle is IRecvByteBufAllocator.IExtendedHandle and its class text
    // Type.ToString(), the full CLR name.
    private sealed class Http2StreamChannelConfig : DefaultChannelConfig
    {

        internal volatile bool autoStreamFlowControl = true;
        internal Http2StreamChannelConfig(IChannel channel)
            : base(channel)
        {
            base.SetMessageSizeEstimator(FlowControlledFrameSizeEstimator.INSTANCE);
        }

        public override IChannelConfig SetMessageSizeEstimator(IMessageSizeEstimator estimator)
        {
            throw new NotSupportedException();
        }

        public override IChannelConfig SetRecvByteBufAllocator(IRecvByteBufAllocator allocator)
        {
            if (!(allocator.NewHandle() is IRecvByteBufAllocator.IExtendedHandle))
            {
                throw new ArgumentException("allocator.newHandle() must return an object of type: " +
                        typeof(IRecvByteBufAllocator.IExtendedHandle));
            }
            base.SetRecvByteBufAllocator(allocator);
            return this;
        }

        public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
        {
            Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();
            foreach (KeyValuePair<ChannelOption, object> entry in base.GetOptions())
            {
                options[entry.Key] = entry.Value;
            }
            options[Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL] = autoStreamFlowControl;
            return options;
        }

        protected override bool TryGetValue(ChannelOption option, out object value)
        {
            if (option == Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL)
            {
                value = autoStreamFlowControl;
                return true;
            }
            return base.TryGetValue(option, out value);
        }

        protected override bool SetValue(ChannelOption option, object value)
        {
            if (option == Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL)
            {
                bool newValue = (bool) value;
                bool changed = newValue && !autoStreamFlowControl;
                autoStreamFlowControl = (bool) value;
                if (changed)
                {
                    if (Channel.IsRegistered)
                    {
                        Http2ChannelUnsafe @unsafe = ((AbstractHttp2StreamChannel) Channel).@unsafe;
                        if (Channel.EventLoop.InEventLoop())
                        {
                            @unsafe.UpdateLocalWindowIfNeededAndFlush();
                        }
                        else
                        {
                            Channel.EventLoop.Execute(() => @unsafe.UpdateLocalWindowIfNeededAndFlush());
                        }
                    }
                }
                return true;
            }
            return base.SetValue(option, value);
        }
    }

    private void MaybeAddChannelToReadCompletePendingQueue()
    {
        if (!readCompletePending)
        {
            readCompletePending = true;
            AddChannelToReadCompletePendingQueue();
        }
    }

    protected virtual void Flush0(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    protected virtual Task Write0(IChannelHandlerContext ctx, object msg)
    {
        TaskCompletionSource promise = NewPromise();
        Http2CodecUtil.Write(ctx, msg, promise);
        return promise.Task;
    }

    protected abstract bool IsParentReadInProgress();

    protected abstract void AddChannelToReadCompletePendingQueue();

    protected abstract IChannelHandlerContext ParentContext();
}
