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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DirectByteBufTest : HeapByteBufTest
{
    private readonly NativeMemoryAllocator _contractAllocator = new();
    protected override bool UsesDirectMemory => true;
    protected override ByteBuf NewBuffer(int initialCapacity, int maxCapacity = int.MaxValue)
        => new UnpooledDirectByteBuf(initialCapacity, maxCapacity, _contractAllocator);
    [Fact]
    public void HeapAndDirectWireOperationsAndSharedViewsAgree()
    {
        var allocator = new NativeMemoryAllocator(1024);
        ByteBuf direct = new UnpooledDirectByteBuf(0, 64, allocator);
        ByteBuf heap = Unpooled.Buffer(0, 64);
        try
        {
            foreach (ByteBuf buffer in new[] { direct, heap })
                buffer.WriteByte(0xFF).WriteShortLE(0xFEDC).WriteMedium(0xABCDEF)
                    .WriteIntLE(unchecked((int)0xFEDCBA98)).WriteLong(long.MinValue).WriteDoubleLE(-0.0);
            Assert.True(direct.IsDirect);
            Assert.Equal(heap.ReadableMemory.ToArray(), direct.ReadableMemory.ToArray());
            ByteBuf slice = direct.Slice(1, 5);
            Assert.True(slice.IsDirect);
            slice.SetByte(0, 0x77);
            Assert.Equal(0x77, direct.GetByte(1));
            ByteBuf duplicate = direct.Duplicate();
            duplicate.SkipBytes(2);
            Assert.Equal(0, direct.ReaderIndex);
            Assert.Equal(2, duplicate.ReaderIndex);
        }
        finally { direct.Release(); heap.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void NativeCapacityChangesPreservePrefixAndInvalidateOldMemory()
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf buffer = new UnpooledDirectByteBuf(8, 16, allocator);
        try
        {
            buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            buffer.ReaderIndex = 6;
            Memory<byte> old = buffer.AsMemory(0, 8);
            buffer.Capacity = 4;
            Assert.Equal(4, buffer.ReaderIndex);
            Assert.Equal(4, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.AsMemory(0, 4).ToArray());
            Assert.Throws<ObjectDisposedException>(() => old.ToArray());
            buffer.Capacity = 16;
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.AsMemory(0, 4).ToArray());
            Assert.Equal(new byte[12], buffer.AsMemory(4, 12).ToArray());
            Assert.Equal(16, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void NativeAllocationFailureKeepsStorageAndIndices()
    {
        var allocator = new NativeMemoryAllocator(8);
        ByteBuf buffer = new UnpooledDirectByteBuf(8, 16, allocator);
        try
        {
            buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            buffer.ReaderIndex = 3;
            Memory<byte> existing = buffer.AsMemory(0, 8);
            Assert.Throws<OutOfMemoryException>(() => buffer.Capacity = 12);
            Assert.Equal(8, buffer.Capacity);
            Assert.Equal(3, buffer.ReaderIndex);
            Assert.Equal(8, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, existing.ToArray());
            Assert.Equal(8, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void RetainedSliceKeepsNativeAllocationUntilItsFinalRelease()
    {
        var allocator = new NativeMemoryAllocator(16);
        ByteBuf buffer = new UnpooledDirectByteBuf(8, 8, allocator);
        buffer.SetLong(0, 0x0102030405060708);
        ByteBuf retained = buffer.RetainedSlice(2, 4);
        Assert.False(buffer.Release());
        Assert.Equal(8, allocator.ReservedBytes);
        Assert.Equal(0x03040506, retained.ReadInt());
        Assert.True(retained.Release());
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.Throws<IllegalReferenceCountException>(() => retained.GetByte(0));
    }

    [Fact]
    public unsafe void PinKeepsPhysicalAllocationAfterLogicalFinalRelease()
    {
        var allocator = new NativeMemoryAllocator(16);
        ByteBuf buffer = new UnpooledDirectByteBuf(8, 8, allocator);
        buffer.SetLong(0, 0x0102030405060708);
        Memory<byte> borrowed = buffer.AsMemory(2, 4);
        MemoryHandle pin = borrowed.Pin();
        try
        {
            Assert.True(buffer.Release());
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.Equal(8, allocator.ReservedBytes);
            Assert.Equal(3, *(byte*)pin.Pointer);
            Assert.Throws<IllegalReferenceCountException>(() => buffer.GetByte(2));
            Assert.Throws<ObjectDisposedException>(() => borrowed.ToArray());
        }
        finally { pin.Dispose(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public unsafe void PinDescribesItsOriginalAllocationAfterCapacityReplacement()
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf buffer = new UnpooledDirectByteBuf(4, 16, allocator);
        buffer.WriteBytes(new byte[] { 1, 2, 3, 4 });
        MemoryHandle pin = buffer.AsMemory(0, 4).Pin();
        try
        {
            buffer.Capacity = 12;
            Assert.Equal(16, allocator.ReservedBytes);
            buffer.SetByte(0, 9);
            Assert.Equal(1, *(byte*)pin.Pointer);
            Assert.Equal(9, buffer.GetByte(0));
        }
        finally { pin.Dispose(); }
        Assert.Equal(12, allocator.ReservedBytes);
        Assert.True(buffer.Release());
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrowingSpanWritePreservesAliasingNativeSource(bool duplicate)
    {
        var allocator = new NativeMemoryAllocator(32);
        ByteBuf buffer = new UnpooledDirectByteBuf(4, 8, allocator);
        try
        {
            buffer.WriteBytes(new byte[] { 1, 2, 3, 4 });
            ReadOnlySpan<byte> old = buffer.AsSpan(0, 4);
            ByteBuf writer = duplicate ? buffer.Duplicate() : buffer;
            writer.WriteBytes(old);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, writer.ReadableMemory.ToArray());
            Assert.Equal(8, writer.WriterIndex);
            Assert.Equal(8, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void GrowingByteBufWriteSupportsSelfSource()
    {
        var allocator = new NativeMemoryAllocator(32);
        ByteBuf buffer = new UnpooledDirectByteBuf(4, 8, allocator);
        try
        {
            buffer.WriteBytes(new byte[] { 1, 2, 3, 4 });
            buffer.WriteBytes(buffer, 4);
            Assert.Equal(4, buffer.ReaderIndex);
            Assert.Equal(8, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.ReadableMemory.ToArray());
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void NativeCopyHasIndependentOwnershipAndTheSameStorageKind()
    {
        var allocator = new NativeMemoryAllocator(32);
        ByteBuf buffer = new UnpooledDirectByteBuf(8, 16, allocator);
        buffer.WriteBytes(new byte[] { 1, 2, 3, 4 }).SkipBytes(1);
        ByteBuf copy = buffer.Copy();
        try
        {
            Assert.True(copy.IsDirect);
            Assert.Equal(16, copy.MaxCapacity);
            Assert.Equal(new byte[] { 2, 3, 4 }, copy.ReadableMemory.ToArray());
            buffer.SetByte(1, 9);
            Assert.Equal(2, copy.GetByte(0));
            Assert.True(buffer.Release());
            Assert.Equal(3, allocator.ReservedBytes);
        }
        finally { copy.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void ZeroNativeCapacityRemainsOwnedAndCanGrow()
    {
        var allocator = new NativeMemoryAllocator(16);
        ByteBuf buffer = new UnpooledDirectByteBuf(0, 8, allocator);
        try
        {
            Assert.Equal(1, allocator.ReservedBytes);
            Assert.Equal(0, buffer.Capacity);
            buffer.WriteLong(long.MaxValue);
            Assert.Equal(long.MaxValue, buffer.ReadLong());
            Assert.Equal(8, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(9, 8)]
    [InlineData(0, -1)]
    public void InvalidConstructorDoesNotReserveNativeMemory(int initial, int maximum)
    {
        var allocator = new NativeMemoryAllocator(16);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnpooledDirectByteBuf(initial, maximum, allocator));
        Assert.Equal(0, allocator.ReservedBytes);
    }
}
