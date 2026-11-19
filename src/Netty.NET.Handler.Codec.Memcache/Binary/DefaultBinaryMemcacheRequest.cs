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
 * The default implementation of the {@link BinaryMemcacheRequest}.
 */
// CLR: covariant override returns as in AbstractBinaryMemcacheMessage
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); the package-private copyMeta
// is internal.
[UnstableApi]
public class DefaultBinaryMemcacheRequest : AbstractBinaryMemcacheMessage, IBinaryMemcacheRequest
{

    /**
     * Default magic byte for a request.
     */
    // CLR: (byte) 0x80 is the signed Java byte -128; as System.Byte (owner decision, docs/codec-memcache-porting.md,
    // Byte values) the constant is 128 with the same bits on the wire
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    public const byte REQUEST_MAGIC_BYTE = 0x80;

    private short reserved;

    /**
     * Create a new {@link DefaultBinaryMemcacheRequest} with the header only.
     */
    public DefaultBinaryMemcacheRequest() : this(null, null)
    {
    }

    /**
     * Create a new {@link DefaultBinaryMemcacheRequest} with the header and key.
     *
     * @param key    the key to use.
     */
    public DefaultBinaryMemcacheRequest(ByteBuf key) : this(key, null)
    {
    }

    /**
     * Create a new {@link DefaultBinaryMemcacheRequest} with the header only.
     *
     * @param key    the key to use.
     * @param extras the extras to use.
     */
    public DefaultBinaryMemcacheRequest(ByteBuf key, ByteBuf extras) : base(key, extras)
    {
        SetMagic(REQUEST_MAGIC_BYTE);
    }

    public short Reserved()
    {
        return reserved;
    }

    public IBinaryMemcacheRequest SetReserved(short reserved)
    {
        this.reserved = reserved;
        return this;
    }

    public override IBinaryMemcacheRequest Retain()
    {
        base.Retain();
        return this;
    }

    public override IBinaryMemcacheRequest Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IBinaryMemcacheRequest Touch()
    {
        base.Touch();
        return this;
    }

    public override IBinaryMemcacheRequest Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    /**
     * Copies special metadata hold by this instance to the provided instance
     *
     * @param dst The instance where to copy the metadata of this instance to
     */
    internal void CopyMeta(DefaultBinaryMemcacheRequest dst)
    {
        base.CopyMeta(dst);
        dst.reserved = reserved;
    }
}
