/*
 * Copyright 2016 The Netty Project
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

using System;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * A {@link ChannelHandler} that aggregates an {@link BulkStringHeaderRedisMessage}
 * and its following {@link BulkStringRedisContent}s into a single {@link FullBulkStringRedisMessage}
 * with no following {@link BulkStringRedisContent}s.  It is useful when you don't want to take
 * care of {@link RedisMessage}s whose transfer encoding is 'chunked'.  Insert this
 * handler after {@link RedisDecoder} in the {@link ChannelPipeline}:
 * <pre>
 * {@link ChannelPipeline} p = ...;
 * ...
 * p.addLast("encoder", new {@link RedisEncoder}());
 * p.addLast("decoder", new {@link RedisDecoder}());
 * p.addLast("aggregator", <b>new {@link RedisBulkStringAggregator}()</b>);
 * ...
 * p.addLast("handler", new HttpRequestHandler());
 * </pre>
 * Be aware that you need to have the {@link RedisEncoder} before the {@link RedisBulkStringAggregator}
 * in the {@link ChannelPipeline}.
 */
// CLR: RedisMessage.class is typeof(IRedisMessage), and UnsupportedOperationException is NotSupportedException.
[UnstableApi]
public sealed class RedisBulkStringAggregator : MessageAggregator<IRedisMessage, BulkStringHeaderRedisMessage,
                                                                 IBulkStringRedisContent, FullBulkStringRedisMessage>
{

    /**
     * Creates a new instance.
     */
    public RedisBulkStringAggregator() : base(RedisConstants.REDIS_MESSAGE_MAX_LENGTH, typeof(IRedisMessage))
    {
    }

    protected override bool IsStartMessage(IRedisMessage msg)
    {
        return msg is BulkStringHeaderRedisMessage && !IsAggregated(msg);
    }

    protected override bool IsContentMessage(IRedisMessage msg)
    {
        return msg is IBulkStringRedisContent;
    }

    protected override bool IsLastContentMessage(IBulkStringRedisContent msg)
    {
        return msg is ILastBulkStringRedisContent;
    }

    protected override bool IsAggregated(IRedisMessage msg)
    {
        return msg is FullBulkStringRedisMessage;
    }

    protected override bool IsContentLengthInvalid(BulkStringHeaderRedisMessage start, int maxContentLength)
    {
        return start.BulkStringLength() > maxContentLength;
    }

    protected override object NewContinueResponse(BulkStringHeaderRedisMessage start, int maxContentLength,
                                         IChannelPipeline pipeline)
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

    protected override FullBulkStringRedisMessage BeginAggregation(BulkStringHeaderRedisMessage start, ByteBuf content)
    {
        return new FullBulkStringRedisMessage(content);
    }
}
