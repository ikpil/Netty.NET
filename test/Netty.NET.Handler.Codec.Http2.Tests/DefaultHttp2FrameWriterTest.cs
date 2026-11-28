/*
 * Copyright 2017 The Netty Project
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2FrameWriter}.
 */
// CLR: The answer of ctx.write(msg, promise) is the callback of IChannelHandlerContext.WriteAsync(msg,
// CancellationToken), the member DefaultHttp2FrameWriter writes through (Http2CodecUtil.Write); it returns the
// never-completing Task of a TaskCompletionSource that stands for the mocked ChannelFuture, so the write promises
// stay pending as Java's mocked future leaves them. A DefaultChannelPromise is a TaskCompletionSource.
// ctx.alloc()/channel()/executor() are the Alloc() method and the Channel/Executor properties. Strings passed as
// CharSequence are StringCharSequence values (a C# string is not an ICharSequence; docs/codec-http-porting.md,
// Shared CLR adaptations). assertEquals(ByteBuf, ByteBuf) is ByteBuf.Equals (IEquatable<ByteBuf>, content
// comparison as Java's equals). OutOfMemoryError is OutOfMemoryException and assertThrows is Assert.ThrowsAny;
// ByteArrayOutputStream is a MemoryStream; Arrays.copyOfRange is an array range; String.format("%d") is invariant
// text and repeat(...) is string.Concat of Enumerable.Repeat (the same text as the format-and-replace trick).
// AbstractByteBufAllocator is the Buffer port's AbstractByteBufAllocator, whose isDirectBufferPooled() is the
// IsDirectBufferPooled property.
public class DefaultHttp2FrameWriterTest : IDisposable
{
    private DefaultHttp2FrameWriter frameWriter;

    private ByteBuf outbound;

    private ByteBuf expectedOutbound;

    private TaskCompletionSource promise;

    private IHttp2HeadersEncoder http2HeadersEncoder;

    private readonly Mock<IChannel> channel = new Mock<IChannel>();

    private readonly Task future = new TaskCompletionSource().Task;

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    public DefaultHttp2FrameWriterTest()
    {
        http2HeadersEncoder = new DefaultHttp2HeadersEncoder(
                IHttp2HeadersEncoder.NEVER_SENSITIVE, new HpackEncoder(false, 16, 0));

        frameWriter = new DefaultHttp2FrameWriter(new DefaultHttp2HeadersEncoder(
                IHttp2HeadersEncoder.NEVER_SENSITIVE, new HpackEncoder(false, 16, 0)));

        outbound = Unpooled.Buffer();

        expectedOutbound = Unpooled.EmptyBuffer;

        promise = new TaskCompletionSource();

        ctx.Setup(c => c.WriteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns<object, CancellationToken>((msg, _) =>
                {
                    if (msg is ByteBuf)
                    {
                        outbound.WriteBytes((ByteBuf) msg);
                    }

                    ReferenceCountUtil.Release(msg);
                    return future;
                });
        ctx.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        ctx.SetupGet(c => c.Channel).Returns(channel.Object);
        ctx.SetupGet(c => c.Executor).Returns(ImmediateEventExecutor.INSTANCE);
    }

    public void Dispose()
    {
        outbound.Release();
        expectedOutbound.Release();
        frameWriter.Close();
    }

    [Fact]
    public void WriteHeaders()
    {
        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));

        frameWriter.WriteHeadersAsync(ctx.Object, streamId, headers, 0, true, promise);

