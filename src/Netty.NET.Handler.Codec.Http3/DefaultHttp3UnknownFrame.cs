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

using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: DefaultByteBufHolder is the Buffer port's DefaultByteBufHolder; the overrides that narrow its return types
// to IHttp3UnknownFrame are C# 9 covariant overrides, and content() is the Content property.
// Objects.hash(super.hashCode(), type) is spelled out (31 * (31 + hash) + Long.hashCode(type)) to keep the Java
// value; the type prints with the invariant culture. getClass() != o.getClass() is GetType() != o.GetType().
public sealed class DefaultHttp3UnknownFrame : DefaultByteBufHolder, IHttp3UnknownFrame
{
    private readonly long type;

    public DefaultHttp3UnknownFrame(long type, ByteBuf payload) : base(payload)
    {
        this.type = Http3CodecUtils.CheckIsReservedFrameType(type);
    }

    public long Type()
    {
        return type;
    }

    public override IHttp3UnknownFrame Copy()
    {
        return new DefaultHttp3UnknownFrame(type, Content.Copy());
    }

    public override IHttp3UnknownFrame Duplicate()
    {
        return new DefaultHttp3UnknownFrame(type, Content.Duplicate());
    }

    public override IHttp3UnknownFrame RetainedDuplicate()
    {
        return new DefaultHttp3UnknownFrame(type, Content.RetainedDuplicate());
    }

    public override IHttp3UnknownFrame Replace(ByteBuf content)
    {
        return new DefaultHttp3UnknownFrame(type, content);
    }

    public override IHttp3UnknownFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override IHttp3UnknownFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IHttp3UnknownFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override IHttp3UnknownFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(type=" + Type().ToString(CultureInfo.InvariantCulture) +
               ", content=" + Content + ')';
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        DefaultHttp3UnknownFrame that = (DefaultHttp3UnknownFrame) o;
        if (type != that.type)
        {
            return false;
        }

        return base.Equals(o);
    }

    public override int GetHashCode()
    {
        return unchecked(31 * (31 + base.GetHashCode()) + (int) (type ^ (type >>> 32)));
    }
}
