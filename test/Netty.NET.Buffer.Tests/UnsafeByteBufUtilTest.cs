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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        public Storage(int provider, int length, int maxOrder = 0)
        {
            if (provider is 1 or 2)
                Pool = new PooledByteBufAllocator(true, pageSize: 4096, maxOrder: maxOrder, nativeAllocator: Native);
            Target = provider switch
            {
                0 => new UnpooledDirectByteBuf(length, length, Native),
                1 => new PooledDirectByteBuf((PoolDirectArena)Pool.DirectArenas[0], length, length),
                3 => new UnpooledHeapByteBuf(length, length),
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

    public static IEnumerable<object[]> ZeroAlignments()
    {
        foreach (int provider in Enumerable.Range(0, 4))
        foreach (int offset in Enumerable.Range(0, 8)) yield return [provider, offset];
    }

    [Theory]
    [MemberData(nameof(ZeroAlignments))]
    public void ZeroFillReplacesEveryNativeAlignmentAndBatchBoundary(int provider, int offset)
    {
        using var s = new Storage(provider, 160, maxOrder: 4);
        foreach (int length in new[] { 0, 1, 7, 8, 63, 64, 65, 129 })
        {
            byte[] expected = Enumerable.Repeat((byte)0xa5, 160).ToArray();
            s.Target.SetBytes(0, expected).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
            Array.Clear(expected, offset, length);
            Assert.Same(s.Target, s.Target.SetZero(offset, length));
            Assert.Equal(expected, ByteBufUtil.GetBytes(s.Target, 0, 160));
            Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
            s.Target.SetIndex(0, 160).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetZero(159, 2));
    }

    public static IEnumerable<object[]> StreamLengths()
    {
        foreach (int provider in Enumerable.Range(0, 4))
        foreach (int length in new[] { 1024, 1025, 8192, 8193 })
        foreach (bool fail in new[] { false, true }) yield return [provider, length, fail];
    }

    private sealed class Output(bool fail) : MemoryStream
    {
        public int Calls;
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            Calls++;
            base.Write(fail ? bytes[..Math.Min(17, bytes.Length)] : bytes);
            if (fail) throw new IOException("output failure");
        }
    }

    [Theory]
    [MemberData(nameof(StreamLengths))]
    public void StreamOutputPreservesBytesAcrossOriginalTemporaryStorageThresholds(int provider, int length, bool fail)
    {
        using var s = new Storage(provider, length + 16, maxOrder: 4);
        byte[] data = Enumerable.Range(0, length + 16).Select(i => unchecked((byte)(i * 7 + 1))).ToArray();
        s.Target.SetBytes(0, data).SetIndex(3, length + 11).MarkReaderIndex().MarkWriterIndex();
        using var output = new Output(fail);
        if (fail) Assert.Throws<IOException>(() => s.Target.GetBytes(5, output, length));
        else Assert.Same(s.Target, s.Target.GetBytes(5, output, length));
        Assert.Equal(data.Skip(5).Take(fail ? 17 : length), output.ToArray());
        Assert.Equal(1, output.Calls); Assert.True(output.CanWrite);
        Assert.Equal(data, ByteBufUtil.GetBytes(s.Target, 0, data.Length));
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(length + 11, s.Target.WriterIndex);
        s.Target.SetIndex(0, data.Length).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(length + 11, s.Target.WriterIndex);
    }

    public static IEnumerable<object[]> FactorySizes()
    {
        foreach (bool adaptive in new[] { false, true })
        foreach (int length in new[] { 1, 128, 1048577 }) yield return [adaptive, length];
    }

    private static IByteBufAllocator Factory(bool adaptive, NativeMemoryAllocator native)
        => adaptive ? new AdaptiveByteBufAllocator(true, false, native)
            : new PooledByteBufAllocator(true, poolHeap: false, poolDirect: false, nativeAllocator: native);

    [Theory]
    [MemberData(nameof(FactorySizes))]
    public void ActualDirectHelperConsumersUseTheExplicitNativeDomain(bool adaptive, int length)
    {
        var native = new NativeMemoryAllocator(16 << 20);
        IByteBufAllocator allocator = Factory(adaptive, native);
        ByteBuf b = null, copy = null;
        try
        {
            b = allocator.DirectBuffer(length, length + 64);
            Assert.True(b.IsDirect); Assert.Same(allocator, b.Allocator);
            if (adaptive)
                Assert.True(MemoryMarshal.TryGetMemoryManager<byte, AdaptiveChunkAllocation>(b.AsMemory(0, length), out _));
            else Assert.True(NativeMemoryOwner.TryGetOwner(b.AsMemory(0, length), out _));
            Assert.True(native.ReservedBytes >= length);
            byte[] prefix = Enumerable.Range(0, Math.Min(32, length)).Select(i => (byte)(i + 1)).ToArray();
            b.WriteBytes(prefix).MarkReaderIndex().MarkWriterIndex();
            copy = b.Copy(0, prefix.Length);
            Assert.True(copy.IsDirect); Assert.Same(allocator, copy.Allocator);
            Assert.Equal(prefix, ByteBufUtil.GetBytes(copy));
            b.Capacity = length + 64;
            Assert.Equal(prefix, ByteBufUtil.GetBytes(b, 0, prefix.Length));
            if (adaptive)
                Assert.True(MemoryMarshal.TryGetMemoryManager<byte, AdaptiveChunkAllocation>(b.AsMemory(0, b.Capacity), out _));
            else Assert.True(NativeMemoryOwner.TryGetOwner(b.AsMemory(0, b.Capacity), out _));
            Assert.True(native.ReservedBytes >= b.Capacity);
            b.SetIndex(0, b.Capacity).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(prefix.Length, b.WriterIndex);
            Assert.True(b.Release());
            Assert.Equal(prefix, ByteBufUtil.GetBytes(copy));
        }
        finally
        {
            copy?.Release();
            if (b?.ReferenceCount > 0) b.Release();
            ((IDisposable)allocator).Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
        Assert.Equal(adaptive ? 0 : -1, ((IByteBufAllocatorMetricProvider)allocator).Metric.UsedDirectMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedDirectBackingAllocationReleasesEveryReservation(bool adaptive)
    {
        var native = new NativeMemoryAllocator(0);
        IByteBufAllocator allocator = Factory(adaptive, native);
        try { Assert.Throws<OutOfMemoryException>(() => allocator.DirectBuffer(128, 192)); }
        finally { ((IDisposable)allocator).Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
        Assert.Equal(adaptive ? 0 : -1, ((IByteBufAllocatorMetricProvider)allocator).Metric.UsedDirectMemory);
    }
}
