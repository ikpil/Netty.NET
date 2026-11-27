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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    // CLR: a region has one buffer claim and temporary operation leases. Returning
    // the buffer invalidates its borrowed Memory, but a lease delays slot reuse. Lease tokens
    // are never recycled; old references cannot attach to a new allocation.
    // CLR: this reservation is what Chunk.TryAllocate hands to AdaptiveByteBuf where the source's
    // readInitInto initializes the buffer's chunk, rootParent and startIndex; it has no source type.
    internal sealed class AdaptiveChunkAllocation : MemoryManager<byte>
    {
        private Chunk _chunk;
        private Memory<byte> _memory;
        private int _references = 1;
        private int _disposed;
        internal int Offset { get; }
        internal int Length { get; }
        internal ByteBuf Root
        {
            get { ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this); return _chunk.Root; }
        }
        internal IByteBufAllocator Allocator => _chunk.Allocator;
        internal bool IsDirect => _chunk.IsDirect;
        internal bool IsPooled => _chunk.Pooled;
        internal bool IsThreadLocal { get; }
        internal bool CurrentThreadLocalAssociation => _chunk.IsThreadLocal;

        internal ByteBuf Copy(int index, int length)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (index < 0 || length < 0 || index > Length - length) throw new ArgumentOutOfRangeException(nameof(index));
            return _chunk.Copy(Offset + index, length);
        }

        internal AdaptiveChunkAllocation(Chunk chunk, Memory<byte> memory, int offset)
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

        internal bool TryGetAddress(int offset, out nint address) =>
            NativeMemoryOwner.TryGetAddress(_memory.Slice(offset), out address);

        internal ReadOnlyMemory<byte> GetStorage(int offset, int length) => _memory.Slice(offset, length);

        public override MemoryHandle Pin(int elementIndex = 0) =>
            throw new NotSupportedException("Keep the buffer retained while using this reusable memory.");

        public override void Unpin() => throw new NotSupportedException();
        internal void Release() => Dispose(true);
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) ReleaseClaim();
        }

        private void ReleaseClaim()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (!ReferenceCountUpdater.Release(ref _references)) return;
            Chunk chunk = _chunk;
            _chunk = null;
            _memory = default;
            chunk.ReleaseSegment(Offset, Length);
        }

        private sealed class Claim : IDisposable
        {
            private AdaptiveChunkAllocation _allocation;
            internal void Activate(AdaptiveChunkAllocation allocation) => _allocation = allocation;
            public void Dispose()
            {
                AdaptiveChunkAllocation allocation = Interlocked.Exchange(ref _allocation, null);
                allocation?.ReleaseClaim();
                GC.SuppressFinalize(this);
            }
            ~Claim() => Dispose();
        }
    }
}
