/*
 * Copyright 2015 The Netty Project
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
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Abstract base class which defines commonly used features required to build {@link Http2ConnectionHandler} instances.
 *
 * <h3>Three ways to build a {@link Http2ConnectionHandler}</h3>
 * <h4>Let the builder create a {@link Http2ConnectionHandler}</h4>
 * Simply call all the necessary setter methods, and then use {@link #build()} to build a new
 * {@link Http2ConnectionHandler}. Setting the following properties are prohibited because they are used for
 * other ways of building a {@link Http2ConnectionHandler}.
 * conflicts with this option:
 * <ul>
 *   <li>{@link #connection(Http2Connection)}</li>
 *   <li>{@link #codec(Http2ConnectionDecoder, Http2ConnectionEncoder)}</li>
 * </ul>
 *
 *
 * <h4>Let the builder use the {@link Http2ConnectionHandler} you specified</h4>
 * Call {@link #connection(Http2Connection)} to tell the builder that you want to build the handler from the
 * {@link Http2Connection} you specified. Setting the following properties are prohibited and thus will trigger
 * an {@link IllegalStateException} because they conflict with this option.
 * <ul>
 *   <li>{@link #server(boolean)}</li>
 *   <li>{@link #codec(Http2ConnectionDecoder, Http2ConnectionEncoder)}</li>
 * </ul>
 *
 * <h4>Let the builder use the {@link Http2ConnectionDecoder} and {@link Http2ConnectionEncoder} you specified</h4>
 * Call {@link #codec(Http2ConnectionDecoder, Http2ConnectionEncoder)} to tell the builder that you want to built the
 * handler from the {@link Http2ConnectionDecoder} and {@link Http2ConnectionEncoder} you specified. Setting the
 * following properties are prohibited and thus will trigger an {@link IllegalStateException} because they conflict
 * with this option:
 * <ul>
 *   <li>{@link #server(boolean)}</li>
 *   <li>{@link #connection(Http2Connection)}</li>
 *   <li>{@link #frameLogger(Http2FrameLogger)}</li>
 *   <li>{@link #headerSensitivityDetector(SensitivityDetector)}</li>
 *   <li>{@link #encoderEnforceMaxConcurrentStreams(boolean)}</li>
 *   <li>{@link #encoderIgnoreMaxHeaderListSize(boolean)}</li>
 * </ul>
 *
 * <h3>Exposing necessary methods in a subclass</h3>
 * {@link #build()} method and all property access methods are {@code protected}. Choose the methods to expose to the
 * users of your builder implementation and make them {@code public}.
 *
 * @param <T> The type of handler created by this builder.
 * @param <B> The concrete type of this builder.
 */
