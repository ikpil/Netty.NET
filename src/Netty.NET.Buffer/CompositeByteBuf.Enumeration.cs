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
using System.Collections;
using System.Collections.Generic;

namespace Netty.NET.Buffer;

public partial class CompositeByteBuf : IEnumerable<ByteBuf>
{
    /// <summary>Enumerates borrowed component views, including empty components.</summary>
    /// <remarks>Views share the component cache and do not acquire references.
    /// Retain a view before keeping it beyond component removal or buffer release.
    /// As in Netty, a changed component count invalidates a nonempty enumerator;
    /// replacing components while keeping the same count is not detected.
    /// This is not a synchronization mechanism.</remarks>
    public IEnumerator<ByteBuf> GetEnumerator()
    {
        EnsureAccessible();
        // CLR: the original empty iterator is independent of subsequent additions.
        return _components.Count == 0
            ? ((IEnumerable<ByteBuf>)Array.Empty<ByteBuf>()).GetEnumerator()
            : new ComponentEnumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
    public ByteBuf InternalComponentAtOffset(int offset) => InternalComponent(ToComponentIndex(offset));

    private sealed class ComponentEnumerator : IEnumerator<ByteBuf>
    {
        private readonly CompositeByteBuf _buffer;
        private readonly int _size;
        private int _index;
        private ByteBuf _current;
        private bool _disposed;

        internal ComponentEnumerator(CompositeByteBuf buffer)
        { _buffer = buffer; _size = buffer.NumComponents; }

        public ByteBuf Current => _current ?? throw new InvalidOperationException("The enumerator is not positioned on a component.");
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _current = null;
            if (_disposed) return false;
            // CLR: MoveNext combines Java hasNext/next. Count changes map to
            // InvalidOperationException, while ordinary exhaustion returns false.
            if (_size != _buffer.NumComponents)
                throw new InvalidOperationException("The component count changed during enumeration.");
            if (_index == _size) return false;
            _current = _buffer._components[_index++].Slice();
            return true;
        }

        // Read-Only
        // CLR: IEnumerator has no remove operation. Reset is unsupported;
        // Dispose ends enumeration without releasing any borrowed component.
        public void Reset() => throw new NotSupportedException("Read-Only");
        public void Dispose() { _current = null; _disposed = true; }
    }
}
