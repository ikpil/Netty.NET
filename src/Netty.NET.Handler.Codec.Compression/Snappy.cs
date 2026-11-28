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

using System;
using System.Globalization;
using System.Numerics;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Uncompresses an input {@link ByteBuf} encoded with Snappy compression into an
 * output {@link ByteBuf}.
 * <p>
 * See <a href="https://github.com/google/snappy/blob/master/format_description.txt">snappy format</a>.
 */
// CLR: the public final class is a public sealed class. FastThreadLocal, SystemPropertyUtil and MathUtil are the
// Common ports. Java switch fall-through is goto case and the last switch section ends with break, because C# does
// not let control fall through a switch section. The labeled "break outer" is "goto outer", whose label sits on
// the first statement after the labeled loop, as the Common port does
// (src/Netty.NET.Common/Concurrent/ConcurrentSkipListIntObjMultimap.cs): a labeled break exists only "Starting in
// C# 15" and goto "transfers control to a statement that a label marks"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/jump-statements). The local base
// is @base (a C# keyword). Java int arithmetic that overflows wraps the same way in the unchecked C# project, and
// >>> is the C# unsigned right shift. Numbers in exception messages use the invariant culture.
public sealed class Snappy
{

    private const int MAX_HT_SIZE = 1 << 14;
    private const int MIN_COMPRESSIBLE_BYTES = 15;

    // used as a return value to indicate that we haven't yet read our full preamble
    private const int PREAMBLE_NOT_FULL = -1;
    private const int NOT_ENOUGH_INPUT = -1;

    // constants for the tag types
    private const int LITERAL = 0;
    private const int COPY_1_BYTE_OFFSET = 1;
    private const int COPY_2_BYTE_OFFSET = 2;
    private const int COPY_4_BYTE_OFFSET = 3;

    // Hash table used to compress, shared between subsequent call to .encode()
    private static readonly FastThreadLocal<short[]> HASH_TABLE = new FastThreadLocal<short[]>();

    private static readonly bool DEFAULT_REUSE_HASHTABLE =
            SystemPropertyUtil.GetBoolean("io.netty.handler.codec.compression.snappy.reuseHashTable", false);

    public Snappy() : this(DEFAULT_REUSE_HASHTABLE)
    {
    }

    internal Snappy(bool reuseHashtable)
    {
        this.reuseHashtable = reuseHashtable;
    }

    public static Snappy WithHashTableReuse()
    {
        return new Snappy(true);
    }

    private readonly bool reuseHashtable;
    private State state = State.READING_PREAMBLE;
    // CLR: the Java byte tag is the unsigned System.Byte that ReadByte returns (owner decision); every use masks the tag
    // (tag & 0x03, tag >> 2 & 0x3F, (tag & 0x01c) >> 2, (tag & 0x0e0) << 8 >> 5), so the sign-extended Java value and
    // the unsigned value give the same bits.
    private byte tag;
    private int written;

    private enum State
    {
        READING_PREAMBLE,
        READING_TAG,
        READING_LITERAL,
        READING_COPY
    }

    public void Reset()
    {
        state = State.READING_PREAMBLE;
        tag = 0;
        written = 0;
    }

