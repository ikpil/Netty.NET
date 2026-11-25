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
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Basic implementation of {@link Http2LocalFlowController}.
 * <p>
 * This class is <strong>NOT</strong> thread safe. The assumption is all methods must be invoked from a single thread.
 * Typically this thread is the event loop thread for the {@link ChannelHandlerContext} managed by this class.
 */
// CLR: the inner classes (DefaultState, AutoRefillState, WindowUpdateVisitor and the anonymous Http2ConnectionAdapter,
// here the private nested StreamListener) receive the controller explicitly, because a C# nested class has no
// enclosing instance; the private interface FlowState is IFlowState and the anonymous REDUCED_FLOW_STATE is the private
// nested ReducedFlowState. frameWriter(Http2FrameWriter) narrows its return type to this class: the public method
// returns DefaultHttp2LocalFlowController and the IHttp2LocalFlowController member is implemented explicitly (C#
// covariant returns apply to class overrides only, docs/codec-http-porting.md, Shared CLR adaptations).
// PlatformDependent.throwException(e) rethrows the caught Http2Exception (throw;). ctx.newPromise() is
// Http2CodecUtil.NewPromise() and the write's Task is discarded as Java discards the future. assert is Debug.Assert
// (docs/codec-http-porting.md). Double.compare(ratio, 0.0) is double.CompareTo: they order NaN differently (Java
// puts NaN above every value, .NET below), but a NaN ratio fails one of the two checks in both, and -0.0 fails the
// first in both. "Invalid ratio: " + ratio prints the float with the invariant culture (Java's Float.toString writes
// 0.5 the same way but 1.0 as "1.0" where .NET writes "1").
public class DefaultHttp2LocalFlowController : IHttp2LocalFlowController
{
    /**
     * The default ratio of window size to initial window size below which a {@code WINDOW_UPDATE}
     * is sent to expand the window.
     */
    public const float DEFAULT_WINDOW_UPDATE_RATIO = 0.5f;

    private readonly IHttp2Connection connection;
    private readonly IHttp2Connection.IPropertyKey stateKey;
    private IHttp2FrameWriter frameWriter;
    private IChannelHandlerContext ctx;
    private float windowUpdateRatio;
    private int initialWindowSize = DEFAULT_WINDOW_SIZE;

    public DefaultHttp2LocalFlowController(IHttp2Connection connection)
        : this(connection, DEFAULT_WINDOW_UPDATE_RATIO, false)
    {
    }

    /**
     * Constructs a controller with the given settings.
     *
     * @param connection the connection state.
     * @param windowUpdateRatio the window percentage below which to send a {@code WINDOW_UPDATE}.
     * @param autoRefillConnectionWindow if {@code true}, effectively disables the connection window
     * in the flow control algorithm as they will always refill automatically without requiring the
     * application to consume the bytes. When enabled, the maximum bytes you must be prepared to
     * queue is proportional to {@code maximum number of concurrent streams * the initial window
     * size per stream}
     * (<a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_CONCURRENT_STREAMS</a>
     * <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_INITIAL_WINDOW_SIZE</a>).
     */
    public DefaultHttp2LocalFlowController(IHttp2Connection connection,
                                           float windowUpdateRatio,
                                           bool autoRefillConnectionWindow)
    {
        this.connection = CheckNotNull(connection, "connection");
        WindowUpdateRatio(windowUpdateRatio);

        // Add a flow state for the connection.
        stateKey = connection.NewKey();
        IFlowState connectionState = autoRefillConnectionWindow ?
                new AutoRefillState(this, connection.ConnectionStream(), initialWindowSize) :
                new DefaultState(this, connection.ConnectionStream(), initialWindowSize);
        connection.ConnectionStream().SetProperty(stateKey, connectionState);

        // Register for notification of new streams.
        connection.AddListener(new StreamListener(this));
    }

    // CLR: the anonymous Http2ConnectionAdapter registered by the constructor.
    private sealed class StreamListener : Http2ConnectionAdapter
    {
        private readonly DefaultHttp2LocalFlowController controller;

        internal StreamListener(DefaultHttp2LocalFlowController controller)
        {
            this.controller = controller;
        }

        public override void OnStreamAdded(IHttp2Stream stream)
        {
            // Unconditionally used the reduced flow control state because it requires no object allocation
            // and the DefaultFlowState will be allocated in onStreamActive.
            stream.SetProperty(controller.stateKey, REDUCED_FLOW_STATE);
        }

