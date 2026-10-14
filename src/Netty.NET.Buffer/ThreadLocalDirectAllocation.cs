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

// CLR: a fresh memory facade invalidates old borrows; pins/operation claims delay
// native-storage reuse until their final return. Copied handles return exactly once.
internal sealed unsafe class ThreadLocalDirectAllocation : MemoryManager<byte>
{
    private NativeMemoryOwner _owner;
    private ThreadLocalDirectBufferCache _cache;
    private Memory<byte> _memory;
    private int _references = 1;
    private int _disposed;
    internal int Length { get; }

    internal ThreadLocalDirectAllocation(NativeMemoryOwner owner, ThreadLocalDirectBufferCache cache)
    {
        _owner = owner;
        _cache = cache;
        _memory = owner.Memory;
        Length = owner.Length;
    }

    internal void DisableCaching() => _cache = null;

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
        throw new ObjectDisposedException(nameof(ThreadLocalDirectAllocation));
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
        if (!ReferenceCountUpdater.Release(ref _references)) return;
        NativeMemoryOwner owner = _owner;
        ThreadLocalDirectBufferCache cache = _cache;
        _owner = null;
        _cache = null;
        _memory = default;
        if (cache == null) owner.Dispose();
        else cache.Return(owner);
    }

    private sealed class Claim : IDisposable, IPinnable
    {
        private ThreadLocalDirectAllocation _allocation;
        private MemoryHandle _handle;
        internal void* Pointer => _handle.Pointer;
        internal void Activate(ThreadLocalDirectAllocation allocation) => _allocation = allocation;
        internal void Pin(Memory<byte> memory) => _handle = memory.Pin();
        public MemoryHandle Pin(int elementIndex) => throw new NotSupportedException("A pin claim is single-use.");
        public void Unpin() => Dispose();
        public void Dispose()
        {
            ThreadLocalDirectAllocation allocation = Interlocked.Exchange(ref _allocation, null);
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
