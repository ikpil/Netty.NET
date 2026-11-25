/*
 * Copyright 2019 The Netty Project
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

using System.Text;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * <p>
 * Performs server side opening and closing handshakes for web socket specification version <a
 * href="https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-10" >draft-ietf-hybi-thewebsocketprotocol-
 * 10</a>
 * </p>
 */
// CLR: the static imports HttpMethod.GET and HttpVersion.* are using static of the core HttpMethod and HttpVersion.
// String.getBytes(US_ASCII) is Encoding.ASCII.GetBytes, which replaces a character it cannot encode with '?' as Java's
// encoder does; the CharSequence key is the string returned by headers.get(...). The ACCEPT_GUID constant is a const.
public class WebSocketServerHandshaker07 : WebSocketServerHandshaker
{

    public const string WEBSOCKET_07_ACCEPT_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols
     * @param allowExtensions
     *            Allow extensions to be used in the reserved bits of the web socket frame
     * @param maxFramePayloadLength
     *            Maximum allowable frame payload length. Setting this value to your application's
     *            requirement may reduce denial of service attacks using long data frames.
     */
    public WebSocketServerHandshaker07(
            string webSocketURL, string subprotocols, bool allowExtensions, int maxFramePayloadLength)
                : this(webSocketURL, subprotocols, allowExtensions, maxFramePayloadLength, false)
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols
     * @param allowExtensions
     *            Allow extensions to be used in the reserved bits of the web socket frame
     * @param maxFramePayloadLength
     *            Maximum allowable frame payload length. Setting this value to your application's
     *            requirement may reduce denial of service attacks using long data frames.
     * @param allowMaskMismatch
     *            When set to true, frames which are not masked properly according to the standard will still be
     *            accepted.
     */
    public WebSocketServerHandshaker07(
            string webSocketURL, string subprotocols, bool allowExtensions, int maxFramePayloadLength,
            bool allowMaskMismatch) : this(webSocketURL, subprotocols, WebSocketDecoderConfig.NewBuilder()
            .AllowExtensions(allowExtensions)
            .MaxFramePayloadLength(maxFramePayloadLength)
            .AllowMaskMismatch(allowMaskMismatch)
            .Build())
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param decoderConfig
     *            Frames decoder configuration.
     */
    public WebSocketServerHandshaker07(string webSocketURL, string subprotocols, WebSocketDecoderConfig decoderConfig)
        : base(WebSocketVersion.V07, webSocketURL, subprotocols, decoderConfig)
    {
    }

    /**
     * <p>
     * Handle the web socket handshake for the web socket specification <a href=
     * "https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-07">HyBi version 7</a>.
     * </p>
     *
     * <p>
     * Browser request to the server:
     * </p>
     *
     * <pre>
     * GET /chat HTTP/1.1
     * Host: server.example.com
     * Upgrade: websocket
     * Connection: Upgrade
     * Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==
     * Sec-WebSocket-Origin: http://example.com
     * Sec-WebSocket-Protocol: chat, superchat
     * Sec-WebSocket-Version: 7
     * </pre>
     *
     * <p>
     * Server response:
     * </p>
     *
     * <pre>
     * HTTP/1.1 101 Switching Protocols
     * Upgrade: websocket
     * Connection: Upgrade
     * Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=
     * Sec-WebSocket-Protocol: chat
     * </pre>
     */
    protected override IFullHttpResponse NewHandshakeResponse(IFullHttpRequest req, HttpHeaders headers)
    {
        HttpMethod method = req.Method();
        if (!GET.Equals(method))
        {
            throw new WebSocketServerHandshakeException("Invalid WebSocket handshake method: " + method, req);
        }
        HttpHeaders reqHeaders = req.Headers();
        if (!reqHeaders.ContainsValue(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE, true))
        {
            throw new WebSocketServerHandshakeException(
                    "not a WebSocket request: a |Connection| header must include a token 'Upgrade'", req);
        }
        if (!reqHeaders.Contains(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET, true))
        {
            throw new WebSocketServerHandshakeException(
                    "not a WebSocket request: an |Upgrade| header must containing the value 'websocket'", req);
        }
        string key = reqHeaders.Get(HttpHeaderNames.SEC_WEBSOCKET_KEY);
        if (key == null)
        {
            throw new WebSocketServerHandshakeException("not a WebSocket request: missing key", req);
        }

        IFullHttpResponse res =
                new DefaultFullHttpResponse(HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS,
                        req.Content.Alloc().Buffer(0));

        if (headers != null)
        {
            res.Headers().Add(headers);
        }

        string acceptSeed = key + WEBSOCKET_07_ACCEPT_GUID;
        byte[] sha1 = WebSocketUtil.Sha1(Encoding.ASCII.GetBytes(acceptSeed));
        string accept = WebSocketUtil.Base64(sha1);

        if (logger.IsDebugEnabled())
        {
            logger.Debug("WebSocket version 07 server handshake key: {}, response: {}.", key, accept);
        }

        res.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
                     .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
                     .Set(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT, accept);

        string subprotocols = req.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL);
        if (subprotocols != null)
        {
            string selectedSubprotocol = SelectSubprotocol(subprotocols);
            if (selectedSubprotocol == null)
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Requested subprotocol(s) not supported: {}", subprotocols);
                }
            }
            else
            {
                res.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, selectedSubprotocol);
            }
        }
        return res;
    }

    protected override IWebSocketFrameDecoder NewWebsocketDecoder()
    {
        return new WebSocket07FrameDecoder(DecoderConfig());
    }

    protected override IWebSocketFrameEncoder NewWebSocketEncoder()
    {
        return new WebSocket07FrameEncoder(false);
    }
}
