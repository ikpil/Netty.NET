/*
 * Copyright 2024 The Netty Project
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
using System.Diagnostics;
using System.Runtime.InteropServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

/**
 * A collection of utility methods that is related with handling {@link AsciiString}.
 */
// Utility
// CLR adaptation: MemoryMarshal reads/writes bounded native-order words,
// including unaligned slices. JVM Unsafe capability does not select this path.
// CLR adaptation: a static class supplies the original non-instantiable utility.
public static class AsciiStringUtil
{
    /**
     * Convert the {@link AsciiString} to a lower case.
     *
     * @param string the {@link AsciiString} to convert
     * @return the new {@link AsciiString} in lower case
     */
    public static AsciiString toLowerCase(AsciiString str)
    {
        byte[] byteArray = str.array();
        int offset = str.arrayOffset();
        int length = str.length();
        if (!containsUpperCase(byteArray, offset, length))
        {
            return str;
        }

        byte[] newByteArray = GC.AllocateUninitializedArray<byte>(length);
        toLowerCase(byteArray, offset, newByteArray);
        return new AsciiString(newByteArray, false);
    }

    private static bool containsUpperCase(byte[] byteArray, int offset, int length)
    {
        int longCount = length >>> 3;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(byteArray.AsSpan(offset, sizeof(long)));
            if (SWARUtil.containsUpperCase(word))
            {
                return true;
            }

            offset += sizeof(long);
        }

        return unrolledContainsUpperCase(byteArray, offset, length & 7);
    }

    private static bool unrolledContainsUpperCase(byte[] byteArray, int offset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(byteArray.AsSpan(offset, sizeof(int)));
            if (SWARUtil.containsUpperCase(word))
            {
                return true;
            }

            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            if (isUpperCase(byteArray[offset]))
            {
                return true;
            }

            if (isUpperCase(byteArray[offset + 1]))
            {
                return true;
            }

            offset += sizeof(short);
        }

        if ((byteCount & sizeof(byte)) != 0)
        {
            return isUpperCase(byteArray[offset]);
        }

        return false;
    }

    private static void toLowerCase(byte[] src, int srcOffset, byte[] dst)
    {
        int length = dst.Length;
        int longCount = length >>> 3;
        int offset = 0;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(src.AsSpan(srcOffset + offset, sizeof(long)));
            MemoryMarshal.Write(dst.AsSpan(offset, sizeof(long)), SWARUtil.toLowerCase(word));
            offset += sizeof(long);
        }

        unrolledToLowerCase(src, srcOffset + offset, dst, offset, length & 7);
    }

    private static void unrolledToLowerCase(byte[] src, int srcPos,
        byte[] dst, int dstOffset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        int offset = 0;
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(src.AsSpan(srcPos + offset, sizeof(int)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(int)), SWARUtil.toLowerCase(word));
            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            short word = MemoryMarshal.Read<short>(src.AsSpan(srcPos + offset, sizeof(short)));
            short result = unchecked((short)((toLowerCase((byte)(word >>> 8)) << 8) | toLowerCase((byte)word)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(short)), result);
            offset += sizeof(short);
        }

        // this is equivalent to byteCount >= Byte.BYTES (i.e. whether byteCount is odd)
        // CLR note: the mask tests the low bit (oddness), not byteCount's magnitude.
        if ((byteCount & sizeof(byte)) != 0)
        {
            dst[dstOffset + offset] = toLowerCase(src[srcPos + offset]);
        }
    }

    /**
     * Convert the {@link AsciiString} to a upper case.
     *
     * @param string the {@link AsciiString} to convert
     * @return the {@link AsciiString} in upper case
     */
    public static AsciiString toUpperCase(AsciiString str)
    {
        byte[] byteArray = str.array();
        int offset = str.arrayOffset();
        int length = str.length();
        if (!containsLowerCase(byteArray, offset, length))
        {
            return str;
        }

        byte[] newByteArray = GC.AllocateUninitializedArray<byte>(length);
        toUpperCase(byteArray, offset, newByteArray);
        return new AsciiString(newByteArray, false);
    }

    private static bool containsLowerCase(byte[] byteArray, int offset, int length)
    {
        int longCount = length >>> 3;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(byteArray.AsSpan(offset, sizeof(long)));
            if (SWARUtil.containsLowerCase(word))
            {
                return true;
            }

            offset += sizeof(long);
        }

        return unrolledContainsLowerCase(byteArray, offset, length & 7);
    }

    private static bool unrolledContainsLowerCase(byte[] byteArray, int offset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(byteArray.AsSpan(offset, sizeof(int)));
            if (SWARUtil.containsLowerCase(word))
            {
                return true;
            }

            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            if (isLowerCase(byteArray[offset]))
            {
                return true;
            }

            if (isLowerCase(byteArray[offset + 1]))
            {
                return true;
            }

            offset += sizeof(short);
        }

        if ((byteCount & sizeof(byte)) != 0)
        {
            return isLowerCase(byteArray[offset]);
        }

        return false;
    }

    private static void toUpperCase(byte[] src, int srcOffset, byte[] dst)
    {
        int length = dst.Length;
        int longCount = length >>> 3;
        int offset = 0;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(src.AsSpan(srcOffset + offset, sizeof(long)));
            MemoryMarshal.Write(dst.AsSpan(offset, sizeof(long)), SWARUtil.toUpperCase(word));
            offset += sizeof(long);
        }

        unrolledToUpperCase(src, srcOffset + offset, dst, offset, length & 7);
    }

    private static void unrolledToUpperCase(byte[] src, int srcOffset,
        byte[] dst, int dstOffset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        int offset = 0;
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(src.AsSpan(srcOffset + offset, sizeof(int)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(int)), SWARUtil.toUpperCase(word));
            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            short word = MemoryMarshal.Read<short>(src.AsSpan(srcOffset + offset, sizeof(short)));
            short result = unchecked((short)((toUpperCase((byte)(word >>> 8)) << 8) | toUpperCase((byte)word)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(short)), result);
            offset += sizeof(short);
        }

        if ((byteCount & sizeof(byte)) != 0)
        {
            dst[dstOffset + offset] = toUpperCase(src[srcOffset + offset]);
        }
    }

    private static bool isLowerCase(byte value)
    {
        return value >= 'a' && value <= 'z';
    }

    /**
     * Check if the given byte is upper case.
     *
     * @param value the byte to check
     * @return {@code true} if the byte is upper case, {@code false} otherwise.
     */
    public static bool isUpperCase(byte value)
    {
        return value >= 'A' && value <= 'Z';
    }

    /**
     * Convert the given byte to lower case.
     *
     * @param value the byte to convert
     * @return the lower case byte
     */
    public static byte toLowerCase(byte value)
    {
        return isUpperCase(value) ? (byte)(value + 32) : value;
    }

    /**
     * Convert the given byte to upper case.
     *
     * @param value the byte to convert
     * @return the upper case byte
     */
    public static byte toUpperCase(byte value)
    {
        return isLowerCase(value) ? (byte)(value - 32) : value;
    }
}
