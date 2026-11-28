/*
 * Copyright 2017 The Netty Project
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
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: DefaultByteBufHolder is the Buffer port's DefaultByteBufHolder; the overrides that narrow its return types to
// DefaultHttp2UnknownFrame are C# 9 covariant overrides, and content() is the Content property (docs/buffer-porting.md,
// Buffer holders and conversion; as codec-smtp's DefaultSmtpContent). stream(Http2FrameStream) narrows the
// IHttp2StreamFrame member, which C# cannot do for an interface member, so IHttp2StreamFrame.Stream is implemented
// explicitly with the public Stream(IHttp2FrameStream) (docs/codec-http-porting.md, Shared CLR adaptations). The
// Java byte frameType is the CLR byte; toString() and hashCode() use its signed Java value through (sbyte), so an
// unknown frame type 0xFF prints -1 as in Java (owner rule 6). The other numbers use the invariant culture and a null
// stream prints "null" (Http2CodecUtil.ToJavaString).
public sealed class DefaultHttp2UnknownFrame : DefaultByteBufHolder, IHttp2UnknownFrame
{
    private readonly byte frameType;
    private readonly Http2Flags flags;
    private IHttp2FrameStream stream;

    public DefaultHttp2UnknownFrame(byte frameType, Http2Flags flags) : this(frameType, flags, Unpooled.EmptyBuffer)
    {
    }

    public DefaultHttp2UnknownFrame(byte frameType, Http2Flags flags, ByteBuf data) : base(data)
    {
        this.frameType = frameType;
        this.flags = flags;
    }

    public IHttp2FrameStream Stream()
    {
        return stream;
    }

    public DefaultHttp2UnknownFrame Stream(IHttp2FrameStream stream)
    {
        this.stream = stream;
        return this;
    }

    IHttp2StreamFrame IHttp2StreamFrame.Stream(IHttp2FrameStream stream)
    {
        return Stream(stream);
    }

    public byte FrameType()
    {
        return frameType;
    }

    public Http2Flags Flags()
    {
        return flags;
    }

    public string Name()
    {
        return "UNKNOWN";
    }

    public override DefaultHttp2UnknownFrame Copy()
    {
        return Replace(Content.Copy());
    }

    public override DefaultHttp2UnknownFrame Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public override DefaultHttp2UnknownFrame RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public override DefaultHttp2UnknownFrame Replace(ByteBuf content)
    {
        return new DefaultHttp2UnknownFrame(frameType, flags, content).Stream(stream);
    }

    public override DefaultHttp2UnknownFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override DefaultHttp2UnknownFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(frameType=" + ToJavaString((sbyte) frameType) + ", stream=" +
               ToJavaString(stream) + ", flags=" + flags + ", content=" + ContentToString() + ')';
    }

    public override DefaultHttp2UnknownFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override DefaultHttp2UnknownFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttp2UnknownFrame))
        {
            return false;
        }

        DefaultHttp2UnknownFrame other = (DefaultHttp2UnknownFrame) o;
        IHttp2FrameStream otherStream = other.Stream();
        return (ReferenceEquals(stream, otherStream) || otherStream != null && otherStream.Equals(stream))
               && flags.Equals(other.Flags())
               && frameType == other.FrameType()
               && base.Equals(other);
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = hash * 31 + (sbyte) frameType;
        hash = hash * 31 + flags.GetHashCode();

        if (stream != null)
        {
            hash = hash * 31 + stream.GetHashCode();
        }

        return hash;
    }
}
