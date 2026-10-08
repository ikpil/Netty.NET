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
using System.Buffers;

namespace Netty.NET.Buffer;

/**
 * Abstract base class for {@link ByteBuf} implementations that wrap another
 * {@link ByteBuf}.
 *
 * @deprecated Do not use.
 */
// CLR: one private view represents fixed slices or dynamic-capacity duplicates.
// It delegates shared ownership to the parent and keeps independent indices.
internal sealed class ByteBufView : ByteBuf
{
    private readonly ByteBuf _parent;
    private readonly int _offset;
    private readonly bool _fixedCapacity;
    internal ByteBufView(ByteBuf parent) : base(parent.MaxCapacity)
    {
        _parent = parent;
        SetIndex(parent.ReaderIndex, parent.WriterIndex);
        MarkReaderIndex(); MarkWriterIndex();
    }
    internal ByteBufView(ByteBuf parent, int offset, int length) : base(length)
    {
        _parent = parent; _offset = offset; _fixedCapacity = true;
        WriterIndex = length;
    }
    protected override Memory<byte> GetMemoryCore(int index, int length) => _parent.AsMemory(_offset + index, length);
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => _parent.AsReadOnlyMemory(_offset + index, length);
    internal override BufferMemoryLease PinMemoryForWrite() => _parent.PinMemoryForWrite();
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => _parent.TryGetReadOnlyMemory(_offset + index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination)
        => _parent.GetBytes(_offset + index, destination);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => _parent.AsReadOnlySequence(_offset + index, length);
    internal override BufferMemoryLease AcquireReadLease() => _parent.AcquireReadLease();
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
        => _parent.TryGetMemory(_offset + index, length, out memory);
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source) => _parent.SetBytes(_offset + index, source);
    protected override void SetZeroCore(int index, int length) => _parent.SetZero(_offset + index, length);
    public override bool IsDirect => _parent.IsDirect;
    public override bool IsReadOnly => _parent.IsReadOnly;
    public override int Capacity
    {
        get => _fixedCapacity ? MaxCapacity : _parent.Capacity;
        set
        {
            EnsureCanWrite();
            if (_fixedCapacity) throw new NotSupportedException("A slice has fixed capacity.");
            _parent.Capacity = value;
        }
    }
    public override ByteBuf Unwrap() => _parent;
    public override int ReferenceCount => _parent.ReferenceCount;
    public override ByteBuf Retain(int increment = 1) { _parent.Retain(increment); return this; }
    public override bool Release(int decrement = 1) => _parent.Release(decrement);
    public override ByteBuf Touch(object hint = null) { _parent.Touch(hint); return this; }
}