    // CLR: Integer.numberOfLeadingZeros(x) "Returns the number of zero bits preceding the highest-order ("leftmost")
    // one-bit in the two's complement binary representation" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html);
    // BitOperations.LeadingZeroCount((uint) x) "Counts the number of leading zero bits in an unsigned 32-bit integer mask"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.leadingzerocount?view=net-10.0), the
    // same count for the same bit pattern. The Java literal 0xFFFFFF80 is an int (negative), while an unsuffixed C#
    // literal takes "the first of the following types in which its value can be represented: int, uint, long, ulong"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types), so it is
    // written unchecked((int) 0xFFFFFF80) to keep the int mask. (short) (inIndex - baseIndex) keeps the low 16 bits in
    // the unchecked context as Java's narrowing does, and table[hash] & 0xffff reads them back unsigned.
    public void Encode(ByteBuf @in, ByteBuf @out, int length)
    {
        // Write the preamble length to the output buffer
        for (int i = 0;; i ++)
        {
            int b = length >>> i * 7;
            if ((b & unchecked((int) 0xFFFFFF80)) != 0)
            {
                @out.WriteByte(b & 0x7f | 0x80);
            }
            else
            {
                @out.WriteByte(b);
                break;
            }
        }

        int inIndex = @in.ReaderIndex;
        int baseIndex = inIndex;

        int hashTableSize = MathUtil.FindNextPositivePowerOfTwo(length);
        hashTableSize = Math.Min(hashTableSize, MAX_HT_SIZE);
        short[] table = GetHashTable(hashTableSize);
        int shift = BitOperations.LeadingZeroCount((uint) hashTableSize) + 1;

        int nextEmit = inIndex;

        if (length - inIndex >= MIN_COMPRESSIBLE_BYTES)
        {
            int nextHash = Hash(@in, ++inIndex, shift);

            while (true)
            {
                int skip = 32;

                int candidate;
                int nextIndex = inIndex;

                do
                {
                    inIndex = nextIndex;
                    int hash = nextHash;
                    int bytesBetweenHashLookups = skip++ >> 5;
                    nextIndex = inIndex + bytesBetweenHashLookups;

                    // We need at least 4 remaining bytes to read the hash
                    if (nextIndex > length - 4)
                    {
                        goto outer;
                    }

                    nextHash = Hash(@in, nextIndex, shift);

                    // equivalent to Short.toUnsignedInt
                    // use unsigned short cast to avoid loss precision when 32767 <= length <= 65355
                    candidate = baseIndex + (table[hash] & 0xffff);

                    table[hash] = (short) (inIndex - baseIndex);
                }
                while (@in.GetInt(inIndex) != @in.GetInt(candidate));

                EncodeLiteral(@in, @out, inIndex - nextEmit);

                int insertTail;

                do
                {
                    int @base = inIndex;
                    int matched = 4 + FindMatchingLength(@in, candidate + 4, inIndex + 4, length);
                    inIndex += matched;
                    int offset = @base - candidate;
                    EncodeCopy(@out, offset, matched);
                    @in.ReaderIndex = @in.ReaderIndex + matched;
                    insertTail = inIndex - 1;
                    nextEmit = inIndex;

                    if (inIndex >= length - 4)
                    {
                        goto outer;
                    }

                    int prevHash = Hash(@in, insertTail, shift);
                    table[prevHash] = (short) (inIndex - baseIndex - 1);
                    int currentHash = Hash(@in, insertTail + 1, shift);
                    candidate = baseIndex + (table[currentHash] & 0xFFFF);
                    table[currentHash] = (short) (inIndex - baseIndex);
                }
                while (@in.GetInt(insertTail + 1) == @in.GetInt(candidate));

                nextHash = Hash(@in, insertTail + 2, shift);
                ++inIndex;
            }
        }

        // CLR: the statement after the labeled loop of the source, the target of its break outer.
        outer:
        // If there are any remaining characters, write them out as a literal
        if (nextEmit < length)
        {
            EncodeLiteral(@in, @out, length - nextEmit);
        }
    }

    /**
     * Hashes the 4 bytes located at index, shifting the resulting hash into
     * the appropriate range for our hash table.
     *
     * @param in The input buffer to read 4 bytes from
     * @param index The index to read at
     * @param shift The shift value, for ensuring that the resulting value is
     *     within the range of our hash table size
     * @return A 32-bit hash of 4 bytes located at index
     */
    private static int Hash(ByteBuf @in, int index, int shift)
    {
        return @in.GetInt(index) * 0x1e35a7bd >>> shift;
    }

    /**
     * Returns a short[] to be used as a hashtable
     *
     * @param hashTableSize the size for the hashtable
     * @return An appropriately sized empty hashtable
     */
    private short[] GetHashTable(int hashTableSize)
    {
        if (reuseHashtable)
        {
            return GetHashTableFastThreadLocalArrayFill(hashTableSize);
        }

        return new short[hashTableSize];
    }

