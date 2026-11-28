/*
 * Copyright 2014 The Netty Project
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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests encoding/decoding each HTTP2 frame type.
 */
// CLR: Mockito mocks are Moq loose mocks; @BeforeEach is the constructor and @AfterEach Dispose().
// verify(mock).m(eq(...)) is Verify(..., Times.Once()) and atLeastOnce()/never() are Times.AtLeastOnce()/Times.Never()
// (Moq Quickstart, https://github.com/devlooped/moq/wiki/Quickstart; docs/codec-http-porting.md, Shared CLR
// adaptations). An ArgumentCaptor is read from the mock's recorded invocations (Mock.Invocations) of the verified
// member, in call order, as captor.getAllValues() returns them. ctx.write(msg, promise) is the
// IChannelHandlerContext.WriteAsync(msg, CancellationToken) that DefaultHttp2FrameWriter writes through
// (Http2CodecUtil.Write), so captureWrites() reads its msg arguments; the loose mock returns a completed Task for it.
// alloc.buffer() and alloc.buffer(int) are the one C# member Buffer(int initialCapacity = 256, int maxCapacity =
// int.MaxValue), so the answer allocates Unpooled.Buffer(initialCapacity) for both. ctx.newPromise() (a
// DefaultChannelPromise) is a new TaskCompletionSource. EmptyByteBuf is internal to Netty.NET.Buffer, so "buf
// instanceof EmptyByteBuf" compares the runtime type name. String.getBytes() (default charset) is
// Encoding.Default.GetBytes and getBytes(UTF_8) is Encoding.UTF8.GetBytes (docs/codec-http-porting.md, Shared CLR
// adaptations); System.arraycopy is Array.Copy and AsciiString.of(String) is new AsciiString(string).
// assertThrows(Http2Exception.class) is Assert.ThrowsAny<Http2Exception>.
public class Http2FrameRoundtripTest : IDisposable
{
    private static readonly byte[] MESSAGE = Encoding.UTF8.GetBytes("hello world");
    private const int STREAM_ID = 0x7FFFFFFF;
    private const int WINDOW_UPDATE = 0x7FFFFFFF;
    private const long ERROR_CODE = 0xFFFFFFFFL;

    private readonly Mock<IHttp2FrameListener> listener = new Mock<IHttp2FrameListener>();

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private readonly Mock<IEventExecutor> executor = new Mock<IEventExecutor>();

    private readonly Mock<IChannel> channel = new Mock<IChannel>();

    private readonly Mock<IByteBufAllocator> alloc = new Mock<IByteBufAllocator>();

    private IHttp2FrameWriter writer;
    private IHttp2FrameReader reader;
    private readonly List<ByteBuf> needReleasing = new List<ByteBuf>();

