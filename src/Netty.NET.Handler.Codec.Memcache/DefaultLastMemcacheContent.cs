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

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * The default implementation for the {@link LastMemcacheContent}.
 */
// CLR: covariant override returns as in DefaultMemcacheContent
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); content() is the Content
// property (docs/buffer-porting.md, Buffer holders and conversion).
[UnstableApi]
public class DefaultLastMemcacheContent : DefaultMemcacheContent, ILastMemcacheContent
{

    public DefaultLastMemcacheContent() : base(Unpooled.Buffer())
    {
    }

    public DefaultLastMemcacheContent(ByteBuf content) : base(content)
    {
    }

    public override ILastMemcacheContent Retain()
    {
        base.Retain();
        return this;
    }

    public override ILastMemcacheContent Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override ILastMemcacheContent Touch()
    {
        base.Touch();
        return this;
    }

    public override ILastMemcacheContent Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override ILastMemcacheContent Copy()
    {
        return Replace(Content.Copy());
    }

    public override ILastMemcacheContent Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public override ILastMemcacheContent RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public override ILastMemcacheContent Replace(ByteBuf content)
    {
        return new DefaultLastMemcacheContent(content);
    }
}