    /**
     * Returns a short[] from a FastThreadLocal, zeroing for correctness
     * creating a new one and resizing it if necessary
     *
     * @return An appropriately sized empty hashtable
     * @param hashTableSize
     */
    // The Java end index is also the .NET element count because this fill starts at zero.
    public static short[] GetHashTableFastThreadLocalArrayFill(int hashTableSize)
    {
        short[] hashTable = HASH_TABLE.Get();
        if (hashTable == null || hashTable.Length < hashTableSize)
        {
            hashTable = new short[hashTableSize];
            HASH_TABLE.Set(hashTable);
            return hashTable;
        }

        Array.Fill(hashTable, (short) 0, 0, hashTableSize);
        return hashTable;
    }

    /**
     * Iterates over the supplied input buffer between the supplied minIndex and
     * maxIndex to find how long our matched copy overlaps with an already-written
     * literal value.
     *
     * @param in The input buffer to scan over
     * @param minIndex The index in the input buffer to start scanning from
     * @param inIndex The index of the start of our copy
     * @param maxIndex The length of our input buffer
     * @return The number of bytes for which our candidate copy is a repeat of
     */
    // CLR: the two GetByte results are compared for equality only, which does not depend on the sign of the byte.
    private static int FindMatchingLength(ByteBuf @in, int minIndex, int inIndex, int maxIndex)
    {
        int matched = 0;

        while (inIndex <= maxIndex - 4 &&
                @in.GetInt(inIndex) == @in.GetInt(minIndex + matched))
        {
            inIndex += 4;
            matched += 4;
        }

        while (inIndex < maxIndex && @in.GetByte(minIndex + matched) == @in.GetByte(inIndex))
        {
            ++inIndex;
            ++matched;
        }

        return matched;
    }

    /**
     * Calculates the minimum number of bits required to encode a value.  This can
     * then in turn be used to calculate the number of septets or octets (as
     * appropriate) to use to encode a length parameter.
     *
     * @param value The value to calculate the minimum number of bits required to encode
     * @return The minimum number of bits required to encode the supplied value
     */
    // CLR: Integer.highestOneBit(value) "Returns an int value with at most a single one-bit, in the position of the
    // highest-order ("leftmost") one-bit in the specified int value. Returns zero if the specified value ... is equal to
    // zero" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html). The BCL has no such
    // method; value & (int.MinValue >>> BitOperations.LeadingZeroCount((uint) value)) computes it: the shifted sign bit
    // lands on the highest one-bit, and for zero the count is 32, which the int shift masks to 0, so the AND gives 0.
    private static int BitsToEncode(int value)
    {
        int highestOneBit = value & (int.MinValue >>> BitOperations.LeadingZeroCount((uint) value));
        int bitLength = 0;

        while ((highestOneBit >>= 1) != 0)
        {
            bitLength++;
        }

        return bitLength;
    }

    /**
     * Writes a literal to the supplied output buffer by directly copying from
     * the input buffer.  The literal is taken from the current readerIndex
     * up to the supplied length.
     *
     * @param in The input buffer to copy from
     * @param out The output buffer to copy to
     * @param length The length of the literal to copy
     */
    internal static void EncodeLiteral(ByteBuf @in, ByteBuf @out, int length)
    {
        if (length < 61)
        {
            @out.WriteByte(length - 1 << 2);
        }
        else
        {
            int bitLength = BitsToEncode(length - 1);
            int bytesToEncode = 1 + bitLength / 8;
            @out.WriteByte(59 + bytesToEncode << 2);

            for (int i = 0; i < bytesToEncode; i++)
            {
                @out.WriteByte(length - 1 >> i * 8 & 0x0ff);
            }
        }

        @out.WriteBytes(@in, length);
    }

