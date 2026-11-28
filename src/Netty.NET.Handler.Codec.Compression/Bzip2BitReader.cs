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
using System.Globalization;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * An bit reader that allows the reading of single bit booleans, bit strings of
 * arbitrary length (up to 32 bits), and bit aligned 32-bit integers. A single byte
 * at a time is read from the {@link ByteBuf} when more bits are required.
 */
// CLR: the package-private class is internal and the field in is @in. readUnsignedByte() is the Buffer port's
// ReadByte(), which returns the unsigned octet; readUnsignedShort() and readUnsignedInt() return ushort and uint,
// which widen to the long readData with zero extension as Java's int and long results do. Java's >>> is C#'s >>>,
// the int and long arithmetic wraps as in Java (the projects are unchecked) and the (int) narrowing of the long
// keeps its low 32 bits. IllegalArgumentException is ArgumentException and the numbers in its messages use the
// invariant culture.
internal class Bzip2BitReader
{
    /**
     * Maximum count of possible readable bytes to check.
     */
    private const int MAX_COUNT_OF_READABLE_BYTES = int.MaxValue >>> 3;

    /**
     * The {@link ByteBuf} from which to read data.
     */
    private ByteBuf @in;

    /**
     * A buffer of bits read from the input stream that have not yet been returned.
     */
    private long bitBuffer;

    /**
     * The number of bits currently buffered in {@link #bitBuffer}.
     */
    private int bitCount;

    /**
     * Set the {@link ByteBuf} from which to read data.
     */
    internal void SetByteBuf(ByteBuf @in)
    {
        this.@in = @in;
    }

    /**
     * Reads up to 32 bits from the {@link ByteBuf}.
     * @param count The number of bits to read (maximum {@code 32} as a size of {@code int})
     * @return The bits requested, right-aligned within the integer
     */
    internal int ReadBits(int count)
    {
        if (count < 0 || count > 32)
        {
            throw new ArgumentException("count: " + count.ToString(CultureInfo.InvariantCulture) + " (expected: 0-32 )");
        }

        int bitCount = this.bitCount;
        long bitBuffer = this.bitBuffer;

        if (bitCount < count)
        {
            long readData;
            int offset;

            switch (@in.ReadableBytes)
            {
                case 1:
                {
                    readData = @in.ReadByte();
                    offset = 8;
                    break;
                }
                case 2:
                {
                    readData = @in.ReadUnsignedShort();
                    offset = 16;
                    break;
                }
                case 3:
                {
                    readData = @in.ReadUnsignedMedium();
                    offset = 24;
                    break;
                }
                default:
                {
                    readData = @in.ReadUnsignedInt();
                    offset = 32;
                    break;
                }
            }

            bitBuffer = bitBuffer << offset | readData;
            bitCount += offset;
            this.bitBuffer = bitBuffer;
        }

        this.bitCount = bitCount -= count;
        return (int) (bitBuffer >>> bitCount & (count != 32 ? (1 << count) - 1 : 0xFFFFFFFFL));
    }

    /**
     * Reads a single bit from the {@link ByteBuf}.
     * @return {@code true} if the bit read was {@code 1}, otherwise {@code false}
     */
    internal bool ReadBoolean()
    {
        return ReadBits(1) != 0;
    }

    /**
     * Reads 32 bits of input as an integer.
     * @return The integer read
     */
    internal int ReadInt()
    {
        return ReadBits(32);
    }

    /**
     * Refill the {@link ByteBuf} by one byte.
     */
    // CLR: the unsigned octet stays in a byte local (Java: an int holding 0..255), which widens to the long without sign
    // extension; or-ing an int would raise CS0675, "Bitwise-or operator used on a sign-extended operand"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0675). The value is the same.
    internal void Refill()
    {
        byte readData = @in.ReadByte();
        bitBuffer = bitBuffer << 8 | readData;
        bitCount += 8;
    }

    /**
     * Checks that at least one bit is available for reading.
     * @return {@code true} if one bit is available for reading, otherwise {@code false}
     */
    internal bool IsReadable()
    {
        return bitCount > 0 || @in.IsReadable();
    }

    /**
     * Checks that the specified number of bits available for reading.
     * @param count The number of bits to check
     * @return {@code true} if {@code count} bits are available for reading, otherwise {@code false}
     */
    internal bool HasReadableBits(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException("count: " + count.ToString(CultureInfo.InvariantCulture) +
                    " (expected value greater than 0)");
        }

        return bitCount >= count || (@in.ReadableBytes << 3 & int.MaxValue) >= count - bitCount;
    }

    /**
     * Checks that the specified number of bytes available for reading.
     * @param count The number of bytes to check
     * @return {@code true} if {@code count} bytes are available for reading, otherwise {@code false}
     */
    internal bool HasReadableBytes(int count)
    {
        if (count < 0 || count > MAX_COUNT_OF_READABLE_BYTES)
        {
            throw new ArgumentException("count: " + count.ToString(CultureInfo.InvariantCulture)
                    + " (expected: 0-" + MAX_COUNT_OF_READABLE_BYTES.ToString(CultureInfo.InvariantCulture) + ')');
        }

        return HasReadableBits(count << 3);
    }
}
