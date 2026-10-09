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

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
    /**
         * Returns {@code true} if and only if this buffer is read-only.
         */
    public virtual bool IsReadOnly => false;

    // Original deprecated Java factory documentation; AsReadOnly is the supported CLR entry point.
    /**
     * Creates a read-only buffer which disallows any modification operations
     * on the specified {@code buffer}.  The new buffer has the same
     * {@code readerIndex} and {@code writerIndex} with the specified
     * {@code buffer}.
     *
     * @deprecated Use {@link ByteBuf#asReadOnly()}.
     */
    /**
         * Returns a read-only version of this buffer.
         */
    /// <summary>Returns a borrowed read-only view with independent indices and shared reference count.</summary>
    public virtual ByteBuf AsReadOnly()
    {
        EnsureAccessible();
        // We can only use ReadOnlyAbstractByteBuf if we either have nothing to unwrap or the unwrapped buffer is of
        // type AbstractByteBuf. Otherwise we will produce a CCE later.
        // CLR: the original factory rationale is retained; one bounded-memory view avoids subtype casts entirely.
        return IsReadOnly ? this : new ReadOnlyByteBuf(this);
    }

    /**
         * Returns {@code true} if and only if this buffer has enough room to allow writing the specified number of
         * elements.
         */
    /// <summary>Checks write permission and available bytes without growing the buffer.</summary>
    public virtual bool CanWrite(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        return !IsReadOnly && byteCount <= WritableBytes;
    }

    /**
         * Exposes this buffer's sub-region as an NIO {@link ByteBuffer}. The returned buffer
         * either share or contains the copied content of this buffer, while changing the position
         * and limit of the returned NIO buffer does not affect the indexes and marks of this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of this buffer.
         * Please note that the returned NIO buffer will not see the changes of this buffer if this buffer
         * is a dynamic buffer and it adjusted its capacity.
         *
         * @throws UnsupportedOperationException
         *         if this buffer cannot create a {@link ByteBuffer} that shares the content with itself
         *
         * @see #nioBufferCount()
         * @see #nioBuffers()
         * @see #nioBuffers(int, int)
         */
    /// <summary>Returns a borrowed capacity-bounded read-only range without changing indices.</summary>
    /// <remarks>This is a live view, not a snapshot. Exclude concurrent resize/release while borrowing it.</remarks>
    /// <exception cref="NotSupportedException">The range spans multiple components;
    /// use AsReadOnlySequence or GetBytes for such ranges.</exception>
    public ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    {
        CheckIndex(index, length);
        return GetReadOnlyMemoryCore(index, length);
    }

    public ReadOnlySpan<byte> AsReadOnlySpan(int index, int length) => AsReadOnlyMemory(index, length).Span;

    protected void EnsureCanWrite()
    {
        EnsureAccessible();
        if (IsReadOnly) throw new NotSupportedException("The buffer is read-only.");
    }
}
