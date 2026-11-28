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
 * The default implementation of the {@link BinaryMemcacheResponse}.
 */
[UnstableApi]
public class DefaultBinaryMemcacheResponse : AbstractBinaryMemcacheMessage, IBinaryMemcacheResponse
{

    /**
     * Default magic byte for a request.
     */
    // CLR: (byte) 0x81 is the signed Java byte -127; as System.Byte (owner decision, docs/codec-memcache-porting.md,
    // Byte values) the constant is 129 with the same bits on the wire
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    public const byte RESPONSE_MAGIC_BYTE = 0x81;

    private short status;

    /**
     * Create a new {@link DefaultBinaryMemcacheResponse} with the header only.
     */
    public DefaultBinaryMemcacheResponse() : this(null, null)
    {
    }

    /**
     * Create a new {@link DefaultBinaryMemcacheResponse} with the header and key.
     *
     * @param key    the key to use.
     */
    public DefaultBinaryMemcacheResponse(ByteBuf key) : this(key, null)
    {
    }

    /**
     * Create a new {@link DefaultBinaryMemcacheResponse} with the header, key and extras.
     *
     * @param key    the key to use.
     * @param extras the extras to use.
     */
    public DefaultBinaryMemcacheResponse(ByteBuf key, ByteBuf extras) : base(key, extras)
    {
        SetMagic(RESPONSE_MAGIC_BYTE);
    }

    public short Status()
    {
        return status;
    }

    public IBinaryMemcacheResponse SetStatus(short status)
    {
        this.status = status;
        return this;
    }

    public override IBinaryMemcacheResponse Retain()
    {
        base.Retain();
        return this;
    }

    public override IBinaryMemcacheResponse Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IBinaryMemcacheResponse Touch()
    {
        base.Touch();
        return this;
    }

    public override IBinaryMemcacheResponse Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    /**
     * Copies special metadata hold by this instance to the provided instance
     *
     * @param dst The instance where to copy the metadata of this instance to
     */
    internal void CopyMeta(DefaultBinaryMemcacheResponse dst)
    {
        base.CopyMeta(dst);
        dst.status = status;
    }
}