    public Http2FrameRoundtripTest()
    {
        ctx.Setup(c => c.Alloc()).Returns(alloc.Object);
        ctx.SetupGet(c => c.Executor).Returns(executor.Object);
        ctx.SetupGet(c => c.Channel).Returns(channel.Object);
        alloc.Setup(a => a.Buffer(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((initialCapacity, _) => Unpooled.Buffer(initialCapacity));

        writer = new DefaultHttp2FrameWriter(new DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.NEVER_SENSITIVE,
                NewTestEncoder()));
        reader = new DefaultHttp2FrameReader(new DefaultHttp2HeadersDecoder(false, false, NewTestDecoder()));
    }

    public void Dispose()
    {
        try
        {
            // Release all of the buffers.
            foreach (ByteBuf buf in needReleasing)
            {
                buf.Release();
            }

            // Now verify that all of the reference counts are zero.
            foreach (ByteBuf buf in needReleasing)
            {
                int expectedFinalRefCount = 0;

                if (buf.IsReadOnly || buf.GetType().Name == "EmptyByteBuf")
                {
                    // Special case for when we're writing slices of the padding buffer.
                    expectedFinalRefCount = 1;
                }

                Assert.Equal(expectedFinalRefCount, buf.RefCnt);
            }
        }
        finally
        {
            needReleasing.Clear();
        }
    }

    // CLR: ctx.newPromise() of the mocked context, answered with a new DefaultChannelPromise.
    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    [Fact]
    public void EmptyDataShouldMatch()
    {
        ByteBuf data = EmptyBuffer;
        writer.WriteDataAsync(ctx.Object, STREAM_ID, data.Slice(), 0, false, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnDataRead(ctx.Object, STREAM_ID, data, 0, false), Times.Once());
    }

    [Fact]
    public void DataShouldMatch()
    {
        ByteBuf data = Data(10);
        writer.WriteDataAsync(ctx.Object, STREAM_ID, data.Slice(), 1, false, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnDataRead(ctx.Object, STREAM_ID, data, 1, false), Times.Once());
    }

    [Fact]
    public void DataWithPaddingShouldMatch()
    {
        ByteBuf data = Data(10);
        writer.WriteDataAsync(ctx.Object, STREAM_ID, data.Slice(), MAX_PADDING, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnDataRead(ctx.Object, STREAM_ID, data, MAX_PADDING, true), Times.Once());
    }

    [Fact]
    public void LargeDataFrameShouldMatch()
    {
        // Create a large message to force chunking.
        ByteBuf originalData = Data(1024 * 1024);
        int originalPadding = 100;
        bool endOfStream = true;

        writer.WriteDataAsync(ctx.Object, STREAM_ID, originalData.Slice(), originalPadding,
                endOfStream, NewPromise());
        ReadFrames();

        // Verify that at least one frame was sent with eos=false and exactly one with eos=true.
        listener.Verify(l => l.OnDataRead(ctx.Object, STREAM_ID, It.IsAny<ByteBuf>(),
                It.IsAny<int>(), false), Times.AtLeastOnce());
        listener.Verify(l => l.OnDataRead(ctx.Object, STREAM_ID, It.IsAny<ByteBuf>(),
                It.IsAny<int>(), true), Times.Once());

        // Capture the read data and padding.
        listener.Verify(l => l.OnDataRead(ctx.Object, STREAM_ID, It.IsAny<ByteBuf>(),
                It.IsAny<int>(), It.IsAny<bool>()), Times.AtLeastOnce());
        List<object[]> dataReads = Captured(listener, nameof(IHttp2FrameListener.OnDataRead));

        // Make sure the data matches the original.
        foreach (object[] args in dataReads)
        {
            ByteBuf chunk = (ByteBuf) args[2];
            ByteBuf originalChunk = originalData.ReadSlice(chunk.ReadableBytes);
            Assert.Equal(originalChunk, chunk);
        }

        Assert.False(originalData.IsReadable());

        // Make sure the padding matches the original.
        int totalReadPadding = 0;

        foreach (object[] args in dataReads)
        {
            int framePadding = (int) args[3];
            totalReadPadding += framePadding;
        }

        Assert.Equal(originalPadding, totalReadPadding);
    }

    [Fact]
    public void EmptyHeadersShouldMatch()
    {
        IHttp2Headers headers = EmptyHttp2Headers.INSTANCE;
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 0, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 0, true), Times.Once());
    }

    [Fact]
    public void EmptyHeadersWithPaddingShouldMatch()
    {
        IHttp2Headers headers = EmptyHttp2Headers.INSTANCE;
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, MAX_PADDING, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, MAX_PADDING, true), Times.Once());
    }

    [Fact]
    public void BinaryHeadersWithoutPriorityShouldMatch()
    {
        IHttp2Headers headers = BinaryHeaders();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 0, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 0, true), Times.Once());
    }

    [Fact]
    public void HeadersFrameWithoutPriorityShouldMatch()
    {
        IHttp2Headers headers = Headers();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 0, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 0, true), Times.Once());
    }

    [Fact]
    public void HeadersFrameWithPriorityShouldMatch()
    {
        IHttp2Headers headers = Headers();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 4, (short) 255, true, 0, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 4, (short) 255,
                true, 0, true), Times.Once());
    }

    [Fact]
    public void HeadersWithPaddingWithoutPriorityShouldMatch()
    {
        IHttp2Headers headers = Headers();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, MAX_PADDING, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, MAX_PADDING, true), Times.Once());
    }

    [Fact]
    public void HeadersWithPaddingWithPriorityShouldMatch()
    {
        IHttp2Headers headers = Headers();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 2, (short) 3, true, 1, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 2, (short) 3, true,
                1, true), Times.Once());
    }

    [Fact]
    public void ContinuedHeadersShouldMatch()
    {
        IHttp2Headers headers = LargeHeaders();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 2, (short) 3, true, 0, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 2, (short) 3, true, 0, true),
                Times.Once());
    }

    [Fact]
    public void ContinuedHeadersWithPaddingShouldMatch()
    {
        IHttp2Headers headers = LargeHeaders();
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 2, (short) 3, true, MAX_PADDING, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnHeadersRead(ctx.Object, STREAM_ID, headers, 2, (short) 3, true,
                MAX_PADDING, true), Times.Once());
    }

    [Fact]
    public void HeadersThatAreTooBigShouldFail()
    {
        reader = new DefaultHttp2FrameReader(false);
        int maxListSize = 100;
        reader.Configuration().HeadersConfiguration().MaxHeaderListSize(maxListSize, maxListSize);
        IHttp2Headers headers = HeadersOfSize(maxListSize + 1);
        writer.WriteHeadersAsync(ctx.Object, STREAM_ID, headers, 2, (short) 3, true, MAX_PADDING, true, NewPromise());
        Assert.ThrowsAny<Http2Exception>(() => ReadFrames());
        listener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public void EmptyPushPromiseShouldMatch()
    {
        IHttp2Headers headers = EmptyHttp2Headers.INSTANCE;
        writer.WritePushPromiseAsync(ctx.Object, STREAM_ID, 2, headers, 0, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnPushPromiseRead(ctx.Object, STREAM_ID, 2, headers, 0), Times.Once());
    }

    [Fact]
    public void PushPromiseFrameShouldMatch()
    {
        IHttp2Headers headers = Headers();
        writer.WritePushPromiseAsync(ctx.Object, STREAM_ID, 1, headers, 5, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnPushPromiseRead(ctx.Object, STREAM_ID, 1, headers, 5), Times.Once());
    }

    [Fact]
    public void PushPromiseWithPaddingShouldMatch()
    {
        IHttp2Headers headers = Headers();
        writer.WritePushPromiseAsync(ctx.Object, STREAM_ID, 2, headers, MAX_PADDING, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnPushPromiseRead(ctx.Object, STREAM_ID, 2, headers, MAX_PADDING), Times.Once());
    }

    [Fact]
    public void ContinuedPushPromiseShouldMatch()
    {
        IHttp2Headers headers = LargeHeaders();
        writer.WritePushPromiseAsync(ctx.Object, STREAM_ID, 2, headers, 0, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnPushPromiseRead(ctx.Object, STREAM_ID, 2, headers, 0), Times.Once());
    }

    [Fact]
    public void ContinuedPushPromiseWithPaddingShouldMatch()
    {
        IHttp2Headers headers = LargeHeaders();
        writer.WritePushPromiseAsync(ctx.Object, STREAM_ID, 2, headers, 0xFF, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnPushPromiseRead(ctx.Object, STREAM_ID, 2, headers, 0xFF), Times.Once());
    }

    [Fact]
    public void GoAwayFrameShouldMatch()
    {
        string text = "test";
        ByteBuf data = Buf(Encoding.Default.GetBytes(text));

        writer.WriteGoAwayAsync(ctx.Object, STREAM_ID, ERROR_CODE, data.Slice(), NewPromise());
        ReadFrames();

        listener.Verify(l => l.OnGoAwayRead(ctx.Object, STREAM_ID, ERROR_CODE, It.IsAny<ByteBuf>()), Times.Once());
        ByteBuf captured = (ByteBuf) Captured(listener, nameof(IHttp2FrameListener.OnGoAwayRead)).Last()[3];
        Assert.Equal(data, captured);
    }

    [Fact]
    public void PingFrameShouldMatch()
    {
        writer.WritePingAsync(ctx.Object, false, 1234567, NewPromise());
        ReadFrames();

        listener.Verify(l => l.OnPingRead(ctx.Object, It.IsAny<long>()), Times.Once());
        long captured = (long) Captured(listener, nameof(IHttp2FrameListener.OnPingRead)).Last()[1];
        Assert.Equal(1234567, captured);
    }

    [Fact]
    public void PingAckFrameShouldMatch()
    {
        writer.WritePingAsync(ctx.Object, true, 1234567, NewPromise());
        ReadFrames();

        listener.Verify(l => l.OnPingAckRead(ctx.Object, It.IsAny<long>()), Times.Once());
        long captured = (long) Captured(listener, nameof(IHttp2FrameListener.OnPingAckRead)).Last()[1];
        Assert.Equal(1234567, captured);
    }

    [Fact]
    public void PriorityFrameShouldMatch()
    {
        writer.WritePriorityAsync(ctx.Object, STREAM_ID, 1, (short) 1, true, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnPriorityRead(ctx.Object, STREAM_ID, 1, (short) 1, true), Times.Once());
    }

    [Fact]
    public void RstStreamFrameShouldMatch()
    {
        writer.WriteRstStreamAsync(ctx.Object, STREAM_ID, ERROR_CODE, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnRstStreamRead(ctx.Object, STREAM_ID, ERROR_CODE), Times.Once());
    }

    [Fact]
    public void EmptySettingsFrameShouldMatch()
    {
        Http2Settings settings = new Http2Settings();
        writer.WriteSettingsAsync(ctx.Object, settings, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnSettingsRead(ctx.Object, settings), Times.Once());
    }

    [Fact]
    public void SettingsShouldStripShouldMatch()
    {
        Http2Settings settings = new Http2Settings();
        settings.PushEnabled(true);
        settings.HeaderTableSize(4096);
        settings.InitialWindowSize(123);
        settings.MaxConcurrentStreams(456);

        writer.WriteSettingsAsync(ctx.Object, settings, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnSettingsRead(ctx.Object, settings), Times.Once());
    }

    [Fact]
    public void SettingsAckShouldMatch()
    {
        writer.WriteSettingsAckAsync(ctx.Object, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnSettingsAckRead(ctx.Object), Times.Once());
    }

    [Fact]
    public void WindowUpdateFrameShouldMatch()
    {
        writer.WriteWindowUpdateAsync(ctx.Object, STREAM_ID, WINDOW_UPDATE, NewPromise());
        ReadFrames();
        listener.Verify(l => l.OnWindowUpdateRead(ctx.Object, STREAM_ID, WINDOW_UPDATE), Times.Once());
    }

    private void ReadFrames()
    {
        // Now read all of the written frames.
        ByteBuf write = CaptureWrites();
        reader.ReadFrame(ctx.Object, write, listener.Object);
    }

    private static ByteBuf Data(int size)
    {
        byte[] data = new byte[size];

        for (int ix = 0; ix < data.Length;)
        {
            int length = Math.Min(MESSAGE.Length, data.Length - ix);
            Array.Copy(MESSAGE, 0, data, ix, length);
            ix += length;
        }

        return Buf(data);
    }

    private static ByteBuf Buf(byte[] bytes)
    {
        return Unpooled.WrappedBuffer(bytes);
    }

    private T ReleaseLater<T>(T buf) where T : ByteBuf
    {
        needReleasing.Add(buf);
        return buf;
    }

    private ByteBuf CaptureWrites()
    {
        ctx.Verify(c => c.WriteAsync(It.IsAny<ByteBuf>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        CompositeByteBuf composite = ReleaseLater(Unpooled.CompositeBuffer());

        foreach (object[] args in Captured(ctx, nameof(IChannelHandlerContext.WriteAsync)))
        {
            ByteBuf buf = (ByteBuf) args[0];
            buf = ReleaseLater(buf.Retain());
            composite.AddComponent(buf, true);
        }

        return composite;
    }

    // CLR: the values an ArgumentCaptor captures: the arguments of every recorded call of the named member, in call
    // order (Moq's Mock.Invocations).
    private static List<object[]> Captured<T>(Mock<T> mock, string member) where T : class
    {
        return mock.Invocations.Where(i => i.Method.Name == member).Select(i => i.Arguments.ToArray()).ToList();
    }

    private static IHttp2Headers Headers()
    {
        return new DefaultHttp2Headers(false).Method(new AsciiString("GET")).Scheme(new AsciiString("https"))
                .Authority(new AsciiString("example.org")).Path(new AsciiString("/some/path/resource2"))
                .Add(RandomString(), RandomString());
    }

    private static IHttp2Headers LargeHeaders()
    {
        DefaultHttp2Headers headers = new DefaultHttp2Headers(false);

        for (int i = 0; i < 100; ++i)
        {
            string key = "this-is-a-test-header-key-" + i.ToString(CultureInfo.InvariantCulture);
            string value = "this-is-a-test-header-value-" + i.ToString(CultureInfo.InvariantCulture);
            headers.Add(new AsciiString(key), new AsciiString(value));
        }

        return headers;
    }

    private static IHttp2Headers HeadersOfSize(int minSize)
    {
        AsciiString singleByte = new AsciiString(new byte[]{0}, false);
        DefaultHttp2Headers headers = new DefaultHttp2Headers(false);

        for (int size = 0; size < minSize; size += 2)
        {
            headers.Add(singleByte, singleByte);
        }

        return headers;
    }

    private static IHttp2Headers BinaryHeaders()
    {
        DefaultHttp2Headers headers = new DefaultHttp2Headers(false);

        for (int ix = 0; ix < 10; ++ix)
        {
            headers.Add(RandomString(), RandomString());
        }

        return headers;
    }
}
