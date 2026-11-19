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
 * The encoder part which takes care of encoding the request headers.
 */
[UnstableApi]
public class BinaryMemcacheRequestEncoder
    : AbstractBinaryMemcacheEncoder<IBinaryMemcacheRequest>
{

    protected override void EncodeHeader(ByteBuf buf, IBinaryMemcacheRequest msg)
    {
        buf.WriteByte(msg.Magic());
        buf.WriteByte(msg.Opcode());
        buf.WriteShort(msg.KeyLength());
        buf.WriteByte(msg.ExtrasLength());
        buf.WriteByte(msg.DataType());
        buf.WriteShort(msg.Reserved());
        buf.WriteInt(msg.TotalBodyLength());
        buf.WriteInt(msg.Opaque());
        buf.WriteLong(msg.Cas());
    }

}
