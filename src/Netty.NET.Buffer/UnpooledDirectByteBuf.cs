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
/// <summary>A reference-counted buffer with explicitly owned native storage.</summary>
/// <remarks>Memory views are borrowed. Pins preserve physical storage across final
/// release; they do not keep the buffer's logical reference count above zero.</remarks>
// CLR: a NativeMemoryAllocator reservation domain replaces Java direct-memory
// provider selection. ByteBufAllocator policy and borrowed-address/I/O APIs remain
// separate porting work; Memory/Span carry the implemented bounded data contract.
public class UnpooledDirectByteBuf : AbstractReferenceCountedByteBuf
{
    private readonly NativeMemoryAllocator _allocator;
    private NativeMemoryOwner _owner;

    /**
         * Creates a new direct buffer.
         *
         * @param initialCapacity the initial capacity of the underlying direct buffer
         * @param maxCapacity     the maximum capacity of the underlying direct buffer
         */
    public UnpooledDirectByteBuf(int initialCapacity = 256, int maxCapacity = int.MaxValue,
        NativeMemoryAllocator allocator = null) : base(maxCapacity)
    {
        if (initialCapacity < 0 || initialCapacity > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _allocator = allocator ?? NativeMemoryAllocator.Shared;
        _owner = _allocator.Allocate(initialCapacity, clear: true);
    }
    protected override Memory<byte> GetMemoryCore(int index, int length) => _owner.Memory.Slice(index, length);
    internal override BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();
        return new BufferMemoryLease(_owner.Memory.Pin());
    }
    public override bool IsDirect => true;
    public override int Capacity
    {
        get => _owner.Length;
        set
        {
            CheckNewCapacity(value);
            if (value == _owner.Length) return;
            NativeMemoryOwner previous = _owner;
            NativeMemoryOwner replacement = _allocator.Allocate(value, clear: true);
            try { previous.Memory.Span.Slice(0, Math.Min(value, previous.Length)).CopyTo(replacement.Memory.Span); }
            catch { replacement.Dispose(); throw; }
            _owner = replacement;
            TrimIndicesToCapacity(value);
            previous.Dispose();
        }
    }
    public override ByteBuf Copy(int index, int length)
    {
        ReadOnlySpan<byte> source = AsSpan(index, length);
        var result = new UnpooledDirectByteBuf(length, MaxCapacity, _allocator);
        try { return result.WriteBytes(source); }
        catch { result.Release(); throw; }
    }
    protected override void Deallocate() => _owner.Dispose();
}
