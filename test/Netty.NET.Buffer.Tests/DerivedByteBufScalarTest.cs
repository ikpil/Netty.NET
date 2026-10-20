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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DerivedByteBufScalarTest
{
    private sealed record Visit(string Method, int Index, ulong Value);
    private sealed class Observation
    {
        public readonly List<Visit> Visits = new();
        public bool Fail, Alter;
        public void Record(string method, int index, ulong value = 0)
        {
            Visits.Add(new(method, index, value));
            if (Fail) throw new IOException("parent scalar policy");
        }
    }
    private sealed class ObservedHeap(Observation o) : UnpooledHeapByteBuf(64, 128)
    {
        public override byte GetByte(int index)
        {
            o.Record("GetByte", index);
            return unchecked((byte)(base.GetByte(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetByte(int index, int value)
        {
            o.Record("SetByte", index, unchecked((ulong)value));
            return base.SetByte(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override short GetShort(int index)
        {
            o.Record("GetShort", index);
            return unchecked((short)(base.GetShort(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetShort(int index, int value)
        {
            o.Record("SetShort", index, unchecked((ulong)value));
            return base.SetShort(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override short GetShortLE(int index)
        {
            o.Record("GetShortLE", index);
            return unchecked((short)(base.GetShortLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetShortLE(int index, int value)
        {
            o.Record("SetShortLE", index, unchecked((ulong)value));
            return base.SetShortLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetUnsignedMedium(int index)
        {
            o.Record("GetUnsignedMedium", index);
            return unchecked((int)(base.GetUnsignedMedium(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetMedium(int index, int value)
        {
            o.Record("SetMedium", index, unchecked((ulong)value));
            return base.SetMedium(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetUnsignedMediumLE(int index)
        {
            o.Record("GetUnsignedMediumLE", index);
            return unchecked((int)(base.GetUnsignedMediumLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetMediumLE(int index, int value)
        {
            o.Record("SetMediumLE", index, unchecked((ulong)value));
            return base.SetMediumLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetInt(int index)
        {
            o.Record("GetInt", index);
            return unchecked((int)(base.GetInt(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetInt(int index, int value)
        {
            o.Record("SetInt", index, unchecked((ulong)value));
            return base.SetInt(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetIntLE(int index)
        {
            o.Record("GetIntLE", index);
            return unchecked((int)(base.GetIntLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetIntLE(int index, int value)
        {
            o.Record("SetIntLE", index, unchecked((ulong)value));
            return base.SetIntLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override long GetLong(int index)
        {
            o.Record("GetLong", index);
            return unchecked((long)(base.GetLong(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetLong(int index, long value)
        {
            o.Record("SetLong", index, unchecked((ulong)value));
            return base.SetLong(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override long GetLongLE(int index)
        {
            o.Record("GetLongLE", index);
            return unchecked((long)(base.GetLongLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetLongLE(int index, long value)
        {
            o.Record("SetLongLE", index, unchecked((ulong)value));
            return base.SetLongLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
    }
    private sealed class ObservedDirect(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(64, 128, native)
    {
        public override byte GetByte(int index)
        {
            o.Record("GetByte", index);
            return unchecked((byte)(base.GetByte(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetByte(int index, int value)
        {
            o.Record("SetByte", index, unchecked((ulong)value));
            return base.SetByte(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override short GetShort(int index)
        {
            o.Record("GetShort", index);
            return unchecked((short)(base.GetShort(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetShort(int index, int value)
        {
            o.Record("SetShort", index, unchecked((ulong)value));
            return base.SetShort(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override short GetShortLE(int index)
        {
            o.Record("GetShortLE", index);
            return unchecked((short)(base.GetShortLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetShortLE(int index, int value)
        {
            o.Record("SetShortLE", index, unchecked((ulong)value));
            return base.SetShortLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetUnsignedMedium(int index)
        {
            o.Record("GetUnsignedMedium", index);
            return unchecked((int)(base.GetUnsignedMedium(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetMedium(int index, int value)
        {
            o.Record("SetMedium", index, unchecked((ulong)value));
            return base.SetMedium(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetUnsignedMediumLE(int index)
        {
            o.Record("GetUnsignedMediumLE", index);
            return unchecked((int)(base.GetUnsignedMediumLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetMediumLE(int index, int value)
        {
            o.Record("SetMediumLE", index, unchecked((ulong)value));
            return base.SetMediumLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetInt(int index)
        {
            o.Record("GetInt", index);
            return unchecked((int)(base.GetInt(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetInt(int index, int value)
        {
            o.Record("SetInt", index, unchecked((ulong)value));
            return base.SetInt(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override int GetIntLE(int index)
        {
            o.Record("GetIntLE", index);
            return unchecked((int)(base.GetIntLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetIntLE(int index, int value)
        {
            o.Record("SetIntLE", index, unchecked((ulong)value));
            return base.SetIntLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override long GetLong(int index)
        {
            o.Record("GetLong", index);
            return unchecked((long)(base.GetLong(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetLong(int index, long value)
        {
            o.Record("SetLong", index, unchecked((ulong)value));
            return base.SetLong(index, value ^ (o.Alter ? 0x35 : 0));
        }
        public override long GetLongLE(int index)
        {
            o.Record("GetLongLE", index);
            return unchecked((long)(base.GetLongLE(index) ^ (o.Alter ? 0x35 : 0)));
        }
        public override ByteBuf SetLongLE(int index, long value)
        {
            o.Record("SetLongLE", index, unchecked((ulong)value));
            return base.SetLongLE(index, value ^ (o.Alter ? 0x35 : 0));
        }
    }
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Observation Observation = new();
        public readonly ByteBuf Root, View, Owner;
        public readonly int Offset;
        public Storage(bool direct, int kind)
        {
            Root = direct ? new ObservedDirect(Observation, Native) : new ObservedHeap(Observation);
            for (int i = 0; i < 64; i++) Root.WriteByte(128 + i);
            Root.SetIndex(4, 60).MarkReaderIndex().MarkWriterIndex();
            if (kind is >= 5 and <= 9)
                Owner = kind is 6 or 8 ? PooledDerivedByteBuf.NewDuplicate(Root, Root, 4, 60)
                    : PooledDerivedByteBuf.NewSlice(Root, Root, 5, 40);
            View = kind switch
            {
                0 => Root.Duplicate(), 1 => Root.Slice(5, 40), 2 => Root.AsReadOnly(),
                3 => Root.Slice(5, 40).Slice(2, 24), 4 => Root.Slice(5, 40).AsReadOnly(),
                5 or 6 => Owner, 7 => Owner.Slice(2, 24), 8 or 9 => Owner.Duplicate(),
                _ => Root.Slice(0, 64)
            };
            Offset = kind is 3 or 7 ? 7 : kind is 1 or 4 or 5 or 9 ? 5 : 0;
            View.SetIndex(1, 10).MarkReaderIndex().MarkWriterIndex();
            Observation.Visits.Clear();
        }
        public void Dispose()
        {
            if (Owner?.ReferenceCount > 0) Assert.True(Owner.Release());
            if (Root.ReferenceCount > 0) Assert.True(Root.Release());
            Assert.Equal(0, Native.ReservedBytes);
        }
    }
    private const ulong Value = 0xfedcba9876543210UL;
    private static readonly int[] Widths = [1, 2, 2, 3, 3, 4, 4, 8, 8, 1, 3, 3, 2, 2, 4, 4, 2, 4, 4, 8, 8];
    private static readonly int[] Kernels = [0, 1, 2, 3, 4, 5, 6, 7, 8, 0, 3, 4, 1, 2, 5, 6, 1, 5, 6, 7, 8];
    private static readonly string[] GetterNames = ["Byte", "Short", "ShortLE", "UnsignedMedium", "UnsignedMediumLE", "Int", "IntLE", "Long", "LongLE"];
    private static readonly string[] SetterNames = ["Byte", "Short", "ShortLE", "Medium", "MediumLE", "Int", "IntLE", "Long", "LongLE"];
    private static bool Little(int type) => type is 2 or 4 or 6 or 8 or 11 or 13 or 15 or 18 or 20;
    private static ulong Decode(byte[] bytes, int index, int type)
    {
        ulong bits = 0; int width = Widths[type];
        for (int i = 0; i < width; i++) bits |= (ulong)bytes[index + i] << (8 * (Little(type) ? i : width - 1 - i));
        return bits;
    }
    private static ulong Result(ulong bits, int type)
    {
        if (type == 9) return bits == 0 ? 0UL : 1UL;
        if (type is 1 or 2) return unchecked((ulong)(short)bits);
        if (type is 5 or 6) return unchecked((ulong)(int)bits);
        if (type is 10 or 11) return unchecked((ulong)((int)(bits << 8) >> 8));
        return bits;
    }
    private static void Encode(byte[] bytes, int index, int type, ulong bits)
    {
        for (int i = 0; i < Widths[type]; i++) bytes[index + i] = unchecked((byte)(bits >> (8 * (Little(type) ? i : Widths[type] - 1 - i))));
    }
    private static ulong Written(int type) => type == 9 ? 1UL : Value;
    private static byte[] Snapshot(Storage s) => s.Root.AsReadOnlySpan(0, 64).ToArray();
    private static ulong Read(ByteBuf b, int type, int index, bool relative) => type switch
    {
        0 => unchecked((ulong)(relative ? b.ReadByte() : b.GetByte(index))),
        1 => unchecked((ulong)(relative ? b.ReadShort() : b.GetShort(index))),
        2 => unchecked((ulong)(relative ? b.ReadShortLE() : b.GetShortLE(index))),
        3 => unchecked((ulong)(relative ? b.ReadUnsignedMedium() : b.GetUnsignedMedium(index))),
        4 => unchecked((ulong)(relative ? b.ReadUnsignedMediumLE() : b.GetUnsignedMediumLE(index))),
        5 => unchecked((ulong)(relative ? b.ReadInt() : b.GetInt(index))),
        6 => unchecked((ulong)(relative ? b.ReadIntLE() : b.GetIntLE(index))),
        7 => unchecked((ulong)(relative ? b.ReadLong() : b.GetLong(index))),
        8 => unchecked((ulong)(relative ? b.ReadLongLE() : b.GetLongLE(index))),
        9 => (relative ? b.ReadBoolean() : b.GetBoolean(index)) ? 1UL : 0UL,
        10 => unchecked((ulong)(relative ? b.ReadMedium() : b.GetMedium(index))),
        11 => unchecked((ulong)(relative ? b.ReadMediumLE() : b.GetMediumLE(index))),
        12 => unchecked((ulong)(relative ? b.ReadUnsignedShort() : b.GetUnsignedShort(index))),
        13 => unchecked((ulong)(relative ? b.ReadUnsignedShortLE() : b.GetUnsignedShortLE(index))),
        14 => unchecked((ulong)(relative ? b.ReadUnsignedInt() : b.GetUnsignedInt(index))),
        15 => unchecked((ulong)(relative ? b.ReadUnsignedIntLE() : b.GetUnsignedIntLE(index))),
        16 => unchecked((ulong)(relative ? b.ReadChar() : b.GetChar(index))),
        17 => unchecked((ulong)BitConverter.SingleToInt32Bits((relative ? b.ReadFloat() : b.GetFloat(index)))) & uint.MaxValue,
        18 => unchecked((ulong)BitConverter.SingleToInt32Bits((relative ? b.ReadFloatLE() : b.GetFloatLE(index)))) & uint.MaxValue,
        19 => unchecked((ulong)BitConverter.DoubleToInt64Bits((relative ? b.ReadDouble() : b.GetDouble(index)))),
        20 => unchecked((ulong)BitConverter.DoubleToInt64Bits((relative ? b.ReadDoubleLE() : b.GetDoubleLE(index)))),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static ByteBuf Write(ByteBuf b, int type, int index, bool relative) => type switch
    {
        0 => relative ? b.WriteByte(unchecked((int)Value)) : b.SetByte(index, unchecked((int)Value)),
        1 => relative ? b.WriteShort(unchecked((int)Value)) : b.SetShort(index, unchecked((int)Value)),
        2 => relative ? b.WriteShortLE(unchecked((int)Value)) : b.SetShortLE(index, unchecked((int)Value)),
        3 => relative ? b.WriteMedium(unchecked((int)Value)) : b.SetMedium(index, unchecked((int)Value)),
        4 => relative ? b.WriteMediumLE(unchecked((int)Value)) : b.SetMediumLE(index, unchecked((int)Value)),
        5 => relative ? b.WriteInt(unchecked((int)Value)) : b.SetInt(index, unchecked((int)Value)),
        6 => relative ? b.WriteIntLE(unchecked((int)Value)) : b.SetIntLE(index, unchecked((int)Value)),
        7 => relative ? b.WriteLong(unchecked((long)Value)) : b.SetLong(index, unchecked((long)Value)),
        8 => relative ? b.WriteLongLE(unchecked((long)Value)) : b.SetLongLE(index, unchecked((long)Value)),
        9 => relative ? b.WriteBoolean(true) : b.SetBoolean(index, true),
        10 => relative ? b.WriteMedium(unchecked((int)Value)) : b.SetMedium(index, unchecked((int)Value)),
        11 => relative ? b.WriteMediumLE(unchecked((int)Value)) : b.SetMediumLE(index, unchecked((int)Value)),
        12 => relative ? b.WriteShort(unchecked((int)Value)) : b.SetShort(index, unchecked((int)Value)),
        13 => relative ? b.WriteShortLE(unchecked((int)Value)) : b.SetShortLE(index, unchecked((int)Value)),
        14 => relative ? b.WriteInt(unchecked((int)Value)) : b.SetInt(index, unchecked((int)Value)),
        15 => relative ? b.WriteIntLE(unchecked((int)Value)) : b.SetIntLE(index, unchecked((int)Value)),
        16 => relative ? b.WriteChar(unchecked((char)Value)) : b.SetChar(index, unchecked((char)Value)),
        17 => relative ? b.WriteFloat(BitConverter.Int32BitsToSingle(unchecked((int)Value))) : b.SetFloat(index, BitConverter.Int32BitsToSingle(unchecked((int)Value))),
        18 => relative ? b.WriteFloatLE(BitConverter.Int32BitsToSingle(unchecked((int)Value))) : b.SetFloatLE(index, BitConverter.Int32BitsToSingle(unchecked((int)Value))),
        19 => relative ? b.WriteDouble(BitConverter.Int64BitsToDouble(unchecked((long)Value))) : b.SetDouble(index, BitConverter.Int64BitsToDouble(unchecked((long)Value))),
        20 => relative ? b.WriteDoubleLE(BitConverter.Int64BitsToDouble(unchecked((long)Value))) : b.SetDoubleLE(index, BitConverter.Int64BitsToDouble(unchecked((long)Value))),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int kind = 0; kind < 11; kind++)
        for (int type = 0; type < 21; type++)
        foreach (bool write in new[] { false, true }) yield return [direct, kind, type, write];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void AbsolutePoliciesAndRelativeKernelsFollowOriginalViewRules(bool direct, int kind, int type, bool write)
    {
        using var s = new Storage(direct, kind);
        byte[] expected = Snapshot(s); int refs = s.Root.ReferenceCount, width = Widths[type];
        s.Observation.Alter = true;
        bool forwards = kind is 0 or 2 or 6 or 8;
        if (write && s.View.IsReadOnly)
        {
            Assert.Throws<NotSupportedException>(() => Write(s.View, type, 3, false));
            Assert.Throws<NotSupportedException>(() => Write(s.View, type, 3, true));
            Assert.Empty(s.Observation.Visits); Assert.Equal(expected, Snapshot(s)); return;
        }
        ulong bits = Decode(expected, s.Offset + 3, type);
        if (write)
        {
            Assert.Same(s.View, Write(s.View, type, 3, false));
            Encode(expected, s.Offset + 3, type, Written(type) ^ (forwards ? 0x35UL : 0));
        }
        else Assert.Equal(Result(bits ^ (forwards ? 0x35UL : 0), type), Read(s.View, type, 3, false));
        if (forwards)
        {
            Visit v = Assert.Single(s.Observation.Visits);
            Assert.Equal((write ? "Set" : "Get") + (write ? SetterNames : GetterNames)[Kernels[type]], v.Method);
            Assert.Equal(s.Offset + 3, v.Index);
        }
        else Assert.Empty(s.Observation.Visits);
        Assert.Equal(expected, Snapshot(s)); Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(10, s.View.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(10, s.View.WriterIndex);
        Assert.Equal(refs, s.Root.ReferenceCount);

        bool relativeForwards = kind == 6 && write && type is 8 or 20;
        s.View.SetIndex(1, write ? 2 : 10); s.Observation.Visits.Clear(); s.Observation.Fail = !relativeForwards;
        if (write)
        {
            Assert.Same(s.View, Write(s.View, type, 0, true));
            Encode(expected, s.Offset + 2, type, Written(type) ^ (relativeForwards ? 0x35UL : 0));
        }
        else Assert.Equal(Result(Decode(expected, s.Offset + 1, type), type), Read(s.View, type, 0, true));
        if (relativeForwards)
        {
            Visit v = Assert.Single(s.Observation.Visits);
            Assert.Equal("SetLongLE", v.Method); Assert.Equal(2, v.Index);
        }
        else Assert.Empty(s.Observation.Visits);
        Assert.Equal(expected, Snapshot(s));
        Assert.Equal(write ? 1 : 1 + width, s.View.ReaderIndex); Assert.Equal(write ? 2 + width : 10, s.View.WriterIndex);
        if (forwards)
        {
            s.Observation.Visits.Clear(); s.Observation.Fail = true;
            if (write) Assert.Throws<IOException>(() => Write(s.View, type, 3, false));
            else Assert.Throws<IOException>(() => Read(s.View, type, 3, false));
            Assert.Single(s.Observation.Visits); Assert.Equal(expected, Snapshot(s));
        }
        if (relativeForwards)
        {
            s.View.SetIndex(1, 2); s.Observation.Visits.Clear(); s.Observation.Fail = true;
            Assert.Throws<IOException>(() => Write(s.View, type, 0, true));
            Assert.Single(s.Observation.Visits); Assert.Equal(1, s.View.ReaderIndex); Assert.Equal(2, s.View.WriterIndex);
            Assert.Equal(expected, Snapshot(s));
        }
        s.Observation.Fail = false; s.Observation.Visits.Clear();
        int invalid = s.View.Capacity - width + 1;
        if (write) Assert.Throws<ArgumentOutOfRangeException>(() => Write(s.View, type, invalid, false));
        else Assert.Throws<ArgumentOutOfRangeException>(() => Read(s.View, type, invalid, false));
        Assert.Empty(s.Observation.Visits); Assert.Equal(4, s.Root.ReaderIndex); Assert.Equal(60, s.Root.WriterIndex);
        if (s.Owner != null)
        {
            Assert.True(s.Owner.Release()); Assert.Equal(1, s.Root.ReferenceCount);
            if (write) Assert.Throws<IllegalReferenceCountException>(() => Write(s.View, type, 3, false));
            else Assert.Throws<IllegalReferenceCountException>(() => Read(s.View, type, 3, false));
            Assert.Empty(s.Observation.Visits);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootRelativeAccessUsesStorageRatherThanPublicAbsoluteOverrides(bool direct)
    {
        using var s = new Storage(direct, 0);
        s.Observation.Fail = s.Observation.Alter = true;
        for (int type = 0; type < 21; type++)
        {
            byte[] expected = Snapshot(s); s.Root.SetIndex(1, 10);
            Assert.Equal(Result(Decode(expected, 1, type), type), Read(s.Root, type, 0, true));
            Assert.Equal(1 + Widths[type], s.Root.ReaderIndex); Assert.Equal(10, s.Root.WriterIndex);
            s.Root.SetIndex(1, 2); Assert.Same(s.Root, Write(s.Root, type, 0, true));
            Encode(expected, 2, type, Written(type)); Assert.Equal(expected, Snapshot(s));
            Assert.Equal(1, s.Root.ReaderIndex); Assert.Equal(2 + Widths[type], s.Root.WriterIndex);
        }
        Assert.Empty(s.Observation.Visits);
    }
    private sealed class RelativePolicy : UnpooledHeapByteBuf
    {
        public int Calls;
        public ulong Bits;
        public ByteBuf Returned;
        public RelativePolicy() : base(16, 16) { SetIndex(1, 10); }
        public override byte ReadByte() { Calls++; return 0x89; }
        public override short ReadShort() { Calls++; return unchecked((short)0x89ab); }
        public override ByteBuf WriteByte(int value) { Calls++; Bits = unchecked((ulong)value); return Returned; }
        public override ByteBuf WriteShort(int value) { Calls++; Bits = unchecked((ulong)value); return Returned; }
        public override int ReadInt() { Calls++; return unchecked((int)0x89abcdef); }
        public override int ReadIntLE() { Calls++; return unchecked((int)0x89abcdef); }
        public override long ReadLong() { Calls++; return unchecked((long)0x89abcdef01234567UL); }
        public override long ReadLongLE() { Calls++; return unchecked((long)0x89abcdef01234567UL); }
        public override int ReadUnsignedMedium() { Calls++; return 0x89abcd; }
        public override int ReadUnsignedMediumLE() { Calls++; return 0x89abcd; }
        public override ByteBuf WriteInt(int value) { Calls++; Bits = unchecked((uint)value); return Returned; }
        public override ByteBuf WriteIntLE(int value) { Calls++; Bits = unchecked((uint)value); return Returned; }
        public override ByteBuf WriteLong(long value) { Calls++; Bits = unchecked((ulong)value); return Returned; }
        public override ByteBuf WriteLongLE(long value) { Calls++; Bits = unchecked((ulong)value); return Returned; }
    }
    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void FloatingPointAndSignedMediumComposeRelativeIntegerPolicies(int type)
    {
        var policy = new RelativePolicy();
        var other = new UnpooledHeapByteBuf(1, 1);
        try
        {
            policy.Returned = other;
            ulong bits = type == 9 ? 0x89UL : type == 16 ? 0x89abUL : type is 10 or 11 ? 0x89abcdUL : Widths[type] == 4 ? 0x89abcdefUL : 0x89abcdef01234567UL;
            Assert.Equal(Result(bits, type), Read(policy, type, 0, true));
            Assert.Equal(1, policy.Calls);
            if (type is 9 or 16 or >= 17)
            {
                Assert.Same(type is 18 or 20 ? other : policy, Write(policy, type, 0, true));
                ulong written = type == 9 ? 1 : type == 16 ? Value & char.MaxValue : Widths[type] == 4 ? Value & uint.MaxValue : Value;
                Assert.Equal(written, policy.Bits);
                Assert.Equal(2, policy.Calls);
            }
            Assert.Equal(1, policy.ReaderIndex); Assert.Equal(10, policy.WriterIndex);
        }
        finally { Assert.True(other.Release()); Assert.True(policy.Release()); }
    }
}
