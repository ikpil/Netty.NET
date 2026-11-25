/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Default implementation of {@link Http2ConnectionEncoder}.
 */
// CLR: ChannelPromise is TaskCompletionSource and ChannelFuture Task; the Task-returning methods take the Async suffix
// and return the promise's Task (docs/codec-http-porting.md, Shared CLR adaptations). promise.unvoid() returns the
// promise itself (there is no void promise, docs/transport-porting.md), so those assignments are left out.
// promise.setFailure(e)/tryFailure(e) are SetException/TrySetException followed by returning the promise's Task;
// future.cause() is Http2CodecUtil.Cause(future) (null for a pending or successful Task) and future.isSuccess()/isDone()
// are IsCompletedSuccessfully/IsCompleted. future.addListener(...) and ctx.newPromise().addListener(this) are
// Http2CodecUtil.AddListener, a continuation on Common's EventExecutorTaskScheduler over ctx.Executor that keeps
// DefaultPromise's listener timing (docs/codec-http-porting.md, Listener timing). The ChannelFutureListener inner
// classes have an OperationComplete(Task) method. A Java switch on Http2Stream.State is an if/else chain on the State
// instances (a C# case label must be a constant). The non-final Java methods are virtual. The inner (non-static)
// classes receive the enclosing encoder in their constructors. ArrayDeque<OutstandingLocalSettings> is a LinkedList,
// because operationComplete removes an arbitrary element (Queue<T> has no Remove(T)); the remote settings ArrayDeque is
// a Queue. IllegalStateException is InvalidOperationException, IllegalArgumentException ArgumentException and
// UnsupportedOperationException NotSupportedException (docs/codec-base-porting.md); numbers and booleans in messages
// keep their Java text (Http2CodecUtil.ToJavaString). The CoalescingBufferQueue of FlowControlledData is created for
// promise.channel() in Java; a TaskCompletionSource has no channel, so writeData passes ctx.Channel (the channel of the
// context whose promise Java receives).
public class DefaultHttp2ConnectionEncoder : IHttp2ConnectionEncoder, IHttp2SettingsReceivedConsumer
{
    private readonly IHttp2FrameWriter frameWriter;
    private readonly IHttp2Connection connection;
    private IHttp2LifecycleManager lifecycleManager;
    // We prefer ArrayDeque to LinkedList because later will produce more GC.
    // This initial capacity is plenty for SETTINGS traffic.
    private readonly LinkedList<OutstandingLocalSettings> outstandingLocalSettingsQueue =
            new LinkedList<OutstandingLocalSettings>();
    private Queue<Http2Settings> outstandingRemoteSettingsQueue;

    public DefaultHttp2ConnectionEncoder(IHttp2Connection connection, IHttp2FrameWriter frameWriter)
    {
        this.connection = CheckNotNull(connection, "connection");
        this.frameWriter = CheckNotNull(frameWriter, "frameWriter");
        if (connection.Remote().FlowController() == null)
        {
            connection.Remote().FlowController(new DefaultHttp2RemoteFlowController(connection));
        }
    }

    public virtual void LifecycleManager(IHttp2LifecycleManager lifecycleManager)
    {
        this.lifecycleManager = CheckNotNull(lifecycleManager, "lifecycleManager");
    }

    public virtual IHttp2FrameWriter FrameWriter()
    {
        return frameWriter;
    }

    public virtual IHttp2Connection Connection()
    {
        return connection;
    }

    public IHttp2RemoteFlowController FlowController()
    {
        return Connection().Remote().FlowController();
    }

