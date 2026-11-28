/*
 * Copyright 2016 The Netty Project
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

using System;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * The default {@link Http2DataFrame} implementation.
 */
// CLR: ByteBufHolder is IByteBufHolder, whose content() is the read-only Content property and refCnt() the RefCnt
// property (docs/buffer-porting.md, Buffer holders and conversion). The overrides that narrow the return type to
// DefaultHttp2DataFrame (stream, copy, duplicate, retainedDuplicate, replace, retain, touch) are covariant class
// overrides where a base class declares them and otherwise public methods with the narrowed type, while the
// IByteBufHolder/IReferenceCounted members declared with other return types are implemented explicitly with them
// (docs/codec-http-porting.md, Shared CLR adaptations). IllegalArgumentException is ArgumentException. toString()
// keeps Java's text: a null stream prints "null" and booleans print true/false (Http2CodecUtil.ToJavaString).
public sealed class DefaultHttp2DataFrame : AbstractHttp2StreamFrame, IHttp2DataFrame
{
    private readonly ByteBuf content;
    private readonly bool endStream;
    private readonly int padding;
    private readonly int initialFlowControlledBytes;

    /**
     * Equivalent to {@code new DefaultHttp2DataFrame(content, false)}.
     *
     * @param content non-{@code null} payload
     */
    public DefaultHttp2DataFrame(ByteBuf content) : this(content, false)
    {
    }

    /**
     * Equivalent to {@code new DefaultHttp2DataFrame(Unpooled.EMPTY_BUFFER, endStream)}.
     *
     * @param endStream whether this data should terminate the stream
     */
    public DefaultHttp2DataFrame(bool endStream) : this(Unpooled.EmptyBuffer, endStream)
    {
    }

    /**
     * Equivalent to {@code new DefaultHttp2DataFrame(content, endStream, 0)}.
     *
     * @param content non-{@code null} payload
     * @param endStream whether this data should terminate the stream
     */
    public DefaultHttp2DataFrame(ByteBuf content, bool endStream) : this(content, endStream, 0)
    {
    }

    /**
     * Construct a new data message.
     *
     * @param content non-{@code null} payload
     * @param endStream whether this data should terminate the stream
     * @param padding additional bytes that should be added to obscure the true content size. Must be between 0 and
     *                256 (inclusive).
     */
    public DefaultHttp2DataFrame(ByteBuf content, bool endStream, int padding)
    {
        this.content = CheckNotNull(content, "content");
        this.endStream = endStream;
        VerifyPadding(padding);
        this.padding = padding;
        if (Content.ReadableBytes + (long) padding > int.MaxValue)
        {
            throw new ArgumentException("content + padding must be <= Integer.MAX_VALUE");
        }

        initialFlowControlledBytes = Content.ReadableBytes + padding;
    }

    public override DefaultHttp2DataFrame Stream(IHttp2FrameStream stream)
    {
        base.Stream(stream);
        return this;
    }

    public override string Name()
    {
        return "DATA";
    }

    public bool IsEndStream()
    {
        return endStream;
    }

    public int Padding()
    {
        return padding;
    }

    public ByteBuf Content => ByteBufUtil.EnsureAccessible(content);

    public int InitialFlowControlledBytes()
    {
        return initialFlowControlledBytes;
    }

    public DefaultHttp2DataFrame Copy()
    {
        return Replace(Content.Copy());
    }

    public DefaultHttp2DataFrame Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public DefaultHttp2DataFrame RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public DefaultHttp2DataFrame Replace(ByteBuf content)
    {
        return new DefaultHttp2DataFrame(content, endStream, padding);
    }

    public int RefCnt => content.RefCnt;

    public bool Release()
    {
        return content.Release();
    }

    public bool Release(int decrement)
    {
        return content.Release(decrement);
    }

    public DefaultHttp2DataFrame Retain()
    {
        content.Retain();
        return this;
    }

    public DefaultHttp2DataFrame Retain(int increment)
    {
        content.Retain(increment);
        return this;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(stream=" + ToJavaString(Stream()) + ", content=" + content
               + ", endStream=" + ToJavaString(endStream) + ", padding=" + ToJavaString(padding) + ')';
    }

    public DefaultHttp2DataFrame Touch()
    {
        content.Touch();
        return this;
    }

    public DefaultHttp2DataFrame Touch(object hint)
    {
        content.Touch(hint);
        return this;
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

    IReferenceCounted IReferenceCounted.Retain()
    {
        return Retain();
    }

    IReferenceCounted IReferenceCounted.Retain(int increment)
    {
        return Retain(increment);
    }

    IReferenceCounted IReferenceCounted.Touch()
    {
        return Touch();
    }

    IReferenceCounted IReferenceCounted.Touch(object hint)
    {
        return Touch(hint);
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttp2DataFrame))
        {
            return false;
        }

        DefaultHttp2DataFrame other = (DefaultHttp2DataFrame) o;
        return base.Equals(other) && content.Equals(other.Content)
            && endStream == other.endStream && padding == other.padding;
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = hash * 31 + content.GetHashCode();
        hash = hash * 31 + (endStream ? 0 : 1);
        hash = hash * 31 + padding;
        return hash;
    }
}
