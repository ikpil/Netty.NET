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
using System.Security.Cryptography;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

/** {@link AbstractMemoryHttpData} test cases. */
// CLR: File is FileInfo, FileOutputStream/FileInputStream are FileStream instances (FileMode.Create truncates as
// FileOutputStream does) and FileInputStream.available() of a file is Length - Position, the bytes a seekable stream
// has left (docs/handler-porting.md, ChunkedStream). ByteArrayInputStream is MemoryStream and ByteBufInputStream the
// Buffer port's ByteBufInputStream (a Stream). UUID.randomUUID() is Guid.NewGuid(); ThreadLocalRandom.nextBytes is
// Random.Shared.NextBytes and SecureRandom is RandomNumberGenerator (nextInt(bound) is GetInt32(bound), nextBytes is
// Fill); only the sizes matter. File.deleteOnExit() is the AppDomain.ProcessExit deletion of handler's
// ChunkedWriteHandlerTest (test/Netty.NET.Handler.Tests/Stream/ChunkedWriteHandlerTest.cs). buffer.array() of the
// stored heap buffer has no Buffer-port counterpart, so Arrays.copyOf(buffer.array(), bytes.length) is
// ByteBufUtil.GetBytes(buffer, 0, bytes.Length), the same first bytes of the storage. UnsupportedOperationException is
// NotSupportedException. assertEquals(buf.readerIndex(), 0) is written literal-first, Assert.Equal(expected, actual), as
// the xUnit analyzer requires (xUnit2000, https://xunit.net/xunit.analyzers/rules/xUnit2000).
public class AbstractMemoryHttpDataTest
{

    [Fact]
    public void TestSetContentFromFile()
    {
        TestHttpData test = new TestHttpData("test", Encoding.UTF8, 0);
        try
        {
            FileInfo tmpFile = PlatformDependent.CreateTempFile(Guid.NewGuid().ToString(), ".tmp", null);
            DeleteOnExit(tmpFile);
            FileStream fos = new FileStream(tmpFile.FullName, FileMode.Create, FileAccess.Write);
            byte[] bytes = new byte[4096];
            Random.Shared.NextBytes(bytes);
            try
            {
                fos.Write(bytes, 0, bytes.Length);
                fos.Flush();
            }
            finally
            {
                fos.Dispose();
            }
            test.SetContent(tmpFile);
            ByteBuf buf = test.GetByteBuf();
            Assert.Equal(0, buf.ReaderIndex);
            Assert.Equal(bytes.Length, buf.WriterIndex);
            Assert.Equal(bytes, test.Get());
            Assert.Equal(bytes, ByteBufUtil.GetBytes(buf));
        }
        finally
        {
            //release the ByteBuf
            test.Delete();
        }
    }

    [Fact]
    public void TestRenameTo()
    {
        TestHttpData test = new TestHttpData("test", Encoding.UTF8, 0);
        try
        {
            FileInfo tmpFile = PlatformDependent.CreateTempFile(Guid.NewGuid().ToString(), ".tmp", null);
            DeleteOnExit(tmpFile);
            const int totalByteCount = 4096;
            byte[] bytes = new byte[totalByteCount];
            Random.Shared.NextBytes(bytes);
            ByteBuf content = Unpooled.WrappedBuffer(bytes);
            test.SetContent(content);
            bool succ = test.RenameTo(tmpFile);
            Assert.True(succ);
            using (FileStream fis = new FileStream(tmpFile.FullName, FileMode.Open, FileAccess.Read))
            {
                byte[] buf = new byte[totalByteCount];
                int count = 0;
                int offset = 0;
                int size = totalByteCount;
                while ((count = fis.Read(buf, offset, size)) > 0)
                {
                    offset += count;
                    size -= count;
                    if (offset >= totalByteCount || size <= 0)
                    {
                        break;
                    }
                }
                Assert.Equal(bytes, buf);
                Assert.Equal(0, fis.Length - fis.Position);
            }
        }
        finally
        {
            //release the ByteBuf in AbstractMemoryHttpData
            test.Delete();
        }
    }

    /**
     * Provide content into HTTP data with input stream.
     *
     * @throws Exception In case of any exception.
     */
    [Fact]
    public void TestSetContentFromStream()
    {
        // definedSize=0
        TestHttpData test = new TestHttpData("test", Encoding.UTF8, 0);
        string contentStr = "foo_test";
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(contentStr));
        buf.MarkReaderIndex();
        using (ByteBufInputStream @is = new ByteBufInputStream(buf))
        {
            test.SetContent(@is);
            Assert.False(buf.IsReadable());
            Assert.Equal(contentStr, test.GetString(Encoding.UTF8));
            buf.ResetReaderIndex();
            Assert.True(ByteBufUtil.Equals(buf, test.GetByteBuf()));
        }

        for (int i = 0; i < 20; i++)
        {
            // Generate input data bytes.
            int size = RandomNumberGenerator.GetInt32(short.MaxValue);
            byte[] bytes = new byte[size];

            RandomNumberGenerator.Fill(bytes);

            // Generate parsed HTTP data block.
            TestHttpData data = new TestHttpData("name", Encoding.UTF8, 0);

            data.SetContent(new MemoryStream(bytes));

            // Validate stored data.
            ByteBuf buffer = data.GetByteBuf();

            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(bytes.Length, buffer.WriterIndex);
            Assert.Equal(bytes, ByteBufUtil.GetBytes(buffer, 0, bytes.Length));
            Assert.Equal(bytes, data.Get());
        }
    }

    /** Memory-based HTTP data implementation for test purposes. */
    private sealed class TestHttpData : AbstractMemoryHttpData
    {
        /**
         * Constructs HTTP data for tests.
         *
         * @param name    Name of parsed data block.
         * @param charset Used charset for data decoding.
         * @param size    Expected data block size.
         */
        internal TestHttpData(string name, Encoding charset, long size) : base(name, charset, size)
        {
        }

        public override IInterfaceHttpData.HttpDataType GetHttpDataType()
        {
            throw Reject();
        }

        public override IHttpData Copy()
        {
            throw Reject();
        }

        public override IHttpData Duplicate()
        {
            throw Reject();
        }

        public override IHttpData RetainedDuplicate()
        {
            throw Reject();
        }

        public override IHttpData Replace(ByteBuf content)
        {
            return null;
        }

        public override int CompareTo(IInterfaceHttpData o)
        {
            throw Reject();
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        private static NotSupportedException Reject()
        {
            throw new NotSupportedException("Should never be called.");
        }
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
