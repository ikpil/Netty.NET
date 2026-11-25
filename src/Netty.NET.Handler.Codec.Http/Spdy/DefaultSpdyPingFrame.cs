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
 * The default {@link SpdyPingFrame} implementation.
 */
// CLR: Java methods are virtual unless final; numbers are appended with the invariant culture.
public class DefaultSpdyPingFrame : ISpdyPingFrame
{

    private int id;

    /**
     * Creates a new instance.
     *
     * @param id the unique ID of this frame
     */
    public DefaultSpdyPingFrame(int id)
    {
        SetId(id);
    }

    public virtual int Id()
    {
        return id;
    }

    public virtual ISpdyPingFrame SetId(int id)
    {
        this.id = id;
        return this;
    }

    public override string ToString()
    {
        return new StringBuilder()
            .Append(StringUtil.SimpleClassName(this))
            .Append(StringUtil.NEWLINE)
            .Append("--> ID = ")
            .Append(Id().ToString(CultureInfo.InvariantCulture))
            .ToString();
    }
}
