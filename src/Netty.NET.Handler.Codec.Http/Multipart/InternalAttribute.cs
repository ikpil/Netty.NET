/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * This Attribute is only for Encoder use to insert special command between object if needed
 * (like Multipart Mixed mode)
 */
// CLR: the package-private final class is internal sealed. ArrayList is List<T>: add(rank, buf) is Insert, and
// set(rank, buf), which returns the old element, is the indexer after reading the old element. Charset is Encoding and
// Unpooled.copiedBuffer(String, Charset) is CopiedBuffer(string, Encoding). hashCode()/equals(Object)/toString() are
// GetHashCode()/Equals(object)/ToString(); String.equalsIgnoreCase/compareToIgnoreCase are string.Equals/string.Compare
// with StringComparison.OrdinalIgnoreCase and ClassCastException is InvalidCastException (as MemoryAttribute).
// compositeBuffer().addComponents(value).writerIndex(size()).readerIndex(0) is AddComponents(value) followed by the
// WriterIndex and ReaderIndex property assignments. retain()/touch() narrow the AbstractReferenceCounted members with
// covariant override returns (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override).
internal sealed class InternalAttribute : AbstractReferenceCounted, IInterfaceHttpData
{
    private readonly List<ByteBuf> value = new List<ByteBuf>();
    private readonly Encoding charset;
    private int size;

    internal InternalAttribute(Encoding charset)
    {
        this.charset = charset;
    }

    public IInterfaceHttpData.HttpDataType GetHttpDataType()
    {
        return IInterfaceHttpData.HttpDataType.InternalAttribute;
    }

    public void AddValue(string value)
    {
        ObjectUtil.CheckNotNull(value, "value");

        ByteBuf buf = Unpooled.CopiedBuffer(value, charset);
        this.value.Add(buf);
        size += buf.ReadableBytes;
    }

    public void AddValue(string value, int rank)
    {
        ObjectUtil.CheckNotNull(value, "value");

        ByteBuf buf = Unpooled.CopiedBuffer(value, charset);
        this.value.Insert(rank, buf);
        size += buf.ReadableBytes;
    }

    public void SetValue(string value, int rank)
    {
        ObjectUtil.CheckNotNull(value, "value");

        ByteBuf buf = Unpooled.CopiedBuffer(value, charset);
        ByteBuf old = this.value[rank];
        this.value[rank] = buf;

        if (old != null)
        {
            size -= old.ReadableBytes;
            old.Release();
        }

        size += buf.ReadableBytes;
    }

    public override int GetHashCode()
    {
        return GetName().GetHashCode();
    }

    public override bool Equals(object o)
    {
        if (!(o is InternalAttribute))
        {
            return false;
        }

        InternalAttribute attribute = (InternalAttribute) o;
        return string.Equals(GetName(), attribute.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    public int CompareTo(IInterfaceHttpData o)
    {
        if (!(o is InternalAttribute))
        {
            throw new InvalidCastException("Cannot compare " + GetHttpDataType() +
                    " with " + o.GetHttpDataType());
        }

        return CompareTo((InternalAttribute) o);
    }

    public int CompareTo(InternalAttribute o)
    {
        return string.Compare(GetName(), o.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();

        foreach (ByteBuf elt in value)
        {
            result.Append(elt.ToString(charset));
        }

        return result.ToString();
    }

    public int Size()
    {
        return size;
    }

    public ByteBuf ToByteBuf()
    {
        CompositeByteBuf buf = Unpooled.CompositeBuffer().AddComponents(value);
        buf.WriterIndex = Size();
        buf.ReaderIndex = 0;
        return buf;
    }

    public string GetName()
    {
        return "InternalAttribute";
    }

    protected override void Deallocate()
    {
        // Do nothing
    }

    public override IInterfaceHttpData Retain()
    {
        foreach (ByteBuf buf in value)
        {
            buf.Retain();
        }

        return this;
    }

    public override IInterfaceHttpData Retain(int increment)
    {
        foreach (ByteBuf buf in value)
        {
            buf.Retain(increment);
        }

        return this;
    }

    public override IInterfaceHttpData Touch()
    {
        foreach (ByteBuf buf in value)
        {
            buf.Touch();
        }

        return this;
    }

    public override IInterfaceHttpData Touch(object hint)
    {
        foreach (ByteBuf buf in value)
        {
            buf.Touch(hint);
        }

        return this;
    }
}
