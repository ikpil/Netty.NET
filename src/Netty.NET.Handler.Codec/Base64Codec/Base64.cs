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
/*
 * Written by Robert Harder and released to the public domain, as explained at
 * https://creativecommons.org/licenses/publicdomain
 */

using System;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Base64Codec;

/**
 * Utility class for {@link ByteBuf} that encodes and decodes to and from
 * <a href="https://en.wikipedia.org/wiki/Base64">Base64</a> notation.
 * <p>
 * The encoding and decoding algorithm in this class has been derived from
 * <a href="http://iharder.sourceforge.net/current/java/base64/">Robert Harder's Public Domain
 * Base64 Encoder/Decoder</a>.
 */
// CLR: Netty.NET's ByteBuf has no order()/order(ByteOrder) (deprecated in the source: "use the Little Endian
// accessors, e.g. getShortLE, getIntLE instead of creating a buffer with swapped endianness", and not ported), so every
// buffer is accessed BIG_ENDIAN. The src.order()/dest.order() == ByteOrder.BIG_ENDIAN branches are therefore the only
// ones, the .order(src.order()) calls on the allocated buffers are dropped, and the LITTLE_ENDIAN branches (toIntLE,
// encode3to4LittleEndian and the swapped decode4to3 paths, which produce the same bytes through a swapped-order buffer)
// are not ported; their original comments are kept where the branches were.
public static class Base64
{
    /** Maximum line length (76) of Base64 output. */
    private const int MAX_LINE_LENGTH = 76;

    /** The equals sign (=) as a byte. */
    private const byte EQUALS_SIGN = (byte) '=';

    /** The new line character (\n) as a byte. */
    private const byte NEW_LINE = (byte) '\n';

    // CLR: sbyte keeps the signed Java byte values that are compared with the decodabet entries.
    private const sbyte WHITE_SPACE_ENC = -5; // Indicates white space in encoding

    private const sbyte EQUALS_SIGN_ENC = -1; // Indicates equals sign in encoding

    private static byte[] Alphabet(Base64Dialect dialect)
    {
        return ObjectUtil.CheckNotNull(dialect, "dialect").alphabet;
    }

    private static sbyte[] Decodabet(Base64Dialect dialect)
    {
        return ObjectUtil.CheckNotNull(dialect, "dialect").decodabet;
    }

    private static bool BreakLines(Base64Dialect dialect)
    {
        return ObjectUtil.CheckNotNull(dialect, "dialect").breakLinesByDefault;
    }

    public static ByteBuf Encode(ByteBuf src)
    {
        return Encode(src, Base64Dialect.STANDARD);
    }

    public static ByteBuf Encode(ByteBuf src, Base64Dialect dialect)
    {
        return Encode(src, BreakLines(dialect), dialect);
    }

    public static ByteBuf Encode(ByteBuf src, Base64Dialect dialect, bool addPadding)
    {
        return Encode(src, BreakLines(dialect), dialect, addPadding);
    }

    public static ByteBuf Encode(ByteBuf src, bool breakLines)
    {
        return Encode(src, breakLines, Base64Dialect.STANDARD);
    }

    public static ByteBuf Encode(ByteBuf src, bool breakLines, bool addPadding)
    {
        return Encode(src, breakLines, Base64Dialect.STANDARD, addPadding);
    }

    public static ByteBuf Encode(ByteBuf src, bool breakLines, Base64Dialect dialect)
    {
        ObjectUtil.CheckNotNull(src, "src");

        ByteBuf dest = Encode(src, src.ReaderIndex, src.ReadableBytes, breakLines, dialect);
        src.ReaderIndex = src.WriterIndex;
        return dest;
    }

    public static ByteBuf Encode(ByteBuf src, bool breakLines, Base64Dialect dialect, bool addPadding)
    {
        ObjectUtil.CheckNotNull(src, "src");

        ByteBuf dest = Encode(src, src.ReaderIndex, src.ReadableBytes, breakLines, dialect, addPadding);
        src.ReaderIndex = src.WriterIndex;
        return dest;
    }

    public static ByteBuf Encode(ByteBuf src, int off, int len)
    {
        return Encode(src, off, len, Base64Dialect.STANDARD);
    }

    public static ByteBuf Encode(ByteBuf src, int off, int len, Base64Dialect dialect)
    {
        return Encode(src, off, len, BreakLines(dialect), dialect);
    }

    public static ByteBuf Encode(
            ByteBuf src, int off, int len, bool breakLines)
    {
        return Encode(src, off, len, breakLines, Base64Dialect.STANDARD);
    }

    public static ByteBuf Encode(
            ByteBuf src, int off, int len, bool breakLines, Base64Dialect dialect)
    {
        return Encode(src, off, len, breakLines, dialect, src.Alloc(), true);
    }

