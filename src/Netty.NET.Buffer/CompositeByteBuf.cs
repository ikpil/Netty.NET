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
using System.Collections;
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
public class CompositeByteBuf : AbstractReferenceCountedByteBuf, IEnumerable<ByteBuf>
{
    /**
     * Add the given {@link ByteBuf}s.
     * <p>
     * Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuf}.
     * If you need to have it increased use {@link #addComponents(boolean, ByteBuf[])}.
     * <p>
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param buffers the {@link ByteBuf}s to add. {@link ByteBuf#release()} ownership of all {@link ByteBuf#release()}
     * ownership of all {@link ByteBuf} objects is transferred to this {@link CompositeByteBuf}.
     */
    public virtual CompositeByteBuf AddComponents(params ByteBuf[] buffers)
    {
        return AddComponents(buffers, false);
    }

    /**
     * Add the given {@link ByteBuf}s and increase the {@code writerIndex} if {@code increaseWriterIndex} is
     * {@code true}.
     *
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param buffers the {@link ByteBuf}s to add. {@link ByteBuf#release()} ownership of all {@link ByteBuf#release()}
     * ownership of all {@link ByteBuf} objects is transferred to this {@link CompositeByteBuf}.
     */
    public virtual CompositeByteBuf AddComponents(ByteBuf[] buffers, bool increaseWriterIndex)
    {
        return AddComponentsCore(_components.Count, buffers, 0, increaseWriterIndex);
    }

    /**
     * Add the given {@link ByteBuf}s on the specific index
     * <p>
     * Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuf}.
     * If you need to have it increased you need to handle it by your own.
     * <p>
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param cIndex the index on which the {@link ByteBuf} will be added. {@link ByteBuf#release()} ownership of all
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects is transferred to this
     * {@link CompositeByteBuf}.
     * @param buffers the {@link ByteBuf}s to add. {@link ByteBuf#release()} ownership of all {@link ByteBuf#release()}
     * ownership of all {@link ByteBuf} objects is transferred to this {@link CompositeByteBuf}.
     */
    /// <remarks>Array capacity/index/cycle preflight fails before ownership transfer.
    /// Null ends additions; remaining entries are safely released. A later insertion
    /// failure keeps the successful prefix, including its writer-index increase.
    /// Consolidation runs once after the complete batch.</remarks>
    public virtual CompositeByteBuf AddComponents(int componentIndex, ByteBuf[] buffers, bool increaseWriterIndex = false)
    {
        return AddComponentsCore(componentIndex, buffers, 0, increaseWriterIndex);
    }

    private CompositeByteBuf AddComponentsCore(int componentIndex, ByteBuf[] buffers, int offset, bool increaseWriterIndex)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        int readableBytes = 0;
        int capacity = Capacity;
        for (int i = offset; i < buffers.Length; ++i)
        {
            ByteBuf buffer = buffers[i];
            if (buffer == null)
            {
                break;
            }
            int length = buffer.ReadableBytes;
            // Check if we would overflow.
            // See https://github.com/netty/netty/issues/10194
            if (length < 0 || length > MaxCapacity - capacity - readableBytes)
                throw new ArgumentOutOfRangeException(nameof(buffers));
            readableBytes += length;
        }
        CheckComponentRange(componentIndex, 0);
        // CLR: cyclic references, including discarded tails, cannot transfer ownership.
        for (int i = offset; i < buffers.Length; ++i)
        {
            ByteBuf buffer = buffers[i];
            if (buffer != null && ContainsBuffer(buffer, this))
                throw new ArgumentException("Component ownership must be acyclic.", nameof(buffers));
        }

