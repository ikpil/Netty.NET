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

    public static IEnumerable<object[]> ZeroCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int scenario = 0; scenario < 4; ++scenario) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(ZeroCases))]
    public void OriginalZeroFillFixtures(int provider, int scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, scenario >= 2 ? 8 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario >= 2)
        {
            ReleaseBuffer(buffer);
            if (scenario == 2) Assert.Throws<IllegalReferenceCountException>(() => buffer.SetZero(0, 1));
            else Assert.Throws<IllegalReferenceCountException>(() => buffer.WriteZero(1));
            Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        if (scenario == 1) Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteZero(-1));
        buffer.Clear();
        while (buffer.IsWritable) buffer.WriteByte(0xFF);
        if (scenario == 1) buffer.Clear();
        var random = new Random(12345);
        for (int i = 0; i < buffer.Capacity;)
        {
            int length = Math.Min(buffer.Capacity - i, random.Next(32));
            if (scenario == 0) Assert.Same(buffer, buffer.SetZero(i, length));
            else Assert.Same(buffer, buffer.WriteZero(length));
            i += length;
        }
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(capacity, buffer.WriterIndex);
        Assert.Equal(new byte[capacity], ByteBufUtil.GetBytes(buffer, 0, capacity));
        Assert.Equal(1, buffer.ReferenceCount);
    }

    public static IEnumerable<object[]> IterationCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int scenario = 0; scenario < 9; ++scenario) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(IterationCases))]
    public void OriginalByteProcessorFixtures(int provider, int scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, scenario >= 5 ? 8 : scenario >= 3 ? 4 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario >= 5)
        {
            ReleaseBuffer(buffer);
            int calls = 0;
            Func<byte, bool> processor = _ => { ++calls; return false; };
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                _ = scenario switch
                {
                    5 => buffer.ForEachByte(processor),
                    6 => buffer.ForEachByte(0, 1, processor),
                    7 => buffer.ForEachByteDesc(processor),
                    _ => buffer.ForEachByteDesc(0, 1, processor)
                };
            });
            Assert.Equal(0, calls); Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        if (scenario >= 3)
        {
            byte[] expected = [1, 2, 3, 4];
            buffer.WriteBytes(expected);
            byte[] bytes = new byte[expected.Length];
            int index = scenario == 3 ? bytes.Length - 1 : 0;
            Func<byte, bool> processor = value =>
            {
                bytes[index] = value;
                index += scenario == 3 ? -1 : 1;
                return true;
            };
            Assert.Equal(-1, scenario == 3 ? buffer.ForEachByteDesc(processor) : buffer.ForEachByte(processor));
            Assert.Equal(expected, bytes);
            Assert.Equal(scenario == 3 ? -1 : bytes.Length, index);
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        }
        else
        {
            for (int i = 0; i < capacity; ++i) buffer.WriteByte(i + 1);
            if (scenario == 0) buffer.SetIndex(capacity / 4, capacity * 3 / 4);
            int reader = buffer.ReaderIndex, writer = buffer.WriterIndex;
            int index = scenario switch { 0 => capacity / 4, 1 => capacity / 3, _ => capacity * 3 / 4 - 1 };
            int lastIndex = 0, calls = 0;
            // ByteProcessor becomes a synchronous Func<byte, bool>; captured locals replace AtomicInteger.
            // Compare octet patterns explicitly because Java byte is signed and CLR byte is unsigned.
            Func<byte, bool> processor = value =>
            {
                Assert.Equal(unchecked((byte)(index + 1)), value);
                lastIndex = index; ++calls;
                if (scenario == 1 && index == capacity / 2) return false;
                index += scenario == 2 ? -1 : 1;
                return true;
            };
            int result = scenario switch
            {
                0 => buffer.ForEachByte(processor),
                1 => buffer.ForEachByte(capacity / 3, capacity / 3, processor),
                _ => buffer.ForEachByteDesc(capacity / 4, capacity * 2 / 4, processor)
            };
            Assert.Equal(scenario == 1 ? capacity / 2 : -1, result);
            Assert.Equal(scenario switch { 0 => capacity * 3 / 4 - 1, 1 => capacity / 2, _ => capacity / 4 }, lastIndex);
            Assert.Equal(scenario == 1 ? capacity / 2 - capacity / 3 + 1 : capacity / 2, calls);
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

    public static IEnumerable<object[]> ReleasedWriteCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int operation = 0; operation < ReleasedWrites.Length; ++operation) yield return [provider, operation];
    }

    [Theory]
    [MemberData(nameof(ReleasedWriteCases))]
    public void OriginalScalarWriteAfterReleaseFixtures(int provider, int operation)
    {
        using var s = new Storage(provider, 8);
        ReleaseBuffer(s.Root);
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedWrites[operation](s.Root));
        Assert.Equal(0, s.Root.ReferenceCount);
        Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
    }
}
