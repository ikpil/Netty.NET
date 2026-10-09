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
using System.Buffers;
using System.Runtime.InteropServices;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
/**
     * Create a copy of the underlying storage from {@code buf} into a byte array.
     * The copy will start at {@link ByteBuf#readerIndex()} and copy {@link ByteBuf#readableBytes()} bytes.
     */
    /// <remarks>Returns an independent snapshot of readable bytes without changing indices,
    /// marks or reference counts. Content, layout and lifetime must remain stable during extraction.</remarks>
    public static byte[] GetBytes(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return GetBytes(buffer, buffer.ReaderIndex, buffer.ReadableBytes);
    }

/**
     * Create a copy of the underlying storage from {@code buf} into a byte array.
     * The copy will start at {@code start} and copy {@code length} bytes.
     */
    /// <remarks>The absolute range may extend past WriterIndex up to Capacity.
    /// Returns an independent snapshot; empty results use Array.Empty&lt;byte&gt;().</remarks>
    public static byte[] GetBytes(ByteBuf buffer, int start, int length)
        => GetBytes(buffer, start, length, true);

/**
     * Return an array of the underlying storage from {@code buf} into a byte array.
     * The copy will start at {@code start} and copy {@code length} bytes.
     * If {@code copy} is true a copy will be made of the memory.
     * If {@code copy} is false the underlying storage will be shared, if possible.
     */
    /// <remarks>Sharing requires a writable contiguous mapping of the complete buffer to an
    /// entire managed byte array, and a request covering that entire mapping. Read-only buffers
    /// and components, native storage, partial arrays and segmented ranges are copied.
    /// CLR memory mapping determines eligibility, including contiguous views of composites;
    /// Java's hasArray wrapper restrictions are not reproduced. Empty results use Array.Empty.
    /// A returned array keeps its managed storage alive, but shared content can change;
    /// resizing can detach the previous storage. Use copying for a stable snapshot.
    /// No ownership reference is acquired. Exclude concurrent content/layout/lifetime changes.
    /// Invalid and released ranges throw even when length is zero.</remarks>
    public static byte[] GetBytes(ByteBuf buffer, int start, int length, bool copy)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ReadOnlySequence<byte> bytes = buffer.AsReadOnlySequence(start, length);
        if (length == 0) return Array.Empty<byte>();
        if (!copy && start == 0 && length == buffer.Capacity && !buffer.IsReadOnly)
        {
            try
            {
                // Probe writable memory, so a mutable composite cannot expose the array
                // of a read-only component through a read-only memory borrow.
                if (buffer.TryGetMemory(0, length, out Memory<byte> memory) &&
                    MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out ArraySegment<byte> array) &&
                    array.Offset == 0 && array.Count == array.Array.Length)
                    return array.Array;
            }
            catch (NotSupportedException)
            {
                // A component can deny writable borrowing even when its composite is mutable.
                // Extraction is still supported through the read-only sequence below.
            }
        }
        return bytes.ToArray();
    }
}
