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

using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Auto-detects the version of the Web Socket protocol in use and creates a new proper
 * {@link WebSocketServerHandshaker}.
 */
// CLR: the CharSequence returned by headers().get(SEC_WEBSOCKET_VERSION) is a string, so version.equals(String) is
// string.Equals. ChannelFuture is Task and ChannelPromise is TaskCompletionSource (see WebSocketClientHandshaker); the
// promise overload completes the caller's promise from the write Task with TrySetFromTask, and the Task-returning
// methods take the Async suffix. The @Deprecated sendUnsupportedWebSocketVersionResponse(Channel), whose @deprecated
// text names sendUnsupportedVersionResponse(Channel), is not ported (owner decision 2026-11-16). The public methods are
// virtual because the Java class is not final.
public class WebSocketServerHandshakerFactory
{

    private readonly string webSocketURL;

    private readonly string subprotocols;

    private readonly WebSocketDecoderConfig decoderConfig;

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param allowExtensions
     *            Allow extensions to be used in the reserved bits of the web socket frame
     */
    public WebSocketServerHandshakerFactory(
            string webSocketURL, string subprotocols, bool allowExtensions)
                : this(webSocketURL, subprotocols, allowExtensions, 65536)
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param allowExtensions
     *            Allow extensions to be used in the reserved bits of the web socket frame
     * @param maxFramePayloadLength
     *            Maximum allowable frame payload length. Setting this value to your application's
     *            requirement may reduce denial of service attacks using long data frames.
     */
    public WebSocketServerHandshakerFactory(
            string webSocketURL, string subprotocols, bool allowExtensions,
            int maxFramePayloadLength) : this(webSocketURL, subprotocols, allowExtensions, maxFramePayloadLength, false)
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param allowExtensions
     *            Allow extensions to be used in the reserved bits of the web socket frame
     * @param maxFramePayloadLength
     *            Maximum allowable frame payload length. Setting this value to your application's
     *            requirement may reduce denial of service attacks using long data frames.
     * @param allowMaskMismatch
     *            When set to true, frames which are not masked properly according to the standard will still be
     *            accepted.
     */
    public WebSocketServerHandshakerFactory(
            string webSocketURL, string subprotocols, bool allowExtensions,
            int maxFramePayloadLength, bool allowMaskMismatch)
                : this(webSocketURL, subprotocols, WebSocketDecoderConfig.NewBuilder()
            .AllowExtensions(allowExtensions)
            .MaxFramePayloadLength(maxFramePayloadLength)
            .AllowMaskMismatch(allowMaskMismatch)
            .Build())
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param decoderConfig
     *            Frames decoder options.
     */
    public WebSocketServerHandshakerFactory(
            string webSocketURL, string subprotocols, WebSocketDecoderConfig decoderConfig)
    {
        this.webSocketURL = webSocketURL;
        this.subprotocols = subprotocols;
        this.decoderConfig = ObjectUtil.CheckNotNull(decoderConfig, "decoderConfig");
    }

    /**
     * Instances a new handshaker
     *
     * @return A new WebSocketServerHandshaker for the requested web socket version. Null if web
     *         socket version is not supported.
     */
    public virtual WebSocketServerHandshaker NewHandshaker(IHttpRequest req)
    {
        return ResolveHandshaker0(req, webSocketURL, subprotocols, decoderConfig);
    }

    /**
     * Resolves the client's WebSocket protocol version from the HTTP request header,
     * and creates the corresponding handshaker.
     * This method is identical to {@link #newHandshaker(HttpRequest)},
     * however it does not require a {@link WebSocketServerHandshakerFactory} instance allocation.
     *
     * @param req
     *            The HTTP request that came from the client to upgrade the protocol to WebSocket.
     * @param webSocketURL
     *            URL for web socket communications. e.g "ws://myhost.com/mypath".
     *            Subsequent web socket frames will be sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param decoderConfig
     *            Frames decoder options.
     * @return A new {@link WebSocketServerHandshaker} for the requested web socket version. Null if web
     *         socket version is not supported.
     * @throws NullPointerException if the {@code decoderConfig} is {@code null}.
     */
    public static WebSocketServerHandshaker ResolveHandshaker(IHttpRequest req, string webSocketURL,
                                                              string subprotocols,
                                                              WebSocketDecoderConfig decoderConfig)
    {
        ObjectUtil.CheckNotNull(decoderConfig, "decoderConfig");

        return ResolveHandshaker0(req, webSocketURL, subprotocols, decoderConfig);
    }

    private static WebSocketServerHandshaker ResolveHandshaker0(IHttpRequest req,
                                                                string webSocketURL,
                                                                string subprotocols,
                                                                WebSocketDecoderConfig decoderConfig)
    {
        string version = req.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_VERSION);
        if (version != null)
        {
            if (version.Equals(WebSocketVersion.V13.ToHttpHeaderValue()))
            {
                // Version 13 of the wire protocol - RFC 6455 (version 17 of the draft hybi specification).
                return new WebSocketServerHandshaker13(
                        webSocketURL, subprotocols, decoderConfig);
            }
            else if (version.Equals(WebSocketVersion.V08.ToHttpHeaderValue()))
            {
                // Version 8 of the wire protocol - version 10 of the draft hybi specification.
                return new WebSocketServerHandshaker08(
                        webSocketURL, subprotocols, decoderConfig);
            }
            else if (version.Equals(WebSocketVersion.V07.ToHttpHeaderValue()))
            {
                // Version 8 of the wire protocol - version 07 of the draft hybi specification.
                return new WebSocketServerHandshaker07(
                        webSocketURL, subprotocols, decoderConfig);
            }
            else
            {
                return null;
            }
        }
        else
        {
            // Assume version 00 where version header was not specified
            return new WebSocketServerHandshaker00(webSocketURL, subprotocols, decoderConfig);
        }
    }

    /**
     * Return that we need cannot support the web socket version
     */
    public static Task SendUnsupportedVersionResponseAsync(IChannel channel)
    {
        return SendUnsupportedVersionResponseAsync(channel, new TaskCompletionSource());
    }

    /**
     * Return that we need cannot support the web socket version
     */
    public static Task SendUnsupportedVersionResponseAsync(IChannel channel, TaskCompletionSource promise)
    {
        IHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1,
                HttpResponseStatus.UPGRADE_REQUIRED, channel.Alloc().Buffer(0));
        res.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, WebSocketVersion.V13.ToHttpHeaderValue());
        HttpUtil.SetContentLength(res, 0);
        _ = channel.WriteAndFlushAsync(res).ContinueWith(f => promise.TrySetFromTask(f), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise.Task;
    }
}