    private static void EncodeCopyWithOffset(ByteBuf @out, int offset, int length)
    {
        if (length < 12 && offset < 2048)
        {
            @out.WriteByte(COPY_1_BYTE_OFFSET | length - 4 << 2 | offset >> 8 << 5);
            @out.WriteByte(offset & 0x0ff);
        }
        else
        {
            @out.WriteByte(COPY_2_BYTE_OFFSET | length - 1 << 2);
            @out.WriteByte(offset & 0x0ff);
            @out.WriteByte(offset >> 8 & 0x0ff);
        }
    }

    /**
     * Encodes a series of copies, each at most 64 bytes in length.
     *
     * @param out The output buffer to write the copy pointer to
     * @param offset The offset at which the original instance lies
     * @param length The length of the original instance
     */
    private static void EncodeCopy(ByteBuf @out, int offset, int length)
    {
        while (length >= 68)
        {
            EncodeCopyWithOffset(@out, offset, 64);
            length -= 64;
        }

        if (length > 64)
        {
            EncodeCopyWithOffset(@out, offset, 60);
            length -= 60;
        }

        EncodeCopyWithOffset(@out, offset, length);
    }

    public void Decode(ByteBuf @in, ByteBuf @out)
    {
        while (@in.IsReadable())
        {
            switch (state)
            {
            case State.READING_PREAMBLE:
                int uncompressedLength = ReadPreamble(@in);
                if (uncompressedLength == PREAMBLE_NOT_FULL)
                {
                    // We've not yet read all of the preamble, so wait until we can
                    return;
                }
                if (uncompressedLength == 0)
                {
                    // Should never happen, but it does mean we have nothing further to do
                    return;
                }

                @out.EnsureWritable(uncompressedLength);
                state = State.READING_TAG;
                // fall through
                goto case State.READING_TAG;
            case State.READING_TAG:
                if (!@in.IsReadable())
                {
                    return;
                }

                tag = @in.ReadByte();

                switch (tag & 0x03)
                {
                case LITERAL:
                    state = State.READING_LITERAL;
                    break;
                case COPY_1_BYTE_OFFSET:
                case COPY_2_BYTE_OFFSET:
                case COPY_4_BYTE_OFFSET:
                    state = State.READING_COPY;
                    break;
                }

                break;
            case State.READING_LITERAL:
                int literalWritten = DecodeLiteral(tag, @in, @out);
                if (literalWritten != NOT_ENOUGH_INPUT)
                {
                    state = State.READING_TAG;
                    written += literalWritten;
                }
                else
                {
                    // Need to wait for more data
                    return;
                }

                break;
            case State.READING_COPY:
                int decodeWritten;

                switch (tag & 0x03)
                {
                case COPY_1_BYTE_OFFSET:
                    decodeWritten = DecodeCopyWith1ByteOffset(tag, @in, @out, written);
                    if (decodeWritten != NOT_ENOUGH_INPUT)
                    {
                        state = State.READING_TAG;
                        written += decodeWritten;
                    }
                    else
                    {
                        // Need to wait for more data
                        return;
                    }

                    break;
                case COPY_2_BYTE_OFFSET:
                    decodeWritten = DecodeCopyWith2ByteOffset(tag, @in, @out, written);
                    if (decodeWritten != NOT_ENOUGH_INPUT)
                    {
                        state = State.READING_TAG;
                        written += decodeWritten;
                    }
                    else
                    {
                        // Need to wait for more data
                        return;
                    }

                    break;
                case COPY_4_BYTE_OFFSET:
                    decodeWritten = DecodeCopyWith4ByteOffset(tag, @in, @out, written);
                    if (decodeWritten != NOT_ENOUGH_INPUT)
                    {
                        state = State.READING_TAG;
                        written += decodeWritten;
                    }
                    else
                    {
                        // Need to wait for more data
                        return;
                    }

                    break;
                }

                break;
            }
        }
    }

