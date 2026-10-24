/*
 * Copyright 2022 The Netty Project
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

// CLR: the shared Memory/Span kernel provides primitive, bulk, text, sequence and
// .NET stream/file access. The allocation callback binds the owning pool's magazine
// selection and fallback while preserving buffer identity during reallocation.
// Each wrapper is fresh; recycling a released CLR object could resurrect stale references.
internal sealed partial class AdaptiveByteBuf : AbstractReferenceCountedByteBuf
{
    // We need the top-level allocator so ByteBuf.capacity(int) can call reallocate()
    // CLR: the pool reservation callback replaces the chunk's allocator back-reference.
    private Func<int, int, AdaptiveChunkAllocation> _allocate;
    private Func<int, int, AdaptiveChunkAllocation, AdaptiveChunkAllocation> _reallocate;
    // this both act as adjustment and the start index for a free list segment allocation
    // CLR: the allocation owns this offset as well as operation/pin claims.
    private AdaptiveChunkAllocation _allocation;
    private int _length;
    private int _maxFastCapacity;
    internal AdaptiveChunkAllocation Allocation => _allocation;

    internal AdaptiveByteBuf(int initialCapacity, int maxCapacity, Func<int, int, AdaptiveChunkAllocation> allocate,
        Func<int, int, AdaptiveChunkAllocation, AdaptiveChunkAllocation> reallocate = null)
        : base(maxCapacity)
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        ArgumentNullException.ThrowIfNull(allocate);
        if (initialCapacity < 0 || initialCapacity > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _allocate = allocate;
        _reallocate = reallocate;
        _allocation = Allocate(initialCapacity);
        _maxFastCapacity = _allocation.Length;
        _length = initialCapacity;
        BufferAllocatorEventSource.Log.Allocated(CaptureEvent(BufferAllocatorEventSource.Keywords.AllocateBuffer));
    }

    private AdaptiveChunkAllocation Allocate(int capacity)
    {
        AdaptiveChunkAllocation allocation = (_allocation != null && _reallocate != null
            ? _reallocate(capacity, MaxCapacity, _allocation) : _allocate(capacity, MaxCapacity))
            ?? throw new InvalidOperationException("The adaptive storage binding did not supply an allocation.");
        if (allocation.Length >= capacity) return allocation;
        allocation.Release();
        throw new InvalidOperationException("The adaptive reservation is smaller than the requested capacity.");
    }

    public override IByteBufAllocator Allocator
    {
        get { EnsureAccessible(); return _allocation.Allocator; }
    }
    public override bool IsDirect
    {
        get { EnsureAccessible(); return _allocation.IsDirect; }
    }
    public override bool IsContiguous { get { EnsureAccessible(); return _allocation.Root.IsContiguous; } }
    public override int MaxFastWritableBytes => Math.Min(_maxFastCapacity, MaxCapacity) - WriterIndex;

    // setBytes(...) will take care of checking the indices.
    // CLR: text/bulk operations inherit those bounds from ByteBuf, then use this bounded region.
    protected override Memory<byte> GetMemoryCore(int index, int length) => _allocation.Memory.Slice(index, length);
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
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            CheckNewCapacity(value);
            if (_length <= value && value <= _allocation.Length)
            {
                _length = value;
                return;
            }
            if (value < _length)
            {
                _length = value;
                TrimIndicesToCapacity(value);
                return;
            }

            // Reallocation required.
            BufferAllocatorEventSource.Log.Reallocated(CaptureEvent(BufferAllocatorEventSource.Keywords.ReallocateBuffer), value);
            AdaptiveChunkAllocation previous = _allocation;
            AdaptiveChunkAllocation replacement = Allocate(value);
            try
            {
                // CLR: copy before publishing, so allocation/copy failure preserves the
                // old region, logical capacity, data, indices and marks. Span copying
                // needs no temporary NIO cursor. Both regions remain owned throughout.
                previous.Memory.Span[.._length].CopyTo(replacement.Memory.Span);
            }
            catch
            {
                replacement.Release();
                throw;
            }
            _allocation = replacement;
            _maxFastCapacity = replacement.Length;
            _length = value;
            BufferAllocatorEventSource.Log.Allocated(CaptureEvent(BufferAllocatorEventSource.Keywords.AllocateBuffer));
            previous.Release();
        }
    }

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        // Preserve the raw chunk delegate's copy policy, including its maximum
        // capacity and storage kind, rather than the outer buffer's maximum.
        return _allocation.Copy(index, length);
    }

    private BufferAllocatorEventSource.Snapshot? CaptureEvent(System.Diagnostics.Tracing.EventKeywords keyword)
        => BufferAllocatorEventSource.Log.CaptureAdaptive(this, _allocation, keyword);

    protected override void Deallocate()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        BufferAllocatorEventSource.Log.Freed(CaptureEvent(BufferAllocatorEventSource.Keywords.FreeBuffer));
        AdaptiveChunkAllocation allocation = _allocation;
        _allocation = null;
        _allocate = null;
        _reallocate = null;
        allocation.Release();
    }
}
