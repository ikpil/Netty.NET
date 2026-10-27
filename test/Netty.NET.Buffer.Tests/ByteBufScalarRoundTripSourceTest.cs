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
using System.Buffers.Binary;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufScalarRoundTripSourceTest
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

    // Supplemental independent BCL wire model; original bodies live in AbstractByteBufTest.ScalarRoundTrip.cs.
    public enum ScalarKind
    {
        SignedByte, UnsignedByte, SignedShort, UnsignedShort, SignedMedium, UnsignedMedium,
        SignedInt, UnsignedInt, Long, Float, Double
    }

    public static IEnumerable<object[]> WireCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (ScalarKind kind in Enum.GetValues<ScalarKind>())
        foreach (bool sequential in new[] { false, true })
        {
            if (sequential && kind is ScalarKind.Float or ScalarKind.Double) continue;
            yield return [provider, kind, false, sequential];
            if (kind is not (ScalarKind.SignedByte or ScalarKind.UnsignedByte))
                yield return [provider, kind, true, sequential];
        }
    }

    private static int Width(int kind) => kind switch
    { 0 or 1 => 1, 2 or 3 => 2, 4 or 5 => 3, 6 or 7 or 9 => 4, _ => 8 };

    private static long NextBits(Random random, int kind, bool sequential)
    {
        if (kind == 9) return BitConverter.SingleToInt32Bits(random.NextSingle());
        if (kind == 10) return BitConverter.DoubleToInt64Bits(random.NextDouble());
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes[..(kind == 8 ? 8 : 4)]);
        if (kind == 8) return BinaryPrimitives.ReadInt64BigEndian(bytes);
        int value = BinaryPrimitives.ReadInt32BigEndian(bytes);
        return kind switch
        {
            0 or 1 => unchecked((sbyte)value), 2 or 3 => unchecked((short)value),
            5 when sequential => value & 0x00ffffff, _ => value
        };
    }

    private static long LogicalValue(long bits, int kind) => kind switch
    {
        1 => unchecked((byte)bits), 3 => unchecked((ushort)bits),
        4 => unchecked((int)bits) << 8 >> 8, 5 => bits & 0x00ffffff,
        7 => unchecked((uint)bits), _ => bits
    };

    private static void Model(Span<byte> destination, long bits, int kind, bool little)
    {
        int value = unchecked((int)bits);
        if (destination.Length == 1) destination[0] = unchecked((byte)bits);
        else if (destination.Length == 2)
        {
            if (little) BinaryPrimitives.WriteInt16LittleEndian(destination, unchecked((short)bits));
            else BinaryPrimitives.WriteInt16BigEndian(destination, unchecked((short)bits));
        }
        else if (destination.Length == 3)
        {
            Span<byte> word = stackalloc byte[4];
            if (little) BinaryPrimitives.WriteInt32LittleEndian(word, value);
            else BinaryPrimitives.WriteInt32BigEndian(word, value);
            (little ? word[..3] : word[1..]).CopyTo(destination);
        }
        else if (destination.Length == 4)
        {
            if (little) BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            else BinaryPrimitives.WriteInt32BigEndian(destination, value);
        }
        else if (little) BinaryPrimitives.WriteInt64LittleEndian(destination, bits);
        else BinaryPrimitives.WriteInt64BigEndian(destination, bits);
    }

    private static void Store(ByteBuf b, int index, long bits, int kind, bool little, bool sequential)
    {
        int value = unchecked((int)bits);
        if (sequential)
        {
            switch (kind)
            {
                case 0 or 1: b.WriteByte(unchecked((byte)bits)); break;
                case 2 or 3: if (little) b.WriteShortLE(value); else b.WriteShort(value); break;
                case 4 or 5: if (little) b.WriteMediumLE(value); else b.WriteMedium(value); break;
                case 6 or 7: if (little) b.WriteIntLE(value); else b.WriteInt(value); break;
                default: if (little) b.WriteLongLE(bits); else b.WriteLong(bits); break;
            }
            return;
        }
        switch (kind)
        {
            case 0 or 1: b.SetByte(index, unchecked((byte)bits)); break;
            case 2 or 3: if (little) b.SetShortLE(index, value); else b.SetShort(index, value); break;
            case 4 or 5: if (little) b.SetMediumLE(index, value); else b.SetMedium(index, value); break;
            case 6 or 7: if (little) b.SetIntLE(index, value); else b.SetInt(index, value); break;
            case 8: if (little) b.SetLongLE(index, bits); else b.SetLong(index, bits); break;
            case 9:
                if (little) b.SetFloatLE(index, BitConverter.Int32BitsToSingle(value));
                else b.SetFloat(index, BitConverter.Int32BitsToSingle(value)); break;
            default:
                if (little) b.SetDoubleLE(index, BitConverter.Int64BitsToDouble(bits));
                else b.SetDouble(index, BitConverter.Int64BitsToDouble(bits)); break;
        }
    }

    private static long Load(ByteBuf b, int index, int kind, bool little, bool sequential)
    {
        if (sequential) return kind switch
        {
            0 => unchecked((sbyte)b.ReadByte()), 1 => b.ReadByte(),
            2 => little ? b.ReadShortLE() : b.ReadShort(),
            3 => little ? b.ReadUnsignedShortLE() : b.ReadUnsignedShort(),
            4 => little ? b.ReadMediumLE() : b.ReadMedium(),
            5 => little ? b.ReadUnsignedMediumLE() : b.ReadUnsignedMedium(),
            6 => little ? b.ReadIntLE() : b.ReadInt(),
            7 => little ? b.ReadUnsignedIntLE() : b.ReadUnsignedInt(),
            _ => little ? b.ReadLongLE() : b.ReadLong()
        };
        return kind switch
        {
            0 => unchecked((sbyte)b.GetByte(index)), 1 => b.GetByte(index),
            2 => little ? b.GetShortLE(index) : b.GetShort(index),
            3 => little ? b.GetUnsignedShortLE(index) : b.GetUnsignedShort(index),
            4 => little ? b.GetMediumLE(index) : b.GetMedium(index),
            5 => little ? b.GetUnsignedMediumLE(index) : b.GetUnsignedMedium(index),
            6 => little ? b.GetIntLE(index) : b.GetInt(index),
            7 => little ? b.GetUnsignedIntLE(index) : b.GetUnsignedInt(index),
            8 => little ? b.GetLongLE(index) : b.GetLong(index),
            9 => BitConverter.SingleToInt32Bits(little ? b.GetFloatLE(index) : b.GetFloat(index)),
            _ => BitConverter.DoubleToInt64Bits(little ? b.GetDoubleLE(index) : b.GetDouble(index))
        };
    }

    private static void RunCase(int provider, int kind, bool little, bool sequential)
    {
        const int capacity = 4096, seed = 12345;
        using var s = new Storage(provider, capacity);
        ByteBuf buffer = s.Root;
        // Initialize otherwise-unused float gaps/medium tails for the independent whole-byte oracle.
        buffer.SetZero(0, capacity);
        if (sequential) buffer.WriterIndex = 0;
        int width = Width(kind), step = kind == 9 ? 8 : width, end = capacity / step * step;
        byte[] expectedWire = new byte[capacity];
        var random = new Random(seed);
        for (int i = 0; i < end; i += step)
        {
            long bits = NextBits(random, kind, sequential);
            if (sequential) { Assert.Equal(i, buffer.WriterIndex); Assert.True(buffer.IsWritable); }
            Store(buffer, i, bits, kind, little, sequential);
            Model(expectedWire.AsSpan(i, width), bits, kind, little);
        }
        Assert.Equal(expectedWire, ByteBufUtil.GetBytes(buffer, 0, capacity));
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(sequential ? end : 0, buffer.WriterIndex);
        if (sequential)
        {
            Assert.Equal(capacity - end, buffer.WritableBytes);
            if (width != 3) Assert.False(buffer.IsWritable);
        }
        // Each runtime independently reseeds its own PRNG; full signed integer domains use random bytes.
        random = new Random(seed);
        for (int i = 0; i < end; i += step)
        {
            long expected = LogicalValue(NextBits(random, kind, sequential), kind);
            if (sequential) { Assert.Equal(i, buffer.ReaderIndex); Assert.True(buffer.IsReadable); }
            long actual = Load(buffer, i, kind, little, sequential);
            if (kind == 9)
                Assert.InRange(Math.Abs(BitConverter.Int32BitsToSingle((int)expected) - BitConverter.Int32BitsToSingle((int)actual)), 0, 0.01);
            if (kind == 10)
                Assert.InRange(Math.Abs(BitConverter.Int64BitsToDouble(expected) - BitConverter.Int64BitsToDouble(actual)), 0, 0.01);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(sequential ? end : 0, buffer.ReaderIndex); Assert.Equal(sequential ? end : 0, buffer.WriterIndex);
        if (sequential) { Assert.False(buffer.IsReadable); Assert.Equal(0, buffer.ReadableBytes); Assert.Equal(capacity - end, buffer.WritableBytes); }
        Assert.Equal(1, buffer.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(WireCases))]
    public void ScalarWireEncodingMatchesIndependentBclModel(int provider, ScalarKind kind, bool littleEndian, bool sequential)
    {
        RunCase(provider, (int)kind, littleEndian, sequential);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ActualUnsignedShortLittleEndianClosesTheOriginalFixtureFlagGap(int provider)
    {
        // The pinned testSequentialUnsignedShortLEAccess calls its helper with true (big endian).
        // The named shared wrapper retains that original call; independently execute the real LE path here.
        RunCase(provider, 3, true, true);
        using var s = new Storage(provider, 18);
        ushort[] values = [0, 1, 127, 128, 255, 256, 32767, 32768, 65535];
        byte[] expected = [0, 0, 1, 0, 127, 0, 128, 0, 255, 0, 0, 1, 255, 127, 0, 128, 255, 255];
        foreach (ushort value in values) s.Root.WriteShortLE(value);
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Root, 0, 18));
        for (int i = 0; i < values.Length; ++i)
        { Assert.Equal(i * 2, s.Root.ReaderIndex); Assert.Equal(values[i], s.Root.ReadUnsignedShortLE()); }
        Assert.Equal(18, s.Root.ReaderIndex); Assert.Equal(18, s.Root.WriterIndex);
        Assert.Equal(1, s.Root.ReferenceCount);
    }
}
