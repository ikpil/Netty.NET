/*
 * Copyright 2019 The Netty Project
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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Enforce a limit on the maximum number of consecutive empty DATA frames (without end_of_stream flag) that are allowed
 * before the connection will be closed.
 */
// CLR: the package-private final class is internal sealed; the package-private constructor and frameListener0() are
// internal. The unwrapping reads the decorated listener through the listener field of Http2FrameListenerDecorator,
// which Java reaches through package access (protected internal in C#).
internal sealed class Http2EmptyDataFrameConnectionDecoder : DecoratingHttp2ConnectionDecoder
{

    private readonly int maxConsecutiveEmptyFrames;

    internal Http2EmptyDataFrameConnectionDecoder(IHttp2ConnectionDecoder @delegate, int maxConsecutiveEmptyFrames)
        : base(@delegate)
    {
        this.maxConsecutiveEmptyFrames = ObjectUtil.CheckPositive(
                maxConsecutiveEmptyFrames, "maxConsecutiveEmptyFrames");
    }

    public override void FrameListener(IHttp2FrameListener listener)
    {
        if (listener != null)
        {
            base.FrameListener(new Http2EmptyDataFrameListener(listener, maxConsecutiveEmptyFrames));
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
        if (frameListener is Http2EmptyDataFrameListener)
        {
            return ((Http2EmptyDataFrameListener) frameListener).listener;
        }
        return frameListener;
    }

    // Package-private for testing
    internal IHttp2FrameListener FrameListener0()
    {
        return base.FrameListener();
    }
}
