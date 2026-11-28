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

using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * <p>
 * Performs server side opening and closing handshakes for web socket specification version <a
 * href="https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-00" >draft-ietf-hybi-thewebsocketprotocol-
 * 00</a>
 * </p>
 * <p>
 * A very large portion of this code was taken from the Netty 3.2 HTTP example.
 * </p>
 */
// CLR: the static imports HttpMethod.GET and HttpVersion.HTTP_1_1 are using static of the core HttpMethod and
// HttpVersion. java.util.regex.Pattern is System.Text.RegularExpressions.Regex with the same patterns, and
// matcher(s).replaceAll("") is Regex.Replace(s, ""), which "in a specified input string, replaces all strings that
// match" (https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.replace?view=net-10.0).
// Long.parseLong is core's HttpUtil.ParseLong (NumberFormatException is FormatException); the long division by a zero
// space count throws DivideByZeroException where Java throws ArithmeticException, and the (int) cast keeps the low 32
// bits as in Java (unchecked conversion). The Buffer port has no array() (docs/buffer-porting.md, Array extraction), so
// the 16-byte challenge array that Java wraps and reads back with input.array() is kept in a local and handed to
// WebSocketUtil.Md5. The CharSequence returned by headers.get(UPGRADE) is a string: AsciiString.contentEqualsIgnoreCase
// wraps it in the Common StringCharSequence. Set.toString() of headers().names() prints "[a, b]", which the message
// builds with string.Join. The [Obsolete] uri() (see WebSocketServerHandshaker) is still the Location value, so its two
// calls disable CS0612 with that reason. The close overrides write the frame and complete the caller's promise with
// TrySetFromTask, as writeAndFlush(frame, promise) does.
public class WebSocketServerHandshaker00 : WebSocketServerHandshaker
{

    private static readonly Regex BEGINNING_DIGIT = new Regex("[^0-9]");
    private static readonly Regex BEGINNING_SPACE = new Regex("[^ ]");

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols
     * @param maxFramePayloadLength
     *            Maximum allowable frame payload length. Setting this value to your application's requirement may
     *            reduce denial of service attacks using long data frames.
     */
    public WebSocketServerHandshaker00(string webSocketURL, string subprotocols, int maxFramePayloadLength)
        : this(webSocketURL, subprotocols, WebSocketDecoderConfig.NewBuilder()
            .MaxFramePayloadLength(maxFramePayloadLength)
            .Build())
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols
     * @param decoderConfig
     *            Frames decoder configuration.
     */
    public WebSocketServerHandshaker00(string webSocketURL, string subprotocols, WebSocketDecoderConfig decoderConfig)
        : base(WebSocketVersion.V00, webSocketURL, subprotocols, decoderConfig)
    {
    }

