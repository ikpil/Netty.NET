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

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpVersion;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketServerProtocolConfig;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * This handler does all the heavy lifting for you to run a websocket server.
 *
 * It takes care of websocket handshaking as well as processing of control frames (Close, Ping, Pong). Text and Binary
 * data frames are passed to the next handler in the pipeline (implemented by you) for processing.
 *
 * See <tt>io.netty.example.http.websocketx.html5.WebSocketServer</tt> for usage.
 *
 * The implementation of this handler assumes that you just want to run  a websocket server and not process other types
 * HTTP requests (like GET and POST). If you wish to support both HTTP requests and websockets in the one server, refer
 * to the <tt>io.netty.example.http.websocketx.server.WebSocketServer</tt> example.
 *
 * To know once a handshake was done you can intercept the
 * {@link ChannelInboundHandler#userEventTriggered(ChannelHandlerContext, Object)} and check if the event was instance
 * of {@link HandshakeComplete}, the event will contain extra information about the handshake such as the request and
 * selected subprotocol.
 */
// CLR: the enum ServerHandshakeStateEvent is a simple C# enum; its @Deprecated HANDSHAKE_COMPLETE, whose @deprecated
// text names the HandshakeComplete class, is not ported (owner decision 2026-11-16), so the handshake handler fires
// only the HandshakeComplete event. The static import of WebSocketServerProtocolConfig.DEFAULT_HANDSHAKE_TIMEOUT_MILLIS
// and HttpVersion.HTTP_1_1 are using static, and checkNotNull is the Common ObjectUtil.CheckNotNull. Class.getName() of
// the handler names is Type.FullName. AttributeKey.valueOf(Class, String) is AttributeKey<T>.ValueOf(Type, string) and
// channel.attr(key).get()/set() are Attr(key).Get()/Set(). ChannelFutureListener.CLOSE closes the channel from a
// synchronous continuation of the write Task (docs/codec-base-porting.md, MessageAggregator), and ctx.newPromise() is a
// TaskCompletionSource (see WebSocketClientHandshaker). cause.getMessage().getBytes() encodes with the platform default
// charset, which "typically depends upon the locale and charset of the underlying operating system"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html); the port encodes the
// Message with Encoding.UTF8. buildHandshakeException narrows the return type with a C# 9 covariant override. The
// package-private getHandshaker/setHandshaker are internal.
public class WebSocketServerProtocolHandler : WebSocketProtocolHandler
{

    /**
     * Events that are fired to notify about handshake status
     */
    public enum ServerHandshakeStateEvent
    {
        /**
         * The Handshake was timed out
         */
        HANDSHAKE_TIMEOUT
    }

    /**
     * The Handshake was completed successfully and the channel was upgraded to websockets.
     */
    public sealed class HandshakeComplete
    {
        private readonly string requestUri;
        private readonly HttpHeaders requestHeaders;
        private readonly string selectedSubprotocol;

        public HandshakeComplete(string requestUri, HttpHeaders requestHeaders, string selectedSubprotocol)
        {
            this.requestUri = requestUri;
            this.requestHeaders = requestHeaders;
            this.selectedSubprotocol = selectedSubprotocol;
        }

        public string RequestUri()
        {
            return requestUri;
        }

        public HttpHeaders RequestHeaders()
        {
            return requestHeaders;
        }

        public string SelectedSubprotocol()
        {
            return selectedSubprotocol;
        }
    }

    private static readonly AttributeKey<WebSocketServerHandshaker> HANDSHAKER_ATTR_KEY =
            AttributeKey<WebSocketServerHandshaker>.ValueOf(typeof(WebSocketServerHandshaker), "HANDSHAKER");

    private readonly WebSocketServerProtocolConfig serverConfig;

    /**
     * Base constructor
     *
     * @param serverConfig
     *            Server protocol configuration.
     */
    public WebSocketServerProtocolHandler(WebSocketServerProtocolConfig serverConfig)
        : base(ObjectUtil.CheckNotNull(serverConfig, "serverConfig").DropPongFrames(),
              serverConfig.SendCloseFrame(),
              serverConfig.ForceCloseTimeoutMillis()
        )
    {
        this.serverConfig = serverConfig;
    }

    public WebSocketServerProtocolHandler(string websocketPath)
        : this(websocketPath, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, long handshakeTimeoutMillis)
        : this(websocketPath, false, handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, bool checkStartsWith)
        : this(websocketPath, checkStartsWith, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, bool checkStartsWith, long handshakeTimeoutMillis)
        : this(websocketPath, null, false, 65536, false, checkStartsWith, handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols)
        : this(websocketPath, subprotocols, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols, long handshakeTimeoutMillis)
        : this(websocketPath, subprotocols, false, handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols, bool allowExtensions)
        : this(websocketPath, subprotocols, allowExtensions, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols, bool allowExtensions,
                                          long handshakeTimeoutMillis)
        : this(websocketPath, subprotocols, allowExtensions, 65536, handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols,
                                          bool allowExtensions, int maxFrameSize)
        : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols,
                                          bool allowExtensions, int maxFrameSize, long handshakeTimeoutMillis)
        : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, false, handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols,
            bool allowExtensions, int maxFrameSize, bool allowMaskMismatch)
                : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, allowMaskMismatch,
             DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols, bool allowExtensions,
                                          int maxFrameSize, bool allowMaskMismatch, long handshakeTimeoutMillis)
        : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, allowMaskMismatch, false,
            handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols,
            bool allowExtensions, int maxFrameSize, bool allowMaskMismatch, bool checkStartsWith)
                : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, allowMaskMismatch, checkStartsWith,
             DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols,
                                          bool allowExtensions, int maxFrameSize, bool allowMaskMismatch,
                                          bool checkStartsWith, long handshakeTimeoutMillis)
        : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, allowMaskMismatch, checkStartsWith, true,
            handshakeTimeoutMillis)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols,
                                          bool allowExtensions, int maxFrameSize, bool allowMaskMismatch,
                                          bool checkStartsWith, bool dropPongFrames)
        : this(websocketPath, subprotocols, allowExtensions, maxFrameSize, allowMaskMismatch, checkStartsWith,
            dropPongFrames, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols, bool allowExtensions,
                                          int maxFrameSize, bool allowMaskMismatch, bool checkStartsWith,
                                          bool dropPongFrames, long handshakeTimeoutMillis)
        : this(websocketPath, subprotocols, checkStartsWith, dropPongFrames, handshakeTimeoutMillis,
            WebSocketDecoderConfig.NewBuilder()
                .MaxFramePayloadLength(maxFrameSize)
                .AllowMaskMismatch(allowMaskMismatch)
                .AllowExtensions(allowExtensions)
                .Build())
    {
    }

    public WebSocketServerProtocolHandler(string websocketPath, string subprotocols, bool checkStartsWith,
                                          bool dropPongFrames, long handshakeTimeoutMillis,
                                          WebSocketDecoderConfig decoderConfig)
                                              : this(WebSocketServerProtocolConfig.NewBuilder()
            .WebsocketPath(websocketPath)
            .Subprotocols(subprotocols)
            .CheckStartsWith(checkStartsWith)
            .HandshakeTimeoutMillis(handshakeTimeoutMillis)
            .DropPongFrames(dropPongFrames)
            .DecoderConfig(decoderConfig)
            .Build())
    {
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        IChannelPipeline cp = ctx.Pipeline;
        if (cp.Get<WebSocketServerProtocolHandshakeHandler>() == null)
        {
            // Add the WebSocketHandshakeHandler before this one.
            cp.AddBefore(ctx.Name, typeof(WebSocketServerProtocolHandshakeHandler).FullName,
                    new WebSocketServerProtocolHandshakeHandler(serverConfig));
        }
        if (serverConfig.DecoderConfig().WithUTF8Validator() && cp.Get<Utf8FrameValidator>() == null)
        {
            // Add the UFT8 checking before this one.
            cp.AddBefore(ctx.Name, typeof(Utf8FrameValidator).FullName,
                    new Utf8FrameValidator(serverConfig.DecoderConfig().CloseOnProtocolViolation()));
        }
    }

    protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame frame, IList<object> @out)
    {
        if (serverConfig.HandleCloseFrames() && frame is CloseWebSocketFrame)
        {
            WebSocketServerHandshaker handshaker = GetHandshaker(ctx.Channel);
            if (handshaker != null)
            {
                frame.Retain();
                TaskCompletionSource promise = new TaskCompletionSource();
                CloseSent(promise);
                _ = handshaker.CloseAsync(ctx, (CloseWebSocketFrame) frame, promise);
                // handshaker.close() bypasses this handler's own write()/flush() overrides (it writes
                // directly on ctx), so the force-close timeout must be armed explicitly here. Doing so
                // after the close0() write-and-flush attempt above mirrors close()'s ordering and avoids
                // racing a write that completes synchronously.
                ApplyCloseSentTimeout(ctx);
            }
            else
            {
                _ = ctx.WriteAndFlushAsync(Unpooled.EmptyBuffer).ContinueWith(future => ctx.Channel.CloseAsync(),
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return;
        }
        base.Decode(ctx, frame, @out);
    }

    protected override WebSocketServerHandshakeException BuildHandshakeException(string message)
    {
        return new WebSocketServerHandshakeException(message);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (cause is WebSocketHandshakeException)
        {
            IFullHttpResponse response = new DefaultFullHttpResponse(
                    HTTP_1_1, HttpResponseStatus.BAD_REQUEST,
                    Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(cause.Message)));
            _ = ctx.Channel.WriteAndFlushAsync(response).ContinueWith(future => ctx.Channel.CloseAsync(),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        else
        {
            ctx.FireExceptionCaught(cause);
            _ = ctx.CloseAsync();
        }
    }

    internal static WebSocketServerHandshaker GetHandshaker(IChannel channel)
    {
        return channel.Attr(HANDSHAKER_ATTR_KEY).Get();
    }

    internal static void SetHandshaker(IChannel channel, WebSocketServerHandshaker handshaker)
    {
        channel.Attr(HANDSHAKER_ATTR_KEY).Set(handshaker);
    }
}
