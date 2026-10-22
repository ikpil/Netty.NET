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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * A NIO {@link ByteBuffer} based buffer. It is recommended to use
 * {@link UnpooledByteBufAllocator#directBuffer(int, int)}, {@link Unpooled#directBuffer(int)} and
 * {@link Unpooled#wrappedBuffer(ByteBuffer)} instead of calling the constructor explicitly.
 */
/// <summary>A reference-counted buffer with owned or borrowed native storage.</summary>
/// <remarks>Memory views are borrowed. Pins preserve physical storage across final
/// release; they do not keep the buffer's logical reference count above zero.</remarks>
// CLR: NativeMemoryAllocator supplies the native reservation domain, while
// IByteBufAllocator controls buffer creation, copies and growth policy.
// UnpooledUnsafeDirectByteBuf uses this same bounded storage kernel; native
// addresses are scoped pins rather than cached addresses or JVM VarHandle dispatch.
public class UnpooledDirectByteBuf : AbstractReferenceCountedByteBuf
{
    private readonly NativeMemoryAllocator _allocator;
    private NativeMemoryOwner _owner;
    private IDisposable _externalOwner;
    private Memory<byte> _memory;
    private int _capacity;
    private readonly IByteBufAllocator _bufferAllocator;
    private readonly UnpooledByteBufAllocator.UnpooledByteBufAllocatorMetric _metric;
    public override IByteBufAllocator Allocator => _bufferAllocator;

    /**
         * Creates a new direct buffer.
         *
         * @param initialCapacity the initial capacity of the underlying direct buffer
         * @param maxCapacity     the maximum capacity of the underlying direct buffer
         */
    public UnpooledDirectByteBuf(int initialCapacity = 256, int maxCapacity = int.MaxValue,
        NativeMemoryAllocator allocator = null)
        : this(allocator == null ? UnpooledByteBufAllocator.Default : new UnpooledByteBufAllocator(true, allocator),
            initialCapacity, maxCapacity, allocator) { }

    /// <summary>Creates native storage with an explicit buffer allocation policy and optional reservation domain.</summary>
    public UnpooledDirectByteBuf(IByteBufAllocator bufferAllocator, int initialCapacity, int maxCapacity,
        NativeMemoryAllocator nativeAllocator = null)
        : this(bufferAllocator, initialCapacity, maxCapacity, nativeAllocator, null) { }

    internal UnpooledDirectByteBuf(IByteBufAllocator bufferAllocator, int initialCapacity, int maxCapacity,
        NativeMemoryAllocator nativeAllocator, UnpooledByteBufAllocator.UnpooledByteBufAllocatorMetric metric)
        : base(maxCapacity)
    {
        ArgumentNullException.ThrowIfNull(bufferAllocator);
        _bufferAllocator = bufferAllocator;
        if (initialCapacity < 0 || initialCapacity > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _allocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        _owner = _allocator.Allocate(initialCapacity, clear: true);
        _memory = _owner.Memory;
        _capacity = initialCapacity;
        _metric = metric;
        _metric?.AddDirect(initialCapacity);
    }
    /**
     * Creates a new direct buffer by wrapping the specified initial buffer.
     *
     * @param maxCapacity the maximum capacity of the underlying direct buffer
     */
    /// <remarks>The memory must come from NativeMemoryOwner. The caller keeps that
    /// owner valid during access; Release never disposes the borrowed owner. A capacity
    /// change detaches to owned native storage in the optional reservation domain.
    /// Wrapped storage and its replacements do not charge allocator factory metrics.</remarks>
    public UnpooledDirectByteBuf(IByteBufAllocator bufferAllocator, Memory<byte> initialBuffer,
        int maxCapacity, NativeMemoryAllocator nativeAllocator = null) : base(maxCapacity)
    {
        // We never try to free the buffer if it was provided by the end-user as we don't know if this is a duplicate or
        // a slice. This is done to prevent an IllegalArgumentException when using Java9 as Unsafe.invokeCleaner(...)
        // will check if the given buffer is either a duplicate or slice and in this case throw an
        // IllegalArgumentException.
        //
        // See https://hg.openjdk.java.net/jdk9/hs-demo/jdk/file/0d2ab72ba600/src/jdk.unsupported/share/classes/
        // sun/misc/Unsafe.java#l1250
        //
        // We also call slice() explicitly here to preserve behaviour with previous netty releases.
        // CLR: Memory<byte> already captures the caller's range without mutable NIO
        // position/limit state. The owner stays borrowed until explicitly detached.
        ArgumentNullException.ThrowIfNull(bufferAllocator);
        if (!NativeMemoryOwner.TryGetOwner(initialBuffer, out _))
            throw new ArgumentException("The memory must be backed by NativeMemoryOwner.", nameof(initialBuffer));
        if (initialBuffer.Length > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(maxCapacity));
        _bufferAllocator = bufferAllocator;
        _allocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        _memory = initialBuffer;
        _capacity = initialBuffer.Length;
        SetIndex(0, _capacity);
    }

    // CLR: raw-address wrapping has a separately disposable memory facade. Its
    // SafeHandle controls optional external ownership and outstanding pin leases.
    internal UnpooledDirectByteBuf(IByteBufAllocator bufferAllocator, NativeAddressMemoryManager manager,
        NativeMemoryAllocator nativeAllocator = null) : base(manager.Length)
    {
        ArgumentNullException.ThrowIfNull(bufferAllocator);
        _bufferAllocator = bufferAllocator;
        _allocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        _memory = manager.Memory;
        _externalOwner = manager;
        _capacity = manager.Length;
        SetIndex(0, _capacity);
    }

    protected override Memory<byte> GetMemoryCore(int index, int length) => _memory.Slice(index, length);
    internal override BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();
        return new BufferMemoryLease(_memory.Pin());
    }
    public override bool IsContiguous => true;
    public override bool IsDirect => true;
    public override int Capacity
    {
        get => _capacity;
        set
        {
            CheckNewCapacity(value);
            if (value == _capacity) return;
            // CLR replacement for the JVM NoCleaner reallocation specialization:
            // allocate/copy before publishing preserves old pins and failure state.
            // The reservation limit must cover both allocations, even when shrinking.
            NativeMemoryOwner previous = _owner;
            NativeMemoryOwner replacement = _allocator.Allocate(value, clear: true);
            try { _memory.Span.Slice(0, Math.Min(value, _capacity)).CopyTo(replacement.Memory.Span); }
            catch { replacement.Dispose(); throw; }
            int previousCapacity = _capacity;
            _owner = replacement;
            _memory = replacement.Memory;
            _capacity = value;
            TrimIndicesToCapacity(value);
            previous?.Dispose();
            _externalOwner?.Dispose();
            _externalOwner = null;
            _metric?.AddDirect((long)value - previousCapacity);
        }
    }
    public override ByteBuf Copy(int index, int length)
    {
        // CLR: keep the borrowed native span valid across allocator/write callbacks,
        // including a callback that resizes or releases this buffer. Copy only reads.
        using BufferMemoryLease lease = AcquireReadLease();
        ReadOnlySpan<byte> source = AsReadOnlySpan(index, length);
        ByteBuf result = Allocator.DirectBuffer(length, MaxCapacity);
        try { return result.WriteBytes(source); }
        catch { result.Release(); throw; }
    }
    protected override void Deallocate()
    {
        _memory = default;
        _owner?.Dispose();
        _owner = null;
        _externalOwner?.Dispose();
        _externalOwner = null;
        _metric?.AddDirect(-_capacity);
    }
}
