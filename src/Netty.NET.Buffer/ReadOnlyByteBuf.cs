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

namespace Netty.NET.Buffer;

/// <summary>A live read-only view. Indices are independent; data and ownership belong to the parent.</summary>
// CLR: one sealed view plus bounded ReadOnlyMemory replaces the deprecated wrapper and its unchecked JVM subtype.
/**
 * A derived buffer which forbids any write requests to its parent.  It is
 * recommended to use {@link Unpooled#unmodifiableBuffer(ByteBuf)}
 * instead of calling the constructor explicitly.
 *
 * @deprecated Do not use.
 */
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
/**
 * Specialized {@link ReadOnlyByteBuf} sub-type which allows fast access to parent
 * without extra bound-checks.
 */
// CLR: the original subtype rationale above is retained; bounded spans replace its unchecked JVM dispatch.
internal sealed class ReadOnlyByteBuf : ByteBuf
{
    private readonly ByteBuf _parent;

    internal ReadOnlyByteBuf(ByteBuf parent)
        : base(parent?.MaxCapacity ?? throw new ArgumentNullException(nameof(parent)))
    {
        _parent = parent is ReadOnlyByteBuf readOnly ? readOnly._parent : parent;
        SetIndex(parent.ReaderIndex, parent.WriterIndex);
    }

    public override bool IsReadOnly => true;
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => _parent.TryGetReadOnlyMemory(index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination)
        => _parent.GetBytes(index, destination);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => _parent.AsReadOnlySequence(index, length);
    internal override BufferMemoryLease AcquireReadLease() => _parent.AcquireReadLease();
    public override IByteBufAllocator Allocator => _parent.Allocator;
    public override bool IsDirect => _parent.IsDirect;
    public override int Capacity
    {
        get => _parent.Capacity;
        set { EnsureCanWrite(); }
    }
    public override ByteBuf Unwrap() => _parent;
    protected override Memory<byte> GetMemoryCore(int index, int length)
        => throw new NotSupportedException("The buffer is read-only.");
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => _parent.AsReadOnlyMemory(index, length);
    public override int ReferenceCount => _parent.ReferenceCount;
    public override ByteBuf Retain(int increment = 1) { _parent.Retain(increment); return this; }
    public override bool Release(int decrement = 1) => _parent.Release(decrement);
    public override ByteBuf Touch(object hint = null) { _parent.Touch(hint); return this; }

    public override ByteBuf Copy(int index, int length) => _parent.Copy(index, length);
    public override ByteBuf Slice(int index, int length)
    {
        CheckIndex(index, length);
        return new ReadOnlyByteBuf(_parent.Slice(index, length));
    }
    public override ByteBuf Duplicate()
    {
        EnsureAccessible();
        return new ReadOnlyByteBuf(this);
    }
}
