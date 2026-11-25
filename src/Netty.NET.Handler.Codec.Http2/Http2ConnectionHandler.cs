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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.ByteBufUtil;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides the default implementation for processing inbound frame events and delegates to a
 * {@link Http2FrameListener}
 * <p>
 * This class will read HTTP/2 frames and delegate the events to a {@link Http2FrameListener}
 * <p>
 * This interface enforces inbound flow control functionality through
 * {@link Http2LocalFlowController}
 */
// CLR: ByteToMessageDecoder is the codec-base ByteToMessageDecoder and ChannelOutboundHandler the Transport's
// IChannelOutboundHandler, whose operations return a Task instead of completing a ChannelPromise argument
// (docs/codec-base-porting.md, Outbound operations): bind/connect/close create the TaskCompletionSource that the
// handler completes and return its Task (as handler's SslHandler and codec-http's SpdySessionHandler do), and the
// CancellationToken is passed on only where the operation goes to the context unchanged. ChannelPromise is
// TaskCompletionSource and ChannelFuture is Task (docs/codec-http-porting.md, Shared CLR adaptations);
// ctx.newPromise()/ctx.voidPromise() are Http2CodecUtil.NewPromise(), promise.unvoid() is the promise itself (there is
// no void promise, docs/transport-porting.md), ctx.write(msg, promise)/ctx.close(promise) are Http2CodecUtil.Write/
// Close, and ctx.newSucceededFuture() is Task.CompletedTask. A ChannelFutureListener is an Action<Task> run through
// Http2CodecUtil.AddListener, a continuation on Common's EventExecutorTaskScheduler over ctx.Executor, which keeps
// DefaultPromise's notification timing (docs/codec-http-porting.md, Listener timing). closeStream(...) adds a
// listener although Java has no context at hand there; the context of handlerAdded is kept in the CLR-only field
// listenerCtx for that. ChannelFutureListener.CLOSE_ON_FAILURE closes ctx.Channel, the channel the operation ran on,
// because a Task has no channel(). The switches on Http2Stream.State are if/else chains on reference equality
// (IHttp2Stream.State is a class with static readonly instances). The lambdas that implement Http2StreamVisitor and
// Http2ConnectionAdapter are the private nested PendingDataVisitor and StreamClosedListener. Throwable is Exception,
// IllegalArgumentException ArgumentException and IllegalStateException InvalidOperationException
// (docs/codec-base-porting.md); String.valueOf of a number in an exception text uses the invariant culture.
// Set<Integer> is HashSet<int>. The inner classes BaseDecoder, PrefaceDecoder, FrameDecoder and PrefaceSendListener
// get the handler instance, which Java's inner classes reach implicitly. The protected constructors and
// onConnectionError(...) are protected internal, because Http2ConnectionHandlerBuilder and the tests reach them through
// Java's package access (docs/codec-http-porting.md, Package access).
public class Http2ConnectionHandler : ByteToMessageDecoder, IHttp2LifecycleManager, IChannelOutboundHandler
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Http2ConnectionHandler));

    private static readonly IHttp2Headers HEADERS_TOO_LARGE_HEADERS = ReadOnlyHttp2Headers.ServerHeaders(false,
            HttpResponseStatus.REQUEST_HEADER_FIELDS_TOO_LARGE.CodeAsText());
    private static readonly ByteBuf HTTP_1_X_BUF = UnreleasableBuffer(
        WrappedBuffer(new byte[] {(byte) 'H', (byte) 'T', (byte) 'T', (byte) 'P', (byte) '/', (byte) '1', (byte) '.'}))
        .AsReadOnly();

    private readonly IHttp2ConnectionDecoder decoder;
    private readonly IHttp2ConnectionEncoder encoder;
    private readonly Http2Settings initialSettings;
    private readonly bool decoupleCloseAndGoAway;
    private readonly bool flushPreface;
    private Action<Task> closeListener;
    private BaseDecoder byteDecoder;
    private long gracefulShutdownTimeoutMillis;
    private bool inFlush;
    private bool flushAgain;
    // CLR: the context of handlerAdded, used to notify the closeStream(...) listener on the event loop (see the type
    // comment).
    private IChannelHandlerContext listenerCtx;

    protected internal Http2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                     Http2Settings initialSettings) : this(decoder, encoder, initialSettings, false)
    {
    }

    protected internal Http2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                     Http2Settings initialSettings, bool decoupleCloseAndGoAway)
        : this(decoder, encoder, initialSettings, decoupleCloseAndGoAway, true)
    {
    }

    protected internal Http2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                     Http2Settings initialSettings, bool decoupleCloseAndGoAway,
                                     bool flushPreface)
    {
        this.initialSettings = CheckNotNull(initialSettings, "initialSettings");
        this.decoder = CheckNotNull(decoder, "decoder");
        this.encoder = CheckNotNull(encoder, "encoder");
        this.decoupleCloseAndGoAway = decoupleCloseAndGoAway;
        this.flushPreface = flushPreface;
        if (encoder.Connection() != decoder.Connection())
        {
            throw new ArgumentException("Encoder and Decoder do not share the same connection object");
        }
    }

    /**
     * Get the amount of time (in milliseconds) this endpoint will wait for all streams to be closed before closing
     * the connection during the graceful shutdown process. Returns -1 if this connection is configured to wait
     * indefinitely for all streams to close.
     */
    public long GracefulShutdownTimeoutMillis()
    {
        return gracefulShutdownTimeoutMillis;
    }

    /**
     * Set the amount of time (in milliseconds) this endpoint will wait for all streams to be closed before closing
     * the connection during the graceful shutdown process.
     * @param gracefulShutdownTimeoutMillis the amount of time (in milliseconds) this endpoint will wait for all
     * streams to be closed before closing the connection during the graceful shutdown process.
     */
    public void GracefulShutdownTimeoutMillis(long gracefulShutdownTimeoutMillis)
    {
        if (gracefulShutdownTimeoutMillis < -1)
        {
            throw new ArgumentException("gracefulShutdownTimeoutMillis: " + ToJavaString(gracefulShutdownTimeoutMillis) +
                                               " (expected: -1 for indefinite or >= 0)");
        }
        this.gracefulShutdownTimeoutMillis = gracefulShutdownTimeoutMillis;
    }

    public IHttp2Connection Connection()
    {
        return encoder.Connection();
    }

    public IHttp2ConnectionDecoder Decoder()
    {
        return decoder;
    }

    public IHttp2ConnectionEncoder Encoder()
    {
        return encoder;
    }

    private bool PrefaceSent()
    {
        return byteDecoder != null && byteDecoder.PrefaceSent();
    }

    /**
     * Handles the client-side (cleartext) upgrade from HTTP to HTTP/2.
     * Reserves local stream 1 for the HTTP/2 response.
     */
    public virtual void OnHttpClientUpgrade()
    {
        if (Connection().IsServer())
        {
            throw ConnectionError(PROTOCOL_ERROR, "Client-side HTTP upgrade requested for a server");
        }
        if (!PrefaceSent())
        {
            // If the preface was not sent yet it most likely means the handler was not added to the pipeline before
            // calling this method.
            throw ConnectionError(INTERNAL_ERROR, "HTTP upgrade must occur after preface was sent");
        }
        if (decoder.PrefaceReceived())
        {
            throw ConnectionError(PROTOCOL_ERROR, "HTTP upgrade must occur before HTTP/2 preface is received");
        }

        // Create a local stream used for the HTTP cleartext upgrade.
        Connection().Local().CreateStream(HTTP_UPGRADE_STREAM_ID, true);
    }

    /**
     * Handles the server-side (cleartext) upgrade from HTTP to HTTP/2.
     * @param settings the settings for the remote endpoint.
     */
    public virtual void OnHttpServerUpgrade(Http2Settings settings)
    {
        if (!Connection().IsServer())
        {
            throw ConnectionError(PROTOCOL_ERROR, "Server-side HTTP upgrade requested for a client");
        }
        if (!PrefaceSent())
        {
            // If the preface was not sent yet it most likely means the handler was not added to the pipeline before
            // calling this method.
            throw ConnectionError(INTERNAL_ERROR, "HTTP upgrade must occur after preface was sent");
        }
        if (decoder.PrefaceReceived())
        {
            throw ConnectionError(PROTOCOL_ERROR, "HTTP upgrade must occur before HTTP/2 preface is received");
        }

        // Apply the settings but no ACK is necessary.
        encoder.RemoteSettings(settings);

        // Create a stream in the half-closed state.
        Connection().Remote().CreateStream(HTTP_UPGRADE_STREAM_ID, true);
    }

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        if (inFlush)
        {
            // Reentrant flush (e.g. from a synchronous writability change) must not recurse — that livelocks
            // (#17256); the in-progress flush loop picks it up.
            flushAgain = true;
            return;
        }
        inFlush = true;
        try
        {
            do
            {
                flushAgain = false;
                // Trigger pending writes in the remote flow controller.
                encoder.FlowController().WritePendingBytes();
                ctx.Flush();
                // Honor any flush re-requested while we were flushing (a resumed write, or a direct flush from
                // elsewhere in the pipeline) regardless of writability — flushing already-written data doesn't
                // need a writable channel.
            } while (flushAgain);
        }
        catch (Http2Exception e)
        {
            OnError(ctx, true, e);
        }
        catch (Exception cause)
        {
            OnError(ctx, true, ConnectionError(INTERNAL_ERROR, cause, "Error flushing"));
        }
        finally
        {
            inFlush = false;
        }
    }

    private bool HasPendingData()
    {
        IHttp2RemoteFlowController flowController = encoder.FlowController();
        try
        {
            // Stop at the first stream that still has a flow-controlled frame queued. Frame-based, so it counts
            // zero-length frames (e.g. trailing headers) that carry no flow-control bytes.
            return Connection().ForEachActiveStream(new PendingDataVisitor(flowController)) != null;
        }
        catch (Http2Exception)
        {
            return false;
        }
    }

    // CLR: the lambda stream -> !flowController.hasFlowControlled(stream) of hasPendingData().
    private sealed class PendingDataVisitor : IHttp2StreamVisitor
    {
        private readonly IHttp2RemoteFlowController flowController;

        internal PendingDataVisitor(IHttp2RemoteFlowController flowController)
        {
            this.flowController = flowController;
        }

        public bool Visit(IHttp2Stream stream)
        {
            return !flowController.HasFlowControlled(stream);
        }
    }

    private abstract class BaseDecoder
    {
        protected readonly Http2ConnectionHandler handler;

        protected BaseDecoder(Http2ConnectionHandler handler)
        {
            this.handler = handler;
        }

        public abstract void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out);
        public virtual void HandlerRemoved(IChannelHandlerContext ctx) { }
        public virtual void ChannelActive(IChannelHandlerContext ctx) { }

        public virtual void ChannelInactive(IChannelHandlerContext ctx)
        {
            // Connection has terminated, close the encoder and decoder.
            handler.Encoder().Close();
            handler.Decoder().Close();

            // We need to remove all streams (not just the active ones).
            // See https://github.com/netty/netty/issues/4838.
            _ = handler.Connection().CloseAsync(NewPromise());
        }

        /**
         * Determine if the HTTP/2 connection preface been sent.
         */
        public virtual bool PrefaceSent()
        {
            return true;
        }

        /**
         * Send the preface if needed.
         *
         * @param ctx           the {@link ChannelHandlerContext} to use.
         * @throws Exception    thrown on error.
         */
        public virtual void SendPrefaceIfNeeded(IChannelHandlerContext ctx)
        {
            // Noop by default.
        }
    }

    private sealed class PrefaceDecoder : BaseDecoder
    {
        private ByteBuf clientPrefaceString;
        private bool prefaceSent;

        internal PrefaceDecoder(Http2ConnectionHandler handler, IChannelHandlerContext ctx) : base(handler)
        {
            clientPrefaceString = ClientPrefaceString(handler.encoder.Connection());
            // This handler was just added to the context. In case it was handled after
            // the connection became active, send the connection preface now.
            SendPrefaceIfNeeded(ctx);
        }

        public override bool PrefaceSent()
        {
            return prefaceSent;
        }

        public override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            try
            {
                if (ctx.Channel.IsActive && ReadClientPrefaceString(@in) && VerifyFirstFrameIsSettings(@in))
                {
                    // After the preface is read, it is time to hand over control to the post initialized decoder.
                    handler.byteDecoder = new FrameDecoder(handler);
                    handler.byteDecoder.Decode(ctx, @in, @out);
                }
            }
            catch (Exception e)
            {
                if (handler.byteDecoder != null)
                {
                    // Skip all bytes before we report the exception as
                    @in.SkipBytes(@in.ReadableBytes);
                }
                handler.OnError(ctx, false, e);
            }
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // The channel just became active - send the connection preface to the remote endpoint.
            SendPrefaceIfNeeded(ctx);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            Cleanup();
            base.ChannelInactive(ctx);
        }

        /**
         * Releases the {@code clientPrefaceString}. Any active streams will be left in the open.
         */
        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            Cleanup();
        }

        /**
         * Releases the {@code clientPrefaceString}. Any active streams will be left in the open.
         */
        private void Cleanup()
        {
            if (clientPrefaceString != null)
            {
                clientPrefaceString.Release();
                clientPrefaceString = null;
            }
        }

        /**
         * Decodes the client connection preface string from the input buffer.
         *
         * @return {@code true} if processing of the client preface string is complete. Since client preface strings can
         *         only be received by servers, returns true immediately for client endpoints.
         */
        // CLR: in.toString(index, length, US_ASCII) is ToString(index, length, Encoding.ASCII).
        private bool ReadClientPrefaceString(ByteBuf @in)
        {
            if (clientPrefaceString == null)
            {
                return true;
            }

            int prefaceRemaining = clientPrefaceString.ReadableBytes;
            int bytesRead = Math.Min(@in.ReadableBytes, prefaceRemaining);

            // If the input so far doesn't match the preface, break the connection.
            if (bytesRead == 0 || !ByteBufUtil.Equals(@in, @in.ReaderIndex,
                                                      clientPrefaceString, clientPrefaceString.ReaderIndex,
                                                      bytesRead))
            {
                int maxSearch = 1024; // picked because 512 is too little, and 2048 too much
                int http1Index =
                    ByteBufUtil.IndexOf(HTTP_1_X_BUF, @in.Slice(@in.ReaderIndex, Math.Min(@in.ReadableBytes, maxSearch)));
                if (http1Index != -1)
                {
                    string chunk = @in.ToString(@in.ReaderIndex, http1Index - @in.ReaderIndex, Encoding.ASCII);
                    throw ConnectionError(PROTOCOL_ERROR, "Unexpected HTTP/1.x request: %s", chunk);
                }
                string receivedBytes = HexDump(@in, @in.ReaderIndex,
                                               Math.Min(@in.ReadableBytes, clientPrefaceString.ReadableBytes));
                throw ConnectionError(PROTOCOL_ERROR, "HTTP/2 client preface string missing or corrupt. " +
                                                      "Hex dump for received bytes: %s", receivedBytes);
            }
            @in.SkipBytes(bytesRead);
            clientPrefaceString.SkipBytes(bytesRead);

            if (!clientPrefaceString.IsReadable())
            {
                // Entire preface has been read.
                clientPrefaceString.Release();
                clientPrefaceString = null;
                return true;
            }
            return false;
        }

        /**
         * Peeks at that the next frame in the buffer and verifies that it is a non-ack {@code SETTINGS} frame.
         *
         * @param in the inbound buffer.
         * @return {@code true} if the next frame is a non-ack {@code SETTINGS} frame, {@code false} if more
         * data is required before we can determine the next frame type.
         * @throws Http2Exception thrown if the next frame is NOT a non-ack {@code SETTINGS} frame.
         */
        // CLR: getUnsignedByte is GetByte, which returns the unsigned octet (docs/buffer-porting.md).
        private static bool VerifyFirstFrameIsSettings(ByteBuf @in)
        {
            if (@in.ReadableBytes < 5)
            {
                // Need more data before we can see the frame type for the first frame.
                return false;
            }

            short frameType = @in.GetByte(@in.ReaderIndex + 3);
            if (frameType != Http2FrameTypes.SETTINGS)
            {
                throw ConnectionError(PROTOCOL_ERROR, "First received frame was not SETTINGS. " +
                                                      "Hex dump for first 5 bytes: %s",
                                      HexDump(@in, @in.ReaderIndex, 5));
            }
            short flags = @in.GetByte(@in.ReaderIndex + 4);
            if ((flags & Http2Flags.ACK) != 0)
            {
                throw ConnectionError(PROTOCOL_ERROR, "First received frame was SETTINGS frame but had ACK flag set. " +
                        "Hex dump for first 5 bytes: %s",
                        HexDump(@in, @in.ReaderIndex, 5));
            }
            return true;
        }

        /**
         * Sends the HTTP/2 connection preface upon establishment of the connection, if not already sent.
         */
        public override void SendPrefaceIfNeeded(IChannelHandlerContext ctx)
        {
            if (prefaceSent || !ctx.Channel.IsActive)
            {
                return;
            }

            prefaceSent = true;

            bool isClient = !handler.Connection().IsServer();
            if (isClient)
            {
                // Clients must send the preface string as the first bytes on the connection.
                AddListener(ctx, ctx.WriteAsync(ConnectionPrefaceBuf()), f => CloseOnFailure(ctx, f));
            }

            // Both client and server must send their initial settings.
            AddListener(ctx, handler.encoder.WriteSettingsAsync(ctx, handler.initialSettings, NewPromise()),
                    f => CloseOnFailure(ctx, f));

            try
            {
                if (isClient)
                {
                    // If this handler is extended by the user and we directly fire the userEvent from this context then
                    // the user will not see the event. We should fire the event starting with this handler so this
                    // class (and extending classes) have a chance to process the event.
                    handler.UserEventTriggered(ctx, Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.INSTANCE);
                }
            }
            finally
            {
                if (handler.flushPreface)
                {
                    // As we don't know if any channelReadComplete() events will be triggered at all we need to ensure
                    // we also flush. Otherwise the remote peer might never see the preface / settings frame.
                    // See https://github.com/netty/netty/issues/12089
                    ctx.Flush();
                }
            }
        }
    }

    private sealed class FrameDecoder : BaseDecoder
    {
        internal FrameDecoder(Http2ConnectionHandler handler) : base(handler)
        {
        }

        public override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            try
            {
                handler.decoder.DecodeFrame(ctx, @in, @out);
            }
            catch (Exception e)
            {
                handler.OnError(ctx, false, e);
            }
        }
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        listenerCtx = ctx;
        // Initialize the encoder, decoder, flow controllers, and internal state.
        encoder.LifecycleManager(this);
        decoder.LifecycleManager(this);
        encoder.FlowController().ChannelHandlerContext(ctx);
        decoder.FlowController().ChannelHandlerContext(ctx);
        byteDecoder = new PrefaceDecoder(this, ctx);
        // Streams are not only closed via closeStream(...): the connection also closes them itself, for example
        // when a GOAWAY is received or the channel becomes inactive. Check if a pending graceful shutdown can
        // complete whenever a stream is closed, whatever the reason.
        Connection().AddListener(new StreamClosedListener(this));
    }

    // CLR: the anonymous Http2ConnectionAdapter of handlerAdded(...).
    private sealed class StreamClosedListener : Http2ConnectionAdapter
    {
        private readonly Http2ConnectionHandler handler;

        internal StreamClosedListener(Http2ConnectionHandler handler)
        {
            this.handler = handler;
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            handler.CheckCloseConnection(Task.CompletedTask);
        }
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        if (byteDecoder != null)
        {
            byteDecoder.HandlerRemoved(ctx);
            byteDecoder = null;
        }
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (byteDecoder == null)
        {
            byteDecoder = new PrefaceDecoder(this, ctx);
        }
        byteDecoder.ChannelActive(ctx);
        base.ChannelActive(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        // Call super class first, as this may result in decode being called.
        base.ChannelInactive(ctx);
        if (byteDecoder != null)
        {
            byteDecoder.ChannelInactive(ctx);
            byteDecoder = null;
        }
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        try
        {
            // Only flush on a writability change if the flow controller has frames queued. A toggle with
            // nothing to write (e.g. SslHandler during setup) must not flush, or the flush -> writability ->
            // flush cycle livelocks (#17256).
            if (ctx.Channel.IsWritable && HasPendingData())
            {
                Flush(ctx);
            }
            encoder.FlowController().ChannelWritabilityChanged();
        }
        finally
        {
            base.ChannelWritabilityChanged(ctx);
        }
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        byteDecoder.Decode(ctx, @in, @out);
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        // Ensure we send the preface before we notify the bind promise as the user might try to write
        // directly in the listener attached to the promise and we need to ensure the preface is always the first
        // thing that is written.
        TaskCompletionSource promise = NewPromise();
        AddListener(ctx, ctx.BindAsync(localAddress, cancellationToken),
                new PrefaceSendListener(this, ctx, promise).OperationComplete);
        return promise.Task;
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                        CancellationToken cancellationToken)
    {
        // Ensure we send the preface before we notify the connect promise as the user might try to write
        // directly in the listener attached to the promise and we need to ensure the preface is always the first
        // thing that is written.
        TaskCompletionSource promise = NewPromise();
        AddListener(ctx, ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken),
                new PrefaceSendListener(this, ctx, promise).OperationComplete);
        return promise.Task;
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (decoupleCloseAndGoAway)
        {
            return ctx.CloseAsync(cancellationToken);
        }
        TaskCompletionSource promise = NewPromise();
        // Avoid NotYetConnectedException and avoid sending before connection preface
        if (!ctx.Channel.IsActive || !PrefaceSent())
        {
            return Close(ctx, promise);
        }

        // If the user has already sent a GO_AWAY frame they may be attempting to do a graceful shutdown which requires
        // sending multiple GO_AWAY frames. We should only send a GO_AWAY here if one has not already been sent. If
        // a GO_AWAY has been sent we send a empty buffer just so we can wait to close until all other data has been
        // flushed to the OS.
        // https://github.com/netty/netty/issues/5307
        Task f = Connection().GoAwaySent() ? ctx.WriteAsync(EmptyBuffer) : GoAwayAsync(ctx, null, NewPromise());
        ctx.Flush();
        DoGracefulShutdown(ctx, f, promise);
        return promise.Task;
    }

    // CLR: the ChannelFutureListener is the OperationComplete method of the ClosingChannelFutureListener.
    private Action<Task> NewClosingChannelFutureListener(
            IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        long gracefulShutdownTimeoutMillis = this.gracefulShutdownTimeoutMillis;
        return gracefulShutdownTimeoutMillis < 0 ?
                new ClosingChannelFutureListener(ctx, promise).OperationComplete :
                new ClosingChannelFutureListener(ctx, promise, TimeSpan.FromMilliseconds(gracefulShutdownTimeoutMillis))
                        .OperationComplete;
    }

    private void DoGracefulShutdown(IChannelHandlerContext ctx, Task future, TaskCompletionSource promise)
    {
        Action<Task> listener = NewClosingChannelFutureListener(ctx, promise);
        if (IsGracefulShutdownComplete())
        {
            // If there are no active streams, close immediately after the GO_AWAY write completes or the timeout
            // elapsed.
            AddListener(ctx, future, listener);
        }
        else
        {
            // If there are active streams we should wait until they are all closed before closing the connection.

            // The ClosingChannelFutureListener will cascade promise completion. We need to always notify the
            // new ClosingChannelFutureListener when the graceful close completes if the promise is not null.
            if (closeListener == null)
            {
                closeListener = listener;
            }
            else if (promise != null)
            {
                Action<Task> oldCloseListener = closeListener;
                closeListener = future1 =>
                {
                    try
                    {
                        oldCloseListener(future1);
                    }
                    finally
                    {
                        listener(future1);
                    }
                };
            }
        }
    }

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual void Read(IChannelHandlerContext ctx)
    {
        ctx.Read();
    }

    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        return ctx.WriteAsync(msg, cancellationToken);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        // Trigger flush after read on the assumption that flush is cheap if there is nothing to write and that
        // for flow-control the read may release window that causes data to be written that can now be flushed.
        try
        {
            // First call channelReadComplete0(...) as this may produce more data that we want to flush
            ChannelReadComplete0(ctx);
        }
        finally
        {
            Flush(ctx);
        }
    }

    internal void ChannelReadComplete0(IChannelHandlerContext ctx)
    {
        // Discard bytes of the cumulation buffer if needed.
        DiscardSomeReadBytes();

        // Ensure we never stale the HTTP/2 Channel. Flow-control is enforced by HTTP/2.
        //
        // See https://tools.ietf.org/html/rfc7540#section-5.2.2
        if (!ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }

        ctx.FireChannelReadComplete();
    }

    /**
     * Handles {@link Http2Exception} objects that were thrown from other handlers. Ignores all other exceptions.
     */
    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (GetEmbeddedHttp2Exception(cause) != null)
        {
            // Some exception in the causality chain is an Http2Exception - handle it.
            OnError(ctx, false, cause);
        }
        else
        {
            base.ExceptionCaught(ctx, cause);
        }
    }

    /**
     * Closes the local side of the given stream. If this causes the stream to be closed, adds a
     * hook to close the channel after the given future completes.
     *
     * @param stream the stream to be half closed.
     * @param future If closing, the future after which to close the channel.
     */
    public virtual void CloseStreamLocal(IHttp2Stream stream, Task future)
    {
        IHttp2Stream.State state = stream.State_();
        if (state == IHttp2Stream.State.HALF_CLOSED_LOCAL || state == IHttp2Stream.State.OPEN)
        {
            stream.CloseLocalSide();
        }
        else
        {
            CloseStream(stream, future);
        }
    }

    /**
     * Closes the remote side of the given stream. If this causes the stream to be closed, adds a
     * hook to close the channel after the given future completes.
     *
     * @param stream the stream to be half closed.
     * @param future If closing, the future after which to close the channel.
     */
    public virtual void CloseStreamRemote(IHttp2Stream stream, Task future)
    {
        IHttp2Stream.State state = stream.State_();
        if (state == IHttp2Stream.State.HALF_CLOSED_REMOTE || state == IHttp2Stream.State.OPEN)
        {
            stream.CloseRemoteSide();
        }
        else
        {
            CloseStream(stream, future);
        }
    }

    public virtual void CloseStream(IHttp2Stream stream, Task future)
    {
        if (future.IsCompleted)
        {
            DoCloseStream(stream, future);
        }
        else if (listenerCtx != null)
        {
            AddListener(listenerCtx, future, future1 => DoCloseStream(stream, future1));
        }
        else
        {
            // CLR: before handlerAdded there is no event loop to notify on; the listener runs when the future
            // completes, as a synchronous continuation.
            _ = future.ContinueWith(future1 => DoCloseStream(stream, future1), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /**
     * Central handler for all exceptions caught during HTTP/2 processing.
     */
    public virtual void OnError(IChannelHandlerContext ctx, bool outbound, Exception cause)
    {
        Http2Exception embedded = GetEmbeddedHttp2Exception(cause);
        if (IsStreamError(embedded))
        {
            OnStreamError(ctx, outbound, cause, (StreamException) embedded);
        }
        else if (embedded is CompositeStreamException)
        {
            CompositeStreamException compositException = (CompositeStreamException) embedded;
            // Each contained StreamException is generally an independent error for a distinct stream (e.g.
            // one per active stream that overflowed its flow-control window when SETTINGS_INITIAL_WINDOW_SIZE
            // changed), so every affected stream must still be reset individually; otherwise it would be left
            // open with a corrupted flow-control window. Should the composite ever contain more than one
            // exception for the same stream id, only report the first one, per RFC 9113, Section 5.4's
            // guidance to report at most one stream error per stream.
            HashSet<int> handledStreamIds = new HashSet<int>();
            foreach (StreamException streamException in compositException)
            {
                if (handledStreamIds.Add(streamException.StreamId()))
                {
                    OnStreamError(ctx, outbound, cause, streamException);
                }
            }
        }
        else
        {
            OnConnectionError(ctx, outbound, cause, embedded);
        }
        ctx.Flush();
    }

    /**
     * Called by the graceful shutdown logic to determine when it is safe to close the connection. Returns {@code true}
     * if the graceful shutdown has completed and the connection can be safely closed. This implementation just
     * guarantees that there are no active streams. Subclasses may override to provide additional checks.
     */
    protected virtual bool IsGracefulShutdownComplete()
    {
        return Connection().NumActiveStreams() == 0;
    }

    /**
     * Handler for a connection error. Sends a GO_AWAY frame to the remote endpoint. Once all
     * streams are closed, the connection is shut down.
     *
     * @param ctx the channel context
     * @param outbound {@code true} if the error was caused by an outbound operation.
     * @param cause the exception that was caught
     * @param http2Ex the {@link Http2Exception} that is embedded in the causality chain. This may
     *            be {@code null} if it's an unknown exception.
     */
    protected internal virtual void OnConnectionError(IChannelHandlerContext ctx, bool outbound,
                                     Exception cause, Http2Exception http2Ex)
    {
        if (http2Ex == null)
        {
            http2Ex = new Http2Exception(INTERNAL_ERROR, cause.Message, cause);
        }

        TaskCompletionSource promise = NewPromise();
        Task future = GoAwayAsync(ctx, http2Ex, NewPromise());
        if (http2Ex.ShutdownHint_() == Http2Exception.ShutdownHint.GRACEFUL_SHUTDOWN)
        {
            DoGracefulShutdown(ctx, future, promise);
        }
        else
        {
            AddListener(ctx, future, NewClosingChannelFutureListener(ctx, promise));
        }
    }

    /**
     * Handler for a stream error. Sends a {@code RST_STREAM} frame to the remote endpoint and closes the
     * stream.
     *
     * @param ctx the channel context
     * @param outbound {@code true} if the error was caused by an outbound operation.
     * @param cause the exception that was caught
     * @param http2Ex the {@link StreamException} that is embedded in the causality chain.
     */
    protected virtual void OnStreamError(IChannelHandlerContext ctx, bool outbound,
                                 Exception cause, StreamException http2Ex)
    {
        int streamId = http2Ex.StreamId();
        IHttp2Stream stream = Connection().Stream(streamId);

        //if this is caused by reading headers that are too large, send a header with status 431
        if (http2Ex is Http2Exception.HeaderListSizeException &&
            ((Http2Exception.HeaderListSizeException) http2Ex).DuringDecode() &&
            Connection().IsServer())
        {

            // NOTE We have to check to make sure that a stream exists before we send our reply.
            // We likely always create the stream below as the stream isn't created until the
            // header block is completely processed.

            // The case of a streamId referring to a stream which was already closed is handled
            // by createStream and will land us in the catch block below
            if (stream == null)
            {
                try
                {
                    stream = encoder.Connection().Remote().CreateStream(streamId, true);
                }
                catch (Http2Exception)
                {
                    _ = Encoder().WriteRstStreamAsync(ctx, streamId, http2Ex.Error().Code(), NewPromise());
                    return;
                }
            }

            // ensure that we have not already sent headers on this stream
            if (stream != null && !stream.IsHeadersSent())
            {
                try
                {
                    HandleServerHeaderDecodeSizeError(ctx, stream);
                }
                catch (Exception cause2)
                {
                    OnError(ctx, outbound, ConnectionError(INTERNAL_ERROR, cause2, "Error DecodeSizeError"));
                }
            }
        }

        if (stream == null)
        {
            if (!outbound || Connection().Local().MayHaveCreatedStream(streamId))
            {
                _ = Encoder().WriteRstStreamAsync(ctx, streamId, http2Ex.Error().Code(), NewPromise());
            }
        }
        else
        {
            _ = Encoder().WriteRstStreamAsync(ctx, streamId, http2Ex.Error().Code(), NewPromise());
        }
    }

    /**
     * Notifies client that this server has received headers that are larger than what it is
     * willing to accept. Override to change behavior.
     *
     * @param ctx the channel context
     * @param stream the Http2Stream on which the header was received
     */
    protected virtual void HandleServerHeaderDecodeSizeError(IChannelHandlerContext ctx, IHttp2Stream stream)
    {
        _ = Encoder().WriteHeadersAsync(ctx, stream.Id(), HEADERS_TOO_LARGE_HEADERS, 0, true, NewPromise());
    }

    protected virtual IHttp2FrameWriter FrameWriter()
    {
        return Encoder().FrameWriter();
    }

    /**
     * Sends a {@code RST_STREAM} frame even if we don't know about the stream. This error condition is most likely
     * triggered by the first frame of a stream being invalid. That is, there was an error reading the frame before
     * we could create a new stream.
     */
    private Task ResetUnknownStream(IChannelHandlerContext ctx, int streamId, long errorCode,
                                             TaskCompletionSource promise)
    {
        Task future = FrameWriter().WriteRstStreamAsync(ctx, streamId, errorCode, promise);
        if (future.IsCompleted)
        {
            CloseConnectionOnError(ctx, future);
        }
        else
        {
            AddListener(ctx, future, f => CloseConnectionOnError(ctx, f));
        }
        return future;
    }

    public virtual Task ResetStreamAsync(IChannelHandlerContext ctx, int streamId, long errorCode,
                                     TaskCompletionSource promise)
    {
        IHttp2Stream stream = Connection().Stream(streamId);
        if (stream == null)
        {
            return ResetUnknownStream(ctx, streamId, errorCode, promise);
        }

       return ResetStreamAsync(ctx, stream, errorCode, promise);
    }

    private Task ResetStreamAsync(IChannelHandlerContext ctx, IHttp2Stream stream,
                                      long errorCode, TaskCompletionSource promise)
    {
        if (stream.IsResetSent())
        {
            // Don't write a RST_STREAM frame if we have already written one.
            promise.SetResult();
            return promise.Task;
        }
        // Synchronously set the resetSent flag to prevent any subsequent calls
        // from resulting in multiple reset frames being sent.
        //
        // This needs to be done before we notify the promise as the promise may have a listener attached that
        // call resetStream(...) again.
        stream.ResetSent();

        Task future;
        // If the remote peer is not aware of the steam, then we are not allowed to send a RST_STREAM
        // https://tools.ietf.org/html/rfc7540#section-6.4.
        if (stream.State_() == IHttp2Stream.State.IDLE ||
            Connection().Local().Created(stream) && !stream.IsHeadersSent() && !stream.IsPushPromiseSent())
        {
            promise.SetResult();
            future = promise.Task;
        }
        else
        {
            future = FrameWriter().WriteRstStreamAsync(ctx, stream.Id(), errorCode, promise);
        }
        if (future.IsCompleted)
        {
            ProcessRstStreamWriteResult(ctx, stream, future);
        }
        else
        {
            AddListener(ctx, future, f => ProcessRstStreamWriteResult(ctx, stream, f));
        }

        return future;
    }

    public virtual Task GoAwayAsync(IChannelHandlerContext ctx, int lastStreamId, long errorCode,
                                ByteBuf debugData, TaskCompletionSource promise)
    {
        IHttp2Connection connection = Connection();
        try
        {
            if (!connection.GoAwaySent(lastStreamId, errorCode, debugData))
            {
                debugData.Release();
                promise.TrySetResult();
                return promise.Task;
            }
        }
        catch (Exception cause)
        {
            debugData.Release();
            promise.TrySetException(cause);
            return promise.Task;
        }

        // Need to retain before we write the buffer because if we do it after the refCnt could already be 0 and
        // result in an IllegalRefCountException.
        debugData.Retain();
        Task future = FrameWriter().WriteGoAwayAsync(ctx, lastStreamId, errorCode, debugData, promise);

        if (future.IsCompleted)
        {
            ProcessGoAwayWriteResult(ctx, lastStreamId, errorCode, debugData, future);
        }
        else
        {
            AddListener(ctx, future, f =>
                    ProcessGoAwayWriteResult(ctx, lastStreamId, errorCode, debugData, f));
        }

        return future;
    }

    /**
     * Closes the connection if the graceful shutdown process has completed.
     * @param future Represents the status that will be passed to the {@link #closeListener}.
     */
    private void CheckCloseConnection(Task future)
    {
        // If this connection is closing and the graceful shutdown has completed, close the connection
        // once this operation completes.
        if (closeListener != null && IsGracefulShutdownComplete())
        {
            Action<Task> closeListener = this.closeListener;
            // This method could be called multiple times
            // and we don't want to notify the closeListener multiple times.
            this.closeListener = null;
            try
            {
                closeListener(future);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Close listener threw an unexpected exception", e);
            }
        }
    }

    /**
     * Close the remote endpoint with a {@code GO_AWAY} frame. Does <strong>not</strong> flush
     * immediately, this is the responsibility of the caller.
     */
    private Task GoAwayAsync(IChannelHandlerContext ctx, Http2Exception cause, TaskCompletionSource promise)
    {
        long errorCode = cause != null ? cause.Error().Code() : NO_ERROR.Code();
        int lastKnownStream;
        if (cause != null && cause.ShutdownHint_() == Http2Exception.ShutdownHint.HARD_SHUTDOWN)
        {
            // The hard shutdown could have been triggered during header processing, before updating
            // lastStreamCreated(). Specifically, any connection errors encountered by Http2FrameReader or HPACK
            // decoding will fail to update the last known stream. So we must be pessimistic.
            // https://github.com/netty/netty/issues/10670
            lastKnownStream = int.MaxValue;
        }
        else
        {
            lastKnownStream = Connection().Remote().LastStreamCreated();
        }
        return GoAwayAsync(ctx, lastKnownStream, errorCode, Http2CodecUtil.ToByteBuf(ctx, cause), promise);
    }

    private void ProcessRstStreamWriteResult(IChannelHandlerContext ctx, IHttp2Stream stream, Task future)
    {
        if (future.IsCompletedSuccessfully)
        {
            CloseStream(stream, future);
        }
        else
        {
            // The connection will be closed and so no need to change the resetSent flag to false.
            OnConnectionError(ctx, true, Cause(future), null);
        }
    }

    private void CloseConnectionOnError(IChannelHandlerContext ctx, Task future)
    {
        if (!future.IsCompletedSuccessfully)
        {
            OnConnectionError(ctx, true, Cause(future), null);
        }
    }

    private void DoCloseStream(IHttp2Stream stream, Task future)
    {
        stream.Close();
        CheckCloseConnection(future);
    }

    /**
     * Returns the client preface string if this is a client connection, otherwise returns {@code null}.
     */
    private static ByteBuf ClientPrefaceString(IHttp2Connection connection)
    {
        return connection.IsServer() ? ConnectionPrefaceBuf() : null;
    }

    // CLR: debugData.toString(UTF_8) is ToString(Encoding.UTF8).
    private static void ProcessGoAwayWriteResult(IChannelHandlerContext ctx, int lastStreamId,
                                                 long errorCode, ByteBuf debugData, Task future)
    {
        try
        {
            if (future.IsCompletedSuccessfully)
            {
                if (errorCode != NO_ERROR.Code())
                {
                    if (logger.IsDebugEnabled())
                    {
                        logger.Debug("{} Sent GOAWAY: lastStreamId '{}', errorCode '{}', " +
                                     "debugData '{}'. Forcing shutdown of the connection.",
                                     ctx.Channel, lastStreamId, errorCode, debugData.ToString(Encoding.UTF8));
                    }
                    _ = ctx.CloseAsync();
                }
            }
            else
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("{} Sending GOAWAY failed: lastStreamId '{}', errorCode '{}', " +
                                 "debugData '{}'. Forcing shutdown of the connection.",
                                 ctx.Channel, lastStreamId, errorCode, debugData.ToString(Encoding.UTF8), Cause(future));
                }
                _ = ctx.CloseAsync();
            }
        }
        finally
        {
            // We're done with the debug data now.
            debugData.Release();
        }
    }

    // CLR: ChannelFutureListener.CLOSE_ON_FAILURE: closes the channel when the operation failed (or was canceled).
    private static void CloseOnFailure(IChannelHandlerContext ctx, Task future)
    {
        if (!future.IsCompletedSuccessfully)
        {
            _ = ctx.Channel.CloseAsync();
        }
    }

    /**
     * Closes the channel when the future completes.
     */
    // CLR: the ChannelFutureListener is the OperationComplete(Task) method. ctx.executor().schedule(task, timeout,
    // unit) is ctx.Executor.ScheduleAsync with a CancellationTokenSource, whose Cancel() is timeoutTask.cancel(false)
    // (docs/codec-http-porting.md, WebSocket "scheduled timeouts are ScheduleAsync with a CancellationTokenSource"); the
    // long timeout with its TimeUnit is a TimeSpan. ctx.close(promise) completes the promise with the context's close
    // Task (Http2CodecUtil.Close). assert is Debug.Assert.
    private sealed class ClosingChannelFutureListener
    {
        private readonly IChannelHandlerContext ctx;
        private readonly TaskCompletionSource promise;
        private readonly CancellationTokenSource timeoutTask;
        private bool closed;

        internal ClosingChannelFutureListener(IChannelHandlerContext ctx, TaskCompletionSource promise)
        {
            this.ctx = ctx;
            this.promise = promise;
            timeoutTask = null;
        }

        internal ClosingChannelFutureListener(IChannelHandlerContext ctx, TaskCompletionSource promise,
                                     TimeSpan timeout)
        {
            this.ctx = ctx;
            this.promise = promise;
            timeoutTask = new CancellationTokenSource();
            _ = ctx.Executor.ScheduleAsync(DoClose, timeout, timeoutTask.Token);
        }

        public void OperationComplete(Task sentGoAwayFuture)
        {
            if (timeoutTask != null)
            {
                timeoutTask.Cancel(false);
            }
            DoClose();
        }

        private void DoClose()
        {
            // We need to guard against multiple calls as the timeout may trigger close() first and then it will be
            // triggered again because of operationComplete(...) is called.
            if (closed)
            {
                // This only happens if we also scheduled a timeout task.
                Debug.Assert(timeoutTask != null);
                return;
            }
            closed = true;
            if (promise == null)
            {
                _ = ctx.CloseAsync();
            }
            else
            {
                Close(ctx, promise);
            }
        }
    }

    private sealed class PrefaceSendListener
    {
        private readonly Http2ConnectionHandler handler;
        private readonly IChannelHandlerContext ctx;
        private readonly TaskCompletionSource promise;

        internal PrefaceSendListener(Http2ConnectionHandler handler, IChannelHandlerContext ctx,
                                     TaskCompletionSource promise)
        {
            this.handler = handler;
            this.ctx = ctx;
            this.promise = promise;
        }

        public void OperationComplete(Task f)
        {
            if (f.IsCompletedSuccessfully)
            {
                try
                {
                    if (handler.byteDecoder != null)
                    {
                        handler.byteDecoder.SendPrefaceIfNeeded(ctx);
                    }
                }
                catch (Exception e)
                {
                    promise.SetException(e);
                    return;
                }
                promise.SetResult();
            }
            else
            {
                promise.SetException(Cause(f));
            }
        }
    }
}
