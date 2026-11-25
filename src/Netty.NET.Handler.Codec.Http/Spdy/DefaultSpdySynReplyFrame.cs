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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdySynReplyFrame} implementation.
 */
// CLR: setStreamId/setLast/setInvalid narrow the base class members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). A boolean is appended as
// Java's "true"/"false" and numbers with the invariant culture; StringBuilder.setLength is the Length setter.
public class DefaultSpdySynReplyFrame : DefaultSpdyHeadersFrame, ISpdySynReplyFrame
{

    /**
     * Creates a new instance.
     *
     * @param streamId the Stream-ID of this frame
     */
    public DefaultSpdySynReplyFrame(int streamId) : base(streamId)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param streamId        the Stream-ID of this frame
     * @param validateHeaders validate the header names and values when adding them to the {@link SpdyHeaders}
     */
    public DefaultSpdySynReplyFrame(int streamId, bool validateHeaders) : base(streamId, validateHeaders)
    {
    }

    public override ISpdySynReplyFrame SetStreamId(int streamId)
    {
        base.SetStreamId(streamId);
        return this;
    }

    public override ISpdySynReplyFrame SetLast(bool last)
    {
        base.SetLast(last);
        return this;
    }

    public override ISpdySynReplyFrame SetInvalid()
    {
        base.SetInvalid();
        return this;
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
}
