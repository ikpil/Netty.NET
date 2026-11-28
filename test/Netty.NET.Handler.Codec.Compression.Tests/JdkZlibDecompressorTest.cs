/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: @ParameterizedClass with @MethodSource("wrappers") runs "all tests in a test class ... multiple times with
// different arguments", here the elements of wrappers(), which the @Parameter field receives
// (https://docs.junit.org/current/writing-tests/parameterized-classes-and-tests.html); the inherited
// AbstractDecompressorTest tests run per element too. xUnit has no parameterized test classes, so the class is abstract,
// the field is set by its constructor, and the nested classes ZLIB, GZIP and NONE pass the elements of Wrappers() in
// order; xUnit runs the inherited [Fact]/[Theory] methods for each of them. assumeTrue(...) is Assert.SkipUnless, which
// "Will skip the current test unless condition evaluates to true"
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/SkipAsserts.cs), as a failed JUnit assumption signals
// "that the test should be aborted instead of marked as a failure"
// (https://docs.junit.org/current/writing-tests/assumptions.html). Decompressor is IDecompressor, status() is Status_(), builder() is Builder_(),
// ByteBufAllocator.DEFAULT is IByteBufAllocator.Default and ChannelHandler is IChannelHandler; try-with-resources is a
// using statement over Dispose(), which calls Close(). assertDoesNotThrow(decompressor::close) is
// Assert.Null(Record.Exception(decompressor.Close)), which "Returns the exception that was thrown by the code; null,
// otherwise" (https://raw.githubusercontent.com/xunit/assert.xunit/main/Record.cs); fail(...) and the AssertionError of
// an unknown status are Assert.Fail; the AssertionError of an unexpected wrapper in the value-returning compress(...) is a
// thrown XunitException, the exception xUnit assertion failures derive from. IllegalArgumentException is ArgumentException. The Buffer port's
// addComponent(true, buffer) is AddComponent(buffer, true). The java.util.zip helpers are the BCL streams (owner
// decision 2026-11-23): GZIPOutputStream/GZIPInputStream are GZipStream, DeflaterOutputStream is ZLibStream, the raw
// DeflaterOutputStream with new Deflater(Deflater.DEFAULT_COMPRESSION, true) is DeflateStream with
// ZLibCompressionOptions.CompressionLevel -1, which "requests the default compression level"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0),
// and InflaterInputStream with new Inflater(nowrap) is a DeflateStream or ZLibStream in CompressionMode.Decompress
// (inflater.end() is its Dispose()); InputStream.read's -1 at the end is Stream.Read's 0, ByteArrayOutputStream is a
// MemoryStream, System.arraycopy is Array.Copy, Arrays.fill is Array.Fill, Arrays.copyOf/copyOfRange are span copies and
// CharsetUtil.UTF_8 is Encoding.UTF8. The helpers compress with the BCL, so the compressed bytes can differ from the
// JDK's; the tests compare inflated data only.
public abstract class JdkZlibDecompressorTest : AbstractDecompressorTest
{
    internal readonly ZlibWrapper wrapper;

    protected JdkZlibDecompressorTest(ZlibWrapper wrapper)
    {
        this.wrapper = wrapper;
    }

    internal static List<ZlibWrapper> Wrappers()
    {
        return new List<ZlibWrapper> { ZlibWrapper.ZLIB, ZlibWrapper.GZIP, ZlibWrapper.NONE };
    }

    public sealed class ZLIB : JdkZlibDecompressorTest
    {
        public ZLIB() : base(Wrappers()[0])
        {
        }
    }

    public sealed class GZIP : JdkZlibDecompressorTest
    {
        public GZIP() : base(Wrappers()[1])
        {
        }
    }

    public sealed class NONE : JdkZlibDecompressorTest
    {
        public NONE() : base(Wrappers()[2])
        {
        }
    }

    protected override IChannelHandler CreateCompressor()
    {
        return new JdkZlibEncoder(wrapper);
    }

