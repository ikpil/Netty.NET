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
 * Core of FastLZ compression algorithm.
 *
 * This class provides methods for compression and decompression of buffers and saves
 * constants which use by {@link FastLzFrameEncoder} and {@link FastLzFrameDecoder}.
 *
 * This is refactored code of <a href="https://code.google.com/p/jfastlz/">jfastlz</a>
 * library written by William Kinney.
 */
// CLR: The local ref is @ref (a C# keyword). getUnsignedByte(i) is GetByte(i), which is unsigned in the Buffer
// port. The getByte results are only compared for equality or copied with setByte, which writes the low eight
// bits, so the unsigned bytes give the Java results; the one sign-dependent read is the level in decompress (see
// there). Each (byte) cast of a setByte argument keeps the low eight bits as Java's narrowing does, and long
// distance/len/ofs/ctrl arithmetic keeps the Java types. String.format("%d") is string.Format with the invariant
// culture. The C# compiler reports nothing for compress(), so @SuppressWarnings ("IdentityBinaryExpression"),
// which silences an IDE inspection of the source, has no counterpart.
internal sealed class FastLz
{

    private const int MAX_DISTANCE = 8191;
    private const int MAX_FARDISTANCE = 65535 + MAX_DISTANCE - 1;

    private const int HASH_LOG = 13;
    private const int HASH_SIZE = 1 << HASH_LOG; // 8192
    private const int HASH_MASK = HASH_SIZE - 1;

    private const int MAX_COPY = 32;
    private const int MAX_LEN = 256 + 8;

    private const int MIN_RECOMENDED_LENGTH_FOR_LEVEL_2 = 1024 * 64;

    internal const int MAGIC_NUMBER = 'F' << 16 | 'L' << 8 | 'Z';

    internal const byte BLOCK_TYPE_NON_COMPRESSED = 0x00;
    internal const byte     BLOCK_TYPE_COMPRESSED = 0x01;
    internal const byte    BLOCK_WITHOUT_CHECKSUM = 0x00;
    internal const byte       BLOCK_WITH_CHECKSUM = 0x10;

    internal const int OPTIONS_OFFSET = 3;
    internal const int CHECKSUM_OFFSET = 4;

    internal const int MAX_CHUNK_LENGTH = 0xFFFF;

    /**
     * Do not call {@link #compress(ByteBuf, int, int, ByteBuf, int, int)} for input buffers
     * which length less than this value.
     */
    internal const int MIN_LENGTH_TO_COMPRESSION = 32;

    /**
     * In this case {@link #compress(ByteBuf, int, int, ByteBuf, int, int)} will choose level
     * automatically depending on the length of the input buffer. If length less than
     * {@link #MIN_RECOMENDED_LENGTH_FOR_LEVEL_2} {@link #LEVEL_1} will be chosen,
     * otherwise {@link #LEVEL_2}.
     */
    internal const int LEVEL_AUTO = 0;

    /**
     * Level 1 is the fastest compression and generally useful for short data.
     */
    internal const int LEVEL_1 = 1;

    /**
     * Level 2 is slightly slower but it gives better compression ratio.
     */
    internal const int LEVEL_2 = 2;

    /**
     * The output buffer must be at least 6% larger than the input buffer and can not be smaller than 66 bytes.
     * @param inputLength length of input buffer
     * @return Maximum output buffer length
     */
    internal static int CalculateOutputBufferLength(int inputLength)
    {
        int outputLength = (int) (inputLength * 1.06);
        return Math.Max(outputLength, 66);
    }

