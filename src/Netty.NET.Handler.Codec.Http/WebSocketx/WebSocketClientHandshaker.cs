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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Base class for web socket client handshake implementations
 */
// CLR: Parse Uri.OriginalString for the handshake target and authority because System.Uri can canonicalize those
// components. The helper does not validate Java's registry-based hostname distinction.
// Promise continuations preserve inline completion when possible. Forced close uses a cancellable event-loop timer
// and an atomic guard to arm it only once.
public abstract class WebSocketClientHandshaker
{

    private static readonly string HTTP_SCHEME_PREFIX = HttpScheme.HTTP + "://";
    private static readonly string HTTPS_SCHEME_PREFIX = HttpScheme.HTTPS + "://";
    protected const int DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS = 10000;

    private readonly Uri uri;

    private readonly WebSocketVersion version;

    private volatile bool handshakeComplete;

    private long forceCloseTimeoutMillis = DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS;

    private volatile int forceCloseInit;

    private volatile bool forceCloseComplete;

    private readonly string expectedSubprotocol;

    private volatile string actualSubprotocol;

    protected readonly HttpHeaders customHeaders;

    private readonly int maxFramePayloadLength;

    private readonly bool absoluteUpgradeUrl;

    protected readonly bool generateOriginHeader;

    /**
     * Base constructor
     *
     * @param uri
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
    protected WebSocketClientHandshaker(Uri uri, WebSocketVersion version, string subprotocol,
                                        HttpHeaders customHeaders, int maxFramePayloadLength)
        : this(uri, version, subprotocol, customHeaders, maxFramePayloadLength, DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Base constructor
     *
     * @param uri
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
    protected WebSocketClientHandshaker(Uri uri, WebSocketVersion version, string subprotocol,
                                        HttpHeaders customHeaders, int maxFramePayloadLength,
                                        long forceCloseTimeoutMillis)
        : this(uri, version, subprotocol, customHeaders, maxFramePayloadLength, forceCloseTimeoutMillis, false)
    {
    }

    /**
     * Base constructor
     *
     * @param uri
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
    protected WebSocketClientHandshaker(Uri uri, WebSocketVersion version, string subprotocol,
                                        HttpHeaders customHeaders, int maxFramePayloadLength,
                                        long forceCloseTimeoutMillis, bool absoluteUpgradeUrl)
        : this(uri, version, subprotocol, customHeaders, maxFramePayloadLength, forceCloseTimeoutMillis,
            absoluteUpgradeUrl, true)
    {
    }

    /**
     * Base constructor
     *
     * @param uri
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
     *            Allows to generate the `Origin`|`Sec-WebSocket-Origin` header value for handshake request
     *            according to the given webSocketURL
     */
    protected WebSocketClientHandshaker(Uri uri, WebSocketVersion version, string subprotocol,
            HttpHeaders customHeaders, int maxFramePayloadLength,
            long forceCloseTimeoutMillis, bool absoluteUpgradeUrl, bool generateOriginHeader)
    {
        this.uri = uri;
        this.version = version;
        expectedSubprotocol = subprotocol;
        this.customHeaders = customHeaders;
        this.maxFramePayloadLength = maxFramePayloadLength;
        Volatile.Write(ref this.forceCloseTimeoutMillis, forceCloseTimeoutMillis);
        this.absoluteUpgradeUrl = absoluteUpgradeUrl;
        this.generateOriginHeader = generateOriginHeader;
    }

    /**
     * Returns the URI to the web socket. e.g. "ws://myhost.com/path"
     */
    public virtual Uri Uri()
    {
        return uri;
    }

    /**
     * Version of the web socket specification that is being used
     */
    public virtual WebSocketVersion Version()
    {
        return version;
    }

    /**
     * Returns the max length for any frame's payload
     */
    public virtual int MaxFramePayloadLength()
    {
        return maxFramePayloadLength;
    }

    /**
     * Flag to indicate if the opening handshake is complete
     */
    public virtual bool IsHandshakeComplete()
    {
        return handshakeComplete;
    }

    private void SetHandshakeComplete()
    {
        handshakeComplete = true;
    }

