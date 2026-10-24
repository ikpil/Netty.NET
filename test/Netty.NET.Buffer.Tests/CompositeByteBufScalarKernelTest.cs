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

public class CompositeByteBufScalarKernelTest
{
    private sealed class ProbeFailure : Exception { }
    private sealed class Observation
    {
        public readonly List<(string Method, int Index)> Visits = [];
        public T Fail<T>(string method, int index)
        { Visits.Add((method, index)); throw new ProbeFailure(); }
    }
    private sealed class HeapProbe : UnpooledHeapByteBuf
    {
        private readonly Observation _observation;
        public HeapProbe(Observation observation) : base(32)
            => _observation = observation;
        public override byte GetByte(int index) => _observation.Fail<byte>("GetByte", index);
        public override short GetShort(int index) => _observation.Fail<short>("GetShort", index);
        public override short GetShortLE(int index) => _observation.Fail<short>("GetShortLE", index);
        public override int GetUnsignedMedium(int index) => _observation.Fail<int>("GetUnsignedMedium", index);
        public override int GetUnsignedMediumLE(int index) => _observation.Fail<int>("GetUnsignedMediumLE", index);
        public override int GetInt(int index) => _observation.Fail<int>("GetInt", index);
        public override int GetIntLE(int index) => _observation.Fail<int>("GetIntLE", index);
        public override long GetLong(int index) => _observation.Fail<long>("GetLong", index);
        public override long GetLongLE(int index) => _observation.Fail<long>("GetLongLE", index);
        public override ByteBuf SetByte(int index, int value) => _observation.Fail<ByteBuf>("SetByte", index);
        public override ByteBuf SetShort(int index, int value) => _observation.Fail<ByteBuf>("SetShort", index);
        public override ByteBuf SetShortLE(int index, int value) => _observation.Fail<ByteBuf>("SetShortLE", index);
        public override ByteBuf SetMedium(int index, int value) => _observation.Fail<ByteBuf>("SetMedium", index);
        public override ByteBuf SetMediumLE(int index, int value) => _observation.Fail<ByteBuf>("SetMediumLE", index);
        public override ByteBuf SetInt(int index, int value) => _observation.Fail<ByteBuf>("SetInt", index);
        public override ByteBuf SetIntLE(int index, int value) => _observation.Fail<ByteBuf>("SetIntLE", index);
        public override ByteBuf SetLong(int index, long value) => _observation.Fail<ByteBuf>("SetLong", index);
        public override ByteBuf SetLongLE(int index, long value) => _observation.Fail<ByteBuf>("SetLongLE", index);
    }
    private sealed class NativeProbe : UnpooledDirectByteBuf
    {
        private readonly Observation _observation;
        public NativeProbe(Observation observation, NativeMemoryAllocator native) : base(32, 32, native)
            => _observation = observation;
        public override byte GetByte(int index) => _observation.Fail<byte>("GetByte", index);
        public override short GetShort(int index) => _observation.Fail<short>("GetShort", index);
        public override short GetShortLE(int index) => _observation.Fail<short>("GetShortLE", index);
        public override int GetUnsignedMedium(int index) => _observation.Fail<int>("GetUnsignedMedium", index);
        public override int GetUnsignedMediumLE(int index) => _observation.Fail<int>("GetUnsignedMediumLE", index);
        public override int GetInt(int index) => _observation.Fail<int>("GetInt", index);
        public override int GetIntLE(int index) => _observation.Fail<int>("GetIntLE", index);
        public override long GetLong(int index) => _observation.Fail<long>("GetLong", index);
        public override long GetLongLE(int index) => _observation.Fail<long>("GetLongLE", index);
        public override ByteBuf SetByte(int index, int value) => _observation.Fail<ByteBuf>("SetByte", index);
        public override ByteBuf SetShort(int index, int value) => _observation.Fail<ByteBuf>("SetShort", index);
        public override ByteBuf SetShortLE(int index, int value) => _observation.Fail<ByteBuf>("SetShortLE", index);
        public override ByteBuf SetMedium(int index, int value) => _observation.Fail<ByteBuf>("SetMedium", index);
        public override ByteBuf SetMediumLE(int index, int value) => _observation.Fail<ByteBuf>("SetMediumLE", index);
        public override ByteBuf SetInt(int index, int value) => _observation.Fail<ByteBuf>("SetInt", index);
        public override ByteBuf SetIntLE(int index, int value) => _observation.Fail<ByteBuf>("SetIntLE", index);
        public override ByteBuf SetLong(int index, long value) => _observation.Fail<ByteBuf>("SetLong", index);
        public override ByteBuf SetLongLE(int index, long value) => _observation.Fail<ByteBuf>("SetLongLE", index);
    }

