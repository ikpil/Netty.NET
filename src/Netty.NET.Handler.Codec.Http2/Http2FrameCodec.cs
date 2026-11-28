/*
 * Copyright 2016 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * <p>An HTTP/2 handler that maps HTTP/2 frames to {@link Http2Frame} objects and vice versa. For every incoming HTTP/2
 * frame, an {@link Http2Frame} object is created and propagated via {@link #channelRead}. Outbound {@link Http2Frame}
 * objects received via {@link #write} are converted to the HTTP/2 wire format. HTTP/2 frames specific to a stream
 * implement the {@link Http2StreamFrame} interface. The {@link Http2FrameCodec} is instantiated using the
 * {@link Http2FrameCodecBuilder}. It's recommended for channel handlers to inherit from the
 * {@link Http2ChannelDuplexHandler}, as it provides additional functionality like iterating over all active streams or
 * creating outbound streams.
 *
 * <h3>Stream Lifecycle</h3>
 * <p>
 * The frame codec delivers and writes frames for active streams. An active stream is closed when either side sends a
 * {@code RST_STREAM} frame or both sides send a frame with the {@code END_STREAM} flag set. Each
 * {@link Http2StreamFrame} has a {@link Http2FrameStream} object attached that uniquely identifies a particular stream.
 *
 * <p>{@link Http2StreamFrame}s read from the channel always a {@link Http2FrameStream} object set, while when writing a
 * {@link Http2StreamFrame} the application code needs to set a {@link Http2FrameStream} object using
 * {@link Http2StreamFrame#stream(Http2FrameStream)}.
 *
 * <h3>Flow control</h3>
 * <p>
 * The frame codec automatically increments stream and connection flow control windows.
 *
 * <p>Incoming flow controlled frames need to be consumed by writing a {@link Http2WindowUpdateFrame} with the consumed
 * number of bytes and the corresponding stream identifier set to the frame codec.
 *
 * <p>The local stream-level flow control window can be changed by writing a {@link Http2SettingsFrame} with the
 * {@link Http2Settings#initialWindowSize()} set to the targeted value.
 *
 * <p>The connection-level flow control window can be changed by writing a {@link Http2WindowUpdateFrame} with the
 * desired window size <em>increment</em> in bytes and the stream identifier set to {@code 0}. By default the initial
 * connection-level flow control window is the same as initial stream-level flow control window.
 *
 * <h3>New inbound Streams</h3>
 * <p>
 * The first frame of an HTTP/2 stream must be an {@link Http2HeadersFrame}, which will have an {@link Http2FrameStream}
 * object attached.
 *
 * <h3>New outbound Streams</h3>
 * <p>
 * A outbound HTTP/2 stream can be created by first instantiating a new {@link Http2FrameStream} object via
 * {@link Http2ChannelDuplexHandler#newStream()}, and then writing a {@link Http2HeadersFrame} object with the stream
 * attached.
 *
 * <pre> {@code
 *     final Http2Stream2 stream = handler.newStream();
 *     ctx.write(headersFrame.stream(stream)).addListener(new ChannelFutureListener() {
 *
 *         @Override
 *         public void operationComplete(ChannelFuture f) {
 *             if (f.isSuccess()) {
 *                 // Stream is active and stream.id() returns a valid stream identifier.
 *                 System.out.println("New stream with id " + stream.id() + " created.");
 *             } else {
 *                 // Stream failed to become active. Handle error.
 *                 if (f.cause() instanceof Http2NoMoreStreamIdsException) {
 *
 *                 } else if (f.cause() instanceof Http2GoAwayException) {
 *
 *                 } else {
 *
 *                 }
 *             }
 *         }
 *     }
 *     }
 * </pre>
 *
 * <p>If a new stream cannot be created due to stream id exhaustion of the endpoint, the {@link ChannelPromise} of the
 * HEADERS frame will fail with a {@link Http2NoMoreStreamIdsException}.
 *
 * <p>The HTTP/2 standard allows for an endpoint to limit the maximum number of concurrently active streams via the
 * {@code SETTINGS_MAX_CONCURRENT_STREAMS} setting. When this limit is reached, no new streams can be created. However,
 * the {@link Http2FrameCodec} can be build with
 * {@link Http2FrameCodecBuilder#encoderEnforceMaxConcurrentStreams(boolean)} enabled, in which case a new stream and
 * its associated frames will be buffered until either the limit is increased or an active stream is closed. It's,
 * however, possible that a buffered stream will never become active. That is, the channel might
 * get closed or a GO_AWAY frame might be received. In the first case, all writes of buffered streams will fail with a
 * {@link Http2ChannelClosedException}. In the second case, all writes of buffered streams with an identifier less than
 * the last stream identifier of the GO_AWAY frame will fail with a {@link Http2GoAwayException}.
 *
 * <h3>Error Handling</h3>
 * <p>
 * Exceptions and errors are propagated via {@link ChannelInboundHandler#exceptionCaught}. Exceptions that apply to
 * a specific HTTP/2 stream are wrapped in a {@link Http2FrameStreamException} and have the corresponding
 * {@link Http2FrameStream} object attached.
 *
 * <h3>Reference Counting</h3>
 * <p>
 * Some {@link Http2StreamFrame}s implement the {@link ReferenceCounted} interface, as they carry
 * reference counted objects (e.g. {@link ByteBuf}s). The frame codec will call {@link ReferenceCounted#retain()} before
 * propagating a reference counted object through the pipeline, and thus an application handler needs to release such
 * an object after having consumed it. For more information on reference counting take a look at
 * <a href="https://netty.io/wiki/reference-counted-objects.html">Reference counted objects</a>
 *
 * <h3>HTTP Upgrade</h3>
 * <p>
 * Server-side HTTP to HTTP/2 upgrade is supported in conjunction with {@link Http2ServerUpgradeCodec}; the necessary
 * HTTP-to-HTTP/2 conversion is performed automatically.
 */
