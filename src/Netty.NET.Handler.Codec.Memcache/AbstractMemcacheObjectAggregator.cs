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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * A {@link ChannelHandler} that aggregates an {@link MemcacheMessage}
 * and its following {@link MemcacheContent}s into a single {@link MemcacheMessage} with
 * no following {@link MemcacheContent}s.  It is useful when you don't want to take
 * care of memcache messages where the content comes along in chunks. Insert this
 * handler after a AbstractMemcacheObjectDecoder in the {@link ChannelPipeline}.
 * <p/>
 * For example, here for the binary protocol:
 * <p/>
 * <pre>
 * {@link ChannelPipeline} p = ...;
 * ...
 * p.addLast("decoder", new {@link BinaryMemcacheRequestDecoder}());
 * p.addLast("aggregator", <b>new {@link io.netty.handler.codec.memcache.binary.BinaryMemcacheObjectAggregator}(1048576)
 * </b>);
 * ...
 * p.addLast("encoder", new {@link BinaryMemcacheResponseEncoder}());
 * p.addLast("handler", new YourMemcacheRequestHandler());
 * </pre>
 */
// CLR: MemcacheObject.class is typeof(IMemcacheObject) (docs/codec-base-porting.md, Type matching), and
// UnsupportedOperationException is NotSupportedException (docs/codec-base-porting.md, Shared CLR adaptations).
[UnstableApi]
public abstract class AbstractMemcacheObjectAggregator<H> :
        MessageAggregator<IMemcacheObject, H, IMemcacheContent, IFullMemcacheMessage>
    where H : IMemcacheMessage
{

    protected AbstractMemcacheObjectAggregator(int maxContentLength) : base(maxContentLength, typeof(IMemcacheObject))
    {
    }

    protected override bool IsContentMessage(IMemcacheObject msg)
    {
        return msg is IMemcacheContent;
    }

    protected override bool IsLastContentMessage(IMemcacheContent msg)
    {
        return msg is ILastMemcacheContent;
    }

    protected override bool IsAggregated(IMemcacheObject msg)
    {
        return msg is IFullMemcacheMessage;
    }

    protected override bool IsContentLengthInvalid(H start, int maxContentLength)
    {
        return false;
    }

    protected override object NewContinueResponse(H start, int maxContentLength, IChannelPipeline pipeline)
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
}
