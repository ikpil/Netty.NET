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
using System.Buffers.Binary;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * <p>
 * Performs client side opening and closing handshakes for web socket specification version <a
 * href="https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-00" >draft-ietf-hybi-thewebsocketprotocol-
 * 00</a>
 * </p>
 * <p>
 * A very large portion of this code was taken from the Netty 3.2 HTTP example.
 * </p>
 */
// CLR: java.net.URI is System.Uri and the URI accessors, the promise/Task mapping and the Async suffix are those of
// WebSocketClientHandshaker (see its class comment). The package-private constructors are internal (the Java tests and
// WebSocketClientHandshakerFactory reach them through package access). Integer.toString(int) uses the invariant
// culture. java.nio.ByteBuffer.allocate(4).putInt(n).array() is BinaryPrimitives.WriteInt32BigEndian into a 4-byte
// array: "The initial order of a byte buffer is always BIG_ENDIAN" and putInt "writes four bytes containing the given
// int value, in the current byte order"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/ByteBuffer.html), and WriteInt32BigEndian
// "writes an Int32 into a span of bytes, as big endian"
// (https://learn.microsoft.com/en-us/dotnet/api/system.buffers.binary.binaryprimitives.writeint32bigendian?view=net-10.0).
// System.arraycopy is Array.Copy. ThreadLocalRandom.current().nextInt(0x7e) is Random.Shared.Next(0x7e), whose argument
// is "the exclusive upper bound of the random number to be generated", as Java's bound
// (https://learn.microsoft.com/en-us/dotnet/api/system.random.next?view=net-10.0; ThreadLocalRandom is Random.Shared,
// see WebSocketUtil). The CharSequence returned by headers.get(...) is a string: AsciiString.contentEqualsIgnoreCase
// wraps it in the Common StringCharSequence, and a null header prints "null" as in Java string concatenation.
// challenge.equals(...) is the Buffer port's content comparison ByteBuf.Equals. setForceCloseTimeoutMillis narrows the
// return type with a C# 9 covariant override.
public class WebSocketClientHandshaker00 : WebSocketClientHandshaker
{

    private ByteBuf expectedChallengeResponseBytes;

