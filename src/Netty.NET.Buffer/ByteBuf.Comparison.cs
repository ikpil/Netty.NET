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

public abstract partial class ByteBuf : IEquatable<ByteBuf>, IComparable<ByteBuf>
{
/**
     * Returns a hash code which was calculated from the content of this
     * buffer.  If there's a byte array which is
     * {@linkplain #equals(Object) equal to} this array, both arrays should
     * return the same value.
     */
    /// <remarks>Depends on the current readable bytes. Do not change content, indices or
    /// lifetime while the buffer is a key in a hash-based collection.</remarks>
    public override int GetHashCode() => ByteBufUtil.HashCode(this);

/**
     * Determines if the content of the specified buffer is identical to the
     * content of this array.  'Identical' here means:
     * <ul>
     * <li>the size of the contents of the two buffers are same and</li>
     * <li>every single byte of the content of the two buffers are same.</li>
     * </ul>
     * Please note that it does not compare {@link #readerIndex()} nor
     * {@link #writerIndex()}.  This method also returns {@code false} for
     * {@code null} and an object which is not an instance of
     * {@link ByteBuf} type.
     */
    public override bool Equals(object obj) => obj is ByteBuf other && Equals(other);

    /// <summary>Compares the current readable content, regardless of storage or absolute indices.</summary>
    public bool Equals(ByteBuf other) => other is not null && ByteBufUtil.Equals(this, other);

/**
     * Compares the content of the specified buffer to the content of this
     * buffer. Comparison is performed in the same manner with the string
     * comparison functions of various languages such as {@code strcmp},
     * {@code memcmp} and {@link String#compareTo(String)}.
     */
    /// <remarks>As required by IComparable, a non-null buffer sorts after null.
    /// Comparison otherwise follows ByteBufUtil.Compare over the readable bytes.</remarks>
    public int CompareTo(ByteBuf other) => other is null ? 1 : ByteBufUtil.Compare(this, other);
}
