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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Enforce a limit on the maximum number of consecutive empty DATA frames (without end_of_stream flag) that are allowed
 * before the connection will be closed.
 */
// CLR: the package-private final class is internal sealed and its constructor internal; the overridden listener
// methods override the virtual Http2FrameListenerDecorator members.
internal sealed class Http2EmptyDataFrameListener : Http2FrameListenerDecorator
{
    private readonly int maxConsecutiveEmptyFrames;

    private bool violationDetected;
    private int emptyDataFrames;

    internal Http2EmptyDataFrameListener(IHttp2FrameListener listener, int maxConsecutiveEmptyFrames) : base(listener)
    {
        this.maxConsecutiveEmptyFrames = ObjectUtil.CheckPositive(
                maxConsecutiveEmptyFrames, "maxConsecutiveEmptyFrames");
    }

    public override int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream)
    {
        if (endOfStream || data.IsReadable())
        {
            emptyDataFrames = 0;
        }
        else if (emptyDataFrames++ == maxConsecutiveEmptyFrames && !violationDetected)
        {
            violationDetected = true;
            throw Http2Exception.ConnectionError(Http2Error.ENHANCE_YOUR_CALM,
                    "Maximum number %d of empty data frames without end_of_stream flag received",
                    maxConsecutiveEmptyFrames);
        }

        return base.OnDataRead(ctx, streamId, data, padding, endOfStream);
    }

    public override void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                              int padding, bool endStream)
    {
        emptyDataFrames = 0;
        base.OnHeadersRead(ctx, streamId, headers, padding, endStream);
    }

    public override void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                              short weight, bool exclusive, int padding, bool endStream)
    {
        emptyDataFrames = 0;
        base.OnHeadersRead(ctx, streamId, headers, streamDependency, weight, exclusive, padding, endStream);
    }
}
