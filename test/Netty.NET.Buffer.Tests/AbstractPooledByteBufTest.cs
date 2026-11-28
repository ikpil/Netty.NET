/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// CLR: the source extends AbstractByteBufTest directly; AbstractContiguousByteBufTest is the CLR layer of that base
// holding the shared tests the source restricts to single-region buffers through assumptions.
public abstract class AbstractPooledByteBufTest : AbstractContiguousByteBufTest
{
    protected abstract ByteBuf Alloc(int length, int maxCapacity);

    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        ByteBuf buffer = Alloc(length, maxCapacity);

        // Testing if the writerIndex and readerIndex are correct when allocate and also after we reset the mark.
        Assert.Equal(0, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReaderIndex);
        buffer.ResetReaderIndex();
        buffer.ResetWriterIndex();
        Assert.Equal(0, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReaderIndex);
        return buffer;
    }

    [Fact]
    public void EnsureWritableWithEnoughSpaceShouldNotThrow()
    {
        ByteBuf buf = NewBuffer(1, 10);
        buf.EnsureWritable(3);
        Assert.True(buf.WritableBytes >= 3);
        buf.Release();
    }

    [Fact]
    public void EnsureWritableWithNotEnoughSpaceShouldThrow()
    {
        ByteBuf buf = NewBuffer(1, 10);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buf.EnsureWritable(11);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    // CLR: overrides AbstractByteBufTest.testMaxFastWritableBytes in the source; the CLR declares that shared body
    // only on the unpooled concrete fixtures (BigEndianHeapByteBufTest/BigEndianDirectByteBufTest), so this is the
    // pooled chain's declaration. Virtual so that AbstractAdaptiveByteBufTest can disable it as the source does.
    [Fact]
    public virtual void TestMaxFastWritableBytes()
    {
        ByteBuf buffer = NewBuffer(150, 500);
        buffer.WriterIndex = 100;
        Assert.Equal(50, buffer.WritableBytes);
        Assert.Equal(150, buffer.Capacity);
        Assert.Equal(500, buffer.MaxCapacity);
        Assert.Equal(400, buffer.MaxWritableBytes);

        // CLR: PooledByteBuf.maxLength/handle are the arena allocation's reserved length and handle
        // (src/Netty.NET.Buffer/PoolArena.cs, PoolArena.Allocation).
        int chunkSize = PooledByteBuf(buffer).Allocation.ReservedLength;
        Assert.True(chunkSize >= 150);
        int remainingInAlloc = Math.Min(chunkSize - 100, 400);
        Assert.Equal(remainingInAlloc, buffer.MaxFastWritableBytes);

        // write up to max, chunk alloc should not change (same handle)
        long handleBefore = PooledByteBuf(buffer).Allocation.Handle;
        buffer.WriteBytes(new byte[remainingInAlloc]);
        Assert.Equal(handleBefore, PooledByteBuf(buffer).Allocation.Handle);

        Assert.Equal(0, buffer.MaxFastWritableBytes);
        // writing one more should trigger a reallocation (new handle)
        buffer.WriteByte(7);
        Assert.NotEqual(handleBefore, PooledByteBuf(buffer).Allocation.Handle);

        // should not exceed maxCapacity even if chunk alloc does
        buffer.Capacity = 500;
        Assert.Equal(500 - buffer.WriterIndex, buffer.MaxFastWritableBytes);
        buffer.Release();
    }

    private static global::Netty.NET.Buffer.PooledByteBuf PooledByteBuf(ByteBuf buffer)
    {
        // might need to unwrap if swapped (LE) and/or leak-aware-wrapped
        while (buffer is not global::Netty.NET.Buffer.PooledByteBuf)
        {
            buffer = buffer.Unwrap();
        }

        return (global::Netty.NET.Buffer.PooledByteBuf) buffer;
    }

    // CLR: virtual so that AbstractAdaptiveByteBufTest can disable it as the source does.
    [Fact]
    public virtual void TestEnsureWritableDoesntGrowTooMuch()
    {
        ByteBuf buffer = NewBuffer(150, 500);
        buffer.WriterIndex = 100;

        Assert.Equal(50, buffer.WritableBytes);
        int fastWritable = buffer.MaxFastWritableBytes;
        Assert.True(fastWritable > 50);

        long handleBefore = PooledByteBuf(buffer).Allocation.Handle;

        // capacity expansion should not cause reallocation
        // (should grow precisely the specified amount)
        buffer.EnsureWritable(fastWritable);
        Assert.Equal(handleBefore, PooledByteBuf(buffer).Allocation.Handle);
        Assert.Equal(100 + fastWritable, buffer.Capacity);
        Assert.Equal(buffer.WritableBytes, buffer.MaxFastWritableBytes);
        buffer.Release();
    }

    [Fact]
    public void TestIsContiguous()
    {
        ByteBuf buf = NewBuffer(4);
        Assert.True(buf.IsContiguous);
        buf.Release();
    }

    [Fact]
    public void DistinctBuffersMustNotOverlap()
    {
        ByteBuf a = NewBuffer(16384);
        ByteBuf b = NewBuffer(65536);
        a.SetByte(a.Capacity - 1, 1);
        b.SetByte(0, 2);

        try
        {
            Assert.Equal(1, a.GetByte(a.Capacity - 1));
        }
        finally
        {
            a.Release();
            b.Release();
        }
    }

    // CLR: arena-level test allocator of the CLR supplements (AbstractPooledArenaByteBufTest, PoolChunkListTest); it
    // keeps its earlier nested name so those consumers are unchanged. It is not part of the source fixture.
    internal sealed class TestAllocator : AbstractByteBufAllocator, IDisposable
    {
        internal readonly PoolArena Arena;
        internal readonly NativeMemoryAllocator Native;
        internal int Calculations;
        private readonly bool _direct;
        internal TestAllocator(bool direct, int shifts = 13, int order = 4, int alignment = 0,
            long maximumBytes = long.MaxValue) : base(direct)
        {
            _direct = direct;
            Native = new NativeMemoryAllocator(maximumBytes);
            var sizes = new SizeClasses(1 << shifts, shifts, 1 << (shifts + order), alignment);
            Arena = direct ? new PoolDirectArena(this, sizes, Native) : new PoolHeapArena(this, sizes);
        }
        public override bool IsDirectBufferPooled => _direct;
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            return _direct ? new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity)
                : Arena.Allocate(initialCapacity, maxCapacity);
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            return _direct ? Arena.Allocate(initialCapacity, maxCapacity)
                : new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, Native);
        }

        public override int CalculateNewCapacity(int minimumNewCapacity, int maxCapacity)
        {
            ++Calculations;
            return base.CalculateNewCapacity(minimumNewCapacity, maxCapacity);
        }

        public void Dispose()
        {
            Arena.Dispose();
        }
    }
}