    /**
     * Returns the CSV of requested subprotocol(s) sent to the server as specified in the constructor
     */
    public virtual string ExpectedSubprotocol()
    {
        return expectedSubprotocol;
    }

    /**
     * Returns the subprotocol response sent by the server. Only available after end of handshake.
     * Null if no subprotocol was requested or confirmed by the server.
     */
    public virtual string ActualSubprotocol()
    {
        return actualSubprotocol;
    }

    private void SetActualSubprotocol(string actualSubprotocol)
    {
        this.actualSubprotocol = actualSubprotocol;
    }

    public virtual long ForceCloseTimeoutMillis()
    {
        return Volatile.Read(ref forceCloseTimeoutMillis);
    }

    /**
     * Flag to indicate if the closing handshake was initiated because of timeout.
     * For testing only.
     */
    protected virtual bool IsForceCloseComplete()
    {
        return forceCloseComplete;
    }

    // CLR: package access of the Java tests to the protected isForceCloseComplete() (see the class comment).
    internal bool InvokeIsForceCloseComplete()
    {
        return IsForceCloseComplete();
    }

    /**
     * Sets timeout to close the connection if it was not closed by the server.
     *
     * @param forceCloseTimeoutMillis
     *            Close the connection if it was not closed by the server after timeout specified
     */
    public virtual WebSocketClientHandshaker SetForceCloseTimeoutMillis(long forceCloseTimeoutMillis)
    {
        Volatile.Write(ref this.forceCloseTimeoutMillis, forceCloseTimeoutMillis);
        return this;
    }

    /**
     * Begins the opening handshake
     *
     * @param channel
     *            Channel
     */
    public virtual Task HandshakeAsync(IChannel channel)
    {
        ObjectUtil.CheckNotNull(channel, "channel");

        return HandshakeAsync(channel, new TaskCompletionSource());
    }

