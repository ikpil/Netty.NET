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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * An aggregated bulk string of <a href="https://redis.io/topics/protocol">RESP</a>.
 */
// CLR: the covariant overrides of the virtual DefaultByteBufHolder members (content(), refCnt(), copy(), duplicate(),
// retainedDuplicate(), replace(), retain(), touch(), release()) are C# covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), and content()/refCnt() are the
// Content/RefCnt properties (docs/buffer-porting.md, Buffer holders and conversion). The module interfaces inherit the
// holder methods from IByteBufHolder instead of narrowing them (owner decision 2026-11-19; docs/transport-porting.md,
// Decisions), whose members DefaultByteBufHolder implements. The anonymous subclasses of NULL_INSTANCE and EMPTY_INSTANCE are the named nested classes NullFullBulkStringRedisMessage
// and EmptyFullBulkStringRedisMessage.
[UnstableApi]
public class FullBulkStringRedisMessage : DefaultByteBufHolder, ILastBulkStringRedisContent
{

    private FullBulkStringRedisMessage() : this(Unpooled.EmptyBuffer)
    {
    }

    /**
     * Creates a {@link FullBulkStringRedisMessage} for the given {@code content}.
     *
     * @param content the content, must not be {@code null}. If content is null or empty,
     * use {@link FullBulkStringRedisMessage#NULL_INSTANCE} or {@link FullBulkStringRedisMessage#EMPTY_INSTANCE}
     * instead of constructor.
     */
    public FullBulkStringRedisMessage(ByteBuf content) : base(content)
    {
    }

    /**
     * Returns whether the content of this message is {@code null}.
     *
     * @return indicates whether the content of this message is {@code null}.
     */
    public virtual bool IsNull()
    {
        return false;
    }

    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("content=")
                .Append(Content)
                .Append(']').ToString();
    }

    /**
     * A predefined null instance of {@link FullBulkStringRedisMessage}.
     */
    public static readonly FullBulkStringRedisMessage NULL_INSTANCE = new NullFullBulkStringRedisMessage();

    private sealed class NullFullBulkStringRedisMessage : FullBulkStringRedisMessage
    {
        public override bool IsNull()
        {
            return true;
        }

        public override ByteBuf Content => Unpooled.EmptyBuffer;

        public override FullBulkStringRedisMessage Copy()
        {
            return this;
        }

        public override FullBulkStringRedisMessage Duplicate()
        {
            return this;
        }

        public override FullBulkStringRedisMessage RetainedDuplicate()
        {
            return this;
        }

        public override int RefCnt => 1;

        public override FullBulkStringRedisMessage Retain()
        {
            return this;
        }

        public override FullBulkStringRedisMessage Retain(int increment)
        {
            return this;
        }

        public override FullBulkStringRedisMessage Touch()
        {
            return this;
        }

        public override FullBulkStringRedisMessage Touch(object hint)
        {
            return this;
        }

        public override bool Release()
        {
            return false;
        }

        public override bool Release(int decrement)
        {
            return false;
        }
    }

    /**
     * A predefined empty instance of {@link FullBulkStringRedisMessage}.
     */
    public static readonly FullBulkStringRedisMessage EMPTY_INSTANCE = new EmptyFullBulkStringRedisMessage();

    private sealed class EmptyFullBulkStringRedisMessage : FullBulkStringRedisMessage
    {
        public override ByteBuf Content => Unpooled.EmptyBuffer;

        public override FullBulkStringRedisMessage Copy()
        {
            return this;
        }

        public override FullBulkStringRedisMessage Duplicate()
        {
            return this;
        }

        public override FullBulkStringRedisMessage RetainedDuplicate()
        {
            return this;
        }

        public override int RefCnt => 1;

        public override FullBulkStringRedisMessage Retain()
        {
            return this;
        }

        public override FullBulkStringRedisMessage Retain(int increment)
        {
            return this;
        }

        public override FullBulkStringRedisMessage Touch()
        {
            return this;
        }

        public override FullBulkStringRedisMessage Touch(object hint)
        {
            return this;
        }

        public override bool Release()
        {
            return false;
        }

        public override bool Release(int decrement)
        {
            return false;
        }
    }

    public override FullBulkStringRedisMessage Copy()
    {
        return (FullBulkStringRedisMessage) base.Copy();
    }

    public override FullBulkStringRedisMessage Duplicate()
    {
        return (FullBulkStringRedisMessage) base.Duplicate();
    }

    public override FullBulkStringRedisMessage RetainedDuplicate()
    {
        return (FullBulkStringRedisMessage) base.RetainedDuplicate();
    }

    public override FullBulkStringRedisMessage Replace(ByteBuf content)
    {
        return new FullBulkStringRedisMessage(content);
    }

    public override FullBulkStringRedisMessage Retain()
    {
        base.Retain();
        return this;
    }

    public override FullBulkStringRedisMessage Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override FullBulkStringRedisMessage Touch()
    {
        base.Touch();
        return this;
    }

    public override FullBulkStringRedisMessage Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
