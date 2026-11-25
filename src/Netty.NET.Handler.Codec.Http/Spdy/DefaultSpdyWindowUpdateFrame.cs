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
 * The default {@link SpdyWindowUpdateFrame} implementation.
 */
// CLR: Java methods are virtual unless final; numbers are appended with the invariant culture.
public class DefaultSpdyWindowUpdateFrame : ISpdyWindowUpdateFrame
{

    private int streamId;
    private int deltaWindowSize;

    /**
     * Creates a new instance.
     *
     * @param streamId        the Stream-ID of this frame
     * @param deltaWindowSize the Delta-Window-Size of this frame
     */
    public DefaultSpdyWindowUpdateFrame(int streamId, int deltaWindowSize)
    {
        SetStreamId(streamId);
        SetDeltaWindowSize(deltaWindowSize);
    }

    public virtual int StreamId()
    {
        return streamId;
    }

    public virtual ISpdyWindowUpdateFrame SetStreamId(int streamId)
    {
        CheckPositiveOrZero(streamId, "streamId");
        this.streamId = streamId;
        return this;
    }

    public virtual int DeltaWindowSize()
    {
        return deltaWindowSize;
    }

    public virtual ISpdyWindowUpdateFrame SetDeltaWindowSize(int deltaWindowSize)
    {
        CheckPositive(deltaWindowSize, "deltaWindowSize");
        this.deltaWindowSize = deltaWindowSize;
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
            .Append("--> Delta-Window-Size = ")
            .Append(DeltaWindowSize().ToString(CultureInfo.InvariantCulture))
            .ToString();
    }
}
