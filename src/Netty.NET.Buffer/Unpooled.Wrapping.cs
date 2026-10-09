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
    /// <summary>Returns the shared empty buffer.</summary>
    // CLR: resolves the otherwise ambiguous two params overloads for an empty call.
    public static ByteBuf WrappedBuffer() => EmptyBuffer;
    /// <summary>Returns the shared empty buffer when no inputs are supplied.</summary>
    public static ByteBuf WrappedBuffer(int maxNumComponents) => EmptyBuffer;

    /**
     * Creates a new big-endian buffer which wraps the sub-region of the
     * specified {@code array}.  A modification on the specified array's
     * content will be visible to the returned buffer.
     */
    /// <remarks>CLR argument validation also applies to empty ranges.</remarks>
    public static ByteBuf WrappedBuffer(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length == 0) return EmptyBuffer;
        return offset == 0 && length == bytes.Length ? WrappedBuffer(bytes) : WrappedBuffer(bytes).Slice(offset, length);
    }

    /**
     * Creates a new buffer which wraps the specified buffer's readable bytes.
     * A modification on the specified buffer's content will be visible to the
     * returned buffer.
     * @param buffer The buffer to wrap. Reference count ownership of this variable is transferred to this method.
     * @return The readable portion of the {@code buffer}, or an empty buffer if there is no readable portion.
     * The caller is responsible for releasing this buffer.
     */
    public static ByteBuf WrappedBuffer(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.IsReadable) return buffer.Slice();
        buffer.Release();
        return EmptyBuffer;
    }

    /**
     * Creates a new big-endian composite buffer which wraps the specified
     * arrays without copying them.  A modification on the specified arrays'
     * content will be visible to the returned buffer.
     */
    public static ByteBuf WrappedBuffer(params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        return WrappedBuffer(arrays.Length, arrays);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the readable bytes of the
     * specified buffers without copying them.  A modification on the content
     * of the specified buffers will be visible to the returned buffer.
     * @param buffers The buffers to wrap. Reference count ownership of all variables is transferred to this method.
     * @return The readable portion of the {@code buffers}. The caller is responsible for releasing this buffer.
     */
    public static ByteBuf WrappedBuffer(params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return WrappedBuffer(buffers.Length, buffers);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the specified
     * arrays without copying them.  A modification on the specified arrays'
     * content will be visible to the returned buffer.
     */
    /// <remarks>Empty arrays are skipped; null terminates the input. If more
    /// nonempty components than the limit are present, consolidation copies them.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        if (arrays.Length == 1)
        {
            ArgumentNullException.ThrowIfNull(arrays[0]);
            return WrappedBuffer(arrays[0]);
        }
        for (int i = 0; i < arrays.Length; ++i)
        {
            byte[] array = arrays[i];
            if (array == null) return EmptyBuffer;
            if (array.Length == 0) continue;
            var composite = new CompositeByteBuf(maxNumComponents);
            try { return composite.AddWrappedArrays(arrays, i); }
            catch
            {
                // CLR: a failing factory cannot return its partially built owner.
                // Release its acquired components without masking the original failure.
                ReferenceCountUtil.SafeRelease(composite);
                throw;
            }
        }
        return EmptyBuffer;
    }

    /**
     * Creates a new big-endian composite buffer which wraps the readable bytes of the
     * specified buffers without copying them.  A modification on the content
     * of the specified buffers will be visible to the returned buffer.
     * @param maxNumComponents Advisement as to how many independent buffers are allowed to exist before
     * consolidation occurs.
     * @param buffers The buffers to wrap. Reference count ownership of all variables is transferred to this method.
     * @return The readable portion of the {@code buffers}. The caller is responsible for releasing this buffer.
     */
    /// <remarks>Leading unreadable buffers are released immediately. Once a
    /// readable input is found, array insertion owns subsequent empty components
    /// and safely releases a null-terminated tail. Preflight failure leaves the
    /// untransferred suffix caller-owned. Later failure releases the inaccessible
    /// partial composite, including its successfully transferred prefix.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1) return WrappedBuffer(buffers[0]);
        for (int i = 0; i < buffers.Length; ++i)
        {
            ByteBuf buffer = buffers[i];
            ArgumentNullException.ThrowIfNull(buffer);
            if (!buffer.IsReadable) { buffer.Release(); continue; }
            var composite = new CompositeByteBuf(maxNumComponents);
            try { return composite.AddWrappedComponents(buffers, i); }
            catch
            {
                // CLR: release only transferred ownership. Array preflight may
                // have failed before consuming any inputs from this suffix.
                ReferenceCountUtil.SafeRelease(composite);
                throw;
            }
        }
        return EmptyBuffer;
    }
}