// CLR: ChannelPromise is TaskCompletionSource and ChannelFuture Task (docs/codec-http-porting.md, Shared CLR
// adaptations): write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), which creates the promise it
// completes (Http2CodecUtil.NewPromise) and returns its Task; ctx.write(msg, promise) is Http2CodecUtil.Cascade of the
// context's write Task; promise.addListener is Http2CodecUtil.AddListener (EventExecutorTaskScheduler over
// ctx.Executor, the DefaultPromise.notifyListeners timing of docs/codec-http-porting.md, Listener timing);
// promise.unvoid() is the promise itself (docs/transport-porting.md, VoidChannelPromise). PropertyKey is
// IHttp2Connection.IPropertyKey and the unchecked getProperty is the generic GetProperty<V> (src/Netty.NET.Handler.Codec.Http2/
// IHttp2Stream.cs). IntObjectMap<DefaultHttp2FrameStream> is Dictionary<int, DefaultHttp2FrameStream> (owner
// decision 2026-11-19, confirmed 2026-11-25). Integer initialFlowControlWindowSize is int?. Java's package-private
// members are internal (the multiplex handler, the builder and the tests use them), the protected constructor and the
// protected streamKey are protected internal because Java's protected includes package access (Http2FrameCodecBuilder
// calls the constructor and Http2MultiplexClientUpgradeTest reads streamKey), and the package-private hooks that the
// deprecated, not ported Http2MultiplexCodec overrides are internal virtual. The anonymous Http2StreamVisitor and Runnable are a
// private nested class and a lambda. Http2ConnectionHandler's final userEventTriggered/handlerAdded overrides are
// sealed overrides. The checked "throws" clauses have no counterpart.
public class Http2FrameCodec : Http2ConnectionHandler
{

    private static readonly IInternalLogger LOG = InternalLoggerFactory.GetInstance(typeof(Http2FrameCodec));

    private static readonly Type[] SUPPORTED_MESSAGES = new Type[] {
            typeof(IHttp2DataFrame), typeof(IHttp2HeadersFrame), typeof(IHttp2WindowUpdateFrame), typeof(IHttp2ResetFrame),
            typeof(IHttp2PingFrame), typeof(IHttp2SettingsFrame), typeof(IHttp2SettingsAckFrame), typeof(IHttp2GoAwayFrame),
            typeof(IHttp2PushPromiseFrame), typeof(IHttp2PriorityFrame), typeof(IHttp2UnknownFrame) };

    protected internal readonly IHttp2Connection.IPropertyKey streamKey;
    private readonly IHttp2Connection.IPropertyKey upgradeKey;

    private readonly int? initialFlowControlWindowSize;

    internal IChannelHandlerContext ctx;

    /**
     * Number of buffered streams if the {@link StreamBufferingEncoder} is used.
     **/
    private int numBufferedStreams;
    private readonly Dictionary<int, DefaultHttp2FrameStream> frameStreamToInitializeMap =
            new Dictionary<int, DefaultHttp2FrameStream>(8);

    protected internal Http2FrameCodec(IHttp2ConnectionEncoder encoder, IHttp2ConnectionDecoder decoder,
                              Http2Settings initialSettings, bool decoupleCloseAndGoAway, bool flushPreface)
        : base(decoder, encoder, initialSettings, decoupleCloseAndGoAway, flushPreface)
    {

        decoder.FrameListener(new FrameListener(this));
        Connection().AddListener(new ConnectionListener(this));
        Connection().Remote().FlowController().Listener(new Http2RemoteFlowControllerListener(this));
        streamKey = Connection().NewKey();
        upgradeKey = Connection().NewKey();
        initialFlowControlWindowSize = initialSettings.InitialWindowSize();
    }