    /**
     * Begins the opening handshake
     *
     * @param channel
     *            Channel
     * @param promise
     *            the {@link ChannelPromise} to be notified when the opening handshake is sent
     */
    public Task HandshakeAsync(IChannel channel, TaskCompletionSource promise)
    {
        IChannelPipeline pipeline = channel.Pipeline;
        HttpResponseDecoder decoder = pipeline.Get<HttpResponseDecoder>();
        if (decoder == null)
        {
            HttpClientCodec codec = pipeline.Get<HttpClientCodec>();
            if (codec == null)
            {
               promise.SetException(new InvalidOperationException("ChannelPipeline does not contain " +
                       "an HttpResponseDecoder or HttpClientCodec"));
               return promise.Task;
            }
        }

        if (new UriComponents(uri).host == null)
        {
            if (customHeaders == null || !customHeaders.Contains(HttpHeaderNames.HOST))
            {
                promise.SetException(new ArgumentException("Cannot generate the 'host' header value," +
                        " webSocketURI should contain host or passed through customHeaders"));
                return promise.Task;
            }

            if (generateOriginHeader && !customHeaders.Contains(HttpHeaderNames.ORIGIN))
            {
                string originName;

                if (version == WebSocketVersion.V07 || version == WebSocketVersion.V08)
                {
                    originName = HttpHeaderNames.SEC_WEBSOCKET_ORIGIN.ToString();
                }
                else
                {
                    originName = HttpHeaderNames.ORIGIN.ToString();
                }

                promise.SetException(new ArgumentException("Cannot generate the '" + originName + "' header" +
                        " value, webSocketURI should contain host or disable generateOriginHeader or pass value" +
                        " through customHeaders"));
                return promise.Task;
            }
        }

        IFullHttpRequest request = NewHandshakeRequest();

        _ = channel.WriteAndFlushAsync(request).ContinueWith(future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                IChannelHandlerContext ctx = pipeline.Context(typeof(HttpRequestEncoder));
                if (ctx == null)
                {
                    ctx = pipeline.Context(typeof(HttpClientCodec));
                }

                if (ctx == null)
                {
                    promise.SetException(new InvalidOperationException("ChannelPipeline does not contain " +
                            "an HttpRequestEncoder or HttpClientCodec"));
                    return;
                }

                pipeline.AddAfter(ctx.Name, "ws-encoder", NewWebSocketEncoder());

                promise.SetResult();
            }
            else
            {
                promise.TrySetFromTask(future);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise.Task;
    }

    /**
     * Returns a new {@link FullHttpRequest) which will be used for the handshake.
     */
    protected abstract IFullHttpRequest NewHandshakeRequest();

    // CLR: package access of the Java tests to the protected newHandshakeRequest() (see the class comment).
    internal IFullHttpRequest InvokeNewHandshakeRequest()
    {
        return NewHandshakeRequest();
    }

    /**
     * Validates and finishes the opening handshake initiated by {@link #handshake}}.
     *
     * @param channel
     *            Channel
     * @param response
     *            HTTP response containing the closing handshake details
     */
    public void FinishHandshake(IChannel channel, IFullHttpResponse response)
    {
        Verify(response);

        // Verify the subprotocol that we received from the server.
        // This must be one of our expected subprotocols - or null/empty if we didn't want to speak a subprotocol
        string receivedProtocol = response.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL);
        receivedProtocol = receivedProtocol != null ? HttpUtil.TrimJava(receivedProtocol) : null;
        string expectedProtocol = expectedSubprotocol != null ? expectedSubprotocol : "";
        bool protocolValid = false;

        if (expectedProtocol.Length == 0 && receivedProtocol == null)
        {
            // No subprotocol required and none received
            protocolValid = true;
            SetActualSubprotocol(expectedSubprotocol); // null or "" - we echo what the user requested
        }
        else if (expectedProtocol.Length != 0 && receivedProtocol != null && receivedProtocol.Length != 0)
        {
            // We require a subprotocol and received one -> verify it
            foreach (string protocol in Split(expectedProtocol, ','))
            {
                if (HttpUtil.TrimJava(protocol).Equals(receivedProtocol))
                {
                    protocolValid = true;
                    SetActualSubprotocol(receivedProtocol);
                    break;
                }
            }
        } // else mixed cases - which are all errors

        if (!protocolValid)
        {
            throw new WebSocketClientHandshakeException(
                    "Invalid subprotocol. Actual: " + (receivedProtocol ?? "null") + ". Expected one of: " +
                    (expectedSubprotocol ?? "null"), response);
        }

        SetHandshakeComplete();

        IChannelPipeline p = channel.Pipeline;
        // Remove decompressor from pipeline if its in use
        HttpContentDecompressor decompressor = p.Get<HttpContentDecompressor>();
        if (decompressor != null)
        {
            p.Remove(decompressor);
        }

        // Remove aggregator if present before
        HttpObjectAggregator aggregator = p.Get<HttpObjectAggregator>();
        if (aggregator != null)
        {
            p.Remove(aggregator);
        }

        IChannelHandlerContext ctx = p.Context(typeof(HttpResponseDecoder));
        if (ctx == null)
        {
            ctx = p.Context(typeof(HttpClientCodec));
            if (ctx == null)
            {
                throw new InvalidOperationException("ChannelPipeline does not contain " +
                        "an HttpRequestEncoder or HttpClientCodec");
            }

            HttpClientCodec codec =  (HttpClientCodec) ctx.Handler;
            // Remove the encoder part of the codec as the user may start writing frames after this method returns.
            codec.RemoveOutboundHandler();

            p.AddAfter(ctx.Name, "ws-decoder", NewWebsocketDecoder());

            // Delay the removal of the decoder so the user can setup the pipeline if needed to handle
            // WebSocketFrame messages.
            // See https://github.com/netty/netty/issues/4533
            channel.EventLoop.Execute(() => p.Remove(codec));
        }
        else
        {
            if (p.Get<HttpRequestEncoder>() != null)
            {
                // Remove the encoder part of the codec as the user may start writing frames after this method returns.
                p.Remove<HttpRequestEncoder>();
            }

            IChannelHandlerContext context = ctx;
            p.AddAfter(context.Name, "ws-decoder", NewWebsocketDecoder());

            // Delay the removal of the decoder so the user can setup the pipeline if needed to handle
            // WebSocketFrame messages.
            // See https://github.com/netty/netty/issues/4533
            channel.EventLoop.Execute(() => p.Remove(context.Handler));
        }
    }

