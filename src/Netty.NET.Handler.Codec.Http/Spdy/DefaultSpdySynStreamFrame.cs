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
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdySynStreamFrame} implementation.
 */
// CLR: setStreamId/setLast/setInvalid narrow the base class members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). The Java byte priority
// is the CLR byte; setPriority keeps the Java check (a negative Java byte is 0x80-0xFF here, which is also above
// 7) and prints the signed Java value in the exception text through (sbyte) (rule 6). IllegalArgumentException is
// ArgumentException. A boolean is appended as Java's "true"/"false" and numbers with the invariant culture;
// StringBuilder.setLength is the Length setter.
public class DefaultSpdySynStreamFrame : DefaultSpdyHeadersFrame, ISpdySynStreamFrame
{

    private int associatedStreamId;
    private byte priority;
    private bool unidirectional;

    /**
     * Creates a new instance.
     *
     * @param streamId           the Stream-ID of this frame
     * @param associatedStreamId the Associated-To-Stream-ID of this frame
     * @param priority           the priority of the stream
     */
    public DefaultSpdySynStreamFrame(int streamId, int associatedStreamId, byte priority)
        : this(streamId, associatedStreamId, priority, true)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param streamId           the Stream-ID of this frame
     * @param associatedStreamId the Associated-To-Stream-ID of this frame
     * @param priority           the priority of the stream
     * @param validateHeaders    validate the header names and values when adding them to the {@link SpdyHeaders}
     */
    public DefaultSpdySynStreamFrame(int streamId, int associatedStreamId, byte priority, bool validateHeaders)
        : base(streamId, validateHeaders)
    {
        SetAssociatedStreamId(associatedStreamId);
        SetPriority(priority);
    }

    public override ISpdySynStreamFrame SetStreamId(int streamId)
    {
        base.SetStreamId(streamId);
        return this;
    }

    public override ISpdySynStreamFrame SetLast(bool last)
    {
        base.SetLast(last);
        return this;
    }

    public override ISpdySynStreamFrame SetInvalid()
    {
        base.SetInvalid();
        return this;
    }

    public virtual int AssociatedStreamId()
    {
        return associatedStreamId;
    }

    public virtual ISpdySynStreamFrame SetAssociatedStreamId(int associatedStreamId)
    {
        CheckPositiveOrZero(associatedStreamId, "associatedStreamId");

        this.associatedStreamId = associatedStreamId;
        return this;
    }

    public virtual byte Priority()
    {
        return priority;
    }

    public virtual ISpdySynStreamFrame SetPriority(byte priority)
    {
        if (unchecked((sbyte) priority) < 0 || priority > 7)
        {
            throw new ArgumentException(
                    "Priority must be between 0 and 7 inclusive: "
                    + unchecked((sbyte) priority).ToString(CultureInfo.InvariantCulture));
        }

        this.priority = priority;
        return this;
    }

    public virtual bool IsUnidirectional()
    {
        return unidirectional;
    }

    public virtual ISpdySynStreamFrame SetUnidirectional(bool unidirectional)
    {
        this.unidirectional = unidirectional;
        return this;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append("(last: ")
            .Append(IsLast() ? "true" : "false")
            .Append("; unidirectional: ")
            .Append(IsUnidirectional() ? "true" : "false")
            .Append(')')
            .Append(StringUtil.NEWLINE)
            .Append("--> Stream-ID = ")
            .Append(StreamId().ToString(CultureInfo.InvariantCulture))
            .Append(StringUtil.NEWLINE);

        if (associatedStreamId != 0)
        {
            buf.Append("--> Associated-To-Stream-ID = ")
               .Append(AssociatedStreamId().ToString(CultureInfo.InvariantCulture))
               .Append(StringUtil.NEWLINE);
        }

        buf.Append("--> Priority = ")
           .Append(Priority().ToString(CultureInfo.InvariantCulture))
           .Append(StringUtil.NEWLINE)
           .Append("--> Headers:")
           .Append(StringUtil.NEWLINE);
        AppendHeaders(buf);

        // Remove the last newline.
        buf.Length = buf.Length - StringUtil.NEWLINE.Length;
        return buf.ToString();
    }
}
