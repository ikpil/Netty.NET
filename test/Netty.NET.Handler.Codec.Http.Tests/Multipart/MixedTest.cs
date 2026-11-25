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

using System;
using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: refCnt() is RefCnt and CharsetUtil.UTF_8 Encoding.UTF8. File is FileInfo: new File(path).mkdirs() is
// DirectoryInfo.Create(), getAbsolutePath() FullName (startsWith is StartsWith with StringComparison.Ordinal), and
// exists()/length() are AbstractDiskHttpData.FileExists/FileLength, because FileInfo caches its state until Refresh().
public class MixedTest
{
    [Fact]
    public void MixedAttributeRefCnt()
    {
        MixedAttribute attribute = new MixedAttribute("foo", 100);
        Assert.Equal(1, attribute.RefCnt);
        attribute.Retain();
        Assert.Equal(2, attribute.RefCnt);

        attribute.AddContent(Unpooled.WrappedBuffer(new byte[90]), false);
        Assert.Equal(2, attribute.RefCnt);

        attribute.AddContent(Unpooled.WrappedBuffer(new byte[90]), true);
        Assert.Equal(2, attribute.RefCnt);

        attribute.Release(2);
    }

    [Fact]
    public void MixedFileUploadRefCnt()
    {
        MixedFileUpload upload = new MixedFileUpload("foo", "foo", "text/plain", "UTF-8", Encoding.UTF8, 0, 100);
        Assert.Equal(1, upload.RefCnt);
        upload.Retain();
        Assert.Equal(2, upload.RefCnt);

        upload.AddContent(Unpooled.WrappedBuffer(new byte[90]), false);
        Assert.Equal(2, upload.RefCnt);

        upload.AddContent(Unpooled.WrappedBuffer(new byte[90]), true);
        Assert.Equal(2, upload.RefCnt);

        upload.Release(2);
    }

    [Fact]
    public void TestSpecificCustomBaseDir()
    {
        DirectoryInfo baseDir = new DirectoryInfo("target/MixedTest/testSpecificCustomBaseDir");
        baseDir.Create(); // we don't need to clean it since it is in volatile files anyway
        MixedFileUpload upload = new MixedFileUpload("foo", "foo", "text/plain", "UTF-8", Encoding.UTF8, 1000, 100,
                                                     baseDir.FullName, true);

        upload.AddContent(Unpooled.WrappedBuffer(new byte[1000]), true);

        Assert.StartsWith(baseDir.FullName, upload.GetFile().FullName, StringComparison.Ordinal);
        Assert.True(AbstractDiskHttpData.FileExists(upload.GetFile()));
        Assert.Equal(1000, AbstractDiskHttpData.FileLength(upload.GetFile()));
        upload.Delete();
    }
}
