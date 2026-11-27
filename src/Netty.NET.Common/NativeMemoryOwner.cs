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
using System.Runtime.InteropServices;
using System.Threading;

namespace Netty.NET.Common;

/// <summary>Owns managed storage and exposes bounded, non-owning Memory slices.</summary>
/// <remarks>Disposal invalidates access through the owner. Previously acquired spans and
/// runtime pins reference the same managed array, whose lifetime is controlled by the GC.</remarks>
public sealed class NativeMemoryOwner : IMemoryOwner<byte>
{
    internal readonly NativeMemoryAllocator Allocator;
    internal readonly object Sync = new();
    private readonly NativeMemoryManager _manager;
    private byte[] _storage;
    private GCHandle _stablePin;
    private bool _hasReservation;
    private bool _disposed;
    private int _leases;

    internal NativeMemoryOwner(NativeMemoryAllocator allocator, int length, bool clear)
    {
        Allocator = allocator;
        Length = length;
        int storageLength = Math.Max(1, length);
        _storage = clear ? new byte[storageLength] : GC.AllocateUninitializedArray<byte>(storageLength);
        _manager = new NativeMemoryManager(this);
    }

    public int Length { get; }
    public Memory<byte> Memory => _manager.Memory;

    /// <summary>Finds the owner that created a memory range, including slices.</summary>
    public static bool TryGetOwner(ReadOnlyMemory<byte> memory, out NativeMemoryOwner owner)
    {
        if (MemoryMarshal.TryGetMemoryManager<byte, NativeMemoryManager>(memory, out var manager))
        {
            owner = manager.Owner;
            return true;
        }
        owner = null;
        return false;
    }

    internal static bool TryGetAddress(ReadOnlyMemory<byte> memory, out nint address)
    {
        if (MemoryMarshal.TryGetMemoryManager<byte, NativeMemoryManager>(memory, out var manager, out int start, out _))
        {
            lock (manager.Owner.Sync)
            {
                address = manager.Owner.GetAddress() + start;
                return true;
            }
        }
        address = 0;
        return false;
    }

    internal IDisposable AcquireStorageLease()
    {
        lock (Sync)
        {
            ThrowIfDisposed();
            var lease = new StorageLease(this);
            _leases++;
            return lease;
        }
    }

    /// <summary>Returns the largest slice whose start and end are aligned.</summary>
    /// <remarks>Alignment must be a power of two. When alignment requires a stable address,
    /// the array is pinned until the owner and any outstanding Netty.NET storage leases are released.</remarks>
    public Memory<byte> GetAlignedMemory(int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (alignment == 1) return Memory;
        lock (Sync)
        {
            nuint mask = (nuint)alignment - 1;
            nuint padding = unchecked(0 - (nuint)GetAddress()) & mask;
            if (padding > (nuint)Length) return Memory.Slice(0, 0);
            int start = (int)padding;
            int count = (Length - start) & ~(alignment - 1);
            return Memory.Slice(start, count);
        }
    }

    private nint GetAddress()
    {
        ThrowIfDisposed();
        // Called under Sync. Only aligned slices and address diagnostics need a stable address.
        if (!_stablePin.IsAllocated)
            _stablePin = GCHandle.Alloc(_storage, GCHandleType.Pinned);
        return _stablePin.AddrOfPinnedObject();
    }

    public void Dispose()
    {
        lock (Sync)
        {
            _disposed = true;
            ReleaseStorageIfUnused();
        }
        GC.SuppressFinalize(this);
    }

    ~NativeMemoryOwner()
    {
        Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal Span<byte> GetSpan()
    {
        ThrowIfDisposed();
        return _storage.AsSpan(0, Length);
    }

    internal void TakeReservation()
    {
        _hasReservation = true;
    }

    internal void EnsureReallocatable()
    {
        ThrowIfDisposed();
        if (_leases != 0) throw new InvalidOperationException("Release storage leases before reallocating.");
    }

    // Called under Sync. The replacement takes over the active reservation.
    internal void TransferReservation()
    {
        _hasReservation = false;
        Dispose();
    }

    private void ReleaseStorageIfUnused()
    {
        if (!_disposed || _leases != 0) return;
        if (_stablePin.IsAllocated) _stablePin.Free();
        _storage = null;
        if (!_hasReservation) return;
        _hasReservation = false;
        Allocator.Release(Math.Max(1, Length));
    }

    private sealed class StorageLease(NativeMemoryOwner owner) : IDisposable
    {
        private NativeMemoryOwner _owner = owner;

        public void Dispose()
        {
            NativeMemoryOwner current = Interlocked.Exchange(ref _owner, null);
            if (current == null) return;
            lock (current.Sync)
            {
                current._leases--;
                current.ReleaseStorageIfUnused();
            }
            GC.SuppressFinalize(this);
        }

        ~StorageLease()
        {
            Dispose();
        }
    }

    private sealed class NativeMemoryManager(NativeMemoryOwner owner) : MemoryManager<byte>
    {
        internal NativeMemoryOwner Owner => owner;

        public override Memory<byte> Memory
        {
            get
            {
                lock (owner.Sync)
                {
                    owner.ThrowIfDisposed();
                    return CreateMemory(owner.Length);
                }
            }
        }

        public override Span<byte> GetSpan()
        {
            lock (owner.Sync) return owner.GetSpan();
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            lock (owner.Sync)
            {
                owner.ThrowIfDisposed();
                ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(elementIndex, owner.Length);
                return owner._storage.AsMemory(elementIndex, owner.Length - elementIndex).Pin();
            }
        }

        public override void Unpin()
        {
            throw new NotSupportedException("Dispose the returned MemoryHandle.");
        }
        protected override void Dispose(bool disposing)
        {
            owner.Dispose();
        }
    }
}
