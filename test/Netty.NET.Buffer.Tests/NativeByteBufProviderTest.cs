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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Portable contracts of pooled/unpooled native providers and borrowed memory.
// Provider 0: pooled region; 1: huge pooled backing; 2: unpooled factory;
// 3: explicit owned constructor; 4: borrowed NativeMemoryOwner range.
public class NativeByteBufProviderTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new(1 << 20);
        public readonly IByteBufAllocator Allocator;
        public readonly ByteBuf Left, Target, Right;
        public readonly int Initial;
        private readonly NativeMemoryOwner _borrowed;
        private IByteBufAllocatorMetric Metric => ((IByteBufAllocatorMetricProvider)Allocator).Metric;
        public Storage(int provider)
        {
            Initial = provider == 1 ? 9000 : 128;
            Allocator = provider < 2
                ? new PooledByteBufAllocator(true, poolHeap: false, pageSize: 4096,
                    maxOrder: 1, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(true, Native);
            Left = Allocator.DirectBuffer(128, 128);
            if (provider == 4)
            {
                _borrowed = Native.Allocate(160);
                _borrowed.Memory.Span.Fill(0x7e);
                Target = new UnpooledDirectByteBuf(Allocator, _borrowed.Memory.Slice(11, Initial), 32768, Native);
                Assert.Equal(0, Target.ReaderIndex); Assert.Equal(Initial, Target.WriterIndex);
            }
            else Target = provider == 3 ? new UnpooledDirectByteBuf(Allocator, Initial, 32768, Native)
                : Allocator.DirectBuffer(Initial, 32768);
            Right = Allocator.DirectBuffer(128, 128);
            Left.SetBytes(0, Filled(128, 0xa5));
            Right.SetBytes(0, Filled(128, 0x5a));
            Assert.True(Target.IsDirect); Assert.True(Target.IsContiguous);
            Assert.Same(Allocator, Target.Allocator);
            if (provider < 2)
            {
                var a = Assert.IsAssignableFrom<PooledByteBuf>(Owner(Left)).Allocation;
                var b = Assert.IsAssignableFrom<PooledByteBuf>(Owner(Target)).Allocation;
                var c = Assert.IsAssignableFrom<PooledByteBuf>(Owner(Right)).Allocation;
                Assert.Equal(provider == 0, b.IsPooled);
                if (provider == 0)
                {
                    Assert.Same(a.Storage, b.Storage); Assert.Same(b.Storage, c.Storage);
                    Assert.True(b.Offset > 0); Assert.NotEqual(a.Offset, b.Offset); Assert.NotEqual(b.Offset, c.Offset);
                }
                else Assert.Equal(0, b.Offset);
            }
            else Assert.IsAssignableFrom<UnpooledDirectByteBuf>(Owner(Target));
        }
        public void Neighbors()
        {
            Assert.Equal(Filled(128, 0xa5), ByteBufUtil.GetBytes(Left, 0, 128));
            Assert.Equal(Filled(128, 0x5a), ByteBufUtil.GetBytes(Right, 0, 128));
            BorrowedGuards();
        }
        private void BorrowedGuards()
        {
            if (_borrowed == null) return;
            Assert.Equal(Filled(11, 0x7e), _borrowed.Memory[..11].ToArray());
            Assert.Equal(Filled(21, 0x7e), _borrowed.Memory[139..].ToArray());
        }
        public void Dispose()
        {
            foreach (ByteBuf b in new[] { Left, Target, Right }) if (b.ReferenceCount > 0) Assert.True(b.Release());
            // Release/detach never disposes the caller's borrowed owner.
            BorrowedGuards();
            _borrowed?.Dispose();
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
            Assert.Equal(0, Metric.UsedDirectMemory);
        }
    }
    private static ByteBuf Owner(ByteBuf b)
    {
        while (b.Unwrap() is ByteBuf parent) b = parent;
        return b;
    }
    private static byte[] Filled(int length, byte value) { byte[] a = new byte[length]; Array.Fill(a, value); return a; }
    private static void IndicesAndMarks(ByteBuf b, int reader = 3, int writer = 100)
    {
        Assert.Equal(reader, b.ReaderIndex); Assert.Equal(writer, b.WriterIndex);
        b.SetIndex(0, b.Capacity).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(reader, b.ReaderIndex); Assert.Equal(writer, b.WriterIndex);
    }
    public static IEnumerable<object[]> ProviderCases()
    {
        foreach (int provider in Enumerable.Range(0, 5)) yield return [provider];
    }
    public static IEnumerable<object[]> BooleanCases()
    {
        foreach (int provider in Enumerable.Range(0, 5))
        foreach (bool value in new[] { false, true }) yield return [provider, value];
    }
    public static IEnumerable<object[]> ScalarCases()
    {
        foreach (int provider in Enumerable.Range(0, 5))
        foreach (int index in new[] { 0, 1, 7, provider == 1 ? 8992 : 120 }) yield return [provider, index];
    }
    [Theory]
    [MemberData(nameof(ScalarCases))]
    public void ScalarsRespectNativeRegionOffsetsAndWireOrder(int provider, int index)
    {
        using var s = new Storage(provider);
        int[] widths = [1, 2, 2, 3, 3, 4, 4, 8, 8];
        foreach (ulong value in new ulong[] { 0, 0x1122334455667788, 0xffeeddcc8000fedc })
        for (int operation = 0; operation < widths.Length; ++operation)
        {
            byte[] expected = new byte[s.Initial]; Array.Fill(expected, (byte)0xcc);
            s.Target.SetBytes(0, expected).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
            int width = widths[operation]; bool little = operation != 0 && (operation & 1) == 0;
            for (int i = 0; i < width; ++i)
                expected[index + i] = unchecked((byte)(value >> (8 * (little ? i : width - i - 1))));
            ByteBuf receiver = operation switch
            {
                0 => s.Target.SetByte(index, unchecked((int)value)),
                1 => s.Target.SetShort(index, unchecked((int)value)),
                2 => s.Target.SetShortLE(index, unchecked((int)value)),
                3 => s.Target.SetMedium(index, unchecked((int)value)),
                4 => s.Target.SetMediumLE(index, unchecked((int)value)),
                5 => s.Target.SetInt(index, unchecked((int)value)),
                6 => s.Target.SetIntLE(index, unchecked((int)value)),
                7 => s.Target.SetLong(index, unchecked((long)value)),
                _ => s.Target.SetLongLE(index, unchecked((long)value))
            };
            Assert.Same(s.Target, receiver);
            Assert.Equal(expected, ByteBufUtil.GetBytes(s.Target, 0, s.Initial));
            long read = operation switch
            {
                0 => s.Target.GetByte(index), 1 => s.Target.GetShort(index), 2 => s.Target.GetShortLE(index),
                3 => s.Target.GetUnsignedMedium(index), 4 => s.Target.GetUnsignedMediumLE(index),
                5 => s.Target.GetInt(index), 6 => s.Target.GetIntLE(index),
                7 => s.Target.GetLong(index), _ => s.Target.GetLongLE(index)
            };
            long wanted = width switch
            {
                1 => unchecked((byte)value), 2 => unchecked((short)value), 3 => (long)(value & 0xffffff),
                4 => unchecked((int)value), _ => unchecked((long)value)
            };
            Assert.Equal(wanted, read);
            Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
            s.Target.SetIndex(0, s.Initial).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
            s.Neighbors();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.GetLong(s.Initial - 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetLong(s.Initial - 7, 1));
    }

    public static IEnumerable<object[]> ZeroCases()
    {
        foreach (int provider in Enumerable.Range(0, 5))
        foreach (int length in new[] { 0, 1, 7, 8, 63, 64, 65, 128, -1 }) yield return [provider, length];
    }
    [Theory]
    [MemberData(nameof(ZeroCases))]
    public void NativeZeroFillChecksLogicalBoundsAndPreservesNeighbors(int provider, int length)
    {
        using var s = new Storage(provider);
        byte[] expected = Filled(s.Initial, 0xcc);
        s.Target.SetBytes(0, expected).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
        int index = length >= 0 ? s.Initial - length : 0;
        if (length < 0) Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetZero(index, length));
        else { Array.Clear(expected, index, length); Assert.Same(s.Target, s.Target.SetZero(index, length)); }
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Target, 0, s.Initial));
        IndicesAndMarks(s.Target); s.Neighbors();
    }
    [Theory]
    [MemberData(nameof(BooleanCases))]
    public void WriteZeroRemainsCorrectAfterNativeStorageMoves(int provider, bool grow)
    {
        using var s = new Storage(provider);
        int writer = grow ? s.Initial - 8 : 10;
        s.Target.SetBytes(0, Filled(s.Initial, 0xcc)).SetIndex(3, writer).MarkReaderIndex().MarkWriterIndex();
        Assert.Same(s.Target, s.Target.WriteZero(16));
        Assert.True(s.Target.Capacity >= writer + 16);
        Assert.Equal(Filled(writer, 0xcc).Concat(new byte[16]), ByteBufUtil.GetBytes(s.Target, 0, writer + 16));
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(writer + 16, s.Target.WriterIndex);
        s.Target.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(writer, s.Target.WriterIndex);
        s.Neighbors();
    }
    public static IEnumerable<object[]> CopyCases()
    {
        foreach (int provider in Enumerable.Range(0, 5))
        foreach (int length in new[] { 0, 7, 128 }) yield return [provider, length];
    }
    [Theory]
    [MemberData(nameof(CopyCases))]
    public void CopyKeepsDirectAllocatorAndIndependentLifetime(int provider, int length)
    {
        using var s = new Storage(provider);
        s.Target.SetBytes(0, Filled(s.Initial, 0x42)).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
        ByteBuf copy = s.Target.Copy(s.Initial - length, length);
        try
        {
            Assert.Same(s.Allocator, copy.Allocator); Assert.True(copy.IsDirect);
            Assert.Equal(length, copy.Capacity); Assert.Equal(32768, copy.MaxCapacity);
            Assert.Equal(0, copy.ReaderIndex); Assert.Equal(length, copy.WriterIndex);
            Assert.Equal(Filled(length, 0x42), ByteBufUtil.GetBytes(copy));
            if (length > 0) copy.SetByte(0, 99);
            Assert.Equal(0x42, s.Target.GetByte(s.Initial - 1));
            IndicesAndMarks(s.Target); s.Neighbors();
            Assert.True(s.Target.Release());
            Assert.Equal(1, copy.ReferenceCount);
            if (length > 0) Assert.Equal(99, copy.GetByte(0));
        }
        finally { copy.Release(); }
    }
    public static IEnumerable<object[]> TransferCases()
    {
        foreach (int provider in Enumerable.Range(0, 5))
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool output in new[] { false, true }) yield return [provider, kind, output];
    }
    [Theory]
    [MemberData(nameof(TransferCases))]
    public void AbsoluteCopiesSupportHeapNativeAndSegmentedPeers(int provider, int kind, bool output)
    {
        using var s = new Storage(provider);
        ByteBuf peer = kind == 0 ? Unpooled.Buffer(32, 32) : kind == 1
            ? new UnpooledDirectByteBuf(32, 32, s.Native)
            : new CompositeByteBuf().AddComponent(Unpooled.WrappedBuffer(new byte[8]), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[24]), true);
        try
        {
            byte[] root = Filled(s.Initial, 0xcc), other = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            s.Target.SetBytes(0, root).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
            peer.SetBytes(0, other).SetIndex(2, 20).MarkReaderIndex().MarkWriterIndex();
            int index = s.Initial - 23;
            if (output) { Assert.Same(s.Target, s.Target.GetBytes(index, peer, 5, 17)); Array.Fill(other, (byte)0xcc, 5, 17); }
            else { Assert.Same(s.Target, s.Target.SetBytes(index, peer, 5, 17)); other.AsSpan(5, 17).CopyTo(root.AsSpan(index)); }
            Assert.Equal(root, ByteBufUtil.GetBytes(s.Target, 0, s.Initial));
            Assert.Equal(other, ByteBufUtil.GetBytes(peer, 0, 32));
            IndicesAndMarks(s.Target); IndicesAndMarks(peer, 2, 20); s.Neighbors();
            ByteBuf readOnly = peer.AsReadOnly();
            Assert.Throws<NotSupportedException>(() => s.Target.GetBytes(0, readOnly, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.GetBytes(s.Initial, peer, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetBytes(0, peer, 32, 1));
            Assert.Equal(root, ByteBufUtil.GetBytes(s.Target, 0, s.Initial)); s.Neighbors();
        }
        finally { peer.Release(); }
    }
    [Theory]
    [MemberData(nameof(BooleanCases))]
    public unsafe void BoundedSpansReplaceArrayAndByteBufferRanges(int provider, bool output)
    {
        using var s = new Storage(provider);
        byte[] root = Filled(s.Initial, 0xcc), other = Filled(32, 0x42);
        s.Target.SetBytes(0, root).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
        int index = s.Initial - 23;
        if (output) { Assert.Same(s.Target, s.Target.GetBytes(index, other.AsSpan(5, 17))); Array.Fill(other, (byte)0xcc, 5, 17); }
        else { Assert.Same(s.Target, s.Target.SetBytes(index, other.AsSpan(5, 17))); Array.Fill(root, (byte)0x42, index, 17); }
        Assert.Equal(root, ByteBufUtil.GetBytes(s.Target, 0, s.Initial));
        Assert.Equal(output ? Filled(5, 0x42).Concat(Filled(17, 0xcc)).Concat(Filled(10, 0x42)) : Filled(32, 0x42), other);
        Memory<byte> memory = s.Target.AsMemory(index, 17);
        Assert.False(MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out _));
        Assert.True(NativeMemoryOwner.TryGetOwner(memory, out _));
        using MemoryHandle pin = memory.Pin();
        Assert.Equal(root.Skip(index).Take(17), new ReadOnlySpan<byte>(pin.Pointer, 17).ToArray());
        IndicesAndMarks(s.Target); s.Neighbors();
    }
    private sealed class Input(int mode) : MemoryStream(new byte[] { 1, 2, 3, 4, 5 })
    {
        public int Calls;
        public override int Read(Span<byte> destination)
        {
            Calls++;
            if (mode == 2) { destination.Fill(0x99); throw new IOException("input failure"); }
            return mode == 1 ? 0 : base.Read(destination[..Math.Min(3, destination.Length)]);
        }
    }
    public static IEnumerable<object[]> StreamCases()
    {
        foreach (int provider in Enumerable.Range(0, 5))
        foreach (int mode in new[] { 0, 1, 2, 3 }) yield return [provider, mode];
    }
    [Theory]
    [MemberData(nameof(StreamCases))]
    public void StreamTransfersAreBoundedAndKeepAbsoluteIndices(int provider, int mode)
    {
        using var s = new Storage(provider);
        byte[] root = Filled(s.Initial, 0xcc);
        s.Target.SetBytes(0, root).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
        int index = s.Initial - 23;
        if (mode == 3)
        {
            using var output = new MemoryStream();
            Assert.Same(s.Target, s.Target.GetBytes(index, output, 17));
            Assert.Equal(Filled(17, 0xcc), output.ToArray()); Assert.True(output.CanWrite);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.GetBytes(s.Initial, output, 1));
            Assert.Equal(17, output.Length);
        }
        else
        {
            using var input = new Input(mode);
            if (mode == 2) Assert.Throws<IOException>(() => s.Target.SetBytes(index, input, 17));
            else
            {
                Assert.Equal(mode == 0 ? 3 : 0, s.Target.SetBytes(index, input, 17));
                if (mode == 0) new byte[] { 1, 2, 3 }.CopyTo(root.AsSpan(index));
            }
            Assert.Equal(1, input.Calls); Assert.True(input.CanRead);
            Assert.Equal(0, s.Target.SetBytes(s.Initial, input, 0)); Assert.Equal(1, input.Calls);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetBytes(s.Initial, input, 1)); Assert.Equal(1, input.Calls);
        }
        Assert.Equal(root, ByteBufUtil.GetBytes(s.Target, 0, s.Initial));
        IndicesAndMarks(s.Target); s.Neighbors();
    }
    [Theory]
    [MemberData(nameof(ProviderCases))]
    public void RecycledStorageCannotReviveAnOldClrOwner(int provider)
    {
        using var s = new Storage(provider);
        ByteBuf old = Owner(s.Target);
        Assert.True(s.Target.Release());
        ByteBuf next = s.Allocator.DirectBuffer(s.Initial, 32768);
        try
        {
            Assert.NotSame(old, Owner(next)); Assert.Equal(0, old.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => old.GetByte(0));
            Assert.Throws<IllegalReferenceCountException>(() => old.Retain());
            next.SetByte(0, 42); Assert.Equal(42, next.GetByte(0)); s.Neighbors();
        }
        finally { next.Release(); }
    }
}