        public override void OnStreamActive(IHttp2Stream stream)
        {
            // Need to be sure the stream's initial window is adjusted for SETTINGS
            // frames which may have been exchanged while it was in IDLE
            stream.SetProperty(controller.stateKey, new DefaultState(controller, stream, controller.initialWindowSize));
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            try
            {
                // When a stream is closed, consume any remaining bytes so that they
                // are restored to the connection window.
                IFlowState state = controller.State(stream);
                int unconsumedBytes = state.UnconsumedBytes();
                if (controller.ctx != null && unconsumedBytes > 0)
                {
                    if (controller.ConsumeAllBytes(state, unconsumedBytes))
                    {
                        // As the user has no real control on when this callback is used we should better
                        // call flush() if we produced any window update to ensure we not stale.
                        controller.ctx.Flush();
                    }
                }
            }
            catch (Http2Exception)
            {
                throw;
            }
            finally
            {
                // Unconditionally reduce the amount of memory required for flow control because there is no
                // object allocation costs associated with doing so and the stream will not have any more
                // local flow control state to keep track of anymore.
                stream.SetProperty(controller.stateKey, REDUCED_FLOW_STATE);
            }
        }
    }

    public virtual DefaultHttp2LocalFlowController FrameWriter(IHttp2FrameWriter frameWriter)
    {
        this.frameWriter = CheckNotNull(frameWriter, "frameWriter");
        return this;
    }

    IHttp2LocalFlowController IHttp2LocalFlowController.FrameWriter(IHttp2FrameWriter frameWriter)
    {
        return FrameWriter(frameWriter);
    }

    public virtual void ChannelHandlerContext(IChannelHandlerContext ctx)
    {
        this.ctx = CheckNotNull(ctx, "ctx");
    }

    public virtual void InitialWindowSize(int newWindowSize)
    {
        Debug.Assert(ctx == null || ctx.Executor.InEventLoop());
        int delta = newWindowSize - initialWindowSize;
        initialWindowSize = newWindowSize;

        WindowUpdateVisitor visitor = new WindowUpdateVisitor(this, delta);
        connection.ForEachActiveStream(visitor);
        visitor.ThrowIfError();
    }

    public virtual int InitialWindowSize()
    {
        return initialWindowSize;
    }

    public virtual int WindowSize(IHttp2Stream stream)
    {
        return State(stream).WindowSize();
    }

    public virtual int InitialWindowSize(IHttp2Stream stream)
    {
        return State(stream).InitialWindowSize();
    }

    public virtual void IncrementWindowSize(IHttp2Stream stream, int delta)
    {
        Debug.Assert(ctx != null && ctx.Executor.InEventLoop());
        IFlowState state = State(stream);
        // Just add the delta to the stream-specific initial window size so that the next time the window
        // expands it will grow to the new initial size.
        state.IncrementInitialStreamWindow(delta);
        state.WriteWindowUpdateIfNeeded();
    }

    public virtual bool ConsumeBytes(IHttp2Stream stream, int numBytes)
    {
        Debug.Assert(ctx != null && ctx.Executor.InEventLoop());
        CheckPositiveOrZero(numBytes, "numBytes");
        if (numBytes == 0)
        {
            return false;
        }

        // Streams automatically consume all remaining bytes when they are closed, so just ignore
        // if already closed.
        if (stream != null && !IsClosed(stream))
        {
            if (stream.Id() == CONNECTION_STREAM_ID)
            {
                throw new NotSupportedException("Returning bytes for the connection window is not supported");
            }

            return ConsumeAllBytes(State(stream), numBytes);
        }
        return false;
    }

    private bool ConsumeAllBytes(IFlowState state, int numBytes)
    {
        return ConnectionState().ConsumeBytes(numBytes) | state.ConsumeBytes(numBytes);
    }

    public virtual int UnconsumedBytes(IHttp2Stream stream)
    {
        return State(stream).UnconsumedBytes();
    }

