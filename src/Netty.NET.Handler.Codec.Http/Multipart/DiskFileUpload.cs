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
 * Disk FileUpload implementation that stores file into real files
 */
// CLR: the public static fields keep their Java names (baseDirectory and deleteOnExitTemporaryFile stay mutable static
// fields; the static final prefix/postfix are constants). java.io.File is FileInfo and getAbsolutePath() FullName.
// Charset is Encoding and Charset.name() HttpPostBodyUtil.CharsetName; the boolean texts of toString() are Java's
// "true"/"false". hashCode()/equals(Object)/toString() are GetHashCode()/Equals(object)/ToString(); ClassCastException is
// InvalidCastException (as MemoryAttribute). getBaseDirectory() and deleteOnExit() are protected internal overrides (see
// AbstractDiskHttpData); the narrowed copy/duplicate/retainedDuplicate/replace/retain/touch members are covariant
// override returns.
public class DiskFileUpload : AbstractDiskHttpData, IFileUpload
{
    public static string baseDirectory;

    public static bool deleteOnExitTemporaryFile = true;

    public const string prefix = "FUp_";

    public const string postfix = ".tmp";

    private readonly string baseDir;

    private readonly bool deleteOnExit;

    private string filename;

    private string contentType;

    private string contentTransferEncoding;

    public DiskFileUpload(string name, string filename, string contentType,
            string contentTransferEncoding, Encoding charset, long size, string baseDir, bool deleteOnExit)
        : base(name, charset, size)
    {
        SetFilename(filename);
        SetContentType(contentType);
        SetContentTransferEncoding(contentTransferEncoding);
        this.baseDir = baseDir == null ? baseDirectory : baseDir;
        this.deleteOnExit = deleteOnExit;
    }

    public DiskFileUpload(string name, string filename, string contentType,
            string contentTransferEncoding, Encoding charset, long size)
        : this(name, filename, contentType, contentTransferEncoding,
                charset, size, baseDirectory, deleteOnExitTemporaryFile)
    {
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
        FileInfo file = null;
        try
        {
            file = GetFile();
        }
        catch (IOException)
        {
            // Should not occur.
        }

        return HttpHeaderNames.CONTENT_DISPOSITION + ": " +
               HttpHeaderValues.FORM_DATA + "; " + HttpHeaderValues.NAME + "=\"" + GetName() +
                "\"; " + HttpHeaderValues.FILENAME + "=\"" + filename + "\"\r\n" +
                HttpHeaderNames.CONTENT_TYPE + ": " + contentType +
                (GetCharset() != null? "; " + HttpHeaderValues.CHARSET + '=' +
                    HttpPostBodyUtil.CharsetName(GetCharset()) + "\r\n" : "\r\n") +
                HttpHeaderNames.CONTENT_LENGTH + ": " + Length() + "\r\n" +
                "Completed: " + (IsCompleted() ? "true" : "false") +
                "\r\nIsInMemory: " + (IsInMemory() ? "true" : "false") + "\r\nRealFile: " +
                (file != null ? file.FullName : "null") + " DeleteAfter: " + (deleteOnExit ? "true" : "false");
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
        return "upload";
    }

    protected override string GetPostfix()
    {
        return postfix;
    }

    protected override string GetPrefix()
    {
        return prefix;
    }

    public override IFileUpload Copy()
    {
        ByteBuf content = Content;
        return Replace(content != null ? content.Copy() : null);
    }

    public override IFileUpload Duplicate()
    {
        ByteBuf content = Content;
        return Replace(content != null ? content.Duplicate() : null);
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
        DiskFileUpload upload = new DiskFileUpload(
                GetName(), GetFilename(), GetContentType(), GetContentTransferEncoding(), GetCharset(), size,
                baseDir, deleteOnExit);
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

    public override IFileUpload Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IFileUpload Retain()
    {
        base.Retain();
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
