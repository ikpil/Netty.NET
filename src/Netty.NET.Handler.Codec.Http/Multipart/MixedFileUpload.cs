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

using System.Text;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Mixed implementation using both in Memory and in File with a limit of size
 */
// CLR: Charset is Encoding. The package-private makeDiskData() is the internal override of
// AbstractMixedHttpData.MakeDiskData. The overrides "for binary compatibility" are kept as overrides of the
// AbstractMixedHttpData<IFileUpload> members, which already return IFileUpload.
public class MixedFileUpload : AbstractMixedHttpData<IFileUpload>, IFileUpload
{

    public MixedFileUpload(string name, string filename, string contentType,
                           string contentTransferEncoding, Encoding charset, long size,
                           long limitSize)
        : this(name, filename, contentType, contentTransferEncoding,
             charset, size, limitSize, DiskFileUpload.baseDirectory, DiskFileUpload.deleteOnExitTemporaryFile)
    {
    }

    public MixedFileUpload(string name, string filename, string contentType,
                           string contentTransferEncoding, Encoding charset, long size,
                           long limitSize, string baseDir, bool deleteOnExit)
        : base(limitSize, baseDir, deleteOnExit,
              size > limitSize?
                      new DiskFileUpload(name, filename, contentType, contentTransferEncoding, charset, size, baseDir,
                                         deleteOnExit) :
                      new MemoryFileUpload(name, filename, contentType, contentTransferEncoding, charset, size)
        )
    {
    }

    public virtual string GetContentTransferEncoding()
    {
        return wrapped.GetContentTransferEncoding();
    }

    public virtual string GetFilename()
    {
        return wrapped.GetFilename();
    }

    public virtual void SetContentTransferEncoding(string contentTransferEncoding)
    {
        wrapped.SetContentTransferEncoding(contentTransferEncoding);
    }

    public virtual void SetFilename(string filename)
    {
        wrapped.SetFilename(filename);
    }

    public virtual void SetContentType(string contentType)
    {
        wrapped.SetContentType(contentType);
    }

    public virtual string GetContentType()
    {
        return wrapped.GetContentType();
    }

    internal override IFileUpload MakeDiskData()
    {
        DiskFileUpload diskFileUpload = new DiskFileUpload(
                GetName(), GetFilename(), GetContentType(), GetContentTransferEncoding(), GetCharset(), DefinedLength(),
                baseDir, deleteOnExit);
        diskFileUpload.SetMaxSize(GetMaxSize());
        return diskFileUpload;
    }

    public override IFileUpload Copy()
    {
        // for binary compatibility
        return base.Copy();
    }

    public override IFileUpload Duplicate()
    {
        // for binary compatibility
        return base.Duplicate();
    }

    public override IFileUpload RetainedDuplicate()
    {
        // for binary compatibility
        return base.RetainedDuplicate();
    }

    public override IFileUpload Replace(ByteBuf content)
    {
        // for binary compatibility
        return base.Replace(content);
    }

    public override IFileUpload Touch()
    {
        // for binary compatibility
        return base.Touch();
    }

    public override IFileUpload Touch(object hint)
    {
        // for binary compatibility
        return base.Touch(hint);
    }

    public override IFileUpload Retain()
    {
        // for binary compatibility
        return base.Retain();
    }

    public override IFileUpload Retain(int increment)
    {
        // for binary compatibility
        return base.Retain(increment);
    }
}
