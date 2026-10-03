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
    public static AsciiString ToLowerCase(AsciiString str)
    {
        byte[] byteArray = str.Array();
        int offset = str.ArrayOffset();
        int length = str.Length();
        if (!ContainsUpperCase(byteArray, offset, length))
        {
            return str;
        }

        byte[] newByteArray = GC.AllocateUninitializedArray<byte>(length);
        ToLowerCase(byteArray, offset, newByteArray);
        return new AsciiString(newByteArray, false);
    }

    private static bool ContainsUpperCase(byte[] byteArray, int offset, int length)
    {
        int longCount = length >>> 3;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(byteArray.AsSpan(offset, sizeof(long)));
            if (SWARUtil.ContainsUpperCase(word))
            {
                return true;
            }

            offset += sizeof(long);
        }

        return UnrolledContainsUpperCase(byteArray, offset, length & 7);
    }

    private static bool UnrolledContainsUpperCase(byte[] byteArray, int offset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(byteArray.AsSpan(offset, sizeof(int)));
            if (SWARUtil.ContainsUpperCase(word))
            {
                return true;
            }

            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            if (IsUpperCase(byteArray[offset]))
            {
                return true;
            }

            if (IsUpperCase(byteArray[offset + 1]))
            {
                return true;
            }

            offset += sizeof(short);
        }

        if ((byteCount & sizeof(byte)) != 0)
        {
            return IsUpperCase(byteArray[offset]);
        }

        return false;
    }

    private static void ToLowerCase(byte[] src, int srcOffset, byte[] dst)
    {
        int length = dst.Length;
        int longCount = length >>> 3;
        int offset = 0;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(src.AsSpan(srcOffset + offset, sizeof(long)));
            MemoryMarshal.Write(dst.AsSpan(offset, sizeof(long)), SWARUtil.ToLowerCase(word));
            offset += sizeof(long);
        }

        UnrolledToLowerCase(src, srcOffset + offset, dst, offset, length & 7);
    }

    private static void UnrolledToLowerCase(byte[] src, int srcPos,
        byte[] dst, int dstOffset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        int offset = 0;
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(src.AsSpan(srcPos + offset, sizeof(int)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(int)), SWARUtil.ToLowerCase(word));
            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            short word = MemoryMarshal.Read<short>(src.AsSpan(srcPos + offset, sizeof(short)));
            short result = unchecked((short)((ToLowerCase((byte)(word >>> 8)) << 8) | ToLowerCase((byte)word)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(short)), result);
            offset += sizeof(short);
        }

        // this is equivalent to byteCount >= Byte.BYTES (i.e. whether byteCount is odd)
        // CLR note: the mask tests the low bit (oddness), not byteCount's magnitude.
        if ((byteCount & sizeof(byte)) != 0)
        {
            dst[dstOffset + offset] = ToLowerCase(src[srcPos + offset]);
        }
    }

    /**
     * Convert the {@link AsciiString} to a upper case.
     *
     * @param string the {@link AsciiString} to convert
     * @return the {@link AsciiString} in upper case
     */
    public static AsciiString ToUpperCase(AsciiString str)
    {
        byte[] byteArray = str.Array();
        int offset = str.ArrayOffset();
        int length = str.Length();
        if (!ContainsLowerCase(byteArray, offset, length))
        {
            return str;
        }

        byte[] newByteArray = GC.AllocateUninitializedArray<byte>(length);
        ToUpperCase(byteArray, offset, newByteArray);
        return new AsciiString(newByteArray, false);
    }

    private static bool ContainsLowerCase(byte[] byteArray, int offset, int length)
    {
        int longCount = length >>> 3;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(byteArray.AsSpan(offset, sizeof(long)));
            if (SWARUtil.ContainsLowerCase(word))
            {
                return true;
            }

            offset += sizeof(long);
        }

        return UnrolledContainsLowerCase(byteArray, offset, length & 7);
    }

    private static bool UnrolledContainsLowerCase(byte[] byteArray, int offset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(byteArray.AsSpan(offset, sizeof(int)));
            if (SWARUtil.ContainsLowerCase(word))
            {
                return true;
            }

            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            if (IsLowerCase(byteArray[offset]))
            {
                return true;
            }

            if (IsLowerCase(byteArray[offset + 1]))
            {
                return true;
            }

            offset += sizeof(short);
        }

        if ((byteCount & sizeof(byte)) != 0)
        {
            return IsLowerCase(byteArray[offset]);
        }

        return false;
    }

    private static void ToUpperCase(byte[] src, int srcOffset, byte[] dst)
    {
        int length = dst.Length;
        int longCount = length >>> 3;
        int offset = 0;
        for (int i = 0; i < longCount; ++i)
        {
            long word = MemoryMarshal.Read<long>(src.AsSpan(srcOffset + offset, sizeof(long)));
            MemoryMarshal.Write(dst.AsSpan(offset, sizeof(long)), SWARUtil.ToUpperCase(word));
            offset += sizeof(long);
        }

        UnrolledToUpperCase(src, srcOffset + offset, dst, offset, length & 7);
    }

    private static void UnrolledToUpperCase(byte[] src, int srcOffset,
        byte[] dst, int dstOffset, int byteCount)
    {
        Debug.Assert(byteCount >= 0 && byteCount < 8);
        int offset = 0;
        if ((byteCount & sizeof(int)) != 0)
        {
            int word = MemoryMarshal.Read<int>(src.AsSpan(srcOffset + offset, sizeof(int)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(int)), SWARUtil.ToUpperCase(word));
            offset += sizeof(int);
        }

        if ((byteCount & sizeof(short)) != 0)
        {
            short word = MemoryMarshal.Read<short>(src.AsSpan(srcOffset + offset, sizeof(short)));
            short result = unchecked((short)((ToUpperCase((byte)(word >>> 8)) << 8) | ToUpperCase((byte)word)));
            MemoryMarshal.Write(dst.AsSpan(dstOffset + offset, sizeof(short)), result);
            offset += sizeof(short);
        }

        if ((byteCount & sizeof(byte)) != 0)
        {
            dst[dstOffset + offset] = ToUpperCase(src[srcOffset + offset]);
        }
    }

    private static bool IsLowerCase(byte value)
    {
        return value >= 'a' && value <= 'z';
    }

    /**
     * Check if the given byte is upper case.
     *
     * @param value the byte to check
     * @return {@code true} if the byte is upper case, {@code false} otherwise.
     */
    public static bool IsUpperCase(byte value)
    {
        return value >= 'A' && value <= 'Z';
    }

    /**
     * Convert the given byte to lower case.
     *
     * @param value the byte to convert
     * @return the lower case byte
     */
    public static byte ToLowerCase(byte value)
    {
        return IsUpperCase(value) ? (byte)(value + 32) : value;
    }

    /**
     * Convert the given byte to upper case.
     *
     * @param value the byte to convert
     * @return the upper case byte
     */
    public static byte ToUpperCase(byte value)
    {
        return IsLowerCase(value) ? (byte)(value - 32) : value;
    }
}
