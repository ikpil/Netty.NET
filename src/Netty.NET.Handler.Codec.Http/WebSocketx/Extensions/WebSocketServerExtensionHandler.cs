/*
 * Copyright 2014 The Netty Project
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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

/**
 * This handler negotiates and initializes the WebSocket Extensions.
 *
 * It negotiates the extensions based on the client desired order,
 * ensures that the successfully negotiated extensions are consistent between them,
 * and initializes the channel pipeline with the extension decoder and encoder.
 *
 * Find a basic implementation for compression extensions at
 * <tt>io.netty.handler.codec.http.websocketx.extensions.compression.WebSocketServerCompressionHandler</tt>.
 */
// CLR: write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose Task replaces the promise
// (docs/codec-base-porting.md, Outbound operations); the protected onHttpResponseWrite(ctx, response, promise) keeps
// its name and returns that Task (it takes the caller's CancellationToken), as handler's
// AbstractTrafficShapingHandler.SubmitWrite does. handlePotentialUpgrade adds two listeners to the caller's promise
// before the response is written; here a TaskCompletionSource stands for that promise, the listeners are continuations
// of its Task, and the Task of base.WriteAsync completes it with TrySetFromTask, which copies "the completion status
// (including exception or cancellation information)"
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0;
// docs/common-task-composition.md). Java's DefaultPromise notifies the listeners at once when the write completes on
// the event loop; the continuations therefore run on Common's EventExecutorTaskScheduler over ctx.Executor, the
// scheduler that handler's IdleStateHandler uses too (docs/handler-porting.md, Listener timing), so the
// pipeline is changed before writeOutbound returns, as the tests check (measured: with TaskScheduler.Default, 10 to 11
// of the 83 extension test cases failed in three runs). future.isSuccess() is IsCompletedSuccessfully. The varargs
// constructors take params arrays; Arrays.asList(checkNonEmpty(...)) is the array as an IList, and
// checkPositive/checkNonEmpty are the Common ObjectUtil checks (ArgumentException). ArrayDeque is Queue (poll() of an
// empty queue is null through TryDequeue, offer is Enqueue), Collections.emptyList() an empty array, and
// IllegalStateException is InvalidOperationException with the number in the invariant culture. msg !=
// LastHttpContent.EMPTY_LAST_CONTENT and msg != Unpooled.EMPTY_BUFFER are reference comparisons (ReferenceEquals);
// ctx.close() is CloseAsync() with its Task discarded, as Java discards the ChannelFuture.
public class WebSocketServerExtensionHandler : ChannelDuplexHandler
{
    private const int DEFAULT_MAX_PIPELINE_DEPTH = 128;
    private readonly int maxPipelineDepth;
    private readonly IList<IWebSocketServerExtensionHandshaker> extensionHandshakers;

    private readonly Queue<IList<IWebSocketServerExtension>> validExtensions =
            new Queue<IList<IWebSocketServerExtension>>(4);

    /**
     * Constructor
     *
     * @param extensionHandshakers
     *      The extension handshaker in priority order. A handshaker could be repeated many times
     *      with fallback configuration.
     */
    public WebSocketServerExtensionHandler(params IWebSocketServerExtensionHandshaker[] extensionHandshakers)
        : this(DEFAULT_MAX_PIPELINE_DEPTH, extensionHandshakers)
    {
    }

    /**
     * Constructor
     *
     * @param maxPipelineDepth
     *      The maximum number of pipelined upgrade requests.
     * @param extensionHandshakers
     *      The extension handshaker in priority order. A handshaker could be repeated many times
     *      with fallback configuration.
     */
    public WebSocketServerExtensionHandler(
        int maxPipelineDepth, params IWebSocketServerExtensionHandshaker[] extensionHandshakers)
    {
        this.maxPipelineDepth = ObjectUtil.CheckPositive(maxPipelineDepth, "maxPipelineDepth");
        this.extensionHandshakers = CheckNonEmpty(extensionHandshakers, "extensionHandshakers");
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        // JDK type checks vs non-implemented interfaces costs O(N), where
        // N is the number of interfaces already implemented by the concrete type that's being tested.
        // The only requirement for this call is to make HttpRequest(s) implementors to call onHttpRequestChannelRead
        // and super.channelRead the others, but due to the O(n) cost we perform few fast-path for commonly met
        // singleton and/or concrete types, to save performing such slow type checks.
        if (!ReferenceEquals(msg, ILastHttpContent.EMPTY_LAST_CONTENT))
        {
            if (msg is DefaultHttpRequest)
            {
                // fast-path
                OnHttpRequestChannelRead(ctx, (DefaultHttpRequest) msg);
            }
            else if (msg is IHttpRequest)
            {
                // slow path
                OnHttpRequestChannelRead(ctx, (IHttpRequest) msg);
            }
            else
            {
                base.ChannelRead(ctx, msg);
            }
        }
        else
        {
            base.ChannelRead(ctx, msg);
        }
    }