    public static ByteBuf Encode(
            ByteBuf src, int off, int len, bool breakLines, Base64Dialect dialect, bool addPadding)
    {
        return Encode(src, off, len, breakLines, dialect, src.Alloc(), addPadding);
    }

    public static ByteBuf Encode(
            ByteBuf src, int off, int len, bool breakLines, Base64Dialect dialect, IByteBufAllocator allocator)
    {
        return Encode(src, off, len, breakLines, dialect, allocator, true);
    }

    private static ByteBuf Encode(
            ByteBuf src, int off, int len, bool breakLines, Base64Dialect dialect, IByteBufAllocator allocator,
            bool addPadding)
    {
        ObjectUtil.CheckNotNull(src, "src");
        ObjectUtil.CheckNotNull(dialect, "dialect");

        int capacity = EncodedBufferSize(len, breakLines);
        ByteBuf destBuf = allocator.Buffer(capacity);
        // Ensure the destination buffer is flat, if possible, and avoid leak detection checks on every write:
        // CLR: AsMemory(0, capacity) borrows the contiguous storage that the source reaches through array()/arrayOffset()
        // or internalNioBuffer(0, capacity); Unpooled.WrappedBuffer(Memory<byte>) wraps an array range as
        // wrappedBuffer(array, offset, length) does and never releases a borrowed owner.
        ByteBuf dest = destBuf.Unwrap() == null || !destBuf.IsContiguous ? destBuf :
                Unpooled.WrappedBuffer(destBuf.AsMemory(0, capacity));

        byte[] alphabet = Alphabet(dialect);
        int d = 0;
        int e = 0;
        int len2 = len - 2;
        int lineLength = 0;
        for (; d < len2; d += 3, e += 4)
        {
            Encode3to4(src, d + off, 3, dest, e, alphabet, addPadding);

            lineLength += 4;

            if (breakLines && lineLength == MAX_LINE_LENGTH)
            {
                dest.SetByte(e + 4, NEW_LINE);
                e ++;
                lineLength = 0;
            } // end if: end of line
        } // end for: each piece of array

        if (d < len)
        {
            e += Encode3to4(src, d + off, len - d, dest, e, alphabet, addPadding);
        } // end if: some padding needed

        // Remove last byte if it's a newline
        if (e > 1 && dest.GetByte(e - 1) == NEW_LINE)
        {
            e--;
        }
        if (dest != destBuf)
        {
            dest.Release();
        }
        return destBuf.SetIndex(0, e);
    }

    private static int Encode3to4(
            ByteBuf src, int srcOffset, int numSigBytes, ByteBuf dest, int destOffset, byte[] alphabet,
            bool addPadding)
    {
        //           1         2         3
        // 01234567890123456789012345678901 Bit position
        // --------000000001111111122222222 Array position from threeBytes
        // --------|    ||    ||    ||    | Six bit groups to index ALPHABET
        //          >>18  >>12  >> 6  >> 0  Right shift necessary
        //                0x3f  0x3f  0x3f  Additional AND

        // Create buffer with zero-padding if there are only one or two
        // significant bytes passed in the array.
        // We have to shift left 24 in order to flush out the 1's that appear
        // when Java treats a value as negative that is cast from a byte to an int.
        // CLR: only the src.order() == ByteOrder.BIG_ENDIAN branch exists (see the class comment).
        int inBuff;
        switch (numSigBytes)
        {
            case 1:
                inBuff = ToInt(src.GetByte(srcOffset));
                break;
            case 2:
                inBuff = ToIntBE(src.GetShort(srcOffset));
                break;
            default:
                inBuff = numSigBytes <= 0 ? 0 : ToIntBE(src.GetMedium(srcOffset));
                break;
        }
        return Encode3to4BigEndian(inBuff, numSigBytes, dest, destOffset, alphabet, addPadding);
    }

    // package-private for testing
    internal static int EncodedBufferSize(int len, bool breakLines)
    {
        // Cast len to long to prevent overflow
        long len43 = ((long) len << 2) / 3;

        // Account for padding
        long ret = (len43 + 3) & ~3;

        if (breakLines)
        {
            ret += len43 / MAX_LINE_LENGTH;
        }

        return ret < int.MaxValue ? (int) ret : int.MaxValue;
    }

    // CLR: GetByte returns the unsigned CLR byte; value & 0xff gives the same int as for the signed Java byte.
    private static int ToInt(byte value)
    {
        return (value & 0xff) << 16;
    }

    private static int ToIntBE(short value)
    {
        return (value & 0xff00) << 8 | (value & 0xff) << 8;
    }

    private static int ToIntBE(int mediumValue)
    {
        return (mediumValue & 0xff0000) | (mediumValue & 0xff00) | (mediumValue & 0xff);
    }

