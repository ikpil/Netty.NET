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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * A {@link ChannelHandler} that aggregates an {@link StompHeadersSubframe}
 * and its following {@link StompContentSubframe}s into a single {@link StompFrame}.
 * It is useful when you don't want to take care of STOMP frames whose content is 'chunked'.  Insert this
 * handler after {@link StompSubframeDecoder} in the {@link ChannelPipeline}:
 */
// CLR: StompSubframe.class is typeof(IStompSubframe), and UnsupportedOperationException is NotSupportedException.
public class StompSubframeAggregator
        : MessageAggregator<IStompSubframe, IStompHeadersSubframe, IStompContentSubframe, IStompFrame>
{

    /**
     * Creates a new instance.
     *
     * @param maxContentLength
     *        the maximum length of the aggregated content.
     *        If the length of the aggregated content exceeds this value,
     *        a {@link TooLongFrameException} will be raised.
     */
    public StompSubframeAggregator(int maxContentLength) : base(maxContentLength, typeof(IStompSubframe))
    {
    }

    protected override bool IsStartMessage(IStompSubframe msg)
    {
        return msg is IStompHeadersSubframe;
    }

    protected override bool IsContentMessage(IStompSubframe msg)
    {
        return msg is IStompContentSubframe;
    }

    protected override bool IsLastContentMessage(IStompContentSubframe msg)
    {
        return msg is ILastStompContentSubframe;
    }

    protected override bool IsAggregated(IStompSubframe msg)
    {
        return msg is IStompFrame;
    }

    protected override bool IsContentLengthInvalid(IStompHeadersSubframe start, int maxContentLength)
    {
        return (int) Math.Min(int.MaxValue, start.Headers().GetLong(IStompHeaders.CONTENT_LENGTH, -1)) >
                     maxContentLength;
    }

    protected override object NewContinueResponse(IStompHeadersSubframe start, int maxContentLength, IChannelPipeline pipeline)
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

    protected override IStompFrame BeginAggregation(IStompHeadersSubframe start, ByteBuf content)
    {
        IStompFrame ret = new DefaultStompFrame(start.Command(), content);
        ret.Headers().Set(start.Headers());
        return ret;
    }
}
