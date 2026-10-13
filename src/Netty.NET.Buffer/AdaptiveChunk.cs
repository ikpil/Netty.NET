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
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: atomics replace LongAdder. Count physical chunk capacity, including cached
// chunks and chunks awaiting their last segment/operation lease. No membership set.
internal sealed class AdaptiveChunkRegistry
{
    private long _totalCapacity;
    internal long TotalCapacity => Volatile.Read(ref _totalCapacity);
    internal void Add(AdaptiveChunk chunk) => Interlocked.Add(ref _totalCapacity, chunk.Capacity);
    internal void Remove(AdaptiveChunk chunk) => Interlocked.Add(ref _totalCapacity, -chunk.Capacity);
}

// The allocating magazine must serialize allocation, capacity inspection and retirement.
// Returns may come from other threads. Construction transfers the raw delegate's single
// reference only on success. Magazine attachment/cache transfer and events are still pending.
internal class AdaptiveChunk : IDisposable
{
    private ByteBuf _storage;
    private readonly AdaptiveChunkRegistry _registry;
    // Always populate the refCnt field, so HotSpot doesn't emit `null` checks.
    // This is safe to do even on native-image.
    // CLR: a non-nullable integer and ReferenceCountUpdater replace the JVM RefCnt object.
    private int _references = 1;
    private int _retired;
    protected int AllocatedBytes;
    internal int Capacity { get; }
    internal bool IsDirect { get; }
    internal bool IsPooled { get; }
    internal bool IsDeallocated => Volatile.Read(ref _storage) == null;
    protected ByteBuf Storage => Volatile.Read(ref _storage) ?? throw new ObjectDisposedException(nameof(AdaptiveChunk));
    protected bool IsRetired => Volatile.Read(ref _retired) != 0;

    internal AdaptiveChunk(ByteBuf storage, AdaptiveChunkRegistry registry)
        : this(storage, registry, false)
    {
        Register();
    }

    private protected AdaptiveChunk(ByteBuf storage, AdaptiveChunkRegistry registry, bool pooled)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(registry);
        if (storage.ReferenceCount != 1)
            throw new ArgumentException("The raw chunk delegate must have one live reference.", nameof(storage));
        _storage = storage;
        _registry = registry;
        Capacity = storage.Capacity;
        IsDirect = storage.IsDirect;
        IsPooled = pooled;
    }

    // Derived constructors register only after all fallible initialization succeeds.
    private protected void Register() => _registry.Add(this);
    private protected void ThrowIfRetired() => ObjectDisposedException.ThrowIf(IsRetired, this);
    private protected bool TryRetire() => Interlocked.Exchange(ref _retired, 1) == 0;
    private protected void Retain() => ReferenceCountUpdater.Retain(ref _references);
    private protected void Release()
    {
        if (ReferenceCountUpdater.Release(ref _references)) Deallocate();
    }

    private protected void Deallocate()
    {
        ByteBuf storage = Interlocked.Exchange(ref _storage, null);
        if (storage == null) return;
        _registry.Remove(this);
        storage.Release();
    }

    internal virtual AdaptiveChunkAllocation TryAllocate(int startingCapacity)
    {
        ThrowIfRetired();
        ArgumentOutOfRangeException.ThrowIfNegative(startingCapacity);
        if (startingCapacity > Capacity - AllocatedBytes) return null;
        int startIndex = AllocatedBytes;
        AllocatedBytes = startIndex + startingCapacity;
        Retain();
        try
        {
            return new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
        }
        catch
        {
            // If chunk is not null we know that buf.init(...) failed and so we need to manually release
            // the chunk again as we retained it before calling buf.init(...). Beside this we also need to
            // restore the old allocatedBytes value.
            // CLR: allocation initialization replaces buf.init here; ownership is transferred on success.
            AllocatedBytes = startIndex;
            Release();
            throw;
        }
    }

    internal virtual int RemainingCapacity => Capacity - AllocatedBytes;
    internal virtual bool HasUnprocessedFreelistEntries => false;
    internal virtual void ProcessFreelistEntries() { }

    /**
     * Called when a ByteBuf is done using its allocation in this chunk.
     */
    internal virtual void ReleaseSegment(int ignoredSegmentId, int size) => Release();

    // CLR: Dispose retires the magazine/cache ownership once. Live allocations and
    // leases still own their segments. Calling it again must not release that ownership again.
    public virtual void Dispose()
    {
        if (TryRetire()) Release();
    }
}