    /**
     * Creates a new instance with the specified destination WebSocket location and version to initiate.
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param version
     *            Version of web socket specification to use to connect to the server
     * @param subprotocol
     *            Sub protocol request sent to the server.
     * @param customHeaders
     *            Map of custom headers to add to the client request
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload
     */
    public WebSocketClientHandshaker00(Uri webSocketURL, WebSocketVersion version, string subprotocol,
            HttpHeaders customHeaders, int maxFramePayloadLength)
                : this(webSocketURL, version, subprotocol, customHeaders, maxFramePayloadLength,
                DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Creates a new instance with the specified destination WebSocket location and version to initiate.
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param version
     *            Version of web socket specification to use to connect to the server
     * @param subprotocol
     *            Sub protocol request sent to the server.
     * @param customHeaders
     *            Map of custom headers to add to the client request
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload
     * @param forceCloseTimeoutMillis
     *            Close the connection if it was not closed by the server after timeout specified
     */
    public WebSocketClientHandshaker00(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                       HttpHeaders customHeaders, int maxFramePayloadLength,
                                       long forceCloseTimeoutMillis)
        : this(webSocketURL, version, subprotocol, customHeaders, maxFramePayloadLength, forceCloseTimeoutMillis, false)
    {
    }

    /**
     * Creates a new instance with the specified destination WebSocket location and version to initiate.
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param version
     *            Version of web socket specification to use to connect to the server
     * @param subprotocol
     *            Sub protocol request sent to the server.
     * @param customHeaders
     *            Map of custom headers to add to the client request
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload
     * @param forceCloseTimeoutMillis
     *            Close the connection if it was not closed by the server after timeout specified
     * @param  absoluteUpgradeUrl
     *            Use an absolute url for the Upgrade request, typically when connecting through an HTTP proxy over
     *            clear HTTP
     */
    internal WebSocketClientHandshaker00(Uri webSocketURL, WebSocketVersion version, string subprotocol,
            HttpHeaders customHeaders, int maxFramePayloadLength,
            long forceCloseTimeoutMillis, bool absoluteUpgradeUrl)
        : this(webSocketURL, version, subprotocol, customHeaders, maxFramePayloadLength, forceCloseTimeoutMillis,
            absoluteUpgradeUrl, true)
    {
    }

    /**
     * Creates a new instance with the specified destination WebSocket location and version to initiate.
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param version
     *            Version of web socket specification to use to connect to the server
     * @param subprotocol
     *            Sub protocol request sent to the server.
     * @param customHeaders
     *            Map of custom headers to add to the client request
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload
     * @param forceCloseTimeoutMillis
     *            Close the connection if it was not closed by the server after timeout specified
     * @param  absoluteUpgradeUrl
     *            Use an absolute url for the Upgrade request, typically when connecting through an HTTP proxy over
     *            clear HTTP
     * @param generateOriginHeader
     *            Allows to generate the `Origin` header value for handshake request
     *            according to the given webSocketURL
     */
    internal WebSocketClientHandshaker00(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                HttpHeaders customHeaders, int maxFramePayloadLength,
                                long forceCloseTimeoutMillis, bool absoluteUpgradeUrl,
                                bool generateOriginHeader)
        : base(webSocketURL, version, subprotocol, customHeaders, maxFramePayloadLength, forceCloseTimeoutMillis,
            absoluteUpgradeUrl, generateOriginHeader)
    {
    }

    /**
     * <p>
     * Sends the opening request to the server:
     * </p>
     *
     * <pre>
     * GET /demo HTTP/1.1
     * Upgrade: WebSocket
     * Connection: Upgrade
     * Host: example.com
     * Origin: http://example.com
     * Sec-WebSocket-Key1: 4 @1  46546xW%0l 1 5
     * Sec-WebSocket-Key2: 12998 5 Y3 1  .P00
     *
     * ^n:ds[4U
     * </pre>
     *
     */
    protected override IFullHttpRequest NewHandshakeRequest()
    {
        // Make keys
        int spaces1 = WebSocketUtil.RandomNumber(1, 12);
        int spaces2 = WebSocketUtil.RandomNumber(1, 12);

        int max1 = int.MaxValue / spaces1;
        int max2 = int.MaxValue / spaces2;

        int number1 = WebSocketUtil.RandomNumber(0, max1);
        int number2 = WebSocketUtil.RandomNumber(0, max2);

        int product1 = number1 * spaces1;
        int product2 = number2 * spaces2;

        string key1 = product1.ToString(CultureInfo.InvariantCulture);
        string key2 = product2.ToString(CultureInfo.InvariantCulture);

        key1 = InsertRandomCharacters(key1);
        key2 = InsertRandomCharacters(key2);

        key1 = InsertSpaces(key1, spaces1);
        key2 = InsertSpaces(key2, spaces2);

        byte[] key3 = WebSocketUtil.RandomBytes(8);

        byte[] number1Array = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number1Array, number1);
        byte[] number2Array = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number2Array, number2);

        byte[] challenge = new byte[16];
        Array.Copy(number1Array, 0, challenge, 0, 4);
        Array.Copy(number2Array, 0, challenge, 4, 4);
        Array.Copy(key3, 0, challenge, 8, 8);
        expectedChallengeResponseBytes = Unpooled.WrappedBuffer(WebSocketUtil.Md5(challenge));

        Uri wsURL = Uri();

