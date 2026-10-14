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

public class PrimitiveAccessReplacementTest
{
    public static IEnumerable<object[]> Cases()
    {
        for (int kind = 0; kind < 8; kind++)
            foreach (int width in new[] { 1, 2, 3, 4, 8 })
                foreach (bool little in width == 1 ? new[] { false } : new[] { false, true })
                    yield return [kind, width, little];
    }

    internal static ByteBuf Set(ByteBuf b, int index, long value, int width, bool little) => width switch
    {
        1 => b.SetByte(index, unchecked((int)value)),
        2 => little ? b.SetShortLE(index, unchecked((int)value)) : b.SetShort(index, unchecked((int)value)),
        3 => little ? b.SetMediumLE(index, unchecked((int)value)) : b.SetMedium(index, unchecked((int)value)),
        4 => little ? b.SetIntLE(index, unchecked((int)value)) : b.SetInt(index, unchecked((int)value)),
        _ => little ? b.SetLongLE(index, value) : b.SetLong(index, value)
    };
    internal static long Get(ByteBuf b, int index, int width, bool little) => width switch
    {
        1 => b.GetByte(index),
        2 => little ? b.GetShortLE(index) : b.GetShort(index),
        3 => little ? b.GetUnsignedMediumLE(index) : b.GetUnsignedMedium(index),
        4 => little ? b.GetIntLE(index) : b.GetInt(index),
        _ => little ? b.GetLongLE(index) : b.GetLong(index)
    };
    private static long[] Values()
    {
        var values = new List<long> { 0, 1, -1, 127, 128, 255, 256, 32767, 32768, 65535, 65536,
            0x7fffff, 0x800000, 0xffffff, int.MinValue, int.MaxValue, 0x100000000L, long.MinValue, long.MaxValue,
            unchecked((long)0xfedcba9876543210UL) };
        uint state = 0x12345678;
        for (int i = 0; i < 32; i++)
        {
            state = unchecked(state * 1664525 + 1013904223); uint high = state;
            state = unchecked(state * 1664525 + 1013904223);
            values.Add(unchecked((long)((ulong)high << 32 | state)));
        }
        return values.ToArray();
    }

    [Theory, MemberData(nameof(Cases))]
    public void AllHelperAccessorsMatchIndependentWireModelsWithoutMovingIndicesOrOverwritingNeighbors(int kind, int width, bool little)
    {
        using var owner = new Owner(kind); ByteBuf b = owner.Buffer.SetIndex(2, 48).MarkReaderIndex().MarkWriterIndex();
        foreach (int index in new[] { 0, 1, 2, 3, 7, 15, 31, 32, 55, 64 - width }.Distinct())
        foreach (long value in Values())
        {
            b.SetBytes(0, Enumerable.Repeat((byte)0xa5, 64).ToArray());
            byte[] expected = Enumerable.Repeat((byte)0xa5, 64).ToArray();
            ulong bits = unchecked((ulong)value);
            for (int i = 0; i < width; i++) expected[index + (little ? i : width - 1 - i)] = unchecked((byte)(bits >> (8 * i)));
            Assert.Same(b, Set(b, index, value, width, little));
            Assert.Equal(expected, ByteBufUtil.GetBytes(b, 0, 64));
            long expectedValue = width switch
            {
                1 => unchecked((byte)value), 2 => unchecked((short)value), 3 => unchecked((int)(bits & 0xffffff)),
                4 => unchecked((int)value), _ => value
            };
            Assert.Equal(expectedValue, Get(b, index, width, little));
            Assert.Equal(expectedValue, Get(b.AsReadOnly(), index, width, little));
            if (width == 3)
            {
                int signed = (int)expectedValue;
                if ((signed & 0x800000) != 0) signed -= 0x1000000;
                Assert.Equal(signed, little ? b.GetMediumLE(index) : b.GetMedium(index));
            }
            Assert.Equal(2, b.ReaderIndex); Assert.Equal(48, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        }
        b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(2, b.ReaderIndex); Assert.Equal(48, b.WriterIndex);
    }

    [Theory, MemberData(nameof(Cases))]
    public void PublicBoundsReadOnlyAndReleasedChecksPrecedeUncheckedHelperAccess(int kind, int width, bool little)
    {
        using var owner = new Owner(kind); ByteBuf b = owner.Buffer;
        b.SetBytes(0, Enumerable.Repeat((byte)0xa5, 64).ToArray());
        foreach (int index in new[] { -1, 65 - width, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Set(b, index, -1, width, little));
            Assert.Throws<ArgumentOutOfRangeException>(() => Get(b, index, width, little));
        }
        Assert.Throws<NotSupportedException>(() => Set(b.AsReadOnly(), 0, -1, width, little));
        Assert.Equal(Enumerable.Repeat((byte)0xa5, 64).ToArray(), ByteBufUtil.GetBytes(b, 0, 64));
        b.Release();
        Assert.Throws<IllegalReferenceCountException>(() => Set(b, 0, -1, width, little));
        Assert.Throws<IllegalReferenceCountException>(() => Get(b, 0, width, little));
    }

    private sealed class Owner : IDisposable
    {
        private readonly NativeMemoryAllocator _native = new();
        private readonly IDisposable _allocator;
        internal ByteBuf Buffer { get; }
        internal Owner(int kind)
        {
            if (kind is 4 or 5)
            {
                var a = new PooledByteBufAllocator(nativeAllocator: _native, maxOrder: 1); _allocator = a;
                Buffer = kind == 4 ? a.HeapBuffer(64, 64) : a.DirectBuffer(64, 64);
            }
            else if (kind == 6)
            {
                var a = new AdaptiveByteBufAllocator(nativeAllocator: _native, lowMemory: false); _allocator = a;
                Buffer = a.DirectBuffer(64, 64);
            }
            else Buffer = kind switch
            {
                0 => Unpooled.Buffer(64, 64),
                1 => new UnpooledDirectByteBuf(64, 64, _native),
                2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.Buffer(3, 3).WriteZero(3))
                    .AddComponent(new UnpooledDirectByteBuf(4, 4, _native).WriteZero(4))
                    .AddComponent(Unpooled.Buffer(57, 57).WriteZero(57)),
                3 => Unpooled.Buffer(68, 68).Slice(2, 64),
                _ => Unpooled.WrappedBuffer(new byte[68].AsMemory(2, 64))
            };
        }
        public void Dispose()
        {
            if (Buffer.ReferenceCount != 0) Buffer.Release(Buffer.ReferenceCount);
            _allocator?.Dispose(); Assert.Equal(0, _native.ReservedBytes);
        }
    }
}
