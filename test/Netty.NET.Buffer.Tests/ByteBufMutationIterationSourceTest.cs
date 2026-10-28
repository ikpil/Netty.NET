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

namespace Netty.NET.Buffer.Tests;

public class ByteBufMutationIterationSourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root;
        public Storage(int provider, int length)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(length),
                1 => new UnpooledDirectByteBuf(length, int.MaxValue, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(length / 2).SetIndex(0, length / 2), true)
                    .AddComponent(new UnpooledDirectByteBuf(length - length / 2, int.MaxValue, Native)
                        .SetIndex(0, length - length / 2), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], length, int.MaxValue),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], length, int.MaxValue)
            };
            Root.Clear();
        }
        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Root.Release();
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    private static void ReleaseBuffer(ByteBuf buffer)
    {
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        buffer.Clear();
        Assert.True(buffer.Release());
    }

    // Supplemental state/ownership checks; named original bodies execute on the actual seven roots.
    public enum ZeroCase
    {
        Set, Write, SetAfterRelease, WriteAfterRelease
    }

    public static IEnumerable<object[]> ZeroCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (ZeroCase scenario in Enum.GetValues<ZeroCase>()) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(ZeroCases))]
    public void ZeroFillPreservesStateAndOwnership(int provider, ZeroCase scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, scenario is ZeroCase.SetAfterRelease or ZeroCase.WriteAfterRelease ? 8 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario is ZeroCase.SetAfterRelease or ZeroCase.WriteAfterRelease)
        {
            ReleaseBuffer(buffer);
            if (scenario == ZeroCase.SetAfterRelease) Assert.Throws<IllegalReferenceCountException>(() => buffer.SetZero(0, 1));
            else Assert.Throws<IllegalReferenceCountException>(() => buffer.WriteZero(1));
            Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        if (scenario == ZeroCase.Write) Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteZero(-1));
        buffer.Clear();
        while (buffer.IsWritable) buffer.WriteByte(0xFF);
        if (scenario == ZeroCase.Write) buffer.Clear();
        var random = new Random(12345);
        for (int i = 0; i < buffer.Capacity;)
        {
            int length = Math.Min(buffer.Capacity - i, random.Next(32));
            if (scenario == ZeroCase.Set) Assert.Same(buffer, buffer.SetZero(i, length));
            else Assert.Same(buffer, buffer.WriteZero(length));
            i += length;
        }
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(capacity, buffer.WriterIndex);
        Assert.Equal(new byte[capacity], ByteBufUtil.GetBytes(buffer, 0, capacity));
        Assert.Equal(1, buffer.ReferenceCount);
    }

    // Supplemental state/ownership checks; named original bodies execute on the actual seven roots.
    public enum IterationCase
    {
        ReadableForward, AbortAtMiddle, IndexedDescending, FourBytesDescending, FourBytesForward, ForwardAfterRelease, IndexedForwardAfterRelease, DescendingAfterRelease, IndexedDescendingAfterRelease
    }

    public static IEnumerable<object[]> IterationCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (IterationCase scenario in Enum.GetValues<IterationCase>()) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(IterationCases))]
    public void IterationPreservesOctetSequenceStateAndOwnership(int provider, IterationCase scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, scenario is IterationCase.ForwardAfterRelease or IterationCase.IndexedForwardAfterRelease or IterationCase.DescendingAfterRelease or IterationCase.IndexedDescendingAfterRelease ? 8 : scenario is IterationCase.FourBytesDescending or IterationCase.FourBytesForward or IterationCase.ForwardAfterRelease or IterationCase.IndexedForwardAfterRelease or IterationCase.DescendingAfterRelease or IterationCase.IndexedDescendingAfterRelease ? 4 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario is IterationCase.ForwardAfterRelease or IterationCase.IndexedForwardAfterRelease or IterationCase.DescendingAfterRelease or IterationCase.IndexedDescendingAfterRelease)
        {
            ReleaseBuffer(buffer);
            int calls = 0;
            Func<byte, bool> processor = _ => { ++calls; return false; };
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                _ = scenario switch
                {
                    IterationCase.ForwardAfterRelease => buffer.ForEachByte(processor),
                    IterationCase.IndexedForwardAfterRelease => buffer.ForEachByte(0, 1, processor),
                    IterationCase.DescendingAfterRelease => buffer.ForEachByteDesc(processor),
                    _ => buffer.ForEachByteDesc(0, 1, processor)
                };
            });
            Assert.Equal(0, calls); Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        if (scenario is IterationCase.FourBytesDescending or IterationCase.FourBytesForward or IterationCase.ForwardAfterRelease or IterationCase.IndexedForwardAfterRelease or IterationCase.DescendingAfterRelease or IterationCase.IndexedDescendingAfterRelease)
        {
            byte[] expected = [1, 2, 3, 4];
            buffer.WriteBytes(expected);
            byte[] bytes = new byte[expected.Length];
            int index = scenario == IterationCase.FourBytesDescending ? bytes.Length - 1 : 0;
            Func<byte, bool> processor = value =>
            {
                bytes[index] = value;
                index += scenario == IterationCase.FourBytesDescending ? -1 : 1;
                return true;
            };
            Assert.Equal(-1, scenario == IterationCase.FourBytesDescending ? buffer.ForEachByteDesc(processor) : buffer.ForEachByte(processor));
            Assert.Equal(expected, bytes);
            Assert.Equal(scenario == IterationCase.FourBytesDescending ? -1 : bytes.Length, index);
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        }
        else
        {
            for (int i = 0; i < capacity; ++i) buffer.WriteByte(i + 1);
            if (scenario == IterationCase.ReadableForward) buffer.SetIndex(capacity / 4, capacity * 3 / 4);
            int reader = buffer.ReaderIndex, writer = buffer.WriterIndex;
            int index = scenario switch { IterationCase.ReadableForward => capacity / 4, IterationCase.AbortAtMiddle => capacity / 3, _ => capacity * 3 / 4 - 1 };
            int lastIndex = 0, calls = 0;
            // ByteProcessor becomes a synchronous Func<byte, bool>; captured locals replace AtomicInteger.
            // Compare octet patterns explicitly because Java byte is signed and CLR byte is unsigned.
            Func<byte, bool> processor = value =>
            {
                Assert.Equal(unchecked((byte)(index + 1)), value);
                lastIndex = index; ++calls;
                if (scenario == IterationCase.AbortAtMiddle && index == capacity / 2) return false;
                index += scenario == IterationCase.IndexedDescending ? -1 : 1;
                return true;
            };
            int result = scenario switch
            {
                IterationCase.ReadableForward => buffer.ForEachByte(processor),
                IterationCase.AbortAtMiddle => buffer.ForEachByte(capacity / 3, capacity / 3, processor),
                _ => buffer.ForEachByteDesc(capacity / 4, capacity * 2 / 4, processor)
            };
            Assert.Equal(scenario == IterationCase.AbortAtMiddle ? capacity / 2 : -1, result);
            Assert.Equal(scenario switch { IterationCase.ReadableForward => capacity * 3 / 4 - 1, IterationCase.AbortAtMiddle => capacity / 2, _ => capacity / 4 }, lastIndex);
            Assert.Equal(scenario == IterationCase.AbortAtMiddle ? capacity / 2 - capacity / 3 + 1 : capacity / 2, calls);
            Assert.Equal(reader, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
        }
        Assert.Equal(1, buffer.ReferenceCount);
    }

    private static readonly Action<ByteBuf>[] ReleasedWrites =
    [
        b => b.WriteBoolean(true),
        b => b.WriteByte(1),
        b => b.WriteShort(1),
        b => b.WriteShortLE(1),
        b => b.WriteMedium(1),
        b => b.WriteMediumLE(1),
        b => b.WriteInt(1),
        b => b.WriteIntLE(1),
        b => b.WriteLong(1),
        b => b.WriteLongLE(1),
        b => b.WriteChar('\u0001'),
        b => b.WriteFloat(1),
        b => b.WriteFloatLE(1),
        b => b.WriteDouble(1),
        b => b.WriteDoubleLE(1)
    ];

    public enum ReleasedWrite
    {
        Boolean, Byte, Short, ShortLE, Medium, MediumLE, Int, IntLE, Long, LongLE, Char, Float, FloatLE, Double, DoubleLE
    }

    public static IEnumerable<object[]> ReleasedWriteCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (ReleasedWrite operation in Enum.GetValues<ReleasedWrite>()) yield return [provider, operation];
    }

    [Theory]
    [MemberData(nameof(ReleasedWriteCases))]
    public void ScalarWriteAfterReleasePreservesState(int provider, ReleasedWrite operation)
    {
        using var s = new Storage(provider, 8);
        ReleaseBuffer(s.Root);
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedWrites[(int)operation](s.Root));
        Assert.Equal(0, s.Root.ReferenceCount);
        Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
    }
}
