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

namespace Netty.NET.Buffer;

internal sealed partial class FixedCompositeByteBuf
{
    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        if (destination.IsEmpty) return this;
        Component component = _components[FindComponent(index)];
        if (destination.Length <= component.EndOffset - index)
            component.Source.GetBytes(index - component.Offset, destination);
        else
        {
            // CLR snapshots preserve memmove semantics across aliased components.
            byte[] bytes = new byte[destination.Length]; GetBytesCore(index, bytes); bytes.AsSpan().CopyTo(destination);
        }
        return this;
    }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length); destination.EnsureCanWrite(); destination.CheckIndex(destinationIndex, length);
        if (length == 0) return this;
        if (BulkRangesOverlap(this, index, destination, destinationIndex, length))
        {
            ByteBuf snapshot = new UnpooledHeapByteBuf(length);
            try { GetBufferComponents(index, snapshot, 0, length); destination.SetBytes(destinationIndex, snapshot, 0, length); }
            finally { snapshot.Release(); }
        }
        else GetBufferComponents(index, destination, destinationIndex, length);
        return this;
    }

    private void GetBufferComponents(int index, ByteBuf destination, int destinationIndex, int length)
    {
        CheckIndex(index, length);
        int componentIndex = FindComponent(index);
        while (length > 0)
        {
            Component component = _components[componentIndex++];
            int count = Math.Min(length, component.EndOffset - index);
            component.Source.GetBytes(index - component.Offset, destination, destinationIndex, count);
            index += count; destinationIndex += count; length -= count;
        }
    }
}