    /**
     * Reads the length varint (a series of bytes, where the lower 7 bits
     * are data and the upper bit is a flag to indicate more bytes to be
     * read).
     *
     * @param in The input buffer to read the preamble from
     * @return The calculated length based on the input buffer, or 0 if
     *   no preamble is able to be calculated
     */
    // CLR: readUnsignedByte() is ReadByte(), which is unsigned in the Buffer port.
    private static int ReadPreamble(ByteBuf @in)
    {
        int length = 0;
        int byteIndex = 0;

        while (@in.IsReadable())
        {
            int current = @in.ReadByte();
            length |= (current & 0x7f) << byteIndex++ * 7;
            if ((current & 0x80) == 0)
            {
                return length;
            }

            if (byteIndex >= 4)
            {
                throw new DecompressionException("Preamble is greater than 4 bytes");
            }
        }

        return 0;
    }

    /**
     * Get the length varint (a series of bytes, where the lower 7 bits
     * are data and the upper bit is a flag to indicate more bytes to be
     * read).
     *
     * @param in The input buffer to get the preamble from
     * @return The calculated length based on the input buffer, or 0 if
     *   no preamble is able to be calculated
     */
    internal int GetPreamble(ByteBuf @in)
    {
        if (state == State.READING_PREAMBLE)
        {
            int readerIndex = @in.ReaderIndex;

            try
            {
                return ReadPreamble(@in);
            }
            finally
            {
                @in.ReaderIndex = readerIndex;
            }
        }

        return 0;
    }

    /**
     * Reads a literal from the input buffer directly to the output buffer.
     * A "literal" is an uncompressed segment of data stored directly in the
     * byte stream.
     *
     * @param tag The tag that identified this segment as a literal is also
     *            used to encode part of the length of the data
     * @param in The input buffer to read the literal from
     * @param out The output buffer to write the literal to
     * @return The number of bytes appended to the output buffer, or -1 to indicate "try again later"
     */
    // CLR: readUnsignedByte() is ReadByte() and readUnsignedShortLE() returns a ushort, both unsigned as in Java.
    internal static int DecodeLiteral(byte tag, ByteBuf @in, ByteBuf @out)
    {
        @in.MarkReaderIndex();
        int length;

        switch(tag >> 2 & 0x3F)
        {
        case 60:
            if (!@in.IsReadable())
            {
                return NOT_ENOUGH_INPUT;
            }

            length = @in.ReadByte();
            break;
        case 61:
            if (@in.ReadableBytes < 2)
            {
                return NOT_ENOUGH_INPUT;
            }

            length = @in.ReadUnsignedShortLE();
            break;
        case 62:
            if (@in.ReadableBytes < 3)
            {
                return NOT_ENOUGH_INPUT;
            }

            length = @in.ReadUnsignedMediumLE();
            break;
        case 63:
            if (@in.ReadableBytes < 4)
            {
                return NOT_ENOUGH_INPUT;
            }

            length = @in.ReadIntLE();
            break;
        default:
            length = tag >> 2 & 0x3F;
            break;
        }

        length += 1;

        if (@in.ReadableBytes < length)
        {
            @in.ResetReaderIndex();
            return NOT_ENOUGH_INPUT;
        }

        @out.WriteBytes(@in, length);
        return length;
    }

    /**
     * Reads a compressed reference offset and length from the supplied input
     * buffer, seeks back to the appropriate place in the input buffer and
     * writes the found data to the supplied output stream.
     *
     * @param tag The tag used to identify this as a copy is also used to encode
     *     the length and part of the offset
     * @param in The input buffer to read from
     * @param out The output buffer to write to
     * @return The number of bytes appended to the output buffer, or -1 to indicate
     *     "try again later"
     * @throws DecompressionException If the read offset is invalid
     */
    private static int DecodeCopyWith1ByteOffset(byte tag, ByteBuf @in, ByteBuf @out, int writtenSoFar)
    {
        if (!@in.IsReadable())
        {
            return NOT_ENOUGH_INPUT;
        }

        int initialIndex = @out.WriterIndex;
        int length = 4 + ((tag & 0x01c) >> 2);
        int offset = (tag & 0x0e0) << 8 >> 5 | @in.ReadByte();

        ValidateOffset(offset, writtenSoFar);

        @out.MarkReaderIndex();

        if (offset < length)
        {
            int copies = length / offset;

            for (; copies > 0; copies--)
            {
                @out.ReaderIndex = initialIndex - offset;
                @out.ReadBytes(@out, offset);
            }

            if (length % offset != 0)
            {
                @out.ReaderIndex = initialIndex - offset;
                @out.ReadBytes(@out, length % offset);
            }
        }
        else
        {
            @out.ReaderIndex = initialIndex - offset;
            @out.ReadBytes(@out, length);
        }

        @out.ResetReaderIndex();

        return length;
    }

