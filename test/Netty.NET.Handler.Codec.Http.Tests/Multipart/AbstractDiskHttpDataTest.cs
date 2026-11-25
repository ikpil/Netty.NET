/*
 * Copyright 2020 The Netty Project
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
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

/**
 * {@link AbstractDiskHttpData} test cases
 */
// CLR: File is FileInfo and FileOutputStream a FileStream (FileMode.Create truncates as FileOutputStream does);
// UUID.randomUUID() is Guid.NewGuid() and ThreadLocalRandom.current().nextBytes is Random.Shared.NextBytes (only the size
// of the random content matters). File.deleteOnExit() is the AppDomain.ProcessExit deletion of handler's
// ChunkedWriteHandlerTest (test/Netty.NET.Handler.Tests/Stream/ChunkedWriteHandlerTest.cs). UTF_8 is Encoding.UTF8.
// Arrays.equals is SequenceEqual. The test subclass returns default for getHttpDataType(), because the C# enum
// HttpDataType cannot be null; the test does not call it. getBaseDirectory()/deleteOnExit() are protected internal in
// the port (see AbstractDiskHttpData), so this friend assembly overrides them as protected internal.
public class AbstractDiskHttpDataTest
{

    [Fact]
    public void TestGetChunk()
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
            ByteBuf buf1 = test.GetChunk(1024);
            Assert.Equal(buf1.ReaderIndex, 0);
            Assert.Equal(buf1.WriterIndex, 1024);
            ByteBuf buf2 = test.GetChunk(1024);
            Assert.Equal(buf2.ReaderIndex, 0);
            Assert.Equal(buf2.WriterIndex, 1024);
            Assert.False(ByteBufUtil.GetBytes(buf1).AsSpan().SequenceEqual(ByteBufUtil.GetBytes(buf2)),
                    "Arrays should not be equal");
        }
        finally
        {
            test.Delete();
        }
    }

    private sealed class TestHttpData : AbstractDiskHttpData
    {

        internal TestHttpData(string name, Encoding charset, long size) : base(name, charset, size)
        {
        }

        protected override string GetDiskFilename()
        {
            return null;
        }

        protected override string GetPrefix()
        {
            return null;
        }

        protected internal override string GetBaseDirectory()
        {
            return null;
        }

        protected override string GetPostfix()
        {
            return null;
        }

        protected internal override bool DeleteOnExit()
        {
            return false;
        }

        public override IHttpData Copy()
        {
            return null;
        }

        public override IHttpData Duplicate()
        {
            return null;
        }

        public override IHttpData RetainedDuplicate()
        {
            return null;
        }

        public override IHttpData Replace(ByteBuf content)
        {
            return null;
        }

        public override IInterfaceHttpData.HttpDataType GetHttpDataType()
        {
            return default;
        }

        public override int CompareTo(IInterfaceHttpData o)
        {
            return 0;
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
