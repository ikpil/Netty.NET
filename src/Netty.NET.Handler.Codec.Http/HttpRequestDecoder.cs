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

using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Decodes {@link ByteBuf}s into {@link HttpRequest}s and {@link HttpContent}s.
 *
 * <h3>Parameters that prevents excessive memory consumption</h3>
 * <table border="1">
 * <tr>
 * <th>Name</th><th>Meaning</th>
 * </tr>
 * <tr>
 * <td>{@code maxInitialLineLength}</td>
 * <td>The maximum length of the initial line (e.g. {@code "GET / HTTP/1.0"})
 *     If the length of the initial line exceeds this value, a
 *     {@link TooLongHttpLineException} will be raised.</td>
 * </tr>
 * <tr>
 * <td>{@code maxHeaderSize}</td>
 * <td>The maximum length of all headers.  If the sum of the length of each
 *     header exceeds this value, a {@link TooLongHttpHeaderException} will be raised.</td>
 * </tr>
 * <tr>
 * <td>{@code maxChunkSize}</td>
 * <td>The maximum length of the content or each chunk.  If the content length
 *     exceeds this value, the transfer encoding of the decoded request will be
 *     converted to 'chunked' and the content will be split into multiple
 *     {@link HttpContent}s.  If the transfer encoding of the HTTP request is
 *     'chunked' already, each chunk will be split into smaller chunks if the
 *     length of the chunk exceeds this value.  If you prefer not to handle
 *     {@link HttpContent}s in your handler, insert {@link HttpObjectAggregator}
 *     after this decoder in the {@link ChannelPipeline}.</td>
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
// CLR: the four @Deprecated constructors whose @deprecated texts name HttpRequestDecoder(HttpDecoderConfig) are
// not ported. The header-name and method fast paths OR the octets as Java does with signed bytes; the C# bytes are
// unsigned, but every constant is pure ASCII, so a word equals its constant exactly when every octet does in both
// languages (a Java negative octet sign-extends into bits that no constant has, a C# octet of 0x80 or more sets
// bit 7 of its byte, which no constant has either), and no sign conversion is needed; the low four octets of a
// long are widened as uint (CS0675), which keeps that result. msg.getClass() == DefaultHttpRequest.class is
// GetType() == typeof(DefaultHttpRequest).
public class HttpRequestDecoder : HttpObjectDecoder
{

    private static readonly AsciiString Accept = AsciiString.Cached("Accept");
    private static readonly AsciiString Host = AsciiString.Cached("Host");
    private static readonly AsciiString Connection = AsciiString.Cached("Connection");
    private static readonly AsciiString ContentType = AsciiString.Cached("Content-Type");
    private static readonly AsciiString ContentLength = AsciiString.Cached("Content-Length");

    private const int GET_AS_INT = 'G' | 'E' << 8 | 'T' << 16;
    private const int POST_AS_INT = 'P' | 'O' << 8 | 'S' << 16 | 'T' << 24;
    private const long HTTP_1_1_AS_LONG = 'H' | 'T' << 8 | 'T' << 16 | 'P' << 24 | (long) '/' << 32 |
            (long) '1' << 40 | (long) '.' << 48 | (long) '1' << 56;

    private const long HTTP_1_0_AS_LONG = 'H' | 'T' << 8 | 'T' << 16 | 'P' << 24 | (long) '/' << 32 |
            (long) '1' << 40 | (long) '.' << 48 | (long) '0' << 56;

    private const int HOST_AS_INT = 'H' | 'o' << 8 | 's' << 16 | 't' << 24;

    private const long CONNECTION_AS_LONG_0 = 'C' | 'o' << 8 | 'n' << 16 | 'n' << 24 |
            (long) 'e' << 32 | (long) 'c' << 40 | (long) 't' << 48 | (long) 'i' << 56;

    private const short CONNECTION_AS_SHORT_1 = 'o' | 'n' << 8;

    private const long CONTENT_AS_LONG = 'C' | 'o' << 8 | 'n' << 16 | 't' << 24 |
            (long) 'e' << 32 | (long) 'n' << 40 | (long) 't' << 48 | (long) '-' << 56;

