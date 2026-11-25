/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: File is FileInfo: new File(path).mkdirs() is DirectoryInfo.Create(), getAbsolutePath() FullName (startsWith is
// StartsWith with StringComparison.Ordinal), and exists()/length() are AbstractDiskHttpData.FileExists/FileLength,
// because FileInfo caches its state until Refresh(). FileOutputStream/FileInputStream are FileStream instances
// (FileMode.Create truncates as FileOutputStream does), ByteBufInputStream is the Buffer port's (a Stream), and
// File.deleteOnExit() is the AppDomain.ProcessExit deletion of handler's ChunkedWriteHandlerTest
// (test/Netty.NET.Handler.Tests/Stream/ChunkedWriteHandlerTest.cs). UUID.randomUUID() is Guid.NewGuid() and
// ThreadLocalRandom.nextBytes is Random.Shared.NextBytes (only the size of the content matters). CharsetUtil.UTF_8 is
// Encoding.UTF8 and String.getBytes(UTF_8) Encoding.UTF8.GetBytes. compositeBuffer().addComponent(true, buffer) is
// AddComponent(buffer, true) (Buffer port parameter order). assertThrows(IOException.class, ...) is Assert.Throws, and
// AssertJ assertThatThrownBy(...).isInstanceOf(IllegalArgumentException.class).hasMessageContaining(text) is
// Assert.ThrowsAny<ArgumentException> (isInstanceOf admits subclasses) with Assert.Contains on the message.
// @ParameterizedTest with @ValueSource(bytes/strings) is a [Theory] with one InlineData per value. The test methods
// with package access (no modifier in JUnit 5) are public, as xUnit requires. assertEquals(buf.readerIndex(), 0) is
// written literal-first, Assert.Equal(expected, actual), as the xUnit analyzer requires (xUnit2000,
// https://xunit.net/xunit.analyzers/rules/xUnit2000).
public class DiskFileUploadTest
{
    [Fact]
    public void TestSpecificCustomBaseDir()
    {
        DirectoryInfo baseDir = new DirectoryInfo("target/DiskFileUploadTest/testSpecificCustomBaseDir");
        baseDir.Create(); // we don't need to clean it since it is in volatile files anyway
        DiskFileUpload f =
                new DiskFileUpload("d1", "d1", "application/json", null, null, 100,
                        baseDir.FullName, false);

        f.SetContent(Unpooled.EmptyBuffer);

        Assert.StartsWith(baseDir.FullName, f.GetFile().FullName, StringComparison.Ordinal);
        Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
        Assert.Equal(0, AbstractDiskHttpData.FileLength(f.GetFile()));
        f.Delete();
    }

    [Fact]
    public void TestDiskFileUploadEquals()
    {
        DiskFileUpload f2 =
                new DiskFileUpload("d1", "d1", "application/json", null, null, 100);
        Assert.Equal(f2, f2);
        f2.Delete();
    }

     [Fact]
     public void TestEmptyBufferSetMultipleTimes()
     {
         DiskFileUpload f =
                 new DiskFileUpload("d1", "d1", "application/json", null, null, 100);

         f.SetContent(Unpooled.EmptyBuffer);

         Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
         Assert.Equal(0, AbstractDiskHttpData.FileLength(f.GetFile()));
         f.SetContent(Unpooled.EmptyBuffer);
         Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
         Assert.Equal(0, AbstractDiskHttpData.FileLength(f.GetFile()));
         f.Delete();
     }

    [Fact]
    public void TestEmptyBufferSetAfterNonEmptyBuffer()
    {
        DiskFileUpload f =
                new DiskFileUpload("d1", "d1", "application/json", null, null, 100);

        f.SetContent(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 }));

        Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
        Assert.Equal(4, AbstractDiskHttpData.FileLength(f.GetFile()));
        f.SetContent(Unpooled.EmptyBuffer);
        Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
        Assert.Equal(0, AbstractDiskHttpData.FileLength(f.GetFile()));
        f.Delete();
    }

    [Fact]
    public void TestNonEmptyBufferSetMultipleTimes()
    {
        DiskFileUpload f =
                new DiskFileUpload("d1", "d1", "application/json", null, null, 100);

        f.SetContent(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 }));

        Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
        Assert.Equal(4, AbstractDiskHttpData.FileLength(f.GetFile()));
        f.SetContent(Unpooled.WrappedBuffer(new byte[] { 1, 2}));
        Assert.True(AbstractDiskHttpData.FileExists(f.GetFile()));
        Assert.Equal(2, AbstractDiskHttpData.FileLength(f.GetFile()));
        f.Delete();
    }

    [Fact]
    public void TestAddContents()
    {
        DiskFileUpload f1 = new DiskFileUpload("file1", "file1", "application/json", null, null, 0);
        try
        {
            byte[] jsonBytes = new byte[4096];
            Random.Shared.NextBytes(jsonBytes);

            f1.AddContent(Unpooled.WrappedBuffer(jsonBytes, 0, 1024), false);
            f1.AddContent(Unpooled.WrappedBuffer(jsonBytes, 1024, jsonBytes.Length - 1024), true);
            Assert.Equal(jsonBytes, f1.Get());

            FileInfo file = f1.GetFile();
            Assert.Equal(jsonBytes.Length, AbstractDiskHttpData.FileLength(file));

            using (FileStream fis = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
            {
                byte[] buf = new byte[jsonBytes.Length];
                int offset = 0;
                int read = 0;
                int len = buf.Length;
                while ((read = fis.Read(buf, offset, len)) > 0)
                {
                    len -= read;
                    offset += read;
                    if (len <= 0 || offset >= buf.Length)
                    {
                        break;
                    }
                }
                Assert.Equal(jsonBytes, buf);
            }
        }
        finally
        {
            f1.Delete();
        }
    }

    [Fact]
    public void TestSetContentFromByteBuf()
    {
        DiskFileUpload f1 = new DiskFileUpload("file2", "file2", "application/json", null, null, 0);
        try
        {
            string json = "{\"hello\":\"world\"}";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            f1.SetContent(Unpooled.WrappedBuffer(bytes));
            Assert.Equal(json, f1.GetString());
            Assert.Equal(bytes, f1.Get());
            FileInfo file = f1.GetFile();
            Assert.Equal((long) bytes.Length, AbstractDiskHttpData.FileLength(file));
            Assert.Equal(bytes, DoReadFile(file, bytes.Length));
        }
        finally
        {
            f1.Delete();
        }
    }

    [Fact]
    public void TestSetContentFromInputStream()
    {
        string json = "{\"hello\":\"world\",\"foo\":\"bar\"}";
        DiskFileUpload f1 = new DiskFileUpload("file3", "file3", "application/json", null, null, 0);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            ByteBuf buf = Unpooled.WrappedBuffer(bytes);
            try
            {
                using (System.IO.Stream @is = new ByteBufInputStream(buf))
                {
                    f1.SetContent(@is);
                    Assert.Equal(json, f1.GetString());
                    Assert.Equal(bytes, f1.Get());
                    FileInfo file = f1.GetFile();
                    Assert.Equal((long) bytes.Length, AbstractDiskHttpData.FileLength(file));
                    Assert.Equal(bytes, DoReadFile(file, bytes.Length));
                }
            }
            finally
            {
                buf.Release();
            }
        }
        finally
        {
            f1.Delete();
        }
    }

    [Fact]
    public void TestAddContentFromByteBuf()
    {
        TestAddContentFromByteBuf0(false);
    }

    [Fact]
    public void TestAddContentFromCompositeByteBuf()
    {
        TestAddContentFromByteBuf0(true);
    }

    private static void TestAddContentFromByteBuf0(bool composite)
    {
        DiskFileUpload f1 = new DiskFileUpload("file3", "file3", "application/json", null, null, 0);
        try
        {
            byte[] bytes = new byte[4096];
            Random.Shared.NextBytes(bytes);

            ByteBuf buffer;

            if (composite)
            {
                buffer = Unpooled.CompositeBuffer()
                        .AddComponent(Unpooled.WrappedBuffer(bytes, 0 , bytes.Length / 2), true)
                        .AddComponent(Unpooled.WrappedBuffer(bytes, bytes.Length / 2, bytes.Length / 2), true);
            }
            else
            {
                buffer = Unpooled.WrappedBuffer(bytes);
            }
            f1.AddContent(buffer, true);
            ByteBuf buf = f1.GetByteBuf();
            Assert.Equal(0, buf.ReaderIndex);
            Assert.Equal(bytes.Length, buf.WriterIndex);
            Assert.Equal(bytes, ByteBufUtil.GetBytes(buf));
        }
        finally
        {
            //release the ByteBuf
            f1.Delete();
        }
    }

    private static byte[] DoReadFile(FileInfo file, int maxRead)
    {
        using (FileStream fis = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
        {
            byte[] buf = new byte[maxRead];
            int offset = 0;
            int read;
            int len = buf.Length;
            while ((read = fis.Read(buf, offset, len)) > 0)
            {
                len -= read;
                offset += read;
                if (len <= 0 || offset >= buf.Length)
                {
                    break;
                }
            }
            return buf;
        }
    }

    [Fact]
    public void TestDelete()
    {
        string json = "{\"foo\":\"bar\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        FileInfo tmpFile = null;
        DiskFileUpload f1 = new DiskFileUpload("file4", "file4", "application/json", null, null, 0);
        try
        {
            Assert.Null(f1.GetFile());
            f1.SetContent(Unpooled.WrappedBuffer(bytes));
            Assert.NotNull(tmpFile = f1.GetFile());
        }
        finally
        {
            f1.Delete();
            Assert.Null(f1.GetFile());
            Assert.NotNull(tmpFile);
            Assert.False(AbstractDiskHttpData.FileExists(tmpFile));
        }
    }

    [Fact]
    public void SetSetContentFromFileExceptionally()
    {
        const long maxSize = 4;
        DiskFileUpload f1 = new DiskFileUpload("file5", "file5", "application/json", null, null, 0);
        f1.SetMaxSize(maxSize);
        try
        {
            f1.SetContent(Unpooled.WrappedBuffer(new byte[(int) maxSize]));
            FileInfo originalFile = f1.GetFile();
            Assert.NotNull(originalFile);
            Assert.Equal(maxSize, AbstractDiskHttpData.FileLength(originalFile));
            Assert.Equal(maxSize, f1.Length());
            byte[] bytes = new byte[8];
            Random.Shared.NextBytes(bytes);
            FileInfo tmpFile = PlatformDependent.CreateTempFile(Guid.NewGuid().ToString(), ".tmp", null);
            DeleteOnExit(tmpFile);
            FileStream fos = new FileStream(tmpFile.FullName, FileMode.Create, FileAccess.Write);
            try
            {
                fos.Write(bytes, 0, bytes.Length);
                fos.Flush();
            }
            finally
            {
                fos.Dispose();
            }
            IOException e = Assert.Throws<IOException>(() => f1.SetContent(tmpFile));
            Assert.NotNull(f1.GetFile());
            Assert.Equal(originalFile, f1.GetFile());
            Assert.Equal(maxSize, f1.Length());
        }
        finally
        {
            f1.Delete();
        }
    }

    [Theory]
    [InlineData((byte) 0x00)]
    [InlineData(HttpConstants.CR)]
    [InlineData(HttpConstants.LF)]
    [InlineData((byte) 0x19)]
    [InlineData(HttpConstants.DEL)]
    [InlineData(HttpConstants.DOUBLE_QUOTE)]
    [InlineData(HttpConstants.BACKSLASH)]
    public void FilenameCannotContainIllegalCharacters(byte illegal)
    {
        AssertIllegalFilename(((char) illegal) + "f");
        AssertIllegalFilename("f" + ((char) illegal) + "f");
        AssertIllegalFilename("f" + ((char) illegal));
    }

    private static void AssertIllegalFilename(string filename)
    {
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(
                () => new DiskFileUpload("f", filename, "plain/text", null, null, 0));
        Assert.Contains("Illegal filename character", e.Message);
    }

    [Theory]
    [InlineData((byte) 0x00)]
    [InlineData(HttpConstants.CR)]
    [InlineData(HttpConstants.LF)]
    [InlineData((byte) 0x19)]
    [InlineData(HttpConstants.DEL)]
    public void ContentTypeCannotContainIllegalCharacters(byte illegal)
    {
        AssertIllegalContentType(((char) illegal) + "text/plain");
        AssertIllegalContentType("text/plain" + ((char) illegal) + " charset=\"us-ascii\"");
        AssertIllegalContentType("text/plain" + ((char) illegal));
    }

    [Fact]
    public void ContentTypeCannotStartWithSpaceOrTabCharacter()
    {
        AssertIllegalContentType(" text/plain");
        AssertIllegalContentType("\ttext/plain");
    }

    private static void AssertIllegalContentType(string contentType)
    {
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(
                () => new DiskFileUpload("f", "f.txt", contentType, null, null, 0));
        Assert.Contains("Illegal Content-Type character", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("text")]
    [InlineData("text;plain")]
    [InlineData("text;plain/octet-stream")]
    [InlineData("text; charset=\"utf/8\"; charset=us-ascii")]
    [InlineData("text/")]
    [InlineData("text/;")]
    [InlineData("/plain")]
    public void ContentTypeCannotHaveMalformedGrammar(string contentType)
    {
        AssertMalformedContentType(contentType);
    }

    private static void AssertMalformedContentType(string contentType)
    {
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(
                () => new DiskFileUpload("f", "f.txt", contentType, null, null, 0));
        Assert.Contains("Malformed content type value", e.Message);
    }

    [Fact]
    public void ContentTypeAllowsWhitespaceQuotesAndObsText()
    {
        AssertValidContentType("text/plain; charset=\"us-ascii\"");
        AssertValidContentType("text/plain;\tcharset=us-ascii");
        AssertValidContentType("text/plain; name=\"é\"");
        AssertValidContentType("t/p;");
        AssertValidContentType("t/p; ");
        AssertValidContentType("t/p");
    }

    private static void AssertValidContentType(string contentType)
    {
        Assert.Equal(contentType, new DiskFileUpload("f", "f.txt", contentType, null, null, 0).GetContentType());
    }

    // CLR: File.deleteOnExit() (see the class comment).
    private static void DeleteOnExit(FileInfo file)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }
}
