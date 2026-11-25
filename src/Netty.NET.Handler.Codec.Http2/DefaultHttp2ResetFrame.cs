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

using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * The default {@link Http2ResetFrame} implementation.
 */
// CLR: stream(Http2FrameStream) is a covariant override of AbstractHttp2StreamFrame.Stream. toString() keeps Java's
// text: a null stream prints "null" and numbers use the invariant culture (Http2CodecUtil.ToJavaString).
// errorCode >>> 32 is the C# unsigned right shift.
public sealed class DefaultHttp2ResetFrame : AbstractHttp2StreamFrame, IHttp2ResetFrame
{

    private readonly long errorCode;

    /**
     * Construct a reset message.
     *
     * @param error the non-{@code null} reason for reset
     */
    public DefaultHttp2ResetFrame(Http2Error error)
    {
        errorCode = CheckNotNull(error, "error").Code();
    }

    /**
     * Construct a reset message.
     *
     * @param errorCode the reason for reset
     */
    public DefaultHttp2ResetFrame(long errorCode)
    {
        this.errorCode = errorCode;
    }

    public override DefaultHttp2ResetFrame Stream(IHttp2FrameStream stream)
    {
        base.Stream(stream);
        return this;
    }

    public override string Name()
    {
        return "RST_STREAM";
    }

    public long ErrorCode()
    {
        return errorCode;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(stream=" + ToJavaString(Stream()) + ", errorCode=" +
               ToJavaString(errorCode) + ')';
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttp2ResetFrame))
        {
            return false;
        }
        DefaultHttp2ResetFrame other = (DefaultHttp2ResetFrame) o;
        return base.Equals(o) && errorCode == other.errorCode;
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = hash * 31 + (int) (errorCode ^ errorCode >>> 32);
        return hash;
    }
}
