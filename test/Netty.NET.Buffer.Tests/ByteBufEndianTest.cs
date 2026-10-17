// Original swapped-provider comments. CLR uses explicit endian accessors and live bounded memory.
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

/**
 * Wrapper which swap the {@link ByteOrder} of a {@link ByteBuf}.
 *
 * @deprecated use the Little Endian accessors, e.g. {@code getShortLE}, {@code getIntLE}
 * instead.
 */

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

/**
 * Special {@link SwappedByteBuf} for {@link ByteBuf}s that is using unsafe.
 */

/*
* Copyright 2014 The Netty Project
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

/**
 * Special {@link SwappedByteBuf} for {@link ByteBuf}s that use unsafe to access the byte array.
 */

/**
 * Special {@link SwappedByteBuf} for {@link ByteBuf}s that are backed by a {@code memoryAddress}.
 */

// We need to call wrapped.memoryAddress() everytime and NOT cache it as it may change if the buffer expand.
// See:
// - https://github.com/netty/netty/issues/2587
// - https://github.com/netty/netty/issues/2580

using System;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufEndianTest
{
    public static IEnumerable<object[]> Cases()
    {
        for (int kind = 0; kind < 7; kind++)
            foreach (bool little in new[] { false, true }) yield return [kind, little];
    }

    private sealed class Storage : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pooled;
        private readonly AdaptiveByteBufAllocator _adaptive;
        internal readonly ByteBuf Buffer;
        internal Storage(int kind)
        {
            if (kind is 2 or 3) _pooled = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1);
            if (kind is 4 or 5) _adaptive = new AdaptiveByteBufAllocator(false, false, Native);
            Buffer = kind switch
            {
                0 => new UnpooledHeapByteBuf(64, 512),
                1 => new UnpooledDirectByteBuf(64, 512, Native),
                2 => _pooled.HeapBuffer(64, 512), 3 => _pooled.DirectBuffer(64, 512),
                4 => _adaptive.HeapBuffer(64, 512), 5 => _adaptive.DirectBuffer(64, 512),
                6 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(Unpooled.WrappedBuffer(new byte[3]), true)
                    .AddComponent(new UnpooledDirectByteBuf(61, 61, Native).WriteZero(61), true),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            Buffer.Clear();
        }
        public void Dispose()
        {
            if (Buffer.ReferenceCount > 0) Buffer.Release();
            _pooled?.Dispose(); _adaptive?.Dispose(); Assert.Equal(0, Native.ReservedBytes);
        }
    }

    private static byte[] Wire(ulong value, int width, bool little)
        => Enumerable.Range(0, width).Select(i => unchecked((byte)(value >> (8 * (little ? i : width - 1 - i))))).ToArray();
    private static long Signed(ReadOnlySpan<byte> bytes, bool little)
    {
        ulong value = 0;
        for (int i = 0; i < bytes.Length; i++) value = (value << 8) | bytes[little ? bytes.Length - 1 - i : i];
        int shift = 64 - bytes.Length * 8; return unchecked((long)(value << shift)) >> shift;
    }
    private static void Set(ByteBuf b, int index, int width, ulong value, bool little)
    {
        if (width == 2) { if (little) b.SetShortLE(index, unchecked((int)value)); else b.SetShort(index, unchecked((int)value)); }
        else if (width == 3) { if (little) b.SetMediumLE(index, unchecked((int)value)); else b.SetMedium(index, unchecked((int)value)); }
        else if (width == 4) { if (little) b.SetIntLE(index, unchecked((int)value)); else b.SetInt(index, unchecked((int)value)); }
        else { if (little) b.SetLongLE(index, unchecked((long)value)); else b.SetLong(index, unchecked((long)value)); }
    }
    private static long Get(ByteBuf b, int index, int width, bool little) => width switch
    {
        2 => little ? b.GetShortLE(index) : b.GetShort(index), 3 => little ? b.GetMediumLE(index) : b.GetMedium(index),
        4 => little ? b.GetIntLE(index) : b.GetInt(index), _ => little ? b.GetLongLE(index) : b.GetLong(index)
    };
    private static void Write(ByteBuf b, int width, ulong value, bool little)
    {
        if (width == 2) { if (little) b.WriteShortLE(unchecked((int)value)); else b.WriteShort(unchecked((int)value)); }
        else if (width == 3) { if (little) b.WriteMediumLE(unchecked((int)value)); else b.WriteMedium(unchecked((int)value)); }
        else if (width == 4) { if (little) b.WriteIntLE(unchecked((int)value)); else b.WriteInt(unchecked((int)value)); }
        else { if (little) b.WriteLongLE(unchecked((long)value)); else b.WriteLong(unchecked((long)value)); }
    }
    private static long Read(ByteBuf b, int width, bool little) => width switch
    {
        2 => little ? b.ReadShortLE() : b.ReadShort(), 3 => little ? b.ReadMediumLE() : b.ReadMedium(),
        4 => little ? b.ReadIntLE() : b.ReadInt(), _ => little ? b.ReadLongLE() : b.ReadLong()
    };

    [Theory] [MemberData(nameof(Cases))]
    public void ExplicitAbsoluteAndRelativeOperationsPreserveSignedWidthsAndCursorEffects(int kind, bool little)
    {
        using var storage = new Storage(kind); ByteBuf b = storage.Buffer;
        foreach (int width in new[] { 2, 3, 4, 8 })
            foreach (ulong value in new ulong[] { 0, 1, 0x7fffffffffffffff, 0x8000000000000000, ulong.MaxValue, 0x0123456789abcdef })
            {
                byte[] expected = Wire(value, width, little); b.SetIndex(2, 13); Set(b, 1, width, value, little);
                Assert.Equal(expected, ByteBufUtil.GetBytes(b, 1, width)); Assert.Equal(2, b.ReaderIndex); Assert.Equal(13, b.WriterIndex);
                foreach (bool accessLittle in new[] { false, true }) Assert.Equal(Signed(expected, accessLittle), Get(b, 1, width, accessLittle));
                ulong mask = width == 8 ? ulong.MaxValue : (1UL << (width * 8)) - 1;
                ulong unsigned = width switch
                {
                    2 => little ? b.GetUnsignedShortLE(1) : b.GetUnsignedShort(1),
                    3 => (ulong)(little ? b.GetUnsignedMediumLE(1) : b.GetUnsignedMedium(1)),
                    4 => little ? b.GetUnsignedIntLE(1) : b.GetUnsignedInt(1), _ => unchecked((ulong)Get(b, 1, width, little))
                };
                Assert.Equal(value & mask, unsigned);
                b.Clear().WriteByte(90); Write(b, width, value, little); b.WriteByte(107);
                Assert.Equal(new byte[] { 90 }.Concat(expected).Append((byte)107), ByteBufUtil.GetBytes(b));
                b.ReaderIndex = 1; Assert.Equal(Signed(expected, little), Read(b, width, little));
                Assert.Equal(width + 1, b.ReaderIndex); Assert.Equal(width + 2, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
            }
    }

    [Theory] [MemberData(nameof(Cases))]
    public void CharacterAndFloatingAccessRetainUtf16UnitsAndRawBitPatterns(int kind, bool little)
    {
        using var storage = new Storage(kind); ByteBuf b = storage.Buffer;
        foreach (uint bits in new uint[] { 0, 0x80000000, 1, 0x7f800000, 0xff800000, 0x7fc00123, 0xffc01234 })
        {
            float value = BitConverter.Int32BitsToSingle(unchecked((int)bits));
            if (little) b.SetFloatLE(1, value); else b.SetFloat(1, value);
            Assert.Equal(Wire(bits, 4, little), ByteBufUtil.GetBytes(b, 1, 4));
            Assert.Equal(bits, unchecked((uint)BitConverter.SingleToInt32Bits(little ? b.GetFloatLE(1) : b.GetFloat(1))));
            b.Clear(); if (little) b.WriteFloatLE(value); else b.WriteFloat(value);
            Assert.Equal(bits, unchecked((uint)BitConverter.SingleToInt32Bits(little ? b.ReadFloatLE() : b.ReadFloat())));
        }
        foreach (ulong bits in new ulong[] { 0, 0x8000000000000000, 1, 0x7ff0000000000000, 0xfff0000000000000, 0x7ff8000000001234 })
        {
            double value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            if (little) b.SetDoubleLE(1, value); else b.SetDouble(1, value);
            Assert.Equal(Wire(bits, 8, little), ByteBufUtil.GetBytes(b, 1, 8));
            Assert.Equal(bits, unchecked((ulong)BitConverter.DoubleToInt64Bits(little ? b.GetDoubleLE(1) : b.GetDouble(1))));
            b.Clear(); if (little) b.WriteDoubleLE(value); else b.WriteDouble(value);
            Assert.Equal(bits, unchecked((ulong)BitConverter.DoubleToInt64Bits(little ? b.ReadDoubleLE() : b.ReadDouble())));
        }
        foreach (char unit in new[] { '\0', '\u0080', '\ud800', '\udfff', '\uffff' })
        {
            if (little) b.SetShortLE(1, unit); else b.SetChar(1, unit);
            Assert.Equal(unit, little ? (char)b.GetUnsignedShortLE(1) : b.GetChar(1));
            b.Clear(); if (little) b.WriteShortLE(unit); else b.WriteChar(unit);
            Assert.Equal(unit, little ? (char)b.ReadUnsignedShortLE() : b.ReadChar());
        }
    }

    [Theory] [MemberData(nameof(Cases))]
    public void ExplicitSelectionFollowsViewsAndCopiesWithoutASecondIndexOwner(int kind, bool little)
    {
        using var storage = new Storage(kind); ByteBuf root = storage.Buffer;
        root.WriteBytes(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray()).SetIndex(1, 15);
        ByteBuf slice = root.Slice(1, 12), duplicate = root.Duplicate(), readOnly = root.AsReadOnly(), copy = root.Copy(1, 12);
        try
        {
            long expected = Signed(new byte[] { 2, 3, 4, 5 }, little);
            Assert.Equal(expected, Get(slice, 0, 4, little)); Assert.Equal(expected, Get(duplicate, 1, 4, little));
            Assert.Equal(expected, Get(readOnly, 1, 4, little)); Assert.Equal(expected, Get(copy, 0, 4, little));
            Assert.Throws<NotSupportedException>(() => Set(readOnly, 1, 4, 0, little));
            slice.SetByte(0, 99); Assert.Equal(99, root.GetByte(1)); Assert.Equal(2, copy.GetByte(0));
            root.ReaderIndex = 2; Assert.Equal(1, duplicate.ReaderIndex); Assert.Equal(0, slice.ReaderIndex); Assert.Equal(0, copy.ReaderIndex);
            root.SetIndex(1, 15); ByteBuf allocated = root.ReadBytes(4);
            try { Assert.Equal(Get(root, 1, 4, little), Get(allocated, 0, 4, little)); Assert.Equal(5, root.ReaderIndex); }
            finally { allocated.Release(); }
            root.SetIndex(1, 15); ByteBuf borrowed = root.ReadSlice(4);
            Assert.Equal(Get(root, 1, 4, little), Get(borrowed, 0, 4, little)); Assert.Equal(5, root.ReaderIndex); Assert.Equal(1, root.ReferenceCount);
            root.SetIndex(1, 15); duplicate.SetIndex(1, 15);
            Assert.True(ByteBufUtil.Equals(root, duplicate)); Assert.Equal(0, ByteBufUtil.Compare(root, duplicate));
            Assert.Equal(ByteBufUtil.HashCode(root), ByteBufUtil.HashCode(duplicate));
        }
        finally { copy.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ResizingUsesTheCurrentBackingForBothWireOrders(int kind)
    {
        using var storage = new Storage(kind); ByteBuf b = storage.Buffer;
        b.WriteBytes(new byte[] { 1, 2, 3 });
        using (b.AsReadOnlyMemory(0, 3).Pin())
        {
            b.Capacity = 256;
            b.SetLongLE(193, 0x0123456789abcdef); Assert.Equal(0x0123456789abcdef, b.GetLongLE(193));
            b.SetLong(201, 0x0123456789abcdef); Assert.Equal(0x0123456789abcdef, b.GetLong(201));
            Assert.Equal(new byte[] { 1, 2, 3 }, ByteBufUtil.GetBytes(b, 0, 3)); Assert.Equal(3, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        }
    }
}
