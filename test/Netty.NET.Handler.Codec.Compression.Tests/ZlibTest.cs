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
using System.IO;
using System.IO.Compression;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit runs the inherited [Fact]/[Theory] methods of this abstract class for each derived test class
// (JdkZlibTest), as JUnit runs inherited @Test methods. The static initializer is the static constructor;
// ThreadLocalRandom.current() is Random.Shared, "a thread-safe Random instance that may be used concurrently from
// any thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0). CharsetUtil.UTF_8
// is Encoding.UTF8, readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>(), readBytes(byte[], int,
// int) is ReadBytes over the array span, and EmptyArrays.EMPTY_BYTES is the Common EmptyArrays. The java.util.zip
// test helpers are the BCL streams : GZIPOutputStream and DeflaterOutputStream with their default Deflater are
// GZipStream and ZLibStream in CompressionMode.Compress, whose CompressionLevel.Optimal default maps to zlib's
// default level as Java's Deflater.DEFAULT_COMPRESSION does (DeflateStream.cs,
// https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs),
// and GZIPInputStream is GZipStream in CompressionMode.Decompress; the tests compare inflated data only. The
// anonymous Executable is a lambda. assertThrows is Assert.Throws and AssertJ's
// assertThatThrownBy(...).isInstanceOf(...) is Assert.ThrowsAny, which also accepts subclasses;
// assertThat(...).isEqualTo(...) is Assert.Equal and hasMessageStartingWith is Assert.StartsWith.
// @CsvSource/@ValueSource are InlineData rows. JUnit asserts with messages use XunitAssertMessages.AssertM.
public abstract class ZlibTest
{

    private static readonly byte[] BYTES_SMALL = new byte[128];
    private static readonly byte[] BYTES_LARGE = new byte[1024 * 1024];
    private static readonly byte[] BYTES_LARGE2 = Encoding.UTF8.GetBytes("<!--?xml version=\"1.0\" encoding=\"ISO-8859-1\"?-->\n" +
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" " +
            "\"https://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">\n" +
            "<html xmlns=\"https://www.w3.org/1999/xhtml\" xml:lang=\"en\" lang=\"en\"><head>\n" +
            "    <title>Apache Tomcat</title>\n" +
            "</head>\n" +
            '\n' +
            "<body>\n" +
            "<h1>It works !</h1>\n" +
            '\n' +
            "<p>If you're seeing this page via a web browser, it means you've setup Tomcat successfully." +
            " Congratulations!</p>\n" +
            " \n" +
            "<p>This is the default Tomcat home page." +
            " It can be found on the local filesystem at: <code>/var/lib/tomcat7/webapps/ROOT/index.html</code></p>\n" +
            '\n' +
            "<p>Tomcat7 veterans might be pleased to learn that this system instance of Tomcat is installed with" +
            " <code>CATALINA_HOME</code> in <code>/usr/share/tomcat7</code> and <code>CATALINA_BASE</code> in" +
            " <code>/var/lib/tomcat7</code>, following the rules from" +
            " <code>/usr/share/doc/tomcat7-common/RUNNING.txt.gz</code>.</p>\n" +
            '\n' +
            "<p>You might consider installing the following packages, if you haven't already done so:</p>\n" +
            '\n' +
            "<p><b>tomcat7-docs</b>: This package installs a web application that allows to browse the Tomcat 7" +
            " documentation locally. Once installed, you can access it by clicking <a href=\"docs/\">here</a>.</p>\n" +
            '\n' +
            "<p><b>tomcat7-examples</b>: This package installs a web application that allows to access the Tomcat" +
            " 7 Servlet and JSP examples. Once installed, you can access it by clicking" +
            " <a href=\"examples/\">here</a>.</p>\n" +
            '\n' +
            "<p><b>tomcat7-admin</b>: This package installs two web applications that can help managing this Tomcat" +
            " instance. Once installed, you can access the <a href=\"manager/html\">manager webapp</a> and" +
            " the <a href=\"host-manager/html\">host-manager webapp</a>.</p><p>\n" +
            '\n' +
            "</p><p>NOTE: For security reasons, using the manager webapp is restricted" +
            " to users with role \"manager\"." +
            " The host-manager webapp is restricted to users with role \"admin\". Users are " +
            "defined in <code>/etc/tomcat7/tomcat-users.xml</code>.</p>\n" +
            '\n' +
            '\n' +
            '\n' +
            "</body></html>");

