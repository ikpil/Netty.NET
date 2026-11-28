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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Base class for server side web socket opening and closing handshakes
 */
// CLR: ChannelFuture is Task and ChannelPromise is TaskCompletionSource, with the promise overloads, the new
// TaskCompletionSource for channel.newPromise()/ctx.newPromise(), the synchronous listeners on TaskScheduler.Default
// and the Async suffix of WebSocketClientHandshaker (see its class comment). ChannelFutureListener.CLOSE closes
// future.channel(); a Task has no channel, so the close continuations close the channel the operation ran on (the given
// channel, or ctx.Channel). The @Deprecated uri() has no @deprecated text, so it names no replacement and is ported
// with [Obsolete] (owner rule 7). Set<String> with LinkedHashSet is ISet<string> with the Common LinkedHashSet
// (insertion order). String.split(",") keeps Java's result in Split ("Trailing empty strings are therefore not included
// in the resulting array", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html) and
// trim() is core's HttpUtil.TrimJava. The anonymous handshake request handler is the named private nested class
// HandshakeRequestHandler. The protected newHandshakeResponse(...), newWebsocketDecoder() and newWebSocketEncoder(),
// which the Java tests reach through package access, have internal forwarders (InvokeNewHandshakeResponse, ...), as
// core's HttpObjectEncoder.InvokeEncodeInitialLine. Non-final Java methods are virtual; SUB_PROTOCOL_WILDCARD is a
// const. IllegalStateException is InvalidOperationException, and assert is Debug.Assert.
public abstract class WebSocketServerHandshaker
{
    protected static readonly IInternalLogger logger =
        InternalLoggerFactory.GetInstance(typeof(WebSocketServerHandshaker));

    private readonly string uri;

    private readonly string[] subprotocols;

    private readonly WebSocketVersion version;

    private readonly WebSocketDecoderConfig decoderConfig;

    private string selectedSubprotocol;

    /**
     * Use this as wildcard to support all requested sub-protocols
     */
    public const string SUB_PROTOCOL_WILDCARD = "*";

    /**
     * Constructor specifying the destination web socket location
     *
     * @param version
     *            the protocol version
     * @param uri
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload
     */
    protected WebSocketServerHandshaker(
            WebSocketVersion version, string uri, string subprotocols,
            int maxFramePayloadLength) : this(version, uri, subprotocols, WebSocketDecoderConfig.NewBuilder()
            .MaxFramePayloadLength(maxFramePayloadLength)
            .Build())
    {
    }

    /**
     * Constructor specifying the destination web socket location
     *
     * @param version
     *            the protocol version
     * @param uri
     *            URL for web socket communications. e.g "ws://myhost.com/mypath". Subsequent web socket frames will be
     *            sent to this URL.
     * @param subprotocols
     *            CSV of supported protocols. Null if sub protocols not supported.
     * @param decoderConfig
     *            Frames decoder configuration.
     */
    protected WebSocketServerHandshaker(
            WebSocketVersion version, string uri, string subprotocols, WebSocketDecoderConfig decoderConfig)
    {
        this.version = version;
        this.uri = uri;

        if (subprotocols != null)
        {
            string[] subprotocolArray = Split(subprotocols, ',');

            for (int i = 0; i < subprotocolArray.Length; i++)
            {
                subprotocolArray[i] = HttpUtil.TrimJava(subprotocolArray[i]);
            }

            this.subprotocols = subprotocolArray;
        }
        else
        {
            this.subprotocols = EmptyArrays.EMPTY_STRINGS;
        }

        this.decoderConfig = ObjectUtil.CheckNotNull(decoderConfig, "decoderConfig");
    }

    /**
     * Returns the URL of the web socket
     */
    [Obsolete]
    public virtual string Uri()
    {
        return uri;
    }

    /**
     * Returns the CSV of supported sub protocols
     */
    public virtual ISet<string> Subprotocols()
    {
        ISet<string> ret = new LinkedHashSet<string>();

        foreach (string subprotocol in subprotocols)
        {
            ret.Add(subprotocol);
        }

        return ret;
    }

    /**
     * Returns the version of the specification being supported
     */
    public virtual WebSocketVersion Version()
    {
        return version;
    }

    /**
     * Gets the maximum length for any frame's payload.
     *
     * @return The maximum length for a frame's payload
     */
    public virtual int MaxFramePayloadLength()
    {
        return decoderConfig.MaxFramePayloadLength();
    }

