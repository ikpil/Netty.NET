/*
 * Copyright 2022 The Netty Project
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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl.Ocsp;

// CLR: Promise<OCSPResp> is a TaskCompletionSource<OcspResp> (OcspResp is the CLR-only replacement of Bouncy Castle's
// OCSPResp, see OcspAsn1.cs) and OCSPException is CryptographicException. The listener that cancels the timeout runs
// synchronously when the promise completes (ExecuteSynchronously), as DefaultPromise notifies it at once on the event
// loop that completes it; Future<?> timeoutFuture is the CancellationTokenSource whose token was passed to
// ctx.Executor.ScheduleAsync (cancel(true) is Cancel()), as handler's AbstractSniHandler keeps its timeout.
// ChannelPromise is the returned Task of WriteAsync (docs/transport-porting.md). FullHttpResponse is IFullHttpResponse,
// response.content() is its Content and status() == OK is a reference comparison, as Java compares the constant.
// equalsIgnoreCase is an ordinal case-insensitive comparison. ClosedChannelException is Transport's
// ClosedChannelException, the CLR counterpart of java.nio.channels.ClosedChannelException.
internal sealed class OcspHttpHandler : ChannelDuplexHandler
{
    private static readonly IInternalLogger LOGGER = InternalLoggerFactory.GetInstance(typeof(OcspHttpHandler));
    private readonly TaskCompletionSource<OcspResp> responseFuture;
    private readonly long timeoutMillis;
    private CancellationTokenSource timeoutFuture;
    internal const string OCSP_REQUEST_TYPE = "application/ocsp-request";
    internal const string OCSP_RESPONSE_TYPE = "application/ocsp-response";

    /**
     * Create new {@link OcspHttpHandler} instance
     *
     * @param responsePromise   {@link Promise} of {@link OCSPResp}
     * @param timeoutMillis     the timeout in milliseconds how long a response can take to before we fail the promise.
     */
    internal OcspHttpHandler(TaskCompletionSource<OcspResp> responsePromise, long timeoutMillis)
    {
        this.responseFuture = ObjectUtil.CheckNotNull(responsePromise, "ResponsePromise");
        this.timeoutMillis = ObjectUtil.CheckPositive(timeoutMillis, "timeoutMillis");
        _ = this.responseFuture.Task.ContinueWith(_ =>
        {
            if (timeoutFuture != null)
            {
                timeoutFuture.Cancel();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        IFullHttpResponse response = (IFullHttpResponse) msg;

        try
        {
            // If DEBUG is enabled then log the response
            if (LOGGER.IsDebugEnabled())
            {
                LOGGER.Debug("Received OCSP HTTP Response: {}", response);
            }

            // Response headers must contain 'Content-Type'
            string contentType = response.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
            if (contentType == null)
            {
                throw new CryptographicException("HTTP Response does not contain 'CONTENT-TYPE' header");
            }

            // Response headers must contain 'application/ocsp-response'
            if (!contentType.Equals(OCSP_RESPONSE_TYPE, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("Response Content-Type was: " + contentType +
                        "; Expected: " + OCSP_RESPONSE_TYPE);
            }

            // Status must be OK for successful lookup
            if (response.Status() != HttpResponseStatus.OK)
            {
                throw new ArgumentException("HTTP Response Code was: " + response.Status().Code() +
                        "; Expected: 200");
            }

            responseFuture.TrySetResult(new OcspResp(ByteBufUtil.GetBytes(response.Content)));
        }
        finally
        {
            response.Release();
            _ = ctx.CloseAsync();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        responseFuture.TrySetException(cause);
        _ = ctx.CloseAsync();
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        Task promise = base.WriteAsync(ctx, msg, cancellationToken);
        timeoutFuture = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (!responseFuture.Task.IsCompleted)
            {
                responseFuture.TrySetException(new CryptographicException("OCSP response was not received within "
                        + timeoutMillis + "ms"));
                _ = ctx.CloseAsync();
            }
        }, TimeSpan.FromMilliseconds(timeoutMillis), timeoutFuture.Token);
        return promise;
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (!responseFuture.Task.IsCompleted)
        {
            responseFuture.TrySetException(new ClosedChannelException());
        }

        base.ChannelInactive(ctx);
    }
}