    private const int TYPE_AS_INT = 'T' | 'y' << 8 | 'p' << 16 | 'e' << 24;

    private const long LENGTH_AS_LONG = 'L' | 'e' << 8 | 'n' << 16 | 'g' << 24 |
            (long) 't' << 32 | (long) 'h' << 40;

    private const long ACCEPT_AS_LONG = 'A' | 'c' << 8 | 'c' << 16 | 'e' << 24 |
            (long) 'p' << 32 | (long) 't' << 40;

    /**
     * Creates a new instance with the default
     * {@code maxInitialLineLength (4096)}, {@code maxHeaderSize (8192)}, and
     * {@code maxChunkSize (8192)}.
     * @see HttpDecoderConfig HttpDecoderConfig API documentation for detailed descriptions of
     * the configuration parameters.
     */
    public HttpRequestDecoder()
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     *
     * @param maxInitialLineLength the initial size of the temporary buffer used when parsing the lines of the
     * HTTP headers.
     * @param maxHeaderSize the maximum permitted combined size of all headers in any one request.
     * @param maxChunkSize The maximum amount of data that the decoder will buffer
     * before sending chunks down the pipeline.
     * @see HttpDecoderConfig HttpDecoderConfig API documentation for detailed descriptions of
     * the configuration parameters.
     */
    public HttpRequestDecoder(
            int maxInitialLineLength, int maxHeaderSize, int maxChunkSize)
        : this(new HttpDecoderConfig()
                .SetMaxInitialLineLength(maxInitialLineLength)
                .SetMaxHeaderSize(maxHeaderSize)
                .SetMaxChunkSize(maxChunkSize))
    {
    }

    /**
     * Creates a new instance with the specified configuration.
     * @see HttpDecoderConfig HttpDecoderConfig API documentation for detailed descriptions of
     * the configuration parameters.
     */
    public HttpRequestDecoder(HttpDecoderConfig config) : base(config)
    {
    }

    protected override IHttpMessage CreateMessage(string[] initialLine)
    {
        return new DefaultHttpRequest(
                // Do strict version checking
                HttpVersion.ValueOf(initialLine[2], true),
                HttpMethod.ValueOf(initialLine[0]), initialLine[1], headersFactory);
    }

    protected override AsciiString SplitHeaderName(byte[] sb, int start, int length)
    {
        byte firstChar = sb[start];
        if (firstChar == 'H')
        {
            if (length == 4 && IsHost(sb, start))
            {
                return Host;
            }
        }
        else if (firstChar == 'A')
        {
            if (length == 6 && IsAccept(sb, start))
            {
                return Accept;
            }
        }
        else if (firstChar == 'C')
        {
            if (length == 10)
            {
                if (IsConnection(sb, start))
                {
                    return Connection;
                }
            }
            else if (length == 12)
            {
                if (IsContentType(sb, start))
                {
                    return ContentType;
                }
            }
            else if (length == 14)
            {
                if (IsContentLength(sb, start))
                {
                    return ContentLength;
                }
            }
        }

        return base.SplitHeaderName(sb, start, length);
    }

    private static bool IsAccept(byte[] sb, int start)
    {
        long maybeAccept = (uint) (sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16 |
                sb[start + 3] << 24) |
                (long) sb[start + 4] << 32 |
                (long) sb[start + 5] << 40;
        return maybeAccept == ACCEPT_AS_LONG;
    }

    private static bool IsHost(byte[] sb, int start)
    {
        int maybeHost = sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16 |
                sb[start + 3] << 24;
        return maybeHost == HOST_AS_INT;
    }

    private static bool IsConnection(byte[] sb, int start)
    {
        long maybeConnecti = (uint) (sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16 |
                sb[start + 3] << 24) |
                (long) sb[start + 4] << 32 |
                (long) sb[start + 5] << 40 |
                (long) sb[start + 6] << 48 |
                (long) sb[start + 7] << 56;
        if (maybeConnecti != CONNECTION_AS_LONG_0)
        {
            return false;
        }

        short maybeOn = unchecked((short) (sb[start + 8] | sb[start + 9] << 8));
        return maybeOn == CONNECTION_AS_SHORT_1;
    }

