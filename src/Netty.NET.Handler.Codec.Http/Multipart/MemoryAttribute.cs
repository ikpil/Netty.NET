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
using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Memory implementation of Attributes
 */
// CLR: Charset is Encoding; String.getBytes(Charset) is Encoding.GetBytes and ByteBuf.toString(Charset) is
// ToString(Encoding). hashCode()/equals(Object)/toString() are GetHashCode()/Equals(object)/ToString();
// String.equalsIgnoreCase is string.Equals and compareToIgnoreCase string.Compare with StringComparison.OrdinalIgnoreCase
// (as FileUploadUtil). ClassCastException is InvalidCastException, "The exception that is thrown for invalid casting or
// explicit conversion" (https://learn.microsoft.com/en-us/dotnet/api/system.invalidcastexception?view=net-10.0), and
// the HttpDataType text is the C# enum name, as Enum.toString() is the Java constant name. copy()/duplicate()/
// retainedDuplicate()/replace(ByteBuf)/retain()/touch() narrow the base members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); content() is the Content
// property and ChannelException the Transport ChannelException.
public class MemoryAttribute : AbstractMemoryHttpData, IAttribute
{

    public MemoryAttribute(string name) : this(name, HttpConstants.DEFAULT_CHARSET)
    {
    }

    public MemoryAttribute(string name, long definedSize) : this(name, definedSize, HttpConstants.DEFAULT_CHARSET)
    {
    }

    public MemoryAttribute(string name, Encoding charset) : base(name, charset, 0)
    {
    }

    public MemoryAttribute(string name, long definedSize, Encoding charset) : base(name, charset, definedSize)
    {
    }

    public MemoryAttribute(string name, string value) : this(name, value, HttpConstants.DEFAULT_CHARSET) // Attribute have no default size
    {
    }

    public MemoryAttribute(string name, string value, Encoding charset) : base(name, charset, 0) // Attribute have no default size
    {
        SetValue(value);
    }

    public override IInterfaceHttpData.HttpDataType GetHttpDataType()
    {
        return IInterfaceHttpData.HttpDataType.Attribute;
    }

    public virtual string GetValue()
    {
        return GetByteBuf().ToString(GetCharset());
    }

    public virtual void SetValue(string value)
    {
        ObjectUtil.CheckNotNull(value, "value");
        byte [] bytes = GetCharset().GetBytes(value);
        CheckSize(bytes.Length);
        ByteBuf buffer = Unpooled.WrappedBuffer(bytes);
        if (definedSize > 0)
        {
            definedSize = buffer.ReadableBytes;
        }
        SetContent(buffer);
    }

    public override void AddContent(ByteBuf buffer, bool last)
    {
        int localsize = buffer.ReadableBytes;
        try
        {
            CheckSize(size + localsize);
        }
        catch (IOException)
        {
            buffer.Release();
            throw;
        }
        if (definedSize > 0 && definedSize < size + localsize)
        {
            definedSize = size + localsize;
        }
        base.AddContent(buffer, last);
    }

    public override int GetHashCode()
    {
        return GetName().GetHashCode();
    }

    public override bool Equals(object o)
    {
        if (!(o is IAttribute))
        {
            return false;
        }
        IAttribute attribute = (IAttribute) o;
        return string.Equals(GetName(), attribute.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    public override int CompareTo(IInterfaceHttpData other)
    {
        if (!(other is IAttribute))
        {
            throw new InvalidCastException("Cannot compare " + GetHttpDataType() +
                    " with " + other.GetHttpDataType());
        }
        return CompareTo((IAttribute) other);
    }

    public virtual int CompareTo(IAttribute o)
    {
        return string.Compare(GetName(), o.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString()
    {
        return GetName() + '=' + GetValue();
    }

    public override IAttribute Copy()
    {
        ByteBuf content = Content;
        return Replace(content != null ? content.Copy() : null);
    }

    public override IAttribute Duplicate()
    {
        ByteBuf content = Content;
        return Replace(content != null ? content.Duplicate() : null);
    }

    public override IAttribute RetainedDuplicate()
    {
        ByteBuf content = Content;
        if (content != null)
        {
            content = content.RetainedDuplicate();
            bool success = false;
            try
            {
                IAttribute duplicate = Replace(content);
                success = true;
                return duplicate;
            }
            finally
            {
                if (!success)
                {
                    content.Release();
                }
            }
        }
        else
        {
            return Replace(null);
        }
    }

    public override IAttribute Replace(ByteBuf content)
    {
        MemoryAttribute attr = new MemoryAttribute(GetName());
        attr.SetCharset(GetCharset());
        if (content != null)
        {
            try
            {
                attr.SetContent(content);
            }
            catch (IOException e)
            {
                throw new ChannelException(e);
            }
        }
        attr.SetCompleted(IsCompleted());
        return attr;
    }

    public override IAttribute Retain()
    {
        base.Retain();
        return this;
    }

    public override IAttribute Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IAttribute Touch()
    {
        base.Touch();
        return this;
    }

    public override IAttribute Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
