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

public static partial class Unpooled
{
    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    public static ByteBuf WrappedBuffer(params Memory<byte>[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return WrappedBuffer(buffers.Length, buffers);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    public static ByteBuf WrappedBuffer(params ReadOnlyMemory<byte>[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return WrappedBuffer(buffers.Length, buffers);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    /// <remarks>External owners are borrowed and must remain valid during access.
    /// Empty ranges are skipped; multiple supplied ranges produce a composite even
    /// when only one is nonempty. Consolidation runs once after all additions and
    /// copies into owned heap storage if the component limit is exceeded.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params Memory<byte>[] buffers)
        => WrapMemoryBuffers(maxNumComponents, buffers, static memory => memory.IsEmpty, static memory => WrappedBuffer(memory));

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    /// <remarks>Read-only component access rejects writes before consolidation.
    /// The composite itself follows Netty's ordinary writable composite policy;
    /// consolidation creates independent writable storage. Use ByteBuf[] wrapping
    /// to mix writable and read-only ranges explicitly. Default memory is empty;
    /// there are no nullable elements or mutable byte-order cursors.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params ReadOnlyMemory<byte>[] buffers)
        => WrapMemoryBuffers(maxNumComponents, buffers, static memory => memory.IsEmpty, static memory => WrappedBuffer(memory));

    private static ByteBuf WrapMemoryBuffers<T>(int maxNumComponents, T[] buffers,
        Func<T, bool> isEmpty, Func<T, ByteBuf> wrap) where T : struct
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1) return wrap(buffers[0]);
        for (int i = 0; i < buffers.Length; ++i)
        {
            if (isEmpty(buffers[i])) continue;
            var composite = new CompositeByteBuf(maxNumComponents);
            try { return composite.AddWrappedMemory(buffers, i, isEmpty, wrap); }
            catch
            {
                // CLR: only temporary wrapper references were acquired. External
                // owners remain caller-owned even when insertion/consolidation fails.
                ReferenceCountUtil.SafeRelease(composite);
                throw;
            }
        }
        return EmptyBuffer;
    }
}
