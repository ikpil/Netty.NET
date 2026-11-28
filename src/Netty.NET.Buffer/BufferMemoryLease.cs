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
using System.Buffers;
using System.Runtime.InteropServices;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

// CLR: preserve every native allocation and pooled reservation while a growing composite write may
// consolidate/release components whose memory aliases the caller's span.
// A root lease is a value type; only a composite allocates a group of leases.
internal readonly struct BufferMemoryLease : IDisposable
{
    private readonly MemoryHandle _handle;
    private readonly BufferMemoryLease[] _children;
    private readonly IDisposable _reservation;

    internal BufferMemoryLease(MemoryHandle handle)
    {
        _handle = handle;
    }

    internal BufferMemoryLease(BufferMemoryLease[] children)
    {
        _children = children;
    }

    // CLR: pooled heap reservations must survive reuse as well as GC collection.
    internal BufferMemoryLease(IDisposable reservation)
    {
        _reservation = reservation;
    }

    internal static BufferMemoryLease ForBorrowedMemory(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetMemoryManager<byte, AdaptivePoolingAllocator.AdaptiveChunkAllocation>(memory, out var adaptive))
            return adaptive.AcquireLease();
        if (MemoryMarshal.TryGetMemoryManager<byte, ThreadLocalDirectAllocation>(memory, out var threadLocal))
            return threadLocal.AcquireLease();
        if (NativeMemoryOwner.TryGetOwner(memory, out var owner))
            return new BufferMemoryLease(owner.AcquireStorageLease());

        return new BufferMemoryLease(memory.Pin());
    }

    // The caller keeps the returned lease until the external consumer has disposed its pin.
    internal static BufferMemoryLease ForPinnableMemory(ReadOnlyMemory<byte> memory, out ReadOnlyMemory<byte> storage)
    {
        if (MemoryMarshal.TryGetMemoryManager<byte, AdaptivePoolingAllocator.AdaptiveChunkAllocation>(
                memory, out var adaptive, out int start, out int length))
        {
            var lease = adaptive.AcquireLease();
            storage = adaptive.GetStorage(start, length);
            return lease;
        }

        if (MemoryMarshal.TryGetMemoryManager<byte, ThreadLocalDirectAllocation>(
                memory, out var threadLocal, out start, out length))
        {
            var lease = threadLocal.AcquireLease();
            storage = threadLocal.GetStorage(start, length);
            return lease;
        }

        storage = memory;
        return default;
    }

    public void Dispose()
    {
        if (_children != null)
            for (int i = _children.Length - 1; i >= 0; --i)
            {
                _children[i].Dispose();
            }

        _handle.Dispose();
        _reservation?.Dispose();
    }
}
