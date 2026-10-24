/*
 * Copyright 2022 The Netty Project
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

internal sealed partial class AdaptiveByteBuf
{
    // Original rootParent()._get*/_set* bypass public primitive overrides;
    // keep the allocation's logical storage kernels distinct from its physical Memory borrow.
    internal override byte ReadByteCore(int index)
    { CheckIndex(index, 1); return _allocation.Root.ReadByteCore(_allocation.Offset + index); }
    internal override void WriteByteCore(int index, int value)
    { EnsureCanWrite(); CheckIndex(index, 1); _allocation.Root.WriteByteCore(_allocation.Offset + index, value); }
    internal override ulong ReadWord(int index, int width, bool littleEndian)
    { CheckIndex(index, width); return _allocation.Root.ReadWord(_allocation.Offset + index, width, littleEndian); }
    internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    { EnsureCanWrite(); CheckIndex(index, width); _allocation.Root.SetWord(_allocation.Offset + index, value, width, littleEndian); return this; }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        _allocation.Root.GetBytes(_allocation.Offset + index, destination);
        return this;
    }
    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        _allocation.Root.GetBytes(_allocation.Offset + index, destination, destinationIndex, length);
        return this;
    }
    protected override void GetBytesCore(int index, Span<byte> destination)
        => _allocation.Root.GetBytes(_allocation.Offset + index, destination);
    protected override void SetZeroCore(int index, int length)
        => _allocation.Root.SetZero(_allocation.Offset + index, length);
    internal override bool SupportsDirectBulkCopy => _allocation.Root.SupportsDirectBulkCopy;
    internal override bool SupportsDirectSearch => _allocation.Root.SupportsDirectSearch;

    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);
        AdaptiveChunkAllocation allocation = _allocation;
        // CLR: a callback can return or resize this buffer. Hold the old region
        // until the root iteration finishes so it cannot be reused mid-operation.
        using var lease = allocation.AcquireLease();
        int result = allocation.Root.ForEachByte(allocation.Offset + index, length, processor);
        return result < allocation.Offset ? -1 : result - allocation.Offset;
    }
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);
        AdaptiveChunkAllocation allocation = _allocation;
        using var lease = allocation.AcquireLease();
        int result = allocation.Root.ForEachByteDesc(allocation.Offset + index, length, processor);
        return result < allocation.Offset ? -1 : result - allocation.Offset;
    }
}
