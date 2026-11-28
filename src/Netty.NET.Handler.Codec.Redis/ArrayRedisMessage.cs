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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Arrays of <a href="https://redis.io/topics/protocol">RESP</a>.
 */
// CLR: List<RedisMessage> is IList<IRedisMessage>; Collections.emptyList() is the empty array Array.Empty, whose
// IList rejects changes as the Java empty list does
// (https://learn.microsoft.com/en-us/dotnet/api/system.array.empty). The covariant touch(Object) override is a C#
// covariant override return
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), as are the
// retain()/touch() overrides of NULL_INSTANCE and EMPTY_INSTANCE. The child count in toString() uses the invariant
// culture (https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.append).
[UnstableApi]
public class ArrayRedisMessage : AbstractReferenceCounted, IRedisMessage
{

    private readonly IList<IRedisMessage> children;

    private ArrayRedisMessage()
    {
        children = Array.Empty<IRedisMessage>();
    }

    /**
     * Creates a {@link ArrayRedisMessage} for the given {@code content}.
     *
     * @param children the children.
     */
    public ArrayRedisMessage(IList<IRedisMessage> children)
    {
        // do not retain here. children are already retained when created.
        this.children = ObjectUtil.CheckNotNull(children, "children");
    }

    /**
     * Get children of this Arrays. It can be null or empty.
     *
     * @return list of {@link RedisMessage}s.
     */
    public IList<IRedisMessage> Children()
    {
        return children;
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

    protected override void Deallocate()
    {
        foreach (IRedisMessage msg in children)
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    public override ArrayRedisMessage Touch(object hint)
    {
        foreach (IRedisMessage msg in children)
        {
            ReferenceCountUtil.Touch(msg);
        }

        return this;
    }

    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("children=")
                .Append(children.Count.ToString(CultureInfo.InvariantCulture))
                .Append(']').ToString();
    }

    /**
     * A predefined null array instance for {@link ArrayRedisMessage}.
     */
    public static readonly ArrayRedisMessage NULL_INSTANCE = new NullArrayRedisMessage();

    private sealed class NullArrayRedisMessage : ArrayRedisMessage
    {
        public override bool IsNull()
        {
            return true;
        }

        public override ArrayRedisMessage Retain()
        {
            return this;
        }

        public override ArrayRedisMessage Retain(int increment)
        {
            return this;
        }

        public override ArrayRedisMessage Touch()
        {
            return this;
        }

        public override ArrayRedisMessage Touch(object hint)
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

        public override string ToString()
        {
            return "NullArrayRedisMessage";
        }
    }

    /**
     * A predefined empty array instance for {@link ArrayRedisMessage}.
     */
    public static readonly ArrayRedisMessage EMPTY_INSTANCE = new EmptyArrayRedisMessage();

    private sealed class EmptyArrayRedisMessage : ArrayRedisMessage
    {
        public override ArrayRedisMessage Retain()
        {
            return this;
        }

        public override ArrayRedisMessage Retain(int increment)
        {
            return this;
        }

        public override ArrayRedisMessage Touch()
        {
            return this;
        }

        public override ArrayRedisMessage Touch(object hint)
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

        public override string ToString()
        {
            return "EmptyArrayRedisMessage";
        }
    }
}
