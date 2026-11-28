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
 * Disk implementation of Attributes
 */
// CLR: the public static fields keep their Java names (baseDirectory and deleteOnExitTemporaryFile stay mutable static
// fields; the static final prefix/postfix are constants). Charset is Encoding; String.getBytes(Charset) is
// Encoding.GetBytes and new String(bytes, charset) Encoding.GetString. hashCode()/equals(Object)/toString() are
// GetHashCode()/Equals(object)/ToString(); String.equalsIgnoreCase/compareToIgnoreCase are string.Equals/string.Compare
// with StringComparison.OrdinalIgnoreCase, and ClassCastException is InvalidCastException (as MemoryAttribute). The
// IOException text of toString() is Throwable.toString() (Common's ThrowableUtil.ThrowableToString).
// getBaseDirectory() and deleteOnExit() are protected internal overrides (see AbstractDiskHttpData); the narrowed
// copy/duplicate/retainedDuplicate/replace/retain/touch members are covariant override returns.
public class DiskAttribute : AbstractDiskHttpData, IAttribute
{
    public static string baseDirectory;

    public static bool deleteOnExitTemporaryFile = true;

    public const string prefix = "Attr_";

    public const string postfix = ".att";

    private string baseDir;

    private bool deleteOnExit;

    /**
     * Constructor used for huge Attribute
     */
    public DiskAttribute(string name) : this(name, HttpConstants.DEFAULT_CHARSET)
    {
    }

    public DiskAttribute(string name, string baseDir, bool deleteOnExit) : this(name, HttpConstants.DEFAULT_CHARSET)
    {
        this.baseDir = baseDir == null ? baseDirectory : baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    public DiskAttribute(string name, long definedSize)
        : this(name, definedSize, HttpConstants.DEFAULT_CHARSET, baseDirectory, deleteOnExitTemporaryFile)
    {
    }

    public DiskAttribute(string name, long definedSize, string baseDir, bool deleteOnExit)
        : this(name, definedSize, HttpConstants.DEFAULT_CHARSET)
    {
        this.baseDir = baseDir == null ? baseDirectory : baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    public DiskAttribute(string name, Encoding charset)
        : this(name, charset, baseDirectory, deleteOnExitTemporaryFile)
    {
    }

    public DiskAttribute(string name, Encoding charset, string baseDir, bool deleteOnExit) : base(name, charset, 0)
    {
        this.baseDir = baseDir == null ? baseDirectory : baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    public DiskAttribute(string name, long definedSize, Encoding charset)
        : this(name, definedSize, charset, baseDirectory, deleteOnExitTemporaryFile)
    {
    }

    public DiskAttribute(string name, long definedSize, Encoding charset, string baseDir, bool deleteOnExit)
        : base(name, charset, definedSize)
    {
        this.baseDir = baseDir == null ? baseDirectory : baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    public DiskAttribute(string name, string value) : this(name, value, HttpConstants.DEFAULT_CHARSET)
    {
    }

    public DiskAttribute(string name, string value, Encoding charset)
        : this(name, value, charset, baseDirectory, deleteOnExitTemporaryFile)
    {
    }

    public DiskAttribute(string name, string value, Encoding charset,
                         string baseDir, bool deleteOnExit) : base(name, charset, 0) // Attribute have no default size
    {
        SetValue(value);
        this.baseDir = baseDir == null ? baseDirectory : baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    public override IInterfaceHttpData.HttpDataType GetHttpDataType()
    {
        return IInterfaceHttpData.HttpDataType.Attribute;
    }

    public virtual string GetValue()
    {
        byte [] bytes = Get();
        return GetCharset().GetString(bytes);
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
        long newDefinedSize = size + buffer.ReadableBytes;

        try
        {
            CheckSize(newDefinedSize);
        }
        catch (IOException)
        {
            buffer.Release();
            throw;
        }

        if (definedSize > 0 && definedSize < newDefinedSize)
        {
            definedSize = newDefinedSize;
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

    public override int CompareTo(IInterfaceHttpData o)
    {
        if (!(o is IAttribute))
        {
            throw new InvalidCastException("Cannot compare " + GetHttpDataType() +
                    " with " + o.GetHttpDataType());
        }

        return CompareTo((IAttribute) o);
    }

    public virtual int CompareTo(IAttribute o)
    {
        return string.Compare(GetName(), o.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString()
    {
        try
        {
            return GetName() + '=' + GetValue();
        }
        catch (IOException e)
        {
            return GetName() + '=' + ThrowableUtil.ThrowableToString(e);
        }
    }

    protected internal override bool DeleteOnExit()
    {
        return deleteOnExit;
    }

    protected internal override string GetBaseDirectory()
    {
        return baseDir;
    }

    protected override string GetDiskFilename()
    {
        return GetName() + postfix;
    }

    protected override string GetPostfix()
    {
        return postfix;
    }

    protected override string GetPrefix()
    {
        return prefix;
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
        DiskAttribute attr = new DiskAttribute(GetName(), baseDir, deleteOnExit);
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

    public override IAttribute Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IAttribute Retain()
    {
        base.Retain();
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
