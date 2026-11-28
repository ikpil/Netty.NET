/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: owner decision 2026-11-23: brotli4j's BrotliOutputStream, which produces the reference data, is the BCL
// BrotliStream, which compresses "streams by using the Brotli data format specification"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream?view=net-10.0).
// new BrotliOutputStream(os) uses new Encoder.Parameters(), whose quality and window are -1, so brotli4j leaves them at
// the native defaults BROTLI_DEFAULT_QUALITY 11 and BROTLI_DEFAULT_WINDOW 22
// (https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/natives/src/main/cpp/encoder_jni.cc,
// https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli/include/brotli/encode.h). The BrotliStream
// therefore gets BrotliCompressionOptions with Quality 11 ("The range is from 0 to 11", default 4,
// https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlicompressionoptions.quality?view=net-10.0);
// its window is the BCL default 22 (BrotliUtils.WindowBits_Default,
// https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Compression.Brotli/src/System/IO/Compression/BrotliUtils.cs).
// ByteArrayOutputStream is MemoryStream and close() is Stream.Close(), which also closes the MemoryStream (leaveOpen is
// false); ToArray() "works when the MemoryStream is closed"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.toarray?view=net-10.0). xUnit creates a new
// test class instance for every test, so the @BeforeEach initChannel() runs from the constructor and the @AfterEach destroyChannel() from Dispose() (https://xunit.net/docs/shared-context); both are
// private, because xUnit1013 asks public methods of a test class to be tests (as in ByteBufChecksumTest). The static
// @BeforeAll setUp() runs from the static constructor, after the static field initializers (as the Java class
// initializer runs before @BeforeAll), and an exception there fails every test as TypeInitializationException, the
// counterpart of ExceptionInInitializerError, so the try/catch is not repeated. The static @MethodSource factories
// smallData()/largeData() are member data whose rows hold the heap and the direct buffer (object[] rows), with
// DisableDiscoveryEnumeration so that the buffers are only created when the theory runs, as in AbstractEncoderTest.
// SplittableRandom is not ported: new SplittableRandom(RANDOM.nextLong()) is new Random(RANDOM.Next()), nextInt(100) is
// Next(100), and (byte) rng.nextInt() keeps the low eight bits of Next(), as in AbstractCompressionTest; the conditional
// casts its 0 to byte, because C# gives i % 4 != 0 ? 0 : (byte) x the type int. readInbound() is ReadInbound<ByteBuf>()
// and the Buffer port's addComponent(true, buffer) is AddComponent(buffer, true).
public class BrotliDecoderTest : IDisposable
{

    private static Random RANDOM;
    private static readonly byte[] BYTES_SMALL = new byte[256];
    private static readonly byte[] BYTES_LARGE = new byte[256 * 1024];
    private static byte[] COMPRESSED_BYTES_SMALL;
    private static byte[] COMPRESSED_BYTES_LARGE;

    static BrotliDecoderTest()
    {
        SetUp();
    }

    private static void SetUp()
    {
        Brotli.EnsureAvailability();

        RANDOM = new Random();
        FillArrayWithCompressibleData(BYTES_SMALL);
        FillArrayWithCompressibleData(BYTES_LARGE);
        COMPRESSED_BYTES_SMALL = Compress(BYTES_SMALL);
        COMPRESSED_BYTES_LARGE = Compress(BYTES_LARGE);
    }

