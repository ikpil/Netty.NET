/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http3;

// CLR: DefaultByteBufHolder is the Buffer port's DefaultByteBufHolder; the overrides that narrow its return types to
// IHttp3DataFrame are C# 9 covariant overrides, and content() is the Content property (docs/buffer-porting.md, Buffer
// holders and conversion; as codec-http2's DefaultHttp2UnknownFrame).
public sealed class DefaultHttp3DataFrame : DefaultByteBufHolder, IHttp3DataFrame
{

    public DefaultHttp3DataFrame(ByteBuf data) : base(data)
    {
    }

    public override IHttp3DataFrame Copy()
    {
        return new DefaultHttp3DataFrame(Content.Copy());
    }

    public override IHttp3DataFrame Duplicate()
    {
        return new DefaultHttp3DataFrame(Content.Duplicate());
    }

    public override IHttp3DataFrame RetainedDuplicate()
    {
        return new DefaultHttp3DataFrame(Content.RetainedDuplicate());
    }

    public override IHttp3DataFrame Replace(ByteBuf content)
    {
        return new DefaultHttp3DataFrame(content);
    }

    public override IHttp3DataFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override IHttp3DataFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IHttp3DataFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override IHttp3DataFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(content=" + Content + ')';
    }
}
