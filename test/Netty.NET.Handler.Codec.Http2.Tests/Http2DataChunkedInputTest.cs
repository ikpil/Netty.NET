/*
 * Copyright 2022 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.IO;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: as codec-http's HttpChunkedInputTest (test/Netty.NET.Handler.Codec.Http.Tests/HttpChunkedInputTest.cs): the
// static initializer is the static constructor; File is FileInfo, FileOutputStream a FileStream, ByteArrayInputStream
// and Channels.newChannel(InputStream) are MemoryStream, and File.deleteOnExit() is the AppDomain.ProcessExit deletion
// that handler's ChunkedWriteHandlerTest uses for the same temporary file (test/Netty.NET.Handler.Tests/Stream/
// ChunkedWriteHandlerTest.cs; docs/handler-porting.md, java.io and NIO). RuntimeException is Exception
// (docs/codec-base-porting.md). ByteBufAllocator.DEFAULT is the Buffer port's IByteBufAllocator.Default. The anonymous
// Http2FrameStream and ChunkedInput are the named nested classes DummyStream and NullChunkedInput; the @Deprecated
// readChunk(ChannelHandlerContext) override of the test input is not ported with the interface member
// (src/Netty.NET.Handler/Stream/IChunkedInput.cs). ChunkedInput<?>... is params object[] (the inputs are written as
// messages), readOutbound() is ReadOutbound<IHttp2DataFrame>() and content() is the Content property.
public class Http2DataChunkedInputTest
{
    private static readonly byte[] BYTES = new byte[1024 * 64];
    private static readonly FileInfo TMP;

    // Just a dummy interface implementation of stream
    private static readonly IHttp2FrameStream STREAM = new DummyStream();

    private sealed class DummyStream : IHttp2FrameStream
    {
        public int Id()
        {
            return 1;
        }

        public IHttp2Stream.State State()
        {
            return IHttp2Stream.State.OPEN;
        }
    }

    static Http2DataChunkedInputTest()
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
        Check(new Http2DataChunkedInput(new ChunkedStream(new MemoryStream(BYTES)), STREAM));
    }

    [Fact]
    public void TestChunkedNioStream()
    {
        Check(new Http2DataChunkedInput(new ChunkedNioStream(new MemoryStream(BYTES)),
                STREAM));
    }

    [Fact]
    public void TestChunkedFile()
    {
        Check(new Http2DataChunkedInput(new ChunkedFile(TMP), STREAM));
    }

    [Fact]
    public void TestChunkedNioFile()
    {
        Check(new Http2DataChunkedInput(new ChunkedNioFile(TMP), STREAM));
    }

    [Fact]
    public void TestWrappedReturnNull()
    {
        Http2DataChunkedInput input = new Http2DataChunkedInput(new NullChunkedInput(), STREAM);
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
        IHttp2DataFrame http2DataFrame = null;
        for (;;)
        {
            IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
            if (dataFrame == null)
            {
                break;
            }

            ByteBuf buffer = dataFrame.Content;
            while (buffer.IsReadable())
            {
                // CLR: Java compares the signed byte (byte) i with the signed readByte(); both sides are the unsigned
                // System.Byte here (owner decision: ReadByte returns an unsigned byte), which is equal for every octet.
                Assert.Equal(BYTES[i++], buffer.ReadByte());
                read++;
                if (i == BYTES.Length)
                {
                    i = 0;
                }
            }
            buffer.Release();

            // Save last chunk
            http2DataFrame = dataFrame;
        }

        Assert.Equal(BYTES.Length * inputs.Length, read);
        Assert.NotNull(http2DataFrame);
        Assert.True(http2DataFrame.IsEndStream(), "Last chunk must be Http2DataFrame#isEndStream() set to true");
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
