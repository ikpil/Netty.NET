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
 * The default {@link Http2HeadersFrame} implementation.
 */
// CLR: stream(Http2FrameStream) is a covariant override of AbstractHttp2StreamFrame.Stream. toString() keeps Java's
// text: a null stream prints "null" and booleans print true/false (Http2CodecUtil.ToJavaString).
public sealed class DefaultHttp2HeadersFrame : AbstractHttp2StreamFrame, IHttp2HeadersFrame
{
    private readonly IHttp2Headers headers;
    private readonly bool endStream;
    private readonly int padding;

    /**
     * Equivalent to {@code new DefaultHttp2HeadersFrame(headers, false)}.
     *
     * @param headers the non-{@code null} headers to send
     */
    public DefaultHttp2HeadersFrame(IHttp2Headers headers) : this(headers, false)
    {
    }

    /**
     * Equivalent to {@code new DefaultHttp2HeadersFrame(headers, endStream, 0)}.
     *
     * @param headers the non-{@code null} headers to send
     */
    public DefaultHttp2HeadersFrame(IHttp2Headers headers, bool endStream) : this(headers, endStream, 0)
    {
    }

    /**
     * Construct a new headers message.
     *
     * @param headers the non-{@code null} headers to send
     * @param endStream whether these headers should terminate the stream
     * @param padding additional bytes that should be added to obscure the true content size. Must be between 0 and
     *                256 (inclusive).
     */
    public DefaultHttp2HeadersFrame(IHttp2Headers headers, bool endStream, int padding)
    {
        this.headers = CheckNotNull(headers, "headers");
        this.endStream = endStream;
        VerifyPadding(padding);
        this.padding = padding;
    }

    public override DefaultHttp2HeadersFrame Stream(IHttp2FrameStream stream)
    {
        base.Stream(stream);
        return this;
    }

    public override string Name()
    {
        return "HEADERS";
    }

    public IHttp2Headers Headers()
    {
        return headers;
    }

    public bool IsEndStream()
    {
        return endStream;
    }

    public int Padding()
    {
        return padding;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(stream=" + ToJavaString(Stream()) + ", headers=" + headers
               + ", endStream=" + ToJavaString(endStream) + ", padding=" + ToJavaString(padding) + ')';
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttp2HeadersFrame))
        {
            return false;
        }
        DefaultHttp2HeadersFrame other = (DefaultHttp2HeadersFrame) o;
        return base.Equals(other) && headers.Equals(other.headers)
                && endStream == other.endStream && padding == other.padding;
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = hash * 31 + headers.GetHashCode();
        hash = hash * 31 + (endStream ? 0 : 1);
        hash = hash * 31 + padding;
        return hash;
    }
}
