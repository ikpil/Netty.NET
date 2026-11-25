/*
 * Copyright 2014 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the static initializer is the static constructor. File is FileInfo, FileOutputStream a FileStream,
// ByteArrayInputStream and Channels.newChannel(InputStream) are MemoryStream, and File.deleteOnExit() is the
// AppDomain.ProcessExit deletion that handler's ChunkedWriteHandlerTest uses for the same temporary file
// (test/Netty.NET.Handler.Tests/Stream/ChunkedWriteHandlerTest.cs; docs/handler-porting.md, java.io and NIO).
// RuntimeException is Exception (docs/codec-base-porting.md). ChannelFuture is Task: writeAndFlush(...) is
// WriteAndFlushAsync and isDone()/isSuccess() are IsCompleted/IsCompletedSuccessfully (docs/transport-porting.md).
// ByteBufAllocator.DEFAULT is the Buffer port's IByteBufAllocator.Default. The anonymous ChunkedInput is the named
// nested class NullChunkedInput; the @Deprecated readChunk(ChannelHandlerContext) overrides of the test inputs are not
// ported with the interface member (src/Netty.NET.Handler/Stream/IChunkedInput.cs). assertInstanceOf(type, value,
// message) is AssertM.True of an `is` test with the message, assertThrows(IOException.class, input::close) is
// Assert.Throws<IOException>, and ChunkedInput<?>... is params object[] (the inputs are written as messages).
#pragma warning disable xUnit1051 // The source's writeAndFlush(...) observes no test cancellation.
public class HttpChunkedInputTest
{
    private static readonly byte[] BYTES = new byte[1024 * 64];
    private static readonly FileInfo TMP;

    static HttpChunkedInputTest()
    {
        for (int i = 0; i < BYTES.Length; i++)
        {
            BYTES[i] = (byte) i;
        }

        try
        {
            TMP = PlatformDependent.CreateTempFile("netty-chunk-", ".tmp", null);
            DeleteOnExit(TMP);
            using (FileStream @out = new FileStream(TMP.FullName, FileMode.Create, FileAccess.Write))
            {
                @out.Write(BYTES);
                @out.Flush();
            }
        }
        catch (IOException e)
        {
            throw new Exception(e.Message, e);
        }
    }

    [Fact]
    public void TestChunkedStream()
    {
        Check(new HttpChunkedInput(new ChunkedStream(new MemoryStream(BYTES))));
    }

    [Fact]
    public void TestChunkedNioStream()
    {
        Check(new HttpChunkedInput(new ChunkedNioStream(new MemoryStream(BYTES))));
    }

    [Fact]
    public void TestChunkedFile()
    {
        Check(new HttpChunkedInput(new ChunkedFile(TMP)));
    }

    [Fact]
    public void TestChunkedNioFile()
    {
        Check(new HttpChunkedInput(new ChunkedNioFile(TMP)));
    }

    [Fact]
    public void TestWrappedReturnNull()
    {
        HttpChunkedInput input = new HttpChunkedInput(new NullChunkedInput());
        Assert.Null(input.ReadChunk(IByteBufAllocator.Default));
    }

    private sealed class NullChunkedInput : IChunkedInput<ByteBuf>
    {
        public bool IsEndOfInput()
        {
            return false;
        }

        public void Close()
        {
            // NOOP
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            return null;
        }

        public long Length()
        {
            return 0;
        }

        public long Progress()
        {
            return 0;
        }
    }

    [Fact]
    public void TestCloseReleasesUnsentLastHttpContent()
    {
        TestChunkedInput chunkedInput = new TestChunkedInput(false, false);
        ILastHttpContent lastHttpContent = new DefaultLastHttpContent(Unpooled.Buffer(1).WriteByte(1));
        HttpChunkedInput input = new HttpChunkedInput(chunkedInput, lastHttpContent);
        EmbeddedChannel channel = new EmbeddedChannel(new ChunkedWriteHandler());

        try
        {
            Task writeFuture = channel.WriteAndFlushAsync(input);
            Assert.False(writeFuture.IsCompleted);

            Assert.False(channel.Finish());
            Assert.True(chunkedInput.closed);
            Assert.False(writeFuture.IsCompletedSuccessfully);
            Assert.Equal(0, lastHttpContent.RefCnt);

            input.Close();
            Assert.Equal(0, lastHttpContent.RefCnt);
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestCloseDoesNotReleaseSentLastHttpContent()
    {
        ILastHttpContent lastHttpContent = new DefaultLastHttpContent(Unpooled.Buffer(1).WriteByte(1));
        HttpChunkedInput input = new HttpChunkedInput(new TestChunkedInput(true, false), lastHttpContent);
        EmbeddedChannel channel = new EmbeddedChannel(new ChunkedWriteHandler());

        try
        {
            Assert.True(channel.WriteOutbound(input));
            Assert.Same(lastHttpContent, channel.ReadOutbound());
            Assert.Null(channel.ReadOutbound());
            Assert.Equal(1, lastHttpContent.RefCnt);

            input.Close();
            input.Close();
            Assert.Equal(1, lastHttpContent.RefCnt);
        }
        finally
        {
            lastHttpContent.Release();
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestCloseReleasesUnsentLastHttpContentWhenInputCloseThrows()
    {
        ILastHttpContent lastHttpContent = new DefaultLastHttpContent(Unpooled.Buffer(1).WriteByte(1));
        HttpChunkedInput input = new HttpChunkedInput(new TestChunkedInput(false, true), lastHttpContent);

        Assert.Throws<IOException>(input.Close);
        Assert.Equal(0, lastHttpContent.RefCnt);
        Assert.Throws<IOException>(input.Close);
        Assert.Equal(0, lastHttpContent.RefCnt);
    }

    private static void Check(params object[] inputs)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new ChunkedWriteHandler());

        foreach (object input in inputs)
        {
            ch.WriteOutbound(input);
        }

        Assert.True(ch.Finish());

        int i = 0;
        int read = 0;
        IHttpContent lastHttpContent = null;
        for (;;)
        {
            IHttpContent httpContent = ch.ReadOutbound<IHttpContent>();
            if (httpContent == null)
            {
                break;
            }
            if (lastHttpContent != null)
            {
                Assert.True(lastHttpContent is DefaultHttpContent, "Chunk must be DefaultHttpContent");
            }

            ByteBuf buffer = httpContent.Content;
            while (buffer.IsReadable())
            {
                Assert.Equal(BYTES[i++], buffer.ReadByte());
                read++;
                if (i == BYTES.Length)
                {
                    i = 0;
                }
            }
            buffer.Release();

            // Save last chunk
            lastHttpContent = httpContent;
        }

        Assert.Equal(BYTES.Length * inputs.Length, read);
        Assert.True(ReferenceEquals(ILastHttpContent.EMPTY_LAST_CONTENT, lastHttpContent),
                "Last chunk must be LastHttpContent.EMPTY_LAST_CONTENT");
    }

    private sealed class TestChunkedInput : IChunkedInput<ByteBuf>
    {
        private readonly bool endOfInput;
        private readonly bool failOnClose;
        internal bool closed;

        internal TestChunkedInput(bool endOfInput, bool failOnClose)
        {
            this.endOfInput = endOfInput;
            this.failOnClose = failOnClose;
        }

        public bool IsEndOfInput()
        {
            return endOfInput;
        }

        public void Close()
        {
            closed = true;
            if (failOnClose)
            {
                throw new IOException("close failed");
            }
        }

        public ByteBuf ReadChunk(IByteBufAllocator allocator)
        {
            return null;
        }

        public long Length()
        {
            return 0;
        }

        public long Progress()
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
