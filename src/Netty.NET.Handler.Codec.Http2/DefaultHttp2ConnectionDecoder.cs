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
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;
using PseudoHeaderName = Netty.NET.Handler.Codec.Http2.IHttp2Headers.PseudoHeaderName;

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
// CLR: the non-final Java methods are virtual; calculateMaxHeaderListSizeGoAway is protected virtual and
// onGoAwayRead0 (package-private) internal. The inner (non-static) listener classes receive the enclosing decoder
// in their constructors, so the field initializer of internalFrameListener runs in the constructor (a C# field
// initializer cannot reference the instance, CS0236). The @Deprecated constructor (autoAckPing, no @deprecated
// text and so no named replacement) is ported with [Obsolete]. Endpoint<?> is the non-generic
// IHttp2Connection.IEndpoint. A Java switch on Http2Stream.State is an if/else chain on the State instances (a C#
// case label must be a constant). ctx.newSucceededFuture() is Task.CompletedTask and ctx.newPromise()
// Http2CodecUtil.NewPromise() (the Task of the auto-ack writes is discarded, as Java discards the future). The
// catch of Http2Exception | RuntimeException is catch (Exception): every CLR exception is unchecked;
// IllegalArgumentException is ArgumentException, which is what codec-http's HttpUtil.NormalizeAndGetContentLength
// throws. List<? extends CharSequence> is IList<ICharSequence>, Iterator<Entry<...>> the codec-base
// IIterator<IEntry<...>>, Class equality is Type equality and Http2SettingsReceivedConsumer.class in the message
// is typeof(IHttp2SettingsReceivedConsumer) (see DecoratingHttp2ConnectionEncoder).
public class DefaultHttp2ConnectionDecoder : IHttp2ConnectionDecoder
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DefaultHttp2ConnectionDecoder));
    private IHttp2FrameListener internalFrameListener;
    private readonly IHttp2Connection connection;
    private IHttp2LifecycleManager lifecycleManager;
    private readonly IHttp2ConnectionEncoder encoder;
    private readonly IHttp2FrameReader frameReader;
    private IHttp2FrameListener listener;
    private readonly IHttp2PromisedRequestVerifier requestVerifier;
    private readonly IHttp2SettingsReceivedConsumer settingsReceivedConsumer;
    private readonly bool autoAckPing;
    private readonly IHttp2Connection.IPropertyKey contentLengthKey;
    private readonly bool validateHeaders;
    private readonly bool validateRequiredPseudoHeaders;

    public DefaultHttp2ConnectionDecoder(IHttp2Connection connection,
                                         IHttp2ConnectionEncoder encoder,
                                         IHttp2FrameReader frameReader)
        : this(connection, encoder, frameReader, IHttp2PromisedRequestVerifier.ALWAYS_VERIFY)
    {
    }

    public DefaultHttp2ConnectionDecoder(IHttp2Connection connection,
                                         IHttp2ConnectionEncoder encoder,
                                         IHttp2FrameReader frameReader,
                                         IHttp2PromisedRequestVerifier requestVerifier)
        : this(connection, encoder, frameReader, requestVerifier, true)
    {
    }

    /**
     * Create a new instance.
     * @param connection The {@link Http2Connection} associated with this decoder.
     * @param encoder The {@link Http2ConnectionEncoder} associated with this decoder.
     * @param frameReader Responsible for reading/parsing the raw frames. As opposed to this object which applies
     *                    h2 semantics on top of the frames.
     * @param requestVerifier Determines if push promised streams are valid.
     * @param autoAckSettings {@code false} to disable automatically applying and sending settings acknowledge frame.
     *  The {@code Http2ConnectionEncoder} is expected to be an instance of {@link Http2SettingsReceivedConsumer} and
     *  will apply the earliest received but not yet ACKed SETTINGS when writing the SETTINGS ACKs.
     * {@code true} to enable automatically applying and sending settings acknowledge frame.
     */
#pragma warning disable CS0612 // CLR: the Java constructor delegates to the @Deprecated one, as here.
    public DefaultHttp2ConnectionDecoder(IHttp2Connection connection,
                                         IHttp2ConnectionEncoder encoder,
                                         IHttp2FrameReader frameReader,
                                         IHttp2PromisedRequestVerifier requestVerifier,
                                         bool autoAckSettings)
        : this(connection, encoder, frameReader, requestVerifier, autoAckSettings, true)
    {
    }
