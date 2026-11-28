/*
 * Copyright 2016 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * HttpServerKeepAliveHandler helps close persistent connections when appropriate.
 * <p>
 * The server channel is expected to set the proper 'Connection' header if it can handle persistent connections. {@link
 * HttpServerKeepAliveHandler} will automatically close the channel for any LastHttpContent that corresponds to a client
 * request for closing the connection, or if the HttpResponse associated with that LastHttpContent requested closing the
 * connection or didn't have a self defined message length.
 * <p>
 * Since {@link HttpServerKeepAliveHandler} expects {@link HttpObject}s it should be added after {@link HttpServerCodec}
 * but before any other handlers that might send a {@link HttpResponse}. <blockquote>
 * <pre>
 *  {@link ChannelPipeline} p = ...;
 *  ...
 *  p.addLast("serverCodec", new {@link HttpServerCodec}());
 *  p.addLast("httpKeepAlive", <b>new {@link HttpServerKeepAliveHandler}()</b>);
 *  p.addLast("aggregator", new {@link HttpObjectAggregator}(1048576));
 *  ...
 *  p.addLast("handler", new HttpRequestHandler());
 *  </pre>
 * </blockquote>
 */
// CLR: write(ctx, msg, promise) is WriteAsync, whose Task replaces the promise (docs/codec-base-porting.md, Outbound
// operations). Java adds ChannelFutureListener.CLOSE to the caller's promise before writing, so the channel closes when
// the write completes or fails; the C# handler closes it from a continuation of the returned Task that runs on the
// channel's executor at once when the write completes on the event loop (Common's EventExecutorTaskScheduler over
// ctx.Executor, the Java listener timing of DefaultPromise.notifyListeners(); docs/handler-porting.md, Listener
// timing), or before rethrowing when the write throws (the pipeline then fails the caller's Task); promise.unvoid() has
// no counterpart because there is no void promise. String.regionMatches(true, ...) is
// string.Compare with StringComparison.OrdinalIgnoreCase over the prefix length (a shorter value does not match).
public class HttpServerKeepAliveHandler : ChannelDuplexHandler
{
    private const string MULTIPART_PREFIX = "multipart";

    private bool persistentConnection = true;
    // Track pending responses to support client pipelining: https://tools.ietf.org/html/rfc7230#section-6.3.2
    private int pendingResponses;

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        // read message and track if it was keepAlive
        if (msg is IHttpRequest)
        {
            IHttpRequest request = (IHttpRequest) msg;

            if (persistentConnection)
            {
                pendingResponses += 1;
                persistentConnection = IsKeepAlive(request);
            }
        }

        base.ChannelRead(ctx, msg);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        // modify message on way out to add headers if needed
        if (msg is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) msg;
            TrackResponse(response);

            // Assume the response writer knows if they can persist or not and sets isKeepAlive on the response
            if (!IsKeepAlive(response) || !IsSelfDefinedMessageLength(response))
            {
                // No longer keep alive as the client can't tell when the message is done unless we close connection
                pendingResponses = 0;
                persistentConnection = false;
            }

            // Server might think it can keep connection alive, but we should fix response header if we know better
            if (!ShouldKeepAlive())
            {
                SetKeepAlive(response, false);
            }
        }

        if (msg is ILastHttpContent && !ShouldKeepAlive())
        {
            IChannel channel = ctx.Channel;
            Task future;

            try
            {
                future = base.WriteAsync(ctx, msg, cancellationToken);
            }
            catch
            {
                _ = channel.CloseAsync();
                throw;
            }

            // CLR: the scheduler's lambda captures this branch's copy of ctx, so the other writes allocate no closure
            // (see EventExecutorTaskScheduler).
            IChannelHandlerContext listenerCtx = ctx;
            _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => listenerCtx.Executor));
            return future;
        }

        return base.WriteAsync(ctx, msg, cancellationToken);
    }

    private void TrackResponse(IHttpResponse response)
    {
        if (!IsInformational(response))
        {
            pendingResponses -= 1;
        }
    }

    private bool ShouldKeepAlive()
    {
        return pendingResponses != 0 || persistentConnection;
    }

    /**
     * Keep-alive only works if the client can detect when the message has ended without relying on the connection being
     * closed.
     * <p>
     * <ul>
     *     <li>See <a href="https://tools.ietf.org/html/rfc7230#section-6.3"/></li>
     *     <li>See <a href="https://tools.ietf.org/html/rfc7230#section-3.3.2"/></li>
     *     <li>See <a href="https://tools.ietf.org/html/rfc7230#section-3.3.3"/></li>
     * </ul>
     *
     * @param response The HttpResponse to check
     *
     * @return true if the response has a self defined message length.
     */
    private static bool IsSelfDefinedMessageLength(IHttpResponse response)
    {
        return IsContentLengthSet(response) || IsTransferEncodingChunked(response) || IsMultipart(response) ||
               IsInformational(response) || response.Status().Code() == HttpResponseStatus.NO_CONTENT.Code();
    }

    private static bool IsInformational(IHttpResponse response)
    {
        return response.Status().CodeClass() == HttpStatusClass.INFORMATIONAL;
    }

    private static bool IsMultipart(IHttpResponse response)
    {
        string contentType = response.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        return contentType != null &&
               string.Compare(contentType, 0, MULTIPART_PREFIX, 0, MULTIPART_PREFIX.Length,
                       StringComparison.OrdinalIgnoreCase) == 0;
    }
}
