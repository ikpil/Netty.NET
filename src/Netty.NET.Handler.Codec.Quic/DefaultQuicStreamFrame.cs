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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Quic;

// CLR: DefaultByteBufHolder is the Buffer port's DefaultByteBufHolder, whose content() is the Content property and
// contentToString() ContentToString(). The overrides that narrow the IByteBufHolder results to QuicStreamFrame (copy,
// duplicate, retainedDuplicate, replace, retain, touch) are covariant class overrides, as codec-haproxy HAProxyTLV
// narrows them (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override). equals/hashCode are
// Equals/GetHashCode with the Java arithmetic; getClass() is GetType(). toString() keeps Java's boolean text (true/false).
public sealed class DefaultQuicStreamFrame : DefaultByteBufHolder, IQuicStreamFrame
{

    private readonly bool fin;

    public DefaultQuicStreamFrame(ByteBuf data, bool fin) : base(data)
    {
        this.fin = fin;
    }

    public bool HasFin()
    {
        return fin;
    }

    public override IQuicStreamFrame Copy()
    {
        return new DefaultQuicStreamFrame(Content.Copy(), fin);
    }

    public override IQuicStreamFrame Duplicate()
    {
        return new DefaultQuicStreamFrame(Content.Duplicate(), fin);
    }

    public override IQuicStreamFrame RetainedDuplicate()
    {
        return new DefaultQuicStreamFrame(Content.RetainedDuplicate(), fin);
    }

    public override IQuicStreamFrame Replace(ByteBuf content)
    {
        return new DefaultQuicStreamFrame(content, fin);
    }

    public override IQuicStreamFrame Retain()
    {
        base.Retain();
        return this;
    }

    public override IQuicStreamFrame Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IQuicStreamFrame Touch()
    {
        base.Touch();
        return this;
    }

    public override IQuicStreamFrame Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override string ToString()
    {
        return "DefaultQuicStreamFrame{" +
                "fin=" + (fin ? "true" : "false") +
                ", content=" + ContentToString() +
                '}';
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
        DefaultQuicStreamFrame that = (DefaultQuicStreamFrame) o;

        if (fin != that.fin)
        {
            return false;
        }

        return base.Equals(o);
    }

    public override int GetHashCode()
    {
        int result = base.GetHashCode();
        result = unchecked(31 * result + (fin ? 1 : 0));
        return result;
    }
}
