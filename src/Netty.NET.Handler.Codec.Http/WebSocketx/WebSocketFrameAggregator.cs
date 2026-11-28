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

using System;
using System.Diagnostics;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Handler that aggregate fragmented WebSocketFrame's.
 *
 * Be aware if PING/PONG/CLOSE frames are send in the middle of a fragmented {@link WebSocketFrame} they will
 * just get forwarded to the next handler in the pipeline.
 */
// CLR: MessageAggregator<WebSocketFrame, WebSocketFrame, ContinuationWebSocketFrame, WebSocketFrame> is the
// codec-base generic of the same shape, and super(maxContentLength, WebSocketFrame.class) is
// base(maxContentLength, typeof(WebSocketFrame)); ChannelPipeline is IChannelPipeline. The "should not reach here"
// throw new Error(...) is UnreachableException, as AssertionError is in the handler port; StringUtil.className is
// the Common StringUtil.ClassName, UnsupportedOperationException is NotSupportedException, and "throws Exception"
// has no C# counterpart.
public class WebSocketFrameAggregator
        : MessageAggregator<WebSocketFrame, WebSocketFrame, ContinuationWebSocketFrame, WebSocketFrame>
{

    /**
     * Creates a new instance
     *
     * @param maxContentLength If the size of the aggregated frame exceeds this value,
     *                         a {@link TooLongFrameException} is thrown.
     */
    public WebSocketFrameAggregator(int maxContentLength) : base(maxContentLength, typeof(WebSocketFrame))
    {
    }

    protected override bool IsStartMessage(WebSocketFrame msg)
    {
        return msg is TextWebSocketFrame || msg is BinaryWebSocketFrame;
    }

    protected override bool IsContentMessage(WebSocketFrame msg)
    {
        return msg is ContinuationWebSocketFrame;
    }

    protected override bool IsLastContentMessage(ContinuationWebSocketFrame msg)
    {
        return IsContentMessage(msg) && msg.IsFinalFragment();
    }

    protected override bool IsAggregated(WebSocketFrame msg)
    {
        if (msg.IsFinalFragment())
        {
            return !IsContentMessage(msg);
        }

        return !IsStartMessage(msg) && !IsContentMessage(msg);
    }

    protected override bool IsContentLengthInvalid(WebSocketFrame start, int maxContentLength)
    {
        return false;
    }

    protected override object NewContinueResponse(WebSocketFrame start, int maxContentLength, IChannelPipeline pipeline)
    {
        return null;
    }

    protected override bool CloseAfterContinueResponse(object msg)
    {
        throw new NotSupportedException();
    }

    protected override bool IgnoreContentAfterContinueResponse(object msg)
    {
        throw new NotSupportedException();
    }

    protected override WebSocketFrame BeginAggregation(WebSocketFrame start, ByteBuf content)
    {
        if (start is TextWebSocketFrame)
        {
            return new TextWebSocketFrame(true, start.Rsv(), content);
        }

        if (start is BinaryWebSocketFrame)
        {
            return new BinaryWebSocketFrame(true, start.Rsv(), content);
        }

        // Should not reach here.
        throw new UnreachableException("Unexpected websocket frame type: " + StringUtil.ClassName(start));
    }
}
