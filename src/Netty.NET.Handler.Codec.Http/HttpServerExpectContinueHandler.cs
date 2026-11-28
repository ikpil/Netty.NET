/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Sends a <a href="https://www.w3.org/Protocols/rfc2616/rfc2616-sec8.html#sec8.2.3">100 CONTINUE</a>
 * {@link HttpResponse} to {@link HttpRequest}s which contain a 'expect: 100-continue' header. It
 * should only be used for applications which do <b>not</b> install the {@link HttpObjectAggregator}.
 * <p>
 * By default it accepts all expectations.
 * <p>
 * Since {@link HttpServerExpectContinueHandler} expects {@link HttpRequest}s it should be added after {@link
 * HttpServerCodec} but before any other handlers that might send a {@link HttpResponse}. <blockquote>
 * <pre>
 *  {@link io.netty.channel.ChannelPipeline} p = ...;
 *  ...
 *  p.addLast("serverCodec", new {@link HttpServerCodec}());
 *  p.addLast("respondExpectContinue", <b>new {@link HttpServerExpectContinueHandler}()</b>);
 *  ...
 *  p.addLast("handler", new HttpRequestHandler());
 *  </pre>
 * </blockquote>
 */
// CLR: the static initializer is the static constructor. ChannelFutureListener.CLOSE_ON_FAILURE is a Task continuation
// that closes the channel when the write did not succeed (src/Netty.NET.Transport/Bootstrapping/AbstractBootstrap.cs
// records the same mapping); it runs on the channel's executor (Common's EventExecutorTaskScheduler over ctx.Executor;
// docs/handler-porting.md, Listener timing), at once when the write completes on the event loop, as a Java listener
// does. The protected Java methods are protected virtual.
// FullHttpResponse.retainedDuplicate() narrows its return type only in Java; the C# interface inherits
// IByteBufHolder.RetainedDuplicate() (see IHttpContent), so the result is cast to IHttpResponse.
public class HttpServerExpectContinueHandler : ChannelInboundHandlerAdapter
{

    private static readonly IFullHttpResponse EXPECTATION_FAILED = new DefaultFullHttpResponse(
            HTTP_1_1, HttpResponseStatus.EXPECTATION_FAILED, Unpooled.EmptyBuffer);

    private static readonly IFullHttpResponse ACCEPT = new DefaultFullHttpResponse(
            HTTP_1_1, CONTINUE, Unpooled.EmptyBuffer);

    static HttpServerExpectContinueHandler()
    {
        EXPECTATION_FAILED.Headers().Set(CONTENT_LENGTH, 0);
        ACCEPT.Headers().Set(CONTENT_LENGTH, 0);
    }

    /**
     * Produces a {@link HttpResponse} for {@link HttpRequest}s which define an expectation. Returns {@code null} if the
     * request should be rejected. See {@link #rejectResponse(HttpRequest)}.
     */
    protected virtual IHttpResponse AcceptMessage(IHttpRequest request)
    {
        return (IHttpResponse) ACCEPT.RetainedDuplicate();
    }

    /**
     * Returns the appropriate 4XX {@link HttpResponse} for the given {@link HttpRequest}.
     */
    protected virtual IHttpResponse RejectResponse(IHttpRequest request)
    {
        return (IHttpResponse) EXPECTATION_FAILED.RetainedDuplicate();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttpRequest)
        {
            IHttpRequest req = (IHttpRequest) msg;

            if (HttpUtil.Is100ContinueExpected(req))
            {
                IHttpResponse accept = AcceptMessage(req);

                if (accept == null)
                {
                    // the expectation failed so we refuse the request.
                    IHttpResponse rejection = RejectResponse(req);
                    ReferenceCountUtil.Release(msg);
                    CloseOnFailure(ctx, ctx.WriteAndFlushAsync(rejection));
                    return;
                }

                CloseOnFailure(ctx, ctx.WriteAndFlushAsync(accept));
                req.Headers().Remove(HttpHeaderNames.EXPECT);
            }
        }

        base.ChannelRead(ctx, msg);
    }

    // CLR: ChannelFutureListener.CLOSE_ON_FAILURE.
    private static void CloseOnFailure(IChannelHandlerContext ctx, Task future)
    {
        IChannel channel = ctx.Channel;
        _ = future.ContinueWith(f =>
        {
            if (!f.IsCompletedSuccessfully)
            {
                _ = channel.CloseAsync();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
    }
}

