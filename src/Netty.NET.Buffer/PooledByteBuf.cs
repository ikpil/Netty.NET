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

// CLR: the shared ByteBuf Memory/Span kernel provides heap/native primitive, bulk and
// stream access. A wrapper is never recycled: a stale released C# reference must
// not become a reference to a later live buffer. Only storage regions are pooled.
internal abstract class PooledByteBuf : AbstractReferenceCountedByteBuf
{
    private PoolArena _arena;
    private readonly object _resizeLock = new();
    private PoolArena.Allocation _allocation;
    private int _length;
    internal PoolArena.Allocation Allocation => _allocation;

    protected PooledByteBuf(PoolArena arena, int initialCapacity, int maxCapacity, PoolThreadCache cache = null) : base(maxCapacity)
    {
        ArgumentNullException.ThrowIfNull(arena);
        if (initialCapacity < 0 || initialCapacity > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _allocation = arena.AllocateMemory(initialCapacity, cache);
        _arena = _allocation.Arena;
        _length = initialCapacity;
    }

    public override IByteBufAllocator Allocator => _arena.Parent;
    public override bool IsContiguous => true;
    public override int MaxFastWritableBytes
        => Math.Min(_allocation?.ReservedLength ?? _length, MaxCapacity) - WriterIndex;

    protected override Memory<byte> GetMemoryCore(int index, int length)
        => _allocation.Memory.Slice(_allocation.Offset + index, length);

    internal override BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();
        return _allocation.AcquireLease();
    }

    public override int Capacity
    {
        get => _length;
        set
        {
            // We synchronize on the ByteBuf itself to ensure there is no "concurrent" reallocations for the same buffer.
            // We do this to ensure the ByteBuf internal fields that are used to allocate / free are not accessed
            // concurrently. This is important as otherwise we might end up corrupting our internal state of our data
            // structures.
            //
            // Also note we don't use a Lock here but just synchronized even tho this might seem like a bad choice for Loom.
            // This is done to minimize the overhead per ByteBuf. The time this would block another thread should be
            // relative small and so not be a problem for Loom.
            // See https://github.com/netty/netty/issues/13467
            // CLR: use a private Monitor for all capacity mutations. Data access,
            // active cursors and final Release still require caller synchronization.
            lock (_resizeLock)
            {
                CheckNewCapacity(value);
                if (value == _length) return;
                PoolArena.Allocation previous = _allocation;
                int maxLength = previous.ReservedLength;
                if (previous.IsPooled)
                {
                    // If the request capacity does not require reallocation, just update the length of the memory.
                    if (value > _length && value <= maxLength)
                    {
                        _length = value;
                        return;
                    }
                    if (value < _length && value > maxLength >> 1 &&
                        (maxLength > 512 || value > maxLength - 16))
                    {
                        // here newCapacity < length
                        _length = value;
                        TrimIndicesToCapacity(value);
                        return;
                    }
                }

                // Reallocation required.
                PoolThreadCache cache = (_arena.Parent as PooledByteBufAllocator)?.PrepareThreadBinding();
                PoolArena.Allocation replacement = _arena.AllocateMemory(value, cache);
                try
                {
                    // This does not touch buf's reader/writer indices
                    // We must duplicate the NIO buffers because they may be accessed by other Netty buffers.
                    // CLR: bounded Span copying needs no mutable NIO cursor or duplicate object.
                    previous.Memory.Span.Slice(previous.Offset, Math.Min(_length, value))
                        .CopyTo(replacement.Memory.Span.Slice(replacement.Offset));
                }
                catch
                {
                    replacement.Dispose();
                    throw;
                }
                _allocation = replacement;
                // A caller's cache may supply a handle belonging to another arena.
                _arena = replacement.Arena;
                _length = value;
                TrimIndicesToCapacity(value);
                previous.Dispose();
            }
        }
    }

    protected override void Deallocate()
    {
        PoolArena.Allocation allocation = _allocation;
        _allocation = null;
        allocation.Dispose();
    }
}