    /**
     * Process the opening handshake initiated by {@link #handshake}}.
     *
     * @param channel
     *            Channel
     * @param response
     *            HTTP response containing the closing handshake details
     * @return future
     *            the {@link ChannelFuture} which is notified once the handshake completes.
     */
    public Task ProcessHandshakeAsync(IChannel channel, IHttpResponse response)
    {
        return ProcessHandshakeAsync(channel, response, new TaskCompletionSource());
    }

    /**
     * Process the opening handshake initiated by {@link #handshake}}.
     *
     * @param channel
     *            Channel
     * @param response
     *            HTTP response containing the closing handshake details
     * @param promise
     *            the {@link ChannelPromise} to notify once the handshake completes.
     * @return future
     *            the {@link ChannelFuture} which is notified once the handshake completes.
     */
    public Task ProcessHandshakeAsync(IChannel channel, IHttpResponse response,
                                      TaskCompletionSource promise)
    {
        if (response is IFullHttpResponse)
        {
            try
            {
                FinishHandshake(channel, (IFullHttpResponse) response);
                promise.SetResult();
            }
            catch (Exception cause)
            {
                promise.SetException(cause);
            }
        }
        else
        {
            IChannelPipeline p = channel.Pipeline;
            IChannelHandlerContext ctx = p.Context(typeof(HttpResponseDecoder));
            if (ctx == null)
            {
                ctx = p.Context(typeof(HttpClientCodec));
                if (ctx == null)
                {
                    promise.SetException(new InvalidOperationException("ChannelPipeline does not contain " +
                            "an HttpResponseDecoder or HttpClientCodec"));
                    return promise.Task;
                }
            }

            string aggregatorCtx = ctx.Name;

            // Content-Length and Transfer-Encoding must not be sent in any response with a status code of 1xx or 204.
            if (version == WebSocketVersion.V00)
            {
                // Add aggregator and ensure we feed the HttpResponse so it is aggregated. A limit of 8192 should be
                // more then enough for the websockets handshake payload.
                aggregatorCtx = "httpAggregator";
                p.AddAfter(ctx.Name, aggregatorCtx, new HttpObjectAggregator(8192));
            }

            p.AddAfter(aggregatorCtx, "handshaker", new HandshakeResponseHandler(this, channel, promise));

            try
            {
                ctx.FireChannelRead(ReferenceCountUtil.Retain(response));
            }
            catch (Exception cause)
            {
                promise.SetException(cause);
            }
        }

        return promise.Task;
    }

    private sealed class HandshakeResponseHandler : ChannelInboundHandlerAdapter
    {
        private readonly WebSocketClientHandshaker handshaker;
        private readonly IChannel channel;
        private readonly TaskCompletionSource promise;

        private IFullHttpResponse fullHttpResponse;

        internal HandshakeResponseHandler(WebSocketClientHandshaker handshaker, IChannel channel,
                                          TaskCompletionSource promise)
        {
            this.handshaker = handshaker;
            this.channel = channel;
            this.promise = promise;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttpObject)
            {
                try
                {
                    HandleHandshakeResponse(ctx, (IHttpObject) msg);
                }
                finally
                {
                    ReferenceCountUtil.Release(msg);
                }
            }
            else
            {
                base.ChannelRead(ctx, msg);
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            // Remove ourself and fail the handshake promise.
            ctx.Pipeline.Remove(this);
            promise.SetException(cause);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            try
            {
                // Fail promise if Channel was closed
                if (!promise.Task.IsCompleted)
                {
                    promise.TrySetException(new ClosedChannelException());
                }

                ctx.FireChannelInactive();
            }
            finally
            {
                ReleaseFullHttpResponse();
            }
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            ReleaseFullHttpResponse();
        }