        // Format request
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                UpgradeUrl(wsURL), Unpooled.WrappedBuffer(key3));
        HttpHeaders headers = request.Headers();

        if (customHeaders != null)
        {
            headers.Add(customHeaders);
            if (!headers.Contains(HttpHeaderNames.HOST))
            {
                // Only add HOST header if customHeaders did not contain it.
                //
                // See https://github.com/netty/netty/issues/10101
                headers.Set(HttpHeaderNames.HOST, WebsocketHostValue(wsURL));
            }
        }
        else
        {
            headers.Set(HttpHeaderNames.HOST, WebsocketHostValue(wsURL));
        }

        headers.Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
               .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
               .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY1, key1)
               .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY2, key2);

        if (generateOriginHeader && !headers.Contains(HttpHeaderNames.ORIGIN))
        {
            headers.Set(HttpHeaderNames.ORIGIN, WebsocketOriginValue(wsURL));
        }

        string expectedSubprotocol = ExpectedSubprotocol();
        if (expectedSubprotocol != null && expectedSubprotocol.Length != 0)
        {
            headers.Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, expectedSubprotocol);
        }

        // Set Content-Length to workaround some known defect.
        // See also: https://www.ietf.org/mail-archive/web/hybi/current/msg02149.html
        headers.Set(HttpHeaderNames.CONTENT_LENGTH, key3.Length);
        return request;
    }

    /**
     * <p>
     * Process server response:
     * </p>
     *
     * <pre>
     * HTTP/1.1 101 WebSocket Protocol Handshake
     * Upgrade: WebSocket
     * Connection: Upgrade
     * Sec-WebSocket-Origin: http://example.com
     * Sec-WebSocket-Location: ws://example.com/demo
     * Sec-WebSocket-Protocol: sample
     *
     * 8jKS'y:G*Co,Wxa-
     * </pre>
     *
     * @param response
     *            HTTP response returned from the server for the request sent by beginOpeningHandshake00().
     * @throws WebSocketHandshakeException
     */
    protected override void Verify(IFullHttpResponse response)
    {
        HttpResponseStatus status = response.Status();
        if (!HttpResponseStatus.SWITCHING_PROTOCOLS.Equals(status))
        {
            throw new WebSocketClientHandshakeException("Invalid handshake response getStatus: " + status, response);
        }

        HttpHeaders headers = response.Headers();
        string upgrade = headers.Get(HttpHeaderNames.UPGRADE);
        if (!HttpHeaderValues.WEBSOCKET.ContentEqualsIgnoreCase(
                upgrade != null ? new StringCharSequence(upgrade) : null))
        {
            throw new WebSocketClientHandshakeException("Invalid handshake response upgrade: " + (upgrade ?? "null"),
                    response);
        }

        if (!headers.ContainsValue(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE, true))
        {
            throw new WebSocketClientHandshakeException("Invalid handshake response connection: "
                    + (headers.Get(HttpHeaderNames.CONNECTION) ?? "null"), response);
        }

        ByteBuf challenge = response.Content;
        if (!challenge.Equals(expectedChallengeResponseBytes))
        {
            throw new WebSocketClientHandshakeException("Invalid challenge", response);
        }
    }

    private static string InsertRandomCharacters(string key)
    {
        int count = WebSocketUtil.RandomNumber(1, 12);

        char[] randomChars = new char[count];
        int randCount = 0;
        while (randCount < count)
        {
            int rand = Random.Shared.Next(0x7e) + 0x21;
            if (0x21 < rand && rand < 0x2f || 0x3a < rand && rand < 0x7e)
            {
                randomChars[randCount] = (char) rand;
                randCount += 1;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int split = WebSocketUtil.RandomNumber(0, key.Length);
            string part1 = key.Substring(0, split);
            string part2 = key.Substring(split);
            key = part1 + randomChars[i] + part2;
        }

        return key;
    }

    private static string InsertSpaces(string key, int spaces)
    {
        for (int i = 0; i < spaces; i++)
        {
            int split = WebSocketUtil.RandomNumber(1, key.Length - 1);
            string part1 = key.Substring(0, split);
            string part2 = key.Substring(split);
            key = part1 + ' ' + part2;
        }

        return key;
    }

    protected override IWebSocketFrameDecoder NewWebsocketDecoder()
    {
        return new WebSocket00FrameDecoder(MaxFramePayloadLength());
    }

    protected override IWebSocketFrameEncoder NewWebSocketEncoder()
    {
        return new WebSocket00FrameEncoder();
    }

    public override WebSocketClientHandshaker00 SetForceCloseTimeoutMillis(long forceCloseTimeoutMillis)
    {
        base.SetForceCloseTimeoutMillis(forceCloseTimeoutMillis);
        return this;
    }

}