    private static readonly int[] Widths = [1, 2, 2, 3, 3, 4, 4, 8, 8];
    private static readonly string[] Getters = ["Byte", "Short", "ShortLE", "UnsignedMedium", "UnsignedMediumLE", "Int", "IntLE", "Long", "LongLE"];
    private static readonly string[] Setters = ["Byte", "Short", "ShortLE", "Medium", "MediumLE", "Int", "IntLE", "Long", "LongLE"];
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        for (int operation = 0; operation < 9; ++operation)
        foreach (bool boundary in new[] { false, true })
        foreach (bool relative in new[] { false, true })
        foreach (bool write in new[] { false, true })
            yield return [direct, form, operation, boundary, relative, write];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ScalarKernelsDispatchToComponentMethodsThroughViews(bool direct, int form, int operation,
        bool boundary, bool relative, bool write)
    {
        var native = new NativeMemoryAllocator();
        var observation = new Observation();
        ByteBuf first = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf second = direct ? new NativeProbe(observation, native) : new HeapProbe(observation);
        ByteBuf owner = null;
        ByteBuf retained = null;
        try
        {
            byte[] a = new byte[32], b = new byte[32];
            for (int i = 0; i < 32; ++i) { a[i] = (byte)(i + 1); b[i] = (byte)(i + 33); }
            first.SetBytes(0, a).SetIndex(5, 21); second.SetBytes(0, b).SetIndex(3, 19);
            owner = form == 4 ? Unpooled.WrappedUnmodifiableBuffer(first, second)
                : new CompositeByteBuf().AddComponent(first, true).AddComponent(second, true);
            ByteBuf caller = form switch
            {
                1 => owner.Slice(4, 24), 2 => owner.Duplicate(), 3 => owner.AsReadOnly(),
                5 => PooledDerivedByteBuf.NewSlice(owner, owner, 4, 24),
                6 => PooledDerivedByteBuf.NewDuplicate(owner, owner, 0, 32), _ => owner
            };
            if (form >= 5) retained = caller;
            int rootIndex = boundary ? 15 : 8, index = rootIndex - (form is 1 or 5 ? 4 : 0);
            int width = Widths[operation];
            if (relative) caller.SetIndex(index, write ? index : index + width);
            else caller.SetIndex(1, 2);
            caller.MarkReaderIndex().MarkWriterIndex();
            int reader = caller.ReaderIndex, writer = caller.WriterIndex;
            Action request = () =>
            {
                if (write) Write(caller, operation, index, relative);
                else Read(caller, operation, index, relative);
            };
            if (write && caller.IsReadOnly)
            {
                Assert.Throws<NotSupportedException>(request); Assert.Empty(observation.Visits);
            }
            else
            {
                Assert.Throws<ProbeFailure>(request);
                string method = (write ? "Set" : "Get") + (boundary && width > 1 ? "Byte" : (write ? Setters : Getters)[operation]);
                Assert.Equal((method, rootIndex + (form == 4 ? 0 : 5)), Assert.Single(observation.Visits));
            }
            // CLR keeps relative cursors unchanged when a child callback fails; Java WriteByte increments first.
            Assert.Equal(reader, caller.ReaderIndex); Assert.Equal(writer, caller.WriterIndex);
            caller.SetIndex(0, caller.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(reader, caller.ReaderIndex); Assert.Equal(writer, caller.WriterIndex);
            Assert.Equal(a, first.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(b, second.AsReadOnlyMemory(0, 32).ToArray());
            Assert.Equal(5, first.ReaderIndex); Assert.Equal(21, first.WriterIndex);
            Assert.Equal(3, second.ReaderIndex); Assert.Equal(19, second.WriterIndex);
            Assert.Equal(form >= 5 ? 2 : 1, owner.ReferenceCount);
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally
        {
            if (retained?.ReferenceCount > 0) retained.Release();
            if (owner?.ReferenceCount > 0) owner.Release();
            if (first.ReferenceCount > 0) first.Release();
            if (second.ReferenceCount > 0) second.Release();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    private static long Read(ByteBuf buffer, int operation, int index, bool relative) => operation switch
    {
        0 => relative ? buffer.ReadByte() : buffer.GetByte(index),
        1 => relative ? buffer.ReadShort() : buffer.GetShort(index),
        2 => relative ? buffer.ReadShortLE() : buffer.GetShortLE(index),
        3 => relative ? buffer.ReadUnsignedMedium() : buffer.GetUnsignedMedium(index),
        4 => relative ? buffer.ReadUnsignedMediumLE() : buffer.GetUnsignedMediumLE(index),
        5 => relative ? buffer.ReadInt() : buffer.GetInt(index),
        6 => relative ? buffer.ReadIntLE() : buffer.GetIntLE(index),
        7 => relative ? buffer.ReadLong() : buffer.GetLong(index),
        _ => relative ? buffer.ReadLongLE() : buffer.GetLongLE(index)
    };
    private static ByteBuf Write(ByteBuf buffer, int operation, int index, bool relative) => operation switch
    {
        0 => relative ? buffer.WriteByte(0x55667788) : buffer.SetByte(index, 0x55667788),
        1 => relative ? buffer.WriteShort(0x55667788) : buffer.SetShort(index, 0x55667788),
        2 => relative ? buffer.WriteShortLE(0x55667788) : buffer.SetShortLE(index, 0x55667788),
        3 => relative ? buffer.WriteMedium(0x55667788) : buffer.SetMedium(index, 0x55667788),
        4 => relative ? buffer.WriteMediumLE(0x55667788) : buffer.SetMediumLE(index, 0x55667788),
        5 => relative ? buffer.WriteInt(0x55667788) : buffer.SetInt(index, 0x55667788),
        6 => relative ? buffer.WriteIntLE(0x55667788) : buffer.SetIntLE(index, 0x55667788),
        7 => relative ? buffer.WriteLong(0x1122334455667788L) : buffer.SetLong(index, 0x1122334455667788L),
        _ => relative ? buffer.WriteLongLE(0x1122334455667788L) : buffer.SetLongLE(index, 0x1122334455667788L)
    };
}
