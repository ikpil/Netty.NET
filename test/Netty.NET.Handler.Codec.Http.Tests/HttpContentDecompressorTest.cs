/*
 * Copyright 2019 The Netty Project
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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Flow;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the anonymous ChannelOutboundHandlerAdapter and ChannelInboundHandlerAdapter classes are the named nested
// ReadCountingHandler, ReadingHandler and CountingHandler; AtomicInteger is the Common AtomicInteger. assertInstanceOf
// is Assert.IsAssignableFrom, channel.config().setAutoRead/setAllocator are Config.SetAutoRead/SetAllocator, alloc() is
// Alloc(), readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>() and Unpooled.EMPTY_BUFFER is
// Unpooled.EmptyBuffer (docs/transport-porting.md, docs/buffer-porting.md). @ParameterizedTest @MethodSource("encodings")
// is a [Theory] with MemberData rows. Zstd needs zstd-jni, which is library-not-ported (owner decision 2026-11-23;
// docs/codec-compression-porting.md), so encodings() leaves out "zstd" as Java does when Zstd.isAvailable() is false;
// Brotli.IsAvailable() is always true (the BCL BrotliEncoder/BrotliDecoder, docs/codec-compression-porting.md).
// @DisabledForSlowLeakDetection only skips testZipBomb under paranoid leak detection; the CLR test always runs, as for
// the codec-compression tests (test/Netty.NET.Handler.Codec.Compression.Tests/AbstractIntegrationTest.cs).
// Assumptions.assumeTrue(...) is Assert.SkipUnless (test/Netty.NET.Handler.Codec.Compression.Tests/JdkZlibDecompressorTest.cs).
// brotli4j's BrotliOutputStream over a ByteArrayOutputStream is the BCL BrotliStream(Stream, CompressionMode.Compress)
// over a MemoryStream ("Initializes a new instance of the BrotliStream class by using the specified stream and
// compression mode", https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream.-ctor?view=net-10.0);
// close() is Dispose(), which finishes the Brotli stream and closes the MemoryStream, and toByteArray() is ToArray(),
// which "works when the MemoryStream is closed"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.toarray?view=net-10.0). The payload byte
// (byte) (i % 251) has the same bit pattern as the Java byte, and the test only compares the bytes. Arrays.copyOf to a
// longer length is Array.Resize, which copies into a new array of that length, and buf.readBytes(byte[], int, int) is
// ReadBytes(Span<byte>) over the array slice. AssertJ's assertThat(array).as(d).isEqualTo(e) is Assert.Equal(e, array, d)
// and assertThat(n).as(d).isGreaterThan(100) is Assert.True(n > 100, d) (XunitAssertMessages.AssertM).
// PooledByteBufAllocator.metric().usedHeapMemory()/usedDirectMemory() are Metric.UsedHeapMemory/UsedDirectMemory.
public class HttpContentDecompressorTest
{

    private sealed class ReadCountingHandler : ChannelOutboundHandlerAdapter
    {
        private readonly AtomicInteger readCalled;

        internal ReadCountingHandler(AtomicInteger readCalled)
        {
            this.readCalled = readCalled;
        }

        public override void Read(IChannelHandlerContext ctx)
        {
            readCalled.IncrementAndGet();
            ctx.Read();
        }
    }

    private sealed class ReadingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);
            ctx.Read();
        }
    }

    // See https://github.com/netty/netty/issues/8915.
    [Fact]
    public void TestInvokeReadWhenNotProduceMessage()
    {
        AtomicInteger readCalled = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(new ReadCountingHandler(readCalled),
                new HttpContentDecompressor(0), new ReadingHandler());

        channel.Config.SetAutoRead(false);

        readCalled.Set(0);
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        response.Headers().Set(HttpHeaderNames.CONTENT_ENCODING, "gzip");
        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "application/json;charset=UTF-8");
        response.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

        Assert.True(channel.WriteInbound(response));

        // we triggered read explicitly
        Assert.Equal(1, readCalled.Get());

        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        Assert.False(channel.WriteInbound(new DefaultHttpContent(Unpooled.EmptyBuffer)));

        // read was triggered by the HttpContentDecompressor itself as it did not produce any message to the next
        // inbound handler.
        Assert.Equal(2, readCalled.Get());
        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class CountingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger reads;
        private readonly AtomicInteger readCompletes;

        internal CountingHandler(AtomicInteger reads, AtomicInteger readCompletes)
        {
            this.reads = reads;
            this.readCompletes = readCompletes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            reads.IncrementAndGet();
            ReferenceCountUtil.Release(msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            readCompletes.IncrementAndGet();
        }
    }

    // See https://github.com/netty/netty/issues/15053.
    [Fact]
    public void TestFlowControlHandlerEmitsOneMessagePerRead()
    {
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                new FlowControlHandler(),
                new HttpContentDecompressor(0),
                new CountingHandler(reads, readCompletes));

        channel.Config.SetAutoRead(false);

        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        response.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

        Assert.False(channel.WriteInbound(response));
        Assert.False(channel.WriteInbound(new DefaultHttpContent(Unpooled.EmptyBuffer)));
        Assert.False(channel.WriteInbound(new DefaultHttpContent(Unpooled.EmptyBuffer)));

        Assert.Equal(0, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        channel.Read();
        Assert.Equal(1, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.Read();
        Assert.Equal(2, reads.Get());
        Assert.Equal(2, readCompletes.Get());

        channel.Read();
        Assert.Equal(3, reads.Get());
        Assert.Equal(3, readCompletes.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    public static IEnumerable<object[]> Encodings()
    {
        List<string> encodings = new List<string>();
        encodings.Add("gzip");
        encodings.Add("deflate");
        if (Brotli.IsAvailable())
        {
            encodings.Add("br");
        }
        encodings.Add("snappy");
        return encodings.Select(e => new object[] { e });
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void TestZipBomb(string encoding)
    {
        int chunkSize = 1024 * 1024;
        int numberOfChunks = 256;
        int memoryLimit = chunkSize * 128;

        EmbeddedChannel compressionChannel = new EmbeddedChannel(new HttpContentCompressor());
        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        req.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, encoding);
        compressionChannel.WriteInbound(req);

        DefaultHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        response.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        compressionChannel.WriteOutbound(response);

        for (int i = 0; i < numberOfChunks; i++)
        {
            ByteBuf buffer = compressionChannel.Alloc().Buffer(chunkSize);
            buffer.WriteZero(chunkSize);
            compressionChannel.WriteOutbound(new DefaultHttpContent(buffer));
        }
        compressionChannel.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);
        compressionChannel.Finish();
        compressionChannel.ReleaseInbound();

        ByteBuf compressed = compressionChannel.Alloc().Buffer();
        IHttpMessage message = null;
        while (true)
        {
            IHttpObject obj = compressionChannel.ReadOutbound<IHttpObject>();
            if (obj == null)
            {
                break;
            }
            if (obj is IHttpMessage)
            {
                message = (IHttpMessage) obj;
            }
            if (obj is IHttpContent)
            {
                IHttpContent content = (IHttpContent) obj;
                compressed.WriteBytes(content.Content);
                content.Release();
            }
        }

        PooledByteBufAllocator allocator = new PooledByteBufAllocator(false);

        ZipBombIncomingHandler incomingHandler = new ZipBombIncomingHandler(memoryLimit);
        EmbeddedChannel decompressChannel = new EmbeddedChannel(new HttpContentDecompressor(0), incomingHandler);
        decompressChannel.Config.SetAllocator(allocator);
        decompressChannel.WriteInbound(message);
        decompressChannel.WriteInbound(new DefaultLastHttpContent(compressed));

        Assert.Equal((long) chunkSize * numberOfChunks, incomingHandler.total);
    }

    [Fact]
    public void TestBrotliDecodingHonorsMaxAllocationAsOutputCap()
    {
        Assert.SkipUnless(Brotli.IsAvailable(),
                "brotli4j native library not available on this platform");

        // 128KB of moderately compressible bytes so the decompressed size
        // definitely exceeds BrotliDecoder's 64KB default output cap and any
        // small maxAllocation we might set below.
        byte[] payload = new byte[128 * 1024];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte) (i % 251);
        }
        MemoryStream compressedOut = new MemoryStream();
        BrotliStream brotliOs = new BrotliStream(compressedOut, CompressionMode.Compress);
        brotliOs.Write(payload);
        brotliOs.Dispose();
        byte[] compressed = compressedOut.ToArray();

        // Deliberately use a tiny maxAllocation. Before the fix this configured
        // BrotliDecoder.inputBufferSize = 64, effectively breaking the decoder
        // for realistic payloads; after the fix it configures outputBufferSize.
        EmbeddedChannel channel = new EmbeddedChannel(new HttpContentDecompressor(64));
        try
        {
            IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
            response.Headers().Set(HttpHeaderNames.CONTENT_ENCODING, HttpHeaderValues.BR);
            response.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
            Assert.True(channel.WriteInbound(response));
            Assert.True(channel.WriteInbound(new DefaultHttpContent(Unpooled.WrappedBuffer(compressed))));
            Assert.True(channel.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

            // Drain and concatenate every decompressed HttpContent chunk.
            byte[] decompressed = new byte[0];
            int contentChunks = 0;
            object msg;
            while ((msg = channel.ReadInbound()) != null)
            {
                try
                {
                    if (msg is IHttpContent)
                    {
                        ByteBuf buf = ((IHttpContent) msg).Content;
                        if (buf.ReadableBytes > 0)
                        {
                            contentChunks++;
                            int len = decompressed.Length;
                            Array.Resize(ref decompressed, len + buf.ReadableBytes);
                            buf.ReadBytes(decompressed.AsSpan(len, buf.ReadableBytes));
                        }
                    }
                }
                finally
                {
                    ReferenceCountUtil.Release(msg);
                }
            }

            Assert.Equal(payload, decompressed, "decompressed bytes must match original payload");

            // Regression signal: with maxAllocation=64 correctly routed to
            // BrotliDecoder.outputBufferSize, a 128KB payload must be forwarded
            // in many small chunks. Under the previous (buggy) wiring the same
            // maxAllocation would be applied to inputBufferSize while
            // outputBufferSize stayed at BrotliDecoder's 64KB default, so only
            // a handful of large chunks would be emitted.
            Assert.True(contentChunks > 100, "expected many small chunks when maxAllocation=64 caps output size");
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private sealed class ZipBombIncomingHandler : ChannelInboundHandlerAdapter
    {
        internal readonly int memoryLimit;
        internal long total;

        internal ZipBombIncomingHandler(int memoryLimit)
        {
            this.memoryLimit = memoryLimit;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            PooledByteBufAllocator allocator = (PooledByteBufAllocator) ctx.Alloc();
            Assert.True(allocator.Metric.UsedHeapMemory < memoryLimit);
            Assert.True(allocator.Metric.UsedDirectMemory < memoryLimit);

            if (msg is IHttpContent)
            {
                IHttpContent buf = (IHttpContent) msg;
                total += buf.Content.ReadableBytes;
                buf.Release();
            }
        }
    }
}
