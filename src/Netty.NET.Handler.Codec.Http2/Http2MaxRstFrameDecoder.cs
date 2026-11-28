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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;


/**
 * Enforce a limit on the maximum number of RST frames that are allowed per a window
 * before the connection will be closed with a GO_AWAY frame.
 */
// CLR: The unwrapping reads the decorated listener through the listener field of Http2FrameListenerDecorator,
// which Java reaches through package access (protected internal in C#).
internal sealed class Http2MaxRstFrameDecoder : DecoratingHttp2ConnectionDecoder
{
    private readonly int maxRstFramesPerWindow;
    private readonly int secondsPerWindow;

    internal Http2MaxRstFrameDecoder(IHttp2ConnectionDecoder @delegate, int maxRstFramesPerWindow, int secondsPerWindow)
        : base(@delegate)
    {
        this.maxRstFramesPerWindow = CheckPositive(maxRstFramesPerWindow, "maxRstFramesPerWindow");
        this.secondsPerWindow = CheckPositive(secondsPerWindow, "secondsPerWindow");
    }

    public override void FrameListener(IHttp2FrameListener listener)
    {
        if (listener != null)
        {
            base.FrameListener(new Http2MaxRstFrameListener(
                    listener, maxRstFramesPerWindow, secondsPerWindow, Ticker.SystemTicker()));
        }
        else
        {
            base.FrameListener(null);
        }
    }

    public override IHttp2FrameListener FrameListener()
    {
        IHttp2FrameListener frameListener = FrameListener0();
        // Unwrap the original Http2FrameListener as we add this decoder under the hood.
        if (frameListener is Http2MaxRstFrameListener)
        {
            return ((Http2MaxRstFrameListener) frameListener).listener;
        }

        return frameListener;
    }

    // Package-private for testing
    internal IHttp2FrameListener FrameListener0()
    {
        return base.FrameListener();
    }
}
