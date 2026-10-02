/*
 * Copyright 2025 The Netty Project
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
using System.Threading;

namespace Netty.NET.Common;

/// <summary>Owns a native allocation and exposes bounded, non-owning Memory slices.</summary>
/// <remarks>Dispose invalidates new memory access, while outstanding pins keep the
/// allocation alive until released. A previously obtained Span cannot be revoked:
/// keep this owner alive and do not dispose or reallocate while using unpinned spans.
/// This is single ownership; buffer retain/release and pool leases remain separate.</remarks>
public sealed unsafe class NativeMemoryOwner : IMemoryOwner<byte>
{
    internal readonly NativeMemoryAllocator Allocator;
    internal readonly NativeMemoryAllocator.NativeAllocationHandle Handle;
    internal readonly object Sync = new();
    private readonly NativeMemoryManager _manager;
    private bool _disposed;
    private int _pinCount;

    internal NativeMemoryOwner(NativeMemoryAllocator allocator,
        NativeMemoryAllocator.NativeAllocationHandle handle, int length)
    {
        Allocator = allocator;
        Handle = handle;
        Length = length;
        _manager = new NativeMemoryManager(this);
    }

    public int Length { get; }
    public Memory<byte> Memory => _manager.Memory;
    internal int PinCount => Volatile.Read(ref _pinCount);

    /// <summary>Returns the largest slice whose start and end are aligned.</summary>
    /// <remarks>The source memory is unchanged and retains ownership. If no aligned
    /// interval fits, the returned slice is empty. Alignment must be a power of two.</remarks>
    public Memory<byte> GetAlignedMemory(int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        lock (Sync)
        {
            ThrowIfDisposed();
            nuint mask = (nuint)alignment - 1;
            nuint address = (nuint)Handle.DangerousGetHandle();
            nuint padding = unchecked(0 - address) & mask;
            if (padding > (nuint)Length) return Memory.Slice(0, 0);
            int start = (int)padding;
            int count = (Length - start) & ~(alignment - 1);
            return Memory.Slice(start, count);
        }
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (_disposed) return;
            _disposed = true;
            Handle.Dispose();
        }
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    // Called only after successful native reallocation under Sync. The replacement
    // owns the allocation, reservation and GC pressure; the old handle must not free it.
    internal void TransferAllocation()
    {
        _disposed = true;
        Handle.SetHandleAsInvalid();
    }

    private void ReleasePin()
    {
        // Release the SafeHandle reference before allowing a reallocation to see
        // no pins. This path also works on the pin lease's finalizer thread.
        try { Handle.DangerousRelease(); }
        finally { Interlocked.Decrement(ref _pinCount); }
    }

    private sealed class NativeMemoryManager : MemoryManager<byte>
    {
        private readonly NativeMemoryOwner _owner;
        internal NativeMemoryManager(NativeMemoryOwner owner) => _owner = owner;

        public override Memory<byte> Memory
        {
            get
            {
                lock (_owner.Sync)
                {
                    _owner.ThrowIfDisposed();
                    return CreateMemory(_owner.Length);
                }
            }
        }

        public override Span<byte> GetSpan()
        {
            lock (_owner.Sync)
            {
                _owner.ThrowIfDisposed();
                return new Span<byte>((void*)_owner.Handle.DangerousGetHandle(), _owner.Length);
            }
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            lock (_owner.Sync)
            {
                _owner.ThrowIfDisposed();
                if ((uint)elementIndex > (uint)_owner.Length)
                    throw new ArgumentOutOfRangeException(nameof(elementIndex));
                var lease = new PinLease();
                bool acquired = false;
                try
                {
                    _owner.Handle.DangerousAddRef(ref acquired);
                    Interlocked.Increment(ref _owner._pinCount);
                    lease.Activate(_owner);
                    return new MemoryHandle((byte*)_owner.Handle.DangerousGetHandle() + elementIndex,
                        pinnable: lease);
                }
                catch
                {
                    if (!lease.Release() && acquired) _owner.Handle.DangerousRelease();
                    throw;
                }
            }
        }

        // Each returned handle owns a separate idempotent lease, including copied
        // MemoryHandle structs. There is no shared, unidentifiable Unpin authority.
        public override void Unpin() => throw new NotSupportedException("Dispose the returned MemoryHandle.");
        protected override void Dispose(bool disposing) => _owner.Dispose();
    }

    private sealed class PinLease : IPinnable
    {
        private NativeMemoryOwner _owner;
        internal void Activate(NativeMemoryOwner owner) => _owner = owner;
        public MemoryHandle Pin(int elementIndex) => throw new NotSupportedException("A pin lease is single-use.");
        public void Unpin() => Release();
        internal bool Release()
        {
            NativeMemoryOwner owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleasePin();
            GC.SuppressFinalize(this);
            return owner != null;
        }
        ~PinLease() => Unpin();
    }
}
