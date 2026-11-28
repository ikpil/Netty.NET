/*
 * Copyright 2025 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * {@link DecoratingHttp2ConnectionEncoder} which guards against a remote peer that will trigger a massive amount
 * of RST frames on an existing connection.
 * This encoder will tear-down the connection once we reached the configured limit to reduce the risk of DDOS.
 */
// CLR: ChannelPromise is TaskCompletionSource and ChannelFuture Task; writeRstStream is the WriteRstStreamAsync
// override of DecoratingHttp2FrameWriter. TimeUnit.SECONDS.toNanos(seconds) is seconds * 10^9 (an int number of
// seconds cannot overflow a long) and TimeUnit.NANOSECONDS.toSeconds(nanos) is the truncating division by 10^9:
// "Conversions from finer to coarser granularities truncate"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/TimeUnit.html). Ticker is the
// Common Ticker; ctx.close() discards the returned Task.
internal sealed class Http2MaxRstFrameLimitEncoder : DecoratingHttp2ConnectionEncoder
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Http2MaxRstFrameLimitEncoder));

    private readonly long nanosPerWindow;
    private readonly int maxRstFramesPerWindow;
    private readonly Ticker ticker;
    private long lastRstFrameNano;
    private int sendRstInWindow;
    private IHttp2LifecycleManager lifecycleManager;

    internal Http2MaxRstFrameLimitEncoder(IHttp2ConnectionEncoder @delegate, int maxRstFramesPerWindow, int secondsPerWindow)
        : this(@delegate, maxRstFramesPerWindow, secondsPerWindow, Ticker.SystemTicker())
    {
    }

    internal Http2MaxRstFrameLimitEncoder(IHttp2ConnectionEncoder @delegate, int maxRstFramesPerWindow, int secondsPerWindow,
                                 Ticker ticker) : base(@delegate)
    {
        this.maxRstFramesPerWindow = maxRstFramesPerWindow;
        this.nanosPerWindow = secondsPerWindow * 1000000000L;
        this.ticker = ticker;
        lastRstFrameNano = ticker.NanoTime();
    }

    public override void LifecycleManager(IHttp2LifecycleManager lifecycleManager)
    {
        this.lifecycleManager = lifecycleManager;
        base.LifecycleManager(lifecycleManager);
    }

    public override Task WriteRstStreamAsync(IChannelHandlerContext ctx, int streamId, long errorCode,
                                        TaskCompletionSource promise)
    {
        Task future = base.WriteRstStreamAsync(ctx, streamId, errorCode, promise);

        if (CountRstFrameErrorCode(errorCode))
        {
            long currentNano = ticker.NanoTime();
            if (currentNano - lastRstFrameNano >= nanosPerWindow)
            {
                lastRstFrameNano = currentNano;
                sendRstInWindow = 1;
            }
            else
            {
                sendRstInWindow++;

                if (sendRstInWindow > maxRstFramesPerWindow)
                {
                    Http2Exception exception = Http2Exception.ConnectionError(Http2Error.ENHANCE_YOUR_CALM,
                            "Maximum number %d of RST frames frames reached within %d seconds", maxRstFramesPerWindow,
                            nanosPerWindow / 1000000000L);

                    logger.Debug("{} Maximum number {} of RST frames reached within {} seconds, " +
                                    "closing connection with {} error", ctx.Channel, maxRstFramesPerWindow,
                            nanosPerWindow / 1000000000L, exception.Error(),
                            exception);
                    // First notify the Http2LifecycleManager and then close the connection.
                    lifecycleManager.OnError(ctx, true, exception);
                    _ = ctx.CloseAsync();
                }
            }
        }

        return future;
    }

    private bool CountRstFrameErrorCode(long errorCode)
    {
        // Don't count CANCEL and NO_ERROR as these might be ok.
        return errorCode != Http2Error.CANCEL.Code() && errorCode != Http2Error.NO_ERROR.Code();
    }
}