    // CLR: toIntLE(short) and toIntLE(int) serve only the LITTLE_ENDIAN branch and are not ported.

    private static int Encode3to4BigEndian(
            int inBuff, int numSigBytes, ByteBuf dest, int destOffset, byte[] alphabet, bool addPadding)
    {
        // Packing bytes into an int to reduce bound and reference count checking.
        switch (numSigBytes)
        {
            case 3:
                dest.SetInt(destOffset, alphabet[inBuff >>> 18       ] << 24 |
                                        alphabet[inBuff >>> 12 & 0x3f] << 16 |
                                        alphabet[inBuff >>>  6 & 0x3f] << 8  |
                                        alphabet[inBuff        & 0x3f]);
                return 4;
            case 2:
                if (addPadding)
                {
                    dest.SetInt(destOffset, alphabet[inBuff >>> 18       ] << 24 |
                                            alphabet[inBuff >>> 12 & 0x3f] << 16 |
                                            alphabet[inBuff >>> 6  & 0x3f] << 8  |
                                            EQUALS_SIGN);
                    return 4;
                }
                else
                {
                    dest.SetMedium(destOffset, alphabet[inBuff >>> 18       ] << 16 |
                                               alphabet[inBuff >>> 12 & 0x3f] << 8  |
                                               alphabet[inBuff >>> 6 & 0x3f ]);
                    return 3;
                }
            case 1:
                if (addPadding)
                {
                    dest.SetInt(destOffset, alphabet[inBuff >>> 18       ] << 24 |
                                            alphabet[inBuff >>> 12 & 0x3f] << 16 |
                                            EQUALS_SIGN                    << 8  |
                                            EQUALS_SIGN);
                    return 4;
                }
                else
                {
                    dest.SetShort(destOffset, alphabet[inBuff >>> 18       ] << 8 |
                                              alphabet[inBuff >>> 12 & 0x3f]);
                    return 2;
                }
            default:
                // NOOP
                return 0;
        }
    }

    // CLR: encode3to4LittleEndian serves only the LITTLE_ENDIAN branch and is not ported. Its comments:
    // Packing bytes into an int to reduce bound and reference count checking.
    // NOOP

    public static ByteBuf Decode(ByteBuf src)
    {
        return Decode(src, Base64Dialect.STANDARD);
    }

    public static ByteBuf Decode(ByteBuf src, Base64Dialect dialect)
    {
        ObjectUtil.CheckNotNull(src, "src");

        ByteBuf dest = Decode(src, src.ReaderIndex, src.ReadableBytes, dialect);
        src.ReaderIndex = src.WriterIndex;
        return dest;
    }

    public static ByteBuf Decode(
            ByteBuf src, int off, int len)
    {
        return Decode(src, off, len, Base64Dialect.STANDARD);
    }

    public static ByteBuf Decode(
            ByteBuf src, int off, int len, Base64Dialect dialect)
    {
        return Decode(src, off, len, dialect, src.Alloc());
    }

    public static ByteBuf Decode(
            ByteBuf src, int off, int len, Base64Dialect dialect, IByteBufAllocator allocator)
    {
        ObjectUtil.CheckNotNull(src, "src");
        ObjectUtil.CheckNotNull(dialect, "dialect");

        // Using a ByteProcessor to reduce bound and reference count checking.
        return new Decoder().Decode(src, off, len, allocator, dialect);
    }

    // package-private for testing
    internal static int DecodedBufferSize(int len)
    {
        return len - (len >>> 2);
    }

    // CLR: ByteProcessor is Func<byte, bool> in Netty.NET.Common (ByteBuf.ForEachByte takes that delegate), so the
    // Process method group is passed where the source passes 'this'.
    private sealed class Decoder
    {
        private readonly byte[] b4 = new byte[4];
        private int b4Posn;
        private sbyte[] decodabet;
        private int outBuffPosn;
        private ByteBuf dest;

        internal ByteBuf Decode(ByteBuf src, int off, int len, IByteBufAllocator allocator, Base64Dialect dialect)
        {
            dest = allocator.Buffer(DecodedBufferSize(len)); // Upper limit on size of output

            decodabet = Decodabet(dialect);
            try
            {
                src.ForEachByte(off, len, Process);

                // Padding missing, process additional bytes
                if (b4Posn == 1)
                {
                    throw new ArgumentException(
                            "Invalid Base64 input, single remaining character implies incorrect length or padding");
                }
                else if (b4Posn == 2)
                {
                    b4[2] = EQUALS_SIGN;
                    b4[3] = EQUALS_SIGN;
                    outBuffPosn += Decode4to3(b4, dest, outBuffPosn, decodabet);
                }
                else if (b4Posn == 3)
                {
                    b4[3] = EQUALS_SIGN;
                    outBuffPosn += Decode4to3(b4, dest, outBuffPosn, decodabet);
                }

                return dest.Slice(0, outBuffPosn);
            }
            catch (Exception)
            {
                dest.Release();
                // CLR: PlatformDependent.throwException(cause) rethrows the original exception, so the unreachable
                // 'return null' that follows it in the source is omitted.
                throw;
            }
        }

