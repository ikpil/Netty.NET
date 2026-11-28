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
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: java.util.zip.Inflater is codec-compression's internal stand-in Inflater (InternalsVisibleTo; owner
// decisions 2026-11-23 and 2026-11-24), which implements needsDictionary()/setDictionary() with java.util.zip
// semantics on the BCL streams. It is created with exactRemaining (new Inflater(false, true)): a SPDY header block
// stream sets FDICT, after which the stand-in reads the deflate data one byte per Read and getRemaining() is exact
// anyway; exactRemaining makes getRemaining() exact for a stream without FDICT too, so a complete zlib stream
// followed by more bytes is an invalid header block as in Java. DataFormatException is the InvalidDataException
// the stand-in throws, and setDictionary's IllegalArgumentException is ArgumentException. The Buffer port has no
// hasArray()/array()/arrayOffset(); the readable range of one managed array (AsReadOnlySequence and
// MemoryMarshal.TryGetArray) replaces them, as in codec-compression's JdkZlibDecoder, see TryGetArray and ArrayOf
// below. ByteBufAllocator is IByteBufAllocator. The number in the exception text uses the invariant culture.
internal sealed class SpdyHeaderBlockZlibDecoder : SpdyHeaderBlockRawDecoder
{

    private const int DEFAULT_BUFFER_CAPACITY = 4096;
    private static readonly SpdyProtocolException INVALID_HEADER_BLOCK =
            new SpdyProtocolException("Invalid Header Block");

    // Legitimate header blocks are bounded by maxHeaderSize, but framing overhead (the header
    // count and the per-header name/value length prefixes) means the decompressed size of a
    // wire-legitimate header block can exceed maxHeaderSize itself. A generous multiplier is used
    // instead of an equal cap so this never rejects well-formed header blocks, while still
    // preventing a maliciously compressible header block from forcing an effectively unbounded
    // number of Inflater#inflate() calls on the calling (event-loop) thread.
    private const int MAX_DECOMPRESSED_SIZE_MULTIPLIER = 16;

    private readonly Inflater decompressor = new Inflater(false, true);
    private readonly long maxDecompressedSize;

    private ByteBuf decompressed;
    private long totalInflated;

    internal SpdyHeaderBlockZlibDecoder(SpdyVersion spdyVersion, int maxHeaderSize)
        : base(spdyVersion, maxHeaderSize)
    {
        maxDecompressedSize = CalculateMaxDecompressedSizePerBlock(maxHeaderSize);
    }

    internal static long CalculateMaxDecompressedSizePerBlock(int maxHeaderSize)
    {
        return (long) maxHeaderSize * MAX_DECOMPRESSED_SIZE_MULTIPLIER;
    }

    internal override void Decode(IByteBufAllocator alloc, ByteBuf headerBlock, ISpdyHeadersFrame frame)
    {
        int len = SetInput(headerBlock);

        int numBytes;

        do
        {
            numBytes = Decompress(alloc, frame);
            totalInflated += numBytes;
            if (totalInflated > maxDecompressedSize)
            {
                throw new SpdyProtocolException(
                        "Decompressed header block exceeds "
                        + maxDecompressedSize.ToString(CultureInfo.InvariantCulture) + " bytes");
            }
        } while (numBytes > 0);

        // z_stream has an internal 64-bit hold buffer
        // it is always capable of consuming the entire input
        if (decompressor.GetRemaining() != 0)
        {
            // we reached the end of the deflate stream
            throw INVALID_HEADER_BLOCK;
        }

        headerBlock.SkipBytes(len);
    }

    private int SetInput(ByteBuf compressed)
    {
        int len = compressed.ReadableBytes;

        if (TryGetArray(compressed, out ArraySegment<byte> array))
        {
            decompressor.SetInput(array.Array, array.Offset, len);
        }
        else
        {
            byte[] @in = new byte[len];
            compressed.GetBytes(compressed.ReaderIndex, @in);
            decompressor.SetInput(@in, 0, @in.Length);
        }

        return len;
    }

    private int Decompress(IByteBufAllocator alloc, ISpdyHeadersFrame frame)
    {
        EnsureBuffer(alloc);
        ArraySegment<byte> outSegment = ArrayOf(decompressed, decompressed.WriterIndex, decompressed.WritableBytes);
        byte[] @out = outSegment.Array;
        int off = outSegment.Offset;

        try
        {
            int numBytes = decompressor.Inflate(@out, off, decompressed.WritableBytes);
            if (numBytes == 0 && decompressor.NeedsDictionary())
            {
                try
                {
                    decompressor.SetDictionary(SPDY_DICT);
                }
                catch (ArgumentException)
                {
                    throw INVALID_HEADER_BLOCK;
                }

                numBytes = decompressor.Inflate(@out, off, decompressed.WritableBytes);
            }

            if (frame != null)
            {
                decompressed.WriterIndex = decompressed.WriterIndex + numBytes;
                DecodeHeaderBlock(decompressed, frame);
                decompressed.DiscardReadBytes();
            }

            return numBytes;
        }
        catch (InvalidDataException e)
        {
            throw new SpdyProtocolException("Received invalid header block", e);
        }
    }

    private void EnsureBuffer(IByteBufAllocator alloc)
    {
        if (decompressed == null)
        {
            decompressed = alloc.HeapBuffer(DEFAULT_BUFFER_CAPACITY);
        }

        decompressed.EnsureWritable(1);
    }

    internal override void EndHeaderBlock(ISpdyHeadersFrame frame)
    {
        base.EndHeaderBlock(frame);
        ReleaseBuffer();
        totalInflated = 0;
    }

    internal override void End()
    {
        base.End();
        ReleaseBuffer();
        decompressor.End();
    }

    private void ReleaseBuffer()
    {
        if (decompressed != null)
        {
            decompressed.Release();
            decompressed = null;
        }
    }

    // CLR: hasArray(), array() and arrayOffset() + readerIndex of the readable range: one segment backed by a managed
    // array, as codec-compression's JdkZlibDecoder.TryGetArray. A range in one array of a composite buffer also takes
    // this branch where Java's hasArray() is false; both branches give the inflater the same bytes.
    private static bool TryGetArray(ByteBuf @in, out ArraySegment<byte> array)
    {
        ReadOnlySequence<byte> range = @in.AsReadOnlySequence(@in.ReaderIndex, @in.ReadableBytes);
        if (range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out array))
        {
            return true;
        }

        array = default;
        return false;
    }

    // CLR: array() and arrayOffset() + index of the heap output buffer: the array segment that starts at buffer index
    // index, as codec-compression's JdkZlibDecoder.ArrayOf. Like array(), it throws NotSupportedException (Java:
    // UnsupportedOperationException) when the range is not backed by one managed array; the buffer comes from
    // heapBuffer(...).
    private static ArraySegment<byte> ArrayOf(ByteBuf buf, int index, int length)
    {
        ReadOnlySequence<byte> range = buf.AsReadOnlySequence(index, length);
        if (range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out ArraySegment<byte> array))
        {
            return array;
        }

        throw new NotSupportedException("the buffer range is not backed by one managed array");
    }
}