    /**
     * Gets this decoder configuration.
     *
     * @return This decoder configuration.
     */
    public virtual WebSocketDecoderConfig DecoderConfig()
    {
        return decoderConfig;
    }

    /**
     * Performs the opening handshake. When call this method you <strong>MUST NOT</strong> retain the
     * {@link FullHttpRequest} which is passed in.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #handshake(ChannelHandlerContext, FullHttpRequest)}.
     *
     * @param channel
     *              Channel
     * @param req
     *              HTTP Request
     * @return future
     *              The {@link ChannelFuture} which is notified once the opening handshake completes
     */
    public virtual Task HandshakeAsync(IChannel channel, IFullHttpRequest req)
    {
        return HandshakeAsync(channel, req, null, new TaskCompletionSource());
    }

    /**
     * Performs the opening handshake
     *
     * When call this method you <strong>MUST NOT</strong> retain the {@link FullHttpRequest} which is passed in.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #handshake(ChannelHandlerContext, FullHttpRequest, HttpHeaders, ChannelPromise)}.
     *
     * @param channel
     *            Channel
     * @param req
     *            HTTP Request
     * @param responseHeaders
     *            Extra headers to add to the handshake response or {@code null} if no extra headers should be added
     * @param promise
     *            the {@link ChannelPromise} to be notified when the opening handshake is done
     * @return future
     *            the {@link ChannelFuture} which is notified when the opening handshake is done
     */
    public Task HandshakeAsync(IChannel channel, IFullHttpRequest req,
                               HttpHeaders responseHeaders, TaskCompletionSource promise)
    {
        return Handshake0(channel, channel, req, responseHeaders, promise);
    }

