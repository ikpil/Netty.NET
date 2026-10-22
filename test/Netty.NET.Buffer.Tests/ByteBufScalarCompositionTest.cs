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

public class ByteBufScalarCompositionTest
{
    private sealed class Failure : Exception;
    private sealed class Observation(int sample, bool fail)
    {
        public static readonly ulong[] Samples = [0, ulong.MaxValue, 0x8000000080008000UL,
            0x7ff800007fc12345UL, 0x8000000000000000UL, 0x7ff0000000000000UL];
        public readonly ulong Bits = Samples[sample];
        public readonly int IntBits = sample == 4 ? int.MinValue : sample == 5 ? 0x7f800000 : unchecked((int)Samples[sample]);
        public readonly Failure Error = new();
        public int Count, Index;
        public string Hook;
        public long Argument;
        public bool TrapUnsigned;
        public void Visit(string hook, int index = -1, long argument = 0)
        {
            ++Count; Hook = hook; Index = index; Argument = argument;
            if (fail) throw Error;
        }
    }

    private sealed class Heap(Observation o) : UnpooledHeapByteBuf(64, 64)
    {
        public override byte GetByte(int index) { o.Visit("GetByte", index); return unchecked((byte)o.Bits); }
        public override short GetShort(int index) { o.Visit("GetShort", index); return unchecked((short)o.Bits); }
        public override short GetShortLE(int index) { o.Visit("GetShortLE", index); return unchecked((short)o.Bits); }
        public override int GetInt(int index) { o.Visit("GetInt", index); return o.IntBits; }
        public override int GetIntLE(int index) { o.Visit("GetIntLE", index); return o.IntBits; }
        public override long GetLong(int index) { o.Visit("GetLong", index); return unchecked((long)o.Bits); }
        public override long GetLongLE(int index) { o.Visit("GetLongLE", index); return unchecked((long)o.Bits); }
        public override int GetUnsignedMedium(int index) { o.Visit("GetUnsignedMedium", index); return (int)(o.Bits & 0xffffff); }
        public override int GetUnsignedMediumLE(int index) { o.Visit("GetUnsignedMediumLE", index); return (int)(o.Bits & 0xffffff); }
        public override byte ReadByte() { o.Visit("ReadByte", -1); return unchecked((byte)o.Bits); }
        public override short ReadShort() { o.Visit("ReadShort", -1); return unchecked((short)o.Bits); }
        public override short ReadShortLE() { o.Visit("ReadShortLE", -1); return unchecked((short)o.Bits); }
        public override int ReadInt() { o.Visit("ReadInt", -1); return o.IntBits; }
        public override int ReadIntLE() { o.Visit("ReadIntLE", -1); return o.IntBits; }
        public override long ReadLong() { o.Visit("ReadLong", -1); return unchecked((long)o.Bits); }
        public override long ReadLongLE() { o.Visit("ReadLongLE", -1); return unchecked((long)o.Bits); }
        public override int ReadUnsignedMedium() { o.Visit("ReadUnsignedMedium", -1); return (int)(o.Bits & 0xffffff); }
        public override int ReadUnsignedMediumLE() { o.Visit("ReadUnsignedMediumLE", -1); return (int)(o.Bits & 0xffffff); }
        public override ByteBuf SetByte(int index, int value) { o.Visit("SetByte", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetShort(int index, int value) { o.Visit("SetShort", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetShortLE(int index, int value) { o.Visit("SetShortLE", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetInt(int index, int value) { o.Visit("SetInt", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetIntLE(int index, int value) { o.Visit("SetIntLE", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetLong(int index, long value) { o.Visit("SetLong", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetLongLE(int index, long value) { o.Visit("SetLongLE", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteByte(int value) { o.Visit("WriteByte", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteShort(int value) { o.Visit("WriteShort", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteShortLE(int value) { o.Visit("WriteShortLE", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteInt(int value) { o.Visit("WriteInt", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteIntLE(int value) { o.Visit("WriteIntLE", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteLong(long value) { o.Visit("WriteLong", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteLongLE(long value) { o.Visit("WriteLongLE", -1, value); return Unpooled.EmptyBuffer; }
        public override ushort GetUnsignedShort(int index)
        {
            if (o.TrapUnsigned) throw new InvalidOperationException("GetChar must use GetShort directly.");
            return base.GetUnsignedShort(index);
        }
    }

    private sealed class Direct(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(64, 64, native)
    {
        public override byte GetByte(int index) { o.Visit("GetByte", index); return unchecked((byte)o.Bits); }
        public override short GetShort(int index) { o.Visit("GetShort", index); return unchecked((short)o.Bits); }
        public override short GetShortLE(int index) { o.Visit("GetShortLE", index); return unchecked((short)o.Bits); }
        public override int GetInt(int index) { o.Visit("GetInt", index); return o.IntBits; }
        public override int GetIntLE(int index) { o.Visit("GetIntLE", index); return o.IntBits; }
        public override long GetLong(int index) { o.Visit("GetLong", index); return unchecked((long)o.Bits); }
        public override long GetLongLE(int index) { o.Visit("GetLongLE", index); return unchecked((long)o.Bits); }
        public override int GetUnsignedMedium(int index) { o.Visit("GetUnsignedMedium", index); return (int)(o.Bits & 0xffffff); }
        public override int GetUnsignedMediumLE(int index) { o.Visit("GetUnsignedMediumLE", index); return (int)(o.Bits & 0xffffff); }
        public override byte ReadByte() { o.Visit("ReadByte", -1); return unchecked((byte)o.Bits); }
        public override short ReadShort() { o.Visit("ReadShort", -1); return unchecked((short)o.Bits); }
        public override short ReadShortLE() { o.Visit("ReadShortLE", -1); return unchecked((short)o.Bits); }
        public override int ReadInt() { o.Visit("ReadInt", -1); return o.IntBits; }
        public override int ReadIntLE() { o.Visit("ReadIntLE", -1); return o.IntBits; }
        public override long ReadLong() { o.Visit("ReadLong", -1); return unchecked((long)o.Bits); }
        public override long ReadLongLE() { o.Visit("ReadLongLE", -1); return unchecked((long)o.Bits); }
        public override int ReadUnsignedMedium() { o.Visit("ReadUnsignedMedium", -1); return (int)(o.Bits & 0xffffff); }
        public override int ReadUnsignedMediumLE() { o.Visit("ReadUnsignedMediumLE", -1); return (int)(o.Bits & 0xffffff); }
        public override ByteBuf SetByte(int index, int value) { o.Visit("SetByte", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetShort(int index, int value) { o.Visit("SetShort", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetShortLE(int index, int value) { o.Visit("SetShortLE", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetInt(int index, int value) { o.Visit("SetInt", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetIntLE(int index, int value) { o.Visit("SetIntLE", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetLong(int index, long value) { o.Visit("SetLong", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf SetLongLE(int index, long value) { o.Visit("SetLongLE", index, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteByte(int value) { o.Visit("WriteByte", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteShort(int value) { o.Visit("WriteShort", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteShortLE(int value) { o.Visit("WriteShortLE", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteInt(int value) { o.Visit("WriteInt", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteIntLE(int value) { o.Visit("WriteIntLE", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteLong(long value) { o.Visit("WriteLong", -1, value); return Unpooled.EmptyBuffer; }
        public override ByteBuf WriteLongLE(long value) { o.Visit("WriteLongLE", -1, value); return Unpooled.EmptyBuffer; }
        public override ushort GetUnsignedShort(int index)
        {
            if (o.TrapUnsigned) throw new InvalidOperationException("GetChar must use GetShort directly.");
            return base.GetUnsignedShort(index);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int operation = 0; operation < 36; ++operation)
        for (int sample = 0; sample < 6; ++sample)
        foreach (bool failure in new[] { false, true }) yield return [direct, operation, sample, failure];
    }

    private static object Invoke(ByteBuf b, Observation o, int operation) => operation switch
    {
        0 => (b.GetBoolean(2) ? 1L : 0L),
        1 => b.SetBoolean(2, (o.Bits & 255) != 0),
        2 => (long)b.GetUnsignedShort(2),
        3 => (long)b.GetUnsignedShortLE(2),
        4 => (long)b.GetUnsignedInt(2),
        5 => (long)b.GetUnsignedIntLE(2),
        6 => (long)b.GetChar(2),
        7 => b.SetChar(2, unchecked((char)o.Bits)),
        8 => (long)BitConverter.SingleToInt32Bits(b.GetFloat(2)),
        9 => (long)BitConverter.SingleToInt32Bits(b.GetFloatLE(2)),
        10 => BitConverter.DoubleToInt64Bits(b.GetDouble(2)),
        11 => BitConverter.DoubleToInt64Bits(b.GetDoubleLE(2)),
        12 => b.SetFloat(2, BitConverter.Int32BitsToSingle(o.IntBits)),
        13 => b.SetFloatLE(2, BitConverter.Int32BitsToSingle(o.IntBits)),
        14 => b.SetDouble(2, BitConverter.Int64BitsToDouble(unchecked((long)o.Bits))),
        15 => b.SetDoubleLE(2, BitConverter.Int64BitsToDouble(unchecked((long)o.Bits))),
        16 => (long)b.GetMedium(2),
        17 => (long)b.GetMediumLE(2),
        18 => (b.ReadBoolean() ? 1L : 0L),
        19 => (long)b.ReadUnsignedShort(),
        20 => (long)b.ReadUnsignedShortLE(),
        21 => (long)b.ReadUnsignedInt(),
        22 => (long)b.ReadUnsignedIntLE(),
        23 => (long)b.ReadChar(),
        24 => (long)BitConverter.SingleToInt32Bits(b.ReadFloat()),
        25 => (long)BitConverter.SingleToInt32Bits(b.ReadFloatLE()),
        26 => BitConverter.DoubleToInt64Bits(b.ReadDouble()),
        27 => BitConverter.DoubleToInt64Bits(b.ReadDoubleLE()),
        28 => (long)b.ReadMedium(),
        29 => (long)b.ReadMediumLE(),
        30 => b.WriteBoolean((o.Bits & 255) != 0),
        31 => b.WriteChar(unchecked((char)o.Bits)),
        32 => b.WriteFloat(BitConverter.Int32BitsToSingle(o.IntBits)),
        33 => b.WriteFloatLE(BitConverter.Int32BitsToSingle(o.IntBits)),
        34 => b.WriteDouble(BitConverter.Int64BitsToDouble(unchecked((long)o.Bits))),
        35 => b.WriteDoubleLE(BitConverter.Int64BitsToDouble(unchecked((long)o.Bits))),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static readonly string[] Hooks =
    [
        "GetByte", "SetByte", "GetShort", "GetShortLE", "GetInt", "GetIntLE", "GetShort", "SetShort", "GetInt", "GetIntLE", "GetLong", "GetLongLE", "SetInt", "SetIntLE", "SetLong", "SetLongLE", "GetUnsignedMedium", "GetUnsignedMediumLE", "ReadByte", "ReadShort", "ReadShortLE", "ReadInt", "ReadIntLE", "ReadShort", "ReadInt", "ReadIntLE", "ReadLong", "ReadLongLE", "ReadUnsignedMedium", "ReadUnsignedMediumLE", "WriteByte", "WriteShort", "WriteInt", "WriteIntLE", "WriteLong", "WriteLongLE"
    ];

    // Source contracts: AbstractByteBuf scalar composition plus ByteBuf's LE float/double adapters.
    // Primitive overrides return fixed bits or another receiver; wrappers must preserve their original hook/return rule.
    [Theory]
    [MemberData(nameof(Cases))]
    public void CompositionPreservesPrimitiveHookWireBitsExceptionAndReturn(bool direct, int operation, int sample, bool failure)
    {
        var o = new Observation(sample, failure) { TrapUnsigned = operation == 6 };
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = direct ? new Direct(o, native) : new Heap(o);
        buffer.SetBytes(0, new byte[64]); buffer.SetIndex(3, 32).MarkReaderIndex().MarkWriterIndex();
        try
        {
            if (failure) Assert.Same(o.Error, Assert.Throws<Failure>(() => Invoke(buffer, o, operation)));
            else
            {
                object result = Invoke(buffer, o, operation);
                if (result is ByteBuf)
                    Assert.Same(operation is 13 or 15 or 33 or 35 ? Unpooled.EmptyBuffer : buffer, result);
                else
                {
                    long expected = operation switch
                    {
                        0 or 18 => (o.Bits & 255) != 0 ? 1 : 0,
                        2 or 3 or 6 or 19 or 20 or 23 => (long)(o.Bits & 65535),
                        4 or 5 or 21 or 22 => unchecked((uint)o.IntBits),
                        8 or 9 or 24 or 25 => o.IntBits,
                        10 or 11 or 26 or 27 => unchecked((long)o.Bits),
                        _ => (long)(o.Bits & 0xffffff) - ((o.Bits & 0x800000) != 0 ? 0x1000000 : 0)
                    };
                    Assert.Equal(expected, Assert.IsType<long>(result));
                }
            }
            Assert.Equal(1, o.Count); Assert.Equal(Hooks[operation], o.Hook);
            Assert.Equal(operation < 18 ? 2 : -1, o.Index);
            long argument = operation switch
            {
                1 or 30 => (o.Bits & 255) != 0 ? 1 : 0,
                7 or 31 => (long)(o.Bits & 65535),
                12 or 13 or 32 or 33 => o.IntBits,
                14 or 15 or 34 or 35 => unchecked((long)o.Bits),
                _ => 0
            };
            Assert.Equal(argument, o.Argument);
            Assert.Equal(3, buffer.ReaderIndex); Assert.Equal(32, buffer.WriterIndex);
            Assert.Equal(new byte[64], ByteBufUtil.GetBytes(buffer, 0, 64));
            buffer.SetIndex(0, 64).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(3, buffer.ReaderIndex); Assert.Equal(32, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount); Assert.Equal(1, Unpooled.EmptyBuffer.ReferenceCount);
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
