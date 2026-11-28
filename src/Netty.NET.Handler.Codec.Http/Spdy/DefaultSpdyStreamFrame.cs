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

using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The default {@link SpdyStreamFrame} implementation.
 */
// CLR: Java methods are virtual unless final, so the setters that subclasses narrow are virtual; the subclasses narrow
// them with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), and the interface members
// keep mapping to these virtual methods.
public abstract class DefaultSpdyStreamFrame : ISpdyStreamFrame
{

    private int streamId;
    private bool last;

    /**
     * Creates a new instance.
     *
     * @param streamId the Stream-ID of this frame
     */
    protected DefaultSpdyStreamFrame(int streamId)
    {
        SetStreamId(streamId);
    }

    public virtual int StreamId()
    {
        return streamId;
    }

    public virtual ISpdyStreamFrame SetStreamId(int streamId)
    {
        CheckPositive(streamId, "streamId");

        this.streamId = streamId;
        return this;
    }

    public virtual bool IsLast()
    {
        return last;
    }

    public virtual ISpdyStreamFrame SetLast(bool last)
    {
        this.last = last;
        return this;
    }
}