    /**
     * This is a method exposed to perform fail-fast checks of user-defined http types.<p>
     * eg:<br>
     * If the user has defined a specific {@link HttpRequest} type i.e.{@code CustomHttpRequest} and
     * {@link #channelRead} can receive {@link LastHttpContent#EMPTY_LAST_CONTENT} {@code msg}
     * types too, can override it like this:
     * <pre>
     *     public void channelRead(ChannelHandlerContext ctx, Object msg) throws Exception {
     *         if (msg != LastHttpContent.EMPTY_LAST_CONTENT) {
     *             if (msg instanceof CustomHttpRequest) {
     *                 onHttpRequestChannelRead(ctx, (CustomHttpRequest) msg);
     *             } else {
     *                 // if it's handling other HttpRequest types it MUST use onHttpRequestChannelRead again
     *                 // or have to delegate it to super.channelRead (that can perform redundant checks).
     *                 // If msg is not implementing HttpRequest, it can call ctx.fireChannelRead(msg) on it
     *                 // ...
     *                 super.channelRead(ctx, msg);
     *             }
     *         } else {
     *             // given that msg isn't a HttpRequest type we can just skip calling super.channelRead
     *             ctx.fireChannelRead(msg);
     *         }
     *     }
     * </pre>
     * <strong>IMPORTANT:</strong>
     * It already call {@code super.channelRead(ctx, request)} before returning.
     */
    protected virtual void OnHttpRequestChannelRead(IChannelHandlerContext ctx, IHttpRequest request)
    {
        if (maxPipelineDepth <= validExtensions.Count)
        {
            ReferenceCountUtil.Release(request);
            _ = ctx.CloseAsync();
            throw new InvalidOperationException(
                    "maxPipelineDepth exceeded: " + maxPipelineDepth.ToString(CultureInfo.InvariantCulture));
        }

        IList<IWebSocketServerExtension> validExtensionsList = null;

        if (WebSocketExtensionUtil.IsWebsocketUpgrade(request.Headers()))
        {
            string extensionsHeader = request.Headers().GetAsString(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS);

            if (extensionsHeader != null)
            {
                IList<WebSocketExtensionData> extensions =
                        WebSocketExtensionUtil.ExtractExtensions(extensionsHeader);
                int rsv = 0;

                foreach (WebSocketExtensionData extensionData in extensions)
                {
                    IEnumerator<IWebSocketServerExtensionHandshaker> extensionHandshakersIterator =
                            extensionHandshakers.GetEnumerator();
                    IWebSocketServerExtension validExtension = null;

                    while (validExtension == null && extensionHandshakersIterator.MoveNext())
                    {
                        IWebSocketServerExtensionHandshaker extensionHandshaker =
                                extensionHandshakersIterator.Current;
                        validExtension = extensionHandshaker.HandshakeExtension(extensionData);
                    }

                    if (validExtension != null && ((validExtension.Rsv() & rsv) == 0))
                    {
                        if (validExtensionsList == null)
                        {
                            validExtensionsList = new List<IWebSocketServerExtension>(1);
                        }
                        rsv = rsv | validExtension.Rsv();
                        validExtensionsList.Add(validExtension);
                    }
                }
            }
        }

        if (validExtensionsList == null)
        {
            validExtensionsList = Array.Empty<IWebSocketServerExtension>();
        }
        validExtensions.Enqueue(validExtensionsList);

        base.ChannelRead(ctx, request);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(msg, Unpooled.EmptyBuffer) && !(msg is ByteBuf))
        {
            if (msg is DefaultHttpResponse)
            {
                return OnHttpResponseWrite(ctx, (DefaultHttpResponse) msg, cancellationToken);
            }
            else if (msg is IHttpResponse)
            {
                return OnHttpResponseWrite(ctx, (IHttpResponse) msg, cancellationToken);
            }
            else
            {
                return base.WriteAsync(ctx, msg, cancellationToken);
            }
        }
        else
        {
            return base.WriteAsync(ctx, msg, cancellationToken);
        }
    }

    /**
     * This is a method exposed to perform fail-fast checks of user-defined http types.<p>
     * eg:<br>
     * If the user has defined a specific {@link HttpResponse} type i.e.{@code CustomHttpResponse} and
     * {@link #write} can receive {@link ByteBuf} {@code msg} types too, it can be overridden like this:
     * <pre>
     *     public void write(final ChannelHandlerContext ctx, Object msg, ChannelPromise promise) throws Exception {
     *         if (msg != Unpooled.EMPTY_BUFFER && !(msg instanceof ByteBuf)) {
     *             if (msg instanceof CustomHttpResponse) {
     *                 onHttpResponseWrite(ctx, (CustomHttpResponse) msg, promise);
     *             } else {
     *                 // if it's handling other HttpResponse types it MUST use onHttpResponseWrite again
     *                 // or have to delegate it to super.write (that can perform redundant checks).
     *                 // If msg is not implementing HttpResponse, it can call ctx.write(msg, promise) on it
     *                 // ...
     *                 super.write(ctx, msg, promise);
     *             }
     *         } else {
     *             // given that msg isn't a HttpResponse type we can just skip calling super.write
     *             ctx.write(msg, promise);
     *         }
     *     }
     * </pre>
     * <strong>IMPORTANT:</strong>
     * It already call {@code super.write(ctx, response, promise)} before returning.
     */
    protected virtual Task OnHttpResponseWrite(IChannelHandlerContext ctx, IHttpResponse response,
                                               CancellationToken cancellationToken)
    {
        validExtensions.TryDequeue(out IList<IWebSocketServerExtension> validExtensionsList);
        TaskCompletionSource promise = new TaskCompletionSource();
        // checking the status is faster than looking at headers so we do this first
        if (HttpResponseStatus.SWITCHING_PROTOCOLS.Equals(response.Status()))
        {
            HandlePotentialUpgrade(ctx, promise, response, validExtensionsList);
        }
        Task future = base.WriteAsync(ctx, response, cancellationToken);
        _ = future.ContinueWith(static (f, state) => ((TaskCompletionSource) state).TrySetFromTask(f), promise,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
        return promise.Task;
    }

    /**
     * Returns {@code true} if WebSocket extensions negotiated from the request should be acknowledged for this
     * response.
     * <p>
     * This method can be overridden to make the final extension negotiation decision when the handshake response is
     * written.
     */
    protected virtual bool IsExtensionNegotiationEnabled(IChannelHandlerContext ctx, IHttpResponse response)
    {
        return true;
    }

    private void HandlePotentialUpgrade(IChannelHandlerContext ctx,
                                        TaskCompletionSource promise, IHttpResponse httpResponse,
                                        IList<IWebSocketServerExtension> validExtensionsList)
    {
        HttpHeaders headers = httpResponse.Headers();

        if (WebSocketExtensionUtil.IsWebsocketUpgrade(headers))
        {
            if (IsExtensionNegotiationEnabled(ctx, httpResponse)
                    && validExtensionsList != null && validExtensionsList.Count != 0)
            {
                string headerValue = headers.GetAsString(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS);
                IList<WebSocketExtensionData> extraExtensions =
                  new List<WebSocketExtensionData>(extensionHandshakers.Count);
                foreach (IWebSocketServerExtension extension in validExtensionsList)
                {
                    extraExtensions.Add(extension.NewReponseData());
                }
                string newHeaderValue = WebSocketExtensionUtil
                  .ComputeMergeExtensionsHeaderValue(headerValue, extraExtensions);
                _ = promise.Task.ContinueWith(future =>
                {
                    if (future.IsCompletedSuccessfully)
                    {
                        foreach (IWebSocketServerExtension extension in validExtensionsList)
                        {
                            WebSocketExtensionDecoder decoder = extension.NewExtensionDecoder();
                            WebSocketExtensionEncoder encoder = extension.NewExtensionEncoder();
                            string name = ctx.Name;
                            ctx.Pipeline
                                .AddAfter(name, decoder.GetType().FullName, decoder)
                                .AddAfter(name, encoder.GetType().FullName, encoder);
                        }
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        new EventExecutorTaskScheduler(() => ctx.Executor));

                headers.Set(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS, newHeaderValue);
            }

            _ = promise.Task.ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    ctx.Pipeline.Remove(this);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ctx.Executor));
        }
    }
}