    /**
     * Creates a new outbound/local stream.
     */
    internal virtual DefaultHttp2FrameStream NewStream()
    {
        return new DefaultHttp2FrameStream();
    }

    /**
     * Iterates over all active HTTP/2 streams.
     *
     * <p>This method must not be called outside of the event loop.
     */
    internal void ForEachActiveStream(IHttp2FrameStreamVisitor streamVisitor)
    {
        Debug.Assert(ctx.Executor.InEventLoop());

        if (Connection().NumActiveStreams() > 0)
        {
            Connection().ForEachActiveStream(new FrameStreamVisitorAdapter(this, streamVisitor));
        }
    }

    // CLR: the anonymous Http2StreamVisitor of forEachActiveStream.
    private sealed class FrameStreamVisitorAdapter : IHttp2StreamVisitor
    {
        private readonly Http2FrameCodec codec;
        private readonly IHttp2FrameStreamVisitor streamVisitor;

        internal FrameStreamVisitorAdapter(Http2FrameCodec codec, IHttp2FrameStreamVisitor streamVisitor)
        {
            this.codec = codec;
            this.streamVisitor = streamVisitor;
        }

        public bool Visit(IHttp2Stream stream)
        {
            try
            {
                return streamVisitor.Visit(stream.GetProperty<IHttp2FrameStream>(codec.streamKey));
            }
            catch (Exception cause)
            {
                codec.OnError(codec.ctx, false, cause);
                return false;
            }
        }
    }

    /**
     * Retrieve the number of streams currently in the process of being initialized.
     * <p>
     * This is package-private for testing only.
     */
    internal int NumInitializingStreams()
    {
        return frameStreamToInitializeMap.Count;
    }