    public virtual void RemoteSettings(Http2Settings settings)
    {
        bool? pushEnabled = settings.PushEnabled();
        IHttp2FrameWriter.IConfiguration config = Configuration();
        IHttp2HeadersEncoder.IConfiguration outboundHeaderConfig = config.HeadersConfiguration();
        IHttp2FrameSizePolicy outboundFrameSizePolicy = config.FrameSizePolicy();
        if (pushEnabled != null)
        {
            if (!connection.IsServer() && pushEnabled.Value)
            {
                throw ConnectionError(PROTOCOL_ERROR,
                    "Client received a value of ENABLE_PUSH specified to other than 0");
            }
            connection.Remote().AllowPushTo(pushEnabled.Value);
        }

        long? maxConcurrentStreams = settings.MaxConcurrentStreams();
        if (maxConcurrentStreams != null)
        {
            connection.Local().MaxActiveStreams((int) Math.Min(maxConcurrentStreams.Value, int.MaxValue));
        }

        long? headerTableSize = settings.HeaderTableSize();
        if (headerTableSize != null)
        {
            outboundHeaderConfig.MaxHeaderTableSize(headerTableSize.Value);
        }

        long? maxHeaderListSize = settings.MaxHeaderListSize();
        if (maxHeaderListSize != null && !connection.IsServer())
        {
            // Servers ignore the MAX_HEADER_LIST_SIZE setting from clients.
            // It's advisory in spec (RFC 9113 §6.5.2) and best praxis is to ignore it.
            outboundHeaderConfig.MaxHeaderListSize(maxHeaderListSize.Value);
        }

        int? maxFrameSize = settings.MaxFrameSize();
        if (maxFrameSize != null)
        {
            outboundFrameSizePolicy.MaxFrameSize(maxFrameSize.Value);
        }

        int? initialWindowSize = settings.InitialWindowSize();
        if (initialWindowSize != null)
        {
            FlowController().InitialWindowSize(initialWindowSize.Value);
        }
    }

    public virtual Task WriteDataAsync(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
            bool endOfStream, TaskCompletionSource promise)
    {
        IHttp2Stream stream;
        try
        {
            stream = RequireStream(streamId);

            // Verify that the stream is in the appropriate state for sending DATA frames.
            IHttp2Stream.State state = stream.State_();
            if (state == IHttp2Stream.State.OPEN || state == IHttp2Stream.State.HALF_CLOSED_REMOTE)
            {
                // Allowed sending DATA frames in these states.
            }
            else
            {
                throw new InvalidOperationException("Stream " + ToJavaString(stream.Id()) + " in unexpected state " +
                                                    stream.State_());
            }
        }
        catch (Exception e)
        {
            data.Release();
            promise.SetException(e);
            return promise.Task;
        }

        // Hand control of the frame to the flow controller.
        FlowController().AddFlowControlled(stream,
                new FlowControlledData(this, stream, data, padding, endOfStream, promise, ctx.Channel));
        return promise.Task;
    }