        private void HandleHandshakeResponse(IChannelHandlerContext ctx, IHttpObject response)
        {
            if (response is IFullHttpResponse)
            {
                ctx.Pipeline.Remove(this);
                TryFinishHandshake((IFullHttpResponse) response);
                return;
            }

            if (response is ILastHttpContent)
            {
                Debug.Assert(fullHttpResponse != null);
                IFullHttpResponse handshakeResponse = fullHttpResponse;
                fullHttpResponse = null;

                try
                {
                    ctx.Pipeline.Remove(this);
                    TryFinishHandshake(handshakeResponse);
                }
                finally
                {
                    handshakeResponse.Release();
                }

                return;
            }

            if (response is IHttpResponse)
            {
                IHttpResponse httpResponse = (IHttpResponse) response;
                fullHttpResponse = new DefaultFullHttpResponse(httpResponse.ProtocolVersion(),
                    httpResponse.Status(), Unpooled.EmptyBuffer, httpResponse.Headers(),
                    EmptyHttpHeaders.INSTANCE);

                if (httpResponse.DecoderResult().IsFailure())
                {
                    fullHttpResponse.SetDecoderResult(httpResponse.DecoderResult());
                }
            }
        }

        private void TryFinishHandshake(IFullHttpResponse fullHttpResponse)
        {
            try
            {
                handshaker.FinishHandshake(channel, fullHttpResponse);
                promise.SetResult();
            }
            catch (Exception cause)
            {
                promise.SetException(cause);
            }
        }

        private void ReleaseFullHttpResponse()
        {
            if (fullHttpResponse != null)
            {
                fullHttpResponse.Release();
                fullHttpResponse = null;
            }
        }
    }

    /**
     * Verify the {@link FullHttpResponse} and throws a {@link WebSocketHandshakeException} if something is wrong.
     */
    protected abstract void Verify(IFullHttpResponse response);

    /**
     * Returns the decoder to use after handshake is complete.
     */
    protected abstract IWebSocketFrameDecoder NewWebsocketDecoder();

    // CLR: package access of the Java tests to the protected newWebsocketDecoder() (see the class comment).
    internal IWebSocketFrameDecoder InvokeNewWebsocketDecoder()
    {
        return NewWebsocketDecoder();
    }

    /**
     * Returns the encoder to use after the handshake is complete.
     */
    protected abstract IWebSocketFrameEncoder NewWebSocketEncoder();

    // CLR: package access of the Java tests to the protected newWebSocketEncoder() (see the class comment).
    internal IWebSocketFrameEncoder InvokeNewWebSocketEncoder()
    {
        return NewWebSocketEncoder();
    }

    /**
     * Performs the closing handshake.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #close(ChannelHandlerContext, CloseWebSocketFrame)}.
     *
     * @param channel
     *            Channel
     * @param frame
     *            Closing Frame that was received
     */
    public virtual Task CloseAsync(IChannel channel, CloseWebSocketFrame frame)
    {
        ObjectUtil.CheckNotNull(channel, "channel");

        return CloseAsync(channel, frame, new TaskCompletionSource());
    }

    /**
     * Performs the closing handshake
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #close(ChannelHandlerContext, CloseWebSocketFrame, ChannelPromise)}.
     *
     * @param channel
     *            Channel
     * @param frame
     *            Closing Frame that was received
     * @param promise
     *            the {@link ChannelPromise} to be notified when the closing handshake is done
     */
    public virtual Task CloseAsync(IChannel channel, CloseWebSocketFrame frame, TaskCompletionSource promise)
    {
        ObjectUtil.CheckNotNull(channel, "channel");

        return Close0(channel, channel, frame, promise);
    }