    /**
     * Compress a block of data in the input buffer and returns the size of compressed block.
     * The size of input buffer is specified by length. The minimum input buffer size is 32.
     *
     * If the input is not compressible, the return value might be larger than length (input buffer size).
     */
    internal static int Compress(ByteBuf input, int inOffset, int inLength,
                        ByteBuf output, int outOffset, int proposedLevel)
    {
        int level;

        if (proposedLevel == LEVEL_AUTO)
        {
            level = inLength < MIN_RECOMENDED_LENGTH_FOR_LEVEL_2 ? LEVEL_1 : LEVEL_2;
        }
        else
        {
            level = proposedLevel;
        }

        int ip = 0;
        int ipBound = ip + inLength - 2;
        int ipLimit = ip + inLength - 12;

        int op = 0;

        // const flzuint8* htab[HASH_SIZE];
        int[] htab = new int[HASH_SIZE];
        // const flzuint8** hslot;
        int hslot;
        // flzuint32 hval;
        // int OK b/c address starting from 0
        int hval;
        // flzuint32 copy;
        // int OK b/c address starting from 0
        int copy;

        /* sanity check */
        if (inLength < 4)
        {
            if (inLength != 0)
            {
                // *op++ = length-1;
                output.SetByte(outOffset + op++, (byte) (inLength - 1));
                ipBound++;

                while (ip <= ipBound)
                {
                    output.SetByte(outOffset + op++, input.GetByte(inOffset + ip++));
                }

                return inLength + 1;
            }

            // else
            return 0;
        }

        /* initializes hash table */
        //  for (hslot = htab; hslot < htab + HASH_SIZE; hslot++)
        for (hslot = 0; hslot < HASH_SIZE; hslot++)
        {
            //*hslot = ip;
            htab[hslot] = ip;
        }

        /* we start with literal copy */
        copy = 2;
        output.SetByte(outOffset + op++, MAX_COPY - 1);
        output.SetByte(outOffset + op++, input.GetByte(inOffset + ip++));
        output.SetByte(outOffset + op++, input.GetByte(inOffset + ip++));

        /* main loop */
        while (ip < ipLimit)
        {
            int @ref = 0;

            long distance = 0;

            /* minimum match length */
            // flzuint32 len = 3;
            // int OK b/c len is 0 and octal based
            int len = 3;

            /* comparison starting-point */
            int anchor = ip;

            bool matchLabel = false;

            /* check for a run */
            if (level == LEVEL_2)
            {
                //if(ip[0] == ip[-1] && FASTLZ_READU16(ip-1)==FASTLZ_READU16(ip+1))
                if (input.GetByte(inOffset + ip) == input.GetByte(inOffset + ip - 1) &&
                        ReadU16(input, inOffset + ip - 1) == ReadU16(input, inOffset + ip + 1))
                {
                    distance = 1;
                    ip += 3;
                    @ref = anchor + (3 - 1);

                    /*
                     * goto match;
                     */
                    matchLabel = true;
                }
            }

            if (!matchLabel)
            {
                /* find potential match */
                // HASH_FUNCTION(hval,ip);
                hval = HashFunction(input, inOffset + ip);
                // hslot = htab + hval;
                hslot = hval;
                // ref = htab[hval];
                @ref = htab[hval];

                /* calculate distance to the match */
                distance = anchor - @ref;

                /* update hash table */
                //*hslot = anchor;
                htab[hslot] = anchor;

                /* is this a match? check the first 3 bytes */
                if (distance == 0
                        || (level == LEVEL_1 ? distance >= MAX_DISTANCE : distance >= MAX_FARDISTANCE)
                        || input.GetByte(inOffset + @ref++) != input.GetByte(inOffset + ip++)
                        || input.GetByte(inOffset + @ref++) != input.GetByte(inOffset + ip++)
                        || input.GetByte(inOffset + @ref++) != input.GetByte(inOffset + ip++))
                {
                    /*
                     * goto literal;
                     */
                    output.SetByte(outOffset + op++, input.GetByte(inOffset + anchor++));
                    ip = anchor;
                    copy++;

                    if (copy == MAX_COPY)
                    {
                        copy = 0;
                        output.SetByte(outOffset + op++, MAX_COPY - 1);
                    }

                    continue;
                }

                if (level == LEVEL_2)
                {
                    /* far, needs at least 5-byte match */
                    if (distance >= MAX_DISTANCE)
                    {
                        if (input.GetByte(inOffset + ip++) != input.GetByte(inOffset + @ref++)
                                || input.GetByte(inOffset + ip++) != input.GetByte(inOffset + @ref++))
                        {
                            /*
                             * goto literal;
                             */
                            output.SetByte(outOffset + op++, input.GetByte(inOffset + anchor++));
                            ip = anchor;
                            copy++;

                            if (copy == MAX_COPY)
                            {
                                copy = 0;
                                output.SetByte(outOffset + op++, MAX_COPY - 1);
                            }

                            continue;
                        }

                        len += 2;
                    }
                }
            } // end if(!matchLabel)

            /*
             * match:
             */
            /* last matched byte */
            ip = anchor + len;

            /* distance is biased */
            distance--;

            if (distance == 0)
            {
                /* zero distance means a run */
                //flzuint8 x = ip[-1];
                byte x = input.GetByte(inOffset + ip - 1);

                while (ip < ipBound)
                {
                    if (input.GetByte(inOffset + @ref++) != x)
                    {
                        break;
                    }
                    else
                    {
                        ip++;
                    }
                }
            }
            else
            {
                /* safe because the outer check against ip limit */
                bool missMatch = false;

                for (int i = 0; i < 8; i++)
                {
                    if (input.GetByte(inOffset + @ref++) != input.GetByte(inOffset + ip++))
                    {
                        missMatch = true;
                        break;
                    }
                }

                if (!missMatch)
                {
                    while (ip < ipBound)
                    {
                        if (input.GetByte(inOffset + @ref++) != input.GetByte(inOffset + ip++))
                        {
                            break;
                        }
                    }
                }
            }

            /* if we have copied something, adjust the copy count */
            if (copy != 0)
            {
                /* copy is biased, '0' means 1 byte copy */
                // *(op-copy-1) = copy-1;
                output.SetByte(outOffset + op - copy - 1, (byte) (copy - 1));
            }
            else
            {
                /* back, to overwrite the copy count */
                op--;
            }

            /* reset literal counter */
            copy = 0;

            /* length is biased, '1' means a match of 3 bytes */
            ip -= 3;
            len = ip - anchor;

            /* encode the match */
            if (level == LEVEL_2)
            {
                if (distance < MAX_DISTANCE)
                {
                    if (len < 7)
                    {
                        output.SetByte(outOffset + op++, (byte) ((len << 5) + (distance >>> 8)));
                        output.SetByte(outOffset + op++, (byte) (distance & 255));
                    }
                    else
                    {
                        output.SetByte(outOffset + op++, (byte) ((7 << 5) + (distance >>> 8)));

                        for (len -= 7; len >= 255; len -= 255)
                        {
                            output.SetByte(outOffset + op++, (byte) 255);
                        }

                        output.SetByte(outOffset + op++, (byte) len);
                        output.SetByte(outOffset + op++, (byte) (distance & 255));
                    }
                }
                else
                {
                    /* far away, but not yet in the another galaxy... */
                    if (len < 7)
                    {
                        distance -= MAX_DISTANCE;
                        output.SetByte(outOffset + op++, (byte) ((len << 5) + 31));
                        output.SetByte(outOffset + op++, (byte) 255);
                        output.SetByte(outOffset + op++, (byte) (distance >>> 8));
                        output.SetByte(outOffset + op++, (byte) (distance & 255));
                    }
                    else
                    {
                        distance -= MAX_DISTANCE;
                        output.SetByte(outOffset + op++, (byte) ((7 << 5) + 31));

                        for (len -= 7; len >= 255; len -= 255)
                        {
                            output.SetByte(outOffset + op++, (byte) 255);
                        }

                        output.SetByte(outOffset + op++, (byte) len);
                        output.SetByte(outOffset + op++, (byte) 255);
                        output.SetByte(outOffset + op++, (byte) (distance >>> 8));
                        output.SetByte(outOffset + op++, (byte) (distance & 255));
                    }
                }
            }
            else
            {
                if (len > MAX_LEN - 2)
                {
                    while (len > MAX_LEN - 2)
                    {
                        output.SetByte(outOffset + op++, (byte) ((7 << 5) + (distance >>> 8)));
                        output.SetByte(outOffset + op++, (byte) (MAX_LEN - 2 - 7 - 2));
                        output.SetByte(outOffset + op++, (byte) (distance & 255));
                        len -= MAX_LEN - 2;
                    }
                }

                if (len < 7)
                {
                    output.SetByte(outOffset + op++, (byte) ((len << 5) + (distance >>> 8)));
                    output.SetByte(outOffset + op++, (byte) (distance & 255));
                }
                else
                {
                    output.SetByte(outOffset + op++, (byte) ((7 << 5) + (distance >>> 8)));
                    output.SetByte(outOffset + op++, (byte) (len - 7));
                    output.SetByte(outOffset + op++, (byte) (distance & 255));
                }
            }

            /* update the hash at match boundary */
            //HASH_FUNCTION(hval,ip);
            hval = HashFunction(input, inOffset + ip);
            htab[hval] = ip++;

            //HASH_FUNCTION(hval,ip);
            hval = HashFunction(input, inOffset + ip);
            htab[hval] = ip++;

            /* assuming literal copy */
            output.SetByte(outOffset + op++, MAX_COPY - 1);

            continue;

            // Moved to be inline, with a 'continue'
            /*
             * literal:
             *
              output[outOffset + op++] = input[inOffset + anchor++];
              ip = anchor;
              copy++;
              if(copy == MAX_COPY){
                copy = 0;
                output[outOffset + op++] = MAX_COPY-1;
              }
            */
        }

        /* left-over as literal copy */
        ipBound++;

        while (ip <= ipBound)
        {
            output.SetByte(outOffset + op++, input.GetByte(inOffset + ip++));
            copy++;

            if (copy == MAX_COPY)
            {
                copy = 0;
                output.SetByte(outOffset + op++, MAX_COPY - 1);
            }
        }

        /* if we have copied something, adjust the copy length */
        if (copy != 0)
        {
            //*(op-copy-1) = copy-1;
            output.SetByte(outOffset + op - copy - 1, (byte) (copy - 1));
        }
        else
        {
            op--;
        }

        if (level == LEVEL_2)
        {
            /* marker for fastlz2 */
            output.SetByte(outOffset, output.GetByte(outOffset) | 1 << 5);
        }

        return op;
    }

