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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * The default {@link Http2GoAwayFrame} implementation.
 */
// CLR: DefaultByteBufHolder is the Buffer port's DefaultByteBufHolder; the overrides that narrow its return types to
// Http2GoAwayFrame are C# 9 covariant overrides returning IHttp2GoAwayFrame (as codec-smtp's DefaultSmtpContent;
// docs/codec-http-porting.md, WebSocket frames), and content() is the Content property (docs/buffer-porting.md,
// Buffer holders and conversion). The package-private constructor is internal. toString() keeps Java's text
// (invariant numbers; Http2CodecUtil.ToJavaString). errorCode >>> 32 is the C# unsigned right shift.
public sealed class DefaultHttp2GoAwayFrame : DefaultByteBufHolder, IHttp2GoAwayFrame
{

    private readonly long errorCode;
    private readonly int lastStreamId;
    private int extraStreamIds;

    /**
     * Equivalent to {@code new DefaultHttp2GoAwayFrame(error.code())}.
     *
     * @param error non-{@code null} reason for the go away
     */
    public DefaultHttp2GoAwayFrame(Http2Error error) : this(error.Code())
    {
    }

    /**
     * Equivalent to {@code new DefaultHttp2GoAwayFrame(content, Unpooled.EMPTY_BUFFER)}.
     *
     * @param errorCode reason for the go away
     */
    public DefaultHttp2GoAwayFrame(long errorCode) : this(errorCode, Unpooled.EmptyBuffer)
    {
    }

    /**
     *
     *
     * @param error non-{@code null} reason for the go away
     * @param content non-{@code null} debug data
     */
    public DefaultHttp2GoAwayFrame(Http2Error error, ByteBuf content) : this(error.Code(), content)
    {
    }

    /**
     * Construct a new GOAWAY message.
     *
     * @param errorCode reason for the go away
     * @param content non-{@code null} debug data
     */
    public DefaultHttp2GoAwayFrame(long errorCode, ByteBuf content) : this(-1, errorCode, content)
    {
    }

    /**
     * Construct a new GOAWAY message.
     *
     * This constructor is for internal use only. A user should not have to specify a specific last stream identifier,
     * but use {@link #setExtraStreamIds(int)} instead.
     */
    internal DefaultHttp2GoAwayFrame(int lastStreamId, long errorCode, ByteBuf content) : base(content)
    {
        this.errorCode = errorCode;
        this.lastStreamId = lastStreamId;
    }

    public string Name()
    {
        return "GOAWAY";
    }

    public long ErrorCode()
    {
        return errorCode;
    }

    public int ExtraStreamIds()
    {
        return extraStreamIds;
    }

    public IHttp2GoAwayFrame SetExtraStreamIds(int extraStreamIds)
    {
        CheckPositiveOrZero(extraStreamIds, "extraStreamIds");
        this.extraStreamIds = extraStreamIds;
        return this;
    }

    public int LastStreamId()
    {
        return lastStreamId;
    }

    public override IHttp2GoAwayFrame Copy()
    {
        return new DefaultHttp2GoAwayFrame(lastStreamId, errorCode, Content.Copy());
    }

    public override IHttp2GoAwayFrame Duplicate()
    {
        return (IHttp2GoAwayFrame) base.Duplicate();
    }

    public override IHttp2GoAwayFrame RetainedDuplicate()
    {
        return (IHttp2GoAwayFrame) base.RetainedDuplicate();
    }

    public override IHttp2GoAwayFrame Replace(ByteBuf content)
    {
        return new DefaultHttp2GoAwayFrame(errorCode, content).SetExtraStreamIds(extraStreamIds);
    }

    public override IHttp2GoAwayFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override IHttp2GoAwayFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IHttp2GoAwayFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override IHttp2GoAwayFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttp2GoAwayFrame))
        {
            return false;
        }
        DefaultHttp2GoAwayFrame other = (DefaultHttp2GoAwayFrame) o;
        return errorCode == other.errorCode && extraStreamIds == other.extraStreamIds && base.Equals(other);
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = hash * 31 + (int) (errorCode ^ errorCode >>> 32);
        hash = hash * 31 + extraStreamIds;
        return hash;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(errorCode=" + ToJavaString(errorCode) + ", content=" + Content
               + ", extraStreamIds=" + ToJavaString(extraStreamIds) + ", lastStreamId=" + ToJavaString(lastStreamId) + ')';
    }
}
