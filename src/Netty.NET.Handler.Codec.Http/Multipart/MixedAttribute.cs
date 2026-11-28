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

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Mixed implementation using both in Memory and in File with a limit of size
 */
// CLR: Charset is Encoding. new IllegalArgumentException(e) is an ArgumentException with Java's detail message
// cause.toString() (Common's ThrowableUtil.ThrowableToString) and the cause as its inner exception. The overrides
// "for binary compatibility" are kept as overrides of the AbstractMixedHttpData<IAttribute> members, which already
// return IAttribute.
public class MixedAttribute : AbstractMixedHttpData<IAttribute>, IAttribute
{
    public MixedAttribute(string name, long limitSize) : this(name, limitSize, HttpConstants.DEFAULT_CHARSET)
    {
    }

    public MixedAttribute(string name, long definedSize, long limitSize)
        : this(name, definedSize, limitSize, HttpConstants.DEFAULT_CHARSET)
    {
    }

    public MixedAttribute(string name, long limitSize, Encoding charset)
        : this(name, limitSize, charset, DiskAttribute.baseDirectory, DiskAttribute.deleteOnExitTemporaryFile)
    {
    }

    public MixedAttribute(string name, long limitSize, Encoding charset, string baseDir, bool deleteOnExit)
        : this(name, 0, limitSize, charset, baseDir, deleteOnExit)
    {
    }

    public MixedAttribute(string name, long definedSize, long limitSize, Encoding charset)
        : this(name, definedSize, limitSize, charset,
                DiskAttribute.baseDirectory, DiskAttribute.deleteOnExitTemporaryFile)
    {
    }

    public MixedAttribute(string name, long definedSize, long limitSize, Encoding charset,
                          string baseDir, bool deleteOnExit)
        : base(limitSize, baseDir, deleteOnExit,
                new MemoryAttribute(name, definedSize, charset))
    {
    }

    public MixedAttribute(string name, string value, long limitSize)
        : this(name, value, limitSize, HttpConstants.DEFAULT_CHARSET,
                DiskAttribute.baseDirectory, DiskFileUpload.deleteOnExitTemporaryFile)
    {
    }

    public MixedAttribute(string name, string value, long limitSize, Encoding charset)
        : this(name, value, limitSize, charset,
                DiskAttribute.baseDirectory, DiskFileUpload.deleteOnExitTemporaryFile)
    {
    }

    private static IAttribute MakeInitialAttributeFromValue(string name, string value, long limitSize, Encoding charset,
                                                            string baseDir, bool deleteOnExit)
    {
        if (value.Length > limitSize)
        {
            try
            {
                return new DiskAttribute(name, value, charset, baseDir, deleteOnExit);
            }
            catch (IOException e)
            {
                // revert to Memory mode
                try
                {
                    return new MemoryAttribute(name, value, charset);
                }
                catch (IOException)
                {
                    throw new ArgumentException(ThrowableUtil.ThrowableToString(e), e);
                }
            }
        }
        else
        {
            try
            {
                return new MemoryAttribute(name, value, charset);
            }
            catch (IOException e)
            {
                throw new ArgumentException(ThrowableUtil.ThrowableToString(e), e);
            }
        }
    }

    public MixedAttribute(string name, string value, long limitSize, Encoding charset,
                          string baseDir, bool deleteOnExit)
        : base(limitSize, baseDir, deleteOnExit,
                MakeInitialAttributeFromValue(name, value, limitSize, charset, baseDir, deleteOnExit))
    {
    }

    public virtual string GetValue()
    {
        return wrapped.GetValue();
    }

    public virtual void SetValue(string value)
    {
        wrapped.SetValue(value);
    }

    internal override IAttribute MakeDiskData()
    {
        DiskAttribute diskAttribute = new DiskAttribute(GetName(), DefinedLength(), baseDir, deleteOnExit);
        diskAttribute.SetMaxSize(GetMaxSize());
        return diskAttribute;
    }

    public override IAttribute Copy()
    {
        // for binary compatibility
        return base.Copy();
    }

    public override IAttribute Duplicate()
    {
        // for binary compatibility
        return base.Duplicate();
    }

    public override IAttribute Replace(ByteBuf content)
    {
        // for binary compatibility
        return base.Replace(content);
    }

    public override IAttribute Retain()
    {
        // for binary compatibility
        return base.Retain();
    }

    public override IAttribute Retain(int increment)
    {
        // for binary compatibility
        return base.Retain(increment);
    }

    public override IAttribute RetainedDuplicate()
    {
        // for binary compatibility
        return base.RetainedDuplicate();
    }

    public override IAttribute Touch()
    {
        // for binary compatibility
        return base.Touch();
    }

    public override IAttribute Touch(object hint)
    {
        // for binary compatibility
        return base.Touch(hint);
    }
}
