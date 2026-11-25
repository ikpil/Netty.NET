/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketClientProtocolConfig;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketServerProtocolConfig;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * This handler does all the heavy lifting for you to run a websocket client.
 *
 * It takes care of websocket handshaking as well as processing of Ping, Pong frames. Text and Binary
 * data frames are passed to the next handler in the pipeline (implemented by you) for processing.
 * Also the close frame is passed to the next handler as you may want inspect it before close the connection if
 * the {@code handleCloseFrames} is {@code false}, default is {@code true}.
 *
 * This implementation will establish the websocket connection once the connection to the remote server was complete.
 *
 * To know once a handshake was done you can intercept the
 * {@link ChannelInboundHandler#userEventTriggered(ChannelHandlerContext, Object)} and check if the event was of type
 * {@link ClientHandshakeStateEvent#HANDSHAKE_ISSUED} or {@link ClientHandshakeStateEvent#HANDSHAKE_COMPLETE}.
 */
// CLR: java.net.URI is System.Uri (see WebSocketClientHandshaker). ClientHandshakeStateEvent is a simple C# enum. The
// static imports of the WebSocketClientProtocolConfig DEFAULT_* constants and of
// WebSocketServerProtocolConfig.DEFAULT_HANDSHAKE_TIMEOUT_MILLIS are using static, and checkNotNull is the Common
// ObjectUtil.CheckNotNull. Class.getName() of the handler names is Type.FullName. ctx.close() is CloseAsync() with the
// Task discarded, and buildHandshakeException narrows the return type with a C# 9 covariant override. The public
// methods are virtual because the Java class is not final.
public class WebSocketClientProtocolHandler : WebSocketProtocolHandler
{
    private readonly WebSocketClientHandshaker handshaker;
    private readonly WebSocketClientProtocolConfig clientConfig;

    /**
     * Returns the used handshaker
     */
    public virtual WebSocketClientHandshaker Handshaker()
    {
        return handshaker;
    }

    /**
     * Events that are fired to notify about handshake status
     */
    public enum ClientHandshakeStateEvent
    {
        /**
         * The Handshake was timed out
         */
        HANDSHAKE_TIMEOUT,

        /**
         * The Handshake was started but the server did not response yet to the request
         */
        HANDSHAKE_ISSUED,

        /**
         * The Handshake was complete successful and so the channel was upgraded to websockets
         */
        HANDSHAKE_COMPLETE
    }

    /**
     * Base constructor
     *
     * @param clientConfig
     *            Client protocol configuration.
     */
    public WebSocketClientProtocolHandler(WebSocketClientProtocolConfig clientConfig)
        : base(ObjectUtil.CheckNotNull(clientConfig, "clientConfig").DropPongFrames(),
              clientConfig.SendCloseFrame(), clientConfig.ForceCloseTimeoutMillis())
    {
        this.handshaker = WebSocketClientHandshakerFactory.NewHandshaker(
            clientConfig.WebSocketUri(),
            clientConfig.Version(),
            clientConfig.Subprotocol(),
            clientConfig.AllowExtensions(),
            clientConfig.CustomHeaders(),
            clientConfig.MaxFramePayloadLength(),
            clientConfig.PerformMasking(),
            clientConfig.AllowMaskMismatch(),
            clientConfig.ForceCloseTimeoutMillis(),
            clientConfig.AbsoluteUpgradeUrl(),
            clientConfig.GenerateOriginHeader()
        );
        this.clientConfig = clientConfig;
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param clientConfig
     *            Client protocol configuration.
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker,
                                          WebSocketClientProtocolConfig clientConfig)
        : base(ObjectUtil.CheckNotNull(clientConfig, "clientConfig").DropPongFrames(),
              clientConfig.SendCloseFrame(), clientConfig.ForceCloseTimeoutMillis())
    {
        this.handshaker = handshaker;
        this.clientConfig = clientConfig;
    }

    /**
     * Base constructor
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
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param performMasking
     *            Whether to mask all written websocket frames. This must be set to true in order to be fully compatible
     *            with the websocket specifications. Client applications that communicate with a non-standard server
     *            which doesn't require masking might set this to false to achieve a higher performance.
     * @param allowMaskMismatch
     *            When set to true, frames which are not masked properly according to the standard will still be
     *            accepted.
     */
    public WebSocketClientProtocolHandler(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                          bool allowExtensions, HttpHeaders customHeaders,
                                          int maxFramePayloadLength, bool handleCloseFrames,
                                          bool performMasking, bool allowMaskMismatch)
        : this(webSocketURL, version, subprotocol, allowExtensions, customHeaders, maxFramePayloadLength,
            handleCloseFrames, performMasking, allowMaskMismatch, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
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
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param performMasking
     *            Whether to mask all written websocket frames. This must be set to true in order to be fully compatible
     *            with the websocket specifications. Client applications that communicate with a non-standard server
     *            which doesn't require masking might set this to false to achieve a higher performance.
     * @param allowMaskMismatch
     *            When set to true, frames which are not masked properly according to the standard will still be
     *            accepted.
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     */
    public WebSocketClientProtocolHandler(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                          bool allowExtensions, HttpHeaders customHeaders,
                                          int maxFramePayloadLength, bool handleCloseFrames, bool performMasking,
                                          bool allowMaskMismatch, long handshakeTimeoutMillis)
        : this(WebSocketClientHandshakerFactory.NewHandshaker(webSocketURL, version, subprotocol,
                                                            allowExtensions, customHeaders, maxFramePayloadLength,
                                                            performMasking, allowMaskMismatch),
             handleCloseFrames, handshakeTimeoutMillis)
    {
    }

    /**
     * Base constructor
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
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     */
    public WebSocketClientProtocolHandler(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                                   bool allowExtensions, HttpHeaders customHeaders,
                                                   int maxFramePayloadLength, bool handleCloseFrames)
        : this(webSocketURL, version, subprotocol, allowExtensions, customHeaders, maxFramePayloadLength,
             handleCloseFrames, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
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
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     */
    public WebSocketClientProtocolHandler(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                          bool allowExtensions, HttpHeaders customHeaders, int maxFramePayloadLength,
                                          bool handleCloseFrames, long handshakeTimeoutMillis)
        : this(webSocketURL, version, subprotocol, allowExtensions, customHeaders, maxFramePayloadLength,
             handleCloseFrames, DEFAULT_PERFORM_MASKING, DEFAULT_ALLOW_MASK_MISMATCH, handshakeTimeoutMillis)
    {
    }

    /**
     * Base constructor
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
    public WebSocketClientProtocolHandler(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                          bool allowExtensions, HttpHeaders customHeaders,
                                          int maxFramePayloadLength)
        : this(webSocketURL, version, subprotocol, allowExtensions,
             customHeaders, maxFramePayloadLength, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
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
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     */
    public WebSocketClientProtocolHandler(Uri webSocketURL, WebSocketVersion version, string subprotocol,
                                          bool allowExtensions, HttpHeaders customHeaders,
                                          int maxFramePayloadLength, long handshakeTimeoutMillis)
        : this(webSocketURL, version, subprotocol, allowExtensions, customHeaders,
             maxFramePayloadLength, DEFAULT_HANDLE_CLOSE_FRAMES, handshakeTimeoutMillis)
    {
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker, bool handleCloseFrames)
        : this(handshaker, handleCloseFrames, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker, bool handleCloseFrames,
                                          long handshakeTimeoutMillis)
        : this(handshaker, handleCloseFrames, DEFAULT_DROP_PONG_FRAMES, handshakeTimeoutMillis)
    {
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param dropPongFrames
     *            {@code true} if pong frames should not be forwarded
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker, bool handleCloseFrames,
                                          bool dropPongFrames)
        : this(handshaker, handleCloseFrames, dropPongFrames, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param dropPongFrames
     *            {@code true} if pong frames should not be forwarded
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker, bool handleCloseFrames,
                                          bool dropPongFrames, long handshakeTimeoutMillis)
        : this(handshaker, handleCloseFrames, dropPongFrames, handshakeTimeoutMillis, DEFAULT_WITH_UTF8_VALIDATOR)
    {
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param handleCloseFrames
     *            {@code true} if close frames should not be forwarded and just close the channel
     * @param dropPongFrames
     *            {@code true} if pong frames should not be forwarded
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     * @param withUTF8Validator
     *            {@code true} if UTF8 validation of text frames should be enabled
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker, bool handleCloseFrames,
                                          bool dropPongFrames, long handshakeTimeoutMillis,
                                          bool withUTF8Validator) : base(dropPongFrames)
    {
        this.handshaker = handshaker;
        this.clientConfig = WebSocketClientProtocolConfig.NewBuilder()
            .HandleCloseFrames(handleCloseFrames)
            .HandshakeTimeoutMillis(handshakeTimeoutMillis)
            .WithUTF8Validator(withUTF8Validator)
            .Build();
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker)
        : this(handshaker, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
     *
     * @param handshaker
     *            The {@link WebSocketClientHandshaker} which will be used to issue the handshake once the connection
     *            was established to the remote peer.
     * @param handshakeTimeoutMillis
     *            Handshake timeout in mills, when handshake timeout, will trigger user
     *            event {@link ClientHandshakeStateEvent#HANDSHAKE_TIMEOUT}
     */
    public WebSocketClientProtocolHandler(WebSocketClientHandshaker handshaker, long handshakeTimeoutMillis)
        : this(handshaker, DEFAULT_HANDLE_CLOSE_FRAMES, handshakeTimeoutMillis)
    {
    }

    protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame frame, IList<object> @out)
    {
        if (clientConfig.HandleCloseFrames() && frame is CloseWebSocketFrame)
        {
            _ = ctx.CloseAsync();
            return;
        }
        base.Decode(ctx, frame, @out);
    }

    protected override WebSocketClientHandshakeException BuildHandshakeException(string message)
    {
        return new WebSocketClientHandshakeException(message);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        IChannelPipeline cp = ctx.Pipeline;
        if (cp.Get<WebSocketClientProtocolHandshakeHandler>() == null)
        {
            // Add the WebSocketClientProtocolHandshakeHandler before this one.
            ctx.Pipeline.AddBefore(ctx.Name, typeof(WebSocketClientProtocolHandshakeHandler).FullName,
                new WebSocketClientProtocolHandshakeHandler(handshaker, clientConfig.HandshakeTimeoutMillis()));
        }
        if (clientConfig.WithUTF8Validator() && cp.Get<Utf8FrameValidator>() == null)
        {
            // Add the UFT8 checking before this one.
            ctx.Pipeline.AddBefore(ctx.Name, typeof(Utf8FrameValidator).FullName,
                    new Utf8FrameValidator());
        }
    }
}
