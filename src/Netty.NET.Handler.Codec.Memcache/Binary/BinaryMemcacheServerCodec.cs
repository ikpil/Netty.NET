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

using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * The full server codec that combines the correct encoder and decoder.
 * <p/>
 * Use this codec if you need to implement a server that speaks the memcache binary protocol.
 * Internally, it combines the {@link BinaryMemcacheRequestDecoder} and the
 * {@link BinaryMemcacheResponseEncoder} to request decoding and response encoding.
 */
// CLR: the static DEFAULT_MAX_CHUNK_SIZE of the generic AbstractBinaryMemcacheDecoder is reached through the closed
// type that this codec decodes; a C# static member belongs to each closed constructed type
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time).
[UnstableApi]
public class BinaryMemcacheServerCodec :
        CombinedChannelDuplexHandler<BinaryMemcacheRequestDecoder, BinaryMemcacheResponseEncoder>
{

    public BinaryMemcacheServerCodec() : this(AbstractBinaryMemcacheDecoder<IBinaryMemcacheRequest>.DEFAULT_MAX_CHUNK_SIZE)
    {
    }

    public BinaryMemcacheServerCodec(int decodeChunkSize)
        : base(new BinaryMemcacheRequestDecoder(decodeChunkSize), new BinaryMemcacheResponseEncoder())
    {
    }
}
