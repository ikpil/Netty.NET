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

using System;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdyDataFrame} implementation.
 */
// CLR: content() is the read-only Content property of IByteBufHolder and refCnt() is RefCnt (docs/buffer-porting.md,
// Buffer holders and conversion). setStreamId/setLast narrow the base class members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). The narrowed copy/duplicate/
// retainedDuplicate/replace/retain/touch members return ISpdyDataFrame; the IByteBufHolder and IReferenceCounted
// members, which those interfaces declare with their own return types, are implemented explicitly and call them, as
// core DefaultHttpContent does. IllegalArgumentException is ArgumentException (docs/codec-base-porting.md).
// StringBuilder appends the Java text: a boolean is "true"/"false" (StringBuilder.Append(bool) writes "True"/"False")
// and numbers use the invariant culture.
public class DefaultSpdyDataFrame : DefaultSpdyStreamFrame, ISpdyDataFrame
{

    private readonly ByteBuf data;

    /**
     * Creates a new instance.
     *
     * @param streamId the Stream-ID of this frame
     */
    public DefaultSpdyDataFrame(int streamId) : this(streamId, Unpooled.Buffer(0))
    {
    }

    /**
     * Creates a new instance.
     *
     * @param streamId  the Stream-ID of this frame
     * @param data      the payload of the frame. Can not exceed {@link SpdyCodecUtil#SPDY_MAX_LENGTH}
     */
    public DefaultSpdyDataFrame(int streamId, ByteBuf data) : base(streamId)
    {
        this.data = Validate(
                ObjectUtil.CheckNotNull(data, "data"));
    }

    private static ByteBuf Validate(ByteBuf data)
    {
        if (data.ReadableBytes > SpdyCodecUtil.SPDY_MAX_LENGTH)
        {
            throw new ArgumentException("data payload cannot exceed "
                    + SpdyCodecUtil.SPDY_MAX_LENGTH.ToString(CultureInfo.InvariantCulture) + " bytes");
        }
        return data;
    }

    public override ISpdyDataFrame SetStreamId(int streamId)
    {
        base.SetStreamId(streamId);
        return this;
    }

    public override ISpdyDataFrame SetLast(bool last)
    {
        base.SetLast(last);
        return this;
    }

    public virtual ByteBuf Content => ByteBufUtil.EnsureAccessible(data);

    public virtual ISpdyDataFrame Copy()
    {
        return Replace(Content.Copy());
    }

    public virtual ISpdyDataFrame Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public virtual ISpdyDataFrame RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public virtual ISpdyDataFrame Replace(ByteBuf content)
    {
        ISpdyDataFrame frame = new DefaultSpdyDataFrame(StreamId(), content);
        frame.SetLast(IsLast());
        return frame;
    }

    public virtual int RefCnt => data.RefCnt;

    public virtual ISpdyDataFrame Retain()
    {
        data.Retain();
        return this;
    }

    public virtual ISpdyDataFrame Retain(int increment)
    {
        data.Retain(increment);
        return this;
    }

    public virtual ISpdyDataFrame Touch()
    {
        data.Touch();
        return this;
    }

    public virtual ISpdyDataFrame Touch(object hint)
    {
        data.Touch(hint);
        return this;
    }

    public virtual bool Release()
    {
        return data.Release();
    }

    public virtual bool Release(int decrement)
    {
        return data.Release(decrement);
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append("(last: ")
            .Append(IsLast() ? "true" : "false")
            .Append(')')
            .Append(StringUtil.NEWLINE)
            .Append("--> Stream-ID = ")
            .Append(StreamId().ToString(CultureInfo.InvariantCulture))
            .Append(StringUtil.NEWLINE)
            .Append("--> Size = ");
        if (RefCnt == 0)
        {
            buf.Append("(freed)");
        }
        else
        {
            buf.Append(Content.ReadableBytes.ToString(CultureInfo.InvariantCulture));
        }
        return buf.ToString();
    }

    IByteBufHolder IByteBufHolder.Copy() => Copy();
    IByteBufHolder IByteBufHolder.Duplicate() => Duplicate();
    IByteBufHolder IByteBufHolder.RetainedDuplicate() => RetainedDuplicate();
    IByteBufHolder IByteBufHolder.Replace(ByteBuf content) => Replace(content);
    IByteBufHolder IByteBufHolder.Retain() => Retain();
    IByteBufHolder IByteBufHolder.Retain(int increment) => Retain(increment);
    IByteBufHolder IByteBufHolder.Touch() => Touch();
    IByteBufHolder IByteBufHolder.Touch(object hint) => Touch(hint);
    IReferenceCounted IReferenceCounted.Retain() => Retain();
    IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
    IReferenceCounted IReferenceCounted.Touch() => Touch();
    IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);
}