// CLR: the self-typed generic <T extends Http2ConnectionHandler, B extends AbstractHttp2ConnectionHandlerBuilder<T, B>>
// keeps its two type parameters with the same constraints; self() casts through object, because C# only converts
// this to the type parameter B through an object conversion (an unchecked cast in Java). The protected non-final
// Java methods are protected virtual. A Java subclass widens them to public with @Override; a C# override cannot
// ("An override declaration can't change the accessibility of the virtual method",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so subclasses expose them
// with public members declared new ("you explicitly hide a member that a base class provides",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier) that call base
// (Http2ConnectionHandlerBuilder). The Boolean/Integer/Long fields and locals
// that may be null are bool?/int?/long?. SensitivityDetector is IHttp2HeadersEncoder.ISensitivityDetector and
// ALWAYS_VERIFY is IHttp2PromisedRequestVerifier.ALWAYS_VERIFY. IllegalArgumentException is ArgumentException,
// IllegalStateException InvalidOperationException, Throwable Exception (docs/codec-base-porting.md) and assert
// Debug.Assert; a long or boolean concatenated into an exception text keeps the Java text (Http2CodecUtil.ToJavaString).
// The @Deprecated initialHuffmanDecodeCapacity(int) names no replacement, so it is ported with [Obsolete] (owner
// decision 2026-11-16). Channel#close() in the Javadoc is IChannel.CloseAsync().
public abstract class AbstractHttp2ConnectionHandlerBuilder<T, B>
    where T : Http2ConnectionHandler
    where B : AbstractHttp2ConnectionHandlerBuilder<T, B>
{

    private static readonly IHttp2HeadersEncoder.ISensitivityDetector DEFAULT_HEADER_SENSITIVITY_DETECTOR = IHttp2HeadersEncoder.NEVER_SENSITIVE;

    private const int DEFAULT_MAX_RST_FRAMES_PER_CONNECTION_FOR_SERVER = 200;

    // The properties that can always be set.
    private Http2Settings initialSettings = Http2Settings.DefaultSettings();
    private IHttp2FrameListener frameListener;
    private long gracefulShutdownTimeoutMillis = Http2CodecUtil.DEFAULT_GRACEFUL_SHUTDOWN_TIMEOUT_MILLIS;
    private bool decoupleCloseAndGoAway;
    private bool flushPreface = true;

    // The property that will prohibit connection() and codec() if set by server(),
    // because this property is used only when this builder creates an Http2Connection.
    private bool? isServer;
    private int? maxReservedStreams;

    // The property that will prohibit server() and codec() if set by connection().
    private IHttp2Connection connection;

    // The properties that will prohibit server() and connection() if set by codec().
    private IHttp2ConnectionDecoder decoder;
    private IHttp2ConnectionEncoder encoder;

    // The properties that are:
    // * mutually exclusive against codec() and
    // * OK to use with server() and connection()
    private bool? validateHeaders;
    private bool? validateRequiredPseudoHeaders;
    private Http2FrameLogger frameLogger;
    private IHttp2HeadersEncoder.ISensitivityDetector headerSensitivityDetector;
    private bool? encoderEnforceMaxConcurrentStreams;
    private bool? encoderIgnoreMaxHeaderListSize;
    private IHttp2PromisedRequestVerifier promisedRequestVerifier = IHttp2PromisedRequestVerifier.ALWAYS_VERIFY;
    private bool autoAckSettingsFrame = true;
    private bool autoAckPingFrame = true;
    private int maxQueuedControlFrames = Http2CodecUtil.DEFAULT_MAX_QUEUED_CONTROL_FRAMES;
    private int maxConsecutiveEmptyFrames = 2;
    private int? maxDecodedRstFramesPerWindow;
    private int maxDecodedRstFramesSecondsPerWindow = 30;
    private int? maxEncodedRstFramesPerWindow;
    private int maxEncodedRstFramesSecondsPerWindow = 30;
    private int maxSmallContinuationFrames = Http2CodecUtil.DEFAULT_MAX_SMALL_CONTINUATION_FRAME;

    /**
     * Sets the {@link Http2Settings} to use for the initial connection settings exchange.
     */
    protected virtual Http2Settings InitialSettings()
    {
        return initialSettings;
    }

    /**
     * Sets the {@link Http2Settings} to use for the initial connection settings exchange.
     */
    protected virtual B InitialSettings(Http2Settings settings)
    {
        initialSettings = CheckNotNull(settings, "settings");
        return Self();
    }

    /**
     * Returns the listener of inbound frames.
     *
     * @return {@link Http2FrameListener} if set, or {@code null} if not set.
     */
    protected virtual IHttp2FrameListener FrameListener()
    {
        return frameListener;
    }

    /**
     * Sets the listener of inbound frames.
     * This listener will only be set if the decoder's listener is {@code null}.
     */
    protected virtual B FrameListener(IHttp2FrameListener frameListener)
    {
        this.frameListener = CheckNotNull(frameListener, "frameListener");
        return Self();
    }

    /**
     * Returns the graceful shutdown timeout of the {@link Http2Connection} in milliseconds. Returns -1 if the
     * timeout is indefinite.
     */
    protected virtual long GracefulShutdownTimeoutMillis()
    {
        return gracefulShutdownTimeoutMillis;
    }

    /**
     * Sets the graceful shutdown timeout of the {@link Http2Connection} in milliseconds.
     */
    protected virtual B GracefulShutdownTimeoutMillis(long gracefulShutdownTimeoutMillis)
    {
        if (gracefulShutdownTimeoutMillis < -1)
        {
            throw new ArgumentException("gracefulShutdownTimeoutMillis: " + ToJavaString(gracefulShutdownTimeoutMillis) +
                                               " (expected: -1 for indefinite or >= 0)");
        }
        this.gracefulShutdownTimeoutMillis = gracefulShutdownTimeoutMillis;
        return Self();
    }

    /**
     * Returns if {@link #build()} will to create a {@link Http2Connection} in server mode ({@code true})
     * or client mode ({@code false}).
     */
    protected virtual bool IsServer()
    {
        return isServer != null ? isServer.Value : true;
    }

    /**
     * Sets if {@link #build()} will to create a {@link Http2Connection} in server mode ({@code true})
     * or client mode ({@code false}).
     */
    protected virtual B Server(bool isServer)
    {
        EnforceConstraint("server", "connection", connection);
        EnforceConstraint("server", "codec", decoder);
        EnforceConstraint("server", "codec", encoder);

        this.isServer = isServer;
        return Self();
    }

    /**
     * Get the maximum number of streams which can be in the reserved state at any given time.
     * <p>
     * By default this value will be ignored on the server for local endpoint. This is because the RFC provides
     * no way to explicitly communicate a limit to how many states can be in the reserved state, and instead relies
     * on the peer to send RST_STREAM frames when they will be rejected.
     */
    protected virtual int MaxReservedStreams()
    {
        return maxReservedStreams != null ? maxReservedStreams.Value : DEFAULT_MAX_RESERVED_STREAMS;
    }

    /**
     * Set the maximum number of streams which can be in the reserved state at any given time.
     */
    protected virtual B MaxReservedStreams(int maxReservedStreams)
    {
        EnforceConstraint("server", "connection", connection);
        EnforceConstraint("server", "codec", decoder);
        EnforceConstraint("server", "codec", encoder);

        this.maxReservedStreams = CheckPositiveOrZero(maxReservedStreams, "maxReservedStreams");
        return Self();
    }

    /**
     * Returns the {@link Http2Connection} to use.
     *
     * @return {@link Http2Connection} if set, or {@code null} if not set.
     */
    protected virtual IHttp2Connection Connection()
    {
        return connection;
    }

    /**
     * Sets the {@link Http2Connection} to use.
     */
    protected virtual B Connection(IHttp2Connection connection)
    {
        EnforceConstraint("connection", "maxReservedStreams", maxReservedStreams);
        EnforceConstraint("connection", "server", isServer);
        EnforceConstraint("connection", "codec", decoder);
        EnforceConstraint("connection", "codec", encoder);

        this.connection = CheckNotNull(connection, "connection");

        return Self();
    }

    /**
     * Returns the {@link Http2ConnectionDecoder} to use.
     *
     * @return {@link Http2ConnectionDecoder} if set, or {@code null} if not set.
     */
    protected virtual IHttp2ConnectionDecoder Decoder()
    {
        return decoder;
    }

    /**
     * Returns the {@link Http2ConnectionEncoder} to use.
     *
     * @return {@link Http2ConnectionEncoder} if set, or {@code null} if not set.
     */
    protected virtual IHttp2ConnectionEncoder Encoder()
    {
        return encoder;
    }

    /**
     * Sets the {@link Http2ConnectionDecoder} and {@link Http2ConnectionEncoder} to use.
     */
    protected virtual B Codec(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder)
    {
        EnforceConstraint("codec", "server", isServer);
        EnforceConstraint("codec", "maxReservedStreams", maxReservedStreams);
        EnforceConstraint("codec", "connection", connection);
        EnforceConstraint("codec", "frameLogger", frameLogger);
        EnforceConstraint("codec", "validateHeaders", validateHeaders);
        EnforceConstraint("codec", "validateRequiredPseudoHeaders", validateRequiredPseudoHeaders);
        EnforceConstraint("codec", "headerSensitivityDetector", headerSensitivityDetector);
        EnforceConstraint("codec", "encoderEnforceMaxConcurrentStreams", encoderEnforceMaxConcurrentStreams);

        CheckNotNull(decoder, "decoder");
        CheckNotNull(encoder, "encoder");

        if (decoder.Connection() != encoder.Connection())
        {
            throw new ArgumentException("The specified encoder and decoder have different connections.");
        }

        this.decoder = decoder;
        this.encoder = encoder;

        return Self();
    }

    /**
     * Returns if HTTP headers should be validated according to
     * <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.6">RFC 7540, 8.1.2.6</a>.
     */
    protected virtual bool IsValidateHeaders()
    {
        return validateHeaders != null ? validateHeaders.Value : true;
    }

    /**
     * Sets if HTTP headers should be validated according to
     * <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.6">RFC 7540, 8.1.2.6</a>.
     */
    protected virtual B ValidateHeaders(bool validateHeaders)
    {
        EnforceNonCodecConstraints("validateHeaders");
        this.validateHeaders = validateHeaders;
        return Self();
    }

    /**
     * Returns if mandatory pseudo-header fields are validated according to
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html#section-8.3">RFC 9113, 8.3</a>. Disabled by default.
     */
    protected virtual bool IsValidateRequiredPseudoHeaders()
    {
        return validateRequiredPseudoHeaders != null ? validateRequiredPseudoHeaders.Value : false;
    }

    /**
     * Sets if request and response {@code HEADERS} that omit a mandatory pseudo-header field are rejected,
     * according to <a href="https://www.rfc-editor.org/rfc/rfc9113.html#section-8.3">RFC 9113, 8.3</a>.
     * Disabled by default.
     */
    protected virtual B ValidateRequiredPseudoHeaders(bool validateRequiredPseudoHeaders)
    {
        EnforceNonCodecConstraints("validateRequiredPseudoHeaders");
        this.validateRequiredPseudoHeaders = validateRequiredPseudoHeaders;
        return Self();
    }

    /**
     * Returns the logger that is used for the encoder and decoder.
     *
     * @return {@link Http2FrameLogger} if set, or {@code null} if not set.
     */
    protected virtual Http2FrameLogger FrameLogger()
    {
        return frameLogger;
    }

    /**
     * Sets the logger that is used for the encoder and decoder.
     */
    protected virtual B FrameLogger(Http2FrameLogger frameLogger)
    {
        EnforceNonCodecConstraints("frameLogger");
        this.frameLogger = CheckNotNull(frameLogger, "frameLogger");
        return Self();
    }

    /**
     * Returns if the encoder should queue frames if the maximum number of concurrent streams
     * would otherwise be exceeded.
     */
    protected virtual bool EncoderEnforceMaxConcurrentStreams()
    {
        return encoderEnforceMaxConcurrentStreams != null ? encoderEnforceMaxConcurrentStreams.Value : false;
    }

    /**
     * Sets if the encoder should queue frames if the maximum number of concurrent streams
     * would otherwise be exceeded.
     */
    protected virtual B EncoderEnforceMaxConcurrentStreams(bool encoderEnforceMaxConcurrentStreams)
    {
        EnforceNonCodecConstraints("encoderEnforceMaxConcurrentStreams");
        this.encoderEnforceMaxConcurrentStreams = encoderEnforceMaxConcurrentStreams;
        return Self();
    }

    /**
     * Returns the maximum number of queued control frames that are allowed before the connection is closed.
     * This allows to protected against various attacks that can lead to high CPU / memory usage if the remote-peer
     * floods us with frames that would have us produce control frames, but stops to read from the underlying socket.
     *
     * {@code 0} means no protection is in place.
     */
    protected virtual int EncoderEnforceMaxQueuedControlFrames()
    {
        return maxQueuedControlFrames;
    }

    /**
     * Sets the maximum number of queued control frames that are allowed before the connection is closed.
     * This allows to protected against various attacks that can lead to high CPU / memory usage if the remote-peer
     * floods us with frames that would have us produce control frames, but stops to read from the underlying socket.
     *
     * {@code 0} means no protection should be applied.
     */
    protected virtual B EncoderEnforceMaxQueuedControlFrames(int maxQueuedControlFrames)
    {
        EnforceNonCodecConstraints("encoderEnforceMaxQueuedControlFrames");
        this.maxQueuedControlFrames = CheckPositiveOrZero(maxQueuedControlFrames, "maxQueuedControlFrames");
        return Self();
    }

    /**
     * Returns the {@link SensitivityDetector} to use.
     */
    protected virtual IHttp2HeadersEncoder.ISensitivityDetector HeaderSensitivityDetector()
    {
        return headerSensitivityDetector != null ? headerSensitivityDetector : DEFAULT_HEADER_SENSITIVITY_DETECTOR;
    }

    /**
     * Sets the {@link SensitivityDetector} to use.
     */
    protected virtual B HeaderSensitivityDetector(IHttp2HeadersEncoder.ISensitivityDetector headerSensitivityDetector)
    {
        EnforceNonCodecConstraints("headerSensitivityDetector");
        this.headerSensitivityDetector = CheckNotNull(headerSensitivityDetector, "headerSensitivityDetector");
        return Self();
    }

    /**
     * Sets if the <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_HEADER_LIST_SIZE</a>
     * should be ignored when encoding headers.
     * @param ignoreMaxHeaderListSize {@code true} to ignore
     * <a href="https://tools.ietf.org/html/rfc7540#section-6.5.2">SETTINGS_MAX_HEADER_LIST_SIZE</a>.
     * @return this.
     */
    protected virtual B EncoderIgnoreMaxHeaderListSize(bool ignoreMaxHeaderListSize)
    {
        EnforceNonCodecConstraints("encoderIgnoreMaxHeaderListSize");
        encoderIgnoreMaxHeaderListSize = ignoreMaxHeaderListSize;
        return Self();
    }

    /**
     * Does nothing, do not call.
     *
     * @deprecated Huffman decoding no longer depends on having a decode capacity.
     */
    [Obsolete("Huffman decoding no longer depends on having a decode capacity.")]
    protected virtual B InitialHuffmanDecodeCapacity(int initialHuffmanDecodeCapacity)
    {
        return Self();
    }

    /**
     * Set the {@link Http2PromisedRequestVerifier} to use.
     * @return this.
     */
    protected virtual B PromisedRequestVerifier(IHttp2PromisedRequestVerifier promisedRequestVerifier)
    {
        EnforceNonCodecConstraints("promisedRequestVerifier");
        this.promisedRequestVerifier = CheckNotNull(promisedRequestVerifier, "promisedRequestVerifier");
        return Self();
    }

    /**
     * Get the {@link Http2PromisedRequestVerifier} to use.
     * @return the {@link Http2PromisedRequestVerifier} to use.
     */
    protected virtual IHttp2PromisedRequestVerifier PromisedRequestVerifier()
    {
        return promisedRequestVerifier;
    }

    /**
     * Returns the maximum number of consecutive empty DATA frames (without end_of_stream flag) that are allowed before
     * the connection is closed. This allows to protect against the remote peer flooding us with such frames and
     * so use up a lot of CPU. There is no valid use-case for empty DATA frames without end_of_stream flag.
     *
     * {@code 0} means no protection is in place.
     */
    protected virtual int DecoderEnforceMaxConsecutiveEmptyDataFrames()
    {
        return maxConsecutiveEmptyFrames;
    }

    /**
     * Sets the maximum number of consecutive empty DATA frames (without end_of_stream flag) that are allowed before
     * the connection is closed. This allows to protect against the remote peer flooding us with such frames and
     * so use up a lot of CPU. There is no valid use-case for empty DATA frames without end_of_stream flag.
     *
     * {@code 0} means no protection should be applied.
     */
    protected virtual B DecoderEnforceMaxConsecutiveEmptyDataFrames(int maxConsecutiveEmptyFrames)
    {
        EnforceNonCodecConstraints("maxConsecutiveEmptyFrames");
        this.maxConsecutiveEmptyFrames = CheckPositiveOrZero(
                maxConsecutiveEmptyFrames, "maxConsecutiveEmptyFrames");
        return Self();
    }

    /**
     * Sets the maximum number RST frames that are allowed per window before
     * the connection is closed. This allows to protect against the remote peer flooding us with such frames and
     * so use up a lot of CPU.
     *
     * {@code 0} for any of the parameters means no protection should be applied.
     */
    protected virtual B DecoderEnforceMaxRstFramesPerWindow(int maxRstFramesPerWindow, int secondsPerWindow)
    {
        EnforceNonCodecConstraints("decoderEnforceMaxRstFramesPerWindow");
        this.maxDecodedRstFramesPerWindow = CheckPositiveOrZero(
                maxRstFramesPerWindow, "maxRstFramesPerWindow");
        this.maxDecodedRstFramesSecondsPerWindow = CheckPositiveOrZero(secondsPerWindow, "secondsPerWindow");
        return Self();
    }

    /**
     * Sets the maximum number RST frames that are allowed per window before
     * the connection is closed. This allows to protect against the remote peer that will trigger us to generate a flood
     * of RST frames and so use up a lot of CPU.
     *
     * {@code 0} for any of the parameters means no protection should be applied.
     */
    protected virtual B EncoderEnforceMaxRstFramesPerWindow(int maxRstFramesPerWindow, int secondsPerWindow)
    {
        EnforceNonCodecConstraints("encoderEnforceMaxRstFramesPerWindow");
        this.maxEncodedRstFramesPerWindow = CheckPositiveOrZero(
                maxRstFramesPerWindow, "maxRstFramesPerWindow");
        this.maxEncodedRstFramesSecondsPerWindow = CheckPositiveOrZero(secondsPerWindow, "secondsPerWindow");
        return Self();
    }

    /**
     * Returns the maximum number of small CONTINUATION frames per HEADERS block that are allowed
     * before the connection is closed. Small is defined as 8 KiB, half the minimum allowed HTTP2 frame size.
     * This setting is to protect against the remote peer flooding us with such frames.
     *
     * {@code 0} means no protection is in place.
     */
    protected virtual int DecoderEnforceMaxSmallContinuationFrames()
    {
        return maxSmallContinuationFrames;
    }

    /**
     * Returns the maximum number of small CONTINUATION frames per HEADERS block that are allowed
     * before the connection is closed. Small is defined as 8 KiB, half the minimum allowed HTTP2 frame size.
     * This setting is to protect against the remote peer flooding us with such frames.
     * {@code 0} means no protection should be applied.
     */
    protected virtual B DecoderEnforceMaxSmallContinuationFrames(int maxSmallContinuationFrames)
    {
        EnforceNonCodecConstraints("maxSmallContinuationFrames");
        this.maxSmallContinuationFrames = CheckPositiveOrZero(
                maxSmallContinuationFrames, "maxSmallContinuationFrames");
        return Self();
    }

    /**
     * Determine if settings frame should automatically be acknowledged and applied.
     * @return this.
     */
    protected virtual B AutoAckSettingsFrame(bool autoAckSettings)
    {
        EnforceNonCodecConstraints("autoAckSettingsFrame");
        autoAckSettingsFrame = autoAckSettings;
        return Self();
    }

    /**
     * Determine if the SETTINGS frames should be automatically acknowledged and applied.
     * @return {@code true} if the SETTINGS frames should be automatically acknowledged and applied.
     */
    protected virtual bool IsAutoAckSettingsFrame()
    {
        return autoAckSettingsFrame;
    }

    /**
     * Determine if PING frame should automatically be acknowledged or not.
     * @return this.
     */
    protected virtual B AutoAckPingFrame(bool autoAckPingFrame)
    {
        EnforceNonCodecConstraints("autoAckPingFrame");
        this.autoAckPingFrame = autoAckPingFrame;
        return Self();
    }

    /**
     * Determine if the PING frames should be automatically acknowledged or not.
     * @return {@code true} if the PING frames should be automatically acknowledged.
     */
    protected virtual bool IsAutoAckPingFrame()
    {
        return autoAckPingFrame;
    }

    /**
     * Determine if the {@link Channel#close()} should be coupled with goaway and graceful close.
     * @param decoupleCloseAndGoAway {@code true} to make {@link Channel#close()} directly close the underlying
     *   transport, and not attempt graceful closure via GOAWAY.
     * @return {@code this}.
     */
    protected virtual B DecoupleCloseAndGoAway(bool decoupleCloseAndGoAway)
    {
        this.decoupleCloseAndGoAway = decoupleCloseAndGoAway;
        return Self();
    }

    /**
     * Determine if the {@link Channel#close()} should be coupled with goaway and graceful close.
     */
    protected virtual bool DecoupleCloseAndGoAway()
    {
        return decoupleCloseAndGoAway;
    }

    /**
     * Determine if the <a href="https://datatracker.ietf.org/doc/html/rfc7540#section-3.5">Preface</a>
     * should be automatically flushed when the {@link Channel} becomes active or not.
     * <p>
     * Client may choose to opt-out from this automatic behavior and manage flush manually if it's ready to send
     * request frames immediately after the preface. It may help to avoid unnecessary latency.
     *
     * @param flushPreface {@code true} to automatically flush, {@code false otherwise}.
     * @return {@code this}.
     * @see <a href="https://datatracker.ietf.org/doc/html/rfc7540#section-3.5">HTTP/2 Connection Preface</a>
     */
    protected virtual B FlushPreface(bool flushPreface)
    {
        this.flushPreface = flushPreface;
        return Self();
    }

    /**
     * Determine if the <a href="https://datatracker.ietf.org/doc/html/rfc7540#section-3.5">Preface</a>
     * should be automatically flushed when the {@link Channel} becomes active or not.
     * <p>
     * Client may choose to opt-out from this automatic behavior and manage flush manually if it's ready to send
     * request frames immediately after the preface. It may help to avoid unnecessary latency.
     *
     * @return {@code true} if automatically flushed.
     * @see <a href="https://datatracker.ietf.org/doc/html/rfc7540#section-3.5">HTTP/2 Connection Preface</a>
     */
    protected virtual bool FlushPreface()
    {
        return flushPreface;
    }

    /**
     * Create a new {@link Http2ConnectionHandler}.
     */
    protected virtual T Build()
    {
        if (encoder != null)
        {
            Debug.Assert(decoder != null);
            return BuildFromCodec(decoder, encoder);
        }

        IHttp2Connection connection = this.connection;
        if (connection == null)
        {
            connection = new DefaultHttp2Connection(IsServer(), MaxReservedStreams());
        }

        return BuildFromConnection(connection);
    }

    private T BuildFromConnection(IHttp2Connection connection)
    {
        // Enforce the advertised maxConcurrentStreams limit on the remote endpoint immediately,
        // without waiting for the SETTINGS_ACK round-trip.
        EnforceMaxActiveStreams(connection, initialSettings);

        long? maxHeaderListSize = initialSettings.MaxHeaderListSize();
        IHttp2FrameReader reader = new DefaultHttp2FrameReader(new DefaultHttp2HeadersDecoder(IsValidateHeaders(),
                maxHeaderListSize == null ? DEFAULT_HEADER_LIST_SIZE : maxHeaderListSize.Value,
                /* initialHuffmanDecodeCapacity= */ -1), maxSmallContinuationFrames);
        IHttp2FrameWriter writer = encoderIgnoreMaxHeaderListSize == null ?
                new DefaultHttp2FrameWriter(HeaderSensitivityDetector()) :
                new DefaultHttp2FrameWriter(HeaderSensitivityDetector(), encoderIgnoreMaxHeaderListSize.Value);

        if (frameLogger != null)
        {
            reader = new Http2InboundFrameLogger(reader, frameLogger);
            writer = new Http2OutboundFrameLogger(writer, frameLogger);
        }

        IHttp2ConnectionEncoder encoder = new DefaultHttp2ConnectionEncoder(connection, writer);
        bool encoderEnforceMaxConcurrentStreams = EncoderEnforceMaxConcurrentStreams();

        if (maxQueuedControlFrames != 0)
        {
            encoder = new Http2ControlFrameLimitEncoder(encoder, maxQueuedControlFrames);
        }
        int maxEncodedRstFrames;
        if (maxEncodedRstFramesPerWindow == null)
        {
            // Only enable by default on the server.
            if (IsServer())
            {
                maxEncodedRstFrames = DEFAULT_MAX_RST_FRAMES_PER_CONNECTION_FOR_SERVER;
            }
            else
            {
                maxEncodedRstFrames = 0;
            }
        }
        else
        {
            maxEncodedRstFrames = maxEncodedRstFramesPerWindow.Value;
        }
        if (maxEncodedRstFrames > 0 && maxEncodedRstFramesSecondsPerWindow > 0)
        {
            encoder = new Http2MaxRstFrameLimitEncoder(
                    encoder, maxEncodedRstFrames, maxEncodedRstFramesSecondsPerWindow);
        }
        if (encoderEnforceMaxConcurrentStreams)
        {
            if (connection.IsServer())
            {
                encoder.Close();
                reader.Close();
                throw new ArgumentException(
                        "encoderEnforceMaxConcurrentStreams: " + ToJavaString(encoderEnforceMaxConcurrentStreams) +
                        " not supported for server");
            }
            encoder = new StreamBufferingEncoder(encoder);
        }

        DefaultHttp2ConnectionDecoder decoder = new DefaultHttp2ConnectionDecoder(connection, encoder, reader,
            PromisedRequestVerifier(), IsAutoAckSettingsFrame(), IsAutoAckPingFrame(), IsValidateHeaders(),
            IsValidateRequiredPseudoHeaders());
        return BuildFromCodec(decoder, encoder);
    }

    private T BuildFromCodec(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder)
    {
        // Enforce the advertised maxConcurrentStreams limit on the remote endpoint immediately,
        // without waiting for the SETTINGS_ACK round-trip.
        EnforceMaxActiveStreams(encoder.Connection(), initialSettings);

        int maxConsecutiveEmptyDataFrames = DecoderEnforceMaxConsecutiveEmptyDataFrames();
        if (maxConsecutiveEmptyDataFrames > 0)
        {
            decoder = new Http2EmptyDataFrameConnectionDecoder(decoder, maxConsecutiveEmptyDataFrames);
        }
        int maxDecodedRstFrames;
        if (maxDecodedRstFramesPerWindow == null)
        {
            // Only enable by default on the server.
            if (IsServer())
            {
                maxDecodedRstFrames = DEFAULT_MAX_RST_FRAMES_PER_CONNECTION_FOR_SERVER;
            }
            else
            {
                maxDecodedRstFrames = 0;
            }
        }
        else
        {
            maxDecodedRstFrames = maxDecodedRstFramesPerWindow.Value;
        }
        if (maxDecodedRstFrames > 0 && maxDecodedRstFramesSecondsPerWindow > 0)
        {
            decoder = new Http2MaxRstFrameDecoder(decoder, maxDecodedRstFrames, maxDecodedRstFramesSecondsPerWindow);
        }
        T handler;
        try
        {
            // Call the abstract build method
            handler = Build(decoder, encoder, initialSettings);
        }
        catch (Exception t)
        {
            encoder.Close();
            decoder.Close();
            throw new InvalidOperationException("failed to build an Http2ConnectionHandler", t);
        }

        // Setup post build options
        handler.GracefulShutdownTimeoutMillis(gracefulShutdownTimeoutMillis);
        if (handler.Decoder().FrameListener() == null)
        {
            handler.Decoder().FrameListener(frameListener);
        }
        return handler;
    }

    private static void EnforceMaxActiveStreams(IHttp2Connection connection, Http2Settings initialSettings)
    {
        long? maxConcurrentStreams = initialSettings.MaxConcurrentStreams();
        if (maxConcurrentStreams != null)
        {
            connection.Remote().MaxActiveStreams((int) Math.Min(maxConcurrentStreams.Value, int.MaxValue));
        }
    }

    /**
     * Implement this method to create a new {@link Http2ConnectionHandler} or its subtype instance.
     * <p>
     * The return of this method will be subject to the following:
     * <ul>
     *   <li>{@link #frameListener(Http2FrameListener)} will be set if not already set in the decoder</li>
     *   <li>{@link #gracefulShutdownTimeoutMillis(long)} will always be set</li>
     * </ul>
     */
    protected abstract T Build(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                               Http2Settings initialSettings);

    /**
     * Returns {@code this}.
     */
    protected B Self()
    {
        return (B) (object) this;
    }

    private void EnforceNonCodecConstraints(string rejected)
    {
        EnforceConstraint(rejected, "server/connection", decoder);
        EnforceConstraint(rejected, "server/connection", encoder);
    }

    private static void EnforceConstraint(string methodName, string rejectorName, object value)
    {
        if (value != null)
        {
            throw new InvalidOperationException(
                    methodName + "() cannot be called because " + rejectorName + "() has been called already.");
        }
    }
}
