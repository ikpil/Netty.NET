/*
 * Copyright 2024 The Netty Project
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
using System.Collections.Generic;
using Netty.NET.Common;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

// Test-only storage binding for AdaptiveByteBuf. It exercises the real chunks and the real
// SizeClass/Buddy chunk controllers of an AdaptivePoolingAllocator per storage kind; it does not
// implement or validate magazine/cache/stripe/purge behavior. This fixture never auto-wraps leaks.
internal sealed class AdaptiveBufferTestAllocator : AbstractByteBufAllocator, IDisposable
{
    private readonly object _sync = new();
    private readonly bool _lowMemory;
    // The pools supply controllers and the pooled-chunk registry; their magazines are not used.
    private readonly AdaptivePoolingAllocator[] _pools;
    private readonly Magazine[][] _magazines;
    // Dedicated chunks are created here, so they count in a separate registry.
    private readonly ChunkRegistry[] _registries = [new(), new()];
    private readonly List<(Chunk Chunk, bool Direct, int Class)> _chunks = new();
    private readonly bool _threadLocal;
    private bool _disposed;
    internal NativeMemoryAllocator Native { get; }
    internal long UsedHeapMemory => _pools[0].UsedMemory + _registries[0].TotalCapacity;
    internal long UsedDirectMemory => _pools[1].UsedMemory + _registries[1].TotalCapacity;
    internal Func<bool, int, int, ByteBuf> StorageFactory;
    internal Action<int, AdaptiveChunkAllocation> AfterReservation;

    internal AdaptiveBufferTestAllocator(bool lowMemory = false, bool threadLocal = false,
        NativeMemoryAllocator native = null)
    {
        _lowMemory = lowMemory;
        _threadLocal = threadLocal;
        Native = native ?? new NativeMemoryAllocator();
        _pools = [Pool(false), Pool(true)];
        _magazines = [new Magazine[GetSizeClasses().Length + 1], new Magazine[GetSizeClasses().Length + 1]];
    }

    private AdaptivePoolingAllocator Pool(bool direct)
    {
        return new(new TestChunkAllocator((size, max) => Raw(direct, size, max)), _lowMemory, 2, 128, 3, 1);
    }

    private ByteBuf Raw(bool direct, int capacity, int maxCapacity)
    {
        return StorageFactory != null ? StorageFactory(direct, capacity, maxCapacity)
            : direct ? new UnpooledDirectByteBuf(this, capacity, maxCapacity, Native)
            : new UnpooledHeapByteBuf(this, capacity, maxCapacity);
    }

    // Mirrors AdaptivePoolingAllocator.Allocate: fixed classes, then buddy unless low-memory.
    internal bool IsPooled(int size)
    {
        return size <= _pools[0].MaxPooledBufSize && (SizeClassIndexOf(size) < GetSizeClasses().Length || !_lowMemory);
    }

    internal int ComputeBufferCapacity(bool direct, int size, int maxCapacity)
    {
        return IsPooled(size) ? Controller(direct, SizeClassIndexOf(size)).ComputeBufferCapacity(size, maxCapacity, false) : size;
    }

    private IChunkController Controller(bool direct, int index)
    {
        AdaptivePoolingAllocator pool = _pools[direct ? 1 : 0];
        MagazineGroup group = index < pool.SizeClassedMagazineGroups.Length
            ? pool.SizeClassedMagazineGroups[index] : pool.LargeBufferMagazineGroup;
        return group.Magazines[0].ChunkController;
    }

    // A non-shareable magazine makes the controller create physical-owner (thread-local) chunks.
    private Magazine MagazineFor(bool direct, int index)
    {
        AdaptivePoolingAllocator pool = _pools[direct ? 1 : 0];
        if (!_threadLocal || index >= pool.SizeClassedMagazineGroups.Length)
            return (index < pool.SizeClassedMagazineGroups.Length
                ? pool.SizeClassedMagazineGroups[index] : pool.LargeBufferMagazineGroup).Magazines[0];
        return _magazines[direct ? 1 : 0][index] ??= new Magazine(
            pool.SizeClassedMagazineGroups[index], false, Controller(direct, index));
    }

    public override bool IsDirectBufferPooled => true;
    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
    {
        return new AdaptiveByteBuf(initialCapacity, maxCapacity, (size, max) => Reserve(false, size, max));
    }

    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
    {
        return new AdaptiveByteBuf(initialCapacity, maxCapacity, (size, max) => Reserve(true, size, max));
    }

    internal AdaptiveChunkAllocation Reserve(bool direct, int size, int maxCapacity)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int fast = ComputeBufferCapacity(direct, size, maxCapacity);
            bool pooled = IsPooled(size);
            int index = SizeClassIndexOf(size);
            AdaptiveChunkAllocation allocation = null;
            if (pooled)
                foreach (var candidate in _chunks)
                    if (candidate.Direct == direct && candidate.Class == index &&
                        candidate.Chunk.RemainingCapacity >= fast &&
                        (allocation = candidate.Chunk.TryAllocate(fast)) != null)
                    {
                        break;
                    }
            if (allocation == null)
            {
                Chunk chunk;
                if (pooled)
                {
                    chunk = Controller(direct, index).NewChunkAllocation(size, MagazineFor(direct, index));
                }
                else
                {
                    ByteBuf raw = Raw(direct, size, maxCapacity);
                    try
                    {
                        chunk = new Chunk(raw, _registries[direct ? 1 : 0]);
                    }
                    catch
                    {
                        raw.Release();
                        throw;
                    }
                }
                try
                {
                    allocation = chunk.TryAllocate(fast) ?? throw new InvalidOperationException("A fresh chunk was exhausted.");
                    if (pooled)
                    {
                        _chunks.Add((chunk, direct, index));
                    }
                }
                catch
                {
                    allocation?.Release();
                    chunk.Dispose();
                    throw;
                }
                if (!pooled)
                {
                    chunk.Dispose();
                }
            }
            try
            {
                AfterReservation?.Invoke(size, allocation);
                return allocation;
            }
            catch
            {
                allocation.Release();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (var entry in _chunks) entry.Chunk.Dispose();
            _chunks.Clear();
            foreach (var pool in _pools) pool.Dispose();
        }
    }
}

// Test-only AdaptivePoolingAllocator.IChunkAllocator over a delegate, with pool/controller helpers.
internal sealed class TestChunkAllocator(Func<int, int, ByteBuf> allocate) : IChunkAllocator
{
    public ByteBuf Allocate(int initialCapacity, int maxCapacity)
    {
        return allocate(initialCapacity, maxCapacity);
    }

    internal static AdaptivePoolingAllocator Pool(bool lowMemory = false, Func<int, int, ByteBuf> allocate = null)
    {
        return new(new TestChunkAllocator(allocate ?? ((size, max) => new UnpooledHeapByteBuf(size, max))),
            lowMemory, 2, 128, 3, 4);
    }

    internal static SizeClassChunkController SizeClassController(AdaptivePoolingAllocator pool, int index)
    {
        return (SizeClassChunkController)pool.SizeClassedMagazineGroups[index].Magazines[0].ChunkController;
    }

    internal static BuddyChunkController BuddyController(AdaptivePoolingAllocator pool)
    {
        return (BuddyChunkController)pool.LargeBufferMagazineGroup.Magazines[0].ChunkController;
    }
}