    public virtual Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
            bool endStream, TaskCompletionSource promise)
    {
        return WriteHeaders0(ctx, streamId, headers, false, 0, (short) 0, false, padding, endStream, promise);
    }

    private static bool ValidateHeadersSentState(IHttp2Stream stream, IHttp2Headers headers, bool isServer,
                                                    bool endOfStream)
    {
        bool isInformational = isServer && HttpStatusClass.ValueOf(headers.Status()) == HttpStatusClass.INFORMATIONAL;
        if ((isInformational || !endOfStream) && stream.IsHeadersSent() || stream.IsTrailersSent())
        {
            throw new InvalidOperationException("Stream " + ToJavaString(stream.Id()) + " sent too many headers EOS: " +
                                                ToJavaString(endOfStream));
        }
        return isInformational;
    }

    public virtual Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId,
            IHttp2Headers headers, int streamDependency, short weight,
            bool exclusive, int padding, bool endOfStream, TaskCompletionSource promise)
    {
        return WriteHeaders0(ctx, streamId, headers, true, streamDependency,
                weight, exclusive, padding, endOfStream, promise);
    }

    /**
     * Write headers via {@link Http2FrameWriter}. If {@code hasPriority} is {@code false} it will ignore the
     * {@code streamDependency}, {@code weight} and {@code exclusive} parameters.
     */
    private static Task SendHeaders(IHttp2FrameWriter frameWriter, IChannelHandlerContext ctx, int streamId,
                                       IHttp2Headers headers, bool hasPriority,
                                       int streamDependency, short weight,
                                       bool exclusive, int padding,
                                       bool endOfStream, TaskCompletionSource promise)
    {
        if (hasPriority)
        {
            return frameWriter.WriteHeadersAsync(ctx, streamId, headers, streamDependency,
                    weight, exclusive, padding, endOfStream, promise);
        }
        return frameWriter.WriteHeadersAsync(ctx, streamId, headers, padding, endOfStream, promise);
    }

    private Task WriteHeaders0(IChannelHandlerContext ctx, int streamId,
                                        IHttp2Headers headers, bool hasPriority,
                                        int streamDependency, short weight,
                                        bool exclusive, int padding,
                                        bool endOfStream, TaskCompletionSource promise)
    {
        try
        {
            IHttp2Stream stream = connection.Stream(streamId);
            if (stream == null)
            {
                try
                {
                    // We don't create the stream in a `halfClosed` state because if this is an initial
                    // HEADERS frame we don't want the connection state to signify that the HEADERS have
                    // been sent until after they have been encoded and placed in the outbound buffer.
                    // Therefore, we let the `LifeCycleManager` will take care of transitioning the state
                    // as appropriate.
                    stream = connection.Local().CreateStream(streamId, /*endOfStream*/ false);
                }
                catch (Http2Exception cause)
                {
                    if (connection.Remote().MayHaveCreatedStream(streamId))
                    {
                        promise.TrySetException(new InvalidOperationException("Stream no longer exists: " +
                                                                              ToJavaString(streamId), cause));
                        return promise.Task;
                    }
                    throw;
                }
            }
            else
            {
                IHttp2Stream.State state = stream.State_();
                if (state == IHttp2Stream.State.RESERVED_LOCAL)
                {
                    stream.Open(endOfStream);
                }
                else if (state == IHttp2Stream.State.OPEN || state == IHttp2Stream.State.HALF_CLOSED_REMOTE)
                {
                    // Allowed sending headers in these states.
                }
                else
                {
                    throw new InvalidOperationException("Stream " + ToJavaString(stream.Id()) + " in unexpected state " +
                                                        stream.State_());
                }
            }

            // Trailing headers must go through flow control if there are other frames queued in flow control
            // for this stream.
            IHttp2RemoteFlowController flowController = FlowController();
            if (!endOfStream || !flowController.HasFlowControlled(stream))
            {
                // The behavior here should mirror that in FlowControlledHeaders

                bool isInformational = ValidateHeadersSentState(stream, headers, connection.IsServer(), endOfStream);

                Task future = SendHeaders(frameWriter, ctx, streamId, headers, hasPriority, streamDependency,
                        weight, exclusive, padding, endOfStream, promise);

                // Writing headers may fail during the encode state if they violate HPACK limits.
                Exception failureCause = Cause(future);
                if (failureCause == null)
                {
                    // Synchronously set the headersSent flag to ensure that we do not subsequently write
                    // other headers containing pseudo-header fields.
                    //
                    // This just sets internal stream state which is used elsewhere in the codec and doesn't
                    // necessarily mean the write will complete successfully.
                    stream.HeadersSent(isInformational);

                    if (!future.IsCompletedSuccessfully)
                    {
                        // Either the future is not done or failed in the meantime.
                        NotifyLifecycleManagerOnError(future, ctx);
                    }
                }
                else
                {
                    lifecycleManager.OnError(ctx, true, failureCause);
                }

                if (endOfStream)
                {
                    // Must handle calling onError before calling closeStreamLocal, otherwise the error handler will
                    // incorrectly think the stream no longer exists and so may not send RST_STREAM or perform similar
                    // appropriate action.
                    lifecycleManager.CloseStreamLocal(stream, future);
                }

                return future;
            }
            else
            {
                // Pass headers to the flow-controller so it can maintain their sequence relative to DATA frames.
                flowController.AddFlowControlled(stream,
                        new FlowControlledHeaders(this, stream, headers, hasPriority, streamDependency,
                                weight, exclusive, padding, true, promise));
                return promise.Task;
            }
        }
        catch (Exception t)
        {
            lifecycleManager.OnError(ctx, true, t);
            promise.TrySetException(t);
            return promise.Task;
        }
    }

    public virtual Task WritePriorityAsync(IChannelHandlerContext ctx, int streamId, int streamDependency, short weight,
            bool exclusive, TaskCompletionSource promise)
    {
        return frameWriter.WritePriorityAsync(ctx, streamId, streamDependency, weight, exclusive, promise);
    }

    public virtual Task WriteRstStreamAsync(IChannelHandlerContext ctx, int streamId, long errorCode,
            TaskCompletionSource promise)
    {
        // Delegate to the lifecycle manager for proper updating of connection state.
        return lifecycleManager.ResetStreamAsync(ctx, streamId, errorCode, promise);
    }

    public virtual Task WriteSettingsAsync(IChannelHandlerContext ctx, Http2Settings settings,
            TaskCompletionSource promise)
    {
        try
        {
            bool? pushEnabled = settings.PushEnabled();
            if (pushEnabled != null && connection.IsServer())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Server sending SETTINGS frame with ENABLE_PUSH specified");
            }
        }
        catch (Exception e)
        {
            promise.SetException(e);
            return promise.Task;
        }

        OutstandingLocalSettings outstandingLocalSettings = new OutstandingLocalSettings(this, settings);
        outstandingLocalSettingsQueue.AddLast(outstandingLocalSettings);
        TaskCompletionSource writePromise = promise;
        Task future = frameWriter.WriteSettingsAsync(ctx, settings, writePromise);
        if (future.IsCompleted)
        {
            outstandingLocalSettings.OperationComplete(future);
        }
        else
        {
            AddListener(ctx, future, outstandingLocalSettings.OperationComplete);
        }
        return future;
    }

    public virtual Task WriteSettingsAckAsync(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        if (outstandingRemoteSettingsQueue == null)
        {
            return frameWriter.WriteSettingsAckAsync(ctx, promise);
        }
        if (!outstandingRemoteSettingsQueue.TryDequeue(out Http2Settings settings))
        {
            promise.SetException(new Http2Exception(INTERNAL_ERROR, "attempted to write a SETTINGS ACK with no " +
                    " pending SETTINGS"));
            return promise.Task;
        }
        SimpleChannelPromiseAggregator aggregator = new SimpleChannelPromiseAggregator(promise, ctx.Channel,
                ctx.Executor);
        // Acknowledge receipt of the settings. We should do this before we process the settings to ensure our
        // remote peer applies these settings before any subsequent frames that we may send which depend upon
        // these new settings. See https://github.com/netty/netty/issues/6520.
        frameWriter.WriteSettingsAckAsync(ctx, aggregator.NewPromise());

        // We create a "new promise" to make sure that status from both the write and the application are taken into
        // account independently.
        TaskCompletionSource applySettingsPromise = aggregator.NewPromise();
        try
        {
            RemoteSettings(settings);
            applySettingsPromise.SetResult();
        }
        catch (Exception e)
        {
            applySettingsPromise.SetException(e);
            lifecycleManager.OnError(ctx, true, e);
        }
        return aggregator.DoneAllocatingPromises().Task;
    }

    public virtual Task WritePingAsync(IChannelHandlerContext ctx, bool ack, long data, TaskCompletionSource promise)
    {
        return frameWriter.WritePingAsync(ctx, ack, data, promise);
    }

    public virtual Task WritePushPromiseAsync(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
            IHttp2Headers headers, int padding, TaskCompletionSource promise)
    {
        try
        {
            if (connection.GoAwayReceived())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Sending PUSH_PROMISE after GO_AWAY received.");
            }

            IHttp2Stream stream = RequireStream(streamId);
            // Reserve the promised stream.
            connection.Local().ReservePushStream(promisedStreamId, stream);

            Task future = frameWriter.WritePushPromiseAsync(ctx, streamId, promisedStreamId, headers, padding,
                                                                promise);
            // Writing headers may fail during the encode state if they violate HPACK limits.
            Exception failureCause = Cause(future);
            if (failureCause == null)
            {
                // This just sets internal stream state which is used elsewhere in the codec and doesn't
                // necessarily mean the write will complete successfully.
                stream.PushPromiseSent();

                if (!future.IsCompletedSuccessfully)
                {
                    // Either the future is not done or failed in the meantime.
                    NotifyLifecycleManagerOnError(future, ctx);
                }
            }
            else
            {
                lifecycleManager.OnError(ctx, true, failureCause);
            }
            return future;
        }
        catch (Exception t)
        {
            lifecycleManager.OnError(ctx, true, t);
            promise.TrySetException(t);
            return promise.Task;
        }
    }

    public virtual Task WriteGoAwayAsync(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData,
            TaskCompletionSource promise)
    {
        return lifecycleManager.GoAwayAsync(ctx, lastStreamId, errorCode, debugData, promise);
    }

    public virtual Task WriteWindowUpdateAsync(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement,
            TaskCompletionSource promise)
    {
        promise.SetException(new NotSupportedException("Use the Http2[Inbound|Outbound]FlowController" +
                " objects to control window sizes"));
        return promise.Task;
    }

    public virtual Task WriteFrameAsync(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
            ByteBuf payload, TaskCompletionSource promise)
    {
        return frameWriter.WriteFrameAsync(ctx, frameType, streamId, flags, payload, promise);
    }

    public virtual void Close()
    {
        frameWriter.Close();
    }

    public virtual Http2Settings PollSentSettings()
    {
        // CLR: Queue.poll() on the LinkedList: remove and return the first element, or null when empty.
        OutstandingLocalSettings outstandingLocalSettings = outstandingLocalSettingsQueue.First?.Value;
        if (outstandingLocalSettings != null)
        {
            outstandingLocalSettingsQueue.RemoveFirst();
        }
        return outstandingLocalSettings == null ? null : outstandingLocalSettings.settings;
    }

    public virtual IHttp2FrameWriter.IConfiguration Configuration()
    {
        return frameWriter.Configuration();
    }

    private IHttp2Stream RequireStream(int streamId)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        if (stream == null)
        {
            string message;
            if (connection.StreamMayHaveExisted(streamId))
            {
                message = "Stream no longer exists: " + ToJavaString(streamId);
            }
            else
            {
                message = "Stream does not exist: " + ToJavaString(streamId);
            }
            throw new ArgumentException(message);
        }
        return stream;
    }

    private sealed class OutstandingLocalSettings
    {
        private readonly DefaultHttp2ConnectionEncoder encoder;
        internal readonly Http2Settings settings;

        internal OutstandingLocalSettings(DefaultHttp2ConnectionEncoder encoder, Http2Settings settings)
        {
            this.encoder = encoder;
            this.settings = settings;
        }

        public void OperationComplete(Task future)
        {
            if (!future.IsCompletedSuccessfully)
            {
                encoder.outstandingLocalSettingsQueue.Remove(this);
            }
        }
    }

    public virtual void ConsumeReceivedSettings(Http2Settings settings)
    {
        if (outstandingRemoteSettingsQueue == null)
        {
            outstandingRemoteSettingsQueue = new Queue<Http2Settings>(2);
        }
        outstandingRemoteSettingsQueue.Enqueue(settings);
    }

    /**
     * Wrap a DATA frame so it can be written subject to flow-control. Note that this implementation assumes it
     * only writes padding once for the entire payload as opposed to writing it once per-frame. This makes the
     * {@link #size} calculation deterministic thereby greatly simplifying the implementation.
     * <p>
     * If frame-splitting is required to fit within max-frame-size and flow-control constraints we ensure that
     * the passed promise is not completed until last frame write.
     * </p>
     */
    private sealed class FlowControlledData : FlowControlledBase
    {
        private readonly CoalescingBufferQueue queue;
        private int dataSize;

        internal FlowControlledData(DefaultHttp2ConnectionEncoder encoder, IHttp2Stream stream, ByteBuf buf,
                                   int padding, bool endOfStream, TaskCompletionSource promise, IChannel channel)
            : base(encoder, stream, padding, endOfStream, promise)
        {
            queue = new CoalescingBufferQueue(channel);
            queue.Add(buf, promise);
            dataSize = queue.ReadableBytes;
        }

        public override int Size()
        {
            return dataSize + padding;
        }

        public override void Error(IChannelHandlerContext ctx, Exception cause)
        {
            queue.ReleaseAndFailAll(cause);
            // Don't update dataSize because we need to ensure the size() method returns a consistent size even after
            // error so we don't invalidate flow control when returning bytes to flow control.
            //
            // That said we will set dataSize and padding to 0 in the write(...) method if we cleared the queue
            // because of an error.
            encoder.lifecycleManager.OnError(ctx, true, cause);
        }

        public override void Write(IChannelHandlerContext ctx, int allowedBytes)
        {
            int queuedData = queue.ReadableBytes;
            if (!endOfStream)
            {
                if (queuedData == 0)
                {
                    if (queue.IsEmpty)
                    {
                        // When the queue is empty it means we did clear it because of an error(...) call
                        // (as otherwise we will have at least 1 entry in there), which will happen either when called
                        // explicit or when the write itself fails. In this case just set dataSize and padding to 0
                        // which will signal back that the whole frame was consumed.
                        //
                        // See https://github.com/netty/netty/issues/8707.
                        padding = dataSize = 0;
                    }
                    else
                    {
                        // There's no need to write any data frames because there are only empty data frames in the
                        // queue and it is not end of stream yet. Just complete their promises by getting the buffer
                        // corresponding to 0 bytes and writing it to the channel (to preserve notification order).
                        // CLR: named emptyWritePromise; a nested local cannot reuse the name writePromise of the
                        // enclosing block (CS0136).
                        TaskCompletionSource emptyWritePromise = NewPromise();
                        AddListener(ctx, emptyWritePromise.Task, OperationComplete);
                        Http2CodecUtil.Write(ctx, queue.Remove(0, emptyWritePromise), emptyWritePromise);
                    }
                    return;
                }

                if (allowedBytes == 0)
                {
                    return;
                }
            }

            // Determine how much data to write.
            int writableData = Math.Min(queuedData, allowedBytes);
            TaskCompletionSource writePromise = NewPromise();
            AddListener(ctx, writePromise.Task, OperationComplete);
            ByteBuf toWrite = queue.Remove(writableData, writePromise);
            dataSize = queue.ReadableBytes;

            // The queue reported writableData readable bytes but produced fewer: a queued buffer was released or
            // consumed while still referenced by the queue, so the stream's data is corrupted. Fail the stream.
            int producedBytes = toWrite.ReadableBytes;
            if (producedBytes < writableData)
            {
                ReferenceCountUtil.SafeRelease(toWrite);
                // Set dataSize and padding to 0 to signal that the whole frame was consumed, so it is removed and
                // its bytes are returned to flow control (matching the error path above).
                padding = dataSize = 0;
                writePromise.TrySetException(StreamError(stream.Id(), INTERNAL_ERROR,
                        "Stream %d flow-controlled queue produced %d bytes but reported %d",
                        stream.Id(), producedBytes, writableData));
                return;
            }

            // Determine how much padding to write.
            int writablePadding = Math.Min(allowedBytes - writableData, padding);
            padding -= writablePadding;

            // Write the frame(s).
            encoder.FrameWriter().WriteDataAsync(ctx, stream.Id(), toWrite, writablePadding,
                    endOfStream && Size() == 0, writePromise);
        }

        public override bool Merge(IChannelHandlerContext ctx, IHttp2RemoteFlowController.IFlowControlled next)
        {
            FlowControlledData nextData;
            if (typeof(FlowControlledData) != next.GetType() ||
                int.MaxValue - (nextData = (FlowControlledData) next).Size() < Size())
            {
                return false;
            }
            nextData.queue.CopyTo(queue);
            dataSize = queue.ReadableBytes;
            // Given that we're merging data into a frame it doesn't really make sense to accumulate padding.
            padding = Math.Max(padding, nextData.padding);
            endOfStream = nextData.endOfStream;
            return true;
        }
    }

    private void NotifyLifecycleManagerOnError(Task future, IChannelHandlerContext ctx)
    {
        AddListener(ctx, future, future1 =>
        {
            Exception cause = Cause(future1);
            if (cause != null)
            {
                lifecycleManager.OnError(ctx, true, cause);
            }
        });
    }

    /**
     * Wrap headers so they can be written subject to flow-control. While headers do not have cost against the
     * flow-control window their order with respect to other frames must be maintained, hence if a DATA frame is
     * blocked on flow-control a HEADER frame must wait until this frame has been written.
     */
    private sealed class FlowControlledHeaders : FlowControlledBase
    {
        private readonly IHttp2Headers headers;
        private readonly bool hasPriority;
        private readonly int streamDependency;
        private readonly short weight;
        private readonly bool exclusive;

        internal FlowControlledHeaders(DefaultHttp2ConnectionEncoder encoder, IHttp2Stream stream,
                              IHttp2Headers headers, bool hasPriority,
                              int streamDependency, short weight, bool exclusive,
                              int padding, bool endOfStream, TaskCompletionSource promise)
            : base(encoder, stream, padding, endOfStream, promise)
        {
            this.headers = headers;
            this.hasPriority = hasPriority;
            this.streamDependency = streamDependency;
            this.weight = weight;
            this.exclusive = exclusive;
        }

        public override int Size()
        {
            return 0;
        }

        public override void Error(IChannelHandlerContext ctx, Exception cause)
        {
            if (ctx != null)
            {
                encoder.lifecycleManager.OnError(ctx, true, cause);
            }
            promise.TrySetException(cause);
        }

        public override void Write(IChannelHandlerContext ctx, int allowedBytes)
        {
            bool isInformational = ValidateHeadersSentState(stream, headers, encoder.connection.IsServer(), endOfStream);
            // The code is currently requiring adding this listener before writing, in order to call onError() before
            // closeStreamLocal().
            AddListener(ctx, promise.Task, OperationComplete);

            Task f = SendHeaders(encoder.frameWriter, ctx, stream.Id(), headers, hasPriority, streamDependency,
                    weight, exclusive, padding, endOfStream, promise);
            // Writing headers may fail during the encode state if they violate HPACK limits.
            Exception failureCause = Cause(f);
            if (failureCause == null)
            {
                // This just sets internal stream state which is used elsewhere in the codec and doesn't
                // necessarily mean the write will complete successfully.
                stream.HeadersSent(isInformational);
            }
        }

        public override bool Merge(IChannelHandlerContext ctx, IHttp2RemoteFlowController.IFlowControlled next)
        {
            return false;
        }
    }

    /**
     * Common base type for payloads to deliver via flow-control.
     */
    // CLR: the public abstract inner class keeps its public shape with an internal constructor (the Java constructor is
    // package-private) that also takes the enclosing encoder. The FlowControlled members that Java leaves to the
    // subclasses are declared abstract, because a C# class must map every interface member. ChannelFutureListener's
    // operationComplete(ChannelFuture) is OperationComplete(Task).
    public abstract class FlowControlledBase : IHttp2RemoteFlowController.IFlowControlled
    {
        // CLR: the enclosing instance of the Java inner class.
        private protected readonly DefaultHttp2ConnectionEncoder encoder;
        protected readonly IHttp2Stream stream;
        protected TaskCompletionSource promise;
        protected bool endOfStream;
        protected int padding;

        internal FlowControlledBase(DefaultHttp2ConnectionEncoder encoder, IHttp2Stream stream, int padding,
                bool endOfStream, TaskCompletionSource promise)
        {
            CheckPositiveOrZero(padding, "padding");
            this.encoder = encoder;
            this.padding = padding;
            this.endOfStream = endOfStream;
            this.stream = stream;
            this.promise = promise;
        }

        public abstract int Size();

        public abstract void Error(IChannelHandlerContext ctx, Exception cause);

        public abstract void Write(IChannelHandlerContext ctx, int allowedBytes);

        public abstract bool Merge(IChannelHandlerContext ctx, IHttp2RemoteFlowController.IFlowControlled next);

        public virtual void WriteComplete()
        {
            if (endOfStream)
            {
                encoder.lifecycleManager.CloseStreamLocal(stream, promise.Task);
            }
        }

        public virtual void OperationComplete(Task future)
        {
            if (!future.IsCompletedSuccessfully)
            {
                Error(encoder.FlowController().ChannelHandlerContext(), Cause(future));
            }
        }
    }
}