    private static void CheckValidRatio(float ratio)
    {
        if (((double) ratio).CompareTo(0.0) <= 0 || ((double) ratio).CompareTo(1.0) >= 0)
        {
            throw new ArgumentException("Invalid ratio: " + ratio.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * The window update ratio is used to determine when a window update must be sent. If the ratio
     * of bytes processed since the last update has meet or exceeded this ratio then a window update will
     * be sent. This is the global window update ratio that will be used for new streams.
     * @param ratio the ratio to use when checking if a {@code WINDOW_UPDATE} is determined necessary for new streams.
     * @throws IllegalArgumentException If the ratio is out of bounds (0, 1).
     */
    public virtual void WindowUpdateRatio(float ratio)
    {
        Debug.Assert(ctx == null || ctx.Executor.InEventLoop());
        CheckValidRatio(ratio);
        windowUpdateRatio = ratio;
    }

    /**
     * The window update ratio is used to determine when a window update must be sent. If the ratio
     * of bytes processed since the last update has meet or exceeded this ratio then a window update will
     * be sent. This is the global window update ratio that will be used for new streams.
     */
    public virtual float WindowUpdateRatio()
    {
        return windowUpdateRatio;
    }

    /**
     * The window update ratio is used to determine when a window update must be sent. If the ratio
     * of bytes processed since the last update has meet or exceeded this ratio then a window update will
     * be sent. This window update ratio will only be applied to {@code streamId}.
     * <p>
     * Note it is the responsibly of the caller to ensure that the
     * initial {@code SETTINGS} frame is sent before this is called. It would
     * be considered a {@link Http2Error#PROTOCOL_ERROR} if a {@code WINDOW_UPDATE}
     * was generated by this method before the initial {@code SETTINGS} frame is sent.
     * @param stream the stream for which {@code ratio} applies to.
     * @param ratio the ratio to use when checking if a {@code WINDOW_UPDATE} is determined necessary.
     * @throws Http2Exception If a protocol-error occurs while generating {@code WINDOW_UPDATE} frames
     */
    public virtual void WindowUpdateRatio(IHttp2Stream stream, float ratio)
    {
        Debug.Assert(ctx != null && ctx.Executor.InEventLoop());
        CheckValidRatio(ratio);
        IFlowState state = State(stream);
        state.WindowUpdateRatio(ratio);
        state.WriteWindowUpdateIfNeeded();
    }

    /**
     * The window update ratio is used to determine when a window update must be sent. If the ratio
     * of bytes processed since the last update has meet or exceeded this ratio then a window update will
     * be sent. This window update ratio will only be applied to {@code streamId}.
     * @throws Http2Exception If no stream corresponding to {@code stream} could be found.
     */
    public virtual float WindowUpdateRatio(IHttp2Stream stream)
    {
        return State(stream).WindowUpdateRatio();
    }

    public virtual void ReceiveFlowControlledFrame(IHttp2Stream stream, ByteBuf data, int padding,
            bool endOfStream)
    {
        Debug.Assert(ctx != null && ctx.Executor.InEventLoop());
        int dataLength = data.ReadableBytes + padding;

        // Apply the connection-level flow control
        IFlowState connectionState = ConnectionState();
        connectionState.ReceiveFlowControlledFrame(dataLength);

        if (stream != null && !IsClosed(stream))
        {
            // Apply the stream-level flow control
            IFlowState state = State(stream);
            state.EndOfStream(endOfStream);
            state.ReceiveFlowControlledFrame(dataLength);
        }
        else if (dataLength > 0)
        {
            // Immediately consume the bytes for the connection window.
            connectionState.ConsumeBytes(dataLength);
        }
    }

    private IFlowState ConnectionState()
    {
        return connection.ConnectionStream().GetProperty<IFlowState>(stateKey);
    }

    private IFlowState State(IHttp2Stream stream)
    {
        return stream.GetProperty<IFlowState>(stateKey);
    }

    private static bool IsClosed(IHttp2Stream stream)
    {
        return stream.State_() == IHttp2Stream.State.CLOSED;
    }

    /**
     * Flow control state that does autorefill of the flow control window when the data is
     * received.
     */
    private sealed class AutoRefillState : DefaultState
    {
        internal AutoRefillState(DefaultHttp2LocalFlowController controller, IHttp2Stream stream, int initialWindowSize)
            : base(controller, stream, initialWindowSize)
        {
        }

        public override void ReceiveFlowControlledFrame(int dataLength)
        {
            base.ReceiveFlowControlledFrame(dataLength);
            // Need to call the super to consume the bytes, since this.consumeBytes does nothing.
            base.ConsumeBytes(dataLength);
        }

        public override bool ConsumeBytes(int numBytes)
        {
            // Do nothing, since the bytes are already consumed upon receiving the data.
            return false;
        }
    }

    /**
     * Flow control window state for an individual stream.
     */
    private class DefaultState : IFlowState
    {
        private readonly DefaultHttp2LocalFlowController controller;
        private readonly IHttp2Stream stream;

        /**
         * The actual flow control window that is decremented as soon as {@code DATA} arrives.
         */
        private int window;

        /**
         * A view of {@link #window} that is used to determine when to send {@code WINDOW_UPDATE}
         * frames. Decrementing this window for received {@code DATA} frames is delayed until the
         * application has indicated that the data has been fully processed. This prevents sending
         * a {@code WINDOW_UPDATE} until the number of processed bytes drops below the threshold.
         */
        private int processedWindow;

        /**
         * This is what is used to determine how many bytes need to be returned relative to {@link #processedWindow}.
         * Each stream has their own initial window size.
         */
        private int initialStreamWindowSize;

        /**
         * This is used to determine when {@link #processedWindow} is sufficiently far away from
         * {@link #initialStreamWindowSize} such that a {@code WINDOW_UPDATE} should be sent.
         * Each stream has their own window update ratio.
         */
        private float streamWindowUpdateRatio;

        private int lowerBound;
        private bool endOfStream;

        internal DefaultState(DefaultHttp2LocalFlowController controller, IHttp2Stream stream, int initialWindowSize)
        {
            this.controller = controller;
            this.stream = stream;
            Window(initialWindowSize);
            streamWindowUpdateRatio = controller.windowUpdateRatio;
        }

        public void Window(int initialWindowSize)
        {
            Debug.Assert(controller.ctx == null || controller.ctx.Executor.InEventLoop());
            window = processedWindow = initialStreamWindowSize = initialWindowSize;
        }

        public int WindowSize()
        {
            return window;
        }

        public int InitialWindowSize()
        {
            return initialStreamWindowSize;
        }

        public void EndOfStream(bool endOfStream)
        {
            this.endOfStream = endOfStream;
        }

        public float WindowUpdateRatio()
        {
            return streamWindowUpdateRatio;
        }

        public void WindowUpdateRatio(float ratio)
        {
            Debug.Assert(controller.ctx == null || controller.ctx.Executor.InEventLoop());
            streamWindowUpdateRatio = ratio;
        }

        public void IncrementInitialStreamWindow(int delta)
        {
            // Clip the delta so that the resulting initialStreamWindowSize falls within the allowed range.
            int newValue = (int) Math.Min(MAX_INITIAL_WINDOW_SIZE,
                    Math.Max(MIN_INITIAL_WINDOW_SIZE, initialStreamWindowSize + (long) delta));
            delta = newValue - initialStreamWindowSize;

            initialStreamWindowSize += delta;
        }

        public void IncrementFlowControlWindows(int delta)
        {
            if (delta > 0 && window > MAX_INITIAL_WINDOW_SIZE - delta)
            {
                throw StreamError(stream.Id(), FLOW_CONTROL_ERROR,
                        "Flow control window overflowed for stream: %d", stream.Id());
            }

            window += delta;
            processedWindow += delta;
            lowerBound = Math.Min(delta, 0);
        }

        public virtual void ReceiveFlowControlledFrame(int dataLength)
        {
            Debug.Assert(dataLength >= 0);

            // Apply the delta. Even if we throw an exception we want to have taken this delta into account.
            window -= dataLength;

            // Window size can become negative if we sent a SETTINGS frame that reduces the
            // size of the transfer window after the peer has written data frames.
            // The value is bounded by the length that SETTINGS frame decrease the window.
            // This difference is stored for the connection when writing the SETTINGS frame
            // and is cleared once we send a WINDOW_UPDATE frame.
            if (window < lowerBound)
            {
                throw StreamError(stream.Id(), FLOW_CONTROL_ERROR,
                        "Flow control window exceeded for stream: %d", stream.Id());
            }
        }

        private void ReturnProcessedBytes(int delta)
        {
            if (processedWindow - delta < window)
            {
                throw StreamError(stream.Id(), INTERNAL_ERROR,
                        "Attempting to return too many bytes for stream %d", stream.Id());
            }
            processedWindow -= delta;
        }

        public virtual bool ConsumeBytes(int numBytes)
        {
            // Return the bytes processed and update the window.
            ReturnProcessedBytes(numBytes);
            return WriteWindowUpdateIfNeeded();
        }

        public int UnconsumedBytes()
        {
            return processedWindow - window;
        }

        public bool WriteWindowUpdateIfNeeded()
        {
            if (endOfStream || initialStreamWindowSize <= 0 ||
                    // If the stream is already closed there is no need to try to write a window update for it.
                    IsClosed(stream))
            {
                return false;
            }

            int threshold = (int) (initialStreamWindowSize * streamWindowUpdateRatio);
            if (processedWindow <= threshold)
            {
                WriteWindowUpdate();
                return true;
            }
            return false;
        }

        /**
         * Called to perform a window update for this stream (or connection). Updates the window size back
         * to the size of the initial window and sends a window update frame to the remote endpoint.
         */
        private void WriteWindowUpdate()
        {
            // Expand the window for this stream back to the size of the initial window.
            int deltaWindowSize = initialStreamWindowSize - processedWindow;
            try
            {
                IncrementFlowControlWindows(deltaWindowSize);
            }
            catch (Exception t)
            {
                throw ConnectionError(INTERNAL_ERROR, t,
                        "Attempting to return too many bytes for stream %d", stream.Id());
            }

            // Send a window update for the stream/connection.
            _ = controller.frameWriter.WriteWindowUpdateAsync(controller.ctx, stream.Id(), deltaWindowSize,
                NewPromise());
        }
    }

    /**
     * The local flow control state for a single stream that is not in a state where flow controlled frames cannot
     * be exchanged.
     */
    private static readonly IFlowState REDUCED_FLOW_STATE = new ReducedFlowState();

    // CLR: the anonymous FlowState of REDUCED_FLOW_STATE.
    private sealed class ReducedFlowState : IFlowState
    {

        public int WindowSize()
        {
            return 0;
        }

        public int InitialWindowSize()
        {
            return 0;
        }

        public void Window(int initialWindowSize)
        {
            throw new NotSupportedException();
        }

        public void IncrementInitialStreamWindow(int delta)
        {
            // This operation needs to be supported during the initial settings exchange when
            // the peer has not yet acknowledged this peer being activated.
        }

        public bool WriteWindowUpdateIfNeeded()
        {
            throw new NotSupportedException();
        }

        public bool ConsumeBytes(int numBytes)
        {
            return false;
        }

        public int UnconsumedBytes()
        {
            return 0;
        }

        public float WindowUpdateRatio()
        {
            throw new NotSupportedException();
        }

        public void WindowUpdateRatio(float ratio)
        {
            throw new NotSupportedException();
        }

        public void ReceiveFlowControlledFrame(int dataLength)
        {
            throw new NotSupportedException();
        }

        public void IncrementFlowControlWindows(int delta)
        {
            // This operation needs to be supported during the initial settings exchange when
            // the peer has not yet acknowledged this peer being activated.
        }

        public void EndOfStream(bool endOfStream)
        {
            throw new NotSupportedException();
        }
    }

    /**
     * An abstraction which provides specific extensions used by local flow control.
     */
    private interface IFlowState
    {

        int WindowSize();

        int InitialWindowSize();

        void Window(int initialWindowSize);

        /**
         * Increment the initial window size for this stream.
         * @param delta The amount to increase the initial window size by.
         */
        void IncrementInitialStreamWindow(int delta);

        /**
         * Updates the flow control window for this stream if it is appropriate.
         *
         * @return true if {@code WINDOW_UPDATE} was written, false otherwise.
         */
        bool WriteWindowUpdateIfNeeded();

        /**
         * Indicates that the application has consumed {@code numBytes} from the connection or stream and is
         * ready to receive more data.
         *
         * @param numBytes the number of bytes to be returned to the flow control window.
         * @return true if {@code WINDOW_UPDATE} was written, false otherwise.
         * @throws Http2Exception
         */
        bool ConsumeBytes(int numBytes);

        int UnconsumedBytes();

        float WindowUpdateRatio();

        void WindowUpdateRatio(float ratio);

        /**
         * A flow control event has occurred and we should decrement the amount of available bytes for this stream.
         * @param dataLength The amount of data to for which this stream is no longer eligible to use for flow control.
         * @throws Http2Exception If too much data is used relative to how much is available.
         */
        void ReceiveFlowControlledFrame(int dataLength);

        /**
         * Increment the windows which are used to determine many bytes have been processed.
         * @param delta The amount to increment the window by.
         * @throws Http2Exception if integer overflow occurs on the window.
         */
        void IncrementFlowControlWindows(int delta);

        void EndOfStream(bool endOfStream);
    }

    /**
     * Provides a means to iterate over all active streams and increment the flow control windows.
     */
    private sealed class WindowUpdateVisitor : IHttp2StreamVisitor
    {
        private readonly DefaultHttp2LocalFlowController controller;
        private CompositeStreamException compositeException;
        private readonly int delta;

        internal WindowUpdateVisitor(DefaultHttp2LocalFlowController controller, int delta)
        {
            this.controller = controller;
            this.delta = delta;
        }

        public bool Visit(IHttp2Stream stream)
        {
            try
            {
                // Increment flow control window first so state will be consistent if overflow is detected.
                IFlowState state = controller.State(stream);
                state.IncrementFlowControlWindows(delta);
                state.IncrementInitialStreamWindow(delta);
            }
            catch (StreamException e)
            {
                if (compositeException == null)
                {
                    compositeException = new CompositeStreamException(e.Error(), 4);
                }
                compositeException.Add(e);
            }
            return true;
        }

        public void ThrowIfError()
        {
            if (compositeException != null)
            {
                throw compositeException;
            }
        }
    }
}
