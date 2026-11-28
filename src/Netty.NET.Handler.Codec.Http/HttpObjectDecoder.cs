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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Decodes {@link ByteBuf}s into {@link HttpMessage}s and
 * {@link HttpContent}s.
 *
 * <h3>Parameters that prevents excessive memory consumption</h3>
 * <table border="1">
 * <tr>
 * <th>Name</th><th>Default value</th><th>Meaning</th>
 * </tr>
 * <tr>
 * <td>{@code maxInitialLineLength}</td>
 * <td>{@value #DEFAULT_MAX_INITIAL_LINE_LENGTH}</td>
 * <td>The maximum length of the initial line
 *     (e.g. {@code "GET / HTTP/1.0"} or {@code "HTTP/1.0 200 OK"})
 *     If the length of the initial line exceeds this value, a
 *     {@link TooLongHttpLineException} will be raised.</td>
 * </tr>
 * <tr>
 * <td>{@code maxHeaderSize}</td>
 * <td>{@value #DEFAULT_MAX_HEADER_SIZE}</td>
 * <td>The maximum length of all headers.  If the sum of the length of each
 *     header exceeds this value, a {@link TooLongHttpHeaderException} will be raised.</td>
 * </tr>
 * <tr>
 * <td>{@code maxChunkSize}</td>
 * <td>{@value #DEFAULT_MAX_CHUNK_SIZE}</td>
 * <td>The maximum length of the content or each chunk.  If the content length
 *     (or the length of each chunk) exceeds this value, the content or chunk
 *     will be split into multiple {@link HttpContent}s whose length is
 *     {@code maxChunkSize} at maximum.</td>
 * </tr>
 * </table>
 *
 * <h3>Parameters that control parsing behavior</h3>
 * <table border="1">
 * <tr>
 * <th>Name</th><th>Default value</th><th>Meaning</th>
 * </tr>
 * <tr>
 * <td>{@code allowDuplicateContentLengths}</td>
 * <td>{@value #DEFAULT_ALLOW_DUPLICATE_CONTENT_LENGTHS}</td>
 * <td>When set to {@code false}, will reject any messages that contain multiple Content-Length header fields.
 *     When set to {@code true}, will allow multiple Content-Length headers only if they are all the same decimal value.
 *     The duplicated field-values will be replaced with a single valid Content-Length field.
 *     See <a href="https://tools.ietf.org/html/rfc7230#section-3.3.2">RFC 7230, Section 3.3.2</a>.</td>
 * </tr>
 * <tr>
 * <td>{@code allowPartialChunks}</td>
 * <td>{@value #DEFAULT_ALLOW_PARTIAL_CHUNKS}</td>
 * <td>If the length of a chunk exceeds the {@link ByteBuf}s readable bytes and {@code allowPartialChunks}
 *     is set to {@code true}, the chunk will be split into multiple {@link HttpContent}s.
 *     Otherwise, if the chunk size does not exceed {@code maxChunkSize} and {@code allowPartialChunks}
 *     is set to {@code false}, the {@link ByteBuf} is not decoded into an {@link HttpContent} until
 *     the readable bytes are greater or equal to the chunk size.</td>
 * </tr>
 * </table>
 *
 * <h3>Chunked Content</h3>
 *
 * If the content of an HTTP message is greater than {@code maxChunkSize} or
 * the transfer encoding of the HTTP message is 'chunked', this decoder
 * generates one {@link HttpMessage} instance and its following
 * {@link HttpContent}s per single HTTP message to avoid excessive memory
 * consumption. For example, the following HTTP message:
 * <pre>
 * GET / HTTP/1.1
 * Transfer-Encoding: chunked
 *
 * 1a
 * abcdefghijklmnopqrstuvwxyz
 * 10
 * 1234567890abcdef
 * 0
 * Content-MD5: ...
 * <i>[blank line]</i>
 * </pre>
 * triggers {@link HttpRequestDecoder} to generate 3 objects:
 * <ol>
 * <li>An {@link HttpRequest},</li>
 * <li>The first {@link HttpContent} whose content is {@code 'abcdefghijklmnopqrstuvwxyz'},</li>
 * <li>The second {@link LastHttpContent} whose content is {@code '1234567890abcdef'}, which marks
 * the end of the content.</li>
 * </ol>
 *
 * If you prefer not to handle {@link HttpContent}s by yourself for your
 * convenience, insert {@link HttpObjectAggregator} after this decoder in the
 * {@link ChannelPipeline}.  However, please note that your server might not
 * be as memory efficient as without the aggregator.
 *
 * <h3>Extensibility</h3>
 *
 * Please note that this decoder is designed to be extended to implement
 * a protocol derived from HTTP, such as
 * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
 * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>.
 * To implement the decoder of such a derived protocol, extend this class and
 * implement all abstract methods properly.
 *
 * <h3>Header Validation</h3>
 *
 * It is recommended to always enable header validation.
 * <p>
 * Without header validation, your system can become vulnerable to
 * <a href="https://cwe.mitre.org/data/definitions/113.html">
 *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
 * </a>.
 * <p>
 * This recommendation stands even when both peers in the HTTP exchange are trusted,
 * as it helps with defence-in-depth.
 */
// CLR: the deprecated validateHeaders field is used here, which javac does not report inside the same outermost class
// (JLS 9.6.4.6: https://docs.oracle.com/javase/specs/jls/se17/html/jls-9.html#jls-9.6.4.6) while C# reports every
// reference to an [Obsolete] member (CS0618: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0618),
// so the warning is disabled for this file, as codec-stomp's StompSubframeDecoder does.
#pragma warning disable CS0618
// CLR: the five @Deprecated constructors whose @deprecated texts name HttpObjectDecoder(HttpDecoderConfig) are not
// ported (owner decision 2026-11-16); the deprecated validateHeaders field names no replacement and is ported with
// [Obsolete]. The switch sections that fall through end with goto case, and a try block that falls through into the
// next state jumps after it ("control can't fall through from one switch section to the next",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements;
// docs/codec-compression-porting.md). Runnable is Action and the anonymous Runnables/ByteProcessor are lambdas; the
// ByteProcessor parameter of forEachByte is Func<byte, bool> (Common ByteProcessor). AtomicBoolean is the Common
// AtomicBoolean, whose Set is lazySet. ByteBuf.array()/arrayOffset() of the heap scratch buffer are the array segment of
// the line's readable memory (LineArray; docs/buffer-porting.md, Array extraction), copied when the storage is not a
// managed array. Java byte sign (Java byte sign rule): SP_LENIENT_BYTES, LATIN_WHITESPACE and ISO_CONTROL_OR_WHITESPACE
// are indexed by the signed Java byte + 128, so every lookup reads the octet as sbyte; Character.isWhitespace(byte) and
// Character.isISOControl(byte) widen the signed byte to an int code point, so the negative bytes (0x80-0xFF) are neither
// whitespace nor control in those tables, and the Java loops stop before Byte.MAX_VALUE (0x7F stays false); the hex
// text and decimal of an octet in exception messages print the signed value. new String(bytes, 0, start, length) (one
// char per octet, hibyte 0) is Encoding.Latin1.GetString, String.trim() is HttpUtil.TrimJava and Long.parseLong is
// HttpUtil.ParseLong. Character.isISOControl(int) and Character.isWhitespace(int) keep the Java definitions in
// IsISOControl/IsJavaWhitespace (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html).
// IllegalArgumentException is ArgumentException, IllegalStateException InvalidOperationException, assert
// Debug.Assert. The inner class LineParser reads HttpObjectDecoder.this.currentState through the decoder it receives.
// HttpMessage/HttpResponse/HttpContent/LastHttpContent are the I-prefixed interfaces.
public abstract class HttpObjectDecoder : ByteToMessageDecoder
{
    public const int DEFAULT_MAX_INITIAL_LINE_LENGTH = 4096;
    public const int DEFAULT_MAX_HEADER_SIZE = 8192;
    public const bool DEFAULT_CHUNKED_SUPPORTED = true;
    public const bool DEFAULT_ALLOW_PARTIAL_CHUNKS = true;
    public const int DEFAULT_MAX_CHUNK_SIZE = 8192;
    public const bool DEFAULT_VALIDATE_HEADERS = true;
    public const int DEFAULT_INITIAL_BUFFER_SIZE = 128;
    public const bool DEFAULT_ALLOW_DUPLICATE_CONTENT_LENGTHS = false;
    public static readonly bool DEFAULT_STRICT_LINE_PARSING =
            SystemPropertyUtil.GetBoolean("io.netty.handler.codec.http.defaultStrictLineParsing", true);
    public const string PROP_RFC9112_TRANSFER_ENCODING = "io.netty.handler.codec.http.rfc9112TransferEncoding";
    public static readonly bool RFC9112_TRANSFER_ENCODING =
            SystemPropertyUtil.GetBoolean(PROP_RFC9112_TRANSFER_ENCODING, true);

    private static readonly Action THROW_INVALID_CHUNK_EXTENSION = () => throw new InvalidChunkExtensionException();

    private static readonly Action THROW_INVALID_LINE_SEPARATOR = () => throw new InvalidLineSeparatorException();
    private static readonly TransferEncodingNotAllowedException TRANSFER_ENCODING_NOT_ALLOWED =
            ThrowableUtil.UnknownStackTrace(
                    new TransferEncodingNotAllowedException(
                            "The Transfer-Encoding header is only allowed in HTTP/1.1 or newer"),
                    typeof(HttpObjectDecoder),
                    "readHeaders(ByteBuf)");

    private readonly int maxChunkSize;
    private readonly bool chunkedSupported;
    private readonly bool allowPartialChunks;
    /**
     * This field is no longer used. It is only kept around for backwards compatibility purpose.
     */
    [Obsolete("This field is no longer used. It is only kept around for backwards compatibility purpose.")]
    protected readonly bool validateHeaders;
    protected readonly IHttpHeadersFactory headersFactory;
    protected readonly IHttpHeadersFactory trailersFactory;
    private readonly bool allowDuplicateContentLengths;
    private readonly bool useRfc9112TransferEncoding;
    private readonly ByteBuf parserScratchBuffer;
    private readonly Action defaultStrictCRLFCheck;
    private readonly HeaderParser headerParser;
    private readonly LineParser lineParser;

    private IHttpMessage message;
    private long chunkSize;
    private long contentLength = long.MinValue;
    private bool chunked;
    private bool isSwitchingToNonHttp1Protocol;

    private readonly AtomicBoolean resetRequested = new AtomicBoolean();

    // These will be updated by splitHeader(...)
    private AsciiString name;
    private string value;
    private ILastHttpContent trailer;

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        try
        {
            parserScratchBuffer.Release();
        }
        finally
        {
            base.HandlerRemoved0(ctx);
        }
    }

    /**
     * The internal state of {@link HttpObjectDecoder}.
     * <em>Internal use only</em>.
     */
    private enum State
    {
        SKIP_INITIAL_LINE_CHARS,
        SKIP_CONTROL_CHARS,
        READ_INITIAL,
        READ_HEADER,
        READ_VARIABLE_LENGTH_CONTENT,
        READ_FIXED_LENGTH_CONTENT,
        READ_CHUNK_SIZE,
        READ_CHUNKED_CONTENT,
        READ_CHUNK_DELIMITER,
        READ_CHUNK_FOOTER,
        BAD_MESSAGE,
        UPGRADED
    }

    private State currentState = State.SKIP_INITIAL_LINE_CHARS;

    /**
     * Creates a new instance with the default
     * {@code maxInitialLineLength (4096)}, {@code maxHeaderSize (8192)}, and
     * {@code maxChunkSize (8192)}.
     */
    protected HttpObjectDecoder() : this(new HttpDecoderConfig())
    {
    }

    /**
     * Creates a new instance with the specified configuration.
     */
    protected HttpObjectDecoder(HttpDecoderConfig config)
    {
        CheckNotNull(config, "config");

        parserScratchBuffer = Unpooled.Buffer(config.GetInitialBufferSize());
        defaultStrictCRLFCheck = config.IsStrictLineParsing() ? THROW_INVALID_LINE_SEPARATOR : null;
        lineParser = new LineParser(this, parserScratchBuffer, config.GetMaxInitialLineLength());
        headerParser = new HeaderParser(parserScratchBuffer, config.GetMaxHeaderSize());
        maxChunkSize = config.GetMaxChunkSize();
        chunkedSupported = config.IsChunkedSupported();
        headersFactory = config.GetHeadersFactory();
        trailersFactory = config.GetTrailersFactory();
        validateHeaders = IsValidating(headersFactory);
        allowDuplicateContentLengths = config.IsAllowDuplicateContentLengths();
        allowPartialChunks = config.IsAllowPartialChunks();
        useRfc9112TransferEncoding = config.IsUseRfc9112TransferEncoding();
    }

    protected virtual bool IsValidating(IHttpHeadersFactory headersFactory)
    {
        if (headersFactory is DefaultHttpHeadersFactory)
        {
            DefaultHttpHeadersFactory builder = (DefaultHttpHeadersFactory) headersFactory;
            return builder.IsValidatingHeaderNames() || builder.IsValidatingHeaderValues();
        }

        return true; // We can't actually tell in this case, but we assume some validation is taking place.
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf buffer, IList<object> @out)
    {
        if (resetRequested.Get())
        {
            ResetNow();
        }

        switch (currentState)
        {
        case State.SKIP_INITIAL_LINE_CHARS:
            // Fall-through
            goto case State.READ_INITIAL;
        case State.READ_INITIAL:
            try
            {
                ByteBuf line = lineParser.Parse(buffer, defaultStrictCRLFCheck);
                if (line == null)
                {
                    return;
                }

                string[] initialLine = SplitInitialLine(line);
                Debug.Assert(initialLine.Length == 3, "initialLine::length must be 3");

                message = CreateMessage(initialLine);
                currentState = State.READ_HEADER;
                // fall-through
            }
            catch (Exception e)
            {
                @out.Add(InvalidMessage(message, buffer, e));
                return;
            }

            goto case State.READ_HEADER;
        case State.READ_HEADER:
            try
            {
                State? nextStateOrNull = ReadHeaders(buffer);
                if (nextStateOrNull == null)
                {
                    return;
                }

                State nextState = nextStateOrNull.Value;
                currentState = nextState;

                switch (nextState)
                {
                case State.SKIP_CONTROL_CHARS:
                    // fast-path
                    // No content is expected.
                    AddCurrentMessage(@out);
                    @out.Add(ILastHttpContent.EMPTY_LAST_CONTENT);
                    ResetNow();
                    return;
                case State.READ_CHUNK_SIZE:
                    if (!chunkedSupported)
                    {
                        throw new ArgumentException("Chunked messages not supported");
                    }

                    // Chunked encoding - generate HttpMessage first.  HttpChunks will follow.
                    AddCurrentMessage(@out);
                    return;
                default:
                    /*
                     * RFC 7230, 3.3.3 (https://tools.ietf.org/html/rfc7230#section-3.3.3) states that if a
                     * request does not have either a transfer-encoding or a content-length header then the message body
                     * length is 0. However, for a response the body length is the number of octets received prior to the
                     * server closing the connection. So we treat this as variable length chunked encoding.
                     */
                    if (contentLength == 0 || contentLength == -1 && IsDecodingRequest())
                    {
                        AddCurrentMessage(@out);
                        @out.Add(ILastHttpContent.EMPTY_LAST_CONTENT);
                        ResetNow();
                        return;
                    }

                    Debug.Assert(nextState == State.READ_FIXED_LENGTH_CONTENT ||
                            nextState == State.READ_VARIABLE_LENGTH_CONTENT);

                    AddCurrentMessage(@out);

                    if (nextState == State.READ_FIXED_LENGTH_CONTENT)
                    {
                        // chunkSize will be decreased as the READ_FIXED_LENGTH_CONTENT state reads data chunk by chunk.
                        chunkSize = contentLength;
                    }

                    // We return here, this forces decode to be called again where we will decode the content
                    return;
                }
            }
            catch (Exception e)
            {
                @out.Add(InvalidMessage(message, buffer, e));
                return;
            }
        case State.READ_VARIABLE_LENGTH_CONTENT:
        {
            // Keep reading data as a chunk until the end of connection is reached.
            int toRead = Math.Min(buffer.ReadableBytes, maxChunkSize);
            if (toRead > 0)
            {
                ByteBuf content = buffer.ReadRetainedSlice(toRead);
                @out.Add(new DefaultHttpContent(content));
            }

            return;
        }
        case State.READ_FIXED_LENGTH_CONTENT:
        {
            int readLimit = buffer.ReadableBytes;

            // Check if the buffer is readable first as we use the readable byte count
            // to create the HttpChunk. This is needed as otherwise we may end up with
            // create an HttpChunk instance that contains an empty buffer and so is
            // handled like it is the last HttpChunk.
            //
            // See https://github.com/netty/netty/issues/433
            if (readLimit == 0)
            {
                return;
            }

            int toRead = Math.Min(readLimit, maxChunkSize);
            if (toRead > chunkSize)
            {
                toRead = (int) chunkSize;
            }

            ByteBuf content = buffer.ReadRetainedSlice(toRead);
            chunkSize -= toRead;

            if (chunkSize == 0)
            {
                // Read all content.
                @out.Add(new DefaultLastHttpContent(content, trailersFactory));
                ResetNow();
            }
            else
            {
                @out.Add(new DefaultHttpContent(content));
            }

            return;
        }
        /*
         * Everything else after this point takes care of reading chunked content. Basically, read chunk size,
         * read chunk, read and ignore the CRLF and repeat until 0
         */
        case State.READ_CHUNK_SIZE:
            try
            {
                ByteBuf line = lineParser.Parse(buffer, THROW_INVALID_CHUNK_EXTENSION);
                if (line == null)
                {
                    return;
                }

                CheckChunkExtensions(line);
                byte[] lineArray = LineArray(line, out int lineStart);
                int chunkSize = GetChunkSize(lineArray, lineStart, line.ReadableBytes);
                this.chunkSize = chunkSize;

                if (chunkSize == 0)
                {
                    currentState = State.READ_CHUNK_FOOTER;
                    return;
                }

                currentState = State.READ_CHUNKED_CONTENT;
                // fall-through
            }
            catch (Exception e)
            {
                @out.Add(InvalidChunk(buffer, e));
                return;
            }

            goto case State.READ_CHUNKED_CONTENT;
        case State.READ_CHUNKED_CONTENT:
        {
            Debug.Assert(chunkSize <= int.MaxValue);
            int toRead = Math.Min((int) chunkSize, maxChunkSize);
            if (!allowPartialChunks && buffer.ReadableBytes < toRead)
            {
                return;
            }

            toRead = Math.Min(toRead, buffer.ReadableBytes);
            if (toRead == 0)
            {
                return;
            }

            IHttpContent chunk = new DefaultHttpContent(buffer.ReadRetainedSlice(toRead));
            chunkSize -= toRead;

            @out.Add(chunk);

            if (chunkSize != 0)
            {
                return;
            }

            currentState = State.READ_CHUNK_DELIMITER;
            // fall-through
            goto case State.READ_CHUNK_DELIMITER;
        }
        case State.READ_CHUNK_DELIMITER:
        {
            if (buffer.ReadableBytes >= 2)
            {
                int rIdx = buffer.ReaderIndex;
                if (buffer.GetByte(rIdx) == HttpConstants.CR &&
                        buffer.GetByte(rIdx + 1) == HttpConstants.LF)
                {
                    buffer.SkipBytes(2);
                    currentState = State.READ_CHUNK_SIZE;
                }
                else
                {
                    @out.Add(InvalidChunk(buffer, new InvalidChunkTerminationException()));
                }
            }

            return;
        }
        case State.READ_CHUNK_FOOTER:
            try
            {
                ILastHttpContent trailer = ReadTrailingHeaders(buffer);
                if (trailer == null)
                {
                    return;
                }

                @out.Add(trailer);
                ResetNow();
                return;
            }
            catch (Exception e)
            {
                @out.Add(InvalidChunk(buffer, e));
                return;
            }
        case State.BAD_MESSAGE:
        {
            // Keep discarding until disconnection.
            buffer.SkipBytes(buffer.ReadableBytes);
            break;
        }
        case State.UPGRADED:
        {
            int readableBytes = buffer.ReadableBytes;
            if (readableBytes > 0)
            {
                // Keep on consuming as otherwise we may trigger an DecoderException,
                // other handler will replace this codec with the upgraded protocol codec to
                // take the traffic over at some point then.
                // See https://github.com/netty/netty/issues/2173
                @out.Add(buffer.ReadBytes(readableBytes));
            }

            break;
        }
        default:
            break;
        }
    }

    protected override void DecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        base.DecodeLast(ctx, @in, @out);

        if (resetRequested.Get())
        {
            // If a reset was requested by decodeLast() we need to do it now otherwise we may produce a
            // LastHttpContent while there was already one.
            ResetNow();
        }

        // Handle the last unfinished message.
        switch (currentState)
        {
            case State.READ_VARIABLE_LENGTH_CONTENT:
                if (!chunked && !@in.IsReadable())
                {
                    // End of connection.
                    @out.Add(ILastHttpContent.EMPTY_LAST_CONTENT);
                    ResetNow();
                }

                return;
            case State.READ_HEADER:
                // If we are still in the state of reading headers we need to create a new invalid message that
                // signals that the connection was closed before we received the headers.
                @out.Add(InvalidMessage(message, Unpooled.EmptyBuffer,
                        new PrematureChannelClosureException("Connection closed before received headers")));
                ResetNow();
                return;
            case State.READ_CHUNK_DELIMITER: // fall-trough
            case State.READ_CHUNK_FOOTER: // fall-trough
            case State.READ_CHUNKED_CONTENT: // fall-trough
            case State.READ_CHUNK_SIZE: // fall-trough
            case State.READ_FIXED_LENGTH_CONTENT:
                // Check if the closure of the connection signifies the end of the content.
                bool prematureClosure;

                if (IsDecodingRequest() || chunked)
                {
                    // The last request did not wait for a response.
                    prematureClosure = true;
                }
                else
                {
                    // Compare the length of the received content and the 'Content-Length' header.
                    // If the 'Content-Length' header is absent, the length of the content is determined by the end of
                    // the connection, so it is perfectly fine.
                    prematureClosure = contentLength > 0;
                }

                if (!prematureClosure)
                {
                    @out.Add(ILastHttpContent.EMPTY_LAST_CONTENT);
                }

                ResetNow();
                return;
            case State.SKIP_CONTROL_CHARS: // fall-trough
            case State.SKIP_INITIAL_LINE_CHARS: // fall-trough
            case State.READ_INITIAL:// fall-trough
            case State.BAD_MESSAGE: // fall-trough
            case State.UPGRADED: // fall-trough
                // Do nothing
                break;
            default:
                throw new InvalidOperationException("Unhandled state " + currentState);
        }
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is HttpExpectationFailedEvent)
        {
            switch (currentState)
            {
            case State.READ_FIXED_LENGTH_CONTENT:
            case State.READ_VARIABLE_LENGTH_CONTENT:
            case State.READ_CHUNK_SIZE:
                Reset();
                break;
            default:
                break;
            }
        }

        base.UserEventTriggered(ctx, evt);
    }

    private void AddCurrentMessage(IList<object> @out)
    {
        IHttpMessage message = this.message;
        Debug.Assert(message != null);
        this.message = null;
        @out.Add(message);
    }

    protected virtual bool IsContentAlwaysEmpty(IHttpMessage msg)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse res = (IHttpResponse) msg;
            HttpResponseStatus status = res.Status();
            int code = status.Code();
            HttpStatusClass statusClass = status.CodeClass();

            // Correctly handle return codes of 1xx.
            //
            // See:
            //     - https://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html Section 4.4
            //     - https://github.com/netty/netty/issues/222
            if (statusClass == HttpStatusClass.INFORMATIONAL)
            {
                // One exception: Hixie 76 websocket handshake response
                return !(code == 101 && !res.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT)
                         && res.Headers().Contains(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET, true));
            }

            switch (code)
            {
            case 204:
                case 304:
                return true;
            default:
                return false;
            }
        }

        return false;
    }

    /**
     * Returns true if the server switched to a different protocol than HTTP/1.0 or HTTP/1.1, e.g. HTTP/2 or Websocket.
     * Returns false if the upgrade happened in a different layer, e.g. upgrade from HTTP/1.1 to HTTP/1.1 over TLS.
     */
    protected virtual bool IsSwitchingToNonHttp1Protocol(IHttpResponse msg)
    {
        if (msg.Status().Code() != HttpResponseStatus.SWITCHING_PROTOCOLS.Code())
        {
            return false;
        }

        string newProtocol = msg.Headers().Get(HttpHeaderNames.UPGRADE);
        return newProtocol == null ||
                !newProtocol.Contains(HttpVersion.HTTP_1_0.Text(), StringComparison.Ordinal) &&
                !newProtocol.Contains(HttpVersion.HTTP_1_1.Text(), StringComparison.Ordinal);
    }

    /**
     * Resets the state of the decoder so that it is ready to decode a new message.
     * This method is useful for handling a rejected request with {@code Expect: 100-continue} header.
     */
    public void Reset()
    {
        resetRequested.Set(true);
    }

    private void ResetNow()
    {
        message = null;
        name = null;
        value = null;
        ClearContentLength();
        chunked = false;
        lineParser.Reset();
        headerParser.Reset();
        trailer = null;

        if (isSwitchingToNonHttp1Protocol)
        {
            isSwitchingToNonHttp1Protocol = false;
            currentState = State.UPGRADED;
            return;
        }

        resetRequested.Set(false);
        currentState = State.SKIP_INITIAL_LINE_CHARS;
    }

    private IHttpMessage InvalidMessage(IHttpMessage current, ByteBuf @in, Exception cause)
    {
        currentState = State.BAD_MESSAGE;
        message = null;
        trailer = null;

        // Advance the readerIndex so that ByteToMessageDecoder does not complain
        // when we produced an invalid message without consuming anything.
        @in.SkipBytes(@in.ReadableBytes);

        if (current == null)
        {
            current = CreateInvalidMessage();
        }

        current.SetDecoderResult(global::Netty.NET.Handler.Codec.DecoderResult.Failure(cause));

        return current;
    }

    private static void CheckChunkExtensions(ByteBuf line)
    {
        int extensionsStart = line.BytesBefore((byte) ';');
        if (extensionsStart == -1)
        {
            return;
        }

        HttpChunkLineValidatingByteProcessor processor = new HttpChunkLineValidatingByteProcessor();
        line.ForEachByte(processor.Process);
        processor.Finish();
    }

    private IHttpContent InvalidChunk(ByteBuf @in, Exception cause)
    {
        currentState = State.BAD_MESSAGE;
        message = null;
        trailer = null;

        // Advance the readerIndex so that ByteToMessageDecoder does not complain
        // when we produced an invalid message without consuming anything.
        @in.SkipBytes(@in.ReadableBytes);

        IHttpContent chunk = new DefaultLastHttpContent(Unpooled.EmptyBuffer);
        chunk.SetDecoderResult(global::Netty.NET.Handler.Codec.DecoderResult.Failure(cause));
        return chunk;
    }

    // CLR: ByteBuf.array() with arrayOffset() + readerIndex() of a parsed line (see the class comment).
    private static byte[] LineArray(ByteBuf line, out int start)
    {
        ReadOnlyMemory<byte> memory = line.AsReadOnlyMemory(line.ReaderIndex, line.ReadableBytes);
        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            start = segment.Offset;
            return segment.Array;
        }

        start = 0;
        return memory.ToArray();
    }

    private State? ReadHeaders(ByteBuf buffer)
    {
        IHttpMessage message = this.message;
        HttpHeaders headers = message.Headers();

        HeaderParser headerParser = this.headerParser;

        ByteBuf line = headerParser.Parse(buffer, defaultStrictCRLFCheck);
        if (line == null)
        {
            return null;
        }

        int lineLength = line.ReadableBytes;

        while (lineLength > 0)
        {
            byte[] lineContent = LineArray(line, out int startLine);
            byte firstChar = lineContent[startLine];
            if (name != null && (firstChar == ' ' || firstChar == '\t'))
            {
                //please do not make one line from below code
                //as it breaks +XX:OptimizeStringConcat optimization
                string trimmedLine = HttpUtil.TrimJava(LangAsciiString(lineContent, startLine, lineLength));
                string valueStr = value;
                value = valueStr + ' ' + trimmedLine;
            }
            else
            {
                if (name != null)
                {
                    headers.Add(name, value);
                }

                SplitHeader(lineContent, startLine, lineLength);
            }

            line = headerParser.Parse(buffer, defaultStrictCRLFCheck);
            if (line == null)
            {
                return null;
            }

            lineLength = line.ReadableBytes;
        }

        // Add the last header.
        if (name != null)
        {
            headers.Add(name, value);
        }

        // reset name and value fields
        name = null;
        value = null;

        // Done parsing initial line and headers. Set decoder result.
        HttpMessageDecoderResult decoderResult = new HttpMessageDecoderResult(lineParser.size, headerParser.size);
        message.SetDecoderResult(decoderResult);

        IList<string> contentLengthFields = headers.GetAll(HttpHeaderNames.CONTENT_LENGTH);
        if (contentLengthFields.Count != 0)
        {
            HttpVersion version = message.ProtocolVersion();
            bool isHttp10OrEarlier = version.MajorVersion() < 1 || (version.MajorVersion() == 1
                    && version.MinorVersion() == 0);
            // Guard against multiple Content-Length headers as stated in
            // https://tools.ietf.org/html/rfc7230#section-3.3.2:
            contentLength = HttpUtil.NormalizeAndGetContentLength(contentLengthFields,
                    isHttp10OrEarlier, allowDuplicateContentLengths);
            if (contentLength != -1)
            {
                string lengthValue = HttpUtil.TrimJava(contentLengthFields[0]);
                if (contentLengthFields.Count > 1 || // don't unnecessarily re-order headers
                        !IsLengthEqual(lengthValue, contentLength))
                {
                    headers.Set(HttpHeaderNames.CONTENT_LENGTH, contentLength);
                }
            }
        }
        else
        {
            // We know the content length if it's a Web Socket message even if
            // Content-Length header is missing.
            contentLength = HttpUtil.GetWebSocketContentLength(message);
        }

        if (!IsDecodingRequest() && message is IHttpResponse)
        {
            IHttpResponse res = (IHttpResponse) message;
            this.isSwitchingToNonHttp1Protocol = IsSwitchingToNonHttp1Protocol(res);
        }

        if (IsContentAlwaysEmpty(message))
        {
            HttpUtil.SetTransferEncodingChunked(message, false);
            return State.SKIP_CONTROL_CHARS;
        }

        bool hasTransferEncoding = message.Headers().Contains(HttpHeaderNames.TRANSFER_ENCODING);
        if (hasTransferEncoding &&
                message.ProtocolVersion() != HttpVersion.HTTP_1_1 &&
                useRfc9112TransferEncoding)
        {
            // The Transfer-Encoding header is not permitted at all with HTTP protocols older than 1.1,
            // and such requests must be rejected.
            throw TRANSFER_ENCODING_NOT_ALLOWED;
        }

        bool isTransferEncodingChunked = HttpUtil.IsTransferEncodingChunked(message);
        // Unlike responses, requests without a final chunked coding cannot use connection close for framing.
        // See https://datatracker.ietf.org/doc/html/rfc9112#section-6.3-4
        if (hasTransferEncoding && !isTransferEncodingChunked && IsDecodingRequest())
        {
            throw new ArgumentException("The final transfer coding must be chunked for HTTP requests");
        }

        if (isTransferEncodingChunked)
        {
            this.chunked = true;
            // The "chunked must be the last encoding" rule (RFC 9112 6.1) is not specific to
            // HTTP/1.1 -- it applies to any message that carries a Transfer-Encoding header at
            // all. This validation must not be gated on protocolVersion(), since a decoder
            // configured with useRfc9112TransferEncoding(false) (see HttpDecoderConfig) can
            // still reach this point for HTTP/1.0 and other non-1.1 messages, and the ordering
            // requirement applies to those just as much as it does to HTTP/1.1.
            // See https://datatracker.ietf.org/doc/html/rfc9112#name-message-body-length
            if (!IsLastTransferEncodingChunked(headers.GetAll(HttpHeaderNames.TRANSFER_ENCODING)))
            {
                throw new ArgumentException(
                        "chunked must be the last encoding present in the Transfer-Encoding header");
            }

            if (message.ProtocolVersion() == HttpVersion.HTTP_1_1)
            {
                if (contentLengthFields.Count != 0)
                {
                    HandleTransferEncodingChunkedWithContentLength(message);
                }
            }

            return State.READ_CHUNK_SIZE;
        }

        if (contentLength >= 0)
        {
            return State.READ_FIXED_LENGTH_CONTENT;
        }

        return State.READ_VARIABLE_LENGTH_CONTENT;
    }

    private static bool IsLastTransferEncodingChunked(IList<string> transferEncodingFields)
    {
        bool chunkedSeen = false;
        bool lastChunked = false;
        int chunkedLength = HttpHeaderValues.CHUNKED.Length();

        for (int i = 0; i < transferEncodingFields.Count; ++i)
        {
            string value = transferEncodingFields[i];
            int start = 0;

            while (start <= value.Length)
            {
                int comma = value.IndexOf(',', start);
                int end = comma == -1 ? value.Length : comma;

                while (start < end && (value[start] == ' ' || value[start] == '\t'))
                {
                    ++start;
                }

                while (end > start && (value[end - 1] == ' ' || value[end - 1] == '\t'))
                {
                    --end;
                }

                if (start < end)
                {
                    lastChunked = end - start == chunkedLength &&
                            HttpHeaderValues.CHUNKED.RegionMatches(true, 0, new StringCharSequence(value), start,
                                    chunkedLength);
                    if (lastChunked)
                    {
                        if (chunkedSeen)
                        {
                            throw new ArgumentException(
                                    "chunked transfer coding must not be applied more than once");
                        }

                        chunkedSeen = true;
                    }
                }

                if (comma == -1)
                {
                    break;
                }

                start = comma + 1;
            }
        }

        return lastChunked;
    }

    private static bool IsLengthEqual(string lengthValue, long contentLength)
    {
        try
        {
            return HttpUtil.ParseLong(lengthValue) == contentLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /**
     * Invoked when a message with both a "Transfer-Encoding: chunked" and a "Content-Length" header field is detected.
     * The default behavior is to throw a {@link ContentLengthNotAllowedException} exception, but this method could
     * be overridden to change the behavior (to, e.g., remove the {@code Content-Length} header value.
     * <p>
     * See: <a href="https://www.rfc-editor.org/rfc/rfc9112.html#section-6.1-15">RFC 9112, Section 6.1-15</a>.
     * <pre>
     *     A server MAY reject a request that contains both Content-Length and Transfer-Encoding
     *     or process such a request in accordance with the Transfer-Encoding alone.
     *     Regardless, the server MUST close the connection after responding to such a request
     *     to avoid the potential attacks.
     * </pre>
     * Since Netty itself cannot track the request/response pairing, it cannot guarantee that the connection is closed
     * immediately after the response is sent. As such, it is safer to immediately reject the request.
     * <p>
     * <strong>Note:</strong> RFC 7230 (the previous HTTP/1.1 RFC) allowed the {@code Content-Length} header to simply
     * be ignored, in the presence of a {@code Transfer-Encoding} header, but this practice is now obsolete
     * and considered unsafe.
     * The RFC 7230 behavior can be restored in the following ways:
     * <ul>
     *     <li>
     *         Process-wide, by setting the {@value PROP_RFC9112_TRANSFER_ENCODING} system property to {@code false}.
     *     </li>
     *     <li>
     *         Configured for a specific decoder, by setting
     *         {@link HttpDecoderConfig#setUseRfc9112TransferEncoding(boolean)} to {@code false}.
     *     </li>
     *     <li>
     *         Hard-coded for a specific decoder, by overriding this method with an implementation like the following:
     *         <pre>{@code
     * @Override
     * protected void handleTransferEncodingChunkedWithContentLength(HttpMessage message) {
     *     clearContentLength();
     *     message.headers().remove(HttpHeaderNames.CONTENT_LENGTH);
     * }
     *         }</pre>
     *     </li>
     * </ul>
     * <p>
     * <strong>Note:</strong> This method is only called for {@code HTTP/1.1} requests. Under the
     * default configuration, earlier HTTP protocol versions carrying a {@code Transfer-Encoding}
     * header are rejected before reaching this point. However, if
     * {@link HttpDecoderConfig#setUseRfc9112TransferEncoding(boolean)} is set to {@code false}
     * (see above), that rejection does not happen — a non-{@code HTTP/1.1} message can then
     * legitimately carry {@code Transfer-Encoding} without being rejected for it, though this
     * method specifically remains {@code HTTP/1.1}-only and is not invoked for such messages.
     */
    protected virtual void HandleTransferEncodingChunkedWithContentLength(IHttpMessage message)
    {
        ClearContentLength();

        if (useRfc9112TransferEncoding)
        {
            throw new ContentLengthNotAllowedException(
                    "Content-Length are not allowed in HTTP/1.1 messages that contains a Transfer-Encoding header.");
        }
        else
        {
            message.Headers().Remove(HttpHeaderNames.CONTENT_LENGTH);

            if (IsDecodingRequest())
            {
                HttpUtil.SetKeepAlive(message, false);
            }
        }
    }

    protected void ClearContentLength()
    {
        contentLength = long.MinValue;
    }

    private ILastHttpContent ReadTrailingHeaders(ByteBuf buffer)
    {
        HeaderParser headerParser = this.headerParser;
        ByteBuf line = headerParser.Parse(buffer, defaultStrictCRLFCheck);
        if (line == null)
        {
            return null;
        }

        ILastHttpContent trailer = this.trailer;
        int lineLength = line.ReadableBytes;
        if (lineLength == 0 && trailer == null)
        {
            // We have received the empty line which signals the trailer is complete and did not parse any trailers
            // before. Just return an empty last content to reduce allocations.
            return ILastHttpContent.EMPTY_LAST_CONTENT;
        }

        if (trailer == null)
        {
            trailer = this.trailer = new DefaultLastHttpContent(Unpooled.EmptyBuffer, trailersFactory);
        }

        while (lineLength > 0)
        {
            byte[] lineContent = LineArray(line, out int startLine);
            byte firstChar = lineContent[startLine];
            if (name != null && (firstChar == ' ' || firstChar == '\t'))
            {
                //please do not make one line from below code
                //as it breaks +XX:OptimizeStringConcat optimization
                string trimmedLine = HttpUtil.TrimJava(LangAsciiString(lineContent, startLine, lineLength));
                string valueStr = value;
                value = valueStr + ' ' + trimmedLine;
            }
            else
            {
                if (name != null && IsPermittedTrailingHeader(name))
                {
                    trailer.TrailingHeaders().Add(name, value);
                }

                SplitHeader(lineContent, startLine, lineLength);
            }

            line = headerParser.Parse(buffer, defaultStrictCRLFCheck);
            if (line == null)
            {
                return null;
            }

            lineLength = line.ReadableBytes;
        }

        // Add the last trailer
        if (name != null && IsPermittedTrailingHeader(name))
        {
            trailer.TrailingHeaders().Add(name, value);
        }

        // reset name and value fields
        name = null;
        value = null;

        this.trailer = null;
        return trailer;
    }

    /**
     * Checks whether the given trailer field name is permitted per RFC 9110 section 6.5
     */
    private static bool IsPermittedTrailingHeader(AsciiString name)
    {
        return !HttpHeaderNames.CONTENT_LENGTH.ContentEqualsIgnoreCase(name) &&
               !HttpHeaderNames.TRANSFER_ENCODING.ContentEqualsIgnoreCase(name) &&
               !HttpHeaderNames.TRAILER.ContentEqualsIgnoreCase(name);
    }

    protected abstract bool IsDecodingRequest();

    protected abstract IHttpMessage CreateMessage(string[] initialLine);

    protected abstract IHttpMessage CreateInvalidMessage();

    private static int GetChunkSize(byte[] hex, int start, int length)
    {
        if (length == 0)
        {
            // Empty case - not allowed
            throw new FormatException("Empty chunk size");
        }

        long result = 0;

        for (int i = 0; i < length; i++)
        {
            int digit = StringUtil.DecodeHexNibble(hex[start + i]);
            if (digit == -1)
            {
                // Uncommon path:
                // We must either hit the chunk header line (implicit CRLF from the header line parser),
                // or a ';' character for chunk-extensions, or the "bad whitespace" allowed to precede ';'.
                byte b = hex[start + i];
                int j = 0;

                while (b == HttpConstants.SP || b == HttpConstants.HT)
                { // Skip BWS
                    int index = i + (++j);
                    if (index >= length)
                    {
                        throw new FormatException("Invalid chunk size; expected extensions");
                    }

                    b = hex[start + index];
                }

                if (b == ';')
                {
                    if (i == 0)
                    {
                        // Empty case - not allowed
                        throw new FormatException("Empty chunk size");
                    }

                    return (int) result;
                }

                // non-hex char fail-fast path
                throw new FormatException("Invalid character in chunk size");
            }

            result *= 16;
            result += digit;
            if (result > int.MaxValue)
            {
                throw new FormatException("Chunk size overflow: " + result.ToString(CultureInfo.InvariantCulture));
            }
        }

        return (int) result;
    }

    private string[] SplitInitialLine(ByteBuf asciiBuffer)
    {
        byte[] asciiBytes = LineArray(asciiBuffer, out int startContent);

        int end = startContent + asciiBuffer.ReadableBytes;

        byte lastByte = asciiBytes[end - 1];
        if (IsControlOrWhitespaceAsciiChar(lastByte))
        {
            if (IsDecodingRequest() || !IsOWS(lastByte))
            {
                // There should no extra control or whitespace char in case of a request.
                // In case of a response there might be a SP if there is no reason-phrase given.
                // See
                //  - https://datatracker.ietf.org/doc/html/rfc2616#section-5.1
                //  - https://datatracker.ietf.org/doc/html/rfc9112#name-status-line
                throw new ArgumentException(
                        "Illegal character in request line: 0x" + ToHexString(lastByte));
            }
        }

        int aStart = FindNonSPLenient(asciiBytes, startContent, end);
        int aEnd = FindSPLenient(asciiBytes, aStart, end);

        int bStart = FindNonSPLenient(asciiBytes, aEnd, end);
        int bEnd = FindSPLenient(asciiBytes, bStart, end);

        int cStart = FindNonSPLenient(asciiBytes, bEnd, end);
        int cEnd = FindEndOfString(asciiBytes, Math.Max(cStart - 1, startContent), end);

        return new string[]{
                SplitFirstWordInitialLine(asciiBytes, aStart, aEnd - aStart),
                SplitSecondWordInitialLine(asciiBytes, bStart, bEnd - bStart),
                cStart < cEnd ? SplitThirdWordInitialLine(asciiBytes, cStart, cEnd - cStart) : StringUtil.EMPTY_STRING};
    }

    protected virtual string SplitFirstWordInitialLine(byte[] asciiContent, int start, int length)
    {
        return LangAsciiString(asciiContent, start, length);
    }

    protected virtual string SplitSecondWordInitialLine(byte[] asciiContent, int start, int length)
    {
        return LangAsciiString(asciiContent, start, length);
    }

    protected virtual string SplitThirdWordInitialLine(byte[] asciiContent, int start, int length)
    {
        return LangAsciiString(asciiContent, start, length);
    }

    /**
     * This method shouldn't exist: look at https://bugs.openjdk.org/browse/JDK-8295496 for more context
     */
    private static string LangAsciiString(byte[] asciiContent, int start, int length)
    {
        if (length == 0)
        {
            return StringUtil.EMPTY_STRING;
        }

        // DON'T REMOVE: it helps JIT to use a simpler intrinsic stub for System::arrayCopy based on the call-site
        if (start == 0)
        {
            if (length == asciiContent.Length)
            {
                return Encoding.Latin1.GetString(asciiContent, 0, asciiContent.Length);
            }

            return Encoding.Latin1.GetString(asciiContent, 0, length);
        }

        return Encoding.Latin1.GetString(asciiContent, start, length);
    }

    private void SplitHeader(byte[] line, int start, int length)
    {
        int end = start + length;
        int nameEnd;
        int nameStart = start;
        // hoist this load out of the loop, because it won't change!
        bool isDecodingRequest = IsDecodingRequest();

        for (nameEnd = nameStart; nameEnd < end; nameEnd ++)
        {
            byte ch = line[nameEnd];
            // https://tools.ietf.org/html/rfc7230#section-3.2.4
            //
            // No whitespace is allowed between the header field-name and colon. In
            // the past, differences in the handling of such whitespace have led to
            // security vulnerabilities in request routing and response handling. A
            // server MUST reject any received request message that contains
            // whitespace between a header field-name and colon with a response code
            // of 400 (Bad Request). A proxy MUST remove any such whitespace from a
            // response message before forwarding the message downstream.
            if (ch == ':' ||
                    // In case of decoding a request we will just continue processing and header validation
                    // is done in the DefaultHttpHeaders implementation.
                    //
                    // In the case of decoding a response we will "skip" the whitespace.
                    (!isDecodingRequest && IsOWS(ch)))
            {
                break;
            }
        }

        if (nameEnd == end)
        {
            // There was no colon present at all.
            throw new ArgumentException("No colon found");
        }

        int colonEnd;

        for (colonEnd = nameEnd; colonEnd < end; colonEnd ++)
        {
            if (line[colonEnd] == ':')
            {
                colonEnd ++;
                break;
            }
        }

        name = SplitHeaderName(line, nameStart, nameEnd - nameStart);
        int valueStart = FindNonWhitespace(line, colonEnd, end);
        if (valueStart == end)
        {
            value = StringUtil.EMPTY_STRING;
        }
        else
        {
            int valueEnd = FindEndOfString(line, start, end);
            // no need to make uses of the ByteBuf's toString ASCII method here, and risk to get JIT confused
            value = LangAsciiString(line, valueStart, valueEnd - valueStart);
        }
    }

    protected virtual AsciiString SplitHeaderName(byte[] sb, int start, int length)
    {
        return new AsciiString(sb, start, length, true);
    }

    private static int FindNonSPLenient(byte[] sb, int offset, int end)
    {
        for (int result = offset; result < end; ++result)
        {
            byte c = sb[result];
            // See https://tools.ietf.org/html/rfc7230#section-3.5
            if (IsSPLenient(c))
            {
                continue;
            }
            if (IsWhitespace(c))
            {
                // Any other whitespace delimiter is invalid
                throw new ArgumentException("Invalid separator");
            }

            return result;
        }

        return end;
    }

    private static int FindSPLenient(byte[] sb, int offset, int end)
    {
        for (int result = offset; result < end; ++result)
        {
            if (IsSPLenient(sb[result]))
            {
                return result;
            }
        }

        return end;
    }

    private static readonly bool[] SP_LENIENT_BYTES;
    private static readonly bool[] LATIN_WHITESPACE;

    static HttpObjectDecoder()
    {
        // See https://tools.ietf.org/html/rfc7230#section-3.5
        SP_LENIENT_BYTES = new bool[256];
        SP_LENIENT_BYTES[128 + ' '] = true;
        SP_LENIENT_BYTES[128 + 0x09] = true;
        SP_LENIENT_BYTES[128 + 0x0B] = true;
        SP_LENIENT_BYTES[128 + 0x0C] = true;
        SP_LENIENT_BYTES[128 + 0x0D] = true;
        // TO SAVE PERFORMING Character::isWhitespace ceremony
        LATIN_WHITESPACE = new bool[256];

        for (sbyte b = sbyte.MinValue; b < sbyte.MaxValue; b++)
        {
            LATIN_WHITESPACE[128 + b] = IsJavaWhitespace(b);
        }

        ISO_CONTROL_OR_WHITESPACE = new bool[256];

        for (sbyte b = sbyte.MinValue; b < sbyte.MaxValue; b++)
        {
            ISO_CONTROL_OR_WHITESPACE[128 + b] = IsISOControl(b) || IsWhitespace(unchecked((byte) b));
        }
    }

    // CLR: Character.isWhitespace(int) for the code points of the static initializers (-128..126): only the ASCII
    // whitespace characters '\t', '\n', '\u000B', '\f', '\r', '\u001C'-'\u001F' and ' ' qualify, a negative value is
    // not a code point.
    private static bool IsJavaWhitespace(int codePoint)
    {
        return codePoint == ' ' || codePoint >= 0x09 && codePoint <= 0x0D || codePoint >= 0x1C && codePoint <= 0x1F;
    }

    // CLR: Character.isISOControl(int): "codePoint <= 0x9F && (codePoint >= 0x7F || (codePoint >>> 5 == 0))".
    private static bool IsISOControl(int codePoint)
    {
        return codePoint <= 0x9F && (codePoint >= 0x7F || (codePoint >>> 5) == 0);
    }

    // CLR: Integer.toHexString of the sign-extended Java byte (e.g. "ffffff80" for 0x80).
    private static string ToHexString(byte b)
    {
        return ((int) unchecked((sbyte) b)).ToString("x", CultureInfo.InvariantCulture);
    }

    private static bool IsSPLenient(byte c)
    {
        // See https://tools.ietf.org/html/rfc7230#section-3.5
        return SP_LENIENT_BYTES[unchecked((sbyte) c) + 128];
    }

    private static bool IsWhitespace(byte b)
    {
        return LATIN_WHITESPACE[unchecked((sbyte) b) + 128];
    }

    private static int FindNonWhitespace(byte[] sb, int offset, int end)
    {
        for (int result = offset; result < end; ++result)
        {
            byte c = sb[result];
            if (!IsWhitespace(c))
            {
                return result;
            }
            else if (!IsOWS(c))
            {
                // Only OWS is supported for whitespace
                throw new ArgumentException("Invalid separator, only a single space or horizontal tab allowed," +
                        " but received a '" + unchecked((sbyte) c).ToString(CultureInfo.InvariantCulture) + "' (0x" +
                        ToHexString(c) + ")");
            }
        }

        return end;
    }

    private static int FindEndOfString(byte[] sb, int start, int end)
    {
        for (int result = end - 1; result > start; --result)
        {
            if (!IsOWS(sb[result]))
            {
                return result + 1;
            }
        }

        return 0;
    }

    private static bool IsOWS(byte ch)
    {
        return ch == ' ' || ch == 0x09;
    }

    private class HeaderParser
    {
        protected readonly ByteBuf seq;
        protected readonly int maxLength;
        internal int size;

        internal HeaderParser(ByteBuf seq, int maxLength)
        {
            this.seq = seq;
            this.maxLength = maxLength;
        }

        public virtual ByteBuf Parse(ByteBuf buffer, Action strictCRLFCheck)
        {
            int readableBytes = buffer.ReadableBytes;
            int readerIndex = buffer.ReaderIndex;
            int maxBodySize = maxLength - this.size;
            Debug.Assert(maxBodySize >= 0);
            // adding 2 to account for both CR (if present) and LF
            // don't remove 2L: it's key to cover maxLength = Integer.MAX_VALUE
            long maxBodySizeWithCRLF = maxBodySize + 2L;
            int toProcess = (int) Math.Min(maxBodySizeWithCRLF, readableBytes);
            int toIndexExclusive = readerIndex + toProcess;
            Debug.Assert(toIndexExclusive >= readerIndex);
            int indexOfLf = buffer.IndexOf(readerIndex, toIndexExclusive, HttpConstants.LF);
            if (indexOfLf == -1)
            {
                if (readableBytes > maxBodySize)
                {
                    // TODO: Respond with Bad Request and discard the traffic
                    //    or close the connection.
                    //       No need to notify the upstream handlers - just log.
                    //       If decoding a response, just throw an exception.
                    throw NewException(maxLength);
                }

                return null;
            }

            int endOfSeqIncluded;

            if (indexOfLf > readerIndex && buffer.GetByte(indexOfLf - 1) == HttpConstants.CR)
            {
                // Drop CR if we had a CRLF pair
                endOfSeqIncluded = indexOfLf - 1;
            }
            else
            {
                if (strictCRLFCheck != null)
                {
                    strictCRLFCheck();
                }

                endOfSeqIncluded = indexOfLf;
            }

            int newSize = endOfSeqIncluded - readerIndex;
            if (newSize == 0)
            {
                seq.Clear();
                buffer.ReaderIndex = indexOfLf + 1;
                return seq;
            }

            int size = this.size + newSize;
            if (size > maxLength)
            {
                throw NewException(maxLength);
            }

            this.size = size;
            seq.Clear();
            seq.WriteBytes(buffer, readerIndex, newSize);
            buffer.ReaderIndex = indexOfLf + 1;
            return seq;
        }

        public void Reset()
        {
            size = 0;
        }

        protected virtual TooLongFrameException NewException(int maxLength)
        {
            return new TooLongHttpHeaderException("HTTP header is larger than " +
                    maxLength.ToString(CultureInfo.InvariantCulture) + " bytes.");
        }
    }

    private sealed class LineParser : HeaderParser
    {
        private readonly HttpObjectDecoder decoder;

        internal LineParser(HttpObjectDecoder decoder, ByteBuf seq, int maxLength) : base(seq, maxLength)
        {
            this.decoder = decoder;
        }

        public override ByteBuf Parse(ByteBuf buffer, Action strictCRLFCheck)
        {
            // Suppress a warning because HeaderParser.reset() is supposed to be called
            Reset();
            int readableBytes = buffer.ReadableBytes;
            if (readableBytes == 0)
            {
                return null;
            }
            if (decoder.currentState == State.SKIP_INITIAL_LINE_CHARS &&
                    SkipLineChars(buffer, readableBytes, buffer.ReaderIndex, strictCRLFCheck))
            {
                return null;
            }

            return base.Parse(buffer, strictCRLFCheck);
        }

        private bool SkipLineChars(ByteBuf buffer, int readableBytes, int readerIndex, Action strictCRLFCheck)
        {
            Debug.Assert(decoder.currentState == State.SKIP_INITIAL_LINE_CHARS);
            int maxToSkip = Math.Min(maxLength, readableBytes);
            int firstNonLineIndex = buffer.ForEachByte(readerIndex, maxToSkip,
                    strictCRLFCheck == null ? SKIP_CONTROL_CHARS_BYTES : ByteProcessor.FIND_NON_CRLF);
            if (firstNonLineIndex == -1)
            {
                buffer.SkipBytes(maxToSkip);
                if (readableBytes > maxLength)
                {
                    throw NewException(maxLength);
                }

                return true;
            }

            if (strictCRLFCheck != null)
            {
                int b = buffer.GetByte(firstNonLineIndex) & 0xFF;
                if (IsISOControl(b))
                {
                    strictCRLFCheck();
                }
            }

            // from now on we don't care about control chars
            buffer.ReaderIndex = firstNonLineIndex;
            decoder.currentState = State.READ_INITIAL;
            return false;
        }

        protected override TooLongFrameException NewException(int maxLength)
        {
            return new TooLongHttpLineException("An HTTP line is larger than " +
                    maxLength.ToString(CultureInfo.InvariantCulture) + " bytes.");
        }
    }

    private static readonly bool[] ISO_CONTROL_OR_WHITESPACE;

    private static readonly Func<byte, bool> SKIP_CONTROL_CHARS_BYTES =
            value => ISO_CONTROL_OR_WHITESPACE[128 + unchecked((sbyte) value)];

    private static bool IsControlOrWhitespaceAsciiChar(byte b)
    {
        return ISO_CONTROL_OR_WHITESPACE[128 + unchecked((sbyte) b)];
    }
}
