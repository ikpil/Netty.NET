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
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: a region has one buffer claim and temporary operation/pin claims. Returning
// the buffer invalidates its borrowed Memory, but a lease delays slot reuse. Wrappers
// and pin tokens are never recycled; old references cannot attach to a new allocation.
internal sealed unsafe class AdaptiveChunkAllocation : MemoryManager<byte>
{
    private AdaptiveChunk _chunk;
    private Memory<byte> _memory;
    private int _references = 1;
    private int _disposed;
    internal int Offset { get; }
    internal int Length { get; }
    internal IByteBufAllocator Allocator => _chunk.Allocator;
    internal bool IsDirect => _chunk.IsDirect;
    internal bool IsPooled => _chunk.IsPooled;
    internal bool IsThreadLocal { get; }
    internal bool CurrentThreadLocalAssociation => _chunk.IsThreadLocal;

    internal ByteBuf Copy(int index, int length)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (index < 0 || length < 0 || index > Length - length) throw new ArgumentOutOfRangeException(nameof(index));
        return _chunk.Copy(Offset + index, length);
    }

    internal AdaptiveChunkAllocation(AdaptiveChunk chunk, Memory<byte> memory, int offset)
    {
        _chunk = chunk;
        // Capture before a successful last-slot allocation detaches its magazine.
        IsThreadLocal = chunk.IsThreadLocal;
        _memory = memory;
        Offset = offset;
        Length = memory.Length;
    }

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _memory.Span;
    }

    protected override bool TryGetArray(out ArraySegment<byte> segment)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)_memory, out segment);
    }

    internal BufferMemoryLease AcquireLease() => new(AcquireClaim());

    private Claim AcquireClaim()
    {
        // Allocate first so failure cannot strand a reference count increment.
        var claim = new Claim();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ReferenceCountUpdater.Retain(ref _references);
        claim.Activate(this);
        if (Volatile.Read(ref _disposed) == 0) return claim;
        claim.Dispose();
        throw new ObjectDisposedException(nameof(AdaptiveChunkAllocation));
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        if (elementIndex < 0 || elementIndex > Length) throw new ArgumentOutOfRangeException(nameof(elementIndex));
        Claim claim = AcquireClaim();
        try
        {
            // The claim owns the underlying handle too, so copied MemoryHandle values
            // unpin and return the segment exactly once. Native and heap use the same path.
            claim.Pin(_memory.Slice(elementIndex));
            return new MemoryHandle(claim.Pointer, pinnable: claim);
        }
        catch
        {
            claim.Dispose();
            throw;
        }
    }

    public override void Unpin() => throw new NotSupportedException("Dispose the returned MemoryHandle.");
    internal void Release() => Dispose(true);
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) ReleaseClaim();
    }

    private void ReleaseClaim()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        if (!ReferenceCountUpdater.Release(ref _references)) return;
        AdaptiveChunk chunk = _chunk;
        _chunk = null;
        _memory = default;
        chunk.ReleaseSegment(Offset, Length);
    }

    private sealed class Claim : IDisposable, IPinnable
    {
        private AdaptiveChunkAllocation _allocation;
        private MemoryHandle _handle;
        internal void* Pointer => _handle.Pointer;
        internal void Activate(AdaptiveChunkAllocation allocation) => _allocation = allocation;
        internal void Pin(Memory<byte> memory) => _handle = memory.Pin();
        public MemoryHandle Pin(int elementIndex) => throw new NotSupportedException("A pin claim is single-use.");
        public void Unpin() => Dispose();
        public void Dispose()
        {
            AdaptiveChunkAllocation allocation = Interlocked.Exchange(ref _allocation, null);
            if (allocation != null)
            {
                // Release the physical pin before making this segment reusable.
                try { _handle.Dispose(); }
                finally { allocation.ReleaseClaim(); }
            }
            GC.SuppressFinalize(this);
        }
        ~Claim() => Dispose();
    }
}