    private static readonly ByteBuf WRAPPED_BYTES_SMALL = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(BYTES_SMALL)).AsReadOnly();
    private static readonly ByteBuf WRAPPED_BYTES_LARGE = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(BYTES_LARGE)).AsReadOnly();

    private static void FillArrayWithCompressibleData(byte[] array)
    {
        Random rng = new Random(RANDOM.Next());

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i % 4 != 0 ? (byte) 0 : (byte) rng.Next();
        }
    }

    private static byte[] Compress(byte[] data)
    {
        MemoryStream os = new MemoryStream();
        BrotliStream brotliOs = new BrotliStream(os, new BrotliCompressionOptions { Quality = 11 });
        brotliOs.Write(data);
        brotliOs.Close();

        return os.ToArray();
    }

    private EmbeddedChannel channel;

    public BrotliDecoderTest()
    {
        InitChannel();
    }

    public void Dispose()
    {
        DestroyChannel();
    }

    private void InitChannel()
    {
        channel = new EmbeddedChannel(new BrotliDecoder());
    }

    private void DestroyChannel()
    {
        if (channel != null)
        {
            channel.FinishAndReleaseAll();
            channel = null;
        }
    }

    public static IEnumerable<object[]> SmallData()
    {
        ByteBuf heap = Unpooled.WrappedBuffer(COMPRESSED_BYTES_SMALL);
        ByteBuf direct = Unpooled.DirectBuffer(COMPRESSED_BYTES_SMALL.Length);
        direct.WriteBytes(COMPRESSED_BYTES_SMALL);
        return new[] { new object[] {heap}, new object[] {direct} };
    }

    public static IEnumerable<object[]> LargeData()
    {
        ByteBuf heap = Unpooled.WrappedBuffer(COMPRESSED_BYTES_LARGE);
        ByteBuf direct = Unpooled.DirectBuffer(COMPRESSED_BYTES_LARGE.Length);
        direct.WriteBytes(COMPRESSED_BYTES_LARGE);
        return new[] { new object[] {heap}, new object[] {direct} };
    }

    [Theory]
    [MemberData(nameof(SmallData), DisableDiscoveryEnumeration = true)]
    public void TestDecompressionOfSmallChunkOfData(ByteBuf data)
    {
        TestDecompression(WRAPPED_BYTES_SMALL.Duplicate(), data);
    }

    [Theory]
    [MemberData(nameof(LargeData), DisableDiscoveryEnumeration = true)]
    public void TestDecompressionOfLargeChunkOfData(ByteBuf data)
    {
        TestDecompression(WRAPPED_BYTES_LARGE.Duplicate(), data);
    }

    [Theory]
    [MemberData(nameof(LargeData), DisableDiscoveryEnumeration = true)]
    public void TestDecompressionOfBatchedFlowOfData(ByteBuf data)
    {
        TestDecompressionOfBatchedFlow(WRAPPED_BYTES_LARGE, data);
    }

    private void TestDecompression(ByteBuf expected, ByteBuf data)
    {
        Assert.True(channel.WriteInbound(data));

        ByteBuf decompressed = ReadDecompressed(channel);
        Assert.Equal(expected, decompressed);

        decompressed.Release();
    }

    // CLR: the loop's compressedBuf and the later compressedBuf are one local declared before the loop, because C#
    // reports a nested local of the same name "regardless of the textual order of the variable declarations"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136); written and length are declared on one line as in
    // the source.
    private void TestDecompressionOfBatchedFlow(ByteBuf expected, ByteBuf data)
    {
        int compressedLength = data.ReadableBytes;
        Random rng = new Random(RANDOM.Next());
        int written = 0, length = rng.Next(100);
        ByteBuf compressedBuf;

        while (written + length < compressedLength)
        {
            compressedBuf = data.RetainedSlice(written, length);
            channel.WriteInbound(compressedBuf);
            written += length;
            length = rng.Next(100);
        }

        compressedBuf = data.Slice(written, compressedLength - written);
        Assert.True(channel.WriteInbound(compressedBuf.Retain()));

        ByteBuf decompressedBuf = ReadDecompressed(channel);
        Assert.Equal(expected, decompressedBuf);

        decompressedBuf.Release();
        data.Release();
    }

    private static ByteBuf ReadDecompressed(EmbeddedChannel channel)
    {
        CompositeByteBuf decompressed = Unpooled.CompositeBuffer();
        ByteBuf msg;

        while ((msg = channel.ReadInbound<ByteBuf>()) != null)
        {
            decompressed.AddComponent(msg, true);
        }

        return decompressed;
    }
}
