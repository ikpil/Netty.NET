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
 * The default {@link SpdyRstStreamFrame} implementation.
 */
// CLR: setStreamId/setLast narrow the base class members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); the other Java methods are
// virtual. Numbers are appended with the invariant culture.
public class DefaultSpdyRstStreamFrame : DefaultSpdyStreamFrame, ISpdyRstStreamFrame
{

    private SpdyStreamStatus status;

    /**
     * Creates a new instance.
     *
     * @param streamId   the Stream-ID of this frame
     * @param statusCode the Status code of this frame
     */
    public DefaultSpdyRstStreamFrame(int streamId, int statusCode)
        : this(streamId, SpdyStreamStatus.ValueOf(statusCode))
    {
    }

    /**
     * Creates a new instance.
     *
     * @param streamId the Stream-ID of this frame
     * @param status   the status of this frame
     */
    public DefaultSpdyRstStreamFrame(int streamId, SpdyStreamStatus status) : base(streamId)
    {
        SetStatus(status);
    }

    public override ISpdyRstStreamFrame SetStreamId(int streamId)
    {
        base.SetStreamId(streamId);
        return this;
    }

    public override ISpdyRstStreamFrame SetLast(bool last)
    {
        base.SetLast(last);
        return this;
    }

    public virtual SpdyStreamStatus Status()
    {
        return status;
    }

    public virtual ISpdyRstStreamFrame SetStatus(SpdyStreamStatus status)
    {
        this.status = status;
        return this;
    }

    public override string ToString()
    {
        return new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append(StringUtil.NEWLINE)
            .Append("--> Stream-ID = ")
            .Append(StreamId().ToString(CultureInfo.InvariantCulture))
            .Append(StringUtil.NEWLINE)
            .Append("--> Status: ")
            .Append(Status())
            .ToString();
    }
}