    /**
     * Performs the closing handshake
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param frame
     *            Closing Frame that was received
     */
    public virtual Task CloseAsync(IChannelHandlerContext ctx, CloseWebSocketFrame frame)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return CloseAsync(ctx, frame, new TaskCompletionSource());
    }

    /**
     * Performs the closing handshake
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param frame
     *            Closing Frame that was received
     * @param promise
     *            the {@link ChannelPromise} to be notified when the closing handshake is done
     */
    public virtual Task CloseAsync(IChannelHandlerContext ctx, CloseWebSocketFrame frame,
                                   TaskCompletionSource promise)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return Close0(ctx, ctx.Channel, frame, promise);
    }

    private Task Close0(IChannelOutboundInvoker invoker, IChannel channel,
                        CloseWebSocketFrame frame, TaskCompletionSource promise)
    {
        Notify(invoker.WriteAndFlushAsync(frame), promise);
        long forceCloseTimeoutMillis = Volatile.Read(ref this.forceCloseTimeoutMillis);
        WebSocketClientHandshaker handshaker = this;
        if (forceCloseTimeoutMillis <= 0 || !channel.IsActive || forceCloseInit != 0)
        {
            return promise.Task;
        }

        _ = promise.Task.ContinueWith(future =>
        {
            // If flush operation failed, there is no reason to expect
            // a server to receive CloseFrame. Thus this should be handled
            // by the application separately.
            // Also, close might be called twice from different threads.
            if (future.IsCompletedSuccessfully && channel.IsActive &&
                    Interlocked.CompareExchange(ref handshaker.forceCloseInit, 1, 0) == 0)
            {
                CancellationTokenSource forceCloseFuture = new CancellationTokenSource();
                _ = channel.EventLoop.ScheduleAsync(() =>
                {
                    if (channel.IsActive)
                    {
                        _ = invoker.CloseAsync();
                        forceCloseComplete = true;
                    }
                }, TimeSpan.FromMilliseconds(forceCloseTimeoutMillis), forceCloseFuture.Token);
                _ = channel.CloseFuture.ContinueWith(f => forceCloseFuture.Cancel(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise.Task;
    }

    // CLR: the write future completes the caller's promise, as writeAndFlush(msg, promise) does in Java.
    private static void Notify(Task future, TaskCompletionSource promise)
    {
        _ = future.ContinueWith(f => promise.TrySetFromTask(f), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /**
     * Return the constructed raw path for the give {@link URI}.
     */
    protected virtual string UpgradeUrl(Uri wsURL)
    {
        if (absoluteUpgradeUrl)
        {
            return wsURL.OriginalString;
        }

        UriComponents components = new UriComponents(wsURL);
        string path = components.rawPath;
        path = path == null || path.Length == 0 ? "/" : path;
        string query = components.rawQuery;
        return query != null && query.Length != 0 ? path + '?' + query : path;
    }

    internal static string WebsocketHostValue(Uri wsURL)
    {
        UriComponents components = new UriComponents(wsURL);
        int port = components.port;
        if (port == -1)
        {
            return components.host;
        }

        string host = components.host;
        string scheme = components.scheme;
        if (port == HttpScheme.HTTP.Port())
        {
            return ContentEquals(HttpScheme.HTTP.Name(), scheme)
                    || ContentEquals(WebSocketScheme.WS.Name(), scheme) ?
                    host : NetUtil.ToSocketAddressString(host, port);
        }
        if (port == HttpScheme.HTTPS.Port())
        {
            return ContentEquals(HttpScheme.HTTPS.Name(), scheme)
                    || ContentEquals(WebSocketScheme.WSS.Name(), scheme) ?
                    host : NetUtil.ToSocketAddressString(host, port);
        }

        // if the port is not standard (80/443) its needed to add the port to the header.
        // See https://tools.ietf.org/html/rfc6454#section-6.2
        return NetUtil.ToSocketAddressString(host, port);
    }

    internal static string WebsocketOriginValue(Uri wsURL)
    {
        UriComponents components = new UriComponents(wsURL);
        string scheme = components.scheme;
        string schemePrefix;
        int port = components.port;
        int defaultPort;

        if (ContentEquals(WebSocketScheme.WSS.Name(), scheme)
            || ContentEquals(HttpScheme.HTTPS.Name(), scheme)
            || (scheme == null && port == WebSocketScheme.WSS.Port()))
        {

            schemePrefix = HTTPS_SCHEME_PREFIX;
            defaultPort = WebSocketScheme.WSS.Port();
        }
        else
        {
            schemePrefix = HTTP_SCHEME_PREFIX;
            defaultPort = WebSocketScheme.WS.Port();
        }

        // Convert uri-host to lower case (by RFC 6454, chapter 4 "Origin of a URI")
        string host = components.host.ToLowerInvariant();

        if (port != defaultPort && port != -1)
        {
            // if the port is not standard (80/443) its needed to add the port to the header.
            // See https://tools.ietf.org/html/rfc6454#section-6.2
            return schemePrefix + NetUtil.ToSocketAddressString(host, port);
        }

        return schemePrefix + host;
    }

    private static bool ContentEquals(AsciiString name, string value)
    {
        return value != null && name.ToString().Equals(value, StringComparison.Ordinal);
    }

    // CLR: String.split with a one-character literal (see the class comment).
    private static string[] Split(string value, char separator)
    {
        string[] parts = value.Split(separator);
        if (parts.Length == 1)
        {
            return parts;
        }

        int length = parts.Length;

        while (length > 0 && parts[length - 1].Length == 0)
        {
            length--;
        }

        return length == parts.Length ? parts : parts.AsSpan(0, length).ToArray();
    }

    // CLR: the java.net.URI accessors read from Uri.OriginalString (see the class comment).
    private readonly struct UriComponents
    {
        internal readonly string scheme;
        internal readonly string host;
        internal readonly int port;
        internal readonly string rawPath;
        internal readonly string rawQuery;

        internal UriComponents(Uri uri)
        {
            scheme = null;
            host = null;
            port = -1;
            rawPath = null;
            rawQuery = null;
            string s = uri.OriginalString;
            int fragment = s.IndexOf('#');
            if (fragment != -1)
            {
                s = s.Substring(0, fragment);
            }

            int i = 0;

            while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '+' || s[i] == '-' || s[i] == '.'))
            {
                i++;
            }

            int start = 0;

            if (i > 0 && i < s.Length && s[i] == ':' && char.IsAsciiLetter(s[0]))
            {
                scheme = s.Substring(0, i);
                start = i + 1;
                if (start >= s.Length || s[start] != '/')
                {
                    // An opaque URI has no authority, path or query.
                    return;
                }
            }

            if (string.CompareOrdinal(s, start, "//", 0, 2) == 0)
            {
                int authorityStart = start + 2;
                int authorityEnd = authorityStart;

                while (authorityEnd < s.Length && s[authorityEnd] != '/' && s[authorityEnd] != '?')
                {
                    authorityEnd++;
                }

                string authority = s.Substring(authorityStart, authorityEnd - authorityStart);
                ParseServerAuthority(authority, out host, out port);
                start = authorityEnd;
            }

            int query = s.IndexOf('?', start);
            if (query == -1)
            {
                rawPath = s.Substring(start);
            }
            else
            {
                rawPath = s.Substring(start, query - start);
                rawQuery = s.Substring(query + 1);
            }
        }

        private static void ParseServerAuthority(string authority, out string host, out int port)
        {
            host = null;
            port = -1;
            int at = authority.LastIndexOf('@');
            string hostPort = at == -1 ? authority : authority.Substring(at + 1);
            string h;
            string p = null;

            if (hostPort.StartsWith('['))
            {
                int close = hostPort.IndexOf(']');
                if (close == -1)
                {
                    return;
                }

                h = hostPort.Substring(0, close + 1);

                if (close + 1 < hostPort.Length)
                {
                    if (hostPort[close + 1] != ':')
                    {
                        return;
                    }

                    p = hostPort.Substring(close + 2);
                }
            }
            else
            {
                int colon = hostPort.IndexOf(':');
                h = colon == -1 ? hostPort : hostPort.Substring(0, colon);
                p = colon == -1 ? null : hostPort.Substring(colon + 1);
            }

            int parsedPort = -1;

            if (!string.IsNullOrEmpty(p))
            {
                if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out parsedPort))
                {
                    // Not a server-based authority: Java reports no host and no port.
                    return;
                }
            }

            if (h.Length == 0)
            {
                return;
            }

            host = h;
            port = parsedPort;
        }
    }
}