    /**
     * Reads a compressed reference offset and length from the supplied input
     * buffer, seeks back to the appropriate place in the input buffer and
     * writes the found data to the supplied output stream.
     *
     * @param tag The tag used to identify this as a copy is also used to encode
     *     the length and part of the offset
     * @param in The input buffer to read from
     * @param out The output buffer to write to
     * @throws DecompressionException If the read offset is invalid
     * @return The number of bytes appended to the output buffer, or -1 to indicate
     *     "try again later"
     */
    private static int DecodeCopyWith2ByteOffset(byte tag, ByteBuf @in, ByteBuf @out, int writtenSoFar)
    {
        if (@in.ReadableBytes < 2)
        {
            return NOT_ENOUGH_INPUT;
        }

        int initialIndex = @out.WriterIndex;
        int length = 1 + (tag >> 2 & 0x03f);
        int offset = @in.ReadUnsignedShortLE();

        ValidateOffset(offset, writtenSoFar);

        @out.MarkReaderIndex();

        if (offset < length)
        {
            int copies = length / offset;

            for (; copies > 0; copies--)
            {
                @out.ReaderIndex = initialIndex - offset;
                @out.ReadBytes(@out, offset);
            }

            if (length % offset != 0)
            {
                @out.ReaderIndex = initialIndex - offset;
                @out.ReadBytes(@out, length % offset);
            }
        }
        else
        {
            @out.ReaderIndex = initialIndex - offset;
            @out.ReadBytes(@out, length);
        }

        @out.ResetReaderIndex();

        return length;
    }

    /**
     * Reads a compressed reference offset and length from the supplied input
     * buffer, seeks back to the appropriate place in the input buffer and
     * writes the found data to the supplied output stream.
     *
     * @param tag The tag used to identify this as a copy is also used to encode
     *     the length and part of the offset
     * @param in The input buffer to read from
     * @param out The output buffer to write to
     * @return The number of bytes appended to the output buffer, or -1 to indicate
     *     "try again later"
     * @throws DecompressionException If the read offset is invalid
     */
    private static int DecodeCopyWith4ByteOffset(byte tag, ByteBuf @in, ByteBuf @out, int writtenSoFar)
    {
        if (@in.ReadableBytes < 4)
        {
            return NOT_ENOUGH_INPUT;
        }

        int initialIndex = @out.WriterIndex;
        int length = 1 + (tag >> 2 & 0x03F);
        int offset = @in.ReadIntLE();

        ValidateOffset(offset, writtenSoFar);

        @out.MarkReaderIndex();

        if (offset < length)
        {
            int copies = length / offset;

            for (; copies > 0; copies--)
            {
                @out.ReaderIndex = initialIndex - offset;
                @out.ReadBytes(@out, offset);
            }

            if (length % offset != 0)
            {
                @out.ReaderIndex = initialIndex - offset;
                @out.ReadBytes(@out, length % offset);
            }
        }
        else
        {
            @out.ReaderIndex = initialIndex - offset;
            @out.ReadBytes(@out, length);
        }

        @out.ResetReaderIndex();

        return length;
    }

