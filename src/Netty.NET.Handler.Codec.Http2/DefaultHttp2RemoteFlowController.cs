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
using System.Diagnostics;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Basic implementation of {@link Http2RemoteFlowController}.
 * <p>
 * This class is <strong>NOT</strong> thread safe. The assumption is all methods must be invoked from a single thread.
 * Typically this thread is the event loop thread for the {@link ChannelHandlerContext} managed by this class.
 */
// CLR: the inner classes (FlowState, WritabilityMonitor, ListenerWritabilityMonitor and the anonymous
// Http2ConnectionAdapter and Http2StreamVisitor, here the private nested StreamListener and
// InitialWindowSizeVisitor) receive the controller explicitly, because a C# nested class has no enclosing
// instance. The Deque<FlowControlled> of a FlowState is a LinkedList (peekLast is Last, offer AddLast,
// peek/poll/remove the first node), because Queue<T> has no access to its tail.
// ctx.channel().config().getWriteBufferLowWaterMark(), bytesBeforeUnwritable() and isWritable() are the Transport
// members Config.GetWriteBufferLowWaterMark(), BytesBeforeUnwritable and IsWritable. Throwable is Exception,
// RuntimeException is Exception and IllegalStateException is InvalidOperationException; assert is Debug.Assert.
public class DefaultHttp2RemoteFlowController : IHttp2RemoteFlowController
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(DefaultHttp2RemoteFlowController));
    private const int MIN_WRITABLE_CHUNK = 32 * 1024;
    private readonly IHttp2Connection connection;
    private readonly IHttp2Connection.IPropertyKey stateKey;
    private readonly IStreamByteDistributor streamByteDistributor;
    private readonly FlowState connectionState;
    private int initialWindowSize = DEFAULT_WINDOW_SIZE;
    private WritabilityMonitor monitor;
    private IChannelHandlerContext ctx;

    public DefaultHttp2RemoteFlowController(IHttp2Connection connection)
        : this(connection, (IHttp2RemoteFlowController.IListener) null)
    {
    }

    public DefaultHttp2RemoteFlowController(IHttp2Connection connection,
                                            IStreamByteDistributor streamByteDistributor)
        : this(connection, streamByteDistributor, null)
    {
    }

    public DefaultHttp2RemoteFlowController(IHttp2Connection connection, IHttp2RemoteFlowController.IListener listener)
        : this(connection, new WeightedFairQueueByteDistributor(connection), listener)
    {
    }

    public DefaultHttp2RemoteFlowController(IHttp2Connection connection,
                                            IStreamByteDistributor streamByteDistributor,
                                            IHttp2RemoteFlowController.IListener listener)
    {
        this.connection = CheckNotNull(connection, "connection");
        this.streamByteDistributor = CheckNotNull(streamByteDistributor, "streamWriteDistributor");

        // Add a flow state for the connection.
        stateKey = connection.NewKey();
        connectionState = new FlowState(this, connection.ConnectionStream());
        connection.ConnectionStream().SetProperty(stateKey, connectionState);

        // Monitor may depend upon connectionState, and so initialize after connectionState
        Listener(listener);
        monitor.WindowSize(connectionState, initialWindowSize);

        // Register for notification of new streams.
        connection.AddListener(new StreamListener(this));
    }

    private sealed class StreamListener : Http2ConnectionAdapter
    {
        private readonly DefaultHttp2RemoteFlowController controller;

        internal StreamListener(DefaultHttp2RemoteFlowController controller)
        {
            this.controller = controller;
        }

        public override void OnStreamAdded(IHttp2Stream stream)
        {
            // If the stream state is not open then the stream is not yet eligible for flow controlled frames and
            // only requires the ReducedFlowState. Otherwise the full amount of memory is required.
            stream.SetProperty(controller.stateKey, new FlowState(controller, stream));
        }

        public override void OnStreamActive(IHttp2Stream stream)
        {
            // If the object was previously created, but later activated then we have to ensure the proper
            // initialWindowSize is used.
            controller.monitor.WindowSize(controller.State(stream), controller.initialWindowSize);
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            // Any pending frames can never be written, cancel and
            // write errors for any pending frames.
            controller.State(stream).Cancel(STREAM_CLOSED, null);
        }

        public override void OnStreamHalfClosed(IHttp2Stream stream)
        {
            if (IHttp2Stream.State.HALF_CLOSED_LOCAL == stream.State_())
            {
                /*
                 * When this method is called there should not be any
                 * pending frames left if the API is used correctly. However,
                 * it is possible that a erroneous application can sneak
                 * in a frame even after having already written a frame with the
                 * END_STREAM flag set, as the stream state might not transition
                 * immediately to HALF_CLOSED_LOCAL / CLOSED due to flow control
                 * delaying the write.
                 *
                 * This is to cancel any such illegal writes.
                 */
                controller.State(stream).Cancel(STREAM_CLOSED, null);
            }
        }
    }

    /**
     * {@inheritDoc}
     * <p>
     * Any queued {@link FlowControlled} objects will be sent.
     */
    public virtual void ChannelHandlerContext(IChannelHandlerContext ctx)
    {
        this.ctx = CheckNotNull(ctx, "ctx");

        // Writing the pending bytes will not check writability change and instead a writability change notification
        // to be provided by an explicit call.
        ChannelWritabilityChanged();

        // Don't worry about cleaning up queued frames here if ctx is null. It is expected that all streams will be
        // closed and the queue cleanup will occur when the stream state transitions occur.

        // If any frames have been queued up, we should send them now that we have a channel context.
        if (IsChannelWritable())
        {
            WritePendingBytes();
        }
    }

    public virtual IChannelHandlerContext ChannelHandlerContext()
    {
        return ctx;
    }

    public virtual void InitialWindowSize(int newWindowSize)
    {
        Debug.Assert(ctx == null || ctx.Executor.InEventLoop());
        monitor.InitialWindowSize(newWindowSize);
    }

    public virtual int InitialWindowSize()
    {
        return initialWindowSize;
    }

    public virtual int WindowSize(IHttp2Stream stream)
    {
        return State(stream).WindowSize();
    }

    public virtual bool IsWritable(IHttp2Stream stream)
    {
        return monitor.IsWritable(State(stream));
    }

    public virtual void ChannelWritabilityChanged()
    {
        monitor.ChannelWritabilityChange();
    }

    public virtual void UpdateDependencyTree(int childStreamId, int parentStreamId, short weight, bool exclusive)
    {
        // It is assumed there are all validated at a higher level. For example in the Http2FrameReader.
        Debug.Assert(weight >= MIN_WEIGHT && weight <= MAX_WEIGHT, "Invalid weight");
        Debug.Assert(childStreamId != parentStreamId, "A stream cannot depend on itself");
        Debug.Assert(childStreamId > 0 && parentStreamId >= 0, "childStreamId must be > 0. parentStreamId must be >= 0.");

        streamByteDistributor.UpdateDependencyTree(childStreamId, parentStreamId, weight, exclusive);
    }

    private bool IsChannelWritable()
    {
        return ctx != null && IsChannelWritable0();
    }

    private bool IsChannelWritable0()
    {
        return ctx.Channel.IsWritable;
    }

    public virtual void Listener(IHttp2RemoteFlowController.IListener listener)
    {
        monitor = listener == null ? new WritabilityMonitor(this) : new ListenerWritabilityMonitor(this, listener);
    }

    public virtual void IncrementWindowSize(IHttp2Stream stream, int delta)
    {
        Debug.Assert(ctx == null || ctx.Executor.InEventLoop());
        monitor.IncrementWindowSize(State(stream), delta);
    }

    public virtual void AddFlowControlled(IHttp2Stream stream, IHttp2RemoteFlowController.IFlowControlled frame)
    {
        // The context can be null assuming the frame will be queued and send later when the context is set.
        Debug.Assert(ctx == null || ctx.Executor.InEventLoop());
        CheckNotNull(frame, "frame");

        try
        {
            monitor.EnqueueFrame(State(stream), frame);
        }
        catch (Exception t)
        {
            frame.Error(ctx, t);
        }
    }

    public virtual bool HasFlowControlled(IHttp2Stream stream)
    {
        return State(stream).HasFrame();
    }

    private FlowState State(IHttp2Stream stream)
    {
        return stream.GetProperty<FlowState>(stateKey);
    }

    /**
     * Returns the flow control window for the entire connection.
     */
    private int ConnectionWindowSize()
    {
        return connectionState.WindowSize();
    }

    private int MinUsableChannelBytes()
    {
        // The current allocation algorithm values "fairness" and doesn't give any consideration to "goodput". It
        // is possible that 1 byte will be allocated to many streams. In an effort to try to make "goodput"
        // reasonable with the current allocation algorithm we have this "cheap" check up front to ensure there is
        // an "adequate" amount of connection window before allocation is attempted. This is not foolproof as if the
        // number of streams is >= this minimal number then we may still have the issue, but the idea is to narrow the
        // circumstances in which this can happen without rewriting the allocation algorithm.
        return Math.Max(ctx.Channel.Config.GetWriteBufferLowWaterMark(), MIN_WRITABLE_CHUNK);
    }

    private int MaxUsableChannelBytes()
    {
        // If the channel isWritable, allow at least minUsableChannelBytes.
        int channelWritableBytes = (int) Math.Min(int.MaxValue, ctx.Channel.BytesBeforeUnwritable);
        int usableBytes = channelWritableBytes > 0 ? Math.Max(channelWritableBytes, MinUsableChannelBytes()) : 0;

        // Clip the usable bytes by the connection window.
        return Math.Min(connectionState.WindowSize(), usableBytes);
    }

    /**
     * The amount of bytes that can be supported by underlying {@link io.netty.channel.Channel} without
     * queuing "too-much".
     */
    private int WritableBytes()
    {
        return Math.Min(ConnectionWindowSize(), MaxUsableChannelBytes());
    }

    public virtual void WritePendingBytes()
    {
        monitor.WritePendingBytes();
    }

    /**
     * The remote flow control state for a single stream.
     */
    private sealed class FlowState : IStreamByteDistributor.IStreamState
    {
        private readonly DefaultHttp2RemoteFlowController controller;
        internal readonly IHttp2Stream stream;
        private readonly LinkedList<IHttp2RemoteFlowController.IFlowControlled> pendingWriteQueue;
        private int window;
        private long pendingBytes;
        private bool markedWritable;

        /**
         * Set to true while a frame is being written, false otherwise.
         */
        private bool writing;
        /**
         * Set to true if cancel() was called.
         */
        private bool cancelled;

        internal FlowState(DefaultHttp2RemoteFlowController controller, IHttp2Stream stream)
        {
            this.controller = controller;
            this.stream = stream;
            pendingWriteQueue = new LinkedList<IHttp2RemoteFlowController.IFlowControlled>();
        }

        /**
         * Determine if the stream associated with this object is writable.
         * @return {@code true} if the stream associated with this object is writable.
         */
        internal bool IsWritable()
        {
            return WindowSize() > PendingBytes() && !cancelled;
        }

        /**
         * The stream this state is associated with.
         */
        public IHttp2Stream Stream()
        {
            return stream;
        }

        /**
         * Returns the parameter from the last call to {@link #markedWritability(boolean)}.
         */
        internal bool MarkedWritability()
        {
            return markedWritable;
        }

        /**
         * Save the state of writability.
         */
        internal void MarkedWritability(bool isWritable)
        {
            this.markedWritable = isWritable;
        }

        public int WindowSize()
        {
            return window;
        }

        /**
         * Reset the window size for this stream.
         */
        internal void WindowSize(int initialWindowSize)
        {
            window = initialWindowSize;
        }

        /**
         * Write the allocated bytes for this stream.
         * @return the number of bytes written for a stream or {@code -1} if no write occurred.
         */
        internal int WriteAllocatedBytes(int allocated)
        {
            int initialAllocated = allocated;
            int writtenBytes;
            // In case an exception is thrown we want to remember it and pass it to cancel(Throwable).
            Exception cause = null;
            IHttp2RemoteFlowController.IFlowControlled frame;

            try
            {
                Debug.Assert(!writing);
                writing = true;

                // Write the remainder of frames that we are allowed to
                bool writeOccurred = false;

                while (!cancelled && (frame = Peek()) != null)
                {
                    int maxBytes = Math.Min(allocated, WritableWindow());
                    if (maxBytes <= 0 && frame.Size() > 0)
                    {
                        // The frame still has data, but the amount of allocated bytes has been exhausted.
                        // Don't write needless empty frames.
                        break;
                    }

                    writeOccurred = true;
                    int initialFrameSize = frame.Size();

                    try
                    {
                        frame.Write(controller.ctx, Math.Max(0, maxBytes));

                        if (frame.Size() == 0)
                        {
                            // This frame has been fully written, remove this frame and notify it.
                            // Since we remove this frame first, we're guaranteed that its error
                            // method will not be called when we call cancel.
                            pendingWriteQueue.RemoveFirst();
                            frame.WriteComplete();
                        }
                    }
                    finally
                    {
                        // Decrement allocated by how much was actually written.
                        allocated -= initialFrameSize - frame.Size();
                    }
                }

                if (!writeOccurred)
                {
                    // Either there was no frame, or the amount of allocated bytes has been exhausted.
                    return -1;
                }

            }
            catch (Exception t)
            {
                // Mark the state as cancelled, we'll clear the pending queue via cancel() below.
                cancelled = true;
                cause = t;
            }
            finally
            {
                writing = false;
                // Make sure we always decrement the flow control windows
                // by the bytes written.
                writtenBytes = initialAllocated - allocated;

                DecrementPendingBytes(writtenBytes, false);
                DecrementFlowControlWindow(writtenBytes);

                // If a cancellation occurred while writing, call cancel again to
                // clear and error all of the pending writes.
                if (cancelled)
                {
                    Cancel(INTERNAL_ERROR, cause);
                }
            }

            return writtenBytes;
        }

        /**
         * Increments the flow control window for this stream by the given delta and returns the new value.
         */
        internal int IncrementStreamWindow(int delta)
        {
            if (delta > 0 && int.MaxValue - delta < window)
            {
                throw StreamError(stream.Id(), FLOW_CONTROL_ERROR,
                        "Window size overflow for stream: %d", stream.Id());
            }

            window += delta;

            controller.streamByteDistributor.UpdateStreamableBytes(this);
            return window;
        }

        /**
         * Returns the maximum writable window (minimum of the stream and connection windows).
         */
        private int WritableWindow()
        {
            return Math.Min(window, controller.ConnectionWindowSize());
        }

        public long PendingBytes()
        {
            return pendingBytes;
        }

        /**
         * Adds the {@code frame} to the pending queue and increments the pending byte count.
         */
        internal void EnqueueFrame(IHttp2RemoteFlowController.IFlowControlled frame)
        {
            IHttp2RemoteFlowController.IFlowControlled last = pendingWriteQueue.Last?.Value;
            if (last == null)
            {
                EnqueueFrameWithoutMerge(frame);
                return;
            }

            int lastSize = last.Size();

            if (last.Merge(controller.ctx, frame))
            {
                IncrementPendingBytes(last.Size() - lastSize, true);
                return;
            }

            EnqueueFrameWithoutMerge(frame);
        }

        private void EnqueueFrameWithoutMerge(IHttp2RemoteFlowController.IFlowControlled frame)
        {
            pendingWriteQueue.AddLast(frame);
            // This must be called after adding to the queue in order so that hasFrame() is
            // updated before updating the stream state.
            IncrementPendingBytes(frame.Size(), true);
        }

        public bool HasFrame()
        {
            return pendingWriteQueue.Count != 0;
        }

        /**
         * Returns the head of the pending queue, or {@code null} if empty.
         */
        private IHttp2RemoteFlowController.IFlowControlled Peek()
        {
            return pendingWriteQueue.First?.Value;
        }

        // CLR: Deque.poll(): removes and returns the head, or null when the queue is empty.
        private IHttp2RemoteFlowController.IFlowControlled Poll()
        {
            LinkedListNode<IHttp2RemoteFlowController.IFlowControlled> first = pendingWriteQueue.First;
            if (first == null)
            {
                return null;
            }

            pendingWriteQueue.RemoveFirst();
            return first.Value;
        }

        /**
         * Clears the pending queue and writes errors for each remaining frame.
         * @param error the {@link Http2Error} to use.
         * @param cause the {@link Throwable} that caused this method to be invoked.
         */
        internal void Cancel(Http2Error error, Exception cause)
        {
            cancelled = true;
            // Ensure that the queue can't be modified while we are writing.
            if (writing)
            {
                return;
            }

            IHttp2RemoteFlowController.IFlowControlled frame = Poll();
            if (frame != null)
            {
                // Only create exception once and reuse to reduce overhead of filling in the stacktrace.
                Http2Exception exception = StreamError(stream.Id(), error, cause,
                        "Stream closed before write could take place");

                do
                {
                    WriteError(frame, exception);
                    frame = Poll();
                } while (frame != null);
            }

            controller.streamByteDistributor.UpdateStreamableBytes(this);

            controller.monitor.StateCancelled(this);
        }

        /**
         * Increments the number of pending bytes for this node and optionally updates the
         * {@link StreamByteDistributor}.
         */
        private void IncrementPendingBytes(int numBytes, bool updateStreamableBytes)
        {
            pendingBytes += numBytes;
            controller.monitor.IncrementPendingBytes(numBytes);

            if (updateStreamableBytes)
            {
                controller.streamByteDistributor.UpdateStreamableBytes(this);
            }
        }

        /**
         * If this frame is in the pending queue, decrements the number of pending bytes for the stream.
         */
        private void DecrementPendingBytes(int bytes, bool updateStreamableBytes)
        {
            IncrementPendingBytes(-bytes, updateStreamableBytes);
        }

        /**
         * Decrement the per stream and connection flow control window by {@code bytes}.
         */
        private void DecrementFlowControlWindow(int bytes)
        {
            try
            {
                int negativeBytes = -bytes;
                controller.connectionState.IncrementStreamWindow(negativeBytes);
                IncrementStreamWindow(negativeBytes);
            }
            catch (Http2Exception e)
            {
                // Should never get here since we're decrementing.
                throw new InvalidOperationException("Invalid window state when writing frame: " + e.Message, e);
            }
        }

        /**
         * Discards this {@link FlowControlled}, writing an error. If this frame is in the pending queue,
         * the unwritten bytes are removed from this branch of the priority tree.
         */
        private void WriteError(IHttp2RemoteFlowController.IFlowControlled frame, Http2Exception cause)
        {
            Debug.Assert(controller.ctx != null);
            DecrementPendingBytes(frame.Size(), true);
            frame.Error(controller.ctx, cause);
        }
    }

    /**
     * Abstract class which provides common functionality for writability monitor implementations.
     */
    private class WritabilityMonitor : IStreamByteDistributor.IWriter
    {
        protected readonly DefaultHttp2RemoteFlowController controller;
        private bool inWritePendingBytes;
        private long totalPendingBytes;

        internal WritabilityMonitor(DefaultHttp2RemoteFlowController controller)
        {
            this.controller = controller;
        }

        public void Write(IHttp2Stream stream, int numBytes)
        {
            controller.State(stream).WriteAllocatedBytes(numBytes);
        }

        /**
         * Called when the writability of the underlying channel changes.
         * @throws Http2Exception If a write occurs and an exception happens in the write operation.
         */
        internal virtual void ChannelWritabilityChange()
        {
        }

        /**
         * Called when the state is cancelled.
         * @param state the state that was cancelled.
         */
        internal virtual void StateCancelled(FlowState state)
        {
        }

        /**
         * Set the initial window size for {@code state}.
         * @param state the state to change the initial window size for.
         * @param initialWindowSize the size of the window in bytes.
         */
        internal virtual void WindowSize(FlowState state, int initialWindowSize)
        {
            state.WindowSize(initialWindowSize);
        }

        /**
         * Increment the window size for a particular stream.
         * @param state the state associated with the stream whose window is being incremented.
         * @param delta The amount to increment by.
         * @throws Http2Exception If this operation overflows the window for {@code state}.
         */
        internal virtual void IncrementWindowSize(FlowState state, int delta)
        {
            state.IncrementStreamWindow(delta);
        }

        /**
         * Add a frame to be sent via flow control.
         * @param state The state associated with the stream which the {@code frame} is associated with.
         * @param frame the frame to enqueue.
         * @throws Http2Exception If a writability error occurs.
         */
        internal virtual void EnqueueFrame(FlowState state, IHttp2RemoteFlowController.IFlowControlled frame)
        {
            state.EnqueueFrame(frame);
        }

        /**
         * Increment the total amount of pending bytes for all streams. When any stream's pending bytes changes
         * method should be called.
         * @param delta The amount to increment by.
         */
        internal void IncrementPendingBytes(int delta)
        {
            totalPendingBytes += delta;

            // Notification of writibilty change should be delayed until the end of the top level event.
            // This is to ensure the flow controller is more consistent state before calling external listener methods.
        }

        /**
         * Determine if the stream associated with {@code state} is writable.
         * @param state The state which is associated with the stream to test writability for.
         * @return {@code true} if {@link FlowState#stream()} is writable. {@code false} otherwise.
         */
        internal bool IsWritable(FlowState state)
        {
            return IsWritableConnection() && state.IsWritable();
        }

        internal void WritePendingBytes()
        {
            // Reentry is not permitted during the byte distribution process. It may lead to undesirable distribution of
            // bytes and even infinite loops. We protect against reentry and make sure each call has an opportunity to
            // cause a distribution to occur. This may be useful for example if the channel's writability changes from
            // Writable -> Not Writable (because we are writing) -> Writable (because the user flushed to make more room
            // in the channel outbound buffer).
            if (inWritePendingBytes)
            {
                return;
            }

            inWritePendingBytes = true;

            try
            {
                int bytesToWrite = controller.WritableBytes();

                // Make sure we always write at least once, regardless if we have bytesToWrite or not.
                // This ensures that zero-length frames will always be written.
                for (;;)
                {
                    if (!controller.streamByteDistributor.Distribute(bytesToWrite, this) ||
                        (bytesToWrite = controller.WritableBytes()) <= 0 ||
                        !controller.IsChannelWritable0())
                    {
                        break;
                    }
                }
            }
            finally
            {
                inWritePendingBytes = false;
            }
        }

        internal virtual void InitialWindowSize(int newWindowSize)
        {
            CheckPositiveOrZero(newWindowSize, "newWindowSize");

            int delta = newWindowSize - controller.initialWindowSize;
            controller.initialWindowSize = newWindowSize;
            controller.connection.ForEachActiveStream(new InitialWindowSizeVisitor(controller, delta));

            if (delta > 0 && controller.IsChannelWritable())
            {
                // The window size increased, send any pending frames for all streams.
                WritePendingBytes();
            }
        }

        internal bool IsWritableConnection()
        {
            return controller.connectionState.WindowSize() - totalPendingBytes > 0 && controller.IsChannelWritable();
        }
    }

    private sealed class InitialWindowSizeVisitor : IHttp2StreamVisitor
    {
        private readonly DefaultHttp2RemoteFlowController controller;
        private readonly int delta;

        internal InitialWindowSizeVisitor(DefaultHttp2RemoteFlowController controller, int delta)
        {
            this.controller = controller;
            this.delta = delta;
        }

        public bool Visit(IHttp2Stream stream)
        {
            try
            {
                controller.State(stream).IncrementStreamWindow(delta);
            }
            catch (Http2Exception e)
            {
                // RFC 9113 Section 6.9.2 requires a connection error if a SETTINGS change causes overflow.
                throw ConnectionError(FLOW_CONTROL_ERROR, e,
                        "Window size overflow for stream: %d after SETTINGS_INITIAL_WINDOW_SIZE change",
                        stream.Id());
            }

            return true;
        }
    }

    /**
     * Writability of a {@code stream} is calculated using the following:
     * <pre>
     * Connection Window - Total Queued Bytes > 0 &&
     * Stream Window - Bytes Queued for Stream > 0 &&
     * isChannelWritable()
     * </pre>
     */
    private sealed class ListenerWritabilityMonitor : WritabilityMonitor, IHttp2StreamVisitor
    {
        private readonly IHttp2RemoteFlowController.IListener listener;

        internal ListenerWritabilityMonitor(DefaultHttp2RemoteFlowController controller,
                                            IHttp2RemoteFlowController.IListener listener) : base(controller)
        {
            this.listener = listener;
        }

        public bool Visit(IHttp2Stream stream)
        {
            FlowState state = controller.State(stream);
            if (IsWritable(state) != state.MarkedWritability())
            {
                NotifyWritabilityChanged(state);
            }

            return true;
        }

        internal override void WindowSize(FlowState state, int initialWindowSize)
        {
            base.WindowSize(state, initialWindowSize);

            try
            {
                CheckStateWritability(state);
            }
            catch (Http2Exception e)
            {
                throw new Exception("Caught unexpected exception from window", e);
            }
        }

        internal override void IncrementWindowSize(FlowState state, int delta)
        {
            base.IncrementWindowSize(state, delta);
            CheckStateWritability(state);
        }

        internal override void InitialWindowSize(int newWindowSize)
        {
            base.InitialWindowSize(newWindowSize);

            if (IsWritableConnection())
            {
                // If the write operation does not occur we still need to check all streams because they
                // may have transitioned from writable to not writable.
                CheckAllWritabilityChanged();
            }
        }

        internal override void EnqueueFrame(FlowState state, IHttp2RemoteFlowController.IFlowControlled frame)
        {
            base.EnqueueFrame(state, frame);
            CheckConnectionThenStreamWritabilityChanged(state);
        }

        internal override void StateCancelled(FlowState state)
        {
            try
            {
                CheckConnectionThenStreamWritabilityChanged(state);
            }
            catch (Http2Exception e)
            {
                throw new Exception("Caught unexpected exception from checkAllWritabilityChanged", e);
            }
        }

        internal override void ChannelWritabilityChange()
        {
            if (controller.connectionState.MarkedWritability() != controller.IsChannelWritable())
            {
                CheckAllWritabilityChanged();
            }
        }

        private void CheckStateWritability(FlowState state)
        {
            if (IsWritable(state) != state.MarkedWritability())
            {
                if (state == controller.connectionState)
                {
                    CheckAllWritabilityChanged();
                }
                else
                {
                    NotifyWritabilityChanged(state);
                }
            }
        }

        private void NotifyWritabilityChanged(FlowState state)
        {
            state.MarkedWritability(!state.MarkedWritability());

            try
            {
                listener.WritabilityChanged(state.stream);
            }
            catch (Exception cause)
            {
                logger.Error("{} Caught Throwable from listener.writabilityChanged for {}",
                        controller.ctx.Channel, state.stream, cause);
            }
        }

        private void CheckConnectionThenStreamWritabilityChanged(FlowState state)
        {
            // It is possible that the connection window and/or the individual stream writability could change.
            if (IsWritableConnection() != controller.connectionState.MarkedWritability())
            {
                CheckAllWritabilityChanged();
            }
            else if (IsWritable(state) != state.MarkedWritability())
            {
                NotifyWritabilityChanged(state);
            }
        }

        private void CheckAllWritabilityChanged()
        {
            // Make sure we mark that we have notified as a result of this change.
            controller.connectionState.MarkedWritability(IsWritableConnection());
            controller.connection.ForEachActiveStream(this);
        }
    }
}
