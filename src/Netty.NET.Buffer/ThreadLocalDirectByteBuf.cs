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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

internal sealed class ThreadLocalDirectByteBuf : AbstractReferenceCountedByteBuf
{
    private readonly ThreadLocalDirectBufferCache _cache;
    private readonly NativeMemoryAllocator _native;
    private ThreadLocalDirectAllocation _allocation;
    private int _capacity;
    public override IByteBufAllocator Allocator => UnpooledByteBufAllocator.Default;
    public override bool IsDirect => true;
    public override bool IsContiguous => true;

    internal static ByteBuf Create(ThreadLocalDirectBufferCache cache, NativeMemoryOwner owner = null)
    {
        NativeMemoryAllocator native = cache?.NativeAllocator ?? NativeMemoryAllocator.Shared;
        owner ??= native.Allocate(256, clear: true);
        try { return new ThreadLocalDirectByteBuf(cache, native, owner); }
        catch { owner.Dispose(); throw; }
    }

    private ThreadLocalDirectByteBuf(ThreadLocalDirectBufferCache cache, NativeMemoryAllocator native, NativeMemoryOwner owner)
        : base(int.MaxValue)
    {
        _cache = cache;
        _native = native;
        _allocation = new ThreadLocalDirectAllocation(owner, cache);
        _capacity = owner.Length;
    }

    protected override Memory<byte> GetMemoryCore(int index, int length) => _allocation.Memory.Slice(index, length);
    internal override BufferMemoryLease AcquireReadLease()
    { EnsureAccessible(); return _allocation.AcquireLease(); }
    public override int Capacity
    {
        get => _capacity;
        set
        {
            CheckNewCapacity(value);
            if (value == _capacity) return;
            NativeMemoryOwner owner = _native.Allocate(value, clear: true);
            ThreadLocalDirectAllocation replacement;
            try { replacement = new ThreadLocalDirectAllocation(owner, _cache); }
            catch { owner.Dispose(); throw; }
            try { _allocation.Memory.Span.Slice(0, Math.Min(value, _capacity)).CopyTo(replacement.Memory.Span); }
            catch { replacement.DisableCaching(); replacement.Release(); throw; }
            ThreadLocalDirectAllocation previous = _allocation;
            _allocation = replacement;
            _capacity = value;
            TrimIndicesToCapacity(value);
            // Java resizing frees the previous backing; only final release caches.
            previous.DisableCaching();
            previous.Release();
        }
    }
    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        ByteBuf result = Allocator.DirectBuffer(length, MaxCapacity);
        try { return result.WriteBytes(this, index, length); }
        catch { result.Release(); throw; }
    }
    protected override void Deallocate()
    {
        ThreadLocalDirectAllocation allocation = _allocation;
        _allocation = null;
        allocation.Release();
    }
}