    /**
     * Validates that the offset extracted from a compressed reference is within
     * the permissible bounds of an offset (0 < offset < Integer.MAX_VALUE), and does not
     * exceed the length of the chunk currently read so far.
     *
     * @param offset The offset extracted from the compressed reference
     * @param chunkSizeSoFar The number of bytes read so far from this chunk
     * @throws DecompressionException if the offset is invalid
     */
    private static void ValidateOffset(int offset, int chunkSizeSoFar)
    {
        if (offset == 0)
        {
            throw new DecompressionException("Offset is less than minimum permissible value");
        }

        if (offset < 0)
        {
            // Due to arithmetic overflow
            throw new DecompressionException("Offset is greater than maximum value supported by this implementation");
        }

        if (offset > chunkSizeSoFar)
        {
            throw new DecompressionException("Offset exceeds size of chunk");
        }
    }

    /**
     * Computes the CRC32C checksum of the supplied data and performs the "mask" operation
     * on the computed checksum
     *
     * @param data The input data to calculate the CRC32C checksum of
     */
    internal static int CalculateChecksum(ByteBuf data)
    {
        return CalculateChecksum(data, data.ReaderIndex, data.ReadableBytes);
    }

    /**
     * Computes the CRC32C checksum of the supplied data and performs the "mask" operation
     * on the computed checksum
     *
     * @param data The input data to calculate the CRC32C checksum of
     */
    internal static int CalculateChecksum(ByteBuf data, int offset, int length)
    {
        Crc32c crc32 = new Crc32c();

        try
        {
            crc32.Update(data, offset, length);

            return MaskChecksum(crc32.GetValue());
        }
        finally
        {
            crc32.Reset();
        }
    }

    /**
     * Computes the CRC32C checksum of the supplied data, performs the "mask" operation
     * on the computed checksum, and then compares the resulting masked checksum to the
     * supplied checksum.
     *
     * @param expectedChecksum The checksum decoded from the stream to compare against
     * @param data The input data to calculate the CRC32C checksum of
     * @throws DecompressionException If the calculated and supplied checksums do not match
     */
    internal static void ValidateChecksum(int expectedChecksum, ByteBuf data)
    {
        ValidateChecksum(expectedChecksum, data, data.ReaderIndex, data.ReadableBytes);
    }

    /**
     * Computes the CRC32C checksum of the supplied data, performs the "mask" operation
     * on the computed checksum, and then compares the resulting masked checksum to the
     * supplied checksum.
     *
     * @param expectedChecksum The checksum decoded from the stream to compare against
     * @param data The input data to calculate the CRC32C checksum of
     * @throws DecompressionException If the calculated and supplied checksums do not match
     */
    // CLR: Integer.toHexString(i) writes the int "as an unsigned integer in base 16"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html); the "x" format of an
    // int prints a negative int in two's complement as toHexString does, and the masked checksums are often
    // negative.
    internal static void ValidateChecksum(int expectedChecksum, ByteBuf data, int offset, int length)
    {
        int actualChecksum = CalculateChecksum(data, offset, length);
        if (actualChecksum != expectedChecksum)
        {
            throw new DecompressionException(
                    "mismatching checksum: " + actualChecksum.ToString("x", CultureInfo.InvariantCulture) +
                            " (expected: " + expectedChecksum.ToString("x", CultureInfo.InvariantCulture) + ')');
        }
    }

    /**
     * From the spec:
     * <p>
     * "Checksums are not stored directly, but masked, as checksumming data and
     * then its own checksum can be problematic. The masking is the same as used
     * in Apache Hadoop: Rotate the checksum by 15 bits, then add the constant
     * 0xa282ead8 (using wraparound as normal for unsigned integers)."
     *
     * @param checksum The actual checksum of the data
     * @return The masked checksum
     */
    // CLR: the Java literal 0xa282ead8 is an int (negative), which the long addition sign-extends; the unsuffixed C#
    // literal would be a uint (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types),
    // so it is written unchecked((int) 0xa282ead8) to add the same long. The (int) cast keeps the low 32 bits in the
    // unchecked context, as Java's narrowing does.
    internal static int MaskChecksum(long checksum)
    {
        return (int) ((checksum >> 15 | checksum << 17) + unchecked((int) 0xa282ead8));
    }
}
