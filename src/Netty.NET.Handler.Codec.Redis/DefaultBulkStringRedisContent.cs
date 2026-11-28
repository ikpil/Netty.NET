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
 * A default implementation of {@link BulkStringRedisContent}.
 */
// CLR: the covariant overrides of the virtual DefaultByteBufHolder members are C# covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); content() is the Content
// property.
[UnstableApi]
public class DefaultBulkStringRedisContent : DefaultByteBufHolder, IBulkStringRedisContent
{

    /**
     * Creates a {@link DefaultBulkStringRedisContent} for the given {@code content}.
     *
     * @param content the content, can be {@code null}.
     */
    public DefaultBulkStringRedisContent(ByteBuf content) : base(content)
    {
    }

    public override IBulkStringRedisContent Copy()
    {
        return (IBulkStringRedisContent) base.Copy();
    }

    public override IBulkStringRedisContent Duplicate()
    {
        return (IBulkStringRedisContent) base.Duplicate();
    }

    public override IBulkStringRedisContent RetainedDuplicate()
    {
        return (IBulkStringRedisContent) base.RetainedDuplicate();
    }

    public override IBulkStringRedisContent Replace(ByteBuf content)
    {
        return new DefaultBulkStringRedisContent(content);
    }

    public override IBulkStringRedisContent Retain()
    {
        base.Retain();
        return this;
    }

    public override IBulkStringRedisContent Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IBulkStringRedisContent Touch()
    {
        base.Touch();
        return this;
    }

    public override IBulkStringRedisContent Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("content=")
                .Append(Content)
                .Append(']').ToString();
    }
}
