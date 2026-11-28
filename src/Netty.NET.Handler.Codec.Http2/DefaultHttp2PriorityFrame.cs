/*
 * Copyright 2020 The Netty Project
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

using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Default implementation of {@linkplain Http2PriorityFrame}
 */
// CLR: stream(Http2FrameStream) is a covariant override of AbstractHttp2StreamFrame.Stream. toString() keeps Java's
// text: a null stream prints "null", numbers use the invariant culture and booleans print true/false
// (Http2CodecUtil.ToJavaString).
public sealed class DefaultHttp2PriorityFrame : AbstractHttp2StreamFrame, IHttp2PriorityFrame
{

    private readonly int streamDependency;
    private readonly short weight;
    private readonly bool exclusive;

    public DefaultHttp2PriorityFrame(int streamDependency, short weight, bool exclusive)
    {
        this.streamDependency = streamDependency;
        this.weight = weight;
        this.exclusive = exclusive;
    }

    public int StreamDependency()
    {
        return streamDependency;
    }

    public short Weight()
    {
        return weight;
    }

    public bool Exclusive()
    {
        return exclusive;
    }

    public override DefaultHttp2PriorityFrame Stream(IHttp2FrameStream stream)
    {
        base.Stream(stream);
        return this;
    }

    public override string Name()
    {
        return "PRIORITY_FRAME";
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttp2PriorityFrame))
        {
            return false;
        }

        DefaultHttp2PriorityFrame other = (DefaultHttp2PriorityFrame) o;
        bool same = base.Equals(other);
        return same && streamDependency == other.streamDependency
                && weight == other.weight && exclusive == other.exclusive;
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = hash * 31 + streamDependency;
        hash = hash * 31 + weight;
        hash = hash * 31 + (exclusive ? 1 : 0);
        return hash;
    }

    public override string ToString()
    {
        return "DefaultHttp2PriorityFrame(" +
                "stream=" + ToJavaString(Stream()) +
                ", streamDependency=" + ToJavaString(streamDependency) +
                ", weight=" + ToJavaString(weight) +
                ", exclusive=" + ToJavaString(exclusive) +
                ')';
    }
}
