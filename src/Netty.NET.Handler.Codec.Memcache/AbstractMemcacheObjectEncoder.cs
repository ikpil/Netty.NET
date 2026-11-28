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
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * A general purpose {@link AbstractMemcacheObjectEncoder} that encodes {@link MemcacheMessage}s.
 * <p/>
 * <p>Note that this class is designed to be extended, especially because both the binary and ascii protocol
 * require different treatment of their messages. Since the content chunk writing is the same for both, the encoder
 * abstracts this right away.</p>
 */
// CLR: FileRegion is IFileRegion, whose count() is Count(); IllegalStateException is InvalidOperationException
// (docs/codec-base-porting.md, Shared CLR adaptations).
[UnstableApi]
public abstract class AbstractMemcacheObjectEncoder<M> : MessageToMessageEncoder<object>
    where M : IMemcacheMessage
{

    private bool expectingMoreContent;

    public AbstractMemcacheObjectEncoder() : base(typeof(object))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
    {
        if (msg is IMemcacheMessage)
        {
            if (expectingMoreContent)
            {
                throw new InvalidOperationException("unexpected message type: " + StringUtil.SimpleClassName(msg));
            }

            M m = (M) msg;
            @out.Add(EncodeMessage(ctx, m));
        }

        if (msg is IMemcacheContent || msg is ByteBuf || msg is IFileRegion)
        {
            int contentLength = ContentLength(msg);
            if (contentLength > 0)
            {
                @out.Add(EncodeAndRetain(msg));
            }
            else
            {
                @out.Add(Unpooled.EmptyBuffer);
            }

            expectingMoreContent = !(msg is ILastMemcacheContent);
        }
    }

    public override bool AcceptOutboundMessage(object msg)
    {
        return msg is IMemcacheObject || msg is ByteBuf || msg is IFileRegion;
    }

    /**
     * Take the given {@link MemcacheMessage} and encode it into a writable {@link ByteBuf}.
     *
     * @param ctx the channel handler context.
     * @param msg the message to encode.
     * @return the {@link ByteBuf} representation of the message.
     */
    protected abstract ByteBuf EncodeMessage(IChannelHandlerContext ctx, M msg);

    /**
     * Determine the content length of the given object.
     *
     * @param msg the object to determine the length of.
     * @return the determined content length.
     */
    private static int ContentLength(object msg)
    {
        if (msg is IMemcacheContent)
        {
            return ((IMemcacheContent) msg).Content.ReadableBytes;
        }
        if (msg is ByteBuf)
        {
            return ((ByteBuf) msg).ReadableBytes;
        }
        if (msg is IFileRegion)
        {
            return (int) ((IFileRegion) msg).Count();
        }

        throw new InvalidOperationException("unexpected message type: " + StringUtil.SimpleClassName(msg));
    }

    /**
     * Encode the content, depending on the object type.
     *
     * @param msg the object to encode.
     * @return the encoded object.
     */
    private static object EncodeAndRetain(object msg)
    {
        if (msg is ByteBuf)
        {
            return ((ByteBuf) msg).Retain();
        }
        if (msg is IMemcacheContent)
        {
            return ((IMemcacheContent) msg).Content.Retain();
        }
        if (msg is IFileRegion)
        {
            return ((IFileRegion) msg).Retain();
        }

        throw new InvalidOperationException("unexpected message type: " + StringUtil.SimpleClassName(msg));
    }

}