    /**
     * Performs the opening handshake. When call this method you <strong>MUST NOT</strong> retain the
     * {@link FullHttpRequest} which is passed in.
     *
     * The handshake response is written to the given {@link ChannelHandlerContext}, so handlers placed
     * after the {@link ChannelHandler} the context belongs to will not see the response. The handler the
     * context belongs to must be placed after the HTTP encoder as the response still needs to be encoded.
     *
     * @param ctx
     *              the {@link ChannelHandlerContext} to use.
     * @param req
     *              HTTP Request
     * @return future
     *              The {@link ChannelFuture} which is notified once the opening handshake completes
     */
    public virtual Task HandshakeAsync(IChannelHandlerContext ctx, IFullHttpRequest req)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return HandshakeAsync(ctx, req, null, new TaskCompletionSource());
    }

    /**
     * Performs the opening handshake
     *
     * When call this method you <strong>MUST NOT</strong> retain the {@link FullHttpRequest} which is passed in.
     *
     * The handshake response is written to the given {@link ChannelHandlerContext}, so handlers placed
     * after the {@link ChannelHandler} the context belongs to will not see the response. The handler the
     * context belongs to must be placed after the HTTP encoder as the response still needs to be encoded.
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param req
     *            HTTP Request
     * @param responseHeaders
     *            Extra headers to add to the handshake response or {@code null} if no extra headers should be added
     * @param promise
     *            the {@link ChannelPromise} to be notified when the opening handshake is done
     * @return future
     *            the {@link ChannelFuture} which is notified when the opening handshake is done
     */
    public virtual Task HandshakeAsync(IChannelHandlerContext ctx, IFullHttpRequest req,
                                       HttpHeaders responseHeaders, TaskCompletionSource promise)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return Handshake0(ctx, ctx.Channel, req, responseHeaders, promise);
    }

    private Task Handshake0(IChannelOutboundInvoker invoker, IChannel channel, IFullHttpRequest req,
                                     HttpHeaders responseHeaders, TaskCompletionSource promise)
    {

        if (logger.IsDebugEnabled())
        {
            logger.Debug("{} WebSocket version {} server handshake", channel, Version());
        }

        IFullHttpResponse response = NewHandshakeResponse(req, responseHeaders);
        IChannelPipeline p = channel.Pipeline;
        if (p.Get<HttpObjectAggregator>() != null)
        {
            p.Remove<HttpObjectAggregator>();
        }

        if (p.Get<HttpContentCompressor>() != null)
        {
            p.Remove<HttpContentCompressor>();
        }

        IChannelHandlerContext ctx = p.Context(typeof(HttpRequestDecoder));
        string encoderName;

        if (ctx == null)
        {
            // this means the user use an HttpServerCodec
            ctx = p.Context(typeof(HttpServerCodec));
            if (ctx == null)
            {
                promise.SetException(
                        new InvalidOperationException("No HttpDecoder and no HttpServerCodec in the pipeline"));
                response.Release();
                return promise.Task;
            }

            p.AddBefore(ctx.Name, "wsencoder", NewWebSocketEncoder());
            p.AddBefore(ctx.Name, "wsdecoder", NewWebsocketDecoder());
            encoderName = ctx.Name;
        }
        else
        {
            p.Replace(ctx.Name, "wsdecoder", NewWebsocketDecoder());

            encoderName = p.Context(typeof(HttpResponseEncoder)).Name;
            p.AddBefore(encoderName, "wsencoder", NewWebSocketEncoder());
        }

        _ = invoker.WriteAndFlushAsync(response).ContinueWith(future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                IChannelPipeline p1 = channel.Pipeline;
                p1.Remove(encoderName);
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
     * Performs the opening handshake. When call this method you <strong>MUST NOT</strong> retain the
     * {@link FullHttpRequest} which is passed in.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #handshake(ChannelHandlerContext, HttpRequest)}.
     *
     * @param channel
     *              Channel
     * @param req
     *              HTTP Request
     * @return future
     *              The {@link ChannelFuture} which is notified once the opening handshake completes
     */
    public virtual Task HandshakeAsync(IChannel channel, IHttpRequest req)
    {
        return HandshakeAsync(channel, req, null, new TaskCompletionSource());
    }

    /**
     * Performs the opening handshake
     *
     * When call this method you <strong>MUST NOT</strong> retain the {@link HttpRequest} which is passed in.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #handshake(ChannelHandlerContext, HttpRequest, HttpHeaders, ChannelPromise)}.
     *
     * @param channel
     *            Channel
     * @param req
     *            HTTP Request
     * @param responseHeaders
     *            Extra headers to add to the handshake response or {@code null} if no extra headers should be added
     * @param promise
     *            the {@link ChannelPromise} to be notified when the opening handshake is done
     * @return future
     *            the {@link ChannelFuture} which is notified when the opening handshake is done
     */
    public Task HandshakeAsync(IChannel channel, IHttpRequest req,
                               HttpHeaders responseHeaders, TaskCompletionSource promise)
    {
        return Handshake0(channel, channel, req, responseHeaders, promise);
    }

    /**
     * Performs the opening handshake. When call this method you <strong>MUST NOT</strong> retain the
     * {@link HttpRequest} which is passed in.
     *
     * The handshake response is written to the given {@link ChannelHandlerContext}, so handlers placed
     * after the {@link ChannelHandler} the context belongs to will not see the response. The handler the
     * context belongs to must be placed after the HTTP encoder as the response still needs to be encoded.
     *
     * @param ctx
     *              the {@link ChannelHandlerContext} to use.
     * @param req
     *              HTTP Request
     * @return future
     *              The {@link ChannelFuture} which is notified once the opening handshake completes
     */
    public virtual Task HandshakeAsync(IChannelHandlerContext ctx, IHttpRequest req)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return HandshakeAsync(ctx, req, null, new TaskCompletionSource());
    }

    /**
     * Performs the opening handshake
     *
     * When call this method you <strong>MUST NOT</strong> retain the {@link HttpRequest} which is passed in.
     *
     * The handshake response is written to the given {@link ChannelHandlerContext}, so handlers placed
     * after the {@link ChannelHandler} the context belongs to will not see the response. The handler the
     * context belongs to must be placed after the HTTP encoder as the response still needs to be encoded.
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param req
     *            HTTP Request
     * @param responseHeaders
     *            Extra headers to add to the handshake response or {@code null} if no extra headers should be added
     * @param promise
     *            the {@link ChannelPromise} to be notified when the opening handshake is done
     * @return future
     *            the {@link ChannelFuture} which is notified when the opening handshake is done
     */
    public virtual Task HandshakeAsync(IChannelHandlerContext ctx, IHttpRequest req,
                                       HttpHeaders responseHeaders, TaskCompletionSource promise)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return Handshake0(ctx, ctx.Channel, req, responseHeaders, promise);
    }

    private Task Handshake0(IChannelOutboundInvoker invoker, IChannel channel, IHttpRequest req,
                            HttpHeaders responseHeaders, TaskCompletionSource promise)
    {
        if (req is IFullHttpRequest)
        {
            return Handshake0(invoker, channel, (IFullHttpRequest) req, responseHeaders, promise);
        }

        if (logger.IsDebugEnabled())
        {
            logger.Debug("{} WebSocket version {} server handshake", channel, Version());
        }

        IChannelPipeline p = channel.Pipeline;
        IChannelHandlerContext ctx = p.Context(typeof(HttpRequestDecoder));
        if (ctx == null)
        {
            // this means the user use an HttpServerCodec
            ctx = p.Context(typeof(HttpServerCodec));
            if (ctx == null)
            {
                promise.SetException(
                        new InvalidOperationException("No HttpDecoder and no HttpServerCodec in the pipeline"));
                return promise.Task;
            }
        }

        string aggregatorCtx = ctx.Name;

        if (HttpUtil.IsContentLengthSet(req) || HttpUtil.IsTransferEncodingChunked(req) ||
            version == WebSocketVersion.V00)
        {
            // Add aggregator and ensure we feed the HttpRequest so it is aggregated. A limit of 8192 should be
            // more then enough for the websockets handshake payload.
            aggregatorCtx = "httpAggregator";
            p.AddAfter(ctx.Name, aggregatorCtx, new HttpObjectAggregator(8192));
        }

        p.AddAfter(aggregatorCtx, "handshaker",
                   new HandshakeRequestHandler(this, invoker, channel, responseHeaders, promise));

        try
        {
            ctx.FireChannelRead(ReferenceCountUtil.Retain(req));
        }
        catch (Exception cause)
        {
            promise.SetException(cause);
        }

        return promise.Task;
    }

    private sealed class HandshakeRequestHandler : ChannelInboundHandlerAdapter
    {
        private readonly WebSocketServerHandshaker handshaker;
        private readonly IChannelOutboundInvoker invoker;
        private readonly IChannel channel;
        private readonly HttpHeaders responseHeaders;
        private readonly TaskCompletionSource promise;

        private IFullHttpRequest fullHttpRequest;

        internal HandshakeRequestHandler(WebSocketServerHandshaker handshaker, IChannelOutboundInvoker invoker,
                                         IChannel channel, HttpHeaders responseHeaders, TaskCompletionSource promise)
        {
            this.handshaker = handshaker;
            this.invoker = invoker;
            this.channel = channel;
            this.responseHeaders = responseHeaders;
            this.promise = promise;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttpObject)
            {
                try
                {
                    HandleHandshakeRequest(ctx, (IHttpObject) msg);
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
            promise.TrySetException(cause);
            ctx.FireExceptionCaught(cause);
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
                ReleaseFullHttpRequest();
            }
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            ReleaseFullHttpRequest();
        }

        private void HandleHandshakeRequest(IChannelHandlerContext ctx, IHttpObject httpObject)
        {
            if (httpObject is IFullHttpRequest)
            {
                ctx.Pipeline.Remove(this);
                handshaker.Handshake0(invoker, channel, (IFullHttpRequest) httpObject, responseHeaders, promise);
                return;
            }

            if (httpObject is ILastHttpContent)
            {
                Debug.Assert(fullHttpRequest != null);
                IFullHttpRequest handshakeRequest = fullHttpRequest;
                fullHttpRequest = null;

                try
                {
                    ctx.Pipeline.Remove(this);
                    handshaker.Handshake0(invoker, channel, handshakeRequest, responseHeaders, promise);
                }
                finally
                {
                    handshakeRequest.Release();
                }

                return;
            }

            if (httpObject is IHttpRequest)
            {
                IHttpRequest httpRequest = (IHttpRequest) httpObject;
                fullHttpRequest = new DefaultFullHttpRequest(httpRequest.ProtocolVersion(), httpRequest.Method(),
                    httpRequest.Uri(), Unpooled.EmptyBuffer, httpRequest.Headers(), EmptyHttpHeaders.INSTANCE);

                if (httpRequest.DecoderResult().IsFailure())
                {
                    fullHttpRequest.SetDecoderResult(httpRequest.DecoderResult());
                }
            }
        }

        private void ReleaseFullHttpRequest()
        {
            if (fullHttpRequest != null)
            {
                fullHttpRequest.Release();
                fullHttpRequest = null;
            }
        }
    }

    /**
     * Returns a new {@link FullHttpResponse) which will be used for as response to the handshake request.
     */
    protected abstract IFullHttpResponse NewHandshakeResponse(IFullHttpRequest req,
                                         HttpHeaders responseHeaders);

    // CLR: package access of the Java tests to the protected newHandshakeResponse(...) (see the class comment).
    internal IFullHttpResponse InvokeNewHandshakeResponse(IFullHttpRequest req, HttpHeaders responseHeaders)
    {
        return NewHandshakeResponse(req, responseHeaders);
    }

    /**
     * Performs the closing handshake.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #close(ChannelHandlerContext, CloseWebSocketFrame)}.
     *
     * @param channel
     *            the {@link Channel} to use.
     * @param frame
     *            Closing Frame that was received.
     */
    public virtual Task CloseAsync(IChannel channel, CloseWebSocketFrame frame)
    {
        ObjectUtil.CheckNotNull(channel, "channel");

        return CloseAsync(channel, frame, new TaskCompletionSource());
    }

    /**
     * Performs the closing handshake.
     *
     * When called from within a {@link ChannelHandler} you most likely want to use
     * {@link #close(ChannelHandlerContext, CloseWebSocketFrame, ChannelPromise)}.
     *
     * @param channel
     *            the {@link Channel} to use.
     * @param frame
     *            Closing Frame that was received.
     * @param promise
     *            the {@link ChannelPromise} to be notified when the closing handshake is done
     */
    public virtual Task CloseAsync(IChannel channel, CloseWebSocketFrame frame, TaskCompletionSource promise)
    {
        return Close0(channel, channel, frame, promise);
    }

    /**
     * Performs the closing handshake.
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param frame
     *            Closing Frame that was received.
     */
    public virtual Task CloseAsync(IChannelHandlerContext ctx, CloseWebSocketFrame frame)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        return CloseAsync(ctx, frame, new TaskCompletionSource());
    }

    /**
     * Performs the closing handshake.
     *
     * @param ctx
     *            the {@link ChannelHandlerContext} to use.
     * @param frame
     *            Closing Frame that was received.
     * @param promise
     *            the {@link ChannelPromise} to be notified when the closing handshake is done.
     */
    public virtual Task CloseAsync(IChannelHandlerContext ctx, CloseWebSocketFrame frame,
                                   TaskCompletionSource promise)
    {
        ObjectUtil.CheckNotNull(ctx, "ctx");

        Task future = Close0(ctx, ctx.Channel, frame, promise);
        IChannel channel = ctx.Channel;
        _ = future.ContinueWith(f => channel.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return future;
    }

    private static Task Close0(IChannelOutboundInvoker invoker, IChannel channel, CloseWebSocketFrame frame,
                               TaskCompletionSource promise)
    {
        _ = invoker.WriteAndFlushAsync(frame).ContinueWith(f => promise.TrySetFromTask(f), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = promise.Task.ContinueWith(f => channel.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise.Task;
    }

    /**
     * Selects the first matching supported sub protocol
     *
     * @param requestedSubprotocols
     *            CSV of protocols to be supported. e.g. "chat, superchat"
     * @return First matching supported sub protocol. Null if not found.
     */
    protected virtual string SelectSubprotocol(string requestedSubprotocols)
    {
        if (requestedSubprotocols == null || subprotocols.Length == 0)
        {
            return null;
        }

        string[] requestedSubprotocolArray = Split(requestedSubprotocols, ',');

        foreach (string p in requestedSubprotocolArray)
        {
            string requestedSubprotocol = HttpUtil.TrimJava(p);

            foreach (string supportedSubprotocol in subprotocols)
            {
                if (SUB_PROTOCOL_WILDCARD.Equals(supportedSubprotocol)
                        || requestedSubprotocol.Equals(supportedSubprotocol))
                {
                    selectedSubprotocol = requestedSubprotocol;
                    return requestedSubprotocol;
                }
            }
        }

        // No match found
        return null;
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

    /**
     * Returns the selected subprotocol. Null if no subprotocol has been selected.
     * <p>
     * This is only available AFTER <tt>handshake()</tt> has been called.
     * </p>
     */
    public virtual string SelectedSubprotocol()
    {
        return selectedSubprotocol;
    }

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

}