        public bool Process(byte value)
        {
            // CLR: the CLR byte is unsigned; the sbyte view keeps the source's signed 'value > 0' test, which rejects
            // 0x80-0xFF as well as 0.
            if (unchecked((sbyte) value) > 0)
            {
                sbyte sbiDecode = decodabet[value];
                if (sbiDecode >= WHITE_SPACE_ENC)
                { // White space, Equals sign or better
                    if (sbiDecode >= EQUALS_SIGN_ENC)
                    { // Equals sign or better
                        b4[b4Posn ++] = value;
                        if (b4Posn > 3)
                        { // Quartet built
                            outBuffPosn += Decode4to3(b4, dest, outBuffPosn, decodabet);
                            b4Posn = 0;

                            // If that was the equals sign, break out of 'for' loop
                            return value != EQUALS_SIGN;
                        }
                    }
                    return true;
                }
            }
            throw new ArgumentException(
                    "invalid Base64 input character: " + (value & 0xFF).ToString(CultureInfo.InvariantCulture) +
                    " (decimal)");
        }

        // CLR: the source catches IndexOutOfBoundsException around the decodabet lookups; a CLR array index check
        // throws IndexOutOfRangeException (https://learn.microsoft.com/dotnet/api/system.indexoutofrangeexception).
        private static int Decode4to3(byte[] src, ByteBuf dest, int destOffset, sbyte[] decodabet)
        {
            byte src0 = src[0];
            byte src1 = src[1];
            byte src2 = src[2];
            int decodedValue;
            if (src2 == EQUALS_SIGN)
            {
                // Example: Dk==
                try
                {
                    decodedValue = (decodabet[src0] & 0xff) << 2 | (decodabet[src1] & 0xff) >>> 4;
                }
                catch (IndexOutOfRangeException)
                {
                    throw new ArgumentException("not encoded in Base64");
                }
                dest.SetByte(destOffset, decodedValue);
                return 1;
            }

            byte src3 = src[3];
            if (src3 == EQUALS_SIGN)
            {
                // Example: DkL=
                sbyte b1 = decodabet[src1];
                // Packing bytes into a short to reduce bound and reference count checking.
                try
                {
                    // CLR: only the dest.order() == ByteOrder.BIG_ENDIAN branch exists (see the class comment).
                    // The decodabet bytes are meant to straddle byte boundaries and so we must carefully mask out
                    // the bits we care about.
                    decodedValue = ((decodabet[src0] & 0x3f) << 2 | (b1 & 0xf0) >> 4) << 8 |
                                    (b1 & 0xf) << 4 | (decodabet[src2] & 0xfc) >>> 2;
                    // CLR: comment of the LITTLE_ENDIAN branch that is not ported:
                    // This is just a simple byte swap of the operation above.
                }
                catch (IndexOutOfRangeException)
                {
                    throw new ArgumentException("not encoded in Base64");
                }
                dest.SetShort(destOffset, decodedValue);
                return 2;
            }

            // Example: DkLE
            try
            {
                // CLR: only the dest.order() == ByteOrder.BIG_ENDIAN branch exists (see the class comment).
                decodedValue = (decodabet[src0] & 0x3f) << 18 |
                               (decodabet[src1] & 0xff) << 12 |
                               (decodabet[src2] & 0xff) << 6 |
                                decodabet[src3] & 0xff;
                // CLR: comments of the LITTLE_ENDIAN branch that is not ported:
                // The goal is to byte swap the BIG_ENDIAN case above. There are 2 interesting things to consider:
                // 1. We are byte swapping a 3 byte data type. The left and the right byte switch, but the middle
                //    remains the same.
                // 2. The contents straddles byte boundaries. This means bytes will be pulled apart during the byte
                //    swapping process.
                // The bottom half of b1 remains in the middle.
                // The top half of b1 are the least significant bits after the swap.
                // The bottom 2 bits of b2 will be the most significant bits after the swap.
                // The remaining 6 bits of b2 remain in the middle.
            }
            catch (IndexOutOfRangeException)
            {
                throw new ArgumentException("not encoded in Base64");
            }
            dest.SetMedium(destOffset, decodedValue);
            return 3;
        }
    }

    // CLR: the private constructor that prevents instantiation is the static class declaration.
    // Unused
}
