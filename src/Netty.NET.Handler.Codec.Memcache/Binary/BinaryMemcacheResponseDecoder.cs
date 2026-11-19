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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * The decoder which takes care of decoding the response headers.
 */
[UnstableApi]
public class BinaryMemcacheResponseDecoder
    : AbstractBinaryMemcacheDecoder<IBinaryMemcacheResponse>
{

    public BinaryMemcacheResponseDecoder() : this(DEFAULT_MAX_CHUNK_SIZE)
    {
    }

    public BinaryMemcacheResponseDecoder(int chunkSize) : base(chunkSize)
    {
    }

    protected override IBinaryMemcacheResponse DecodeHeader(ByteBuf @in)
    {
        DefaultBinaryMemcacheResponse header = new DefaultBinaryMemcacheResponse();
        header.SetMagic(@in.ReadByte());
        header.SetOpcode(@in.ReadByte());
        header.SetKeyLength(@in.ReadShort());
        header.SetExtrasLength(@in.ReadByte());
        header.SetDataType(@in.ReadByte());
        header.SetStatus(@in.ReadShort());
        header.SetTotalBodyLength(@in.ReadInt());
        header.SetOpaque(@in.ReadInt());
        header.SetCas(@in.ReadLong());
        return header;
    }

    protected override IBinaryMemcacheResponse BuildInvalidMessage()
    {
        return new DefaultBinaryMemcacheResponse(Unpooled.EmptyBuffer, Unpooled.EmptyBuffer);
    }
}
