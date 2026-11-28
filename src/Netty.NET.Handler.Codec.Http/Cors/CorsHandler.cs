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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Handler.Codec.Http.Cors;

/**
 * Handles <a href="https://www.w3.org/TR/cors/">Cross Origin Resource Sharing</a> (CORS) requests.
 * <p>
 * This handler can be configured using one or more {@link CorsConfig}, please
 * refer to this class for details about the configuration options available.
 * <p>
 * If CORS support is enabled for at least one {@link CorsConfig}, every response written
 * by this handler will carry {@code Vary: Origin}, including responses to requests without
 * {@code Origin} header or with an origin that is not allowed.
 */
// CLR: write(ctx, msg, promise) is WriteAsync, whose Task replaces the promise (docs/codec-base-porting.md, Outbound
// operations). ChannelFutureListener.CLOSE added to the writeAndFlush future is a Task continuation that closes the
// channel; it runs on the channel's executor (Common's EventExecutorTaskScheduler over ctx.Executor), at once when the
// write completes on the event loop, as a Java listener does (the core HttpServerKeepAliveHandler and
// HttpServerExpectContinueHandler use the same scheduler, docs/handler-porting.md, Listener timing). List<CorsConfig>
// is IList<CorsConfig> and Collections.singletonList is a one-element ImmutableList (as the core QueryStringDecoder).
// HttpRequest/HttpResponse/HttpContent are IHttpRequest/IHttpResponse/IHttpContent. String.equals is the ordinal
// string.Equals; a String passed as a CharSequence header value binds to the HttpHeaders Set/Add(ICharSequence, object)
// overloads, and the Set<T> results of CorsConfig bind to the Iterable overloads (IEnumerable<object>). ctx.alloc() is
// ctx.Alloc().
public class CorsHandler : ChannelDuplexHandler
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(CorsHandler));
    private const string ANY_ORIGIN = "*";
    private const string NULL_ORIGIN = "null";
    private CorsConfig config;

    private IHttpRequest request;
    private readonly IList<CorsConfig> configList;
    private readonly bool isShortCircuit;
    private readonly bool varyOnOrigin;
    private bool consumeContent;

    /**
     * Creates a new instance with a single {@link CorsConfig}.
     */
    public CorsHandler(CorsConfig config)
        : this(ImmutableList.Create(CheckNotNull(config, "config")), config.IsShortCircuit())
    {
    }

    /**
     * Creates a new instance with the specified config list. If more than one
     * config matches a certain origin, the first in the List will be used.
     *
     * @param configList     List of {@link CorsConfig}
     * @param isShortCircuit Same as {@link CorsConfig#isShortCircuit} but applicable to all supplied configs.
     */
    public CorsHandler(IList<CorsConfig> configList, bool isShortCircuit)
    {
        CheckNonEmpty(configList, "configList");

        this.configList = configList;
        this.isShortCircuit = isShortCircuit;
        this.varyOnOrigin = IsCorsSupportEnabled();
    }

    private bool IsCorsSupportEnabled()
    {
        foreach (CorsConfig config in configList)
        {
            if (config.IsCorsSupportEnabled())
            {
                return true;
            }
        }

        return false;
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttpRequest)
        {
            request = (IHttpRequest) msg;
            string origin = request.Headers().Get(HttpHeaderNames.ORIGIN);
            config = GetForOrigin(origin);

            if (IsPreflightRequest(request))
            {
                HandlePreflight(ctx, request);
                // Enable consumeContent so that all following HttpContent
                // for this request will be released and not propagated downstream.
                consumeContent = true;
                return;
            }

            if (isShortCircuit && !(origin == null || config != null))
            {
                Forbidden(ctx, request);
                consumeContent = true;
                return;
            }

            // This request is forwarded, stop discarding
            consumeContent = false;
            ctx.FireChannelRead(msg);
            return;
        }

        if (consumeContent && (msg is IHttpContent))
        {
            ReferenceCountUtil.Release(msg);
            return;
        }

        ctx.FireChannelRead(msg);
    }

    private void HandlePreflight(IChannelHandlerContext ctx, IHttpRequest request)
    {
        IHttpResponse response = new DefaultFullHttpResponse(
                request.ProtocolVersion(),
                OK,
                Unpooled.Buffer(0),
                DefaultHttpHeadersFactory.HeadersFactory().WithCombiningHeaders(true),
                DefaultHttpHeadersFactory.TrailersFactory().WithCombiningHeaders(true));
        if (SetOrigin(response))
        {
            SetAllowMethods(response);
            SetAllowHeaders(response);
            SetAllowCredentials(response);
            SetMaxAge(response);
            SetPreflightHeaders(response);
            SetAllowPrivateNetwork(response);
        }

        if (!response.Headers().Contains(HttpHeaderNames.CONTENT_LENGTH))
        {
            response.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, HttpHeaderValues.ZERO);
        }

        ReferenceCountUtil.Release(request);
        Respond(ctx, request, response);
    }

    /**
     * This is a non CORS specification feature which enables the setting of preflight
     * response headers that might be required by intermediaries.
     *
     * @param response the HttpResponse to which the preflight response headers should be added.
     */
    private void SetPreflightHeaders(IHttpResponse response)
    {
        response.Headers().Add(config.PreflightResponseHeaders());
    }

    // CLR: Set.contains(null) is false for the Java LinkedHashSet; the Common LinkedHashSet looks the item up in a
    // Dictionary, whose ContainsKey throws ArgumentNullException for a null key
    // (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.containskey?view=net-10.0),
    // so a request without Origin skips the lookup.
    private CorsConfig GetForOrigin(string requestOrigin)
    {
        foreach (CorsConfig corsConfig in configList)
        {
            if (corsConfig.IsAnyOriginSupported())
            {
                return corsConfig;
            }
            if (requestOrigin != null && corsConfig.Origins().Contains(requestOrigin))
            {
                return corsConfig;
            }
            if (corsConfig.IsNullOriginAllowed() && NULL_ORIGIN.Equals(requestOrigin))
            {
                return corsConfig;
            }
        }

        return null;
    }

    private bool SetOrigin(IHttpResponse response)
    {
        string origin = request.Headers().Get(HttpHeaderNames.ORIGIN);
        if (origin != null && config != null)
        {
            if (NULL_ORIGIN.Equals(origin) && config.IsNullOriginAllowed())
            {
                SetNullOrigin(response);
                SetVaryHeader(response);
                return true;
            }

            if (config.IsAnyOriginSupported())
            {
                if (config.IsCredentialsAllowed())
                {
                    EchoRequestOrigin(response);
                    SetVaryHeader(response);
                }
                else
                {
                    SetAnyOrigin(response);
                }

                return true;
            }

            if (config.Origins().Contains(origin))
            {
                SetOrigin(response, origin);
                SetVaryHeader(response);
                return true;
            }

            logger.Debug("Request origin [{}]] was not among the configured origins [{}]", origin, config.Origins());
        }

        return false;
    }

    private void EchoRequestOrigin(IHttpResponse response)
    {
        SetOrigin(response, request.Headers().Get(HttpHeaderNames.ORIGIN));
    }

    private static void SetVaryHeader(IHttpResponse response)
    {
        if (!response.Headers().ContainsValue(HttpHeaderNames.VARY, HttpHeaderNames.ORIGIN, true))
        {
            response.Headers().Add(HttpHeaderNames.VARY, HttpHeaderNames.ORIGIN);
        }
    }

    private static void SetAnyOrigin(IHttpResponse response)
    {
        SetOrigin(response, ANY_ORIGIN);
    }

    private static void SetNullOrigin(IHttpResponse response)
    {
        SetOrigin(response, NULL_ORIGIN);
    }

    private static void SetOrigin(IHttpResponse response, string origin)
    {
        response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_ALLOW_ORIGIN, origin);
    }

    private void SetAllowCredentials(IHttpResponse response)
    {
        if (config.IsCredentialsAllowed()
                && !response.Headers().Get(HttpHeaderNames.ACCESS_CONTROL_ALLOW_ORIGIN).Equals(ANY_ORIGIN))
        {
            response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_ALLOW_CREDENTIALS, "true");
        }
    }

    private static bool IsPreflightRequest(IHttpRequest request)
    {
        HttpHeaders headers = request.Headers();
        return OPTIONS.Equals(request.Method()) &&
                headers.Contains(HttpHeaderNames.ORIGIN) &&
                headers.Contains(HttpHeaderNames.ACCESS_CONTROL_REQUEST_METHOD);
    }

    private void SetExposeHeaders(IHttpResponse response)
    {
        if (config.ExposedHeaders().Count != 0)
        {
            response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_EXPOSE_HEADERS, config.ExposedHeaders());
        }
    }

    private void SetAllowMethods(IHttpResponse response)
    {
        response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_ALLOW_METHODS, config.AllowedRequestMethods());
    }

    private void SetAllowHeaders(IHttpResponse response)
    {
        response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_ALLOW_HEADERS, config.AllowedRequestHeaders());
    }

    private void SetMaxAge(IHttpResponse response)
    {
        response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_MAX_AGE, config.MaxAge());
    }

    private void SetAllowPrivateNetwork(IHttpResponse response)
    {
        if (request.Headers().Contains(HttpHeaderNames.ACCESS_CONTROL_REQUEST_PRIVATE_NETWORK))
        {
            if (config.IsPrivateNetworkAllowed())
            {
                response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_ALLOW_PRIVATE_NETWORK, "true");
            }
            else
            {
                response.Headers().Set(HttpHeaderNames.ACCESS_CONTROL_ALLOW_PRIVATE_NETWORK, "false");
            }
        }
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) msg;
            if (config != null && config.IsCorsSupportEnabled() && SetOrigin(response))
            {
                SetAllowCredentials(response);
                SetExposeHeaders(response);
            }

            if (varyOnOrigin)
            {
                SetVaryHeader(response);
            }
        }

        return ctx.WriteAsync(msg, cancellationToken);
    }

    private static void Forbidden(IChannelHandlerContext ctx, IHttpRequest request)
    {
        IHttpResponse response = new DefaultFullHttpResponse(
                request.ProtocolVersion(), FORBIDDEN, ctx.Alloc().Buffer(0));
        response.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, HttpHeaderValues.ZERO);
        ReferenceCountUtil.Release(request);
        Respond(ctx, request, response);
    }

    private static void Respond(
            IChannelHandlerContext ctx,
            IHttpRequest request,
            IHttpResponse response)
    {

        bool keepAlive = HttpUtil.IsKeepAlive(request);

        HttpUtil.SetKeepAlive(response, keepAlive);

        Task future = ctx.WriteAndFlushAsync(response);

        if (!keepAlive)
        {
            IChannel channel = ctx.Channel;
            // CLR: the scheduler's lambda captures this branch's copy of ctx, so a keep-alive response allocates no
            // closure (see EventExecutorTaskScheduler).
            IChannelHandlerContext listenerCtx = ctx;
            _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => listenerCtx.Executor));
        }
    }
}
