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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * A default implementation for {@link LastBulkStringRedisContent}.
 */
// CLR: covariant override returns as in DefaultBulkStringRedisContent
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override).
[UnstableApi]
public sealed class DefaultLastBulkStringRedisContent : DefaultBulkStringRedisContent
        , ILastBulkStringRedisContent
{

    /**
     * Creates a {@link DefaultLastBulkStringRedisContent} for the given {@code content}.
     * @param content the content, can be {@code null}.
     */
    public DefaultLastBulkStringRedisContent(ByteBuf content) : base(content)
    {
    }

    public override ILastBulkStringRedisContent Copy()
    {
        return (ILastBulkStringRedisContent) base.Copy();
    }

    public override ILastBulkStringRedisContent Duplicate()
    {
        return (ILastBulkStringRedisContent) base.Duplicate();
    }

    public override ILastBulkStringRedisContent RetainedDuplicate()
    {
        return (ILastBulkStringRedisContent) base.RetainedDuplicate();
    }

    public override ILastBulkStringRedisContent Replace(ByteBuf content)
    {
        return new DefaultLastBulkStringRedisContent(content);
    }

    public override ILastBulkStringRedisContent Retain()
    {
        base.Retain();
        return this;
    }

    public override ILastBulkStringRedisContent Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override ILastBulkStringRedisContent Touch()
    {
        base.Touch();
        return this;
    }

    public override ILastBulkStringRedisContent Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