        // only set ci after we've shifted so that finally block logic is always correct
        // CLR: List insertion needs no shifted null slots. Track the next unconsumed
        // entry explicitly; AddComponentCore consumes a failing entry itself.
        int next = offset, addedBytes = 0;
        try
        {
            while (next < buffers.Length)
            {
                ByteBuf buffer = buffers[next++];
                if (buffer == null)
                {
                    break;
                }
                // will increase componentCount
                addedBytes += AddComponentCore(componentIndex++, buffer, false);
            }
        }
        finally
        {
            // ci is now the index following the last successfully added component
            // we bailed early
            for (; next < buffers.Length; ++next)
            {
                ReferenceCountUtil.SafeRelease(buffers[next]);
            }

            // only need to do this here for components after the added ones
            // CLR: each List insertion has already updated offsets; only indices remain.
            if (increaseWriterIndex && addedBytes != 0)
            {
                WriterIndex += addedBytes;
            }
        }
        ConsolidateIfNeeded();
        return this;
    }

    // CLR: factory-only offset entry points avoid copying arrays or replacing
    // the original array preflight with streaming ownership semantics.
    internal CompositeByteBuf AddWrappedComponents(ByteBuf[] buffers, int offset)
    {
        return AddComponentsCore(0, buffers, offset, true);
    }

    internal CompositeByteBuf AddWrappedArrays(byte[][] arrays, int offset)
    {
        // No need for consolidation
        // CLR: the Java ByteWrapper<byte[]> strategy is a direct byte-array loop.
        // Delay consolidation until every nonempty array has been wrapped.
        for (int i = offset; i < arrays.Length; ++i)
        {
            byte[] array = arrays[i];
            if (array == null)
            {
                break;
            }
            if (array.Length != 0)
            {
                AddComponentCore(_components.Count, Unpooled.WrappedBuffer(array), false);
            }
        }
        ConsolidateIfNeeded();
        WriterIndex = Capacity;
        return this;
    }

    // support passing arrays of other types instead of having to copy to a ByteBuf[] first
    // CLR: cached static delegates replace Java ByteWrapper strategies for the two Memory types.
    internal CompositeByteBuf AddWrappedMemory<T>(T[] buffers, int offset,
        Func<T, bool> isEmpty, Func<T, ByteBuf> wrap) where T : struct
    {
        // No need for consolidation
        // CLR: preserve the original batch boundary, rather than consolidating each insertion.
        for (int i = offset; i < buffers.Length; ++i)
            if (!isEmpty(buffers[i]))
            {
                AddComponentCore(_components.Count, wrap(buffers[i]), false);
            }
        ConsolidateIfNeeded();
        WriterIndex = Capacity;
        return this;
    }

    /**
     * Add the given {@link ByteBuf}s.
     * <p>
     * Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuf}.
     * If you need to have it increased use {@link #addComponents(boolean, Iterable)}.
     * <p>
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param buffers the {@link ByteBuf}s to add. {@link ByteBuf#release()} ownership of all {@link ByteBuf#release()}
     * ownership of all {@link ByteBuf} objects is transferred to this {@link CompositeByteBuf}.
     */
    /**
     * Add the given {@link ByteBuf}s and increase the {@code writerIndex} if {@code increaseWriterIndex} is
     * {@code true}.
     *
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param buffers the {@link ByteBuf}s to add. {@link ByteBuf#release()} ownership of all {@link ByteBuf#release()}
     * ownership of all {@link ByteBuf} objects is transferred to this {@link CompositeByteBuf}.
     */
    public virtual CompositeByteBuf AddComponents(IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
    {
        return AddComponentsCore(_components.Count, buffers, increaseWriterIndex);
    }

    /**
     * Add the given {@link ByteBuf}s on the specific index
     *
     * Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuf}.
     * If you need to have it increased you need to handle it by your own.
     * <p>
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param cIndex the index on which the {@link ByteBuf} will be added.
     * @param buffers the {@link ByteBuf}s to add.  {@link ByteBuf#release()} ownership of all
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects is transferred to this
     * {@link CompositeByteBuf}.
     */
    /// <remarks>Streaming insertion keeps the successful prefix on failure. Null
    /// ends additions; remaining yielded entries are safely released. Enumeration
    /// and disposal follow the supplied IEnumerator's CLR contract.</remarks>
    public virtual CompositeByteBuf AddComponents(int componentIndex, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
    {
        return AddComponentsCore(componentIndex, buffers, increaseWriterIndex);
    }

    // The original iterable kernel is private, including its use during construction.
    private CompositeByteBuf AddComponentsCore(int componentIndex, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex)
    {
        // TODO optimize further, similar to ByteBuf[] version
        // (difference here is that we don't know *always* know precise size increase in advance,
        // but we do in the most common case that the Iterable is a Collection)
        // CLR: preserve streaming ownership even for ICollection; no materialization/preflight.
        if (buffers is ByteBuf buffer)
        {
            // If buffers also implements ByteBuf (e.g. CompositeByteBuf), it has to go to addComponent(ByteBuf).
            return AddComponent(componentIndex, buffer, increaseWriterIndex);
        }
        ArgumentNullException.ThrowIfNull(buffers);
        using IEnumerator<ByteBuf> iterator = buffers.GetEnumerator();
        try
        {
            CheckComponentRange(componentIndex, 0);
            // No need for consolidation
            while (iterator.MoveNext())
            {
                ByteBuf next = iterator.Current;
                if (next == null)
                {
                    break;
                }
                AddComponentCore(componentIndex++, next, increaseWriterIndex);
            }
        }
        finally
        {
            while (iterator.MoveNext())
            {
                ByteBuf next = iterator.Current;
                // CLR: rejected cyclic references remain caller-owned, including in cleanup.
                if (next != null && !ContainsBuffer(next, this))
                {
                    ReferenceCountUtil.SafeRelease(next);
                }
            }
        }
        ConsolidateIfNeeded();
        return this;
    }

    /**
     * Add the given {@link ByteBuf} and increase the {@code writerIndex} if {@code increaseWriterIndex} is
     * {@code true}. If the provided buffer is a {@link CompositeByteBuf} itself, a "shallow copy" of its
     * readable components will be performed. Thus the actual number of new components added may vary
     * and in particular will be zero if the provided buffer is not readable.
     * <p>
     * {@link ByteBuf#release()} ownership of {@code buffer} is transferred to this {@link CompositeByteBuf}.
     * @param buffer the {@link ByteBuf} to add. {@link ByteBuf#release()} ownership is transferred to this
     * {@link CompositeByteBuf}.
     */
    /// <remarks>Only an actual CompositeByteBuf is flattened, one level deep.
    /// Derived/read-only views remain a single component to preserve their semantics.
    /// A composite source stays caller-owned on failure; newly retained components
    /// are rolled back. Successful transfer releases the source's existing reference.</remarks>
    public virtual CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (ContainsBuffer(buffer, this))
        {
            throw new ArgumentException("Component ownership must be acyclic.", nameof(buffer));
        }
        EnsureAccessible();
        int readerIndex = buffer.ReaderIndex, writerIndex = buffer.WriterIndex;
        if (readerIndex == writerIndex)
        {
            buffer.Release();
            return this;
        }
        if (buffer is not CompositeByteBuf source)
        {
            AddComponentCore(_components.Count, buffer, increaseWriterIndex);
            ConsolidateIfNeeded();
            return this;
        }

        source.CheckIndex(readerIndex, writerIndex - readerIndex);
        // CLR: checked capacity prevents the original unchecked flatten-offset overflow.
        int length = writerIndex - readerIndex;
        if (length > MaxCapacity - Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }
        int countBefore = _components.Count, writerBefore = WriterIndex;
        int first = source.FindComponentIndex(readerIndex);
        var additions = new List<ComponentEntry>(source._components.Count - first);
        bool committed = false;
        try
        {
            for (int i = first; i < source._components.Count; ++i)
            {
                ComponentEntry component = source._components[i];
                int fromIndex = Math.Max(readerIndex, component.Offset);
                int toIndex = Math.Min(writerIndex, component.EndOffset);
                int count = toIndex - fromIndex;
                if (count > 0) // skip empty components
                {
                    var addition = new ComponentEntry(component.Source,
                        component.SourceIndex + (fromIndex - component.Offset), count, reuseWholeSource: false);
                    // Retain the original source, not an unwrapped parent with possibly different ownership.
                    component.Source.Retain();
                    additions.Add(addition); // capacity is reserved before any retain
                }
                if (writerIndex == toIndex)
                {
                    break;
                }
            }
            _components.EnsureCapacity(checked(countBefore + additions.Count));
            _components.AddRange(additions);
            UpdateOffsets();
            if (increaseWriterIndex)
            {
                WriterIndex = writerBefore + length;
            }
            ConsolidateIfNeeded();
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                // if we did not succeed, attempt to rollback any components that were added
                if (_components.Count > countBefore)
                {
                    _components.RemoveRange(countBefore, _components.Count - countBefore);
                }
                UpdateOffsets();
                if (increaseWriterIndex)
                {
                    WriterIndex = writerBefore;
                }

                foreach (ComponentEntry addition in additions)
                {
                    addition.Free();
                }
            }
        }
        buffer.Release();
        return this;
    }

    /**
     * This should only be called as last operation from a method as this may adjust the underlying
     * array of components and so affect the index etc.
     */
    private void ConsolidateIfNeeded()
    {
        // Consolidate if the number of components will exceed the allowed maximum by the current
        // operation.
        if (_components.Count > MaxNumComponents)
        {
            ConsolidateCore(0, _components.Count);
        }
    }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        if (destination.IsEmpty)
        {
            return this;
        }
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (destination.Length <= component.EndOffset - index)
            component.Source.GetBytes(component.SourceIndex + (index - component.Offset), destination);
        else
        {
            // CLR snapshots preserve memmove semantics across aliased components.
            byte[] bytes = new byte[destination.Length];
            GetBytesCore(index, bytes);
            bytes.AsSpan().CopyTo(destination);
        }
        return this;
    }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        destination.EnsureCanWrite();
        destination.CheckIndex(destinationIndex, length);
        if (length == 0)
            return this;
        if (BulkRangesOverlap(this, index, destination, destinationIndex, length))
        {
            ByteBuf snapshot = new UnpooledHeapByteBuf(length);
            try
            {
                GetBufferComponents(index, snapshot, 0, length);
                destination.SetBytes(destinationIndex, snapshot, 0, length);
            }
            finally
            {
                snapshot.Release();
            }
        }
        else
            GetBufferComponents(index, destination, destinationIndex, length);
        return this;
    }

    private void GetBufferComponents(int index, ByteBuf destination, int destinationIndex, int length)
    {
        CheckIndex(index, length);
        int componentIndex = FindComponentIndex(index);

        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(length, component.EndOffset - index);
            component.Source.GetBytes(component.SourceIndex + (index - component.Offset), destination, destinationIndex, count);
            index += count;
            destinationIndex += count;
            length -= count;
        }
    }

    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index, length);
        source.CheckIndex(sourceIndex, length);
        if (length == 0)
            return this;
        if (BulkRangesOverlap(source, sourceIndex, this, index, length))
        {
            ByteBuf snapshot = new UnpooledHeapByteBuf(length);
            try
            {
                snapshot.SetBytes(0, source, sourceIndex, length);
                SetBufferComponents(index, snapshot, 0, length);
            }
            finally
            {
                snapshot.Release();
            }
        }
        else
            SetBufferComponents(index, source, sourceIndex, length);
        return this;
    }

    private void SetBufferComponents(int index, ByteBuf source, int sourceIndex, int length)
    {
        int componentIndex = FindComponentIndex(index);

        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(length, component.EndOffset - index);
            component.Source.SetBytes(component.SourceIndex + (index - component.Offset), source, sourceIndex, count);
            index += count;
            sourceIndex += count;
            length -= count;
        }
    }

    /// <summary>Creates a composite adopting the supplied buffer references and their captured readable ranges.</summary>
    /// <remarks>Array preflight failures leave inputs caller-owned. Once insertion begins,
    /// failure releases acquired components and the unvisited tail, following AddComponents.
    /// Null terminates insertion. Consolidation runs once after the batch; all bytes are readable.</remarks>
    public CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents, params ByteBuf[] buffers)
        : this(allocator, direct, maxNumComponents, buffers, 0)
    {
    }

    // Original package-private array constructor. Only the suffix participates in
    // capacity preflight and ownership transfer; preceding entries remain untouched.
    internal CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents,
        ByteBuf[] buffers, int offset)
        : this(allocator, direct, maxNumComponents)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(buffers);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffers.Length);
            AddComponentsCore(0, buffers, offset, false);
            SetIndex(0, Capacity);
        }
        catch
        {
            ReleaseFailedConstruction();
            throw;
        }
    }

    /// <summary>Creates a composite adopting references from an enumerable without materializing it.</summary>
    /// <remarks>Streaming ownership, null termination and enumerator disposal follow AddComponents.
    /// Failure releases acquired components; buffers not yielded by a failing enumerator remain caller-owned.
    /// A composite passed as IEnumerable is adopted as one buffer rather than enumerating its components.</remarks>
    public CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents, IEnumerable<ByteBuf> buffers)
        : this(allocator, direct, maxNumComponents)
    {
        try
        {
            AddComponentsCore(0, buffers, false);
            SetIndex(0, Capacity);
        }
        catch
        {
            ReleaseFailedConstruction();
            throw;
        }
    }

    private void ReleaseFailedConstruction()
    {
        // CLR: a failed constructor cannot return its partial composite for caller cleanup.
        // Release only references already acquired; do not invoke a derived Deallocate
        // callback before the derived constructor has initialized its state.
        SetReferenceCount(0);

        for (int i = 0; i < _components.Count; ++i)
        {
            ReferenceCountUtil.SafeRelease(_components[i].Source);
        }

        _components.Clear();
    }

    // resized when needed
    private readonly List<ComponentEntry> _components;
    private int _deallocatedComponentCount;
    private readonly bool _direct;
    private readonly IByteBufAllocator _bufferAllocator;
    public override IByteBufAllocator Alloc()
    {
        return _bufferAllocator;
    }

    public override string ToString()
    {
        string result = base.ToString();
        int count = RefCnt == 0 ? _deallocatedComponentCount : NumComponents;
        return FormattableString.Invariant($"{result[..^1]}, components={count})");
    }

    // CLR: List replaces Java component-array growth.
    // The allocation policy affects padding, copies and consolidation. IsDirect
    // describes the actual components, including nested composites.
    public CompositeByteBuf(int maxNumComponents = 16, bool direct = false, NativeMemoryAllocator allocator = null)
        : this(allocator == null ? UnpooledByteBufAllocator.Default : new UnpooledByteBufAllocator(direct, allocator),
            direct, maxNumComponents)
    {
    }

    public CompositeByteBuf(IByteBufAllocator allocator, bool direct, int maxNumComponents = 16)
        : base(int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        if (maxNumComponents < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNumComponents));
        }
        MaxNumComponents = maxNumComponents;
        _components = new();
        _direct = direct;
        _bufferAllocator = allocator;
    }

    // CLR: transparent composite wrappers use the same component metadata and index
    // owner, without an empty side-list. This also makes existing flatten/enumeration
    // kernels see the real components. Reference ownership stays with the wrapped buffer.
    // Special constructor used by WrappedCompositeByteBuf
    private protected CompositeByteBuf(CompositeByteBuf wrapped) : base(wrapped)
    {
        _components = wrapped._components;
        _direct = wrapped._direct;
        _bufferAllocator = wrapped.Alloc();
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
            if (_components.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < _components.Count; ++i)
                if (!_components[i].Source.IsDirect)
                {
                    return false;
                }
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
    {
        return AddComponent(_components.Count, buffer, increaseWriterIndex);
    }

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

    /**
     * Precondition is that {@code buffer != null}.
     */
    private int AddComponentCore(int componentIndex, ByteBuf buffer, bool increaseWriterIndex)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        // CLR: cycles have no valid reference-counted ownership. Reject them before consuming a reference.
        if (ContainsBuffer(buffer, this))
        {
            throw new ArgumentException("Component ownership must be acyclic.", nameof(buffer));
        }
        bool wasAdded = false;
        try
        {
            CheckComponentRange(componentIndex, 0);
            int length = buffer.ReadableBytes;
            buffer.TryGetReadOnlyMemory(buffer.ReaderIndex, length, out _);
            // No need to consolidate - just add a component to the list.
            // Check if we would overflow.
            // See https://github.com/netty/netty/issues/10194
            if (length > MaxCapacity - Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(buffer));
            }
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
            if (increaseWriterIndex)
            {
                WriterIndex += length;
            }
            return length;
        }
        finally
        {
            if (!wasAdded)
            {
                buffer.Release();
            }
        }
    }

    private static bool ContainsBuffer(ByteBuf buffer, ByteBuf target)
    {
        if (ReferenceEquals(buffer, target))
        {
            return true;
        }
        if (buffer is CompositeByteBuf composite)
        {
            // Wrapper aliases represent the same component owner; reject ownership
            // cycles before consuming a reference, including raw/wrapped aliases.
            if (target is CompositeByteBuf targetComposite &&
                ReferenceEquals(composite._components, targetComposite._components))
            {
                return true;
            }
            foreach (ComponentEntry entry in composite._components)
                if (ContainsBuffer(entry.Source, target))
                {
                    return true;
                }
        }
        ByteBuf parent = buffer.Unwrap();
        return parent != null && ContainsBuffer(parent, target);
    }

    /**
     * Remove the {@link ByteBuf} from the given index.
     *
     * @param cIndex the index on from which the {@link ByteBuf} will be remove
     */
    public virtual CompositeByteBuf RemoveComponent(int componentIndex)
    {
        return RemoveComponentsCore(componentIndex, 1);
    }

    /**
     * Remove the number of {@link ByteBuf}s starting from the given index.
     *
     * @param cIndex the index on which the {@link ByteBuf}s will be started to removed
     * @param numComponents the number of components to remove
     */
    public virtual CompositeByteBuf RemoveComponents(int componentIndex, int numComponents)
    {
        return RemoveComponentsCore(componentIndex, numComponents);
    }

    private CompositeByteBuf RemoveComponentsCore(int componentIndex, int numComponents)
    {
        CheckComponentRange(componentIndex, numComponents);

        for (int i = componentIndex; i < componentIndex + numComponents; ++i)
        {
            _components[i].Free();
        }

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
    public int ToComponentIndex(int offset)
    {
        CheckIndex(offset, 1);
        return FindComponentIndex(offset);
    }

    public int ToByteIndex(int componentIndex)
    {
        CheckComponentRange(componentIndex, 1);
        return _components[componentIndex].Offset;
    }

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
    {
        CheckComponentRange(componentIndex, 1);
        // It's important to use srcBuf and NOT buf as we need to return the "original" source buffer and not the
        // unwrapped one as otherwise we could loose the ability to correctly update the reference count on the
        // returned buffer.
        return _components[componentIndex].Source.Duplicate();
    }

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
    public ByteBuf ComponentAtOffset(int offset)
    {
        return Component(ToComponentIndex(offset));
    }

    /**
     * Same with {@link #slice(int, int)} except that this method returns a list.
     */
    public virtual IReadOnlyList<ByteBuf> Decompose(int offset, int length)
    {
        CheckIndex(offset, length);
        if (length == 0)
        {
            return Array.Empty<ByteBuf>();
        }
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
            result.Add(component.Source.Slice(component.SourceIndex + (offset - component.Offset), count));
            offset += count;
            length -= count;
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
            if (value == oldCapacity)
            {
                return;
            }
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
            int bytesToTrim = oldCapacity - value, keepCount = _components.Count;
            for (int i = _components.Count - 1; i >= 0; --i)
            {
                ComponentEntry component = _components[i];
                if (bytesToTrim < component.Length)
                {
                    // Trim the last component
                    // CLR: prepare its cached slice before returning any tail owner.
                    // Slice construction can throw; preserve the whole old layout,
                    // indices and references until that preparation succeeds.
                    component.Trim(0, component.Length - bytesToTrim);
                    break;
                }
                bytesToTrim -= component.Length;
                keepCount = i;
            }

            for (int i = _components.Count - 1; i >= keepCount; --i)
            {
                _components[i].Free();
            }

            _components.RemoveRange(keepCount, _components.Count - keepCount);
            UpdateOffsets();
            TrimIndicesToCapacity(value);
        }
    }

    /**
     * Consolidate the composed {@link ByteBuf}s
     */
    public virtual CompositeByteBuf Consolidate()
    {
        return ConsolidateCore(0, _components.Count);
    }

    /**
     * Consolidate the composed {@link ByteBuf}s
     *
     * @param cIndex the index on which to start to compose
     * @param numComponents the number of components to compose
     */
    public virtual CompositeByteBuf Consolidate(int componentIndex, int numComponents)
    {
        return ConsolidateCore(componentIndex, numComponents);
    }

    // Netty's consolidate0 is private. Automatic/whole consolidation must not invoke public range overrides.
    private CompositeByteBuf ConsolidateCore(int componentIndex, int numComponents)
    {
        CheckComponentRange(componentIndex, numComponents);
        if (numComponents <= 1)
        {
            return this;
        }
        int length = _components[componentIndex + numComponents - 1].EndOffset - _components[componentIndex].Offset;
        var originals = _components.GetRange(componentIndex, numComponents);
        ByteBuf replacement = AllocateBuffer(length);
        ComponentEntry replacementEntry;
        try
        {
            for (int i = componentIndex; i < componentIndex + numComponents; ++i)
            {
                ComponentEntry component = _components[i];
                replacement.WriteBytes(component.Source, component.SourceIndex, component.Length);
            }
            replacementEntry = new ComponentEntry(replacement, 0, length);
        }
        catch
        {
            replacement.Release();
            throw;
        }
        // copy then release
        // CLR: prepare the entire replacement before publishing/releasing, preserving
        // the original layout and ownership if allocation or a source read fails.
        _components[componentIndex] = replacementEntry;
        _components.RemoveRange(componentIndex + 1, numComponents - 1);
        UpdateOffsets();

        foreach (ComponentEntry component in originals)
        {
            component.Free();
        }

        return this;
    }

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        ByteBuf result = AllocateBuffer(length);
        try
        {
            if (length != 0)
            {
                GetBufferComponents(index, result, 0, length);
            }
            result.WriterIndex = length;
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
    }

    /**
     * Discard all {@link ByteBuf}s which are read.
     */
    public virtual CompositeByteBuf DiscardReadComponents()
    {
        EnsureAccessible();
        int readerIndex = ReaderIndex;
        if (readerIndex == 0)
            return this;
        // Discard everything if (readerIndex = writerIndex = capacity).
        if (readerIndex == WriterIndex && WriterIndex == Capacity)
        {
            RemoveComponentsCore(0, _components.Count);
            SetIndex(0, 0);
            AdjustMarkers(readerIndex);
            return this;
        }
        // Remove read components.
        int first = FindComponentIndex(readerIndex);
        if (first == 0)
            return this; // Nothing to discard
        int offset = _components[first].Offset;
        RemoveComponentsCore(0, first);
        // Update indexes and markers.
        SetIndex(readerIndex - offset, WriterIndex - offset);
        AdjustMarkers(offset);
        return this;
    }

    public override CompositeByteBuf DiscardReadBytes()
    {
        EnsureAccessible();
        int readerIndex = ReaderIndex;
        if (readerIndex == 0)
            return this;
        // Discard everything if (readerIndex = writerIndex = capacity).
        if (readerIndex == WriterIndex && WriterIndex == Capacity)
        {
            RemoveComponentsCore(0, _components.Count);
            SetIndex(0, 0);
            AdjustMarkers(readerIndex);
            return this;
        }
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
        SetIndex(0, WriterIndex - readerIndex);
        AdjustMarkers(readerIndex);
        return this;
    }

    public override CompositeByteBuf DiscardSomeReadBytes()
    {
        return DiscardReadComponents();
    }

    protected override Memory<byte> GetMemoryCore(int index, int length)
    {
        return TryGetMemoryCore(index, length, out var memory) ? memory
            : throw new NotSupportedException("The range spans multiple components. Use byte transfers or consolidate first.");
    }

    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
    {
        return TryGetReadOnlyMemoryCore(index, length, out var memory) ? memory
            : throw new NotSupportedException("The range spans multiple components. Use AsReadOnlySequence or byte transfers.");
    }

    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    {
        if (length == 0)
        {
            memory = Memory<byte>.Empty;
            return true;
        }
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (length <= component.EndOffset - index)
            return component.Source.TryGetMemory(component.SourceIndex + (index - component.Offset), length, out memory);
        memory = default;
        return false;
    }

    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        if (length == 0)
        {
            memory = ReadOnlyMemory<byte>.Empty;
            return true;
        }
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (length <= component.EndOffset - index)
            return component.Source.TryGetReadOnlyMemory(component.SourceIndex + (index - component.Offset), length, out memory);
        memory = default;
        return false;
    }

    protected override void GetBytesCore(int index, Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            return;
        }
        int componentIndex = FindComponentIndex(index);

        while (!destination.IsEmpty)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, destination.Length);
            component.Source.GetBytes(component.SourceIndex + (index - component.Offset), destination[..count]);
            index += count;
            destination = destination[count..];
        }
    }

    protected internal override void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    {
        EnsureCanWrite();
        CheckIndex(index, source.Length);
        if (source.IsEmpty)
            return;
        int componentIndex = FindComponentIndex(index);
        while (!source.IsEmpty)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, source.Length);
            // Skip empty buffer
            if (count == 0)
                continue;
            component.Source.SetBytesFromIoCore(component.SourceIndex + (index - component.Offset), source[..count]);
            index += count;
            source = source[count..];
        }
    }

    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            return;
        }
        int componentIndex = FindComponentIndex(index);

        while (!source.IsEmpty)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, source.Length);
            component.Source.SetBytes(component.SourceIndex + (index - component.Offset), source[..count]);
            index += count;
            source = source[count..];
        }
    }

    protected override void SetZeroCore(int index, int length)
    {
        if (length == 0)
        {
            return;
        }
        int componentIndex = FindComponentIndex(index);

        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, length);
            component.Source.SetZero(component.SourceIndex + (index - component.Offset), count);
            index += count;
            length -= count;
        }
    }

    internal override bool SupportsDirectBulkCopy => false;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    {
        CheckIndex(index, length);
        return CreateReadOnlySequence(index, length, true);
    }

    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
    {
        return CreateReadOnlySequence(index, length, false);
    }

    private ReadOnlySequence<byte> CreateReadOnlySequence(int index, int length, bool copying)
    {
        if (length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }
        SequenceSegment first = null, last = null;
        int componentIndex = FindComponentIndex(index);

        while (length > 0)
        {
            ComponentEntry component = _components[componentIndex++];
            int count = Math.Min(component.EndOffset - index, length);
            // Some buffers override this so we must use srcBuf
            ReadOnlySequence<byte> sequence = copying ? component.Source.BorrowCopySequence(component.SourceIndex + (index - component.Offset), count)
                : component.Source.AsReadOnlySequence(component.SourceIndex + (index - component.Offset), count);
            foreach (ReadOnlyMemory<byte> memory in sequence)
            {
                if (memory.IsEmpty)
                {
                    continue;
                }
                var next = new SequenceSegment(memory);
                if (last == null)
                {
                    first = next;
                }
                else
                {
                    last.Append(next);
                }
                last = next;
            }
            index += count;
            length -= count;
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
            for (; acquired < leases.Length; ++acquired)
            {
                leases[acquired] = _components[acquired].Source.AcquireReadLease();
            }

            return new BufferMemoryLease(leases);
        }
        catch
        {
            for (int i = acquired - 1; i >= 0; --i)
            {
                leases[i].Dispose();
            }

            throw;
        }
    }

    /**
     * Decreases the reference count by the specified {@code decrement} and deallocates this object if the reference
     * count reaches at {@code 0}. At this point it will also decrement the reference count of each internal
     * component by {@code 1}.
     *
     * @param decrement the number by which the reference count should be decreased
     * @return {@code true} if and only if the reference count became {@code 0} and this object has been deallocated
     */
    // CLR: inherited Release invokes this protected owner-lifetime hook.
    protected override void Deallocate()
    {
        // CLR clears component references on release; preserve the original count for freed diagnostics.
        _deallocatedComponentCount = _components.Count;

        // We're not using foreach to avoid creating an iterator.
        // see https://github.com/netty/netty/issues/2642
        for (int i = 0; i < _components.Count; ++i)
        {
            _components[i].Free();
        }

        _components.Clear();
    }

    private ByteBuf AllocateBuffer(int capacity)
    {
        return _direct ? Alloc().DirectBuffer(capacity, MaxCapacity) : Alloc().HeapBuffer(capacity, MaxCapacity);
    }

    private void CheckComponentRange(int index, int count)
    {
        EnsureAccessible();
        if (index < 0 || count < 0 || index > _components.Count - count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
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
            if (offset >= component.EndOffset)
            {
                low = mid + 1;
            }
            else if (offset < component.Offset)
            {
                high = mid - 1;
            }
            else
            {
                return mid;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(offset));
    }

    private void UpdateOffsets()
    {
        int offset = 0;

        foreach (ComponentEntry component in _components)
        {
            component.Offset = offset;
            offset += component.Length;
        }
    }
    private sealed class ComponentEntry
    {
        internal readonly ByteBuf Source; // the originally added buffer
        // index of the start of this CompositeByteBuf relative to srcBuf
        // CLR: store the captured source start and subtract Offset at each access.
        internal int SourceIndex;
        internal int Offset; // offset of this component within this CompositeByteBuf
        internal int Length;
        private ByteBuf _slice; // cached slice, may be null
        // end offset of this component within this CompositeByteBuf
        internal int EndOffset => Offset + Length;
        internal ComponentEntry(ByteBuf source, int sourceIndex, int length, bool reuseWholeSource = true)
        {
            Source = source;
            SourceIndex = sourceIndex;
            Length = length;
            // We don't need to slice later to expose the internal component if the readable range
            // is already the entire buffer
            if (reuseWholeSource && sourceIndex == 0 && length == source.Capacity)
                _slice = source;
        }

        internal ByteBuf Slice()
        {
            return _slice ??= Source.Slice(SourceIndex, Length);
        }

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
        internal SequenceSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        internal void Append(SequenceSegment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
        }
    }

    /// <summary>Enumerates borrowed component views, including empty components.</summary>
    /// <remarks>Views share the component cache and do not acquire references.
    /// Retain a view before keeping it beyond component removal or buffer release.
    /// As in Netty, a changed component count invalidates a nonempty enumerator;
    /// replacing components while keeping the same count is not detected.
    /// This is not a synchronization mechanism.</remarks>
    public virtual IEnumerator<ByteBuf> GetEnumerator()
    {
        EnsureAccessible();
        // CLR: the original empty iterator is independent of subsequent additions.
        return _components.Count == 0
            ? ((IEnumerable<ByteBuf>)Array.Empty<ByteBuf>()).GetEnumerator()
            : new ComponentEnumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Return the internal {@link ByteBuf} on the specified index. Note that updating the indexes of the returned
     * buffer will lead to an undefined behavior of this buffer.
     *
     * @param cIndex the index for which the {@link ByteBuf} should be returned
     */
    public ByteBuf InternalComponent(int componentIndex)
    {
        CheckComponentRange(componentIndex, 1);
        return _components[componentIndex].Slice();
    }

    /**
     * Return the internal {@link ByteBuf} on the specified offset. Note that updating the indexes of the returned
     * buffer will lead to an undefined behavior of this buffer.
     *
     * @param offset the offset for which the {@link ByteBuf} should be returned
     */
    public ByteBuf InternalComponentAtOffset(int offset)
    {
        return InternalComponent(ToComponentIndex(offset));
    }

    private sealed class ComponentEnumerator : IEnumerator<ByteBuf>
    {
        private readonly CompositeByteBuf _buffer;
        private readonly int _size;
        private int _index;
        private ByteBuf _current;
        private bool _disposed;

        internal ComponentEnumerator(CompositeByteBuf buffer)
        {
            _buffer = buffer;
            _size = buffer.NumComponents;
        }

        public ByteBuf Current => _current ?? throw new InvalidOperationException("The enumerator is not positioned on a component.");
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _current = null;
            if (_disposed)
            {
                return false;
            }
            // CLR: MoveNext combines Java hasNext/next. Count changes map to
            // InvalidOperationException, while ordinary exhaustion returns false.
            if (_size != _buffer.NumComponents)
                throw new InvalidOperationException("The component count changed during enumeration.");
            if (_index == _size)
            {
                return false;
            }
            _current = _buffer._components[_index++].Slice();
            return true;
        }

        // Read-Only
        // CLR: IEnumerator has no remove operation. Reset is unsupported;
        // Dispose ends enumeration without releasing any borrowed component.
        public void Reset()
        {
            throw new NotSupportedException("Read-Only");
        }

        public void Dispose()
        {
            _current = null;
            _disposed = true;
        }
    }

    internal override int ForEachByteAscending(int start, int end, Func<byte, bool> processor)
    {
        if (end <= start)
        {
            return -1;
        }
        int componentIndex = FindComponentIndex(start);
        while (start < end)
        {
            CheckIndex(start, end - start);
            ComponentEntry component = _components[componentIndex++];
            if (component.Length == 0)
            {
                continue; // empty
            }
            int count = Math.Min(end - start, component.EndOffset - start);
            int local = component.SourceIndex + (start - component.Offset);
            component.Source.CheckIndex(local, count);
            ByteBuf storage = component.Source.UnwrapIterationStorage(ref local);
            // avoid additional checks in AbstractByteBuf case
            // CLR: all storage providers expose this internal loop, independently of public iteration overrides.
            int remaining = count;

            bool Process(byte value)
            {
                if (!processor(value))
                {
                    return false;
                }
                if (--remaining > 0)
                {
                    EnsureAccessible();
                    component.Source.CheckIndex(0, 0);
                }
                return true;
            }

            int result = storage.ForEachByteAscending(local, local + count, Process);
            if (result != -1)
            {
                return start + (result - local);
            }
            start += count;
        }
        return -1;
    }

    internal override int ForEachByteDescending(int start, int end, Func<byte, bool> processor)
    {
        // rStart *and* rEnd are inclusive
        if (end > start)
        {
            return -1;
        }
        int componentIndex = FindComponentIndex(start);
        while (start >= end)
        {
            CheckIndex(end, start - end + 1);
            ComponentEntry component = _components[componentIndex--];
            if (component.Length == 0)
            {
                continue; // empty
            }
            // CLR: clamp at the captured component boundary. The original localRStart-based
            // length can include bytes before a component's captured readable start.
            int count = Math.Min(start - end + 1, start - component.Offset + 1);
            int componentStart = start - count + 1;
            int local = component.SourceIndex + (componentStart - component.Offset);
            component.Source.CheckIndex(local, count);
            ByteBuf storage = component.Source.UnwrapIterationStorage(ref local);
            // avoid additional checks in AbstractByteBuf case
            int remaining = count;

            bool Process(byte value)
            {
                if (!processor(value))
                {
                    return false;
                }
                if (--remaining > 0)
                {
                    EnsureAccessible();
                    component.Source.CheckIndex(0, 0);
                }
                return true;
            }

            int result = storage.ForEachByteDescending(local + count - 1, local, Process);
            if (result != -1)
            {
                return componentStart + (result - local);
            }
            start -= count;
        }
        return -1;
    }

    // CompositeByteBuf._get*/_set* dispatch through the selected component's public
    // primitive operations. Direct memory access would skip observable overrides.
    internal override byte ReadByteCore(int index)
    {
        CheckIndex(index, 1);
        return (byte)ReadComponentWord(index, 1, false);
    }

    internal override void WriteByteCore(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 1);
        WriteComponentWord(index, unchecked((ulong)value), 1, false);
    }

    internal override ulong ReadWord(int index, int width, bool littleEndian)
    {
        CheckIndex(index, width);
        return ReadComponentWord(index, width, littleEndian);
    }

    internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    {
        EnsureCanWrite();
        CheckIndex(index, width);
        WriteComponentWord(index, value, width, littleEndian);
        return this;
    }

    private ulong ReadComponentWord(int index, int width, bool littleEndian)
    {
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (width <= component.EndOffset - index)
        {
            int local = component.SourceIndex + (index - component.Offset);
            ByteBuf source = component.Source;
            return width switch
            {
                1 => source.GetByte(local),
                2 => unchecked((ushort)(littleEndian ? source.GetShortLE(local) : source.GetShort(local))),
                3 => unchecked((uint)(littleEndian ? source.GetUnsignedMediumLE(local) : source.GetUnsignedMedium(local))),
                4 => unchecked((uint)(littleEndian ? source.GetIntLE(local) : source.GetInt(local))),
                8 => unchecked((ulong)(littleEndian ? source.GetLongLE(local) : source.GetLong(local))),
                _ => throw new ArgumentOutOfRangeException(nameof(width))
            };
        }
        int firstWidth = width == 3 ? 2 : width / 2;
        ulong first = ReadComponentWord(index, firstWidth, littleEndian);
        ulong second = ReadComponentWord(index + firstWidth, width - firstWidth, littleEndian);
        return littleEndian ? first | second << (firstWidth * 8) : first << ((width - firstWidth) * 8) | second;
    }

    private void WriteComponentWord(int index, ulong value, int width, bool littleEndian)
    {
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (width <= component.EndOffset - index)
        {
            int local = component.SourceIndex + (index - component.Offset);
            ByteBuf source = component.Source;
            switch (width)
            {
                case 1:
                    source.SetByte(local, unchecked((int)value));
                    break;
                case 2:
                    if (littleEndian)
                    {
                        source.SetShortLE(local, unchecked((int)value));
                    }
                    else
                    {
                        source.SetShort(local, unchecked((int)value));
                    }
                    break;
                case 3:
                    if (littleEndian)
                    {
                        source.SetMediumLE(local, unchecked((int)value));
                    }
                    else
                    {
                        source.SetMedium(local, unchecked((int)value));
                    }
                    break;
                case 4:
                    if (littleEndian)
                    {
                        source.SetIntLE(local, unchecked((int)value));
                    }
                    else
                    {
                        source.SetInt(local, unchecked((int)value));
                    }
                    break;
                case 8:
                    if (littleEndian)
                    {
                        source.SetLongLE(local, unchecked((long)value));
                    }
                    else
                    {
                        source.SetLong(local, unchecked((long)value));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(width));
            }
            return;
        }
        int firstWidth = width == 3 ? 2 : width / 2, secondWidth = width - firstWidth;
        // Match the original byte/short/int casts before each recursive setter call.
        WriteComponentWord(index, NarrowPart(littleEndian ? value : value >> (secondWidth * 8), firstWidth), firstWidth, littleEndian);
        WriteComponentWord(index + firstWidth, NarrowPart(littleEndian ? value >> (firstWidth * 8) : value, secondWidth), secondWidth, littleEndian);
    }

    private static ulong NarrowPart(ulong value, int width)
    {
        return width switch
        {
            1 => unchecked((ulong)(sbyte)value),
            2 => unchecked((ulong)(short)value),
            4 => unchecked((ulong)(int)value),
            _ => throw new ArgumentOutOfRangeException(nameof(width))
        };
    }
}
