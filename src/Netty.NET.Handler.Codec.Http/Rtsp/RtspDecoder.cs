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

using System.Globalization;
using System.Text.RegularExpressions;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Rtsp;

/**
 * Decodes {@link io.netty.buffer.ByteBuf}s into RTSP messages represented in
 * {@link HttpMessage}s.
 * <p>
 * <h3>Parameters that prevents excessive memory consumption</h3>
 * <table border="1">
 * <tr>
 * <th>Name</th><th>Meaning</th>
 * </tr>
 * <tr>
 * <td>{@code maxInitialLineLength}</td>
 * <td>The maximum length of the initial line
 *     (e.g. {@code "SETUP / RTSP/1.0"} or {@code "RTSP/1.0 200 OK"})
 *     If the length of the initial line exceeds this value, a
 *     {@link io.netty.handler.codec.TooLongFrameException} will be raised.</td>
 * </tr>
 * <tr>
 * <td>{@code maxHeaderSize}</td>
 * <td>The maximum length of all headers. If the sum of the length of each
 *     header exceeds this value, a {@link io.netty.handler.codec.TooLongFrameException} will be
 *     raised.</td>
 * </tr>
 * <tr>
 * <td>{@code maxContentLength}</td>
 * <td>The maximum length of the content.  If the content length exceeds this
 *     value, a {@link io.netty.handler.codec.TooLongFrameException} will be raised.</td>
 * </tr>
 * </table>
 */
// CLR: the @Deprecated constructor RtspDecoder(int, int, int, boolean), whose @deprecated text names
// RtspDecoder(HttpDecoderConfig) and RtspDecoder(int, int, int), is not ported. HttpMessage is IHttpMessage.
// java.util.regex.Pattern is System.Text.RegularExpressions.Regex: Matcher.matches() matches the entire input
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Matcher.html), so the pattern is
// wrapped in \A(?:...)\z, the whole-match form of docs/common-clr-design.md (Native regular expressions); Java's
// \d is "A digit: [0-9]"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html) while the.NET \d
// accepts "any character classified as a Unicode decimal digit"
// (https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions), so \d
// is written [0-9]. Integer.parseInt is int.Parse with an optional sign and the invariant culture, as core
// HttpResponseStatus parses a code; an overflow throws OverflowException where Java throws NumberFormatException,
// and both reach the catch (Exception) of HttpObjectDecoder, which produces the invalid message.
public class RtspDecoder : HttpObjectDecoder
{
    /**
     * Status code for unknown responses.
     */
    private static readonly HttpResponseStatus UNKNOWN_STATUS =
            new HttpResponseStatus(999, "Unknown");
    /**
     * True if the message to decode is a request.
     * False if the message to decode is a response.
     */
    private bool isDecodingRequest;

    /**
     * Regex used on first line in message to detect if it is a response.
     */
    private static readonly Regex versionPattern = new Regex("\\A(?:RTSP/[0-9]\\.[0-9])\\z");

    /**
     * Constant for default max content length.
     */
    public const int DEFAULT_MAX_CONTENT_LENGTH = 8192;

    /**
     * Creates a new instance with the default
     * {@code maxInitialLineLength (4096)}, {@code maxHeaderSize (8192)}, and
     * {@code maxContentLength (8192)}.
     */
    public RtspDecoder()
        : this(DEFAULT_MAX_INITIAL_LINE_LENGTH,
               DEFAULT_MAX_HEADER_SIZE,
               DEFAULT_MAX_CONTENT_LENGTH)
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     * @param maxInitialLineLength The max allowed length of initial line
     * @param maxHeaderSize The max allowed size of header
     * @param maxContentLength The max allowed content length
     */
    public RtspDecoder(int maxInitialLineLength,
                       int maxHeaderSize,
                       int maxContentLength)
        : base(new HttpDecoderConfig()
                .SetMaxInitialLineLength(maxInitialLineLength)
                .SetMaxHeaderSize(maxHeaderSize)
                .SetMaxChunkSize(maxContentLength * 2)
                .SetChunkedSupported(false))
    {
    }

    /**
     * Creates a new instance with the specified configuration.
     */
    public RtspDecoder(HttpDecoderConfig config)
        : base(config.Clone()
                .SetMaxChunkSize(2 * config.GetMaxChunkSize())
                .SetChunkedSupported(false))
    {
    }

    protected override IHttpMessage CreateMessage(string[] initialLine)
    {
        // If the first element of the initial line is a version string then
        // this is a response
        if (versionPattern.IsMatch(initialLine[0]))
        {
            isDecodingRequest = false;
            return new DefaultHttpResponse(RtspVersions.ValueOf(initialLine[0]),
                new HttpResponseStatus(int.Parse(initialLine[1], NumberStyles.AllowLeadingSign,
                                                 CultureInfo.InvariantCulture),
                                       initialLine[2]),
                headersFactory);
        }
        else
        {
            isDecodingRequest = true;
            return new DefaultHttpRequest(RtspVersions.ValueOf(initialLine[2]),
                    RtspMethods.ValueOf(initialLine[0]),
                    initialLine[1],
                    headersFactory);
        }
    }

    protected override bool IsContentAlwaysEmpty(IHttpMessage msg)
    {
        // Unlike HTTP, RTSP always assumes zero-length body if Content-Length
        // header is absent.
        return base.IsContentAlwaysEmpty(msg) || !msg.Headers().Contains(RtspHeaderNames.CONTENT_LENGTH);
    }

    protected override IHttpMessage CreateInvalidMessage()
    {
        if (isDecodingRequest)
        {
            return new DefaultFullHttpRequest(RtspVersions.RTSP_1_0,
                       RtspMethods.OPTIONS, "/bad-request", Unpooled.Buffer(0), headersFactory, trailersFactory);
        }
        else
        {
            return new DefaultFullHttpResponse(
                    RtspVersions.RTSP_1_0, UNKNOWN_STATUS, Unpooled.Buffer(0), headersFactory, trailersFactory);
        }
    }

    protected override bool IsDecodingRequest()
    {
        return isDecodingRequest;
    }
}
