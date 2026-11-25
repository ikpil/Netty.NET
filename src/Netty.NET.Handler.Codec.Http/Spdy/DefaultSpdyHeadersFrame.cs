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

using System.Globalization;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdyHeadersFrame} implementation.
 */
// CLR: setStreamId/setLast narrow the base class members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), and the other Java methods
// are virtual (Java methods are virtual unless final). Map.Entry<CharSequence, CharSequence> is the codec-base
// IEntry<ICharSequence, ICharSequence> of the headers' enumeration (docs/codec-base-porting.md, Headers).
// StringBuilder.setLength is the Length setter; a boolean is appended as Java's "true"/"false" and numbers with the
// invariant culture.
public class DefaultSpdyHeadersFrame : DefaultSpdyStreamFrame, ISpdyHeadersFrame
{

    private bool invalid;
    private bool truncated;
    private readonly ISpdyHeaders headers;

    /**
     * Creates a new instance.
     *
     * @param streamId the Stream-ID of this frame
     */
    public DefaultSpdyHeadersFrame(int streamId) : this(streamId, true)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param streamId the Stream-ID of this frame
     * @param validate validate the header names and values when adding them to the {@link SpdyHeaders}
     */
    public DefaultSpdyHeadersFrame(int streamId, bool validate) : base(streamId)
    {
        headers = new DefaultSpdyHeaders(validate);
    }

    public override ISpdyHeadersFrame SetStreamId(int streamId)
    {
        base.SetStreamId(streamId);
        return this;
    }

    public override ISpdyHeadersFrame SetLast(bool last)
    {
        base.SetLast(last);
        return this;
    }

    public virtual bool IsInvalid()
    {
        return invalid;
    }

    public virtual ISpdyHeadersFrame SetInvalid()
    {
        invalid = true;
        return this;
    }

    public virtual bool IsTruncated()
    {
        return truncated;
    }

    public virtual ISpdyHeadersFrame SetTruncated()
    {
        truncated = true;
        return this;
    }

    public virtual ISpdyHeaders Headers()
    {
        return headers;
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
            .Append("--> Headers:")
            .Append(StringUtil.NEWLINE);
        AppendHeaders(buf);

        // Remove the last newline.
        buf.Length = buf.Length - StringUtil.NEWLINE.Length;
        return buf.ToString();
    }

    protected virtual void AppendHeaders(StringBuilder buf)
    {
        foreach (IEntry<ICharSequence, ICharSequence> e in Headers())
        {
            buf.Append("    ");
            buf.Append(e.GetKey());
            buf.Append(": ");
            buf.Append(e.GetValue());
            buf.Append(StringUtil.NEWLINE);
        }
    }
}
