/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * The priority of a {@link QuicStreamChannel}.
 */
// CLR: Byte.MAX_VALUE is sbyte.MaxValue (127, the signed Java byte). Objects.hash(urgency, incremental) is spelled
// out (31 * (31 + urgency) + Boolean.hashCode) to keep the Java value; toString() keeps Java's boolean text and
// the invariant culture. System.Net.Quic has no stream priorities : the type keeps its Java API and the msquic
// implementation (stage 2) reports updatePriority as unsupported.
public sealed class QuicStreamPriority
{

    private readonly int urgency;
    private readonly bool incremental;

    /**
     * Create a new instance
     *
     * @param urgency       the urgency of the stream.
     * @param incremental   {@code true} if incremental.
     */
    public QuicStreamPriority(int urgency, bool incremental)
    {
        this.urgency = ObjectUtil.CheckInRange(urgency, 0, sbyte.MaxValue, "urgency");
        this.incremental = incremental;
    }

    /**
     * The urgency of the stream. Smaller number means more urgent and so data will be send earlier.
     *
     * @return  the urgency.
     */
    public int Urgency()
    {
        return urgency;
    }

    /**
     * {@code true} if incremental, {@code false} otherwise.
     *
     * @return  if incremental.
     */
    public bool IsIncremental()
    {
        return incremental;
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        QuicStreamPriority that = (QuicStreamPriority) o;
        return urgency == that.urgency && incremental == that.incremental;
    }

    public override int GetHashCode()
    {
        return unchecked(31 * (31 + urgency) + (incremental ? 1231 : 1237));
    }

    public override string ToString()
    {
        return "QuicStreamPriority{" +
                "urgency=" + urgency.ToString(CultureInfo.InvariantCulture) +
                ", incremental=" + (incremental ? "true" : "false") +
                '}';
    }
}
