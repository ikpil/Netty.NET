/*
 * Copyright 2022 The Netty Project
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

using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http.Multipart;

// CLR: the package-private abstract class is public with internal constructor and members, because the public
// MixedAttribute and MixedFileUpload derive from it and "a derived class cannot have broader accessibility than its
// base class" (CS0060, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0060); the internal abstract MakeDiskData
// keeps other assemblies from deriving from it, as the package-private constructor does in Java. D has the class
// constraint, because Java type arguments are reference types (as codec-base MessageAggregator,
// docs/codec-base-porting.md) and the covariant D returns of retain()/touch() need a reference conversion to
// IReferenceCounted (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). The
// unchecked (D) casts of the IByteBufHolder results and of this are C# casts (through object for this). content() is the
// Content property and File.length() is AbstractDiskHttpData.FileLength. hashCode()/equals(Object)/toString() are
// GetHashCode()/Equals(object)/ToString(); the IByteBufHolder members, which that interface declares with its own return
// types, are implemented explicitly and call the D members (as core's DefaultHttpContent does).
public abstract class AbstractMixedHttpData<D> : AbstractReferenceCounted, IHttpData where D : class, IHttpData
{
    internal readonly string baseDir;
    internal readonly bool deleteOnExit;
    internal D wrapped;

    private readonly long limitSize;

    internal AbstractMixedHttpData(long limitSize, string baseDir, bool deleteOnExit, D initial)
    {
        this.limitSize = limitSize;
        this.wrapped = initial;
        this.baseDir = baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    internal abstract D MakeDiskData();

    public virtual long GetMaxSize()
    {
        return wrapped.GetMaxSize();
    }

    public virtual void SetMaxSize(long maxSize)
    {
        wrapped.SetMaxSize(maxSize);
    }

    public virtual ByteBuf Content => wrapped.Content;

    public virtual void CheckSize(long newSize)
    {
        wrapped.CheckSize(newSize);
    }

    public virtual long DefinedLength()
    {
        return wrapped.DefinedLength();
    }

    public virtual Encoding GetCharset()
    {
        return wrapped.GetCharset();
    }

    public virtual string GetName()
    {
        return wrapped.GetName();
    }

    public virtual void AddContent(ByteBuf buffer, bool last)
    {
        if (wrapped is AbstractMemoryHttpData)
        {
            try
            {
                CheckSize(wrapped.Length() + buffer.ReadableBytes);

                if (wrapped.Length() + buffer.ReadableBytes > limitSize)
                {
                    D diskData = MakeDiskData();
                    ByteBuf data = ((AbstractMemoryHttpData) (object) wrapped).GetByteBuf();
                    if (data != null && data.IsReadable())
                    {
                        diskData.AddContent(data.Retain(), false);
                    }

                    wrapped.Release();
                    wrapped = diskData;
                }
            }
            catch (IOException)
            {
                buffer.Release();
                throw;
            }
        }

        wrapped.AddContent(buffer, last);
    }

    protected override void Deallocate()
    {
        Delete();
    }

    public virtual void Delete()
    {
        wrapped.Delete();
    }

    public virtual byte[] Get()
    {
        return wrapped.Get();
    }

    public virtual ByteBuf GetByteBuf()
    {
        return wrapped.GetByteBuf();
    }

    public virtual string GetString()
    {
        return wrapped.GetString();
    }

    public virtual string GetString(Encoding encoding)
    {
        return wrapped.GetString(encoding);
    }

    public virtual bool IsInMemory()
    {
        return wrapped.IsInMemory();
    }

    public virtual long Length()
    {
        return wrapped.Length();
    }

    public virtual bool RenameTo(FileInfo dest)
    {
        return wrapped.RenameTo(dest);
    }

    public virtual void SetCharset(Encoding charset)
    {
        wrapped.SetCharset(charset);
    }

    public virtual void SetContent(ByteBuf buffer)
    {
        try
        {
            CheckSize(buffer.ReadableBytes);
        }
        catch (IOException)
        {
            buffer.Release();
            throw;
        }

        if (buffer.ReadableBytes > limitSize)
        {
            if (wrapped is AbstractMemoryHttpData)
            {
                // change to Disk
                wrapped.Release();
                wrapped = MakeDiskData();
            }
        }

        wrapped.SetContent(buffer);
    }

    public virtual void SetContent(FileInfo file)
    {
        CheckSize(AbstractDiskHttpData.FileLength(file));

        if (AbstractDiskHttpData.FileLength(file) > limitSize)
        {
            if (wrapped is AbstractMemoryHttpData)
            {
                // change to Disk
                wrapped.Release();
                wrapped = MakeDiskData();
            }
        }

        wrapped.SetContent(file);
    }

    public virtual void SetContent(System.IO.Stream inputStream)
    {
        if (wrapped is AbstractMemoryHttpData)
        {
            // change to Disk even if we don't know the size
            wrapped.Release();
            wrapped = MakeDiskData();
        }

        wrapped.SetContent(inputStream);
    }

    public virtual bool IsCompleted()
    {
        return wrapped.IsCompleted();
    }

    public virtual IInterfaceHttpData.HttpDataType GetHttpDataType()
    {
        return wrapped.GetHttpDataType();
    }

    public override int GetHashCode()
    {
        return wrapped.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        return wrapped.Equals(obj);
    }

    public virtual int CompareTo(IInterfaceHttpData o)
    {
        return wrapped.CompareTo(o);
    }

    public override string ToString()
    {
        return "Mixed: " + wrapped;
    }

    public virtual ByteBuf GetChunk(int length)
    {
        return wrapped.GetChunk(length);
    }

    public virtual FileInfo GetFile()
    {
        return wrapped.GetFile();
    }

    public virtual D Copy()
    {
        return (D) wrapped.Copy();
    }

    public virtual D Duplicate()
    {
        return (D) wrapped.Duplicate();
    }

    public virtual D RetainedDuplicate()
    {
        return (D) wrapped.RetainedDuplicate();
    }

    public virtual D Replace(ByteBuf content)
    {
        return (D) wrapped.Replace(content);
    }

    public override D Touch()
    {
        wrapped.Touch();
        return (D) (object) this;
    }

    public override D Touch(object hint)
    {
        wrapped.Touch(hint);
        return (D) (object) this;
    }

    public override D Retain()
    {
        return (D) base.Retain();
    }

    public override D Retain(int increment)
    {
        return (D) base.Retain(increment);
    }

    IByteBufHolder IByteBufHolder.Copy()
    {
        return Copy();
    }

    IByteBufHolder IByteBufHolder.Duplicate()
    {
        return Duplicate();
    }

    IByteBufHolder IByteBufHolder.RetainedDuplicate()
    {
        return RetainedDuplicate();
    }

    IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
    {
        return Replace(content);
    }

    IByteBufHolder IByteBufHolder.Retain()
    {
        return Retain();
    }

    IByteBufHolder IByteBufHolder.Retain(int increment)
    {
        return Retain(increment);
    }

    IByteBufHolder IByteBufHolder.Touch()
    {
        return Touch();
    }

    IByteBufHolder IByteBufHolder.Touch(object hint)
    {
        return Touch(hint);
    }
}
