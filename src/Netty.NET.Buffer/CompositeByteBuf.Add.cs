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
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public partial class CompositeByteBuf
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
    public CompositeByteBuf AddComponents(params ByteBuf[] buffers) => AddComponents(_components.Count, buffers);

    /**
     * Add the given {@link ByteBuf}s and increase the {@code writerIndex} if {@code increaseWriterIndex} is
     * {@code true}.
     *
     * {@link ByteBuf#release()} ownership of all {@link ByteBuf} objects in {@code buffers} is transferred to this
     * {@link CompositeByteBuf}.
     * @param buffers the {@link ByteBuf}s to add. {@link ByteBuf#release()} ownership of all {@link ByteBuf#release()}
     * ownership of all {@link ByteBuf} objects is transferred to this {@link CompositeByteBuf}.
     */
    public CompositeByteBuf AddComponents(ByteBuf[] buffers, bool increaseWriterIndex)
        => AddComponents(_components.Count, buffers, increaseWriterIndex);

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
    public CompositeByteBuf AddComponents(int componentIndex, ByteBuf[] buffers, bool increaseWriterIndex = false)
        => AddComponentsCore(componentIndex, buffers, 0, increaseWriterIndex);

    private CompositeByteBuf AddComponentsCore(int componentIndex, ByteBuf[] buffers, int offset, bool increaseWriterIndex)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        int readableBytes = 0;
        int capacity = Capacity;
        for (int i = offset; i < buffers.Length; ++i)
        {
            ByteBuf buffer = buffers[i];
            if (buffer == null) break;
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
                if (buffer == null) break;
                // will increase componentCount
                addedBytes += AddComponentCore(componentIndex++, buffer, false);
            }
        }
        finally
        {
            // ci is now the index following the last successfully added component
            // we bailed early
            for (; next < buffers.Length; ++next) ReferenceCountUtil.SafeRelease(buffers[next]);
            // only need to do this here for components after the added ones
            // CLR: each List insertion has already updated offsets; only indices remain.
            if (increaseWriterIndex && addedBytes != 0) WriterIndex += addedBytes;
        }
        ConsolidateIfNeeded();
        return this;
    }

    // CLR: factory-only offset entry points avoid copying arrays or replacing
    // the original array preflight with streaming ownership semantics.
    internal CompositeByteBuf AddWrappedComponents(ByteBuf[] buffers, int offset)
        => AddComponentsCore(0, buffers, offset, true);

    internal CompositeByteBuf AddWrappedArrays(byte[][] arrays, int offset)
    {
        // No need for consolidation
        // CLR: the Java ByteWrapper<byte[]> strategy is a direct byte-array loop.
        // Delay consolidation until every nonempty array has been wrapped.
        for (int i = offset; i < arrays.Length; ++i)
        {
            byte[] array = arrays[i];
            if (array == null) break;
            if (array.Length != 0) AddComponentCore(_components.Count, Unpooled.WrappedBuffer(array), false);
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
    public CompositeByteBuf AddComponents(IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
        => AddComponents(_components.Count, buffers, increaseWriterIndex);

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
    public CompositeByteBuf AddComponents(int componentIndex, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
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
                if (next == null) break;
                AddComponentCore(componentIndex++, next, increaseWriterIndex);
            }
        }
        finally
        {
            while (iterator.MoveNext())
            {
                ByteBuf next = iterator.Current;
                // CLR: rejected cyclic references remain caller-owned, including in cleanup.
                if (next != null && !ContainsBuffer(next, this)) ReferenceCountUtil.SafeRelease(next);
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
    public CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (ContainsBuffer(buffer, this)) throw new ArgumentException("Component ownership must be acyclic.", nameof(buffer));
        EnsureAccessible();
        int readerIndex = buffer.ReaderIndex, writerIndex = buffer.WriterIndex;
        if (readerIndex == writerIndex) { buffer.Release(); return this; }
        if (buffer is not CompositeByteBuf source)
            return AddComponent(buffer, increaseWriterIndex);

        source.CheckIndex(readerIndex, writerIndex - readerIndex);
        // CLR: checked capacity prevents the original unchecked flatten-offset overflow.
        int length = writerIndex - readerIndex;
        if (length > MaxCapacity - Capacity) throw new ArgumentOutOfRangeException(nameof(buffer));
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
                        component.SourceIndex + fromIndex - component.Offset, count, reuseWholeSource: false);
                    // Retain the original source, not an unwrapped parent with possibly different ownership.
                    component.Source.Retain();
                    additions.Add(addition); // capacity is reserved before any retain
                }
                if (writerIndex == toIndex) break;
            }
            _components.EnsureCapacity(checked(countBefore + additions.Count));
            _components.AddRange(additions);
            UpdateOffsets();
            if (increaseWriterIndex) WriterIndex = writerBefore + length;
            ConsolidateIfNeeded();
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                // if we did not succeed, attempt to rollback any components that were added
                if (_components.Count > countBefore) _components.RemoveRange(countBefore, _components.Count - countBefore);
                UpdateOffsets();
                if (increaseWriterIndex) WriterIndex = writerBefore;
                foreach (ComponentEntry addition in additions) addition.Free();
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
        if (_components.Count > MaxNumComponents) Consolidate();
    }
}
