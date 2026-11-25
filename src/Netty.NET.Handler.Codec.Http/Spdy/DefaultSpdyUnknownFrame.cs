/*
 * Copyright 2024 The Netty Project
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

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: DefaultByteBufHolder is the Buffer port's DefaultByteBufHolder, whose virtual members return IByteBufHolder;
// the overrides narrow them to DefaultSpdyUnknownFrame with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). content() is the Content
// property and contentToString() is ContentToString(). equals/hashCode/toString are Equals/GetHashCode/ToString. The
// Java byte flags is the CLR byte; hashCode adds the sign-extended Java byte and toString prints the signed Java value,
// so both read it as (sbyte) (rule 6), and the hash arithmetic wraps as Java's int arithmetic (unchecked).
public sealed class DefaultSpdyUnknownFrame : DefaultByteBufHolder, ISpdyUnknownFrame
{
    private readonly int frameType;
    private readonly byte flags;

    public DefaultSpdyUnknownFrame(int frameType, byte flags, ByteBuf data) : base(data)
    {
        this.frameType = frameType;
        this.flags = flags;
    }

    public int FrameType()
    {
        return frameType;
    }

    public byte Flags()
    {
        return flags;
    }

    public override DefaultSpdyUnknownFrame Copy()
    {
        return Replace(Content.Copy());
    }

    public override DefaultSpdyUnknownFrame Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public override DefaultSpdyUnknownFrame RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public override DefaultSpdyUnknownFrame Replace(ByteBuf content)
    {
        return new DefaultSpdyUnknownFrame(frameType, flags, content);
    }

    public override DefaultSpdyUnknownFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override DefaultSpdyUnknownFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override DefaultSpdyUnknownFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override DefaultSpdyUnknownFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultSpdyUnknownFrame))
        {
            return false;
        }
        DefaultSpdyUnknownFrame that = (DefaultSpdyUnknownFrame) o;
        return frameType == that.frameType
            && flags == that.flags
            && base.Equals(that);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int result = base.GetHashCode();
            result = 31 * result + frameType;
            result = 31 * result + (sbyte) flags;
            return result;
        }
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(" + "frameType="
            + frameType.ToString(CultureInfo.InvariantCulture) +
            ", flags=" + unchecked((sbyte) flags).ToString(CultureInfo.InvariantCulture) + ", content=" +
            ContentToString() + ')';
    }
}