    /**
     * Decompress a block of compressed data and returns the size of the decompressed block.
     * If error occurs, e.g. the compressed data is corrupted or the output buffer is not large
     * enough, then 0 (zero) will be returned instead.
     *
     * Decompression is memory safe and guaranteed not to write the output buffer
     * more than what is specified in outLength.
     */
    // CLR: the level reads the first byte as the signed Java byte, (sbyte) input.GetByte(inOffset), because the arithmetic
    // shift of a negative byte gives a negative level: a first byte of 0x80 or more reports "invalid level: -3" to 0, as
    // in Java, where the unsigned byte would give 5 to 8. ref -= ofs (int ref, long ofs) is ref = (int) (ref - ofs):
    // Java's compound assignment "is equivalent to E1 = (T) ((E1) op (E2))"
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-15.html#jls-15.26.2), while the C# compiler rejects
    // ref -= ofs for an int ref and a long ofs (CS0266, cannot implicitly convert long to int; checked with the scratch
    // build); the explicit cast keeps the low 32 bits in the unchecked context, as Java's implied cast does.
    internal static int Decompress(ByteBuf input, int inOffset, int inLength,
                          ByteBuf output, int outOffset, int outLength)
    {
        if (inLength == 0)
        {
            return 0;
        }

        //int level = ((*(const flzuint8*)input) >> 5) + 1;
        int level = ((sbyte) input.GetByte(inOffset) >> 5) + 1;
        if (level != LEVEL_1 && level != LEVEL_2)
        {
            throw new DecompressionException(string.Format(CultureInfo.InvariantCulture,
                    "invalid level: {0} (expected: {1} or {2})", level, LEVEL_1, LEVEL_2
            ));
        }

        // const flzuint8* ip = (const flzuint8*) input;
        int ip = 0;
        // flzuint8* op = (flzuint8*) output;
        int op = 0;
        // flzuint32 ctrl = (*ip++) & 31;
        long ctrl = input.GetByte(inOffset + ip++) & 31;

        int loop = 1;

        do
        {
            //  const flzuint8* ref = op;
            int @ref = op;
            // flzuint32 len = ctrl >> 5;
            long len = ctrl >> 5;
            // flzuint32 ofs = (ctrl & 31) << 8;
            long ofs = (ctrl & 31) << 8;

            if (ctrl >= 32)
            {
                len--;
                // ref -= ofs;
                @ref = (int) (@ref - ofs);

                int code;

                if (len == 6)
                {
                    if (level == LEVEL_1)
                    {
                        if (ip >= inLength)
                        {
                            return 0;
                        }

                        // len += *ip++;
                        len += input.GetByte(inOffset + ip++);
                    }
                    else
                    {
                        do
                        {
                            if (ip >= inLength)
                            {
                                return 0;
                            }

                            code = input.GetByte(inOffset + ip++);
                            len += code;
                        } while (code == 255);
                    }
                }

                if (level == LEVEL_1)
                {
                    if (ip >= inLength)
                    {
                        return 0;
                    }

                    //  ref -= *ip++;
                    @ref -= input.GetByte(inOffset + ip++);
                }
                else
                {
                    if (ip >= inLength)
                    {
                        return 0;
                    }

                    code = input.GetByte(inOffset + ip++);
                    @ref -= code;

                    /* match from 16-bit distance */
                    // if(FASTLZ_UNEXPECT_CONDITIONAL(code==255))
                    // if(FASTLZ_EXPECT_CONDITIONAL(ofs==(31 << 8)))
                    if (code == 255 && ofs == 31 << 8)
                    {
                        if (ip + 2 > inLength)
                        {
                            return 0;
                        }

                        ofs = input.GetByte(inOffset + ip++) << 8;
                        ofs += input.GetByte(inOffset + ip++);

                        @ref = (int) (op - ofs - MAX_DISTANCE);
                    }
                }

                // if the output index + length of block(?) + 3(?) is over the output limit?
                if (op + len + 3 > outLength)
                {
                    return 0;
                }

                // if (FASTLZ_UNEXPECT_CONDITIONAL(ref-1 < (flzuint8 *)output))
                // if the address space of ref-1 is < the address of output?
                // if we are still at the beginning of the output address?
                if (@ref - 1 < 0)
                {
                    return 0;
                }

                if (ip < inLength)
                {
                    ctrl = input.GetByte(inOffset + ip++);
                }
                else
                {
                    loop = 0;
                }

                if (@ref == op)
                {
                    /* optimize copy for a run */
                    // flzuint8 b = ref[-1];
                    byte b = output.GetByte(outOffset + @ref - 1);
                    output.SetByte(outOffset + op++, b);
                    output.SetByte(outOffset + op++, b);
                    output.SetByte(outOffset + op++, b);

                    while (len != 0)
                    {
                        output.SetByte(outOffset + op++, b);
                        --len;
                    }
                }
                else
                {
                    /* copy from reference */
                    @ref--;

                    // *op++ = *ref++;
                    output.SetByte(outOffset + op++, output.GetByte(outOffset + @ref++));
                    output.SetByte(outOffset + op++, output.GetByte(outOffset + @ref++));
                    output.SetByte(outOffset + op++, output.GetByte(outOffset + @ref++));

                    while (len != 0)
                    {
                        output.SetByte(outOffset + op++, output.GetByte(outOffset + @ref++));
                        --len;
                    }
                }
            }
            else
            {
                ctrl++;

                if (op + ctrl > outLength)
                {
                    return 0;
                }
                if (ip + ctrl > inLength)
                {
                    return 0;
                }

                //*op++ = *ip++;
                output.SetByte(outOffset + op++, input.GetByte(inOffset + ip++));

                for (--ctrl; ctrl != 0; ctrl--)
                {
                    // *op++ = *ip++;
                    output.SetByte(outOffset + op++, input.GetByte(inOffset + ip++));
                }

                loop = ip < inLength ? 1 : 0;
                if (loop != 0)
                {
                    //  ctrl = *ip++;
                    ctrl = input.GetByte(inOffset + ip++);
                }
            }

        // while(FASTLZ_EXPECT_CONDITIONAL(loop));
        } while (loop != 0);

        //  return op - (flzuint8*)output;
        return op;
    }

    private static int HashFunction(ByteBuf p, int offset)
    {
        int v = ReadU16(p, offset);
        v ^= ReadU16(p, offset + 1) ^ v >> 16 - HASH_LOG;
        v &= HASH_MASK;
        return v;
    }

    private static int ReadU16(ByteBuf data, int offset)
    {
        if (offset + 1 >= data.ReadableBytes)
        {
            return data.GetByte(offset);
        }

        return data.GetByte(offset + 1) << 8 | data.GetByte(offset);
    }

    private FastLz()
    {
    }
}
