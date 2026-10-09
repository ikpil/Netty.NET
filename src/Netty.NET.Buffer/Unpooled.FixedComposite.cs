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

public static partial class Unpooled
{
    /**
     * Wrap the given {@link ByteBuf}s in an unmodifiable {@link ByteBuf}. Be aware the returned {@link ByteBuf} will
     * not try to slice the given {@link ByteBuf}s to reduce GC-Pressure.
     *
     * The returned {@link ByteBuf} may wrap the provided array directly, and so should not be subsequently modified.
     */
    /// <summary>Wraps buffers without copying their content or acquiring references.</summary>
    /// <remarks>Multiple inputs transfer one existing reference each. Their captured readable lengths
    /// map from absolute index zero, as in Netty; pass Slice() for each input to wrap its readable range.
    /// Keep component storage and layout stable during use. This CLR implementation snapshots the input
    /// array and layout. A single input returns AsReadOnly(), preserving its indices and shared ownership.</remarks>
    public static ByteBuf WrappedUnmodifiableBuffer(params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return buffers.Length switch
        {
            0 => EmptyBuffer,
            1 => (buffers[0] ?? throw new ArgumentException("A component is null.", nameof(buffers))).AsReadOnly(),
            _ => new FixedCompositeByteBuf(buffers)
        };
    }
}
