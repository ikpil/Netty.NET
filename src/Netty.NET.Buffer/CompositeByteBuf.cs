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
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * A virtual buffer which shows multiple buffers as a single merged buffer.  It is recommended to use
 * {@link ByteBufAllocator#compositeBuffer()} or {@link Unpooled#wrappedBuffer(ByteBuf...)} instead of calling the
 * constructor explicitly.
 */
/// <remarks>Adding a component transfers one existing reference without retaining it.
/// Retain before adding if the caller needs independent ownership. Component layout
/// changes, storage resizing and release must be excluded while borrowing memory.</remarks>
public partial class CompositeByteBuf : AbstractReferenceCountedByteBuf
{
    private readonly List<ComponentEntry> _components;
    private int _deallocatedComponentCount;
    private readonly bool _direct;
    private readonly IByteBufAllocator _bufferAllocator;
    public override IByteBufAllocator Allocator => _bufferAllocator;

    public override string ToString()
    {
        string result = base.ToString();
        int count = ReferenceCount == 0 ? _deallocatedComponentCount : NumComponents;
        return FormattableString.Invariant($"{result[..^1]}, components={count})");
    }

    // CLR: List replaces Java component-array growth.
    // The allocation policy affects padding, copies and consolidation. IsDirect
    // describes the actual components, including nested composites.
    public CompositeByteBuf(int maxNumComponents = 16, bool direct = false, NativeMemoryAllocator allocator = null)
        : this(allocator == null ? UnpooledByteBufAllocator.Default : new UnpooledByteBufAllocator(direct, allocator),
            direct, maxNumComponents) { }

    public CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents = 16)
        : base(int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        if (maxNumComponents < 1) throw new ArgumentOutOfRangeException(nameof(maxNumComponents));
        MaxNumComponents = maxNumComponents;
        _components = new();
        _direct = direct;
        _bufferAllocator = allocator;
    }

    // CLR: transparent composite wrappers use the same component metadata and index
    // owner, without an empty side-list. This also makes existing flatten/enumeration
    // kernels see the real components. Reference ownership stays with the wrapped buffer.
    private protected CompositeByteBuf(CompositeByteBuf wrapped) : base(wrapped)
    {
        _components = wrapped._components;
        _direct = wrapped._direct;
        _bufferAllocator = wrapped.Allocator;
        MaxNumComponents = wrapped.MaxNumComponents;
    }

    /**
     * Return the current number of {@link ByteBuf}'s that are composed in this instance
     */
    public int NumComponents => _components.Count;
    /**
     * Return the max number of {@link ByteBuf}'s that are composed in this instance
     */
    public int MaxNumComponents { get; }
    public override bool IsDirect
    {
        get
        {
            if (_components.Count == 0) return false;
            for (int i = 0; i < _components.Count; ++i)
                if (!_components[i].Source.IsDirect) return false;
            return true;
        }
    }

    /**
     * Add the given {@link ByteBuf}.
     * <p>
     * Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuf}.
     * If you need to have it increased use {@link #addComponent(boolean, ByteBuf)}.
     * <p>
     * {@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link CompositeByteBuf}.
     * @param buffer the {@link ByteBuf} to add. {@link ByteBuf#release()} ownership is transferred to this
     * {@link CompositeByteBuf}.
     */
/**
     * Add the given {@link ByteBuf} and increase the {@code writerIndex} if {@code increaseWriterIndex} is
     * {@code true}.
     *
     * {@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link CompositeByteBuf}.
     * @param buffer the {@link ByteBuf} to add. {@link ByteBuf#release()} ownership is transferred to this
     * {@link CompositeByteBuf}.
     */
    public virtual CompositeByteBuf AddComponent(ByteBuf buffer, bool increaseWriterIndex = false)
        => AddComponent(_components.Count, buffer, increaseWriterIndex);

    /**
     * Add the given {@link ByteBuf} on the specific index and increase the {@code writerIndex}
     * if {@code increaseWriterIndex} is {@code true}.
     *
     * {@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link CompositeByteBuf}.
     * @param cIndex the index on which the {@link ByteBuf} will be added.
     * @param buffer the {@link ByteBuf} to add. {@link ByteBuf#release()} ownership is transferred to this
     * {@link CompositeByteBuf}.
     */
/**
     * Add the given {@link ByteBuf} on the specific index.
     * <p>
     * Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuf}.
     * If you need to have it increased use {@link #addComponent(boolean, int, ByteBuf)}.
     * <p>
     * {@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link CompositeByteBuf}.
     * @param cIndex the index on which the {@link ByteBuf} will be added.
     * @param buffer the {@link ByteBuf} to add. {@link ByteBuf#release()} ownership is transferred to this
     * {@link CompositeByteBuf}.
     */
    /// <remarks>Validation failure releases the incoming reference, as in Netty.
    /// Null and ownership cycles are rejected before transfer. A consolidation
    /// failure leaves the successfully inserted component owned by this buffer.</remarks>
    public virtual CompositeByteBuf AddComponent(int componentIndex, ByteBuf buffer, bool increaseWriterIndex = false)
    {
        AddComponentCore(componentIndex, buffer, increaseWriterIndex);
        ConsolidateIfNeeded();
        return this;
    }

    private int AddComponentCore(int componentIndex, ByteBuf buffer, bool increaseWriterIndex)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        // CLR: cycles have no valid reference-counted ownership. Reject them before consuming a reference.
        if (ContainsBuffer(buffer, this)) throw new ArgumentException("Component ownership must be acyclic.", nameof(buffer));
        bool wasAdded = false;
        try
        {
            CheckComponentRange(componentIndex, 0);
            int length = buffer.ReadableBytes;
            buffer.TryGetReadOnlyMemory(buffer.ReaderIndex, length, out _);
            // No need to consolidate - just add a component to the list.
            // Check if we would overflow.
            // See https://github.com/netty/netty/issues/10194
            if (length > MaxCapacity - Capacity) throw new ArgumentOutOfRangeException(nameof(buffer));
            // unpeel any intermediate outer layers (UnreleasableByteBuf, LeakAwareByteBufs, SwappedByteBuf)
            // unwrap if already sliced
            // We don't need to slice later to expose the internal component if the readable range
            // is already the entire buffer
            // CLR: preserve the original rationale, but keep source views and captured
            // coordinates instead of unwrapping into an unchecked hierarchy.
            var component = new ComponentEntry(buffer, buffer.ReaderIndex, length);
            _components.Insert(componentIndex, component);
            wasAdded = true;
            UpdateOffsets();
            if (increaseWriterIndex) WriterIndex += length;
            return length;
        }
        finally { if (!wasAdded) buffer.Release(); }
    }

    private static bool ContainsBuffer(ByteBuf buffer, ByteBuf target)
    {
        if (ReferenceEquals(buffer, target)) return true;
        if (buffer is CompositeByteBuf composite)
        {
            // Wrapper aliases represent the same component owner; reject ownership
            // cycles before consuming a reference, including raw/wrapped aliases.
            if (target is CompositeByteBuf targetComposite &&
                ReferenceEquals(composite._components, targetComposite._components)) return true;
            foreach (ComponentEntry entry in composite._components)
                if (ContainsBuffer(entry.Source, target)) return true;
        }
        ByteBuf parent = buffer.Unwrap();
        return parent != null && ContainsBuffer(parent, target);
    }

    /**
     * Remove the {@link ByteBuf} from the given index.
     *
     * @param cIndex the index on from which the {@link ByteBuf} will be remove
     */
    public virtual CompositeByteBuf RemoveComponent(int componentIndex) => RemoveComponentsCore(componentIndex, 1);
    /**
     * Remove the number of {@link ByteBuf}s starting from the given index.
     *
     * @param cIndex the index on which the {@link ByteBuf}s will be started to removed
     * @param numComponents the number of components to remove
     */
    public virtual CompositeByteBuf RemoveComponents(int componentIndex, int numComponents)
        => RemoveComponentsCore(componentIndex, numComponents);

    private CompositeByteBuf RemoveComponentsCore(int componentIndex, int numComponents)
    {
        CheckComponentRange(componentIndex, numComponents);
        for (int i = componentIndex; i < componentIndex + numComponents; ++i) _components[i].Free();
        _components.RemoveRange(componentIndex, numComponents);
        // Only need to call updateComponentOffsets if the length was > 0
        // CLR: recompute the small List's offsets uniformly, including empty components.
        UpdateOffsets();
        // Netty leaves reader/writer indices unchanged after explicit removal.
        return this;
    }

    /**
     * Return the index for the given offset
     */
    public int ToComponentIndex(int offset) { CheckIndex(offset, 1); return FindComponentIndex(offset); }
    public int ToByteIndex(int componentIndex)
    { CheckComponentRange(componentIndex, 1); return _components[componentIndex].Offset; }

    /**
     * Return a duplicate of the {@link ByteBuf} on the specified component index.
     * <p>
     * Note that this method returns a shallow duplicate of the underlying component buffer.
     * The returned buffer's {@code readerIndex} and {@code writerIndex} will be independent of the
     * composite buffer's indices and will not be adjusted to reflect the component's view within
     * the composite buffer.
     * <p>
     * If you need a buffer that represents the component's readable view as seen from the composite
     * buffer, use {@link #componentSlice(int cIndex)} instead.
     *
     * @param cIndex the index for which the {@link ByteBuf} should be returned
     * @return a duplicate of the underlying {@link ByteBuf} on the specified index
     */
    public ByteBuf Component(int componentIndex)
    { CheckComponentRange(componentIndex, 1); return _components[componentIndex].Source.Duplicate(); }
    /**
     * Return a slice of the {@link ByteBuf} on the specified component index.
     * <p>
     * This method provides a view of the component that reflects its state within the composite buffer.
     * The returned buffer's readable bytes will correspond to the bytes that this component
     * contributes to the composite buffer's capacity. The slice will have its own independent
     * {@code readerIndex} and {@code writerIndex}, starting at {@code 0}.
     *
     * @param cIndex the index for which the sliced {@link ByteBuf} should be returned
     * @return a sliced {@link ByteBuf} representing the component's view
     */
    /// <remarks>Returns the shared cached borrowed view, as in Netty. Repeated
    /// calls do not reset its indices. A whole-source component can return the
    /// source itself; retain the result before taking independent ownership.</remarks>
    public ByteBuf ComponentSlice(int componentIndex)
    {
        CheckComponentRange(componentIndex, 1);
        ComponentEntry component = _components[componentIndex];
        return component.Slice();
    }
    /**
     * Return the {@link ByteBuf} on the specified index
     *
     * @param offset the offset for which the {@link ByteBuf} should be returned
     * @return the {@link ByteBuf} on the specified index
     */
    public ByteBuf ComponentAtOffset(int offset) => Component(ToComponentIndex(offset));

    /**
     * Same with {@link #slice(int, int)} except that this method returns a list.
     */
    public virtual IReadOnlyList<ByteBuf> Decompose(int offset, int length)
    {
        CheckIndex(offset, length);
        if (length == 0) return Array.Empty<ByteBuf>();
        var result = new List<ByteBuf>();
        // The first component
        int componentIndex = FindComponentIndex(offset);
        // Add all the slices until there is nothing more left and then return the List.
        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - offset, length);
            // It's important to use srcBuf and NOT buf as we need to return the "original" source buffer and not the
            // unwrapped one as otherwise we could loose the ability to correctly update the reference count on the
            // returned buffer.
            result.Add(component.Source.Slice(component.SourceIndex + offset - component.Offset, count));
            offset += count; length -= count;
        }
        return result;
    }

    public override int Capacity
    {
        get => _components.Count == 0 ? 0 : _components[^1].EndOffset;
        set
        {
            CheckNewCapacity(value);
            int oldCapacity = Capacity;
            if (value == oldCapacity) return;
            if (value > oldCapacity)
            {
                int paddingLength = value - oldCapacity;
                ByteBuf padding = AllocateBuffer(paddingLength);
                padding.WriterIndex = paddingLength;
                // FIXME: No need to create a padding buffer and consolidate.
                // Just create a big single buffer and put the current content there.
                // CLR: retain the original growth policy; allocation failures preserve owned components.
                // Netty uses addComponent0: public insertion overrides must not intercept padding ownership.
                AddComponentCore(_components.Count, padding, false);
                ConsolidateIfNeeded();
                return;
            }
            int bytesToTrim = oldCapacity - value;
            for (int i = _components.Count - 1; i >= 0; --i)
            {
                ComponentEntry component = _components[i];
                if (bytesToTrim < component.Length)
                {
                    // Trim the last component
                    component.Trim(0, component.Length - bytesToTrim);
                    break;
                }
                bytesToTrim -= component.Length;
                component.Free(); _components.RemoveAt(i);
            }
            UpdateOffsets();
            TrimIndicesToCapacity(value);
        }
    }

    /**
     * Consolidate the composed {@link ByteBuf}s
     */
    public virtual CompositeByteBuf Consolidate() => ConsolidateCore(0, _components.Count);
    /**
     * Consolidate the composed {@link ByteBuf}s
     *
     * @param cIndex the index on which to start to compose
     * @param numComponents the number of components to compose
     */
    public virtual CompositeByteBuf Consolidate(int componentIndex, int numComponents)
        => ConsolidateCore(componentIndex, numComponents);

    // Netty's consolidate0 is private. Automatic/whole consolidation must not invoke public range overrides.
    private CompositeByteBuf ConsolidateCore(int componentIndex, int numComponents)
    {
        CheckComponentRange(componentIndex, numComponents);
        if (numComponents <= 1) return this;
        int length = _components[componentIndex + numComponents - 1].EndOffset - _components[componentIndex].Offset;
        var originals = _components.GetRange(componentIndex, numComponents);
        ByteBuf replacement = AllocateBuffer(length);
        ComponentEntry replacementEntry;
        try
        {
            for (int i = componentIndex; i < componentIndex + numComponents; ++i)
            {
                ComponentEntry component = _components[i];
                replacement.SetBytes(replacement.WriterIndex, component.Source, component.SourceIndex, component.Length);
                replacement.WriterIndex += component.Length;
            }
            replacementEntry = new ComponentEntry(replacement, 0, length);
        }
        catch { replacement.Release(); throw; }
        // copy then release
        // CLR: prepare the entire replacement before publishing/releasing, preserving
        // the original layout and ownership if allocation or a source read fails.
        _components[componentIndex] = replacementEntry;
        _components.RemoveRange(componentIndex + 1, numComponents - 1);
        UpdateOffsets();
        foreach (ComponentEntry component in originals) component.Free();
        return this;
    }

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        ByteBuf result = AllocateBuffer(length);
        try { if (length != 0) GetBufferComponents(index, result, 0, length); result.WriterIndex = length; return result; }
        catch { result.Release(); throw; }
    }

    /**
     * Discard all {@link ByteBuf}s which are read.
     */
    public virtual CompositeByteBuf DiscardReadComponents()
    {
        EnsureAccessible();
        int readerIndex = ReaderIndex;
        if (readerIndex == 0) return this;
        // Discard everything if (readerIndex = writerIndex = capacity).
        if (readerIndex == WriterIndex && WriterIndex == Capacity)
        { RemoveComponentsCore(0, _components.Count); SetIndex(0, 0); AdjustMarkers(readerIndex); return this; }
        // Remove read components.
        int first = FindComponentIndex(readerIndex);
        if (first == 0) return this; // Nothing to discard
        int offset = _components[first].Offset;
        RemoveComponentsCore(0, first);
        // Update indexes and markers.
        SetIndex(readerIndex - offset, WriterIndex - offset); AdjustMarkers(offset);
        return this;
    }

    public override CompositeByteBuf DiscardReadBytes()
    {
        EnsureAccessible();
        int readerIndex = ReaderIndex;
        if (readerIndex == 0) return this;
        // Discard everything if (readerIndex = writerIndex = capacity).
        if (readerIndex == WriterIndex && WriterIndex == Capacity)
        { RemoveComponentsCore(0, _components.Count); SetIndex(0, 0); AdjustMarkers(readerIndex); return this; }
        int first = FindComponentIndex(readerIndex);
        ComponentEntry component = _components[first];
        // Replace the first readable component with a new slice.
        // We must replace the cached slice with a derived one to ensure that
        // it can later be released properly in the case of PooledSlicedByteBuf.
        // CLR: update captured coordinates and replace any cached borrowed view together.
        int trimmedBytes = readerIndex - component.Offset;
        component.Trim(trimmedBytes, component.Length - trimmedBytes);
        RemoveComponentsCore(0, first);
        // Update indexes and markers.
        SetIndex(0, WriterIndex - readerIndex); AdjustMarkers(readerIndex);
        return this;
    }
    public override CompositeByteBuf DiscardSomeReadBytes() => DiscardReadComponents();

    protected override Memory<byte> GetMemoryCore(int index, int length)
        => TryGetMemoryCore(index, length, out var memory) ? memory
            : throw new NotSupportedException("The range spans multiple components. Use byte transfers or consolidate first.");
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => TryGetReadOnlyMemoryCore(index, length, out var memory) ? memory
            : throw new NotSupportedException("The range spans multiple components. Use AsReadOnlySequence or byte transfers.");
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    {
        if (length == 0) { memory = Memory<byte>.Empty; return true; }
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (length <= component.EndOffset - index)
            return component.Source.TryGetMemory(component.SourceIndex + index - component.Offset, length, out memory);
        memory = default; return false;
    }
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        if (length == 0) { memory = ReadOnlyMemory<byte>.Empty; return true; }
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (length <= component.EndOffset - index)
            return component.Source.TryGetReadOnlyMemory(component.SourceIndex + index - component.Offset, length, out memory);
        memory = default; return false;
    }

    protected override void GetBytesCore(int index, Span<byte> destination)
    {
        if (destination.IsEmpty) return;
        int componentIndex = FindComponentIndex(index);
        while (!destination.IsEmpty)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, destination.Length);
            component.Source.GetBytes(component.SourceIndex + index - component.Offset, destination[..count]);
            index += count; destination = destination[count..];
        }
    }
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return;
        int componentIndex = FindComponentIndex(index);
        while (!source.IsEmpty)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, source.Length);
            component.Source.SetBytes(component.SourceIndex + index - component.Offset, source[..count]);
            index += count; source = source[count..];
        }
    }
    protected override void SetZeroCore(int index, int length)
    {
        if (length == 0) return;
        int componentIndex = FindComponentIndex(index);
        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, length);
            component.Source.SetZero(component.SourceIndex + index - component.Offset, count);
            index += count; length -= count;
        }
    }

    internal override bool SupportsDirectBulkCopy => false;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    { CheckIndex(index, length); return CreateReadOnlySequence(index, length, true); }
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => CreateReadOnlySequence(index, length, false);
    private ReadOnlySequence<byte> CreateReadOnlySequence(int index, int length, bool copying)
    {
        if (length == 0) return ReadOnlySequence<byte>.Empty;
        SequenceSegment first = null, last = null;
        int componentIndex = FindComponentIndex(index);
        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, length);
            ReadOnlySequence<byte> sequence = copying ? component.Source.BorrowCopySequence(component.SourceIndex + index - component.Offset, count)
                : component.Source.AsReadOnlySequence(component.SourceIndex + index - component.Offset, count);
            foreach (ReadOnlyMemory<byte> memory in sequence)
            {
                if (memory.IsEmpty) continue;
                var next = new SequenceSegment(memory);
                if (last == null) first = next;
                else last.Append(next);
                last = next;
            }
            index += count; length -= count;
        }
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    internal override BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();
        var leases = new BufferMemoryLease[_components.Count];
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
        // CLR clears component references on release; preserve the original count for freed diagnostics.
        _deallocatedComponentCount = _components.Count;
        // We're not using foreach to avoid creating an iterator.
        // see https://github.com/netty/netty/issues/2642
        for (int i = 0; i < _components.Count; ++i) _components[i].Free();
        _components.Clear();
    }

    private ByteBuf AllocateBuffer(int capacity)
        => _direct ? Allocator.DirectBuffer(capacity, MaxCapacity) : Allocator.HeapBuffer(capacity, MaxCapacity);
    private void CheckComponentRange(int index, int count)
    {
        EnsureAccessible();
        if (index < 0 || count < 0 || index > _components.Count - count) throw new ArgumentOutOfRangeException(nameof(index));
    }
    private int FindComponentIndex(int offset)
    {
        // fast-path zero offset
        // fast-path for 1 and 2 component count
        // CLR: the original JVM branch rationale is retained; use one bounded
        // binary search instead of reproducing its separate dispatch paths.
        // Binary search skips zero-length components at a boundary.
        int low = 0, high = _components.Count - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >>> 1);
            ComponentEntry component = _components[mid];
            if (offset >= component.EndOffset) low = mid + 1;
            else if (offset < component.Offset) high = mid - 1;
            else return mid;
        }
        throw new ArgumentOutOfRangeException(nameof(offset));
    }
    private void UpdateOffsets()
    {
        int offset = 0;
        foreach (ComponentEntry component in _components)
        { component.Offset = offset; offset += component.Length; }
    }
    private sealed class ComponentEntry
    {
        internal readonly ByteBuf Source; // the originally added buffer
        internal int SourceIndex; // captured readable start within the original source
        internal int Offset; // offset of this component within this CompositeByteBuf
        internal int Length;
        private ByteBuf _slice;
        internal int EndOffset => Offset + Length;
        internal ComponentEntry(ByteBuf source, int sourceIndex, int length, bool reuseWholeSource = true)
        {
            Source = source; SourceIndex = sourceIndex; Length = length;
            // We don't need to slice later to expose the internal component if the readable range
            // is already the entire buffer
            if (reuseWholeSource && sourceIndex == 0 && length == source.Capacity) _slice = source;
        }
        internal ByteBuf Slice() => _slice ??= Source.Slice(SourceIndex, Length);
        internal void Trim(int start, int length)
        {
            // We must replace the cached slice with a derived one to ensure that
            // it can later be released properly in the case of PooledSlicedByteBuf.
            ByteBuf replacement = _slice?.Slice(start, length);
            SourceIndex += start;
            Length = length;
            _slice = replacement;
        }
        internal void Free()
        {
            _slice = null;
            // Release the original buffer since it may have a different
            // refcount to the unwrapped buf (e.g. if PooledSlicedByteBuf)
            Source.Release();
        }
    }
    private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
    {
        internal SequenceSegment(ReadOnlyMemory<byte> memory) => Memory = memory;
        internal void Append(SequenceSegment next)
        { next.RunningIndex = RunningIndex + Memory.Length; Next = next; }
    }
}
