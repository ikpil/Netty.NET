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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
    /**
     * Copies the all content of {@code src} to a {@link ByteBuf} using {@link ByteBuf#writeBytes(byte[], int, int)}.
     *
     * @param src the source string to copy
     * @param dst the destination buffer
     */
    /// <remarks>Copies raw octets, including values above 127. Advances only the writer index.
    /// The destination may grow. Keep source content and destination lifetime/layout stable during the call.</remarks>
    public static void Copy(AsciiString source, ByteBuf destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        Copy(source, 0, destination, source.Length());
    }

    /**
     * Copies the content of {@code src} to a {@link ByteBuf} using {@link ByteBuf#setBytes(int, byte[], int, int)}.
     * Unlike the {@link #copy(AsciiString, ByteBuf)} and {@link #copy(AsciiString, int, ByteBuf, int)} methods,
     * this method do not increase a {@code writerIndex} of {@code dst} buffer.
     *
     * @param src the source string to copy
     * @param srcIdx the starting offset of characters to copy
     * @param dst the destination buffer
     * @param dstIdx the starting offset in the destination buffer
     * @param length the number of characters to copy
     */
    /// <remarks>The source range is relative to the string, including its backing-array offset.
    /// The destination range is bounded by Capacity; no growth or index changes occur.
    /// Overlap follows ByteBuf.SetBytes semantics. A failing composite write may have changed earlier components.</remarks>
    public static void Copy(AsciiString source, int sourceIndex, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ReadOnlySpan<byte> bytes = source.AsSpan().Slice(sourceIndex, length);
        ArgumentNullException.ThrowIfNull(destination);
        destination.SetBytes(destinationIndex, bytes);
    }

    /**
     * Copies the content of {@code src} to a {@link ByteBuf} using {@link ByteBuf#writeBytes(byte[], int, int)}.
     *
     * @param src the source string to copy
     * @param srcIdx the starting offset of characters to copy
     * @param dst the destination buffer
     * @param length the number of characters to copy
     */
    /// <remarks>The source range is checked before destination growth or mutation. The writer index
    /// advances only after a successful copy; reader index, marks and reference counts are unchanged.
    /// Overlap follows ByteBuf.WriteBytes semantics. A failing composite write can leave changed bytes
    /// or increased capacity even though its writer index remains unchanged.</remarks>
    public static void Copy(AsciiString source, int sourceIndex, ByteBuf destination, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ReadOnlySpan<byte> bytes = source.AsSpan().Slice(sourceIndex, length);
        ArgumentNullException.ThrowIfNull(destination);
        destination.WriteBytes(bytes);
    }
}
