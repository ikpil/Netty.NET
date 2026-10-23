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

public class ByteBufScalarSourceTest
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

    private static readonly Action<ByteBuf>[] ReleasedOperations =
    [
        b => b.GetBoolean(0),
        b => b.GetByte(0),
        b => b.GetByte(0),
        b => b.GetShort(0),
        b => b.GetShortLE(0),
        b => b.GetUnsignedShort(0),
        b => b.GetUnsignedShortLE(0),
        b => b.GetMedium(0),
        b => b.GetMediumLE(0),
        b => b.GetUnsignedMedium(0),
        b => b.GetInt(0),
        b => b.GetIntLE(0),
        b => b.GetUnsignedInt(0),
        b => b.GetUnsignedIntLE(0),
        b => b.GetLong(0),
        b => b.GetLongLE(0),
        b => b.GetChar(0),
        b => b.GetFloat(0),
        b => b.GetFloatLE(0),
        b => b.GetDouble(0),
        b => b.GetDoubleLE(0),
        b => b.SetBoolean(0, true),
        b => b.SetByte(0, 1),
        b => b.SetShort(0, 1),
        b => b.SetShortLE(0, 1),
        b => b.SetMedium(0, 1),
        b => b.SetMediumLE(0, 1),
        b => b.SetInt(0, 1),
        b => b.SetIntLE(0, 1),
        b => b.SetLong(0, 1),
        b => b.SetLongLE(0, 1),
        b => b.SetChar(0, '\0'),
        b => b.SetFloat(0, 1),
        b => b.SetDouble(0, 1),
        b => b.ReadBoolean(),
        b => b.ReadByte(),
        b => b.ReadByte(),
        b => b.ReadShort(),
        b => b.ReadShortLE(),
        b => b.ReadUnsignedShort(),
        b => b.ReadUnsignedShortLE(),
        b => b.ReadMedium(),
        b => b.ReadMediumLE(),
        b => b.ReadUnsignedMedium(),
        b => b.ReadUnsignedMediumLE(),
        b => b.ReadInt(),
        b => b.ReadIntLE(),
        b => b.ReadUnsignedInt(),
        b => b.ReadUnsignedIntLE(),
        b => b.ReadLong(),
        b => b.ReadLongLE(),
        b => b.ReadChar(),
        b => b.ReadFloat(),
        b => b.ReadFloatLE(),
        b => b.ReadDouble(),
        b => b.ReadDoubleLE()
    ];

    public static IEnumerable<object[]> ReleasedCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int operation = 0; operation < ReleasedOperations.Length; ++operation) yield return [provider, operation];
    }

    [Theory]
    [MemberData(nameof(ReleasedCases))]
    public void OriginalScalarOperationsRejectReleasedStorage(int provider, int operation)
    {
        using var s = new Storage(provider, 8);
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        s.Root.Clear(); Assert.True(s.Root.Release());
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedOperations[operation](s.Root));
        Assert.Equal(0, s.Root.ReferenceCount);
    }

    public static IEnumerable<object[]> BoundaryCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int operation = 0; operation < 6; ++operation)
        foreach (bool negative in new[] { false, true }) yield return [provider, operation, negative];
    }

    private static void GetBoundaryValue(ByteBuf buffer, int operation, int index)
    {
        switch (operation)
        {
            case 0: buffer.GetBoolean(index); break;
            case 1: buffer.GetByte(index); break;
            case 2: buffer.GetShort(index); break;
            case 3: buffer.GetMedium(index); break;
            case 4: buffer.GetInt(index); break;
            default: buffer.GetLong(index); break;
        }
    }

    // The original twelve getBoolean/Byte/Short/Medium/Int/LongBoundaryCheck methods.
    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public void AbsoluteScalarReadsValidateNegativeAndLastIncompleteIndices(int provider, int operation, bool negative)
    {
        using var s = new Storage(provider, 4096);
        int width = operation switch { 0 or 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => 8 };
        int index = negative ? -1 : s.Root.Capacity - width + 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => GetBoundaryValue(s.Root, operation, index));
        Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    public static IEnumerable<object[]> Providers()
    {
        for (int provider = 0; provider < 5; ++provider) yield return [provider];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ReadByteRejectsAnEmptyReadableRangeAfterTheSingleWrittenByte(int provider)
    {
        using var s = new Storage(provider, 8);
        s.Root.WriteByte(0);
        Assert.Equal(0, s.Root.ReadByte());
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.ReadByte());
        Assert.Equal(1, s.Root.ReaderIndex); Assert.Equal(1, s.Root.WriterIndex);
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    private static int Width(int operation) => operation switch { 0 => 1, 1 or 2 => 2, 3 or 4 => 3, 5 or 6 => 4, _ => 8 };
    private static bool LittleEndian(int operation) => operation is 2 or 4 or 6 or 8;
    private static void Set(ByteBuf b, int operation, ulong bits)
    {
        int value = unchecked((int)bits);
        switch (operation)
        {
            case 0: b.SetByte(15, value); break;
            case 1: b.SetShort(15, value); break;
            case 2: b.SetShortLE(15, value); break;
            case 3: b.SetMedium(15, value); break;
            case 4: b.SetMediumLE(15, value); break;
            case 5: b.SetInt(15, value); break;
            case 6: b.SetIntLE(15, value); break;
            case 7: b.SetLong(15, unchecked((long)bits)); break;
            default: b.SetLongLE(15, unchecked((long)bits)); break;
        }
    }
    private static long Get(ByteBuf b, int operation) => operation switch
    {
        0 => b.GetByte(15), 1 => b.GetShort(15), 2 => b.GetShortLE(15),
        3 => b.GetUnsignedMedium(15), 4 => b.GetUnsignedMediumLE(15),
        5 => b.GetInt(15), 6 => b.GetIntLE(15), 7 => b.GetLong(15), _ => b.GetLongLE(15)
    };
    private static long Read(ByteBuf b, int operation) => operation switch
    {
        0 => b.ReadByte(), 1 => b.ReadShort(), 2 => b.ReadShortLE(),
        3 => b.ReadUnsignedMedium(), 4 => b.ReadUnsignedMediumLE(),
        5 => b.ReadInt(), 6 => b.ReadIntLE(), 7 => b.ReadLong(), _ => b.ReadLongLE()
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void RawIntegerKernelsPreserveWireBitsCapacityBoundsAndReadableCursor(int provider)
    {
        using var s = new Storage(provider, 32);
        ulong[] values = [0, ulong.MaxValue, 0x8000000080008000UL, 0x0123456789abcdefUL, 0xfedcba9876543210UL];
        for (int operation = 0; operation < 9; ++operation)
        foreach (ulong bits in values)
        {
            int width = Width(operation);
            byte[] expected = new byte[32]; Array.Fill(expected, (byte)165);
            s.Root.SetBytes(0, expected).SetIndex(2, 6).MarkReaderIndex().MarkWriterIndex();
            for (int i = 0; i < width; ++i)
                expected[15 + i] = unchecked((byte)(bits >> (8 * (LittleEndian(operation) ? i : width - i - 1))));
            Set(s.Root, operation, bits);
            Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 32));
            ulong masked = width == 8 ? bits : bits & ((1UL << (width * 8)) - 1);
            long numeric = width == 8 ? unchecked((long)bits) : width is 1 or 3 ? (long)masked
                : (masked & (1UL << (width * 8 - 1))) == 0 ? (long)masked : (long)masked - (1L << (width * 8));
            Assert.Equal(numeric, Get(s.Root, operation));
            Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(6, s.Root.WriterIndex);
            s.Root.SetIndex(15, 15 + width);
            Assert.Equal(numeric, Read(s.Root, operation));
            Assert.Equal(15 + width, s.Root.ReaderIndex); Assert.Equal(15 + width, s.Root.WriterIndex);
            s.Root.SetIndex(15, 15 + width - 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(s.Root, operation));
            Assert.Equal(15, s.Root.ReaderIndex); Assert.Equal(15 + width - 1, s.Root.WriterIndex);
            s.Root.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(6, s.Root.WriterIndex);
            Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 32)); Assert.Equal(1, s.Root.ReferenceCount);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void UnsignedClrByteCoversBothJavaOctetApisAndExplicitSignedConversion(int provider)
    {
        using var s = new Storage(provider, 8);
        for (int value = 0; value < 256; ++value)
        {
            s.Root.SetByte(0, value).SetIndex(0, 1);
            Assert.Equal(value, s.Root.GetByte(0));
            Assert.Equal(value < 128 ? value : value - 256, unchecked((sbyte)s.Root.GetByte(0)));
            Assert.Equal(value, s.Root.ReadByte()); Assert.Equal(1, s.Root.ReaderIndex);
            s.Root.SetIndex(0, 1); Assert.Equal(value != 0, s.Root.ReadBoolean());
            Assert.Equal(1, s.Root.ReaderIndex); Assert.Equal(1, s.Root.WriterIndex);
        }
    }
}
