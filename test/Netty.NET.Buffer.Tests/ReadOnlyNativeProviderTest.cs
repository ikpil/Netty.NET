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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ReadOnlyNativeProviderTest
{
    [Fact]
    public void NativeReadonlyCopyRetainsOriginalMaximumCapacity()
    {
        var native = new NativeMemoryAllocator(); using var owner = native.Allocate(16);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf source = new ReadOnlyMemoryByteBuf(allocator, owner.Memory.Slice(3, 8));
        ByteBuf copy = source.Copy(1, 3);
        try
        {
            Assert.Equal(8, copy.MaxCapacity);
            copy.Capacity = 8;
            Assert.Throws<ArgumentOutOfRangeException>(() => copy.Capacity = 9);
        }
        finally { source.Release(); copy.Release(); }
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(0, 3)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(1, 3)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(2, 3)]
    public void RootAndDerivedCopiesUseNativeCreatorAndOriginalCapturedMaximum(int allocatorKind, int viewKind)
    {
        var sourceNative = new NativeMemoryAllocator(); using var owner = sourceNative.Allocate(48);
        for (int i = 0; i < 48; i++) owner.Memory.Span[i] = (byte)i;
        var targetNative = new NativeMemoryAllocator();
        IByteBufAllocator allocator = allocatorKind switch
        {
            0 => new UnpooledByteBufAllocator(false, targetNative),
            1 => new PooledByteBufAllocator(nativeAllocator: targetNative, maxOrder: 1),
            _ => new AdaptiveByteBufAllocator(nativeAllocator: targetNative, lowMemory: false)
        };
        ByteBuf source = new ReadOnlyMemoryByteBuf(allocator, owner.Memory.Slice(3, 32)).SetIndex(3, 29);
        ByteBuf view = viewKind switch { 0 => source, 1 => source.Duplicate(), 2 => source.Slice(2, 24), _ => source.Slice(2, 24).Duplicate() };
        view.MarkReaderIndex().MarkWriterIndex(); int reader = view.ReaderIndex, writer = view.WriterIndex;
        try
        {
            foreach (int length in new[] { 0, 1, 7, 24 })
            {
                int index = Math.Min(1, view.Capacity - length);
                byte[] expected = ByteBufUtil.GetBytes(view, index, length);
                ByteBuf copy = view.Copy(index, length);
                try
                {
                    Assert.Equal(32, copy.MaxCapacity); Assert.Equal(length, copy.Capacity);
                    Assert.Same(allocator, copy.Allocator); Assert.True(copy.IsDirect); Assert.False(copy.IsReadOnly);
                    Assert.Equal(expected, ByteBufUtil.GetBytes(copy)); Assert.Equal(length, copy.WriterIndex); Assert.Equal(0, copy.ReaderIndex);
                    copy.Capacity = 32;
                    Assert.Throws<ArgumentOutOfRangeException>(() => copy.Capacity = 33);
                    Assert.Equal(expected, ByteBufUtil.GetBytes(copy, 0, length));
                    if (length != 0) { copy.SetByte(0, 200); Assert.NotEqual(200, view.GetByte(index)); }
                }
                finally { copy.Release(); }
                Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex); Assert.Equal(1, source.ReferenceCount);
            }
            view.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
        }
        finally { source.Release(); (allocator as IDisposable)?.Dispose(); }
        Assert.Equal(0, targetNative.ReservedBytes); Assert.Equal(48, sourceNative.ReservedBytes);
    }

    /**
     * Needs unsafe to run
     */
    // CLR: native allocation and scoped pins replace the JVM Unsafe availability assumption.
    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(8)]
    public unsafe void CapturedNativeOffsetControlsPrimitiveReadsBulkTransfersAndPinnedAddress(int offset)
    {
        var native = new NativeMemoryAllocator(); using var owner = native.Allocate(48);
        for (int i = 0; i < 48; i++) owner.Memory.Span[i] = unchecked((byte)(i * 17 + 128));
        ByteBuf source = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)owner.Memory.Slice(offset, 32)).SetIndex(2, 24);
        ByteBuf heap = Unpooled.Buffer(40, 40), direct = new UnpooledDirectByteBuf(40, 40, native);
        ByteBuf composite = Unpooled.CompositeBuffer().AddComponent(Unpooled.Buffer(5, 5).WriteZero(5))
            .AddComponent(new UnpooledDirectByteBuf(35, 35, native).WriteZero(35));
        try
        {
            for (int i = 0; i <= 24; i++)
            {
                byte[] expected = owner.Memory.Slice(offset + i, 8).ToArray();
                ulong be = 0, le = 0;
                for (int j = 0; j < 8; j++) { be = (be << 8) | expected[j]; le |= (ulong)expected[j] << (8 * j); }
                Assert.Equal(unchecked((long)be), source.GetLong(i)); Assert.Equal(unchecked((long)le), source.GetLongLE(i));
                Assert.Equal(unchecked((short)((expected[0] << 8) | expected[1])), source.GetShort(i));
                Assert.Equal(expected[0] << 16 | expected[1] << 8 | expected[2], source.GetUnsignedMedium(i));
                Assert.Equal(unchecked((int)(be >> 32)), source.GetInt(i));
            }
            foreach (ByteBuf target in new[] { heap, direct, composite })
            {
                source.GetBytes(1, target, 3, 24);
                Assert.Equal(owner.Memory.Slice(offset + 1, 24).ToArray(), ByteBufUtil.GetBytes(target, 3, 24));
                Assert.Equal(0, target.WriterIndex); Assert.Equal(2, source.ReaderIndex); Assert.Equal(24, source.WriterIndex);
            }
            byte[] array = new byte[30]; source.GetBytes(2, array.AsSpan(3, 24));
            Assert.Equal(owner.Memory.Slice(offset + 2, 24).ToArray(), array[3..27]);
            // Use buffer as the super class will slice the passed in ByteBuffer which means the memoryAddress
            // may be different if the position != 0.
            using MemoryHandle rootPin = owner.Memory.Pin();
            using MemoryHandle rangePin = source.AsReadOnlyMemory(0, 32).Pin();
            Assert.Equal((nint)((byte*)rootPin.Pointer + offset), (nint)rangePin.Pointer);
            owner.Memory.Span[offset] = 99; Assert.Equal(99, source.GetByte(0));
        }
        finally { source.Release(); heap.Release(); direct.Release(); composite.Release(); }
    }

    [Fact]
    public void CopyOwnsIndependentNativeStorageAfterWrapperAndExternalOwnerRelease()
    {
        var external = new NativeMemoryAllocator(); var owner = external.Allocate(12);
        owner.Memory.Span.Fill(71); var target = new NativeMemoryAllocator();
        var allocator = new UnpooledByteBufAllocator(true, target);
        ByteBuf source = new ReadOnlyMemoryByteBuf(allocator, owner.Memory.Slice(2, 8));
        ByteBuf copy = source.Copy(1, 5);
        source.Release(); owner.Dispose(); Assert.Equal(0, external.ReservedBytes);
        try { Assert.Equal(8, copy.MaxCapacity); Assert.Equal(Enumerable.Repeat((byte)71, 5), ByteBufUtil.GetBytes(copy)); copy.WriteByte(72); }
        finally { copy.Release(); }
        Assert.Equal(0, target.ReservedBytes);
    }

    [Fact]
    public void ReleasedOrReadonlyDestinationsRejectCopiesBeforePublishingNativeBytes()
    {
        var native = new NativeMemoryAllocator(); using var sourceOwner = native.Allocate(12); using var targetOwner = native.Allocate(12);
        sourceOwner.Memory.Span.Fill(71); targetOwner.Memory.Span.Fill(17);
        ByteBuf source = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)sourceOwner.Memory);
        ByteBuf target = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)targetOwner.Memory);
        ByteBuf dead = new UnpooledDirectByteBuf(12, 12, native); dead.Release();
        try
        {
            Assert.Throws<NotSupportedException>(() => source.GetBytes(0, target, 0, 4));
            Assert.Equal(Enumerable.Repeat((byte)17, 12), targetOwner.Memory.ToArray());
            Assert.Throws<IllegalReferenceCountException>(() => source.GetBytes(0, dead, 0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetBytes(9, new byte[4].AsSpan()));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Copy(9, 4));
            Assert.Equal(0, source.ReaderIndex); Assert.Equal(12, source.WriterIndex);
        }
        finally { source.Release(); target.Release(); }
    }

    [Fact]
    public void FailingDestinationWriteReleasesAllocatedNativeResultAndRetainsBorrowedSource()
    {
        var native = new NativeMemoryAllocator(); using var owner = native.Allocate(8);
        owner.Memory.Span.Fill(71); var allocator = new ReadOnlyTargetAllocator(native);
        ByteBuf source = new ReadOnlyMemoryByteBuf(allocator, owner.Memory);
        try
        {
            Assert.Throws<NotSupportedException>(() => source.Copy(1, 4));
            Assert.Equal(8, allocator.LastMaximum); Assert.Equal(0, allocator.Last.ReferenceCount);
            Assert.Equal(8, native.ReservedBytes); Assert.Equal(71, source.GetByte(1)); Assert.Equal(1, source.ReferenceCount);
        }
        finally { source.Release(); }
    }

    private sealed class ReadOnlyTargetAllocator(NativeMemoryAllocator native) : AbstractByteBufAllocator
    {
        internal ByteBuf Last; internal int LastMaximum;
        public override bool IsDirectBufferPooled => false;
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            LastMaximum = maxCapacity;
            return Last = new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, native).AsReadOnly();
        }
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity) => throw new NotSupportedException();
    }

    [Fact]
    public void EmptyNativeCopyDoesNotInvokeAWritingOperationOnTheFactoryResult()
    {
        var native = new NativeMemoryAllocator(); using var owner = native.Allocate(8);
        var allocator = new ReadOnlyTargetAllocator(native);
        ByteBuf source = new ReadOnlyMemoryByteBuf(allocator, owner.Memory);
        ByteBuf copy = source.Copy(8, 0);
        try
        {
            Assert.Same(allocator.Last, copy); Assert.Equal(8, allocator.LastMaximum);
            Assert.Equal(8, copy.MaxCapacity); Assert.Equal(0, copy.Capacity); Assert.Equal(0, copy.WriterIndex);
            Assert.Equal(1, source.ReferenceCount);
        }
        finally { source.Release(); copy.Release(); }
        Assert.Equal(8, native.ReservedBytes);
    }
}
