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
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// ReadOnlySpan replaces the raw-address/ByteBuffer/byte[] source overloads.
// The original Unsafe prerequisite does not apply to this CLR public copy path.
public class UnsafeByteBufUtilTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly PooledByteBufAllocator Pool;
        public readonly ByteBuf Target;
        public Storage(int provider, int length)
        {
            if (provider != 0)
                Pool = new PooledByteBufAllocator(true, pageSize: 4096, maxOrder: 0, nativeAllocator: Native);
            Target = provider switch
            {
                0 => new UnpooledDirectByteBuf(length, length, Native),
                1 => new PooledDirectByteBuf((PoolDirectArena)Pool.DirectArenas[0], length, length),
                _ => new UnpooledDirectByteBuf(Pool, length, length, Native)
            };
            Target.SetZero(0, length);
        }
        public void Dispose()
        {
            if (Target.ReferenceCount > 0) Assert.True(Target.Release());
            Pool?.Dispose(); Assert.Equal(0, Native.ReservedBytes);
        }
    }

    private static byte[] Data() => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    private static PoolArena.Allocation AllocationOf(ByteBuf buffer)
    {
        // Inspect storage behind optional leak facades; keep ownership/release on the original facade.
        while (buffer is not PooledByteBuf)
            buffer = Assert.IsAssignableFrom<ByteBuf>(buffer.Unwrap());
        return ((PooledByteBuf)buffer).Allocation;
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TestSetBytesOnReadOnlyByteBuffer(int provider)
    {
        byte[] data = Data(); ReadOnlyMemory<byte> readOnly = data;
        using var s = new Storage(provider, data.Length);
        s.Target.SetBytes(0, readOnly.Span);
        Assert.Equal(data, ByteBufUtil.GetBytes(s.Target, 0, data.Length));
        Assert.Equal(data, readOnly.ToArray());
        Assert.Equal(0, s.Target.ReaderIndex); Assert.Equal(0, s.Target.WriterIndex);
        Assert.Equal(1, s.Target.ReferenceCount);
    }

    [Fact]
    public void TestSetBytesOnReadOnlyByteBufferWithPooledAlloc()
    {
        // create memory pool with one page
        using var s = new Storage(2, 10);
        ByteBuf b1 = s.Pool.HeapBuffer(16, 16), b2 = s.Pool.HeapBuffer(16, 16);
        try
        {
            var first = AllocationOf(b1);
            var second = AllocationOf(b2);
            // just check that two following buffers share same array but different offset
            Assert.Equal(4096, first.Memory.Length);
            Assert.Same(first.Storage, second.Storage);
            Assert.NotEqual(first.Offset, second.Offset);
            b1.SetBytes(0, Enumerable.Repeat((byte)0xa1, 16).ToArray());
            b2.SetBytes(0, Enumerable.Repeat((byte)0xb2, 16).ToArray());
            ReadOnlyMemory<byte> source = Data();
            s.Target.SetBytes(0, source.Span);
            Assert.Equal(source.ToArray(), ByteBufUtil.GetBytes(s.Target, 0, 10));
            Assert.Equal(Enumerable.Repeat((byte)0xa1, 16), ByteBufUtil.GetBytes(b1, 0, 16));
            Assert.Equal(Enumerable.Repeat((byte)0xb2, 16), ByteBufUtil.GetBytes(b2, 0, 16));
        }
        finally { b1.Release(); b2.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TestSetBytesWithByteArray(int provider)
    {
        byte[] data = Data(); using var s = new Storage(provider, data.Length);
        s.Target.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex();
        Assert.Same(s.Target, s.Target.SetBytes(0, data.AsSpan()));
        Assert.Equal(data, ByteBufUtil.GetBytes(s.Target, 0, data.Length));
        Assert.Equal(2, s.Target.ReaderIndex); Assert.Equal(7, s.Target.WriterIndex);
        s.Target.SetIndex(0, 10).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, s.Target.ReaderIndex); Assert.Equal(7, s.Target.WriterIndex);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TestSetBytesWithZeroLength(int provider)
    {
        byte[] data = Data(); using var s = new Storage(provider, data.Length);
        s.Target.SetBytes(0, data); s.Target.SetIndex(2, 7);
        byte[] before = ByteBufUtil.GetBytes(s.Target, 0, 10);
        Assert.Same(s.Target, s.Target.SetBytes(0, data.AsSpan(0, 0)));
        Assert.Equal(before, ByteBufUtil.GetBytes(s.Target, 0, 10));
        Assert.Equal(2, s.Target.ReaderIndex); Assert.Equal(7, s.Target.WriterIndex);
    }

    [Fact]
    public void NullArraySourceUsesClrSpanSemantics()
    {
        using var s = new Storage(0, 8);
        byte[] missing = null;
        // A CLR span cannot represent Java's null-array-plus-eight-bytes request.
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetBytes(0, missing.AsSpan(0, 8)));
        // Null arrays converted to spans become valid empty inputs, as does default(ReadOnlySpan<byte>).
        Assert.Same(s.Target, s.Target.SetBytes(0, missing.AsSpan()));
        Assert.Same(s.Target, s.Target.SetBytes(8, default(ReadOnlySpan<byte>)));
        Assert.Equal(new byte[8], ByteBufUtil.GetBytes(s.Target, 0, 8));
        Assert.Equal(0, s.Target.ReaderIndex); Assert.Equal(0, s.Target.WriterIndex);
    }

    public static IEnumerable<object[]> InvalidRanges()
    {
        int[][] ranges =
        [
            [4, 4, -1, 0, 4], // negative index
            [4, 4, 0, 0, -1], // negative length
            [4, 8, 0, 0, 5], // buffer length oversize
            [4, 4, 3, 0, 3], // buffer length oversize
            [4, 4, 0, -1, 4], // negative srcIndex
            [8, 4, 0, 0, 5], // src length oversize
            [4, 4, 0, 1, 4] // src length oversize
        ];
        for (int provider = 0; provider < 3; ++provider)
        foreach (int[] range in ranges)
            yield return [provider, range[0], range[1], range[2], range[3], range[4]];
    }

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public void TestSetBytesOutOfBounds(int provider, int capacity, int sourceLength, int index, int sourceIndex, int length)
    {
        using var s = new Storage(provider, capacity);
        byte[] data = new byte[sourceLength]; Array.Fill(data, (byte)0xee);
        s.Target.SetIndex(1, capacity - 1).MarkReaderIndex().MarkWriterIndex();
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetBytes(index, data.AsSpan(sourceIndex, length)));
        Assert.Equal(new byte[capacity], ByteBufUtil.GetBytes(s.Target, 0, capacity));
        Assert.Equal(1, s.Target.ReaderIndex); Assert.Equal(capacity - 1, s.Target.WriterIndex);
        s.Target.SetIndex(0, capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, s.Target.ReaderIndex); Assert.Equal(capacity - 1, s.Target.WriterIndex);
        Assert.Equal(1, s.Target.ReferenceCount);
    }
}
