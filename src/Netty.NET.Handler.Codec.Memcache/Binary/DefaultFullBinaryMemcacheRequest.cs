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
using Netty.NET.Handler.Codec.Memcache;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * The default implementation of a {@link FullBinaryMemcacheRequest}.
 */
// CLR: content() is the IByteBufHolder Content property (docs/buffer-porting.md, Buffer holders and conversion);
// covariant override returns as in AbstractBinaryMemcacheMessage
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). The module interfaces inherit
// the holder methods instead of narrowing them (owner decision 2026-11-19; docs/transport-porting.md, Decisions), so only
// the IByteBufHolder members, which the shared interface re-declares with its own return type, are explicit
// implementations that forward to the public members, as Transport DatagramPacket does
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
[UnstableApi]
public class DefaultFullBinaryMemcacheRequest : DefaultBinaryMemcacheRequest
    , IFullBinaryMemcacheRequest
{

    private readonly ByteBuf content;

    /**
     * Create a new {@link DefaultBinaryMemcacheRequest} with the header, key and extras.
     *
     * @param key    the key to use.
     * @param extras the extras to use.
     */
    public DefaultFullBinaryMemcacheRequest(ByteBuf key, ByteBuf extras) : this(key, extras, Unpooled.Buffer(0))
    {
    }

    /**
     * Create a new {@link DefaultBinaryMemcacheRequest} with the header, key, extras and content.
     *
     * @param key     the key to use.
     * @param extras  the extras to use.
     * @param content the content of the full request.
     */
    public DefaultFullBinaryMemcacheRequest(ByteBuf key, ByteBuf extras,
                                            ByteBuf content) : base(key, extras)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
        // CLR: Java sign-extends the signed byte extrasLength() in this sum (JLS 5.6, 5.1.2:
        // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6); (sbyte) keeps the Java total for the
        // System.Byte value (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
        SetTotalBodyLength(KeyLength() + (sbyte) ExtrasLength() + content.ReadableBytes);
    }

    public ByteBuf Content => content;

    public override IFullBinaryMemcacheRequest Retain()
    {
        base.Retain();
        return this;
    }

    public override IFullBinaryMemcacheRequest Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IFullBinaryMemcacheRequest Touch()
    {
        base.Touch();
        return this;
    }

    public override IFullBinaryMemcacheRequest Touch(object hint)
    {
        base.Touch(hint);
        content.Touch(hint);
        return this;
    }

    protected override void Deallocate()
    {
        base.Deallocate();
        content.Release();
    }

    public IFullBinaryMemcacheRequest Copy()
    {
        ByteBuf key = Key();
        if (key != null)
        {
            key = key.Copy();
        }
        ByteBuf extras = Extras();
        if (extras != null)
        {
            extras = extras.Copy();
        }
        return NewInstance(key, extras, Content.Copy());
    }

    public IFullBinaryMemcacheRequest Duplicate()
    {
        ByteBuf key = Key();
        if (key != null)
        {
            key = key.Duplicate();
        }
        ByteBuf extras = Extras();
        if (extras != null)
        {
            extras = extras.Duplicate();
        }
        return NewInstance(key, extras, Content.Duplicate());
    }

    public IFullBinaryMemcacheRequest RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public IFullBinaryMemcacheRequest Replace(ByteBuf content)
    {
        ByteBuf key = Key();
        if (key != null)
        {
            key = key.RetainedDuplicate();
        }
        ByteBuf extras = Extras();
        if (extras != null)
        {
            extras = extras.RetainedDuplicate();
        }
        return NewInstance(key, extras, content);
    }

    private DefaultFullBinaryMemcacheRequest NewInstance(ByteBuf key, ByteBuf extras, ByteBuf content)
    {
        DefaultFullBinaryMemcacheRequest newInstance = new DefaultFullBinaryMemcacheRequest(key, extras, content);
        CopyMeta(newInstance);
        return newInstance;
    }

    IByteBufHolder IByteBufHolder.Copy()
    {
        return Copy();
    }
    IByteBufHolder IByteBufHolder.Duplicate()
    {
        return Duplicate();
    }
    IByteBufHolder IByteBufHolder.RetainedDuplicate()
    {
        return RetainedDuplicate();
    }
    IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
    {
        return Replace(content);
    }
    IByteBufHolder IByteBufHolder.Retain()
    {
        return Retain();
    }
    IByteBufHolder IByteBufHolder.Retain(int increment)
    {
        return Retain(increment);
    }
    IByteBufHolder IByteBufHolder.Touch()
    {
        return Touch();
    }
    IByteBufHolder IByteBufHolder.Touch(object hint)
    {
        return Touch(hint);
    }
}
