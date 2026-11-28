/*
 * Copyright 2023 The Netty Project
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

using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;


// CLR: TimeUnit.SECONDS.toNanos(seconds) is seconds * 10^9 (an int number of seconds cannot overflow a long, where
// Java would saturate) and TimeUnit.NANOSECONDS.toSeconds(nanos) is the truncating division by 10^9: "Conversions
// from finer to coarser granularities truncate"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/TimeUnit.html). Ticker is the
// Common Ticker. Class<?> is Type.
internal sealed class Http2MaxRstFrameListener : Http2FrameListenerDecorator
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Http2MaxRstFrameListener));
    private static readonly Http2Exception RST_FRAME_RATE_EXCEEDED = Http2Exception.NewStatic(Http2Error.ENHANCE_YOUR_CALM,
            "Maximum number of RST frames reached",
            Http2Exception.ShutdownHint.HARD_SHUTDOWN, typeof(Http2MaxRstFrameListener), "onRstStreamRead(..)");

    private readonly long nanosPerWindow;
    private readonly int maxRstFramesPerWindow;
    private readonly Ticker ticker;
    private long lastRstFrameNano;
    private int receivedRstInWindow;

    internal Http2MaxRstFrameListener(IHttp2FrameListener listener, int maxRstFramesPerWindow, int secondsPerWindow,
                             Ticker ticker) : base(listener)
    {
        this.maxRstFramesPerWindow = maxRstFramesPerWindow;
        this.nanosPerWindow = secondsPerWindow * 1000000000L;
        this.ticker = ticker;
        this.lastRstFrameNano = ticker.NanoTime() - nanosPerWindow;
    }

    public override void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
    {
        long currentNano = ticker.NanoTime();
        if (currentNano - lastRstFrameNano >= nanosPerWindow)
        {
            lastRstFrameNano = currentNano;
            receivedRstInWindow = 1;
        }
        else
        {
            receivedRstInWindow++;

            if (receivedRstInWindow > maxRstFramesPerWindow)
            {
                logger.Debug("{} Maximum number {} of RST frames reached within {} seconds, " +
                                "closing connection with {} error", ctx.Channel, maxRstFramesPerWindow,
                        nanosPerWindow / 1000000000L, RST_FRAME_RATE_EXCEEDED.Error(),
                        RST_FRAME_RATE_EXCEEDED);
                throw RST_FRAME_RATE_EXCEEDED;
            }
        }

        base.OnRstStreamRead(ctx, streamId, errorCode);
    }
}