        byte[] expectedPayload = HeaderPayload(streamId, headers);
        byte[] expectedFrameBytes = {
                (byte) 0x00, (byte) 0x00, (byte) 0x0a, // payload length = 10
                (byte) 0x01, // payload type = 1
                (byte) 0x05, // flags = (0x01 | 0x04)
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01 // stream id = 1
        };
        expectedOutbound = Unpooled.CopiedBuffer(expectedFrameBytes, expectedPayload);
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WriteHeadersWithPadding()
    {
        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));

        frameWriter.WriteHeadersAsync(ctx.Object, streamId, headers, 5, true, promise);

        byte[] expectedPayload = HeaderPayload(streamId, headers, (byte) 4);
        byte[] expectedFrameBytes = {
                (byte) 0x00, (byte) 0x00, (byte) 0x0f, // payload length = 16
                (byte) 0x01, // payload type = 1
                (byte) 0x0d, // flags = (0x01 | 0x04 | 0x08)
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01 // stream id = 1
        };
        expectedOutbound = Unpooled.CopiedBuffer(expectedFrameBytes, expectedPayload);
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WriteHeadersNotEndStream()
    {
        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));

        frameWriter.WriteHeadersAsync(ctx.Object, streamId, headers, 0, false, promise);

        byte[] expectedPayload = HeaderPayload(streamId, headers);
        byte[] expectedFrameBytes = {
                (byte) 0x00, (byte) 0x00, (byte) 0x0a, // payload length = 10
                (byte) 0x01, // payload type = 1
                (byte) 0x04, // flags = 0x04
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01 // stream id = 1
        };
        ByteBuf expectedOutbound = Unpooled.CopiedBuffer(expectedFrameBytes, expectedPayload);
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WriteEmptyDataWithPadding()
    {
        int streamId = 1;

        ByteBuf payloadByteBuf = Unpooled.Buffer();
        frameWriter.WriteDataAsync(ctx.Object, streamId, payloadByteBuf, 2, true, promise);

        Assert.Equal(0, payloadByteBuf.RefCnt);

        byte[] expectedFrameBytes = {
            (byte) 0x00, (byte) 0x00, (byte) 0x02, // payload length
            (byte) 0x00, // payload type
            (byte) 0x09, // flags
            (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01, // stream id
            (byte) 0x01, (byte) 0x00, // padding
        };
        expectedOutbound = Unpooled.CopiedBuffer(expectedFrameBytes);
        Assert.Equal(expectedOutbound, outbound);
    }

    /**
     * Test large headers that exceed {@link DefaultHttp2FrameWriter#maxFrameSize()}
     * the remaining headers will be sent in a CONTINUATION frame
     */
    [Fact]
    public void WriteLargeHeaders()
    {
        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));
        headers = DummyHeaders(headers, 60);

        http2HeadersEncoder.Configuration().MaxHeaderListSize(int.MaxValue);
        frameWriter.HeadersConfiguration().MaxHeaderListSize(int.MaxValue);
        frameWriter.MaxFrameSize(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND);
        frameWriter.WriteHeadersAsync(ctx.Object, streamId, headers, 0, true, promise);

        byte[] expectedPayload = HeaderPayload(streamId, headers);

        // First frame: HEADER(length=0x4000, type=0x01, flags=0x01)
        Assert.Equal(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND,
                     outbound.ReadUnsignedMedium());
        Assert.Equal(0x01, outbound.ReadByte());
        Assert.Equal(0x01, outbound.ReadByte());
        Assert.Equal(streamId, outbound.ReadInt());

        byte[] firstPayload = new byte[Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND];
        outbound.ReadBytes(firstPayload);
        int index = 0;
        Assert.Equal(expectedPayload[index..(index + firstPayload.Length)],
                          firstPayload);
        index += firstPayload.Length;

        // Second frame: HEADER(length=0x4000, type=0x09, flags=0x00)
        Assert.Equal(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND,
                     outbound.ReadUnsignedMedium());
        Assert.Equal(0x09, outbound.ReadByte());
        Assert.Equal(0x00, outbound.ReadByte());
        Assert.Equal(streamId, outbound.ReadInt());

        byte[] secondPayload = new byte[Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND];
        outbound.ReadBytes(secondPayload);
        Assert.Equal(expectedPayload[index..(index + secondPayload.Length)],
                          secondPayload);
        index += secondPayload.Length;

        // third frame: HEADER(length=0x4000, type=0x09, flags=0x00)
        Assert.Equal(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND,
                     outbound.ReadUnsignedMedium());
        Assert.Equal(0x09, outbound.ReadByte());
        Assert.Equal(0x00, outbound.ReadByte());
        Assert.Equal(streamId, outbound.ReadInt());

        byte[] thirdPayload = new byte[Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND];
        outbound.ReadBytes(thirdPayload);
        Assert.Equal(expectedPayload[index..(index + thirdPayload.Length)],
                          thirdPayload);
        index += thirdPayload.Length;

        int remainPayloadLength = expectedPayload.Length - index;
        // Second frame: CONTINUATION(length=remainPayloadLength, type=0x09, flags=0x04)
        Assert.Equal(remainPayloadLength, outbound.ReadUnsignedMedium());
        Assert.Equal(0x09, outbound.ReadByte());
        Assert.Equal(0x04, outbound.ReadByte());
        Assert.Equal(streamId, outbound.ReadInt());

        byte[] fourthPayload = new byte[remainPayloadLength];
        outbound.ReadBytes(fourthPayload);

        Assert.Equal(expectedPayload[index..(index + fourthPayload.Length)],
                          fourthPayload);
    }

    [Fact]
    public void WriteLargeHeaderWithPadding()
    {
        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));
        headers = DummyHeaders(headers, 20);

        http2HeadersEncoder.Configuration().MaxHeaderListSize(int.MaxValue);
        frameWriter.HeadersConfiguration().MaxHeaderListSize(int.MaxValue);
        frameWriter.MaxFrameSize(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND);
        frameWriter.WriteHeadersAsync(ctx.Object, streamId, headers, 5, true, promise);

        byte[] expectedPayload = BuildLargeHeaderPayload(streamId, headers, (byte) 4,
                Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND);

        // First frame: HEADER(length=0x4000, flags=0x09)
        Assert.Equal(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND,
                outbound.ReadUnsignedMedium());
        Assert.Equal(0x01, outbound.ReadByte());
        Assert.Equal(0x09, outbound.ReadByte()); // 0x01 + 0x08
        Assert.Equal(streamId, outbound.ReadInt());

        byte[] firstPayload = new byte[Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND];
        outbound.ReadBytes(firstPayload);

        int remainPayloadLength = expectedPayload.Length - Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND;
        // Second frame: CONTINUATION(length=remainPayloadLength, flags=0x04)
        Assert.Equal(remainPayloadLength, outbound.ReadUnsignedMedium());
        Assert.Equal(0x09, outbound.ReadByte());
        Assert.Equal(0x04, outbound.ReadByte());
        Assert.Equal(streamId, outbound.ReadInt());

        byte[] secondPayload = new byte[remainPayloadLength];
        outbound.ReadBytes(secondPayload);

        Assert.Equal(expectedPayload[0..firstPayload.Length],
                firstPayload);
        Assert.Equal(expectedPayload[firstPayload.Length..expectedPayload.Length],
                secondPayload);
    }

    [Fact]
    public void WriteFrameZeroPayload()
    {
        frameWriter.WriteFrameAsync(ctx.Object, (byte) 0xf, 0, new Http2Flags(), Unpooled.EmptyBuffer, promise);

        byte[] expectedFrameBytes = {
                (byte) 0x00, (byte) 0x00, (byte) 0x00, // payload length
                (byte) 0x0f, // payload type
                (byte) 0x00, // flags
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00 // stream id
        };

        expectedOutbound = Unpooled.WrappedBuffer(expectedFrameBytes);
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WriteFrameHasPayload()
    {
        byte[] payload = {(byte) 0x01, (byte) 0x03, (byte) 0x05, (byte) 0x07, (byte) 0x09};

        // will auto release after frameWriter.writeFrame succeed
        ByteBuf payloadByteBuf = Unpooled.WrappedBuffer(payload);
        frameWriter.WriteFrameAsync(ctx.Object, (byte) 0xf, 0, new Http2Flags(), payloadByteBuf, promise);

        byte[] expectedFrameHeaderBytes = {
                (byte) 0x00, (byte) 0x00, (byte) 0x05, // payload length
                (byte) 0x0f, // payload type
                (byte) 0x00, // flags
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00 // stream id
        };
        expectedOutbound = Unpooled.CopiedBuffer(expectedFrameHeaderBytes, payload);
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WritePriority()
    {
        frameWriter.WritePriorityAsync(
            ctx.Object, /* streamId= */ 1, /* dependencyId= */ 2, /* weight= */ (short) 256, /* exclusive= */ true,
            promise);

        expectedOutbound = Unpooled.CopiedBuffer(new byte[] {
                (byte) 0x00, (byte) 0x00, (byte) 0x05, // payload length = 5
                (byte) 0x02, // payload type = 2
                (byte) 0x00, // flags = 0x00
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01, // stream id = 1
                (byte) 0x80, (byte) 0x00, (byte) 0x00, (byte) 0x02, // dependency id = 2 | exclusive = 1 << 63
                (byte) 0xFF, // weight = 255 (implicit +1)
        });
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WritePriorityDefaults()
    {
        frameWriter.WritePriorityAsync(
            ctx.Object, /* streamId= */ 1, /* dependencyId= */ 0, /* weight= */ (short) 16, /* exclusive= */ false,
            promise);

        expectedOutbound = Unpooled.CopiedBuffer(new byte[] {
                (byte) 0x00, (byte) 0x00, (byte) 0x05, // payload length = 5
                (byte) 0x02, // payload type = 2
                (byte) 0x00, // flags = 0x00
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01, // stream id = 1
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, // dependency id = 0 | exclusive = 0 << 63
                (byte) 0x0F, // weight = 15 (implicit +1)
        });
        Assert.Equal(expectedOutbound, outbound);
    }

    [Fact]
    public void WriteHeadersReleasesHeaderBlockWhenFrameHeaderAllocationFails()
    {
        TrackingFailingAllocator allocator =
                new TrackingFailingAllocator(Http2CodecUtil.HEADERS_FRAME_HEADER_LENGTH);
        ctx.Setup(c => c.Alloc()).Returns(allocator);

        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));

        Assert.ThrowsAny<OutOfMemoryException>(
                () => frameWriter.WriteHeadersAsync(ctx.Object, streamId, headers, 0, true, promise));

        AssertAllTrackedBuffersReleased(allocator);
    }

    [Fact]
    public void WritePushPromiseReleasesHeaderBlockWhenFrameHeaderAllocationFails()
    {
        TrackingFailingAllocator allocator =
                new TrackingFailingAllocator(Http2CodecUtil.PUSH_PROMISE_FRAME_HEADER_LENGTH);
        ctx.Setup(c => c.Alloc()).Returns(allocator);

        int streamId = 1;
        int promisedStreamId = 2;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));

        Assert.ThrowsAny<OutOfMemoryException>(
                () => frameWriter.WritePushPromiseAsync(ctx.Object, streamId, promisedStreamId, headers, 0, promise));

        AssertAllTrackedBuffersReleased(allocator);
    }

    [Fact]
    public void WriteContinuationFramesReleasesHeaderBlockWhenFrameHeaderAllocationFails()
    {
        int streamId = 1;
        IHttp2Headers headers = new DefaultHttp2Headers()
                .Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                .Authority(new StringCharSequence("foo.com"))
                .Scheme(new StringCharSequence("https"));
        IHttp2Headers largeHeaders = DummyHeaders(headers, 60);

        http2HeadersEncoder.Configuration().MaxHeaderListSize(int.MaxValue);
        frameWriter.HeadersConfiguration().MaxHeaderListSize(int.MaxValue);
        frameWriter.MaxFrameSize(Http2CodecUtil.MAX_FRAME_SIZE_LOWER_BOUND);

        TrackingFailingAllocator allocator =
                new TrackingFailingAllocator(Http2CodecUtil.CONTINUATION_FRAME_HEADER_LENGTH);
        ctx.Setup(c => c.Alloc()).Returns(allocator);

        Assert.ThrowsAny<OutOfMemoryException>(
                () => frameWriter.WriteHeadersAsync(ctx.Object, streamId, largeHeaders, 0, true, promise));

        AssertAllTrackedBuffersReleased(allocator);
    }

    private static void AssertAllTrackedBuffersReleased(TrackingFailingAllocator allocator)
    {
        Assert.False(allocator.allocated.Count == 0, "expected at least one buffer to be allocated");

        foreach (ByteBuf buf in allocator.allocated)
        {
            Assert.Equal(0, buf.RefCnt, "expected tracked buffer to be fully released");
        }
    }

    /**
     * A {@link ByteBufAllocator} that throws an {@link OutOfMemoryError} when asked to allocate a buffer with a
     * given {@code initialCapacity}, and otherwise delegates to an unpooled allocator while tracking every
     * successfully allocated buffer for leak verification.
     */
    private sealed class TrackingFailingAllocator : AbstractByteBufAllocator
    {
        private const int NEVER_FAIL = -1;

        private readonly IByteBufAllocator @delegate = new UnpooledByteBufAllocator(false);
        internal readonly List<ByteBuf> allocated = new List<ByteBuf>();
        private readonly int failOnInitialCapacity;

        internal TrackingFailingAllocator() : this(NEVER_FAIL)
        {
        }

        internal TrackingFailingAllocator(int failOnInitialCapacity) : base(false)
        {
            this.failOnInitialCapacity = failOnInitialCapacity;
        }

        private void FailIfTargeted(int initialCapacity)
        {
            if (failOnInitialCapacity != NEVER_FAIL && initialCapacity == failOnInitialCapacity)
            {
                throw new OutOfMemoryException("simulated allocation failure for capacity " +
                        initialCapacity.ToString(CultureInfo.InvariantCulture));
            }
        }

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            FailIfTargeted(initialCapacity);
            ByteBuf buf = @delegate.HeapBuffer(initialCapacity, maxCapacity);
            allocated.Add(buf);
            return buf;
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            FailIfTargeted(initialCapacity);
            ByteBuf buf = @delegate.DirectBuffer(initialCapacity, maxCapacity);
            allocated.Add(buf);
            return buf;
        }

        public override bool IsDirectBufferPooled => ((AbstractByteBufAllocator) @delegate).IsDirectBufferPooled;
    }

    private byte[] HeaderPayload(int streamId, IHttp2Headers headers, byte padding)
    {
        if (padding == 0)
        {
            return HeaderPayload(streamId, headers);
        }

        MemoryStream outputStream = new MemoryStream();
        outputStream.WriteByte(padding);
        outputStream.Write(HeaderPayload(streamId, headers));
        outputStream.Write(new byte[padding]);

        return outputStream.ToArray();
    }

    private byte[] HeaderPayload(int streamId, IHttp2Headers headers)
    {
        ByteBuf byteBuf = Unpooled.Buffer();

        try
        {
            http2HeadersEncoder.EncodeHeaders(streamId, headers, byteBuf);
            byte[] bytes = new byte[byteBuf.ReadableBytes];
            byteBuf.ReadBytes(bytes);
            return bytes;
        }
        finally
        {
            byteBuf.Release();
        }
    }

    private byte[] BuildLargeHeaderPayload(int streamId, IHttp2Headers headers, byte padding, int maxFrameSize)
    {
        MemoryStream outputStream = new MemoryStream();
        outputStream.WriteByte(padding);
        byte[] payload = HeaderPayload(streamId, headers);
        int firstPayloadSize = maxFrameSize - (padding + 1); //1 for padding length
        outputStream.Write(payload, 0, firstPayloadSize);
        outputStream.Write(new byte[padding]);
        outputStream.Write(payload, firstPayloadSize, payload.Length - firstPayloadSize);

        return outputStream.ToArray();
    }

    private static IHttp2Headers DummyHeaders(IHttp2Headers headers, int times)
    {
        string largeValue = Repeat("dummy-value", 100);

        for (int i = 0; i < times; i++)
        {
            headers.Add(new StringCharSequence("dummy-" + i.ToString(CultureInfo.InvariantCulture)),
                    new StringCharSequence(largeValue));
        }

        return headers;
    }

    private static string Repeat(string str, int count)
    {
        return string.Concat(Enumerable.Repeat(str, count));
    }
}
