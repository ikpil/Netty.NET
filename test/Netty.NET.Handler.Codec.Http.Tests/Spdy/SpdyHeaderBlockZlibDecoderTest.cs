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
using System.IO.Compression;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Spdy;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

#pragma warning disable xUnit2013 // assertEquals(n, collection.size()) of the source stays Assert.Equal(n, Count).

// CLR: The test helper deflate(byte[]) uses java.util.zip.Deflater(BEST_COMPRESSION) with the zlib wrapper; its
// BCL counterpart is a ZLibStream with ZLibCompressionOptions.CompressionLevel 9, which "gives best compression"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0),
// over a MemoryStream (ByteArrayOutputStream), finished by Dispose. The (byte) casts of the header bytes keep the
// octets.
public class SpdyHeaderBlockZlibDecoderTest : IDisposable
{

    // zlib header indicating 32K window size fastest deflate algorithm with SPDY dictionary
    private static readonly byte[] zlibHeader = {0x78, 0x3f, (byte) 0xe3, (byte) 0xc6, (byte) 0xa7, (byte) 0xc2};
    private static readonly byte[] zlibSyncFlush = {0x00, 0x00, 0x00, (byte) 0xff, (byte) 0xff};

    private const int maxHeaderSize = 8192;

    private static readonly long MAX_DECOMPRESSED_SIZE =
            SpdyHeaderBlockZlibDecoder.CalculateMaxDecompressedSizePerBlock(maxHeaderSize);

    private const string name = "name";
    private const string value = "value";
    private static readonly byte[] nameBytes = Encoding.UTF8.GetBytes(name);
    private static readonly byte[] valueBytes = Encoding.UTF8.GetBytes(value);

    private SpdyHeaderBlockZlibDecoder decoder;
    private ISpdyHeadersFrame frame;

    public SpdyHeaderBlockZlibDecoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        decoder = new SpdyHeaderBlockZlibDecoder(SpdyVersion.SPDY_3_1, maxHeaderSize);
        frame = new DefaultSpdyHeadersFrame(1);
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        decoder.End();
    }

    [Fact]
    public void TestHeaderBlock()
    {
        ByteBuf headerBlock = Unpooled.Buffer(37);
        headerBlock.WriteBytes(zlibHeader);
        headerBlock.WriteByte(0); // Non-compressed block
        headerBlock.WriteByte(0x15); // little-endian length (21)
        headerBlock.WriteByte(0x00); // little-endian length (21)
        headerBlock.WriteByte(0xea); // one's compliment of length
        headerBlock.WriteByte(0xff); // one's compliment of length
        headerBlock.WriteInt(1); // number of Name/Value pairs
        headerBlock.WriteInt(4); // length of name
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5); // length of value
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteBytes(zlibSyncFlush);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));

        headerBlock.Release();
    }

    [Fact]
    public void TestHeaderBlockMultipleDecodes()
    {
        ByteBuf headerBlock = Unpooled.Buffer(37);
        headerBlock.WriteBytes(zlibHeader);
        headerBlock.WriteByte(0); // Non-compressed block
        headerBlock.WriteByte(0x15); // little-endian length (21)
        headerBlock.WriteByte(0x00); // little-endian length (21)
        headerBlock.WriteByte(0xea); // one's compliment of length
        headerBlock.WriteByte(0xff); // one's compliment of length
        headerBlock.WriteInt(1); // number of Name/Value pairs
        headerBlock.WriteInt(4); // length of name
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5); // length of value
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteBytes(zlibSyncFlush);

        int readableBytes = headerBlock.ReadableBytes;

        for (int i = 0; i < readableBytes; i++)
        {
            ByteBuf headerBlockSegment = headerBlock.Slice(i, 1);
            decoder.Decode(IByteBufAllocator.Default, headerBlockSegment, frame);
            Assert.False(headerBlockSegment.IsReadable());
        }

        decoder.EndHeaderBlock(frame);

        Assert.False(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));

        headerBlock.Release();
    }

    [Fact]
    public void TestLargeHeaderName()
    {
        ByteBuf headerBlock = Unpooled.Buffer(8220);
        headerBlock.WriteBytes(zlibHeader);
        headerBlock.WriteByte(0); // Non-compressed block
        headerBlock.WriteByte(0x0c); // little-endian length (8204)
        headerBlock.WriteByte(0x20); // little-endian length (8204)
        headerBlock.WriteByte(0xf3); // one's compliment of length
        headerBlock.WriteByte(0xdf); // one's compliment of length
        headerBlock.WriteInt(1); // number of Name/Value pairs
        headerBlock.WriteInt(8192); // length of name

        for (int i = 0; i < 8192; i++)
        {
            headerBlock.WriteByte('n');
        }

        headerBlock.WriteInt(0); // length of value
        headerBlock.WriteBytes(zlibSyncFlush);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.False(frame.IsTruncated());
        Assert.Equal(1, frame.Headers().Names().Count);

        headerBlock.Release();
    }

    [Fact]
    public void TestLargeHeaderValue()
    {
        ByteBuf headerBlock = Unpooled.Buffer(8220);
        headerBlock.WriteBytes(zlibHeader);
        headerBlock.WriteByte(0); // Non-compressed block
        headerBlock.WriteByte(0x0c); // little-endian length (8204)
        headerBlock.WriteByte(0x20); // little-endian length (8204)
        headerBlock.WriteByte(0xf3); // one's compliment of length
        headerBlock.WriteByte(0xdf); // one's compliment of length
        headerBlock.WriteInt(1); // number of Name/Value pairs
        headerBlock.WriteInt(1); // length of name
        headerBlock.WriteByte('n');
        headerBlock.WriteInt(8191); // length of value

        for (int i = 0; i < 8191; i++)
        {
            headerBlock.WriteByte('v');
        }

        headerBlock.WriteBytes(zlibSyncFlush);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.False(frame.IsTruncated());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.Equal(8191, frame.Headers().Get(Of("n")).Length());

        headerBlock.Release();
    }

    [Fact]
    public void TestHeaderBlockExtraData()
    {
        ByteBuf headerBlock = Unpooled.Buffer(37);
        headerBlock.WriteBytes(zlibHeader);
        headerBlock.WriteByte(0); // Non-compressed block
        headerBlock.WriteByte(0x15); // little-endian length (21)
        headerBlock.WriteByte(0x00); // little-endian length (21)
        headerBlock.WriteByte(0xea); // one's compliment of length
        headerBlock.WriteByte(0xff); // one's compliment of length
        headerBlock.WriteInt(1); // number of Name/Value pairs
        headerBlock.WriteInt(4); // length of name
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5); // length of value
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0x19); // adler-32 checksum
        headerBlock.WriteByte(0xa5); // adler-32 checksum
        headerBlock.WriteByte(0x03); // adler-32 checksum
        headerBlock.WriteByte(0xc9); // adler-32 checksum
        headerBlock.WriteByte(0); // Data following zlib stream

        Assert.ThrowsAny<SpdyProtocolException>(
                () => decoder.Decode(IByteBufAllocator.Default, headerBlock, frame));

        headerBlock.Release();
    }

    [Fact]
    public void TestHeaderBlockInvalidDictionary()
    {
        ByteBuf headerBlock = Unpooled.Buffer(7);
        headerBlock.WriteByte(0x78);
        headerBlock.WriteByte(0x3f);
        headerBlock.WriteByte(0x01); // Unknown dictionary
        headerBlock.WriteByte(0x02); // Unknown dictionary
        headerBlock.WriteByte(0x03); // Unknown dictionary
        headerBlock.WriteByte(0x04); // Unknown dictionary
        headerBlock.WriteByte(0); // Non-compressed block

        Assert.ThrowsAny<SpdyProtocolException>(
                () => decoder.Decode(IByteBufAllocator.Default, headerBlock, frame));

        headerBlock.Release();
    }

    [Fact]
    public void TestHeaderBlockInvalidDeflateBlock()
    {
        ByteBuf headerBlock = Unpooled.Buffer(11);
        headerBlock.WriteBytes(zlibHeader);
        headerBlock.WriteByte(0); // Non-compressed block
        headerBlock.WriteByte(0x00); // little-endian length (0)
        headerBlock.WriteByte(0x00); // little-endian length (0)
        headerBlock.WriteByte(0x00); // invalid one's compliment
        headerBlock.WriteByte(0x00); // invalid one's compliment

        Assert.ThrowsAny<SpdyProtocolException>(
                () => decoder.Decode(IByteBufAllocator.Default, headerBlock, frame));

        headerBlock.Release();
    }

    [Fact]
    public void TestHeaderBlockAtDecompressionCapIsAccepted()
    {
        long valueLength = MAX_DECOMPRESSED_SIZE - HeaderBlockOverhead();
        ByteBuf headerBlock = Unpooled.WrappedBuffer(Deflate(BuildHeaderBlock((int) valueLength)));

        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(frame.IsInvalid());
        Assert.True(frame.IsTruncated());

        headerBlock.Release();
    }

    [Fact]
    public void TestHeaderBlockExceedingDecompressionCapThrows()
    {
        long valueLength = MAX_DECOMPRESSED_SIZE - HeaderBlockOverhead() + 1;
        ByteBuf headerBlock = Unpooled.WrappedBuffer(Deflate(BuildHeaderBlock((int) valueLength)));

        Assert.ThrowsAny<SpdyProtocolException>(
                () => decoder.Decode(IByteBufAllocator.Default, headerBlock, frame));

        headerBlock.Release();
    }

    // 4 bytes for the header count, 4 bytes for the name length, the 1-byte name itself,
    // and 4 bytes for the value length: everything but the value payload.
    private static long HeaderBlockOverhead()
    {
        return 4 + 4 + 1 + 4;
    }

    private static byte[] BuildHeaderBlock(int valueLength)
    {
        ByteBuf buf = Unpooled.Buffer((int) HeaderBlockOverhead() + valueLength);
        buf.WriteInt(1); // number of Name/Value pairs
        buf.WriteInt(1); // length of name
        buf.WriteByte('n');
        buf.WriteInt(valueLength);
        buf.WriteZero(valueLength);

        byte[] bytes = new byte[buf.ReadableBytes];
        buf.ReadBytes(bytes);
        buf.Release();
        return bytes;
    }

    private static byte[] Deflate(byte[] input)
    {
        MemoryStream @out = new MemoryStream();

        using (ZLibStream deflater = new ZLibStream(@out, new ZLibCompressionOptions { CompressionLevel = 9 }))
        {
            deflater.Write(input, 0, input.Length);
        }

        return @out.ToArray();
    }
}
