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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdyGoAwayFrame} implementation.
 */
// CLR: Java methods are virtual unless final; numbers are appended with the invariant culture.
public class DefaultSpdyGoAwayFrame : ISpdyGoAwayFrame
{

    private int lastGoodStreamId;
    private SpdySessionStatus status;

    /**
     * Creates a new instance.
     *
     * @param lastGoodStreamId the Last-good-stream-ID of this frame
     */
    public DefaultSpdyGoAwayFrame(int lastGoodStreamId) : this(lastGoodStreamId, 0)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param lastGoodStreamId the Last-good-stream-ID of this frame
     * @param statusCode       the Status code of this frame
     */
    public DefaultSpdyGoAwayFrame(int lastGoodStreamId, int statusCode)
        : this(lastGoodStreamId, SpdySessionStatus.ValueOf(statusCode))
    {
    }

    /**
     * Creates a new instance.
     *
     * @param lastGoodStreamId the Last-good-stream-ID of this frame
     * @param status           the status of this frame
     */
    public DefaultSpdyGoAwayFrame(int lastGoodStreamId, SpdySessionStatus status)
    {
        SetLastGoodStreamId(lastGoodStreamId);
        SetStatus(status);
    }

    public virtual int LastGoodStreamId()
    {
        return lastGoodStreamId;
    }

    public virtual ISpdyGoAwayFrame SetLastGoodStreamId(int lastGoodStreamId)
    {
        CheckPositiveOrZero(lastGoodStreamId, "lastGoodStreamId");
        this.lastGoodStreamId = lastGoodStreamId;
        return this;
    }

    public virtual SpdySessionStatus Status()
    {
        return status;
    }

    public virtual ISpdyGoAwayFrame SetStatus(SpdySessionStatus status)
    {
        this.status = status;
        return this;
    }

    public override string ToString()
    {
        return new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append(StringUtil.NEWLINE)
            .Append("--> Last-good-stream-ID = ")
            .Append(LastGoodStreamId().ToString(CultureInfo.InvariantCulture))
            .Append(StringUtil.NEWLINE)
            .Append("--> Status: ")
            .Append(Status())
            .ToString();
    }
}
