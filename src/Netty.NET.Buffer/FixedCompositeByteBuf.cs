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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * {@link ByteBuf} implementation which allows to wrap an array of {@link ByteBuf} in a read-only mode.
 * This is useful to write an array of {@link ByteBuf}s.
 */
internal sealed class FixedCompositeByteBuf : AbstractReferenceCountedByteBuf
{
    private readonly Component[] _components;
    private readonly bool _direct;

    internal FixedCompositeByteBuf(ByteBuf[] buffers) : this(Capture(buffers)) { }

    private FixedCompositeByteBuf(Component[] components)
        : base(components.Length == 0 ? 0 : components[^1].EndOffset)
    {
        _components = components;
        _direct = true;
        // CLR: check every input, including the first (missed by the original loop).
        foreach (Component component in components) _direct &= component.Source.IsDirect;
        SetIndex(0, Capacity);
    }

    private static Component[] Capture(ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        var components = new Component[buffers.Length];
        int offset = 0;
        for (int i = 0; i < buffers.Length; ++i)
        {
            ByteBuf source = buffers[i] ?? throw new ArgumentException("A component is null.", nameof(buffers));
            if (source.ReferenceCount <= 0) throw new IllegalReferenceCountException(source.ReferenceCount);
            int length = source.ReadableBytes;
            if (length > int.MaxValue - offset)
                throw new ArgumentException("The combined capacity exceeds Int32.MaxValue.", nameof(buffers));
            // Create a new component and store it in the array so it not create a new object
            // on the next access.
            // CLR: capture immutable descriptors once, without slicing or rewriting the caller's array.
            // Preserve the original absolute-zero mapping, not ReaderIndex-based wrapping.
            components[i] = new Component(source, offset, offset + length);
            offset += length;
        }
        return components;
    }

    public override int Capacity
    {
        get => MaxCapacity;
        set => EnsureCanWrite();
    }
    public override bool IsReadOnly => true;
    public override bool IsDirect => _direct;
    public override bool CanWrite(int byteCount) => false;
    public override ByteBuf DiscardReadBytes() { EnsureCanWrite(); return this; }

    protected override Memory<byte> GetMemoryCore(int index, int length)
        => throw new NotSupportedException("The buffer is read-only.");

    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => TryGetReadOnlyMemoryCore(index, length, out var memory) ? memory
            : throw new NotSupportedException("The range spans multiple components. Use AsReadOnlySequence or GetBytes.");

    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        if (length == 0) { memory = ReadOnlyMemory<byte>.Empty; return true; }
        Component component = _components[FindComponent(index)];
        if (length <= component.EndOffset - index)
            return component.Source.TryGetReadOnlyMemory(index - component.Offset, length, out memory);
        memory = default;
        return false;
    }

    protected override void GetBytesCore(int index, Span<byte> destination)
    {
        if (destination.IsEmpty) return;
        int componentIndex = FindComponent(index);
        while (!destination.IsEmpty)
        {
            Component component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, destination.Length);
            component.Source.GetBytes(index - component.Offset, destination[..count]);
            index += count;
            destination = destination[count..];
        }
    }

    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
    {
        if (length == 0) return ReadOnlySequence<byte>.Empty;
        SequenceSegment first = null, last = null;
        int componentIndex = FindComponent(index);
        //noinspection ForLoopReplaceableByForEach
        // CLR: flatten borrowed segments instead of allocating a merged NIO ByteBuffer.
        while (length > 0)
        {
            Component component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, length);
            foreach (ReadOnlyMemory<byte> memory in component.Source.AsReadOnlySequence(index - component.Offset, count))
            {
                if (memory.IsEmpty) continue;
                var next = new SequenceSegment(memory);
                if (last == null) first = next;
                else last.Append(next);
                last = next;
            }
            index += count;
            length -= count;
        }
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        // CLR: Unpooled's heap allocation policy, with the original allocator's default maximum.
        ByteBuf result = Unpooled.Buffer(length);
        try { result.SetBytes(0, this, index, length); result.WriterIndex = length; return result; }
        catch { result.Release(); throw; }
    }

    internal override BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();
        var leases = new BufferMemoryLease[_components.Length];
        int acquired = 0;
        try
        {
            for (; acquired < leases.Length; ++acquired) leases[acquired] = _components[acquired].Source.AcquireReadLease();
            return new BufferMemoryLease(leases);
        }
        catch
        {
            for (int i = acquired - 1; i >= 0; --i) leases[i].Dispose();
            throw;
        }
    }

    protected override void Deallocate()
    {
        for (int i = 0; i < _components.Length; ++i) Buffer(i).Release();
    }

    /**
     * Return the {@link ByteBuf} stored at the given index of the array.
     */
    private ByteBuf Buffer(int index) => _components[index].Source;

    private int FindComponent(int index)
    {
        // CLR: bounded binary search skips empty components, including repeated boundaries.
        int low = 0, high = _components.Length - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >>> 1);
            Component component = _components[mid];
            if (index >= component.EndOffset) low = mid + 1;
            else if (index < component.Offset) high = mid - 1;
            else return mid;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private readonly record struct Component(ByteBuf Source, int Offset, int EndOffset);

    private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
    {
        internal SequenceSegment(ReadOnlyMemory<byte> memory) => Memory = memory;
        internal void Append(SequenceSegment next)
        { next.RunningIndex = RunningIndex + Memory.Length; Next = next; }
    }
}