    private static bool IsContentType(byte[] sb, int start)
    {
        long maybeContent = (uint) (sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16 |
                sb[start + 3] << 24) |
                (long) sb[start + 4] << 32 |
                (long) sb[start + 5] << 40 |
                (long) sb[start + 6] << 48 |
                (long) sb[start + 7] << 56;
        if (maybeContent != CONTENT_AS_LONG)
        {
            return false;
        }

        int maybeType = sb[start + 8] |
                sb[start + 9] << 8 |
                sb[start + 10] << 16 |
                sb[start + 11] << 24;
        return maybeType == TYPE_AS_INT;
    }

    private static bool IsContentLength(byte[] sb, int start)
    {
        long maybeContent = (uint) (sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16 |
                sb[start + 3] << 24) |
                (long) sb[start + 4] << 32 |
                (long) sb[start + 5] << 40 |
                (long) sb[start + 6] << 48 |
                (long) sb[start + 7] << 56;
        if (maybeContent != CONTENT_AS_LONG)
        {
            return false;
        }

        long maybeLength = (uint) (sb[start + 8] |
                sb[start + 9] << 8 |
                sb[start + 10] << 16 |
                sb[start + 11] << 24) |
                (long) sb[start + 12] << 32 |
                (long) sb[start + 13] << 40;
        return maybeLength == LENGTH_AS_LONG;
    }

    private static bool IsGetMethod(byte[] sb, int start)
    {
        int maybeGet = sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16;
        return maybeGet == GET_AS_INT;
    }

    private static bool IsPostMethod(byte[] sb, int start)
    {
        int maybePost = sb[start] |
                sb[start + 1] << 8 |
                sb[start + 2] << 16 |
                sb[start + 3] << 24;
        return maybePost == POST_AS_INT;
    }

    protected override string SplitFirstWordInitialLine(byte[] sb, int start, int length)
    {
        if (length == 3)
        {
            if (IsGetMethod(sb, start))
            {
                return HttpMethod.GET.Name();
            }
        }
        else if (length == 4)
        {
            if (IsPostMethod(sb, start))
            {
                return HttpMethod.POST.Name();
            }
        }

        return base.SplitFirstWordInitialLine(sb, start, length);
    }

    protected override string SplitThirdWordInitialLine(byte[] sb, int start, int length)
    {
        if (length == 8)
        {
            long maybeHttp1_x = (uint) (sb[start] |
                    sb[start + 1] << 8 |
                    sb[start + 2] << 16 |
                    sb[start + 3] << 24) |
                    (long) sb[start + 4] << 32 |
                    (long) sb[start + 5] << 40 |
                    (long) sb[start + 6] << 48 |
                    (long) sb[start + 7] << 56;
            if (maybeHttp1_x == HTTP_1_1_AS_LONG)
            {
                return HttpVersion.HTTP_1_1_STRING;
            }
            else if (maybeHttp1_x == HTTP_1_0_AS_LONG)
            {
                return HttpVersion.HTTP_1_0_STRING;
            }
        }

        return base.SplitThirdWordInitialLine(sb, start, length);
    }

    protected override IHttpMessage CreateInvalidMessage()
    {
        return new DefaultFullHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.GET, "/bad-request",
                Unpooled.Buffer(0), headersFactory, trailersFactory);
    }

    protected override bool IsDecodingRequest()
    {
        return true;
    }

    protected override bool IsContentAlwaysEmpty(IHttpMessage msg)
    {
        // fast-path to save expensive O(n) checks; users can override createMessage
        // and extends DefaultHttpRequest making implementing HttpResponse:
        // this is why we cannot use instanceof DefaultHttpRequest here :(
        if (msg.GetType() == typeof(DefaultHttpRequest))
        {
            return false;
        }

        return base.IsContentAlwaysEmpty(msg);
    }
}
