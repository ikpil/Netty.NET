/*
 * Copyright 2016 The Netty Project
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

using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * The default {@link Http2PingFrame} implementation.
 */
// CLR: the non-final Java methods are virtual; toString() keeps Java's text (invariant number, true/false;
// Http2CodecUtil.ToJavaString). content >>> 32 is the C# unsigned right shift.
public class DefaultHttp2PingFrame : IHttp2PingFrame
{

    private readonly long content;
    private readonly bool ack;

    public DefaultHttp2PingFrame(long content) : this(content, false)
    {
    }

    public DefaultHttp2PingFrame(long content, bool ack)
    {
        this.content = content;
        this.ack = ack;
    }

    public virtual bool Ack()
    {
        return ack;
    }

    public virtual string Name()
    {
        return "PING";
    }

    public virtual long Content()
    {
        return content;
    }

    public override bool Equals(object o)
    {
        if (!(o is IHttp2PingFrame))
        {
            return false;
        }
        IHttp2PingFrame other = (IHttp2PingFrame) o;
        return ack == other.Ack() &&  content == other.Content();
    }

    public override int GetHashCode()
    {
        // Must be consistent with equals; super.hashCode() is Object's identity hash.
        int hash = (int) (content ^ content >>> 32);
        hash = hash * 31 + (ack ? 1 : 0);
        return hash;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(content=" + ToJavaString(content) + ", ack=" + ToJavaString(ack) + ')';
    }
}