    protected override IDecompressor.AbstractDecompressorBuilder CreateDecompressor()
    {
        return JdkZlibDecompressor.Builder_().Wrapper(wrapper);
    }

    [Fact]
    public void TestGzipCompletesAfterFooter()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.GZIP, "wrapper == ZlibWrapper.GZIP");
        byte[] compressed = Gzip(Encoding.UTF8.GetBytes("complete gzip"));
        IDecompressor decompressor = JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.GZIP)
                .Build(IByteBufAllocator.Default);

        try
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(Unpooled.WrappedBuffer(compressed));

            while (decompressor.Status_() == IDecompressor.Status.NEED_OUTPUT)
            {
                decompressor.TakeOutput().Release();
            }

            Assert.Equal(IDecompressor.Status.COMPLETE, decompressor.Status_());
        }
        finally
        {
            decompressor.Close();
        }
    }

    [Fact]
    public void TestCloseBeforeCompletionIsIdempotent()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.GZIP, "wrapper == ZlibWrapper.GZIP");
        IDecompressor decompressor = JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.GZIP)
                .Build(IByteBufAllocator.Default);
        Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
        decompressor.AddInput(Unpooled.WrappedBuffer(new byte[] { 31 }));
        Assert.Null(Record.Exception(decompressor.Close));
        Assert.Null(Record.Exception(decompressor.Close));
    }

    [Fact]
    public void TestTruncatedZlibInputRejectedAtEndOfInput()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.ZLIB, "wrapper == ZlibWrapper.ZLIB");
        byte[] compressed = Deflate(Encoding.UTF8.GetBytes("truncated zlib"));
        AssertTruncated(JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.ZLIB)
                .Build(IByteBufAllocator.Default), compressed.AsSpan(0, compressed.Length - 1).ToArray());
    }

    [Fact]
    public void TestTruncatedGzipInputRejectedAtEndOfInput()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.GZIP, "wrapper == ZlibWrapper.GZIP");
        byte[] compressed = Gzip(Encoding.UTF8.GetBytes("truncated gzip"));
        AssertTruncated(JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.GZIP)
                .Build(IByteBufAllocator.Default), compressed.AsSpan(0, compressed.Length - 1).ToArray());
    }

    [Fact]
    public void TestMalformedInputDoesNotLeakOutput()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.ZLIB, "wrapper == ZlibWrapper.ZLIB");
        IDecompressor decompressor = JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.ZLIB)
                .Build(IByteBufAllocator.Default);

        try
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(Unpooled.WrappedBuffer(new byte[] { 0, 0, 0, 0 }));
            Assert.Equal(IDecompressor.Status.NEED_OUTPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(decompressor.TakeOutput);
        }
        finally
        {
            decompressor.Close();
        }
    }

    [Fact]
    public void TestNegativeMaxAllocationRejected()
    {
        Assert.Throws<ArgumentException>(() => JdkZlibDecompressor.Builder_().MaxAllocation(-1));
    }

    [Fact]
    public void TestDirectInputRetainedAcrossOutputChunks()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.ZLIB, "wrapper == ZlibWrapper.ZLIB");
        byte[] expected = new byte[8192];
        Array.Fill(expected, (byte) 'a');
        byte[] compressed = Deflate(expected);
        CompositeByteBuf output = IByteBufAllocator.Default.CompositeBuffer();

        try
        {
            using (IDecompressor decompressor = JdkZlibDecompressor.Builder_().MaxAllocation(1)
                    .Build(IByteBufAllocator.Default))
            {
                decompressor.Status_();
                decompressor.AddInput(Unpooled.DirectBuffer(compressed.Length).WriteBytes(compressed));

                for (;;)
                {
                    switch (decompressor.Status_())
                    {
                        case IDecompressor.Status.NEED_OUTPUT:
                            output.AddComponent(decompressor.TakeOutput(), true);
                            break;
                        case IDecompressor.Status.COMPLETE:
                            byte[] actual = new byte[output.ReadableBytes];
                            output.ReadBytes(actual.AsSpan());
                            Assert.Equal(expected, actual);
                            return;
                        case IDecompressor.Status.NEED_INPUT:
                            Assert.Fail("Decompressor requested more input before reaching the stream end");
                            break;
                        default:
                            Assert.Fail("Unknown decompressor status");
                            break;
                    }
                }
            }
        }
        finally
        {
            output.Release();
        }
    }

    [Fact]
    public void TestHighlyCompressibleStreamIsFullyDecompressed()
    {
        // The tail of such a stream expands far beyond the number of remaining input bytes: the inflater pulls the
        // last input bytes into its internal state while it still has to emit data. getRemaining() is 0 by then,
        // so sizing the output buffer by it alone leaves the inflater without room to write the rest.
        byte[] expected = new byte[100000];
        Array.Fill(expected, (byte) 'a');
        byte[] compressed = Compress(expected);
        Assert.Equal(expected, JdkDecompress(compressed));
        Assert.Equal(expected, Decompress(CreateDecompressor().Build(IByteBufAllocator.Default), compressed));
    }

    [Fact]
    public void TestBadSecondGzipMagicRejected()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.GZIP, "wrapper == ZlibWrapper.GZIP");
        byte[] compressed = Gzip(Encoding.UTF8.GetBytes("bad magic"));
        compressed[1] = 0;
        IDecompressor decompressor = JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.GZIP)
                .Build(IByteBufAllocator.Default);

        try
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(
                    () => decompressor.AddInput(Unpooled.WrappedBuffer(compressed)));
        }
        finally
        {
            decompressor.Close();
        }
    }

    [Fact]
    public void TestGZIPDecodeWithExtraField()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.GZIP, "wrapper == ZlibWrapper.GZIP");
        byte[] data = Encoding.UTF8.GetBytes("Hello, gzip FEXTRA world!");
        byte[] extra = { 0x42, 0x43, 0x02, 0x00, 0x99, 0x00 };
        byte[] gzipWithExtra = GzipWithExtraField(data, extra);

        Assert.Equal(data, JdkGunzip(gzipWithExtra));
        Assert.Equal(data, Decompress(
                JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.GZIP).Build(IByteBufAllocator.Default),
                gzipWithExtra));
    }

    [Fact]
    public void TestConcatenatedGzipFirstStreamHasExtraField()
    {
        Assert.SkipUnless(wrapper == ZlibWrapper.GZIP, "wrapper == ZlibWrapper.GZIP");
        byte[] first = Encoding.UTF8.GetBytes("first stream");
        byte[] second = Encoding.UTF8.GetBytes("second stream");
        byte[] extra = { 0x42, 0x43, 0x02, 0x00, 0x99, 0x00 };
        byte[] firstGzip = GzipWithExtraField(first, extra);
        byte[] secondGzip = Gzip(second);
        byte[] compressed = new byte[firstGzip.Length + secondGzip.Length];
        Array.Copy(firstGzip, 0, compressed, 0, firstGzip.Length);
        Array.Copy(secondGzip, 0, compressed, firstGzip.Length, secondGzip.Length);

        Assert.Equal(Encoding.UTF8.GetBytes("first streamsecond stream"), Decompress(
                JdkZlibDecompressor.Builder_().Wrapper(ZlibWrapper.GZIP).DecompressConcatenated(true)
                        .Build(IByteBufAllocator.Default), compressed));
    }

    private static byte[] Decompress(IDecompressor decompressor, byte[] compressed)
    {
        CompositeByteBuf output = IByteBufAllocator.Default.CompositeBuffer();

        try
        {
            using (IDecompressor ignored = decompressor)
            {
                decompressor.Status_();
                decompressor.AddInput(Unpooled.WrappedBuffer(compressed));

                for (;;)
                {
                    switch (decompressor.Status_())
                    {
                        case IDecompressor.Status.NEED_INPUT:
                            decompressor.EndOfInput();
                            break;
                        case IDecompressor.Status.NEED_OUTPUT:
                            output.AddComponent(decompressor.TakeOutput(), true);
                            break;
                        case IDecompressor.Status.COMPLETE:
                            byte[] bytes = new byte[output.ReadableBytes];
                            output.ReadBytes(bytes.AsSpan());
                            return bytes;
                        default:
                            Assert.Fail("Unknown decompressor status");
                            break;
                    }
                }
            }
        }
        finally
        {
            output.Release();
        }
    }

    /**
     * Compress with the wrapper this test instance runs with, using the JDK so that the input is known-good
     * independently of netty's own encoders.
     */
    private byte[] Compress(byte[] data)
    {
        switch (wrapper)
        {
            case ZlibWrapper.GZIP:
                return Gzip(data);
            case ZlibWrapper.ZLIB:
                return Deflate(data);
            case ZlibWrapper.NONE:
                return RawDeflate(data);
            default:
                throw new Xunit.Sdk.XunitException("Unexpected wrapper: " + wrapper);
        }
    }

    /** Decompress with the JDK, to prove the compressed input is valid. */
    private byte[] JdkDecompress(byte[] compressed)
    {
        if (wrapper == ZlibWrapper.GZIP)
        {
            return JdkGunzip(compressed);
        }

        Stream inflater = wrapper == ZlibWrapper.NONE
                ? new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress)
                : new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);

        try
        {
            Stream input = inflater;
            MemoryStream output = new MemoryStream();
            byte[] buffer = new byte[256];
            int read;

            while ((read = input.Read(buffer)) != 0)
            {
                output.Write(buffer, 0, read);
            }

            input.Dispose();
            output.Dispose();

            return output.ToArray();
        }
        finally
        {
            inflater.Dispose();
        }
    }

    private static byte[] RawDeflate(byte[] data)
    {
        ZLibCompressionOptions deflater = new ZLibCompressionOptions { CompressionLevel = -1 };
        MemoryStream output = new MemoryStream();
        Stream stream = new DeflateStream(output, deflater);
        stream.Write(data);
        stream.Dispose();

        return output.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        MemoryStream output = new MemoryStream();
        GZipStream gzip = new GZipStream(output, CompressionMode.Compress);
        gzip.Write(data);
        gzip.Dispose();

        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        MemoryStream output = new MemoryStream();
        ZLibStream deflater = new ZLibStream(output, CompressionMode.Compress);
        deflater.Write(data);
        deflater.Dispose();

        return output.ToArray();
    }

    private static void AssertTruncated(IDecompressor decompressor, byte[] compressed)
    {
        try
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(Unpooled.WrappedBuffer(compressed));

            while (decompressor.Status_() == IDecompressor.Status.NEED_OUTPUT)
            {
                decompressor.TakeOutput().Release();
            }

            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(decompressor.EndOfInput);
        }
        finally
        {
            decompressor.Close();
        }
    }

    private static byte[] GzipWithExtraField(byte[] data, byte[] extra)
    {
        byte[] standard = Gzip(data);
        MemoryStream output = new MemoryStream();
        byte[] header = standard.AsSpan(0, 10).ToArray();
        header[3] |= 0x04;
        output.Write(header);
        output.WriteByte((byte) (extra.Length & 0xff));
        output.WriteByte((byte) ((extra.Length >>> 8) & 0xff));
        output.Write(extra);
        output.Write(standard, 10, standard.Length - 10);

        return output.ToArray();
    }

    private static byte[] JdkGunzip(byte[] gzip)
    {
        MemoryStream output = new MemoryStream();
        GZipStream input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        byte[] buffer = new byte[256];
        int read;

        while ((read = input.Read(buffer)) != 0)
        {
            output.Write(buffer, 0, read);
        }

        input.Dispose();

        return output.ToArray();
    }
}