    static ZlibTest()
    {
        Random rand = Random.Shared;
        rand.NextBytes(BYTES_SMALL);
        rand.NextBytes(BYTES_LARGE);
    }

    protected virtual ZlibDecoder CreateDecoder(ZlibWrapper wrapper)
    {
        return CreateDecoder(wrapper, 0);
    }

    protected abstract ZlibEncoder CreateEncoder(ZlibWrapper wrapper);

    protected abstract ZlibDecoder CreateDecoder(ZlibWrapper wrapper, int maxAllocation);

    [Fact]
    public void TestGZIP2()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("message");
        ByteBuf data = Unpooled.WrappedBuffer(bytes);
        ByteBuf deflatedData = Unpooled.WrappedBuffer(Gzip(bytes));

        EmbeddedChannel chDecoderGZip = new EmbeddedChannel(CreateDecoder(ZlibWrapper.GZIP));

        try
        {
            while (deflatedData.IsReadable())
            {
                chDecoderGZip.WriteInbound(deflatedData.ReadRetainedSlice(1));
            }

            deflatedData.Release();
            Assert.True(chDecoderGZip.Finish());
            ByteBuf buf = Unpooled.Buffer();

            for (;;)
            {
                ByteBuf b = chDecoderGZip.ReadInbound<ByteBuf>();
                if (b == null)
                {
                    break;
                }

                buf.WriteBytes(b);
                b.Release();
            }

            Assert.Equal(buf, data);
            Assert.Null(chDecoderGZip.ReadInbound());
            data.Release();
            buf.Release();
        }
        finally
        {
            Dispose(chDecoderGZip);
        }
    }

    [Fact]
    public void TestGZIP3()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Foo");
        ByteBuf data = Unpooled.WrappedBuffer(bytes);
        // CLR: the signed Java byte literals of the fixture are written as the unsigned bytes with the same bits
        // (unchecked casts of the same values).
        ByteBuf deflatedData = Unpooled.WrappedBuffer(
                new byte[]
                {
                        31, unchecked((byte) -117), // magic number
                        8, // CM
                        2, // FLG.FHCRC
                        0, 0, 0, 0, // MTIME
                        0, // XFL
                        7, // OS
                        unchecked((byte) -66), unchecked((byte) -77), // CRC16
                        115, unchecked((byte) -53), unchecked((byte) -49), 7, 0, // compressed blocks
                        unchecked((byte) -63), 35, 62, unchecked((byte) -76), // CRC32
                        3, 0, 0, 0 // ISIZE
                }
        );

        EmbeddedChannel chDecoderGZip = new EmbeddedChannel(CreateDecoder(ZlibWrapper.GZIP));

        try
        {
            while (deflatedData.IsReadable())
            {
                chDecoderGZip.WriteInbound(deflatedData.ReadRetainedSlice(1));
            }

            deflatedData.Release();
            Assert.True(chDecoderGZip.Finish());
            ByteBuf buf = Unpooled.Buffer();

            for (;;)
            {
                ByteBuf b = chDecoderGZip.ReadInbound<ByteBuf>();
                if (b == null)
                {
                    break;
                }

                buf.WriteBytes(b);
                b.Release();
            }

            Assert.Equal(buf, data);
            Assert.Null(chDecoderGZip.ReadInbound());
            data.Release();
            buf.Release();
        }
        finally
        {
            Dispose(chDecoderGZip);
        }
    }

    private void TestCompress0(ZlibWrapper encoderWrapper, ZlibWrapper decoderWrapper, ByteBuf data)
    {
        EmbeddedChannel chEncoder = new EmbeddedChannel(CreateEncoder(encoderWrapper));
        EmbeddedChannel chDecoderZlib = new EmbeddedChannel(CreateDecoder(decoderWrapper));

        try
        {
            chEncoder.WriteOutbound(data.Retain());
            chEncoder.Flush();
            data.ResetReaderIndex();

            for (;;)
            {
                ByteBuf deflatedData = chEncoder.ReadOutbound<ByteBuf>();
                if (deflatedData == null)
                {
                    break;
                }

                chDecoderZlib.WriteInbound(deflatedData);
            }

            byte[] decompressed = new byte[data.ReadableBytes];
            int offset = 0;

            for (;;)
            {
                ByteBuf buf = chDecoderZlib.ReadInbound<ByteBuf>();
                if (buf == null)
                {
                    break;
                }

                int length = buf.ReadableBytes;
                buf.ReadBytes(decompressed.AsSpan(offset, length));
                offset += length;
                buf.Release();
                if (offset == decompressed.Length)
                {
                    break;
                }
            }

            Assert.Equal(data, Unpooled.WrappedBuffer(decompressed));
            Assert.Null(chDecoderZlib.ReadInbound());

            // Closing an encoder channel will generate a footer.
            Assert.True(chEncoder.Finish());

            for (;;)
            {
                object msg = chEncoder.ReadOutbound();
                if (msg == null)
                {
                    break;
                }

                ReferenceCountUtil.Release(msg);
            }

            // But, the footer will be decoded into nothing. It's only for validation.
            Assert.False(chDecoderZlib.Finish());

            data.Release();
        }
        finally
        {
            Dispose(chEncoder);
            Dispose(chDecoderZlib);
        }
    }

    private void TestCompressNone(ZlibWrapper encoderWrapper, ZlibWrapper decoderWrapper)
    {
        EmbeddedChannel chEncoder = new EmbeddedChannel(CreateEncoder(encoderWrapper));
        EmbeddedChannel chDecoderZlib = new EmbeddedChannel(CreateDecoder(decoderWrapper));

        try
        {
            // Closing an encoder channel without writing anything should generate both header and footer.
            Assert.True(chEncoder.Finish());

            for (;;)
            {
                ByteBuf deflatedData = chEncoder.ReadOutbound<ByteBuf>();
                if (deflatedData == null)
                {
                    break;
                }

                chDecoderZlib.WriteInbound(deflatedData);
            }

            // Decoder should not generate anything at all.
            bool decoded = false;

            for (;;)
            {
                ByteBuf buf = chDecoderZlib.ReadInbound<ByteBuf>();
                if (buf == null)
                {
                    break;
                }

                buf.Release();
                decoded = true;
            }

            Assert.False(decoded, "should decode nothing");

            Assert.False(chDecoderZlib.Finish());
        }
        finally
        {
            Dispose(chEncoder);
            Dispose(chDecoderZlib);
        }
    }

    private static void Dispose(EmbeddedChannel ch)
    {
        if (ch.Finish())
        {
            for (;;)
            {
                object msg = ch.ReadInbound();
                if (msg == null)
                {
                    break;
                }

                ReferenceCountUtil.Release(msg);
            }

            for (;;)
            {
                object msg = ch.ReadOutbound();
                if (msg == null)
                {
                    break;
                }

                ReferenceCountUtil.Release(msg);
            }
        }
    }

    // Test for https://github.com/netty/netty/issues/2572
    private void TestDecompressOnly(ZlibWrapper decoderWrapper, byte[] compressed, byte[] data)
    {
        EmbeddedChannel chDecoder = new EmbeddedChannel(CreateDecoder(decoderWrapper));
        chDecoder.WriteInbound(Unpooled.CopiedBuffer(compressed));
        Assert.True(chDecoder.Finish());

        ByteBuf decoded = Unpooled.Buffer(data.Length);

        for (;;)
        {
            ByteBuf buf = chDecoder.ReadInbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }

            decoded.WriteBytes(buf);
            buf.Release();
        }

        Assert.Equal(Unpooled.CopiedBuffer(data), decoded);
        decoded.Release();
    }

    private void TestCompressSmall(ZlibWrapper encoderWrapper, ZlibWrapper decoderWrapper)
    {
        TestCompress0(encoderWrapper, decoderWrapper, Unpooled.WrappedBuffer(BYTES_SMALL));
        TestCompress0(encoderWrapper, decoderWrapper,
                Unpooled.DirectBuffer(BYTES_SMALL.Length).WriteBytes(BYTES_SMALL));
    }

    private void TestCompressLarge(ZlibWrapper encoderWrapper, ZlibWrapper decoderWrapper)
    {
        TestCompress0(encoderWrapper, decoderWrapper, Unpooled.WrappedBuffer(BYTES_LARGE));
        TestCompress0(encoderWrapper, decoderWrapper,
                Unpooled.DirectBuffer(BYTES_LARGE.Length).WriteBytes(BYTES_LARGE));
    }

    [Fact]
    public void TestZLIB()
    {
        TestCompressNone(ZlibWrapper.ZLIB, ZlibWrapper.ZLIB);
        TestCompressSmall(ZlibWrapper.ZLIB, ZlibWrapper.ZLIB);
        TestCompressLarge(ZlibWrapper.ZLIB, ZlibWrapper.ZLIB);
        TestDecompressOnly(ZlibWrapper.ZLIB, Deflate(BYTES_LARGE2), BYTES_LARGE2);
    }

    [Fact]
    public void TestNONE()
    {
        TestCompressNone(ZlibWrapper.NONE, ZlibWrapper.NONE);
        TestCompressSmall(ZlibWrapper.NONE, ZlibWrapper.NONE);
        TestCompressLarge(ZlibWrapper.NONE, ZlibWrapper.NONE);
    }

    [Fact]
    public void TestGZIP()
    {
        TestCompressNone(ZlibWrapper.GZIP, ZlibWrapper.GZIP);
        TestCompressSmall(ZlibWrapper.GZIP, ZlibWrapper.GZIP);
        TestCompressLarge(ZlibWrapper.GZIP, ZlibWrapper.GZIP);
        TestDecompressOnly(ZlibWrapper.GZIP, Gzip(BYTES_LARGE2), BYTES_LARGE2);
    }

    [Fact]
    public void TestGZIPCompressOnly()
    {
        TestGZIPCompressOnly0(null); // Do not write anything; just finish the stream.
        TestGZIPCompressOnly0(EmptyArrays.EMPTY_BYTES); // Write an empty array.
        TestGZIPCompressOnly0(BYTES_SMALL);
        TestGZIPCompressOnly0(BYTES_LARGE);
    }

    // CLR: InputStream.read returns -1 at the end of the stream, Stream.Read returns 0
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0).
    private void TestGZIPCompressOnly0(byte[] data)
    {
        EmbeddedChannel chEncoder = new EmbeddedChannel(CreateEncoder(ZlibWrapper.GZIP));

        if (data != null)
        {
            chEncoder.WriteOutbound(Unpooled.WrappedBuffer(data));
        }

        Assert.True(chEncoder.Finish());

        ByteBuf encoded = Unpooled.Buffer();

        for (;;)
        {
            ByteBuf buf = chEncoder.ReadOutbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }

            encoded.WriteBytes(buf);
            buf.Release();
        }

        ByteBuf decoded = Unpooled.Buffer();

        using (GZipStream stream = new GZipStream(new ByteBufInputStream(encoded, true), CompressionMode.Decompress))
        {
            byte[] buf = new byte[8192];

            for (;;)
            {
                int readBytes = stream.Read(buf);
                if (readBytes <= 0)
                {
                    break;
                }

                decoded.WriteBytes(buf.AsSpan(0, readBytes));
            }
        }

        if (data != null)
        {
            Assert.Equal(Unpooled.WrappedBuffer(data), decoded);
        }
        else
        {
            Assert.False(decoded.IsReadable());
        }

        decoded.Release();
    }

    [Fact]
    public void TestZLIB_OR_NONE()
    {
        TestCompressNone(ZlibWrapper.NONE, ZlibWrapper.ZLIB_OR_NONE);
        TestCompressSmall(ZlibWrapper.NONE, ZlibWrapper.ZLIB_OR_NONE);
        TestCompressLarge(ZlibWrapper.NONE, ZlibWrapper.ZLIB_OR_NONE);
    }

    [Fact]
    public void TestZLIB_OR_NONE2()
    {
        TestCompressNone(ZlibWrapper.ZLIB, ZlibWrapper.ZLIB_OR_NONE);
        TestCompressSmall(ZlibWrapper.ZLIB, ZlibWrapper.ZLIB_OR_NONE);
        TestCompressLarge(ZlibWrapper.ZLIB, ZlibWrapper.ZLIB_OR_NONE);
    }

    // CLR: JdkZlibTest overrides this @Test method. The codec manifest checker maps an original test to a
    // "public void" [Fact] (tools/Test-CodecPortingManifest.ps1), so the [Fact] is not virtual: it runs the virtual
    // TestZLIB_OR_NONE3Core(), which holds the Java body here and the @Override body in JdkZlibTest. xUnit runs the
    // [Fact] once per derived class, which then runs the override, as JUnit runs JdkZlibTest.testZLIB_OR_NONE3().
    [Fact]
    public void TestZLIB_OR_NONE3()
    {
        TestZLIB_OR_NONE3Core();
    }

    protected virtual void TestZLIB_OR_NONE3Core()
    {
        TestCompressNone(ZlibWrapper.GZIP, ZlibWrapper.ZLIB_OR_NONE);
        TestCompressSmall(ZlibWrapper.GZIP, ZlibWrapper.ZLIB_OR_NONE);
        TestCompressLarge(ZlibWrapper.GZIP, ZlibWrapper.ZLIB_OR_NONE);
    }

    [Fact]
    public void TestMaxAllocation()
    {
        int maxAllocation = 1024;
        ZlibDecoder decoder = CreateDecoder(ZlibWrapper.ZLIB, maxAllocation);
        EmbeddedChannel chDecoder = new EmbeddedChannel(decoder);
        TestByteBufAllocator alloc = new TestByteBufAllocator(chDecoder.Alloc());
        chDecoder.Config.SetAllocator(alloc);

        DecompressionException e = Assert.Throws<DecompressionException>(() =>
        {
            chDecoder.WriteInbound(Unpooled.WrappedBuffer(Deflate(BYTES_LARGE)));
        });
        Assert.True(e.Message.StartsWith("Decompression buffer has reached maximum size", StringComparison.Ordinal));
        Assert.Equal(maxAllocation, alloc.GetMaxAllocation());
        Assert.True(decoder.IsClosed());
        Assert.False(chDecoder.Finish());
    }

    [Theory]
    [InlineData(ZlibWrapper.ZLIB, 2)]
    [InlineData(ZlibWrapper.ZLIB, 64)]
    [InlineData(ZlibWrapper.GZIP, 64)]
    [InlineData(ZlibWrapper.NONE, 64)]
    [InlineData(ZlibWrapper.ZLIB, 512)]
    [InlineData(ZlibWrapper.GZIP, 512)]
    [InlineData(ZlibWrapper.NONE, 512)]
    public void TestMaxAllocationWithInputSplitAcrossReads(ZlibWrapper wrapper, int maxAllocation)
    {
        // The whole output is smaller than maxAllocation, so no read can fill the buffer.
        byte[] data = BYTES_LARGE2.AsSpan(0, Math.Min(maxAllocation - 1, BYTES_LARGE2.Length)).ToArray();
        byte[] compressed = Compress(wrapper, data);

        Assert.Equal(data, DecodeInChunks(CreateDecoder(wrapper, maxAllocation), compressed, compressed.Length));
        Assert.Equal(data, DecodeInChunks(CreateDecoder(wrapper, maxAllocation), compressed, compressed.Length / 2 + 1));
        Assert.Equal(data, DecodeInChunks(CreateDecoder(wrapper, maxAllocation), compressed, 1));
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1000)]
    [InlineData(1024)]
    public void TestMaxAllocationWithIncompressibleInputInOneRead(int size)
    {
        // Random bytes do not compress, so the input is about as long as the output.
        byte[] data = BYTES_LARGE.AsSpan(0, size).ToArray();
        byte[] compressed = Compress(ZlibWrapper.ZLIB, data);

        Assert.Equal(data, DecodeInChunks(CreateDecoder(ZlibWrapper.ZLIB, 1024), compressed, compressed.Length));
    }

    [Theory]
    [InlineData(ZlibWrapper.ZLIB, 64)]
    [InlineData(ZlibWrapper.GZIP, 64)]
    [InlineData(ZlibWrapper.NONE, 64)]
    [InlineData(ZlibWrapper.ZLIB, 512)]
    public void TestMaxAllocationStillLimitsOutputOfOneRead(ZlibWrapper wrapper, int maxAllocation)
    {
        // A small input whose output is far larger than maxAllocation is still rejected.
        byte[] compressed = Compress(wrapper, new byte[65536]);
        EmbeddedChannel channel = new EmbeddedChannel(CreateDecoder(wrapper, maxAllocation));

        DecompressionException e = Assert.ThrowsAny<DecompressionException>(
                () => channel.WriteInbound(Unpooled.WrappedBuffer(compressed)));
        Assert.StartsWith("Decompression buffer has reached maximum size", e.Message);
        channel.FinishAndReleaseAll();
    }

    private byte[] Compress(ZlibWrapper wrapper, byte[] data)
    {
        EmbeddedChannel channel = new EmbeddedChannel(CreateEncoder(wrapper));
        channel.WriteOutbound(Unpooled.WrappedBuffer(data));
        channel.Finish();

        return ReadAll(channel, false);
    }

    private static byte[] DecodeInChunks(ZlibDecoder decoder, byte[] compressed, int chunkSize)
    {
        EmbeddedChannel channel = new EmbeddedChannel(decoder);

        for (int offset = 0; offset < compressed.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, compressed.Length - offset);
            channel.WriteInbound(Unpooled.WrappedBuffer(compressed, offset, length));
        }

        channel.Finish();

        return ReadAll(channel, true);
    }

    private static byte[] ReadAll(EmbeddedChannel channel, bool inbound)
    {
        ByteBuf all = Unpooled.Buffer();

        for (;;)
        {
            ByteBuf buf = inbound ? channel.ReadInbound<ByteBuf>() : channel.ReadOutbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }

            all.WriteBytes(buf);
            buf.Release();
        }

        byte[] bytes = new byte[all.ReadableBytes];
        all.ReadBytes(bytes.AsSpan());
        all.Release();
        return bytes;
    }

    private static byte[] Gzip(byte[] bytes)
    {
        MemoryStream @out = new MemoryStream();
        GZipStream stream = new GZipStream(@out, CompressionMode.Compress);
        stream.Write(bytes);
        stream.Dispose();

        return @out.ToArray();
    }

    private static byte[] Deflate(byte[] bytes)
    {
        MemoryStream @out = new MemoryStream();

        using (Stream stream = new ZLibStream(@out, CompressionMode.Compress))
        {
            stream.Write(bytes);
        }

        return @out.ToArray();
    }

    private sealed class TestByteBufAllocator : AbstractByteBufAllocator
    {
        private readonly IByteBufAllocator wrapped;
        private int maxAllocation;

        internal TestByteBufAllocator(IByteBufAllocator wrapped)
        {
            this.wrapped = wrapped;
        }

        public int GetMaxAllocation()
        {
            return maxAllocation;
        }

        public override bool IsDirectBufferPooled => wrapped.IsDirectBufferPooled;

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            maxAllocation = Math.Max(maxAllocation, maxCapacity);
            return wrapped.HeapBuffer(initialCapacity, maxCapacity);
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            maxAllocation = Math.Max(maxAllocation, maxCapacity);
            return wrapped.DirectBuffer(initialCapacity, maxCapacity);
        }
    }
}