#pragma warning restore CS0612

    [Obsolete]
    public DefaultHttp2ConnectionDecoder(IHttp2Connection connection,
                                         IHttp2ConnectionEncoder encoder,
                                         IHttp2FrameReader frameReader,
                                         IHttp2PromisedRequestVerifier requestVerifier,
                                         bool autoAckSettings,
                                         bool autoAckPing)
        : this(connection, encoder, frameReader, requestVerifier, autoAckSettings, autoAckPing, true)
    {
    }

    /**
     * Create a new instance.
     * @param connection The {@link Http2Connection} associated with this decoder.
     * @param encoder The {@link Http2ConnectionEncoder} associated with this decoder.
     * @param frameReader Responsible for reading/parsing the raw frames. As opposed to this object which applies
     *                    h2 semantics on top of the frames.
     * @param requestVerifier Determines if push promised streams are valid.
     * @param autoAckSettings {@code false} to disable automatically applying and sending settings acknowledge frame.
     *                        The {@code Http2ConnectionEncoder} is expected to be an instance of
     *                        {@link Http2SettingsReceivedConsumer} and will apply the earliest received but not yet
     *                        ACKed SETTINGS when writing the SETTINGS ACKs. {@code true} to enable automatically
     *                        applying and sending settings acknowledge frame.
     * @param autoAckPing {@code false} to disable automatically sending ping acknowledge frame. {@code true} to enable
     *                    automatically sending ping ack frame.
     */
    public DefaultHttp2ConnectionDecoder(IHttp2Connection connection,
                                         IHttp2ConnectionEncoder encoder,
                                         IHttp2FrameReader frameReader,
                                         IHttp2PromisedRequestVerifier requestVerifier,
                                         bool autoAckSettings,
                                         bool autoAckPing,
                                         bool validateHeaders)
        : this(connection, encoder, frameReader, requestVerifier, autoAckSettings, autoAckPing, validateHeaders, false)
    {
    }

    /**
     * Create a new instance.
     * @param connection The {@link Http2Connection} associated with this decoder.
     * @param encoder The {@link Http2ConnectionEncoder} associated with this decoder.
     * @param frameReader Responsible for reading/parsing the raw frames. As opposed to this object which applies
     *                    h2 semantics on top of the frames.
     * @param requestVerifier Determines if push promised streams are valid.
     * @param autoAckSettings {@code false} to disable automatically applying and sending settings acknowledge frame.
     *                        The {@code Http2ConnectionEncoder} is expected to be an instance of
     *                        {@link Http2SettingsReceivedConsumer} and will apply the earliest received but not yet
     *                        ACKed SETTINGS when writing the SETTINGS ACKs. {@code true} to enable automatically
     *                        applying and sending settings acknowledge frame.
     * @param autoAckPing {@code false} to disable automatically sending ping acknowledge frame. {@code true} to enable
     *                    automatically sending ping ack frame.
     * @param validateHeaders {@code true} to validate headers according to
     *                        <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.6">RFC 7540, 8.1.2.6</a>.
     * @param validateRequiredPseudoHeaders {@code true} to reject request/response HEADERS that omit a mandatory
     *        pseudo-header field, according to
     *        <a href="https://www.rfc-editor.org/rfc/rfc9113.html#section-8.3">RFC 9113, 8.3</a>, and to reject
     *        PUSH_PROMISE field blocks that do not carry a complete set of request pseudo-header fields, according
     *        to <a href="https://www.rfc-editor.org/rfc/rfc9113.html#section-8.4.1">RFC 9113, 8.4.1</a>.
     */
    public DefaultHttp2ConnectionDecoder(IHttp2Connection connection,
                                         IHttp2ConnectionEncoder encoder,
                                         IHttp2FrameReader frameReader,
                                         IHttp2PromisedRequestVerifier requestVerifier,
                                         bool autoAckSettings,
                                         bool autoAckPing,
                                         bool validateHeaders,
                                         bool validateRequiredPseudoHeaders)
    {
        internalFrameListener = new PrefaceFrameListener(this);
        this.validateHeaders = validateHeaders;
        this.validateRequiredPseudoHeaders = validateRequiredPseudoHeaders;
        this.autoAckPing = autoAckPing;

        if (autoAckSettings)
        {
            settingsReceivedConsumer = null;
        }
        else
        {
            if (!(encoder is IHttp2SettingsReceivedConsumer))
            {
                throw new ArgumentException("disabling autoAckSettings requires the encoder to be a " +
                        typeof(IHttp2SettingsReceivedConsumer));
            }

            settingsReceivedConsumer = (IHttp2SettingsReceivedConsumer) encoder;
        }

        this.connection = CheckNotNull(connection, "connection");
        contentLengthKey = this.connection.NewKey();
        this.frameReader = CheckNotNull(frameReader, "frameReader");
        this.encoder = CheckNotNull(encoder, "encoder");
        this.requestVerifier = CheckNotNull(requestVerifier, "requestVerifier");

        if (connection.Local().FlowController() == null)
        {
            connection.Local().FlowController(new DefaultHttp2LocalFlowController(connection));
        }

        connection.Local().FlowController().FrameWriter(encoder.FrameWriter());
    }

    public virtual void LifecycleManager(IHttp2LifecycleManager lifecycleManager)
    {
        this.lifecycleManager = CheckNotNull(lifecycleManager, "lifecycleManager");
    }

    public virtual IHttp2Connection Connection()
    {
        return connection;
    }

    public IHttp2LocalFlowController FlowController()
    {
        return connection.Local().FlowController();
    }

    public virtual void FrameListener(IHttp2FrameListener listener)
    {
        this.listener = CheckNotNull(listener, "listener");
    }

    public virtual IHttp2FrameListener FrameListener()
    {
        return listener;
    }

    public virtual bool PrefaceReceived()
    {
        return typeof(FrameReadListener) == internalFrameListener.GetType();
    }

    public virtual void DecodeFrame(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        frameReader.ReadFrame(ctx, @in, internalFrameListener);
    }

    public virtual Http2Settings LocalSettings()
    {
        Http2Settings settings = new Http2Settings();
        IHttp2FrameReader.IConfiguration config = frameReader.Configuration();
        IHttp2HeadersDecoder.IConfiguration headersConfig = config.HeadersConfiguration();
        IHttp2FrameSizePolicy frameSizePolicy = config.FrameSizePolicy();
        settings.InitialWindowSize(FlowController().InitialWindowSize());
        settings.MaxConcurrentStreams(connection.Remote().MaxActiveStreams());
        settings.HeaderTableSize(headersConfig.MaxHeaderTableSize());
        settings.MaxFrameSize(frameSizePolicy.MaxFrameSize());
        settings.MaxHeaderListSize(headersConfig.MaxHeaderListSize());

        if (!connection.IsServer())
        {
            // Only set the pushEnabled flag if this is a client endpoint.
            settings.PushEnabled(connection.Local().AllowPushTo());
        }

        return settings;
    }

    public virtual void Close()
    {
        frameReader.Close();
    }

    /**
     * Calculate the threshold in bytes which should trigger a {@code GO_AWAY} if a set of headers exceeds this amount.
     * @param maxHeaderListSize
     *      <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_HEADER_LIST_SIZE</a> for the local
     *      endpoint.
     * @return the threshold in bytes which should trigger a {@code GO_AWAY} if a set of headers exceeds this amount.
     */
    protected virtual long CalculateMaxHeaderListSizeGoAway(long maxHeaderListSize)
    {
        return Http2CodecUtil.CalculateMaxHeaderListSizeGoAway(maxHeaderListSize);
    }

    private int UnconsumedBytes(IHttp2Stream stream)
    {
        return FlowController().UnconsumedBytes(stream);
    }

    internal void OnGoAwayRead0(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
    {
        listener.OnGoAwayRead(ctx, lastStreamId, errorCode, debugData);
        connection.GoAwayReceived(lastStreamId, errorCode, debugData);
    }

    // See https://tools.ietf.org/html/rfc7540#section-8.1.2.6
    private void VerifyContentLength(IHttp2Stream stream, int data, bool isEnd)
    {
        ContentLength contentLength = stream.GetProperty<ContentLength>(contentLengthKey);
        if (contentLength != null)
        {
            try
            {
                contentLength.IncreaseReceivedBytes(connection.IsServer(), stream.Id(), data, isEnd);
            }
            finally
            {
                if (isEnd)
                {
                    stream.RemoveProperty<ContentLength>(contentLengthKey);
                }
            }
        }
    }

    /**
     * Validates that an initial request or response HEADERS frame, or a PUSH_PROMISE field block (which always
     * carries a request header set, per <a href="https://www.rfc-editor.org/rfc/rfc9113.html#section-8.4.1">
     * RFC 9113, 8.4.1</a>), carries the mandatory pseudo-header fields, as required by
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html#section-8.3">RFC 9113, 8.3</a>.
     * Trailers and informational (1xx) responses are handled by the caller and do not reach this method.
     */
    private static void ValidateRequiredPseudoHeaders(bool server, int streamId, IHttp2Headers headers)
    {
        if (server)
        {
            // Request pseudo-header fields (RFC 9113, 8.3.1).
            ICharSequence method = headers.Method();
            if (method == null)
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                        "Request is missing mandatory :method pseudo-header field.");
            }

            // CONNECT (RFC 9113, 8.5) omits :scheme/:path and carries :authority; extended CONNECT (RFC 8441),
            // identified by :protocol, follows the regular :scheme/:path rules.
            if (HttpMethod.CONNECT.AsciiName().ContentEquals(method) &&
                    !headers.Contains(PseudoHeaderName.PROTOCOL.Value()))
            {
                if (headers.Authority() == null)
                {
                    throw StreamError(streamId, PROTOCOL_ERROR,
                            "CONNECT request is missing mandatory :authority pseudo-header field.");
                }
            }
            else
            {
                if (headers.Scheme() == null)
                {
                    throw StreamError(streamId, PROTOCOL_ERROR,
                            "Request is missing mandatory :scheme pseudo-header field.");
                }

                ICharSequence path = headers.Path();
                if (path == null || path.Length() == 0)
                {
                    throw StreamError(streamId, PROTOCOL_ERROR,
                            "Request is missing mandatory :path pseudo-header field.");
                }
            }
        }
        else
        {
            // Response pseudo-header fields (RFC 9113, 8.3.2).
            if (headers.Status() == null)
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                        "Response is missing mandatory :status pseudo-header field.");
            }
        }
    }

    /**
     * Handles all inbound frames from the network.
     */
    private sealed class FrameReadListener : IHttp2FrameListener
    {
        // CLR: the enclosing instance of the Java inner class.
        private readonly DefaultHttp2ConnectionDecoder decoder;

        internal FrameReadListener(DefaultHttp2ConnectionDecoder decoder)
        {
            this.decoder = decoder;
        }

        public int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
                              bool endOfStream)
        {
            IHttp2Stream stream = decoder.connection.Stream(streamId);
            IHttp2LocalFlowController flowController = decoder.FlowController();
            int readable = data.ReadableBytes;
            int bytesToReturn = readable + padding;

            bool shouldIgnore;

            try
            {
                shouldIgnore = ShouldIgnoreHeadersOrDataFrame(ctx, streamId, stream, endOfStream, "DATA");
            }
            catch (Http2Exception)
            {
                // Ignoring this frame. We still need to count the frame towards the connection flow control
                // window, but we immediately mark all bytes as consumed.
                flowController.ReceiveFlowControlledFrame(stream, data, padding, endOfStream);
                flowController.ConsumeBytes(stream, bytesToReturn);
                throw;
            }
            catch (Exception t)
            {
                throw ConnectionError(INTERNAL_ERROR, t, "Unhandled error on data stream id %d", streamId);
            }

            if (shouldIgnore)
            {
                // Ignoring this frame. We still need to count the frame towards the connection flow control
                // window, but we immediately mark all bytes as consumed.
                flowController.ReceiveFlowControlledFrame(stream, data, padding, endOfStream);
                flowController.ConsumeBytes(stream, bytesToReturn);

                // Verify that the stream may have existed after we apply flow control.
                VerifyStreamMayHaveExisted(streamId, endOfStream, "DATA");

                // All bytes have been consumed.
                return bytesToReturn;
            }

            Http2Exception error = null;
            IHttp2Stream.State state = stream.State_();
            if (state == IHttp2Stream.State.OPEN || state == IHttp2Stream.State.HALF_CLOSED_LOCAL)
            {
            }
            else if (state == IHttp2Stream.State.HALF_CLOSED_REMOTE || state == IHttp2Stream.State.CLOSED)
            {
                error = StreamError(stream.Id(), STREAM_CLOSED, "Stream %d in unexpected state: %s",
                    stream.Id(), stream.State_());
            }
            else
            {
                error = StreamError(stream.Id(), PROTOCOL_ERROR,
                    "Stream %d in unexpected state: %s", stream.Id(), stream.State_());
            }

            int unconsumedBytes = decoder.UnconsumedBytes(stream);

            try
            {
                flowController.ReceiveFlowControlledFrame(stream, data, padding, endOfStream);
                // Update the unconsumed bytes after flow control is applied.
                unconsumedBytes = decoder.UnconsumedBytes(stream);

                // If the stream is in an invalid state to receive the frame, throw the error.
                if (error != null)
                {
                    throw error;
                }

                decoder.VerifyContentLength(stream, readable, endOfStream);

                // Call back the application and retrieve the number of bytes that have been
                // immediately processed.
                bytesToReturn = decoder.listener.OnDataRead(ctx, streamId, data, padding, endOfStream);

                if (endOfStream)
                {
                    decoder.lifecycleManager.CloseStreamRemote(stream, Task.CompletedTask);
                }

                return bytesToReturn;
            }
            catch (Exception)
            {
                // If an exception happened during delivery, the listener may have returned part
                // of the bytes before the error occurred. If that's the case, subtract that from
                // the total processed bytes so that we don't return too many bytes.
                int delta = unconsumedBytes - decoder.UnconsumedBytes(stream);
                bytesToReturn -= delta;
                throw;
            }
            finally
            {
                // If appropriate, return the processed bytes to the flow controller.
                flowController.ConsumeBytes(stream, bytesToReturn);
            }
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                bool endOfStream)
        {
            OnHeadersRead(ctx, streamId, headers, 0, DEFAULT_PRIORITY_WEIGHT, false, padding, endOfStream);
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                short weight, bool exclusive, int padding, bool endOfStream)
        {
            IHttp2Connection connection = decoder.connection;
            IHttp2Stream stream = connection.Stream(streamId);
            bool allowHalfClosedRemote = false;
            bool isTrailers = false;

            if (stream == null && !connection.StreamMayHaveExisted(streamId))
            {
                stream = connection.Remote().CreateStream(streamId, endOfStream);
                // Allow the state to be HALF_CLOSE_REMOTE if we're creating it in that state.
                allowHalfClosedRemote = stream.State_() == IHttp2Stream.State.HALF_CLOSED_REMOTE;
            }
            else if (stream != null)
            {
                isTrailers = stream.IsHeadersReceived();
            }

            if (ShouldIgnoreHeadersOrDataFrame(ctx, streamId, stream, endOfStream, "HEADERS"))
            {
                return;
            }

            bool isInformational = !connection.IsServer() &&
                    HttpStatusClass.ValueOf(headers.Status()) == HttpStatusClass.INFORMATIONAL;
            if ((isInformational || !endOfStream) && stream.IsHeadersReceived() || stream.IsTrailersReceived())
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                                  "Stream %d received too many headers EOS: %s state: %s",
                                  streamId, endOfStream, stream.State_());
            }

            IHttp2Stream.State state = stream.State_();
            if (state == IHttp2Stream.State.RESERVED_REMOTE)
            {
                stream.Open(endOfStream);
            }
            else if (state == IHttp2Stream.State.OPEN || state == IHttp2Stream.State.HALF_CLOSED_LOCAL)
            {
                // Allowed to receive headers in these states.
            }
            else if (state == IHttp2Stream.State.HALF_CLOSED_REMOTE)
            {
                if (!allowHalfClosedRemote)
                {
                    throw StreamError(stream.Id(), STREAM_CLOSED, "Stream %d in unexpected state: %s",
                            stream.Id(), stream.State_());
                }
            }
            else if (state == IHttp2Stream.State.CLOSED)
            {
                throw StreamError(stream.Id(), STREAM_CLOSED, "Stream %d in unexpected state: %s",
                        stream.Id(), stream.State_());
            }
            else
            {
                // Connection error.
                throw ConnectionError(PROTOCOL_ERROR, "Stream %d in unexpected state: %s", stream.Id(),
                        stream.State_());
            }

            if (!isTrailers)
            {
                if (decoder.validateRequiredPseudoHeaders && !isInformational)
                {
                    // Reject initial request/response HEADERS that omit a mandatory pseudo-header (RFC 9113, 8.3).
                    ValidateRequiredPseudoHeaders(connection.IsServer(), stream.Id(), headers);
                }

                // extract the content-length header
                IList<ICharSequence> contentLength = headers.GetAll(HttpHeaderNames.CONTENT_LENGTH);
                if (contentLength != null && contentLength.Count != 0)
                {
                    try
                    {
                        long cLength = HttpUtil.NormalizeAndGetContentLength(contentLength, false, true);
                        if (cLength != -1)
                        {
                            headers.SetLong(HttpHeaderNames.CONTENT_LENGTH, cLength);
                            stream.SetProperty(decoder.contentLengthKey, new ContentLength(cLength));
                        }
                    }
                    catch (ArgumentException e)
                    {
                        throw StreamError(stream.Id(), PROTOCOL_ERROR, e,
                                "Multiple content-length headers received");
                    }
                }
                // Use size() instead of isEmpty() for backward compatibility with grpc-java prior to 1.59.1,
                // see https://github.com/grpc/grpc-java/issues/10665
            }
            else if (decoder.validateHeaders && headers.Size() > 0)
            {
                // Need to check trailers don't contain pseudo headers. According to RFC 9113
                // Trailers MUST NOT include pseudo-header fields (Section 8.3).
                for (IIterator<IEntry<ICharSequence, ICharSequence>> iterator =
                    headers.Iterator(); iterator.HasNext();)
                {
                    ICharSequence name = iterator.Next().GetKey();
                    if (PseudoHeaderName.HasPseudoHeaderFormat(name))
                    {
                        throw StreamError(stream.Id(), PROTOCOL_ERROR,
                                "Found invalid Pseudo-Header in trailers: %s", name);
                    }
                }
            }

            stream.HeadersReceived(isInformational);
            decoder.VerifyContentLength(stream, 0, endOfStream);
            decoder.encoder.FlowController().UpdateDependencyTree(streamId, streamDependency, weight, exclusive);
            decoder.listener.OnHeadersRead(ctx, streamId, headers, streamDependency,
                    weight, exclusive, padding, endOfStream);

            // If the headers completes this stream, close it.
            if (endOfStream)
            {
                decoder.lifecycleManager.CloseStreamRemote(stream, Task.CompletedTask);
            }
        }

        public void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency, short weight,
                bool exclusive)
        {
            decoder.encoder.FlowController().UpdateDependencyTree(streamId, streamDependency, weight, exclusive);

            decoder.listener.OnPriorityRead(ctx, streamId, streamDependency, weight, exclusive);
        }

        public void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
        {
            IHttp2Stream stream = decoder.connection.Stream(streamId);
            if (stream == null)
            {
                VerifyStreamMayHaveExisted(streamId, false, "RST_STREAM");
                return;
            }

            IHttp2Stream.State state = stream.State_();
            if (state == IHttp2Stream.State.IDLE)
            {
                throw ConnectionError(PROTOCOL_ERROR, "RST_STREAM received for IDLE stream %d", streamId);
            }
            else if (state == IHttp2Stream.State.CLOSED)
            {
                return; // RST_STREAM frames must be ignored for closed streams.
            }

            decoder.listener.OnRstStreamRead(ctx, streamId, errorCode);

            decoder.lifecycleManager.CloseStream(stream, Task.CompletedTask);
        }

        public void OnSettingsAckRead(IChannelHandlerContext ctx)
        {
            // Apply oldest outstanding local settings here. This is a synchronization point between endpoints.
            Http2Settings settings = decoder.encoder.PollSentSettings();

            if (settings != null)
            {
                ApplyLocalSettings(settings);
            }

            decoder.listener.OnSettingsAckRead(ctx);
        }

        /**
         * Applies settings sent from the local endpoint.
         * <p>
         * This method is only called after the local settings have been acknowledged from the remote endpoint.
         */
        private void ApplyLocalSettings(Http2Settings settings)
        {
            IHttp2Connection connection = decoder.connection;
            bool? pushEnabled = settings.PushEnabled();
            IHttp2FrameReader.IConfiguration config = decoder.frameReader.Configuration();
            IHttp2HeadersDecoder.IConfiguration headerConfig = config.HeadersConfiguration();
            IHttp2FrameSizePolicy frameSizePolicy = config.FrameSizePolicy();

            if (pushEnabled != null)
            {
                if (connection.IsServer())
                {
                    throw ConnectionError(PROTOCOL_ERROR, "Server sending SETTINGS frame with ENABLE_PUSH specified");
                }

                connection.Local().AllowPushTo(pushEnabled.Value);
            }

            long? maxConcurrentStreams = settings.MaxConcurrentStreams();
            if (maxConcurrentStreams != null)
            {
                connection.Remote().MaxActiveStreams((int) Math.Min(maxConcurrentStreams.Value, int.MaxValue));
            }

            long? headerTableSize = settings.HeaderTableSize();
            if (headerTableSize != null)
            {
                headerConfig.MaxHeaderTableSize(headerTableSize.Value);
            }

            long? maxHeaderListSize = settings.MaxHeaderListSize();
            if (maxHeaderListSize != null)
            {
                headerConfig.MaxHeaderListSize(maxHeaderListSize.Value,
                        decoder.CalculateMaxHeaderListSizeGoAway(maxHeaderListSize.Value));
            }

            int? maxFrameSize = settings.MaxFrameSize();
            if (maxFrameSize != null)
            {
                frameSizePolicy.MaxFrameSize(maxFrameSize.Value);
            }

            int? initialWindowSize = settings.InitialWindowSize();
            if (initialWindowSize != null)
            {
                decoder.FlowController().InitialWindowSize(initialWindowSize.Value);
            }
        }

        public void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
        {
            if (decoder.settingsReceivedConsumer == null)
            {
                // Acknowledge receipt of the settings. We should do this before we process the settings to ensure our
                // remote peer applies these settings before any subsequent frames that we may send which depend upon
                // these new settings. See https://github.com/netty/netty/issues/6520.
                decoder.encoder.WriteSettingsAckAsync(ctx, NewPromise());

                decoder.encoder.RemoteSettings(settings);
            }
            else
            {
                decoder.settingsReceivedConsumer.ConsumeReceivedSettings(settings);
            }

            decoder.listener.OnSettingsRead(ctx, settings);
        }

        public void OnPingRead(IChannelHandlerContext ctx, long data)
        {
            if (decoder.autoAckPing)
            {
                // Send an ack back to the remote client.
                decoder.encoder.WritePingAsync(ctx, true, data, NewPromise());
            }

            decoder.listener.OnPingRead(ctx, data);
        }

        public void OnPingAckRead(IChannelHandlerContext ctx, long data)
        {
            decoder.listener.OnPingAckRead(ctx, data);
        }

        public void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                IHttp2Headers headers, int padding)
        {
            IHttp2Connection connection = decoder.connection;
            // A client cannot push.
            if (decoder.Connection().IsServer())
            {
                throw ConnectionError(PROTOCOL_ERROR, "A client cannot push.");
            }

            IHttp2Stream parentStream = connection.Stream(streamId);

            if (ShouldIgnoreHeadersOrDataFrame(ctx, streamId, parentStream, false, "PUSH_PROMISE"))
            {
                return;
            }

            IHttp2Stream.State state = parentStream.State_();
            if (state == IHttp2Stream.State.OPEN || state == IHttp2Stream.State.HALF_CLOSED_LOCAL)
            {
                  // Allowed to receive push promise in these states.
            }
            else
            {
                  // Connection error.
                  throw ConnectionError(PROTOCOL_ERROR,
                      "Stream %d in unexpected state for receiving push promise: %s",
                      parentStream.Id(), parentStream.State_());
            }

            // Reserve the push stream, with a priority based on the current stream's priority, before running
            // the request verifier below. This ensures the promised stream's existence is tracked even if the
            // request is subsequently rejected, so the stream ID cannot be silently reused or forgotten.
            connection.Remote().ReservePushStream(promisedStreamId, parentStream);

            if (decoder.validateRequiredPseudoHeaders)
            {
                // A PUSH_PROMISE field block carries a complete set of request header fields, regardless of the
                // role of the local endpoint. Reject an incomplete promised request on the promised stream
                // (RFC 9113, 8.4.1).
                ValidateRequiredPseudoHeaders(true, promisedStreamId, headers);
            }

            // extract the content-length header
            IList<ICharSequence> contentLength = headers.GetAll(HttpHeaderNames.CONTENT_LENGTH);
            if (contentLength != null && contentLength.Count != 0)
            {
                long cLength = HttpUtil.NormalizeAndGetContentLength(contentLength, false, true);
                if (cLength != -1 && cLength != 0)
                {
                    throw StreamError(promisedStreamId, PROTOCOL_ERROR,
                        "Promised request on stream %d for promised stream %d contains invalid content-length header",
                        streamId, promisedStreamId);
                }
            }

            if (!decoder.requestVerifier.IsAuthoritative(ctx, headers))
            {
                throw StreamError(promisedStreamId, PROTOCOL_ERROR,
                        "Promised request on stream %d for promised stream %d is not authoritative",
                        streamId, promisedStreamId);
            }
            if (!decoder.requestVerifier.IsCacheable(headers))
            {
                throw StreamError(promisedStreamId, PROTOCOL_ERROR,
                        "Promised request on stream %d for promised stream %d is not known to be cacheable",
                        streamId, promisedStreamId);
            }
            if (!decoder.requestVerifier.IsSafe(headers))
            {
                throw StreamError(promisedStreamId, PROTOCOL_ERROR,
                        "Promised request on stream %d for promised stream %d is not known to be safe",
                        streamId, promisedStreamId);
            }

            decoder.listener.OnPushPromiseRead(ctx, streamId, promisedStreamId, headers, padding);
        }

        public void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
        {
            decoder.OnGoAwayRead0(ctx, lastStreamId, errorCode, debugData);
        }

        public void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
        {
            IHttp2Stream stream = decoder.connection.Stream(streamId);
            if (stream == null || stream.State_() == IHttp2Stream.State.CLOSED || StreamCreatedAfterGoAwaySent(streamId))
            {
                // Ignore this frame.
                VerifyStreamMayHaveExisted(streamId, false, "WINDOW_UPDATE");
                return;
            }

            // Update the outbound flow control window.
            decoder.encoder.FlowController().IncrementWindowSize(stream, windowSizeIncrement);

            decoder.listener.OnWindowUpdateRead(ctx, streamId, windowSizeIncrement);
        }

        public void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
                ByteBuf payload)
        {
            IHttp2Stream stream = decoder.connection.Stream(streamId);
            if (stream == null)
            {
                return;
            }

            decoder.listener.OnUnknownFrame(ctx, frameType, streamId, flags, payload);
        }

        /**
         * Helper method to determine if a frame that has the semantics of headers or data should be ignored for the
         * {@code stream} (which may be {@code null}) associated with {@code streamId}.
         */
        private bool ShouldIgnoreHeadersOrDataFrame(IChannelHandlerContext ctx, int streamId, IHttp2Stream stream,
                bool endOfStream, string frameName)
        {
            if (stream == null)
            {
                if (StreamCreatedAfterGoAwaySent(streamId))
                {
                    logger.Info("{} ignoring {} frame for stream {}. Stream sent after GOAWAY sent",
                            ctx.Channel, frameName, streamId);
                    return true;
                }

                // Make sure it's not an out-of-order frame, like a rogue DATA frame, for a stream that could
                // never have existed.
                VerifyStreamMayHaveExisted(streamId, endOfStream, frameName);

                // Its possible that this frame would result in stream ID out of order creation (PROTOCOL ERROR) and its
                // also possible that this frame is received on a CLOSED stream (STREAM_CLOSED after a RST_STREAM is
                // sent). We don't have enough information to know for sure, so we choose the lesser of the two errors.
                throw StreamError(streamId, STREAM_CLOSED, "Received %s frame for an unknown stream %d",
                                  frameName, streamId);
            }

            if (stream.IsResetSent() || StreamCreatedAfterGoAwaySent(streamId))
            {
                // If we have sent a reset stream it is assumed the stream will be closed after the write completes.
                // If we have not sent a reset, but the stream was created after a GoAway this is not supported by
                // DefaultHttp2Connection and if a custom Http2Connection is used it is assumed the lifetime is managed
                // elsewhere so we don't close the stream or otherwise modify the stream's state.

                if (logger.IsInfoEnabled())
                {
                    logger.Info("{} ignoring {} frame for stream {}", ctx.Channel, frameName,
                            stream.IsResetSent() ? "RST_STREAM sent." :
                                    "Stream created after GOAWAY sent. Last known stream by peer " +
                                     ToJavaString(decoder.connection.Remote().LastStreamKnownByPeer()));
                }

                return true;
            }

            return false;
        }

        /**
         * Helper method for determining whether or not to ignore inbound frames. A stream is considered to be created
         * after a {@code GOAWAY} is sent if the following conditions hold:
         * <p/>
         * <ul>
         *     <li>A {@code GOAWAY} must have been sent by the local endpoint</li>
         *     <li>The {@code streamId} must identify a legitimate stream id for the remote endpoint to be creating</li>
         *     <li>{@code streamId} is greater than the Last Known Stream ID which was sent by the local endpoint
         *     in the last {@code GOAWAY} frame</li>
         * </ul>
         * <p/>
         */
        private bool StreamCreatedAfterGoAwaySent(int streamId)
        {
            IHttp2Connection.IEndpoint remote = decoder.connection.Remote();
            return decoder.connection.GoAwaySent() && remote.IsValidStreamId(streamId) &&
                    streamId > remote.LastStreamKnownByPeer();
        }

        private void VerifyStreamMayHaveExisted(int streamId, bool endOfStream, string frameName)
        {
            if (!decoder.connection.StreamMayHaveExisted(streamId))
            {
                throw ConnectionError(PROTOCOL_ERROR,
                        "Stream %d does not exist for inbound frame %s, endOfStream = %b",
                        streamId, frameName, endOfStream);
            }
        }
    }

    private sealed class PrefaceFrameListener : IHttp2FrameListener
    {
        // CLR: the enclosing instance of the Java inner class.
        private readonly DefaultHttp2ConnectionDecoder decoder;

        internal PrefaceFrameListener(DefaultHttp2ConnectionDecoder decoder)
        {
            this.decoder = decoder;
        }

        /**
         * Verifies that the HTTP/2 connection preface has been received from the remote endpoint.
         * It is possible that the current call to
         * {@link Http2FrameReader#readFrame(ChannelHandlerContext, ByteBuf, Http2FrameListener)} will have multiple
         * frames to dispatch. So it may be OK for this class to get legitimate frames for the first readFrame.
         */
        private void VerifyPrefaceReceived()
        {
            if (!decoder.PrefaceReceived())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Received non-SETTINGS as first frame.");
            }
        }

        public int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream)
        {
            VerifyPrefaceReceived();

            return decoder.internalFrameListener.OnDataRead(ctx, streamId, data, padding, endOfStream);
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                bool endOfStream)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnHeadersRead(ctx, streamId, headers, padding, endOfStream);
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                short weight, bool exclusive, int padding, bool endOfStream)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnHeadersRead(ctx, streamId, headers, streamDependency, weight,
                    exclusive, padding, endOfStream);
        }

        public void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency, short weight,
                bool exclusive)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnPriorityRead(ctx, streamId, streamDependency, weight, exclusive);
        }

        public void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnRstStreamRead(ctx, streamId, errorCode);
        }

        public void OnSettingsAckRead(IChannelHandlerContext ctx)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnSettingsAckRead(ctx);
        }

        public void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
        {
            // The first settings should change the internalFrameListener to the "real" listener
            // that expects the preface to be verified.
            if (!decoder.PrefaceReceived())
            {
                decoder.internalFrameListener = new FrameReadListener(decoder);
            }

            decoder.internalFrameListener.OnSettingsRead(ctx, settings);
        }

        public void OnPingRead(IChannelHandlerContext ctx, long data)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnPingRead(ctx, data);
        }

        public void OnPingAckRead(IChannelHandlerContext ctx, long data)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnPingAckRead(ctx, data);
        }

        public void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                IHttp2Headers headers, int padding)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnPushPromiseRead(ctx, streamId, promisedStreamId, headers, padding);
        }

        public void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
        {
            decoder.OnGoAwayRead0(ctx, lastStreamId, errorCode, debugData);
        }

        public void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnWindowUpdateRead(ctx, streamId, windowSizeIncrement);
        }

        public void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
                ByteBuf payload)
        {
            VerifyPrefaceReceived();
            decoder.internalFrameListener.OnUnknownFrame(ctx, frameType, streamId, flags, payload);
        }
    }

    private sealed class ContentLength
    {
        private readonly long expected;
        private long seen;

        internal ContentLength(long expected)
        {
            this.expected = expected;
        }

        internal void IncreaseReceivedBytes(bool server, int streamId, int bytes, bool isEnd)
        {
            seen += bytes;
            // Check for overflow
            if (seen < 0)
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                        "Received amount of data did overflow and so not match content-length header %d", expected);
            }
            // Check if we received more data then what was advertised via the content-length header.
            if (seen > expected)
            {
                throw StreamError(streamId, PROTOCOL_ERROR,
                        "Received amount of data %d does not match content-length header %d", seen, expected);
            }

            if (isEnd)
            {
                if (seen == 0 && !server)
                {
                    // This may be a response to a HEAD request, let's just allow it.
                    return;
                }

                // Check that we really saw what was told via the content-length header.
                if (expected > seen)
                {
                    throw StreamError(streamId, PROTOCOL_ERROR,
                            "Received amount of data %d does not match content-length header %d", seen, expected);
                }
            }
        }
    }
}
