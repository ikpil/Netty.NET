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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Default FileUpload implementation that stores file into memory.<br><br>
 *
 * Warning: be aware of the memory limitation.
 */
// CLR: Charset is Encoding and Charset.name() is HttpPostBodyUtil.CharsetName (the Java canonical name). In toString()
// the boolean texts are Java's "true"/"false" (C# bool.ToString() gives "True"/"False"). hashCode()/equals(Object)/
// toString() are GetHashCode()/Equals(object)/ToString(); ClassCastException is InvalidCastException (as
// MemoryAttribute). The narrowed copy/duplicate/retainedDuplicate/replace/retain/touch members are covariant override
// returns; content() is the Content property and ChannelException the Transport ChannelException.
public class MemoryFileUpload : AbstractMemoryHttpData, IFileUpload
{

    private string filename;

    private string contentType;

    private string contentTransferEncoding;

    public MemoryFileUpload(string name, string filename, string contentType,
            string contentTransferEncoding, Encoding charset, long size) : base(name, charset, size)
    {
        SetFilename(filename);
        SetContentType(contentType);
        SetContentTransferEncoding(contentTransferEncoding);
    }

    public override IInterfaceHttpData.HttpDataType GetHttpDataType()
    {
        return IInterfaceHttpData.HttpDataType.FileUpload;
    }

    public virtual string GetFilename()
    {
        return filename;
    }

    public virtual void SetFilename(string filename)
    {
        this.filename = FileUploadUtil.ValidateFileNameForMultiPart(filename);
    }

    public override int GetHashCode()
    {
        return FileUploadUtil.HashCode(this);
    }

    public override bool Equals(object o)
    {
        return o is IFileUpload && FileUploadUtil.Equals(this, (IFileUpload) o);
    }

    public override int CompareTo(IInterfaceHttpData o)
    {
        if (!(o is IFileUpload))
        {
            throw new InvalidCastException("Cannot compare " + GetHttpDataType() +
                    " with " + o.GetHttpDataType());
        }
        return CompareTo((IFileUpload) o);
    }

    public virtual int CompareTo(IFileUpload o)
    {
        return FileUploadUtil.CompareTo(this, o);
    }

    public virtual void SetContentType(string contentType)
    {
        this.contentType = FileUploadUtil.ValidateContentTypeForMultiPart(contentType);
    }

    public virtual string GetContentType()
    {
        return contentType;
    }

    public virtual string GetContentTransferEncoding()
    {
        return contentTransferEncoding;
    }

    public virtual void SetContentTransferEncoding(string contentTransferEncoding)
    {
        this.contentTransferEncoding = contentTransferEncoding;
    }

    public override string ToString()
    {
        return HttpHeaderNames.CONTENT_DISPOSITION + ": " +
               HttpHeaderValues.FORM_DATA + "; " + HttpHeaderValues.NAME + "=\"" + GetName() +
            "\"; " + HttpHeaderValues.FILENAME + "=\"" + filename + "\"\r\n" +
            HttpHeaderNames.CONTENT_TYPE + ": " + contentType +
            (GetCharset() != null? "; " + HttpHeaderValues.CHARSET + '=' +
                HttpPostBodyUtil.CharsetName(GetCharset()) + "\r\n" : "\r\n") +
            HttpHeaderNames.CONTENT_LENGTH + ": " + Length() + "\r\n" +
            "Completed: " + (IsCompleted() ? "true" : "false") +
            "\r\nIsInMemory: " + (IsInMemory() ? "true" : "false");
    }

    public override IFileUpload Copy()
    {
        ByteBuf content = Content;
        return Replace(content != null ? content.Copy() : content);
    }

    public override IFileUpload Duplicate()
    {
        ByteBuf content = Content;
        return Replace(content != null ? content.Duplicate() : content);
    }

    public override IFileUpload RetainedDuplicate()
    {
        ByteBuf content = Content;
        if (content != null)
        {
            content = content.RetainedDuplicate();
            bool success = false;
            try
            {
                IFileUpload duplicate = Replace(content);
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

    public override IFileUpload Replace(ByteBuf content)
    {
        MemoryFileUpload upload = new MemoryFileUpload(
                GetName(), GetFilename(), GetContentType(), GetContentTransferEncoding(), GetCharset(), size);
        if (content != null)
        {
            try
            {
                upload.SetContent(content);
            }
            catch (IOException e)
            {
                throw new ChannelException(e);
            }
        }
        upload.SetCompleted(IsCompleted());
        return upload;
    }

    public override IFileUpload Retain()
    {
        base.Retain();
        return this;
    }

    public override IFileUpload Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IFileUpload Touch()
    {
        base.Touch();
        return this;
    }

    public override IFileUpload Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }
}