    public sealed override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        base.HandlerAdded(ctx);
        HandlerAdded0(ctx);
        // Must be after Http2ConnectionHandler does its initialization in handlerAdded above.
        // The server will not send a connection preface so we are good to send a window update.
        IHttp2Connection connection = Connection();
        if (connection.IsServer())
        {
            TryExpandConnectionFlowControlWindow(connection);
        }
    }

    private void TryExpandConnectionFlowControlWindow(IHttp2Connection connection)
    {
        if (initialFlowControlWindowSize != null)
        {
            // The window size in the settings explicitly excludes the connection window. So we manually manipulate the
            // connection window to accommodate more concurrent data per connection.
            IHttp2Stream connectionStream = connection.ConnectionStream();
            IHttp2LocalFlowController localFlowController = connection.Local().FlowController();
            int delta = initialFlowControlWindowSize.Value - localFlowController.InitialWindowSize(connectionStream);
            // Only increase the connection window, don't decrease it.
            if (delta > 0)
            {
                // Double the delta just so a single stream can't exhaust the connection window.
                localFlowController.IncrementWindowSize(connectionStream, Math.Max(delta << 1, delta));
                Flush(ctx);
            }
        }
    }

    internal virtual void HandlerAdded0(IChannelHandlerContext ctx)
    {
        // sub-class can override this for extra steps that needs to be done when the handler is added.
    }

    /**
     * Handles the cleartext HTTP upgrade event. If an upgrade occurred, sends a simple response via
     * HTTP/2 on stream 1 (the stream specifically reserved for cleartext HTTP upgrade).
     */
    public sealed override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt == Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.INSTANCE)
        {
            // The user event implies that we are on the client.
            TryExpandConnectionFlowControlWindow(Connection());

            // We schedule this on the EventExecutor to allow to have any extra handlers added to the pipeline
            // before we pass the event to the next handler. This is needed as the event may be called from within
            // handlerAdded(...) which will be run before other handlers will be added to the pipeline.
            ctx.Executor.Execute(() => ctx.FireUserEventTriggered(evt));
        }
        else if (evt is HttpServerUpgradeHandler.UpgradeEvent)
        {
            HttpServerUpgradeHandler.UpgradeEvent upgrade = (HttpServerUpgradeHandler.UpgradeEvent) evt;

            try
            {
                OnUpgradeEvent(ctx, upgrade.Retain());
                IHttp2Stream stream = Connection().Stream(HTTP_UPGRADE_STREAM_ID);
                if (stream.GetProperty<IHttp2FrameStream>(streamKey) == null)
                {
                    // TODO: improve handler/stream lifecycle so that stream isn't active before handler added.
                    // The stream was already made active, but ctx may have been null so it wasn't initialized.
                    // https://github.com/netty/netty/issues/4942
                    OnStreamActive0(stream);
                }

                upgrade.UpgradeRequest().Headers().SetInt(
                        HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), HTTP_UPGRADE_STREAM_ID);
                stream.SetProperty<object>(upgradeKey, true);
                InboundHttpToHttp2Adapter.Handle(
                        ctx, Connection(), Decoder().FrameListener(), (IFullHttpMessage) upgrade.UpgradeRequest().Retain());
            }
            finally
            {
                upgrade.Release();
            }
        }
        else
        {
            OnUserEventTriggered(ctx, evt);
            ctx.FireUserEventTriggered(evt);
        }
    }

    internal virtual void OnUserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        // noop
    }

    /**
     * Processes all {@link Http2Frame}s. {@link Http2StreamFrame}s may only originate in child
     * streams.
     */
    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = NewPromise();

        if (msg is IHttp2DataFrame)
        {
            IHttp2DataFrame dataFrame = (IHttp2DataFrame) msg;
            Encoder().WriteDataAsync(ctx, dataFrame.Stream().Id(), dataFrame.Content,
                    dataFrame.Padding(), dataFrame.IsEndStream(), promise);
        }
        else if (msg is IHttp2HeadersFrame)
        {
            WriteHeadersFrame(ctx, (IHttp2HeadersFrame) msg, promise);
        }
        else if (msg is IHttp2WindowUpdateFrame)
        {
            IHttp2WindowUpdateFrame frame = (IHttp2WindowUpdateFrame) msg;
            IHttp2FrameStream frameStream = frame.Stream();

            // It is legit to send a WINDOW_UPDATE frame for the connection stream. The parent channel doesn't attempt
            // to set the Http2FrameStream so we assume if it is null the WINDOW_UPDATE is for the connection stream.
            try
            {
                if (frameStream == null)
                {
                    IncreaseInitialConnectionWindow(frame.WindowSizeIncrement());
                }
                else
                {
                    ConsumeBytes(frameStream.Id(), frame.WindowSizeIncrement());
                }

                promise.SetResult();
            }
            catch (Exception t)
            {
                promise.SetException(t);
            }
        }
        else if (msg is IHttp2ResetFrame)
        {
            IHttp2ResetFrame rstFrame = (IHttp2ResetFrame) msg;
            int id = rstFrame.Stream().Id();
            // Only ever send a reset frame if stream may have existed before as otherwise we may send a RST on a
            // stream in an invalid state and cause a connection error.
            if (Connection().StreamMayHaveExisted(id))
            {
                Encoder().WriteRstStreamAsync(ctx, rstFrame.Stream().Id(), rstFrame.ErrorCode(), promise);
            }
            else
            {
                ReferenceCountUtil.Release(rstFrame);
                promise.SetException(Http2Exception.StreamError(
                        rstFrame.Stream().Id(), Http2Error.PROTOCOL_ERROR, "Stream never existed"));
            }
        }
        else if (msg is IHttp2PingFrame)
        {
            IHttp2PingFrame frame = (IHttp2PingFrame) msg;
            Encoder().WritePingAsync(ctx, frame.Ack(), frame.Content(), promise);
        }
        else if (msg is IHttp2SettingsFrame)
        {
            Encoder().WriteSettingsAsync(ctx, ((IHttp2SettingsFrame) msg).Settings(), promise);
        }
        else if (msg is IHttp2SettingsAckFrame)
        {
            // In the event of manual SETTINGS ACK, it is assumed the encoder will apply the earliest received but not
            // yet ACKed settings.
            Encoder().WriteSettingsAckAsync(ctx, promise);
        }
        else if (msg is IHttp2GoAwayFrame)
        {
            WriteGoAwayFrame(ctx, (IHttp2GoAwayFrame) msg, promise);
        }
        else if (msg is IHttp2PushPromiseFrame)
        {
            IHttp2PushPromiseFrame pushPromiseFrame = (IHttp2PushPromiseFrame) msg;
            WritePushPromise(ctx, pushPromiseFrame, promise);
        }
        else if (msg is IHttp2PriorityFrame)
        {
            IHttp2PriorityFrame priorityFrame = (IHttp2PriorityFrame) msg;
            Encoder().WritePriorityAsync(ctx, priorityFrame.Stream().Id(), priorityFrame.StreamDependency(),
                    priorityFrame.Weight(), priorityFrame.Exclusive(), promise);
        }
        else if (msg is IHttp2UnknownFrame)
        {
            IHttp2UnknownFrame unknownFrame = (IHttp2UnknownFrame) msg;
            Encoder().WriteFrameAsync(ctx, unknownFrame.FrameType(), unknownFrame.Stream().Id(),
                    unknownFrame.Flags(), unknownFrame.Content, promise);
        }
        else if (!(msg is IHttp2Frame))
        {
            Cascade(ctx, ctx.WriteAsync(msg, cancellationToken), promise);
        }
        else
        {
            ReferenceCountUtil.Release(msg);
            throw new UnsupportedMessageTypeException(msg, SUPPORTED_MESSAGES);
        }

        return promise.Task;
    }

    private void IncreaseInitialConnectionWindow(int deltaBytes)
    {
        // The LocalFlowController is responsible for detecting over/under flow.
        Connection().Local().FlowController().IncrementWindowSize(Connection().ConnectionStream(), deltaBytes);
    }

    internal bool ConsumeBytes(int streamId, int bytes)
    {
        IHttp2Stream stream = Connection().Stream(streamId);
        // Upgraded requests are ineligible for stream control. We add the null check
        // in case the stream has been deregistered.
        if (stream != null && streamId == Http2CodecUtil.HTTP_UPGRADE_STREAM_ID)
        {
            object upgraded = stream.GetProperty<object>(upgradeKey);
            if (true.Equals(upgraded))
            {
                return false;
            }
        }

        return Connection().Local().FlowController().ConsumeBytes(stream, bytes);
    }

    private void WriteGoAwayFrame(IChannelHandlerContext ctx, IHttp2GoAwayFrame frame, TaskCompletionSource promise)
    {
        if (frame.LastStreamId() > -1)
        {
            frame.Release();
            throw new ArgumentException("Last stream id must not be set on GOAWAY frame");
        }

        int lastStreamCreated = Connection().Remote().LastStreamCreated();
        long lastStreamId = lastStreamCreated + ((long) frame.ExtraStreamIds()) * 2;
        // Check if the computation overflowed.
        if (lastStreamId > int.MaxValue)
        {
            lastStreamId = int.MaxValue;
        }

        GoAwayAsync(ctx, (int) lastStreamId, frame.ErrorCode(), frame.Content, promise);
    }

    private void WriteHeadersFrame(IChannelHandlerContext ctx, IHttp2HeadersFrame headersFrame,
                                   TaskCompletionSource promise)
    {

        if (IsStreamIdValid(headersFrame.Stream().Id()))
        {
            Encoder().WriteHeadersAsync(ctx, headersFrame.Stream().Id(), headersFrame.Headers(), headersFrame.Padding(),
                    headersFrame.IsEndStream(), promise);
        }
        else if (InitializeNewStream(ctx, (DefaultHttp2FrameStream) headersFrame.Stream(), promise))
        {
            // CLR: promise.unvoid() is the promise itself (there is no void promise).

            int streamId = headersFrame.Stream().Id();

            Encoder().WriteHeadersAsync(ctx, streamId, headersFrame.Headers(), headersFrame.Padding(),
                    headersFrame.IsEndStream(), promise);

            if (!promise.Task.IsCompleted)
            {
                numBufferedStreams++;
                // Clean up the stream being initialized if writing the headers fails and also
                // decrement the number of buffered streams.
                AddListener(ctx, promise.Task, channelFuture =>
                {
                    numBufferedStreams--;
                    HandleHeaderFuture(channelFuture, streamId);
                });
            }
            else
            {
                HandleHeaderFuture(promise.Task, streamId);
            }
        }
    }

    private void WritePushPromise(IChannelHandlerContext ctx, IHttp2PushPromiseFrame pushPromiseFrame,
                                  TaskCompletionSource promise)
    {
        if (IsStreamIdValid(pushPromiseFrame.PushStream().Id()))
        {
            Encoder().WritePushPromiseAsync(ctx, pushPromiseFrame.Stream().Id(), pushPromiseFrame.PushStream().Id(),
                    pushPromiseFrame.Http2Headers(), pushPromiseFrame.Padding(), promise);
        }
        else if (InitializeNewStream(ctx, (DefaultHttp2FrameStream) pushPromiseFrame.PushStream(), promise))
        {
            int streamId = pushPromiseFrame.Stream().Id();
            Encoder().WritePushPromiseAsync(ctx, streamId, pushPromiseFrame.PushStream().Id(),
                    pushPromiseFrame.Http2Headers(), pushPromiseFrame.Padding(), promise);

            if (promise.Task.IsCompleted)
            {
                HandleHeaderFuture(promise.Task, streamId);
            }
            else
            {
                numBufferedStreams++;
                // Clean up the stream being initialized if writing the headers fails and also
                // decrement the number of buffered streams.
                AddListener(ctx, promise.Task, channelFuture =>
                {
                    numBufferedStreams--;
                    HandleHeaderFuture(channelFuture, streamId);
                });
            }
        }
    }

    private bool InitializeNewStream(IChannelHandlerContext ctx, DefaultHttp2FrameStream http2FrameStream,
                                        TaskCompletionSource promise)
    {
        IHttp2Connection connection = Connection();
        int streamId = connection.Local().IncrementAndGetNextStreamId();
        if (streamId < 0)
        {
            promise.SetException(new Http2NoMoreStreamIdsException());

            // Simulate a GOAWAY being received due to stream exhaustion on this connection. We use the maximum
            // valid stream ID for the current peer.
            OnHttp2Frame(ctx, new DefaultHttp2GoAwayFrame(connection.IsServer() ? int.MaxValue :
                    int.MaxValue - 1, NO_ERROR.Code(),
                    ByteBufUtil.WriteAscii(ctx.Alloc(), "Stream IDs exhausted on local stream creation")));

            return false;
        }

        http2FrameStream.id = streamId;

        // Use a Map to store all pending streams as we may have multiple. This is needed as if we would store the
        // stream in a field directly we may override the stored field before onStreamAdded(...) was called
        // and so not correctly set the property for the buffered stream.
        //
        // See https://github.com/netty/netty/issues/8692
        frameStreamToInitializeMap.TryGetValue(streamId, out DefaultHttp2FrameStream old);
        frameStreamToInitializeMap[streamId] = http2FrameStream;

        // We should not re-use ids.
        Debug.Assert(old == null);
        return true;
    }

    private void HandleHeaderFuture(Task channelFuture, int streamId)
    {
        if (!channelFuture.IsCompletedSuccessfully)
        {
            frameStreamToInitializeMap.Remove(streamId);
        }
    }

    private void OnStreamActive0(IHttp2Stream stream)
    {
        if (stream.Id() != Http2CodecUtil.HTTP_UPGRADE_STREAM_ID &&
                Connection().Local().IsValidStreamId(stream.Id()))
        {
            return;
        }

        DefaultHttp2FrameStream stream2 = NewStream().SetStreamAndProperty(streamKey, stream);
        OnHttp2StreamStateChanged(ctx, stream2);
    }

    // CLR: the inner class reaches the codec through the constructor argument.
    private sealed class ConnectionListener : Http2ConnectionAdapter
    {
        private readonly Http2FrameCodec codec;

        internal ConnectionListener(Http2FrameCodec codec)
        {
            this.codec = codec;
        }

        public override void OnStreamAdded(IHttp2Stream stream)
        {
            codec.frameStreamToInitializeMap.Remove(stream.Id(), out DefaultHttp2FrameStream frameStream);

            if (frameStream != null)
            {
                frameStream.SetStreamAndProperty(codec.streamKey, stream);
            }
        }

        public override void OnStreamActive(IHttp2Stream stream)
        {
            codec.OnStreamActive0(stream);
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            OnHttp2StreamStateChanged0(stream);
        }

        public override void OnStreamHalfClosed(IHttp2Stream stream)
        {
            OnHttp2StreamStateChanged0(stream);
        }

        private void OnHttp2StreamStateChanged0(IHttp2Stream stream)
        {
            DefaultHttp2FrameStream stream2 = stream.GetProperty<DefaultHttp2FrameStream>(codec.streamKey);
            if (stream2 != null)
            {
                codec.OnHttp2StreamStateChanged(codec.ctx, stream2);
            }
        }
    }

    protected internal override void OnConnectionError(
            IChannelHandlerContext ctx, bool outbound, Exception cause, Http2Exception http2Ex)
    {
        if (!outbound)
        {
            // allow the user to handle it first in the pipeline, and then automatically clean up.
            // If this is not desired behavior the user can override this method.
            //
            // We only forward non outbound errors as outbound errors will already be reflected by failing the promise.
            ctx.FireExceptionCaught(cause);
        }

        base.OnConnectionError(ctx, outbound, cause, http2Ex);
    }

    /**
     * Exceptions for unknown streams, that is streams that have no {@link Http2FrameStream} object attached
     * are simply logged and replied to by sending a RST_STREAM frame.
     */
    protected sealed override void OnStreamError(IChannelHandlerContext ctx, bool outbound, Exception cause,
                                       Http2Exception.StreamException streamException)
    {
        int streamId = streamException.StreamId();
        IHttp2Stream connectionStream = Connection().Stream(streamId);
        if (connectionStream == null)
        {
            OnHttp2UnknownStreamError(ctx, cause, streamException);
            // Write a RST_STREAM
            base.OnStreamError(ctx, outbound, cause, streamException);
            return;
        }

        IHttp2FrameStream stream = connectionStream.GetProperty<IHttp2FrameStream>(streamKey);
        if (stream == null)
        {
            LOG.Warn("{} Stream exception thrown without stream object attached.", ctx.Channel, cause);
            // Write a RST_STREAM
            base.OnStreamError(ctx, outbound, cause, streamException);
            return;
        }

        if (!outbound)
        {
            // We only forward non outbound errors as outbound errors will already be reflected by failing the promise.
            OnHttp2FrameStreamException(ctx, new Http2FrameStreamException(stream, streamException.Error(), cause));
        }
    }

    private static void OnHttp2UnknownStreamError(IChannelHandlerContext ctx,
            Exception cause, Http2Exception.StreamException streamException)
    {
        // We log here for debugging purposes. This exception will be propagated to the upper layers through other ways:
        // - fireExceptionCaught
        // - fireUserEventTriggered(Http2ResetFrame), see Http2MultiplexHandler#channelRead(...)
        // - by failing write promise
        // Receiver of the error is responsible for correct handling of this exception.
        LOG.Log(InternalLogLevel.DEBUG, "{} Stream exception thrown for unknown stream {}.",
                ctx.Channel, streamException.StreamId(), cause);
    }

    protected sealed override bool IsGracefulShutdownComplete()
    {
        return base.IsGracefulShutdownComplete() && numBufferedStreams == 0;
    }

    // CLR: the inner class reaches the codec through the constructor argument.
    private sealed class FrameListener : IHttp2FrameListener
    {
        private readonly Http2FrameCodec codec;

        internal FrameListener(Http2FrameCodec codec)
        {
            this.codec = codec;
        }

        public void OnUnknownFrame(
                IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags, ByteBuf payload)
        {
            if (streamId == 0)
            {
                // Ignore unknown frames on connection stream, for example: HTTP/2 GREASE testing
                return;
            }

            IHttp2FrameStream stream = RequireStream(streamId);
            codec.OnHttp2Frame(ctx, codec.NewHttp2UnknownFrame(frameType, streamId, flags, payload.Retain()).Stream(stream));
        }

        public void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
        {
            codec.OnHttp2Frame(ctx, new DefaultHttp2SettingsFrame(settings));
        }

        public void OnPingRead(IChannelHandlerContext ctx, long data)
        {
            codec.OnHttp2Frame(ctx, new DefaultHttp2PingFrame(data, false));
        }

        public void OnPingAckRead(IChannelHandlerContext ctx, long data)
        {
            codec.OnHttp2Frame(ctx, new DefaultHttp2PingFrame(data, true));
        }

        public void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
        {
            IHttp2FrameStream stream = RequireStream(streamId);
            codec.OnHttp2Frame(ctx, new DefaultHttp2ResetFrame(errorCode).Stream(stream));
        }

        public void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
        {
            if (streamId == 0)
            {
                // Ignore connection window updates.
                return;
            }

            IHttp2FrameStream stream = RequireStream(streamId);
            codec.OnHttp2Frame(ctx, new DefaultHttp2WindowUpdateFrame(windowSizeIncrement).Stream(stream));
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId,
                                  IHttp2Headers headers, int streamDependency, short weight, bool
                                          exclusive, int padding, bool endStream)
        {
            OnHeadersRead(ctx, streamId, headers, padding, endStream);
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                                  int padding, bool endOfStream)
        {
            IHttp2FrameStream stream = RequireStream(streamId);
            codec.OnHttp2Frame(ctx, new DefaultHttp2HeadersFrame(headers, endOfStream, padding).Stream(stream));
        }

        public int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
                              bool endOfStream)
        {
            IHttp2FrameStream stream = RequireStream(streamId);
            IHttp2DataFrame dataframe;

            try
            {
                dataframe = new DefaultHttp2DataFrame(data.Retain(), endOfStream, padding);
            }
            catch (ArgumentException)
            {
                // Might be thrown in case of invalid padding / length.
                data.Release();
                throw;
            }

            dataframe.Stream(stream);
            codec.OnHttp2Frame(ctx, dataframe);
            // We return the bytes in consumeBytes() once the stream channel consumed the bytes.
            return 0;
        }

        public void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
        {
            codec.OnHttp2Frame(ctx, new DefaultHttp2GoAwayFrame(lastStreamId, errorCode, debugData.Retain()));
        }

        public void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency,
                                   short weight, bool exclusive)
        {

            IHttp2Stream stream = codec.Connection().Stream(streamId);
            if (stream == null)
            {
                // The stream was not opened yet, let's just ignore this for now.
                return;
            }

            IHttp2FrameStream frameStream = RequireStream(streamId);
            codec.OnHttp2Frame(ctx, new DefaultHttp2PriorityFrame(streamDependency, weight, exclusive)
                    .Stream(frameStream));
        }

        public void OnSettingsAckRead(IChannelHandlerContext ctx)
        {
            codec.OnHttp2Frame(ctx, IHttp2SettingsAckFrame.INSTANCE);
        }

        public void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                                      IHttp2Headers headers, int padding)
        {
            IHttp2FrameStream stream = RequireStream(streamId);
            codec.OnHttp2Frame(ctx, new DefaultHttp2PushPromiseFrame(headers, padding, promisedStreamId)
                    .PushStream(new DefaultHttp2FrameStream()
                            .SetStreamAndProperty(codec.streamKey, codec.Connection().Stream(promisedStreamId)))
                    .Stream(stream));
        }

        private IHttp2FrameStream RequireStream(int streamId)
        {
            IHttp2FrameStream stream = codec.Connection().Stream(streamId).GetProperty<IHttp2FrameStream>(codec.streamKey);
            if (stream == null)
            {
                throw new InvalidOperationException("Stream object required for identifier: " +
                        streamId.ToString(CultureInfo.InvariantCulture));
            }

            return stream;
        }
    }

    private void OnUpgradeEvent(IChannelHandlerContext ctx, HttpServerUpgradeHandler.UpgradeEvent evt)
    {
        ctx.FireUserEventTriggered(evt);
    }

    private void OnHttp2StreamWritabilityChanged(IChannelHandlerContext ctx, DefaultHttp2FrameStream stream,
                                                 bool writable)
    {
        ctx.FireUserEventTriggered(stream.writabilityChanged);
    }

    internal virtual void OnHttp2StreamStateChanged(IChannelHandlerContext ctx, DefaultHttp2FrameStream stream)
    {
        ctx.FireUserEventTriggered(stream.stateChanged);
    }

    internal virtual void OnHttp2Frame(IChannelHandlerContext ctx, IHttp2Frame frame)
    {
        ctx.FireChannelRead(frame);
    }

    /**
     * Create a Http2UnknownFrame. The ownership of the {@link ByteBuf} is transferred.
     * */
    protected virtual IHttp2StreamFrame NewHttp2UnknownFrame(byte frameType, int streamId, Http2Flags flags, ByteBuf payload)
    {
        return new DefaultHttp2UnknownFrame(frameType, flags, payload);
    }

    internal virtual void OnHttp2FrameStreamException(IChannelHandlerContext ctx, Http2FrameStreamException cause)
    {
        ctx.FireExceptionCaught(cause);
    }

    // CLR: the inner class reaches the codec through the constructor argument.
    private sealed class Http2RemoteFlowControllerListener : IHttp2RemoteFlowController.IListener
    {
        private readonly Http2FrameCodec codec;

        internal Http2RemoteFlowControllerListener(Http2FrameCodec codec)
        {
            this.codec = codec;
        }

        public void WritabilityChanged(IHttp2Stream stream)
        {
            DefaultHttp2FrameStream frameStream = stream.GetProperty<DefaultHttp2FrameStream>(codec.streamKey);
            if (frameStream == null)
            {
                return;
            }

            codec.OnHttp2StreamWritabilityChanged(
                    codec.ctx, frameStream, codec.Connection().Remote().FlowController().IsWritable(stream));
        }
    }

    /**
     * {@link Http2FrameStream} implementation.
     */
    // TODO(buchgr): Merge Http2FrameStream and Http2Stream.
    // CLR: the package-private static nested class is internal; its fields are internal because C# does not let the
    // enclosing class read private members of a nested class (Java does). State is IHttp2Stream.State.
    internal class DefaultHttp2FrameStream : IHttp2FrameStream
    {

        internal volatile int id = -1;
        private volatile IHttp2Stream stream;

        internal readonly Http2FrameStreamEvent stateChanged;
        internal readonly Http2FrameStreamEvent writabilityChanged;

        internal IChannel attachment;

        internal DefaultHttp2FrameStream()
        {
            // CLR: a field initializer cannot use this ("Keyword 'this' is not available in the current context",
            // https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0027), so the events are created here.
            stateChanged = Http2FrameStreamEvent.StateChanged(this);
            writabilityChanged = Http2FrameStreamEvent.WritabilityChanged(this);
        }

        internal DefaultHttp2FrameStream SetStreamAndProperty(IHttp2Connection.IPropertyKey streamKey, IHttp2Stream stream)
        {
            Debug.Assert(id == -1 || stream.Id() == id);
            this.stream = stream;
            this.id = stream.Id();
            stream.SetProperty(streamKey, this);
            return this;
        }

        public int Id()
        {
            IHttp2Stream stream = this.stream;
            return stream == null ? id : stream.Id();
        }

        public IHttp2Stream.State State()
        {
            IHttp2Stream stream = this.stream;
            return stream == null ? IHttp2Stream.State.IDLE : stream.State_();
        }

        public override string ToString()
        {
            return Id().ToString(CultureInfo.InvariantCulture);
        }
    }
}
