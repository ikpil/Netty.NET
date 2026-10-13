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

namespace Netty.NET.Buffer.Tests;

// Test-only storage binding for AdaptiveByteBuf. It exercises the real chunk/policy
// components; it does not implement or validate magazine/cache/stripe/purge behavior.
// Public AdaptiveByteBufAllocator is still pending. This fixture never auto-wraps leaks.
internal sealed class AdaptiveBufferTestAllocator : AbstractByteBufAllocator, IDisposable
{
    private readonly object _sync = new();
    private readonly AdaptiveAllocationPolicy[] _policies;
    private readonly AdaptiveChunkRegistry[] _registries = [new(), new()];
    private readonly List<(AdaptiveChunk Chunk, bool Direct, int Class)> _chunks = new();
    private readonly bool _threadLocal;
    private bool _disposed;
    internal NativeMemoryAllocator Native { get; }
    internal long UsedHeapMemory => _registries[0].TotalCapacity;
    internal long UsedDirectMemory => _registries[1].TotalCapacity;
    internal Func<bool, int, int, ByteBuf> StorageFactory;
    internal Action<int, AdaptiveChunkAllocation> AfterReservation;

    internal AdaptiveBufferTestAllocator(bool lowMemory = false, bool threadLocal = false,
        NativeMemoryAllocator native = null)
    {
        _policies = [new(lowMemory), new(lowMemory)];
        _threadLocal = threadLocal;
        Native = native ?? new NativeMemoryAllocator();
    }

    public override bool IsDirectBufferPooled => true;
    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        => new AdaptiveByteBuf(initialCapacity, maxCapacity, (size, max) => Reserve(false, size, max));
    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        => new AdaptiveByteBuf(initialCapacity, maxCapacity, (size, max) => Reserve(true, size, max));

    internal AdaptiveChunkAllocation Reserve(bool direct, int size, int maxCapacity)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            AdaptiveAllocationPolicy policy = _policies[direct ? 1 : 0];
            int fast = policy.ComputeBufferCapacity(size, maxCapacity);
            bool pooled = policy.IsPooled(size);
            int index = AdaptiveAllocationPolicy.SizeClassIndexOf(size);
            AdaptiveChunkAllocation allocation = null;
            if (pooled)
                foreach (var candidate in _chunks)
                    if (candidate.Direct == direct && candidate.Class == index &&
                        candidate.Chunk.RemainingCapacity >= fast &&
                        (allocation = candidate.Chunk.TryAllocate(fast)) != null) break;
            if (allocation == null)
            {
                int capacity = policy.ComputeChunkSize(size);
                int rawMaximum = pooled ? capacity : maxCapacity;
                ByteBuf raw = StorageFactory != null ? StorageFactory(direct, capacity, rawMaximum)
                    : direct ? new UnpooledDirectByteBuf(this, capacity, rawMaximum, Native)
                    : new UnpooledHeapByteBuf(this, capacity, rawMaximum);
                AdaptiveChunk chunk;
                try
                {
                    AdaptiveChunkRegistry registry = _registries[direct ? 1 : 0];
                    chunk = !pooled ? new AdaptiveChunk(raw, registry)
                        : index < 16 ? new AdaptiveSizeClassedChunk(raw, registry,
                            AdaptiveAllocationPolicy.GetSizeClasses()[index], _threadLocal)
                        : new AdaptiveBuddyChunk(raw, registry);
                }
                catch { raw.Release(); throw; }
                try
                {
                    allocation = chunk.TryAllocate(fast) ?? throw new InvalidOperationException("A fresh chunk was exhausted.");
                    if (pooled) _chunks.Add((chunk, direct, index));
                }
                catch { allocation?.Release(); chunk.Dispose(); throw; }
                if (!pooled) chunk.Dispose();
            }
            try { AfterReservation?.Invoke(size, allocation); return allocation; }
            catch { allocation.Release(); throw; }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _chunks) entry.Chunk.Dispose();
            _chunks.Clear();
        }
    }
}
