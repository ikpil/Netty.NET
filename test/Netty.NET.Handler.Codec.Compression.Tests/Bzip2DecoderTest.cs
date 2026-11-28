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
using ICSharpCode.SharpZipLib.BZip2;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited AbstractDecoderTest tests run for this class (createChannel and compress are the overrides
// CreateChannel and Compress). The anonymous Executables are lambdas and this::destroyChannel is the method group
// DestroyChannel; assertThrows with a message is Assert.Throws from Xunit.v3.AssertMessages (DecompressionException
// has no subclasses, so the exact-type check of Assert.Throws accepts what JUnit accepts). The (byte) casts of the DATA
// initializer are unsigned byte literals with the same bits; Arrays.copyOf(DATA, DATA.length) "Copies the specified
// array" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html) and is DATA.Clone().
// readInbound() is ReadInbound<ByteBuf>(), assertArrayEquals is Assert.Equal on byte arrays, and the numbers written with
// writeLong/writeMedium/writeInt and the char literals of writeByte are the same values.
public class Bzip2DecoderTest : AbstractDecoderTest
{

    private static readonly byte[] DATA = { 0x42, 0x5A, 0x68, 0x37, 0x31, 0x41, 0x59, 0x26, 0x53,
                                            0x59, 0x77, 0x7B, (byte) 0xCA, (byte) 0xC0, 0x00, 0x00,
                                            0x00, 0x05, (byte) 0x80, 0x00, 0x01, 0x02, 0x00, 0x04,
                                            0x20, 0x20, 0x00, 0x30, (byte) 0xCD, 0x34, 0x19, (byte) 0xA6,
                                            (byte) 0x89, (byte) 0x99, (byte) 0xC5, (byte) 0xDC, (byte) 0x91,
                                            0x4E, 0x14, 0x24, 0x1D, (byte) 0xDE, (byte) 0xF2, (byte) 0xB0, 0x00 };

    public Bzip2DecoderTest()
    {
    }

    protected override EmbeddedChannel CreateChannel()
    {
        return new EmbeddedChannel(new Bzip2Decoder());
    }

    private void WriteInboundDestroyAndExpectDecompressionException(ByteBuf @in)
    {
        try
        {
            channel.WriteInbound(@in);
        }
        finally
        {
            Assert.Throws<DecompressionException>(DestroyChannel);
        }
    }

    [Fact]
    public void TestUnexpectedStreamIdentifier()
    {
        ByteBuf @in = Unpooled.Buffer();
        @in.WriteLong(1823080128301928729L); //random value
        Assert.Throws<DecompressionException>(() =>
        {
            WriteInboundDestroyAndExpectDecompressionException(@in);
        }, "Unexpected stream identifier contents");
    }

    [Fact]
    public void TestInvalidBlockSize()
    {
        ByteBuf @in = Unpooled.Buffer();
        @in.WriteMedium(MAGIC_NUMBER);
        @in.WriteByte('0');  //incorrect block size

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        }, "block size is invalid");
    }

    [Fact]
    public void TestBadBlockHeader()
    {
        ByteBuf @in = Unpooled.Buffer();
        @in.WriteMedium(MAGIC_NUMBER);
        @in.WriteByte('1');  //block size
        @in.WriteMedium(11); //incorrect block header
        @in.WriteMedium(11); //incorrect block header
        @in.WriteInt(11111); //block CRC

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        }, "bad block header");
    }

    [Fact]
    public void TestStreamCrcErrorOfEmptyBlock()
    {
        ByteBuf @in = Unpooled.Buffer();
        @in.WriteMedium(MAGIC_NUMBER);
        @in.WriteByte('1');  //block size
        @in.WriteMedium(END_OF_STREAM_MAGIC_1);
        @in.WriteMedium(END_OF_STREAM_MAGIC_2);
        @in.WriteInt(1);  //wrong storedCombinedCRC

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        }, "stream CRC error");
    }

    [Fact]
    public void TestStreamCrcError()
    {
        byte[] data = (byte[]) DATA.Clone();
        data[41] = (byte) 0xDD;

        Assert.Throws<DecompressionException>(() =>
        {
            TryDecodeAndCatchBufLeaks(channel, Unpooled.WrappedBuffer(data));
        }, "stream CRC error");
    }

    [Fact]
    public void TestIncorrectHuffmanGroupsNumber()
    {
        byte[] data = (byte[]) DATA.Clone();
        data[25] = 0x70;

        ByteBuf @in = Unpooled.WrappedBuffer(data);
        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        }, "incorrect huffman groups number");
        // Leftover cumulation bytes get reprocessed from stale decoder state when the
        // channel is torn down, which can legitimately raise a second, unrelated exception.
        Assert.Throws<DecompressionException>(DestroyChannel);
    }

    [Fact]
    public void TestIncorrectSelectorsNumber()
    {
        byte[] data = (byte[]) DATA.Clone();
        data[25] = 0x2F;

        ByteBuf @in = Unpooled.WrappedBuffer(data);
        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        }, "incorrect selectors number");
        // Leftover cumulation bytes get reprocessed from stale decoder state when the
        // channel is torn down, which can legitimately raise a second, unrelated exception.
        Assert.Throws<DecompressionException>(DestroyChannel);
    }

    [Fact]
    public void TestBlockCrcError()
    {
        byte[] data = (byte[]) DATA.Clone();
        data[11] = 0x77;

        ByteBuf @in = Unpooled.WrappedBuffer(data);
        Assert.Throws<DecompressionException>(() =>
        {
            WriteInboundDestroyAndExpectDecompressionException(@in);
        }, "block CRC error");
    }

    [Fact]
    public void TestStartPointerInvalid()
    {
        byte[] data = (byte[]) DATA.Clone();
        data[14] = (byte) 0xFF;

        ByteBuf @in = Unpooled.WrappedBuffer(data);
        Assert.Throws<DecompressionException>(() =>
        {
            WriteInboundDestroyAndExpectDecompressionException(@in);
        }, "start pointer invalid");
    }

    /**
     * Regression test for the infinite-loop in {@link Bzip2BlockDecompressor#read()}.
     *
     * <p>The bzip2 block below is hand-crafted so that its inverse-BWT output is exactly
     * four consecutive 'A' bytes with no trailing run-length count byte. This is a
     * malformed-stream
     *
     * <p>Stream construction (bit-level, MSB first after the byte-aligned header):
     * <pre>
     *   Bytes  0– 3   "BZh1"                  stream header (block size 1 = 100 000 bytes)
     *   Bytes  4– 9   0x314159265359           block-header magic (pi)
     *   Bytes 10–13   0x00000000               block CRC — intentionally wrong; a
     *                                           DecompressionException from checkCRC() is the
     *                                           expected outcome after the fix is applied
     *   --- bit-level section (read via Bzip2BitReader, MSB-first) ---
     *    1 bit          randomized = 0
     *   24 bits         bwtStartPointer = 0
     *   16 bits         huffmanInUse16 = 0x0800 (group 4, bytes 0x40–0x4F present)
     *   16 bits         group-4 symbol bitmap = 0x4000 (only 0x41 = 'A' present)
     *    3 bits         totalTables = 2 (minimum allowed)
     *   15 bits         totalSelectors = 1
     *    1 bit          selector[0] unary-coded as 0 → table 0
     *    8 bits         table 0: initial length 2 (5 bits = 00010), then three 0-delta
     *                   bits for RUNA/RUNB/EOB → all code lengths = 2
     *    8 bits         table 1: identical to table 0
     *    6 bits         Huffman data: RUNB(01) RUNA(00) EOB(10)
     *                   RUNB first: repeatCount=2, RUNA: repeatCount=4, EOB flushes
     *                   4 copies of huffmanSymbolMap[0]='A' into the BWT block.
     *                   bwtBlockLength = 4 with NO trailing count byte → triggers the bug.
     *   48 bits         end-of-stream magic 0x177245 0x385090
     *   32 bits         stream CRC = 0
     * </pre>
     */
    // CLR: assertTimeoutPreemptively runs the executable "in a different thread than that of the calling code"
    // (https://docs.junit.org/5.10.0/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html) and fails with the
    // message when the timeout passes. The executable runs in Task.Run, and the test awaits it with
    // WaitAsync(TimeSpan, CancellationToken), whose task "should be faulted with a TimeoutException if it hasn't
    // otherwise completed" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0);
    // the TimeoutException is turned into Assert.Fail with the source message. A hung executable is not aborted (the CLR
    // has no thread abort), so the test method is async and does not block a thread on it.
    [Fact]
    public async Task TestRleOffByOneDoesNotCauseInfiniteLoop()
    {
        byte[] malformed = {
            0x42, 0x5A, 0x68, 0x31,                                    // "BZh1"
            0x31, 0x41, 0x59, 0x26, 0x53, 0x59,                        // block magic
            0x00, 0x00, 0x00, 0x00,                                     // block CRC
            // bit-level section (MSB-first packing, computed field-by-field):
            0x00, 0x00, 0x00, 0x04, 0x00, 0x20,                        // random+bwtPtr+inUse16 start
            0x00, 0x20, 0x00, 0x21, 0x01, 0x04,                        // inUse16 end+grp4+tbls+sel+tbl0
            (byte) 0x85, (byte) 0xDC, (byte) 0x91, 0x4E, 0x14, 0x24,  // tbl1+data+EOS magic
            0x00, 0x00, 0x00, 0x00, 0x00                               // stream CRC + padding
        };

        EmbeddedChannel ch = new EmbeddedChannel(new Bzip2Decoder());
        Task execution = Task.Run(() =>
        {
            Assert.Throws<DecompressionException>(() =>
            {
                ch.WriteInbound(Unpooled.WrappedBuffer(malformed));
                ch.FinishAndReleaseAll();
            });
        }, TestContext.Current.CancellationToken);

        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail("Bzip2Decoder hung: bwtBytesDecoded overshot bwtBlockLength and the " +
               "equality termination check was permanently false");
        }
    }

    /**
     * Verifies that a well-formed bzip2 block whose inverse-BWT output ends with exactly
     * four consecutive identical bytes followed by a run-length count byte is decoded
     * correctly and terminates cleanly.
     *
     * <p>Compressing {@code "AAAA"}: bzip2's pre-BWT RLE encodes a run of exactly 4
     * identical bytes as {@code [A,A,A,A,0x00]} (the four bytes plus count-byte 0,
     * meaning zero additional copies beyond the initial four).  The post-BWT RLE
     * decoder in {@link Bzip2BlockDecompressor#read()} must reach
     * {@code rleAccumulator == 4}, read the count byte, and then terminate cleanly
     * when {@code bwtBytesDecoded} reaches {@code bwtBlockLength == 5}.
     */
    [Fact]
    public void TestWellFormedFourByteRleRunAtBlockEnd()
    {
        byte[] compressed = Compress(new byte[]{(byte) 'A', (byte) 'A', (byte) 'A', (byte) 'A'});
        channel.WriteInbound(Unpooled.WrappedBuffer(compressed));
        ByteBuf decoded = channel.ReadInbound<ByteBuf>();
        Assert.NotNull(decoded, "expected decoded output for well-formed AAAA block");

        try
        {
            byte[] result = new byte[decoded.ReadableBytes];
            decoded.ReadBytes(result);
            Assert.Equal(new byte[]{(byte) 'A', (byte) 'A', (byte) 'A', (byte) 'A'}, result);
        }
        finally
        {
            decoded.Release();
        }
    }

    /**
     * Regression test: the unary-coded MTF selector index read in {@code RECEIVE_SELECTORS} was
     * never validated against the number of declared Huffman tables before being used to index
     * into {@link Bzip2MoveToFrontTable} and later the per-table code-limit array, allowing a
     * crafted stream to trigger an {@link ArrayIndexOutOfBoundsException} instead of a clean
     * {@link DecompressionException}.
     */
    [Fact]
    public void TestSelectorIndexOutOfRange()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(Bzip2MalformedStreams.SelectorIndexOutOfRange());
        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        }, "incorrect selector index");
        // Leftover cumulation bytes get reprocessed from stale decoder state when the
        // channel is torn down, which can legitimately raise a second, unrelated exception.
        Assert.Throws<DecompressionException>(DestroyChannel);
    }

    // CLR: commons-compress BZip2CompressorOutputStream(os, MIN_BLOCK_SIZE) is SharpZipLib's
    // BZip2OutputStream(Stream, int blockSize), the independent bzip2 implementation of the C# tests. Its block
    // size has the same meaning: "Valid block sizes are in the range 1..9, with 1 giving the lowest compression
    // and 9 the highest" (BZip2OutputStream constructor remarks,
    // https://raw.githubusercontent.com/icsharpcode/SharpZipLib/v1.4.2/src/ICSharpCode.SharpZipLib/BZip2/BZip2OutputStream.cs).
    // write(byte[]) is Write(data, 0, data.Length), and close() is Dispose, which on its first call ends the
    // block, writes the end-of-stream marker with the combined CRC and closes the underlying stream (IsStreamOwner
    // "The default value is true"). ByteArrayOutputStream is MemoryStream; ToArray "works when the MemoryStream is
    // closed" (https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.toarray?view=net-10.0).
    protected override byte[] Compress(byte[] data)
    {
        MemoryStream os = new MemoryStream();
        BZip2OutputStream bZip2Os = new BZip2OutputStream(os, MIN_BLOCK_SIZE);
        bZip2Os.Write(data, 0, data.Length);
        bZip2Os.Dispose();

        return os.ToArray();
    }
}
