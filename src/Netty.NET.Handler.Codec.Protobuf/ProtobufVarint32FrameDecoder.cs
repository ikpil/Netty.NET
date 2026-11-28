/*
 * Copyright 2015 The Netty Project
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
using System.Globalization;
using System.Numerics;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Protobuf;

/**
 * A decoder that splits the received {@link ByteBuf}s dynamically by the
 * value of the Google Protocol Buffers
 * <a href="https://developers.google.com/protocol-buffers/docs/encoding#varints">Base
 * 128 Varints</a> integer length field in the message. For example:
 * <pre>
 * BEFORE DECODE (302 bytes)       AFTER DECODE (300 bytes)
 * +--------+---------------+      +---------------+
 * | Length | Protobuf Data |----->| Protobuf Data |
 * | 0xAC02 |  (300 bytes)  |      |  (300 bytes)  |
 * +--------+---------------+      +---------------+
 * </pre>
 *
 * @see CodedInputStream
 * @see CodedInputByteBufferNano
 */
// CLR: the imports com.google.protobuf.CodedInputStream and com.google.protobuf.nano.CodedInputByteBufferNano serve
// only the @see links above, which are kept verbatim; the decoder implements the varint itself. protobuf-java and
// protobuf-javanano have no BCL counterpart and are not referenced (owner decision 2026-11-23,
// docs/codec-protobuf-porting.md), so these imports have no C# using. List<Object> out is IList<object> @out and 'in'
// is @in (C# keywords), and C# has no throws clauses (docs/codec-base-porting.md). Numbers in exception messages use
// the invariant culture (Int32.ToString() uses "the NumberFormatInfo object of the current culture",
// https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class ProtobufVarint32FrameDecoder : ByteToMessageDecoder
{

    private readonly int maxFrameLength;
    private long bytesToDiscard;

    /**
     * Creates a new instance with no frame length limit.
     */
    public ProtobufVarint32FrameDecoder() : this(int.MaxValue)
    {
    }

    /**
     * Creates a new instance with the specified maximum frame length.
     *
     * @param maxFrameLength the maximum length of the frame.
     *                       If the length exceeds this value,
     *                       {@link TooLongFrameException} will be thrown.
     */
    public ProtobufVarint32FrameDecoder(int maxFrameLength)
    {
        this.maxFrameLength = CheckPositive(maxFrameLength, "maxFrameLength");
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (bytesToDiscard > 0)
        {
            int localBytesToDiscard = (int) Math.Min(bytesToDiscard, @in.ReadableBytes);
            @in.SkipBytes(localBytesToDiscard);
            bytesToDiscard -= localBytesToDiscard;
            return;
        }

        @in.MarkReaderIndex();
        int preIndex = @in.ReaderIndex;
        int length = ReadRawVarint32(@in);
        if (preIndex == @in.ReaderIndex)
        {
            return;
        }
        if (length < 0)
        {
            throw new CorruptedFrameException("negative length: " + length.ToString(CultureInfo.InvariantCulture));
        }

        if (length > maxFrameLength)
        {
            long discard = length - @in.ReadableBytes;
            if (discard <= 0)
            {
                @in.SkipBytes(length);
            }
            else
            {
                bytesToDiscard = discard;
                @in.SkipBytes(@in.ReadableBytes);
            }

            throw new TooLongFrameException(
                    "Frame length exceeds " + maxFrameLength.ToString(CultureInfo.InvariantCulture)
                    + ": " + length.ToString(CultureInfo.InvariantCulture));
        }

        if (@in.ReadableBytes < length)
        {
            @in.ResetReaderIndex();
        }
        else
        {
            @out.Add(@in.ReadRetainedSlice(length));
        }
    }

    /**
     * Reads variable length 32bit int from buffer
     *
     * @return decoded int if buffers readerIndex has been forwarded else nonsense value
     */
    // CLR: the package-private method is internal (the tests call it through InternalsVisibleTo). The Java int literal
    // 0x80808080 is a negative int ("can represent a positive, zero, or negative integer", JLS 3.10.1,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-3.html#jls-3.10.1), while a C# literal without suffix
    // takes "the first of the following types in which its value can be represented: int, uint, long, ulong", here
    // uint, and int & uint would be a long
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types); it is
    // written unchecked((int) 0x80808080), the same 32 bits as the Java int. Integer.numberOfTrailingZeros is
    // BitOperations.TrailingZeroCount(int) (both count the trailing zero bits of the int; firstOneOnStop is not 0 here,
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html,
    // https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.trailingzerocount?view=net-10.0).
    // The >> shifts are arithmetic shifts of an int in both languages (JLS 15.19,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-15.html#jls-15.19; C# "performs an arithmetic shift" for
    // an int, https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators).
    // firstOneOnStop - 1 overflows for 0x80000000 (a four-byte varint); Java keeps "the low-order bits" (JLS 15.18.2),
    // and C# does the same in the default unchecked context, which applies because no project sets
    // CheckForOverflowUnderflow (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked).
    internal static int ReadRawVarint32(ByteBuf buffer)
    {
        if (buffer.ReadableBytes < 4)
        {
            return ReadRawVarint24(buffer);
        }

        int wholeOrMore = buffer.GetIntLE(buffer.ReaderIndex);
        int firstOneOnStop = ~wholeOrMore & unchecked((int) 0x80808080);
        if (firstOneOnStop == 0)
        {
            return ReadRawVarint40(buffer, wholeOrMore);
        }

        int bitsToKeep = BitOperations.TrailingZeroCount(firstOneOnStop) + 1;
        buffer.SkipBytes(bitsToKeep >> 3);
        int thisVarintMask = firstOneOnStop ^ (firstOneOnStop - 1);
        int wholeWithContinuations = wholeOrMore & thisVarintMask;
        // mix them up as per varint spec while dropping the continuation bits:
        // 0x7F007F isolate the first byte and the third byte dropping the continuation bits
        // 0x7F007F00 isolate the second byte and the fourth byte dropping the continuation bits
        // the second and fourth byte are shifted to the right by 1, filling the gaps left by the first and third byte
        // it means that the first and second bytes now occupy the first 14 bits (7 bits each)
        // and the third and fourth bytes occupy the next 14 bits (7 bits each), with a gap between the 2s of 2 bytes
        // and another gap of 2 bytes after the forth and third.
        wholeWithContinuations = (wholeWithContinuations & 0x7F007F) | ((wholeWithContinuations & 0x7F007F00) >> 1);
        // 0x3FFF isolate the first 14 bits i.e. the first and second bytes
        // 0x3FFF0000 isolate the next 14 bits i.e. the third and forth bytes
        // the third and forth bytes are shifted to the right by 2, filling the gaps left by the first and second bytes
        return (wholeWithContinuations & 0x3FFF) | ((wholeWithContinuations & 0x3FFF0000) >> 2);
    }

    // CLR: byte-sign spot. Java reads the fifth byte as a signed byte (-128 to 127, JLS 4.2.1,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1), so lastByte < 0 means that its
    // continuation bit is set and the varint is longer than five bytes. GetByte returns the unsigned System.Byte (0 to
    // 255) in the Buffer port, for which the test would never hold, so the byte is cast to sbyte: between types of the
    // same size "the source value is treated as a value of the destination type"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions). The
    // non-negative sbyte is then converted to int for lastByte << 28, as Java widens the byte (JLS 5.6,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6).
    private static int ReadRawVarint40(ByteBuf buffer, int wholeOrMore)
    {
        if (buffer.ReadableBytes == 4)
        {
            // The first 4 bytes all have the continuation bit set, but the 5th byte did not arrive yet.
            // Leave the readerIndex untouched so the caller waits for more data.
            return 0;
        }

        sbyte lastByte = (sbyte) buffer.GetByte(buffer.ReaderIndex + 4);
        if (lastByte < 0)
        {
            throw new CorruptedFrameException("malformed varint.");
        }

        buffer.SkipBytes(5);
        // add it to wholeOrMore
        return wholeOrMore & 0x7F |
               (((wholeOrMore >> 8) & 0x7F) << 7) |
               (((wholeOrMore >> 16) & 0x7F) << 14) |
               (((wholeOrMore >> 24) & 0x7F) << 21) |
               (lastByte << 28);
    }

    // CLR: byte-sign spot. tmp >= 0 tests the continuation bit through the sign of the Java byte (JLS 4.2.1); with the
    // unsigned System.Byte that ReadByte() returns in the Buffer port every byte would pass. Each read is therefore cast
    // to sbyte, which keeps the same eight bits
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions), so
    // tmp >= 0, tmp & 127, tmp << 7 and tmp << 14 give the Java values: sbyte operands of the bitwise and shift
    // operators "are converted to the int type"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators), as
    // Java widens the byte to int with sign extension (JLS 5.1.2 and 5.6,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6).
    private static int ReadRawVarint24(ByteBuf buffer)
    {
        if (!buffer.IsReadable())
        {
            return 0;
        }

        buffer.MarkReaderIndex();

        sbyte tmp = (sbyte) buffer.ReadByte();
        if (tmp >= 0)
        {
            return tmp;
        }

        int result = tmp & 127;

        if (!buffer.IsReadable())
        {
            buffer.ResetReaderIndex();
            return 0;
        }

        if ((tmp = (sbyte) buffer.ReadByte()) >= 0)
        {
            return result | tmp << 7;
        }

        result |= (tmp & 127) << 7;

        if (!buffer.IsReadable())
        {
            buffer.ResetReaderIndex();
            return 0;
        }

        if ((tmp = (sbyte) buffer.ReadByte()) >= 0)
        {
            return result | tmp << 14;
        }

        // All 3 readable bytes have the continuation bit set, which means at least one more byte is needed to
        // complete the varint. We only end up here if less than 4 bytes were readable, so wait for more data.
        buffer.ResetReaderIndex();
        return 0;
    }
}
