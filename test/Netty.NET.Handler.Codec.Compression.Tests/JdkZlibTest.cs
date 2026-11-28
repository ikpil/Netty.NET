/*
 * Copyright 2013 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: The java.util.zip test helpers are the BCL streams : GZIPOutputStream/GZIPInputStream are GZipStream,
// DeflaterOutputStream with new Deflater(BEST_COMPRESSION, nowrap) is a DeflateStream (nowrap) or ZLibStream with
// ZLibCompressionOptions.CompressionLevel 9, the level java.util.zip's BEST_COMPRESSION names ("9 gives best
// compression",
// https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0),
// and InflaterInputStream with new Inflater(nowrap) is a DeflateStream or ZLibStream in
// CompressionMode.Decompress; the tests compare inflated data only, and InputStream.read's -1 at the end is
// Stream.Read's 0. ByteArrayOutputStream is a MemoryStream, readBytes(OutputStream, int) is ReadBytes(Stream,
// int), System.arraycopy is Array.Copy, Arrays.fill is Array.Fill, Arrays.copyOfRange is a span copy,
// ThreadLocalRandom.current() is Random.Shared and CharsetUtil.UTF_8 is Encoding.UTF8.
// Deflater.DEFAULT_COMPRESSION is the constant of the internal stand-in Deflater (InternalsVisibleTo).
// @MethodSource("highlyCompressibleStreams") is MemberData, which must be a public static member
// (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs).
// The anonymous Executable is a lambda; assertThrows is Assert.Throws and assertDoesNotThrow is Record.Exception,
// which "Returns the exception that was thrown by the code; null, otherwise"
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/Record.cs). close() is CloseAsync(), whose Task the
// source ignores; xUnit1051 is suppressed for these calls, because the source's close() observes no cancellation.
public class JdkZlibTest : ZlibTest
{

    protected override ZlibEncoder CreateEncoder(ZlibWrapper wrapper)
    {
        return new JdkZlibEncoder(wrapper);
    }

    protected override ZlibDecoder CreateDecoder(ZlibWrapper wrapper, int maxAllocation)
    {
        return new JdkZlibDecoder(wrapper, maxAllocation);
    }

    protected override void TestZLIB_OR_NONE3Core()
    {
        Assert.Throws<DecompressionException>(() =>
        {
            base.TestZLIB_OR_NONE3Core();
        });
    }

    [Fact]
    // verifies backward compatibility
    public void TestConcatenatedStreamsReadFirstOnly()
    {
        EmbeddedChannel chDecoderGZip = new EmbeddedChannel(CreateDecoder(ZlibWrapper.GZIP));

        try
        {
            using (Stream resourceAsStream = typeof(JdkZlibTest).Assembly.GetManifestResourceStream("multiple.gz"))
            {
                byte[] bytes = ToByteArray(resourceAsStream);

                Assert.True(chDecoderGZip.WriteInbound(Unpooled.CopiedBuffer(bytes)));
                Queue<object> messages = chDecoderGZip.InboundMessages();
                Assert.Single(messages);

                ByteBuf msg = (ByteBuf) messages.Dequeue();
                Assert.Equal("a", msg.ToString(Encoding.UTF8));
                ReferenceCountUtil.Release(msg);
            }
        }
        finally
        {
            Assert.False(chDecoderGZip.Finish());
#pragma warning disable xUnit1051 // The source's close() observes no cancellation.
            _ = chDecoderGZip.CloseAsync();
#pragma warning restore xUnit1051
        }
    }

    [Fact]
    public void TestConcatenatedStreamsReadFully()
    {
        EmbeddedChannel chDecoderGZip = new EmbeddedChannel(new JdkZlibDecoder(true, 0));

        try
        {
            using (Stream resourceAsStream = typeof(JdkZlibTest).Assembly.GetManifestResourceStream("multiple.gz"))
            {
                byte[] bytes = ToByteArray(resourceAsStream);

                Assert.True(chDecoderGZip.WriteInbound(Unpooled.CopiedBuffer(bytes)));
                Queue<object> messages = chDecoderGZip.InboundMessages();
                Assert.Equal(2, messages.Count);

                foreach (string s in new[] { "a", "b" })
                {
                    ByteBuf msg = (ByteBuf) messages.Dequeue();
                    Assert.Equal(s, msg.ToString(Encoding.UTF8));
                    ReferenceCountUtil.Release(msg);
                }
            }
        }
        finally
        {
            Assert.False(chDecoderGZip.Finish());
#pragma warning disable xUnit1051 // The source's close() observes no cancellation.
            _ = chDecoderGZip.CloseAsync();
#pragma warning restore xUnit1051
        }
    }

    [Fact]
    public void TestConcatenatedStreamsReadFullyWhenFragmented()
    {
        EmbeddedChannel chDecoderGZip = new EmbeddedChannel(new JdkZlibDecoder(true, 0));

        try
        {
            using (Stream resourceAsStream = typeof(JdkZlibTest).Assembly.GetManifestResourceStream("multiple.gz"))
            {
                byte[] bytes = ToByteArray(resourceAsStream);

                // Let's feed the input byte by byte to simulate fragmentation.
                ByteBuf buf = Unpooled.CopiedBuffer(bytes);
                bool written = false;

                while (buf.IsReadable())
                {
                    written |= chDecoderGZip.WriteInbound(buf.ReadRetainedSlice(1));
                }

                buf.Release();

                Assert.True(written);
                Queue<object> messages = chDecoderGZip.InboundMessages();
                Assert.Equal(2, messages.Count);

                foreach (string s in new[] { "a", "b" })
                {
                    ByteBuf msg = (ByteBuf) messages.Dequeue();
                    Assert.Equal(s, msg.ToString(Encoding.UTF8));
                    ReferenceCountUtil.Release(msg);
                }
            }
        }
        finally
        {
            Assert.False(chDecoderGZip.Finish());
#pragma warning disable xUnit1051 // The source's close() observes no cancellation.
            _ = chDecoderGZip.CloseAsync();
#pragma warning restore xUnit1051
        }
    }

    [Fact]
    public void TestGZIPDecodeWithExtraField()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello, gzip FEXTRA world!");
        byte[] extra = { 0x42, 0x43, 0x02, 0x00, 0x99, 0x00 }; // 6 arbitrary bytes
        byte[] gzipWithExtra = GzipWithExtraField(data, extra);

        // Sanity-check the crafted stream is a valid gzip by decoding it with the JDK itself.
        Assert.Equal(data, JdkGunzip(gzipWithExtra));

        // netty must decode it identically; before the FEXTRA fix the extra bytes were never
        // skipped, corrupting the deflate stream and throwing DecompressionException.
        EmbeddedChannel ch = new EmbeddedChannel(CreateDecoder(ZlibWrapper.GZIP));

        try
        {
            Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(gzipWithExtra)));
            ByteBuf @out = ch.ReadInbound<ByteBuf>();
            Assert.Equal(Encoding.UTF8.GetString(data), @out.ToString(Encoding.UTF8));
            @out.Release();
        }
        finally
        {
            Assert.False(ch.Finish());
#pragma warning disable xUnit1051 // The source's close() observes no cancellation.
            _ = ch.CloseAsync();
#pragma warning restore xUnit1051
        }
    }

    [Fact]
    public void TestConcatenatedGzipFirstStreamHasExtraField()
    {
        // Regression guard: with concatenated streams, an FEXTRA field on the first stream must not
        // leak its XLEN into the second stream's header parsing. The xlen state has to be reset
        // between streams; otherwise the second stream (which has no extra field) would skip
        // xlen bytes that are actually deflate data and fail to decode.
        string firstText = "first stream";
        string secondText = "second stream";
        byte[] first = Encoding.UTF8.GetBytes(firstText);
        byte[] second = Encoding.UTF8.GetBytes(secondText);
        byte[] extra = { 0x42, 0x43, 0x02, 0x00, 0x99, 0x00 };

        byte[] firstGz = GzipWithExtraField(first, extra); // first stream HAS an extra field
        byte[] secondGz = Gzip(second);                    // second stream has none
        byte[] both = new byte[firstGz.Length + secondGz.Length];
        Array.Copy(firstGz, 0, both, 0, firstGz.Length);
        Array.Copy(secondGz, 0, both, firstGz.Length, secondGz.Length);

        EmbeddedChannel ch = new EmbeddedChannel(new JdkZlibDecoder(true, 0));

        try
        {
            Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(both)));
            MemoryStream decoded = new MemoryStream();
            ByteBuf msg;

            while ((msg = ch.ReadInbound<ByteBuf>()) != null)
            {
                msg.ReadBytes(decoded, msg.ReadableBytes);
                msg.Release();
            }

            Assert.Equal(Encoding.UTF8.GetBytes(firstText + secondText),
                    decoded.ToArray());
            decoded.Dispose();
        }
        finally
        {
            Assert.False(ch.Finish());
        }
    }

    [Theory]
    [MemberData(nameof(HighlyCompressibleStreams))]
    public void TestHighlyCompressibleStreamIsFullyDecoded(ZlibWrapper wrapper, int size, int chunkSize)
    {
        // Every output buffer is sized from the number of remaining input bytes. That is generous at
        // ordinary compression ratios, but at a high ratio the inflater can pull the last input byte into
        // its internal state while it still holds decoded data, and then the proposed size is zero and
        // inflate(...) cannot make progress. The tail stayed inside the inflater and was dropped together
        // with the input, without any exception, so a peer simply saw a short body.
        AssertFullyDecoded(wrapper, size, chunkSize);
    }

    public static object[][] HighlyCompressibleStreams()
    {
        // The two payload shapes reach the two places a buffer is taken. 65537 bytes written in one go
        // crosses the threshold at which the decoder forwards the buffer downstream and then allocates a
        // fresh one; 33333 bytes arriving in 7-byte reads stays under that threshold but starts every
        // decode(...) call with a fresh buffer. Only ZlibWrapper.NONE actually lost bytes before the fix,
        // because the ZLIB and GZIP trailers keep bytes in the input buffer while output is still pending.
        return new object[][]
        {
                new object[] { ZlibWrapper.NONE, 65537, int.MaxValue },
                new object[] { ZlibWrapper.NONE, 33333, 7 },
                new object[] { ZlibWrapper.ZLIB, 65537, int.MaxValue },
                new object[] { ZlibWrapper.ZLIB, 33333, 7 },
                new object[] { ZlibWrapper.GZIP, 65537, int.MaxValue },
                new object[] { ZlibWrapper.GZIP, 33333, 7 },
        };
    }

    private void AssertFullyDecoded(ZlibWrapper wrapper, int size, int chunkSize)
    {
        byte[] data = new byte[size];
        Array.Fill(data, (byte) 'a');
        byte[] compressed = Compress(wrapper, data);

        // Cross-check the fixture with the JDK itself, so a failure below cannot be blamed on it.
        Assert.Equal(data, JdkInflate(wrapper, compressed));

        EmbeddedChannel ch = new EmbeddedChannel(CreateDecoder(wrapper));
        MemoryStream decoded = new MemoryStream();

        try
        {
            ByteBuf @in = Unpooled.WrappedBuffer(compressed);

            try
            {
                while (@in.IsReadable())
                {
                    ch.WriteInbound(@in.ReadRetainedSlice(Math.Min(chunkSize, @in.ReadableBytes)));
                }
            }
            finally
            {
                @in.Release();
            }

            ch.Finish();

            ByteBuf msg;

            while ((msg = ch.ReadInbound<ByteBuf>()) != null)
            {
                msg.ReadBytes(decoded, msg.ReadableBytes);
                msg.Release();
            }

            Assert.Equal(data, decoded.ToArray());
        }
        finally
        {
            decoded.Dispose();
#pragma warning disable xUnit1051 // The source's close() observes no cancellation.
            _ = ch.CloseAsync();
#pragma warning restore xUnit1051
        }
    }

    private static byte[] Compress(ZlibWrapper wrapper, byte[] data)
    {
        if (wrapper == ZlibWrapper.GZIP)
        {
            return Gzip(data);
        }

        MemoryStream bytesOut = new MemoryStream();
        ZLibCompressionOptions bestCompression = new ZLibCompressionOptions { CompressionLevel = 9 };
        Stream deflaterOut = wrapper == ZlibWrapper.NONE
                ? new DeflateStream(bytesOut, bestCompression)
                : new ZLibStream(bytesOut, bestCompression);
        deflaterOut.Write(data);
        deflaterOut.Dispose();

        return bytesOut.ToArray();
    }

    private static byte[] JdkInflate(ZlibWrapper wrapper, byte[] compressed)
    {
        if (wrapper == ZlibWrapper.GZIP)
        {
            return JdkGunzip(compressed);
        }

        MemoryStream @out = new MemoryStream();

        try
        {
            Stream @in = wrapper == ZlibWrapper.NONE
                    ? new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress)
                    : new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);

            try
            {
                byte[] buf = new byte[8192];
                int n;

                while ((n = @in.Read(buf)) != 0)
                {
                    @out.Write(buf, 0, n);
                }
            }
            finally
            {
                @in.Dispose();
            }

            return @out.ToArray();
        }
        finally
        {
            @out.Dispose();
        }
    }

    private static byte[] Gzip(byte[] data)
    {
        MemoryStream bytesOut = new MemoryStream();
        GZipStream gzipOut = new GZipStream(bytesOut, CompressionMode.Compress);
        gzipOut.Write(data);
        gzipOut.Dispose();

        return bytesOut.ToArray();
    }

    private static byte[] GzipWithExtraField(byte[] data, byte[] extra)
    {
        // GZIPOutputStream never emits an FEXTRA field, so build a standard gzip stream first ...
        byte[] standard = Gzip(data);

        // ... then splice in an FEXTRA field by hand: set the FEXTRA flag in FLG and insert
        // XLEN (2 bytes, little-endian per RFC 1952) followed by the extra subfield, right after
        // the fixed 10-byte gzip header.
        MemoryStream withExtra = new MemoryStream();

        try
        {
            byte[] header = standard.AsSpan(0, 10).ToArray();
            header[3] |= 0x04; // FLG.FEXTRA
            withExtra.Write(header);
            withExtra.WriteByte((byte) (extra.Length & 0xff));          // XLEN low byte (little-endian)
            withExtra.WriteByte((byte) ((extra.Length >>> 8) & 0xff));  // XLEN high byte
            withExtra.Write(extra);
            withExtra.Write(standard, 10, standard.Length - 10);

            return withExtra.ToArray();
        }
        finally
        {
            withExtra.Dispose();
        }
    }

    private static byte[] JdkGunzip(byte[] gz)
    {
        MemoryStream @out = new MemoryStream();

        try
        {
            GZipStream @in =
                    new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);

            try
            {
                byte[] buf = new byte[256];
                int n;

                while ((n = @in.Read(buf)) != 0)
                {
                    @out.Write(buf, 0, n);
                }
            }
            finally
            {
                @in.Dispose();
            }

            return @out.ToArray();
        }
        finally
        {
            @out.Dispose();
        }
    }

    [Fact]
    public void TestDecodeWithHeaderFollowingFooter()
    {
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);
        MemoryStream bytesOut = new MemoryStream();
        GZipStream @out = new GZipStream(bytesOut, CompressionMode.Compress);
        @out.Write(bytes);
        @out.Dispose();

        byte[] compressed = bytesOut.ToArray();
        ByteBuf buffer = Unpooled.Buffer().WriteBytes(compressed).WriteBytes(compressed);
        EmbeddedChannel channel = new EmbeddedChannel(new JdkZlibDecoder(ZlibWrapper.GZIP, true, 0));
        // Write it into the Channel in a way that we were able to decompress the first data completely but not the
        // whole footer.
        Assert.True(channel.WriteInbound(buffer.ReadRetainedSlice(compressed.Length - 1)));
        Assert.True(channel.WriteInbound(buffer));
        Assert.True(channel.Finish());

        ByteBuf uncompressedBuffer = Unpooled.WrappedBuffer(bytes);
        ByteBuf read = channel.ReadInbound<ByteBuf>();
        Assert.Equal(uncompressedBuffer, read);
        read.Release();

        read = channel.ReadInbound<ByteBuf>();
        Assert.Equal(uncompressedBuffer, read);
        read.Release();

        Assert.Null(channel.ReadInbound());
        uncompressedBuffer.Release();
    }

    [Fact]
    public void TestLargeEncode()
    {
        // construct a 128M buffer out of many times the same 1M buffer :)
        byte[] smallArray = new byte[1024 * 1024];
        byte[][] arrayOfArrays = new byte[128][];
        Array.Fill(arrayOfArrays, smallArray);
        ByteBuf bigBuffer = Unpooled.WrappedBuffer(arrayOfArrays);

        EmbeddedChannel channel = new EmbeddedChannel(new JdkZlibEncoder(ZlibWrapper.NONE));
        channel.Config.SetAllocator(new LimitedByteBufAllocator(channel.Alloc()));
        Assert.True(channel.WriteOutbound(bigBuffer));
        Assert.True(channel.Finish());
        channel.CheckException();

        Assert.True(channel.ReleaseOutbound());
    }

    [Fact]
    public void TestAllowDefaultCompression()
    {
        Assert.Null(Record.Exception(() => new JdkZlibEncoder(Deflater.DEFAULT_COMPRESSION)));
    }

    [Fact]
    public void TestGzipFooterValidationSuccess()
    {
        byte[] data = Encoding.UTF8.GetBytes("hello gzip world");
        MemoryStream baos = new MemoryStream();
        GZipStream gzipOut = new GZipStream(baos, CompressionMode.Compress);
        gzipOut.Write(data);
        gzipOut.Dispose();

        byte[] compressed = baos.ToArray();
        EmbeddedChannel ch = new EmbeddedChannel(new JdkZlibDecoder(ZlibWrapper.GZIP, int.MaxValue));
        Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(compressed)));
        ByteBuf result = ch.ReadInbound<ByteBuf>();
        Assert.Equal(Unpooled.WrappedBuffer(data), result);
        result.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestGzipFooterCrcMismatchThrows()
    {
        byte[] data = Encoding.UTF8.GetBytes("corrupted gzip");
        MemoryStream baos = new MemoryStream();
        GZipStream gzipOut = new GZipStream(baos, CompressionMode.Compress);
        gzipOut.Write(data);
        gzipOut.Dispose();

        byte[] compressed = baos.ToArray();
        // Corrupt the CRC
        compressed[compressed.Length - 8] ^= 0xFF;

        EmbeddedChannel ch = new EmbeddedChannel(new JdkZlibDecoder(ZlibWrapper.GZIP, int.MaxValue));
        Assert.Throws<DecompressionException>(() =>
        {
            ch.WriteInbound(Unpooled.WrappedBuffer(compressed));
        });
        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestGzipFooterISizeMismatchThrows()
    {
        byte[] data = Encoding.UTF8.GetBytes("wrong size");
        MemoryStream baos = new MemoryStream();
        GZipStream gzipOut = new GZipStream(baos, CompressionMode.Compress);
        gzipOut.Write(data);
        gzipOut.Dispose();

        byte[] compressed = baos.ToArray();
        // Corrupt the ISIZE
        compressed[compressed.Length - 4] ^= 0xFF;

        EmbeddedChannel ch = new EmbeddedChannel(new JdkZlibDecoder(ZlibWrapper.GZIP, int.MaxValue));
        Assert.Throws<DecompressionException>(() =>
        {
            ch.WriteInbound(Unpooled.WrappedBuffer(compressed));
        });
        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestRoundTripCompressionGzipContentMatch()
    {
        byte[] input = Encoding.UTF8.GetBytes("Hello, Netty gzip roundtrip!");

        EmbeddedChannel encoder = new EmbeddedChannel(new JdkZlibEncoder(ZlibWrapper.GZIP));
        Assert.True(encoder.WriteOutbound(Unpooled.WrappedBuffer(input)));
        Assert.True(encoder.Finish());

        ByteBuf compressed = Unpooled.Buffer();

        for (;;)
        {
            ByteBuf part = encoder.ReadOutbound<ByteBuf>();
            if (part == null)
            {
                break;
            }

            compressed.WriteBytes(part);
            part.Release();
        }

        EmbeddedChannel decoder = new EmbeddedChannel(new JdkZlibDecoder(ZlibWrapper.GZIP, int.MaxValue));
        Assert.True(decoder.WriteInbound(compressed));
        ByteBuf result = decoder.ReadInbound<ByteBuf>();
        Assert.Equal(Unpooled.WrappedBuffer(input), result);
        result.Release();
        decoder.Finish();
    }

    [Fact]
    public void TestFragmentedGzipStreamStillYieldsCorrectContent()
    {
        string text = "Fragmented input stream for GZIP!";
        byte[] input = Encoding.UTF8.GetBytes(text);

        MemoryStream baos = new MemoryStream();
        GZipStream gzipOut = new GZipStream(baos, CompressionMode.Compress);
        gzipOut.Write(input);
        gzipOut.Dispose();
        byte[] compressed = baos.ToArray();

        EmbeddedChannel decoder = new EmbeddedChannel(new JdkZlibDecoder(ZlibWrapper.GZIP, int.MaxValue));

        foreach (byte b in compressed)
        {
            decoder.WriteInbound(Unpooled.WrappedBuffer(new byte[]{ b }));
        }

        Assert.True(decoder.Finish());

        ByteBuf result = Unpooled.Buffer();
        ByteBuf chunk;

        while ((chunk = decoder.ReadInbound<ByteBuf>()) != null)
        {
            result.WriteBytes(chunk);
            chunk.Release();
        }

        Assert.Equal(text, result.ToString(Encoding.UTF8));
        result.Release();
    }

    [Fact]
    public void TestMultipleConcatenatedGzipMessagesDecompressedIndividually()
    {
        string first = "first message";
        string second = "second message";

        byte[] c1 = GzipCompress(Encoding.UTF8.GetBytes(first));
        byte[] c2 = GzipCompress(Encoding.UTF8.GetBytes(second));
        byte[] combined = new byte[c1.Length + c2.Length];
        Array.Copy(c1, 0, combined, 0, c1.Length);
        Array.Copy(c2, 0, combined, c1.Length, c2.Length);

        EmbeddedChannel decoder = new EmbeddedChannel(new JdkZlibDecoder(true, 0));
        Assert.True(decoder.WriteInbound(Unpooled.WrappedBuffer(combined)));

        ByteBuf m1 = decoder.ReadInbound<ByteBuf>();
        ByteBuf m2 = decoder.ReadInbound<ByteBuf>();

        Assert.Equal(first, m1.ToString(Encoding.UTF8));
        Assert.Equal(second, m2.ToString(Encoding.UTF8));

        m1.Release();
        m2.Release();
        decoder.Finish();
    }

    private static byte[] GzipCompress(byte[] input)
    {
        MemoryStream baos = new MemoryStream();
        GZipStream gzipOut = new GZipStream(baos, CompressionMode.Compress);
        gzipOut.Write(input);
        gzipOut.Dispose();

        return baos.ToArray();
    }

    // CLR: commons-compress IOUtils.toByteArray(InputStream) is Stream.CopyTo a MemoryStream.
    private static byte[] ToByteArray(Stream stream)
    {
        MemoryStream bytes = new MemoryStream();
        stream.CopyTo(bytes);

        return bytes.ToArray();
    }

    /**
     * Allocator that will limit buffer capacity to 1M.
     */
    private sealed class LimitedByteBufAllocator : AbstractByteBufAllocator
    {
        private const int MAX = 1024 * 1024;

        private readonly IByteBufAllocator wrapped;

        internal LimitedByteBufAllocator(IByteBufAllocator wrapped)
        {
            this.wrapped = wrapped;
        }

        public override bool IsDirectBufferPooled => wrapped.IsDirectBufferPooled;

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            return wrapped.HeapBuffer(initialCapacity, Math.Min(maxCapacity, MAX));
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            return wrapped.DirectBuffer(initialCapacity, Math.Min(maxCapacity, MAX));
        }
    }
}
