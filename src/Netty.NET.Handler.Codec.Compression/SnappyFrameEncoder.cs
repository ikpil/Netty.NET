/*
 * Copyright 2012 The Netty Project
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

using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Compression.Snappy;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Compresses a {@link ByteBuf} using the Snappy framing format.
 *
 * See <a href="https://github.com/google/snappy/blob/master/framing_format.txt">Snappy framing format</a>.
 */
// CLR: the Class argument of the MessageToByteEncoder constructor is a Type (MessageToByteEncoder in codec-base);
// encode(ctx, in, out) is Encode(IChannelHandlerContext, ByteBuf, ByteBuf) and C# has no throws clauses. The byte[]
// initializer keeps the bit pattern of the Java (byte) 0xff cast as a System.Byte. Numbers in exception messages use
// the invariant culture (docs/codec-base-porting.md).
public class SnappyFrameEncoder : MessageToByteEncoder<ByteBuf>
{

    private const short SNAPPY_SLICE_SIZE = short.MaxValue;

    /**
     * Both
     * {@value io.netty.handler.codec.compression.SnappyFrameEncoder#SNAPPY_SLICE_SIZE}
     * and {@value io.netty.handler.codec.compression.SnappyFrameEncoder#SNAPPY_SLICE_JUMBO_SIZE}
     * are valid lengths for the Snappy framing format
     */
    private const int SNAPPY_SLICE_JUMBO_SIZE = 65535;

    /**
     * The minimum amount that we'll consider actually attempting to compress.
     * This value is preamble + the minimum length our Snappy service will
     * compress (instead of just emitting a literal).
     */
    private const int MIN_COMPRESSIBLE_LENGTH = 18;

    /**
     * All streams should start with the "Stream identifier", containing chunk
     * type 0xff, a length field of 0x6, and 'sNaPpY' in ASCII.
     */
    private static readonly byte[] STREAM_START = {
        (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59
    };

    public SnappyFrameEncoder() : this(SNAPPY_SLICE_SIZE)
    {
    }

    /**
     * Create a new instance with a
     * {@value io.netty.handler.codec.compression.SnappyFrameEncoder#SNAPPY_SLICE_JUMBO_SIZE}
     * chunk size.
     */
    public static SnappyFrameEncoder SnappyEncoderWithJumboFrames()
    {
        return new SnappyFrameEncoder(SNAPPY_SLICE_JUMBO_SIZE);
    }

    private SnappyFrameEncoder(int sliceSize) : base(typeof(ByteBuf))
    {
        this.sliceSize = sliceSize;
    }

    private readonly Snappy snappy = new Snappy();
    private bool started;
    private readonly int sliceSize;

    protected override void Encode(IChannelHandlerContext ctx, ByteBuf @in, ByteBuf @out)
    {
        if (!@in.IsReadable())
        {
            return;
        }

        if (!started)
        {
            started = true;
            @out.WriteBytes(STREAM_START);
        }

        int dataLength = @in.ReadableBytes;
        if (dataLength > MIN_COMPRESSIBLE_LENGTH)
        {
            for (;;)
            {
                int lengthIdx = @out.WriterIndex + 1;
                if (dataLength < MIN_COMPRESSIBLE_LENGTH)
                {
                    ByteBuf slice = @in.ReadSlice(dataLength);
                    WriteUnencodedChunk(slice, @out, dataLength);
                    break;
                }

                @out.WriteInt(0);
                if (dataLength > sliceSize)
                {
                    ByteBuf slice = @in.ReadSlice(sliceSize);
                    CalculateAndWriteChecksum(slice, @out);
                    snappy.Encode(slice, @out, sliceSize);
                    SetChunkLength(@out, lengthIdx);
                    dataLength -= sliceSize;
                }
                else
                {
                    ByteBuf slice = @in.ReadSlice(dataLength);
                    CalculateAndWriteChecksum(slice, @out);
                    snappy.Encode(slice, @out, dataLength);
                    SetChunkLength(@out, lengthIdx);
                    break;
                }
            }
        }
        else
        {
            WriteUnencodedChunk(@in, @out, dataLength);
        }
    }

    private static void WriteUnencodedChunk(ByteBuf @in, ByteBuf @out, int dataLength)
    {
        @out.WriteByte(1);
        WriteChunkLength(@out, dataLength + 4);
        CalculateAndWriteChecksum(@in, @out);
        @out.WriteBytes(@in, dataLength);
    }

    private static void SetChunkLength(ByteBuf @out, int lengthIdx)
    {
        int chunkLength = @out.WriterIndex - lengthIdx - 3;
        if (chunkLength >>> 24 != 0)
        {
            throw new CompressionException("compressed data too large: " +
                    chunkLength.ToString(CultureInfo.InvariantCulture));
        }
        @out.SetMediumLE(lengthIdx, chunkLength);
    }

    /**
     * Writes the 2-byte chunk length to the output buffer.
     *
     * @param out The buffer to write to
     * @param chunkLength The length to write
     */
    private static void WriteChunkLength(ByteBuf @out, int chunkLength)
    {
        @out.WriteMediumLE(chunkLength);
    }

    /**
     * Calculates and writes the 4-byte checksum to the output buffer
     *
     * @param slice The data to calculate the checksum for
     * @param out The output buffer to write the checksum to
     */
    private static void CalculateAndWriteChecksum(ByteBuf slice, ByteBuf @out)
    {
        @out.WriteIntLE(CalculateChecksum(slice));
    }
}
