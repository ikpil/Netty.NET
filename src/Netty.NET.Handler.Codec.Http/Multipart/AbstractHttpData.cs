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

using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Abstract HttpData implementation
 */
// CLR: Charset is Encoding and HttpConstants.DEFAULT_CHARSET is Encoding.UTF8; content() is the virtual read-only
// Content property of IByteBufHolder (docs/buffer-porting.md, Buffer holders and conversion) and refCnt() the RefCnt
// property of the Common AbstractReferenceCounted. Character.isWhitespace is core's HttpVersion.IsJavaWhitespace and
// StringBuilder.append(CharSequence, start, end) is Append(string, start, end - start). retain()/retain(int) and the
// abstract touch()/touch(Object) narrow the AbstractReferenceCounted members with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). A Java abstract class need not
// declare the interface methods it leaves to its subclasses; a C# class must map every member of IHttpData, so the
// members this class does not implement (getHttpDataType(), compareTo(InterfaceHttpData), the content mutators and
// accessors, copy()/duplicate()/retainedDuplicate()/replace(ByteBuf)) are declared abstract here, and the
// IByteBufHolder members, which that interface declares with its own return types, are implemented explicitly and call
// them (as core's DefaultHttpContent does). The ChannelException of content() is the Transport ChannelException.
public abstract class AbstractHttpData : AbstractReferenceCounted, IHttpData
{

    private readonly string name;
    protected long definedSize;
    protected long size;
    private Encoding charset = HttpConstants.DEFAULT_CHARSET;
    private bool completed;
    private long maxSize = DefaultHttpDataFactory.MAXSIZE;

    protected AbstractHttpData(string name, Encoding charset, long size)
    {
        ObjectUtil.CheckNotNull(name, "name");

        this.name = CheckNonEmpty(CleanName(name), "name");
        if (charset != null)
        {
            SetCharset(charset);
        }
        definedSize = size;
    }

    //Replaces \r and \t with a space
    //Removes leading and trailing whitespace and newlines
    private static string CleanName(string name)
    {
        int len = name.Length;
        StringBuilder sb = null;

        int start = 0;
        int end = len;

        // Trim leading whitespace
        while (start < end && HttpVersion.IsJavaWhitespace(name[start]))
        {
            start++;
        }

        // Trim trailing whitespace
        while (end > start && HttpVersion.IsJavaWhitespace(name[end - 1]))
        {
            end--;
        }

        for (int i = start; i < end; i++)
        {
            char c = name[i];

            if (c == '\n')
            {
                // Skip newline entirely
                if (sb == null)
                {
                    sb = new StringBuilder(len);
                    sb.Append(name, start, i - start);
                }
                continue;
            }

            if (c == '\r' || c == '\t')
            {
                if (sb == null)
                {
                    sb = new StringBuilder(len);
                    sb.Append(name, start, i - start);
                }
                sb.Append(' ');
            }
            else if (sb != null)
            {
                sb.Append(c);
            }
        }

        // If no replacements were needed, return the trimmed slice
        return sb == null ? name.Substring(start, end - start) : sb.ToString();
    }

    public virtual long GetMaxSize()
    {
        return maxSize;
    }

    public virtual void SetMaxSize(long maxSize)
    {
        this.maxSize = maxSize;
    }

    public virtual void CheckSize(long newSize)
    {
        if (maxSize >= 0 && newSize > maxSize)
        {
            throw new IOException("Size exceed allowed maximum capacity");
        }
    }

    public virtual string GetName()
    {
        return name;
    }

    public virtual bool IsCompleted()
    {
        return completed;
    }

    protected void SetCompleted()
    {
        SetCompleted(true);
    }

    protected void SetCompleted(bool completed)
    {
        this.completed = completed;
    }

    public virtual Encoding GetCharset()
    {
        return charset;
    }

    public virtual void SetCharset(Encoding charset)
    {
        this.charset = ObjectUtil.CheckNotNull(charset, "charset");
    }

    public virtual long Length()
    {
        return size;
    }

    public virtual long DefinedLength()
    {
        return definedSize;
    }

    public virtual ByteBuf Content
    {
        get
        {
            try
            {
                return GetByteBuf();
            }
            catch (IOException e)
            {
                throw new ChannelException(e);
            }
        }
    }

    protected override void Deallocate()
    {
        Delete();
    }

    public override IHttpData Retain()
    {
        base.Retain();
        return this;
    }

    public override IHttpData Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public abstract override IHttpData Touch();

    public abstract override IHttpData Touch(object hint);

    // CLR: the IHttpData/IInterfaceHttpData members that Java leaves to the subclasses (see the class comment).
    public abstract IInterfaceHttpData.HttpDataType GetHttpDataType();

    public abstract int CompareTo(IInterfaceHttpData other);

    public abstract void SetContent(ByteBuf buffer);

    public abstract void AddContent(ByteBuf buffer, bool last);

    public abstract void SetContent(FileInfo file);

    public abstract void SetContent(System.IO.Stream inputStream);

    public abstract void Delete();

    public abstract byte[] Get();

    public abstract ByteBuf GetByteBuf();

    public abstract ByteBuf GetChunk(int length);

    public abstract string GetString();

    public abstract string GetString(Encoding encoding);

    public abstract bool RenameTo(FileInfo dest);

    public abstract bool IsInMemory();

    public abstract FileInfo GetFile();

    public abstract IHttpData Copy();

    public abstract IHttpData Duplicate();

    public abstract IHttpData RetainedDuplicate();

    public abstract IHttpData Replace(ByteBuf content);

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
