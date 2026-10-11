/*
 * Copyright 2013 The Netty Project
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

/**
 * Read-only ByteBuf which wraps a read-only ByteBuffer.
 */
/// <summary>A reference-counted read-only buffer borrowing an external memory range.</summary>
/// <remarks>ReadOnlyMemory replaces the Java ByteBuffer's captured remaining slice.
/// The caller keeps the external owner valid until all buffer access has ended;
/// Release drops this buffer's memory reference without disposing that owner.
/// External writes remain visible. NativeMemoryOwner backing is recognized as direct;
/// arbitrary memory managers have no reliable storage-kind metadata and use heap copy policy.</remarks>
public sealed class ReadOnlyMemoryByteBuf : AbstractReferenceCountedByteBuf
{
    private ReadOnlyMemory<byte> _memory;
    private readonly IByteBufAllocator _allocator;
    private readonly bool _direct;

    public ReadOnlyMemoryByteBuf(IByteBufAllocator allocator, ReadOnlyMemory<byte> memory)
        : base(memory.Length)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        _allocator = allocator;
        _memory = memory;
        _direct = NativeMemoryOwner.TryGetOwner(memory, out _);
        SetIndex(0, memory.Length);
    }

    public override IByteBufAllocator Allocator => _allocator;
    public override bool IsReadOnly => true;
    public override bool IsDirect => _direct;
    public override int Capacity
    {
        get => MaxCapacity;
        set { EnsureCanWrite(); }
    }

    protected override Memory<byte> GetMemoryCore(int index, int length)
        => throw new NotSupportedException("The buffer is read-only.");

    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => _memory.Slice(index, length);

    internal override BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();
        return new BufferMemoryLease(_memory.Pin());
    }

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        // Always duplicate the buffer so it's safe to call copy from multiple threads.
        // CLR: a local immutable range replaces Java's per-copy ByteBuffer cursor.
        ReadOnlyMemory<byte> source = _memory.Slice(index, length);
        ByteBuf result = IsDirect ? Allocator.DirectBuffer(length) : Allocator.HeapBuffer(length);
        try { return result.WriteBytes(source.Span); }
        catch { result.Release(); throw; }
    }

    protected override void Deallocate() => _memory = default;
}
