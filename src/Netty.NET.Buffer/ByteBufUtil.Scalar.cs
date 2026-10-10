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
using System.Buffers.Binary;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
    // CLR: ByteBuf's unsuffixed word operations are always big endian. Explicit LE methods
    // replace Java's mutable byte-order facade. The unsigned 16-bit result uses ushort.
    // Accessibility is an observation, not a retain token or a guarantee against a concurrent release.
    /**
     * @return whether the specified buffer has a nonzero ref count
     */
    public static bool IsAccessible(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.ReferenceCount != 0;
    }

    /**
     * @throws IllegalReferenceCountException if the buffer has a zero ref count
     * @return the passed in buffer
     */
    public static ByteBuf EnsureAccessible(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int count = buffer.ReferenceCount;
        if (count == 0) throw new IllegalReferenceCountException(count);
        return buffer;
    }

    /**
     * Used to determine if the return value of {@link ByteBuf#ensureWritable(int, boolean)} means that there is
     * adequate space and a write operation will succeed.
     * @param ensureWritableResult The return value from {@link ByteBuf#ensureWritable(int, boolean)}.
     * @return {@code true} if {@code ensureWritableResult} means that there is adequate space and a write operation
     * will succeed.
     */
    public static bool EnsureWritableSuccess(int ensureWritableResult) => ensureWritableResult is 0 or 2;

    /**
     * Toggles the endianness of the specified 16-bit short integer.
     */
    public static short SwapShort(short value) => BinaryPrimitives.ReverseEndianness(value);

    /**
     * Toggles the endianness of the specified 24-bit medium integer.
     */
    public static int SwapMedium(int value)
    {
        int swapped = ((value & 0xff) << 16) | (value & 0xff00) | ((value >> 16) & 0xff);
        return (swapped & 0x800000) != 0 ? swapped | ~0xffffff : swapped;
    }

    /**
     * Toggles the endianness of the specified 32-bit integer.
     */
    public static int SwapInt(int value) => BinaryPrimitives.ReverseEndianness(value);

    /**
     * Toggles the endianness of the specified 64-bit long integer.
     */
    public static long SwapLong(long value) => BinaryPrimitives.ReverseEndianness(value);

    /**
     * Writes a big-endian 16-bit short integer to the buffer.
     */
    public static ByteBuf WriteShortBE(ByteBuf buffer, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.WriteShort(value);
    }

    /**
     * Sets a big-endian 16-bit short integer to the buffer.
     */
    public static ByteBuf SetShortBE(ByteBuf buffer, int index, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.SetShort(index, value);
    }

    /**
     * Writes a big-endian 24-bit medium integer to the buffer.
     */
    public static ByteBuf WriteMediumBE(ByteBuf buffer, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.WriteMedium(value);
    }

    /**
     * Reads a big-endian unsigned 16-bit short integer from the buffer.
     */
    public static ushort ReadUnsignedShortBE(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.ReadUnsignedShort();
    }

    /**
     * Reads a big-endian 32-bit integer from the buffer.
     */
    public static int ReadIntBE(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.ReadInt();
    }
}
