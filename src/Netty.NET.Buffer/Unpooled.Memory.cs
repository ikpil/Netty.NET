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
using System.Runtime.InteropServices;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public static partial class Unpooled
{
    /**
     * Creates a new buffer which wraps the specified NIO buffer's current
     * slice.  A modification on the specified buffer's content will be
     * visible to the returned buffer.
     */
    /// <summary>Wraps a writable memory range without copying or acquiring its owner's lifetime.</summary>
    /// <remarks>Keep the external storage valid until buffer access ends. Array ranges
    /// reuse array wrapping. NativeMemoryOwner ranges may detach on capacity change;
    /// other managers have fixed capacity and heap copy policy, since Memory cannot
    /// identify their storage kind. Use explicit LE operations for little-endian data.</remarks>
    public static ByteBuf WrappedBuffer(Memory<byte> memory)
    {
        if (memory.IsEmpty) return EmptyBuffer;
        if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out ArraySegment<byte> array))
            return WrappedBuffer(array.Array, array.Offset, array.Count);
        if (NativeMemoryOwner.TryGetOwner(memory, out _))
            return new UnpooledDirectByteBuf(UnpooledByteBufAllocator.Default, memory, memory.Length);
        return new WrappedMemoryByteBuf(memory);
    }

    // CLR: non-array managers have no Java provider equivalent or reliable native-kind
    // metadata. A fixed borrowed range preserves writable Memory access without guessing.
    private sealed class WrappedMemoryByteBuf : AbstractReferenceCountedByteBuf
    {
        private Memory<byte> _memory;
        internal WrappedMemoryByteBuf(Memory<byte> memory) : base(memory.Length)
        {
            _memory = memory;
            SetIndex(0, memory.Length);
        }
        public override IByteBufAllocator Allocator => UnpooledByteBufAllocator.Default;
        public override bool IsContiguous => true;
        public override bool IsDirect => false;
        public override int Capacity
        {
            get => MaxCapacity;
            set { CheckNewCapacity(value); throw new NotSupportedException("A borrowed manager range has fixed capacity."); }
        }
        protected override Memory<byte> GetMemoryCore(int index, int length) => _memory.Slice(index, length);
        internal override BufferMemoryLease AcquireReadLease()
        {
            EnsureAccessible();
            return new BufferMemoryLease(_memory.Pin());
        }
        protected override void Deallocate() => _memory = default;
    }
}