    /**
     * <p>
     * Handle the web socket handshake for the web socket specification <a href=
     * "https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-00">HyBi version 0</a> and lower. This standard
     * is really a rehash of <a href="https://tools.ietf.org/html/draft-hixie-thewebsocketprotocol-76" >hixie-76</a> and
     * <a href="https://tools.ietf.org/html/draft-hixie-thewebsocketprotocol-75" >hixie-75</a>.
     * </p>
     *
     * <p>
     * Browser request to the server:
     * </p>
     *
     * <pre>
     * GET /demo HTTP/1.1
     * Upgrade: WebSocket
     * Connection: Upgrade
     * Host: example.com
     * Origin: http://example.com
     * Sec-WebSocket-Protocol: chat, sample
     * Sec-WebSocket-Key1: 4 @1  46546xW%0l 1 5
     * Sec-WebSocket-Key2: 12998 5 Y3 1  .P00
     *
     * ^n:ds[4U
     * </pre>
     *
     * <p>
     * Server response:
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
     */
    protected override IFullHttpResponse NewHandshakeResponse(IFullHttpRequest req, HttpHeaders headers)
    {
        HttpMethod method = req.Method();
        if (!GET.Equals(method))
        {
            throw new WebSocketServerHandshakeException("Invalid WebSocket handshake method: " + method, req);
        }

        // Serve the WebSocket handshake request.
        if (!req.Headers().ContainsValue(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE, true)
                || !HttpHeaderValues.WEBSOCKET.ContentEqualsIgnoreCase(UpgradeHeader(req)))
        {
            throw new WebSocketServerHandshakeException("not a WebSocket handshake request: missing upgrade", req);
        }

        // Hixie 75 does not contain these headers while Hixie 76 does
        bool isHixie76 = req.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_KEY1) &&
                            req.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_KEY2);

        string origin = req.Headers().Get(HttpHeaderNames.ORIGIN);
        //throw before allocating FullHttpResponse
        if (origin == null && !isHixie76)
        {
            throw new WebSocketServerHandshakeException("Missing origin header, got only " +
                                                        "[" + string.Join(", ", req.Headers().Names()) + "]",
                                                        req);
        }

        // Create the WebSocket handshake response.
        IFullHttpResponse res = new DefaultFullHttpResponse(HTTP_1_1, new HttpResponseStatus(101,
                isHixie76 ? "WebSocket Protocol Handshake" : "Web Socket Protocol Handshake"),
                req.Content.Alloc().Buffer(0));

        if (headers != null)
        {
            res.Headers().Add(headers);
        }

        res.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
                     .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE);

        // Fill in the headers and contents depending on handshake getMethod.
        if (isHixie76)
        {
            // New handshake getMethod with a challenge:
            res.Headers().Add(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, origin);
#pragma warning disable CS0612 // uri() is @Deprecated without a named replacement and still the Location in Java.
            res.Headers().Add(HttpHeaderNames.SEC_WEBSOCKET_LOCATION, Uri());
#pragma warning restore CS0612

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

            // Calculate the answer of the challenge.
            string key1 = req.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_KEY1);
            string key2 = req.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_KEY2);
            int a = (int) (HttpUtil.ParseLong(BEGINNING_DIGIT.Replace(key1, "")) /
                           BEGINNING_SPACE.Replace(key1, "").Length);
            int b = (int) (HttpUtil.ParseLong(BEGINNING_DIGIT.Replace(key2, "")) /
                           BEGINNING_SPACE.Replace(key2, "").Length);
            long c = req.Content.ReadLong();
            byte[] inputArray = new byte[16];
            ByteBuf input = Unpooled.WrappedBuffer(inputArray).SetIndex(0, 0);
            input.WriteInt(a);
            input.WriteInt(b);
            input.WriteLong(c);
            res.Content.WriteBytes(WebSocketUtil.Md5(inputArray));
        }
        else
        {
            // Old Hixie 75 handshake getMethod with no challenge:
            res.Headers().Add(HttpHeaderNames.WEBSOCKET_ORIGIN, origin);
#pragma warning disable CS0612 // uri() is @Deprecated without a named replacement and still the Location in Java.
            res.Headers().Add(HttpHeaderNames.WEBSOCKET_LOCATION, Uri());
#pragma warning restore CS0612

            string protocol = req.Headers().Get(HttpHeaderNames.WEBSOCKET_PROTOCOL);
            if (protocol != null)
            {
                res.Headers().Set(HttpHeaderNames.WEBSOCKET_PROTOCOL, SelectSubprotocol(protocol));
            }
        }

        return res;
    }

    /**
     * Echo back the closing frame
     *
     * @param channel
     *            the {@link Channel} to use.
     * @param frame
     *            Web Socket frame that was received.
     * @param promise
     *            the {@link ChannelPromise} to be notified when the closing handshake is done.
     */
    public override Task CloseAsync(IChannel channel, CloseWebSocketFrame frame, TaskCompletionSource promise)
    {
        return Notify(channel.WriteAndFlushAsync(frame), promise);
    }

    /**
     * Echo back the closing frame
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param frame
     *            Closing Frame that was received.
     * @param promise
     *            the {@link ChannelPromise} to be notified when the closing handshake is done.
     */
    public override Task CloseAsync(IChannelHandlerContext ctx, CloseWebSocketFrame frame,
                                    TaskCompletionSource promise)
    {
        return Notify(ctx.WriteAndFlushAsync(frame), promise);
    }

    // CLR: the write future completes the caller's promise, as writeAndFlush(msg, promise) does in Java.
    private static Task Notify(Task future, TaskCompletionSource promise)
    {
        _ = future.ContinueWith(f => promise.TrySetFromTask(f), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise.Task;
    }

    // CLR: the String returned by headers().get(UPGRADE) as the CharSequence argument of contentEqualsIgnoreCase.
    private static ICharSequence UpgradeHeader(IFullHttpRequest req)
    {
        string upgrade = req.Headers().Get(HttpHeaderNames.UPGRADE);
        return upgrade != null ? new StringCharSequence(upgrade) : null;
    }

    protected override IWebSocketFrameDecoder NewWebsocketDecoder()
    {
        return new WebSocket00FrameDecoder(DecoderConfig());
    }

    protected override IWebSocketFrameEncoder NewWebSocketEncoder()
    {
        return new WebSocket00FrameEncoder();
    }
}
