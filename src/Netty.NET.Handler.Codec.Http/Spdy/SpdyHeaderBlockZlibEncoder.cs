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
using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: java.util.zip.Deflater is codec-compression's internal stand-in Deflater (InternalsVisibleTo; owner
// decisions 2026-11-23 and 2026-11-24), whose setDictionary() writes the zlib header with FDICT and the
// dictionary's Adler-32 and keeps the dictionary as history on the BCL DeflateStream. The compressed bytes can
// differ from Java's zlib (.NET uses zlib-ng); the format and the inflated data are the same. The Buffer port has
// no hasArray()/array()/arrayOffset(); the array segment of one managed array (AsReadOnlySequence and
// MemoryMarshal.TryGetArray) replaces them, as in codec-compression's JdkZlibEncoder, see TryGetArray and ArrayOf
// below. IllegalArgumentException is ArgumentException and checkNotNullWithIAE is ObjectUtil.CheckNotNullWithIAE.
// writerIndex(int) is the WriterIndex setter and capacity() is Capacity. Numbers in the exception text use the
// invariant culture.
internal class SpdyHeaderBlockZlibEncoder : SpdyHeaderBlockRawEncoder
{

    private readonly Deflater compressor;

    private bool finished;

    internal SpdyHeaderBlockZlibEncoder(SpdyVersion spdyVersion, int compressionLevel) : base(spdyVersion)
    {
        if (compressionLevel < 0 || compressionLevel > 9)
        {
            throw new ArgumentException(
                    "compressionLevel: " + compressionLevel.ToString(CultureInfo.InvariantCulture)
                    + " (expected: 0-9)");
        }

        compressor = new Deflater(compressionLevel);
        compressor.SetDictionary(SPDY_DICT);
    }

    private int SetInput(ByteBuf decompressed)
    {
        int len = decompressed.ReadableBytes;

        if (TryGetArray(decompressed, out ArraySegment<byte> array))
        {
            compressor.SetInput(array.Array, array.Offset, len);
        }
        else
        {
            byte[] @in = new byte[len];
            decompressed.GetBytes(decompressed.ReaderIndex, @in);
            compressor.SetInput(@in, 0, @in.Length);
        }

        return len;
    }

    private ByteBuf Encode(IByteBufAllocator alloc, int len)
    {
        ByteBuf compressed = alloc.HeapBuffer(len);
        bool release = true;

        try
        {
            while (CompressInto(compressed))
            {
                // Although unlikely, it's possible that the compressed size is larger than the decompressed size
                compressed.EnsureWritable(compressed.Capacity << 1);
            }

            release = false;
            return compressed;
        }
        finally
        {
            if (release)
            {
                compressed.Release();
            }
        }
    }

    private bool CompressInto(ByteBuf compressed)
    {
        int toWrite = compressed.WritableBytes;
        ArraySegment<byte> outSegment = ArrayOf(compressed, compressed.WriterIndex, toWrite);
        byte[] @out = outSegment.Array;
        int off = outSegment.Offset;
        int numBytes = compressor.Deflate(@out, off, toWrite, Deflater.SYNC_FLUSH);
        compressed.WriterIndex = compressed.WriterIndex + numBytes;
        return numBytes == toWrite;
    }

    public override ByteBuf Encode(IByteBufAllocator alloc, ISpdyHeadersFrame frame)
    {
        CheckNotNullWithIAE(alloc, "alloc");
        CheckNotNullWithIAE(frame, "frame");

        if (finished)
        {
            return Unpooled.EmptyBuffer;
        }

        ByteBuf decompressed = base.Encode(alloc, frame);

        try
        {
            if (!decompressed.IsReadable())
            {
                return Unpooled.EmptyBuffer;
            }

            int len = SetInput(decompressed);
            return Encode(alloc, len);
        }
        finally
        {
            decompressed.Release();
        }
    }

    internal override void End()
    {
        if (finished)
        {
            return;
        }

        finished = true;
        compressor.End();
        base.End();
    }

    // CLR: hasArray(), array() and arrayOffset() + readerIndex of the readable range: one segment backed by a managed
    // array (see the class comment). A range in one array of a composite buffer also takes this branch where Java's
    // hasArray() is false; both branches give the deflater the same bytes.
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
    // index. Like array(), it throws NotSupportedException (Java: UnsupportedOperationException) when the range is not
    // backed by one managed array; the buffer comes from heapBuffer(...).
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
