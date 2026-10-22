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

public class ByteBufWritableKernelTest
{
    private sealed class Failure : Exception;
    private sealed class Calls(bool reject)
    {
        public int Count;
        public readonly Failure Error = new();
        public void Visit() { ++Count; if (reject) throw Error; }
    }
    private sealed class Heap(Calls calls, int initial, int maximum) : UnpooledHeapByteBuf(initial, maximum)
    {
        public override ByteBuf EnsureWritable(int minimumWritableBytes)
        { calls.Visit(); return base.EnsureWritable(minimumWritableBytes); }
    }
    private sealed class Direct(Calls calls, int initial, int maximum, NativeMemoryAllocator native)
        : UnpooledDirectByteBuf(initial, maximum, native)
    {
        public override ByteBuf EnsureWritable(int minimumWritableBytes)
        { calls.Visit(); return base.EnsureWritable(minimumWritableBytes); }
    }

    private static ByteBuf Create(bool direct, Calls calls, int scenario, NativeMemoryAllocator native)
    {
        int initial = scenario == 1 ? 4 : 16, maximum = scenario == 2 ? 16 : 64;
        ByteBuf buffer = direct ? new Direct(calls, initial, maximum, native) : new Heap(calls, initial, maximum);
        for (int i = 0; i < initial; ++i) buffer.SetByte(i, 160 + i);
        return buffer.SetIndex(1, scenario == 0 ? 3 : initial).MarkReaderIndex().MarkWriterIndex();
    }

    private static byte[] WireBytes(int operation) => operation switch
    {
        0 => [239],
        1 => [205, 239],
        2 => [239, 205],
        3 => [171, 205, 239],
        4 => [239, 205, 171],
        5 => [137, 171, 205, 239],
        6 => [239, 205, 171, 137],
        7 => [136, 153, 170, 187, 204, 221, 238, 255],
        _ => [255, 238, 221, 204, 187, 170, 153, 136]
    };

    private static ByteBuf Write(ByteBuf buffer, int operation) => operation switch
    {
        0 => buffer.WriteByte(239),
        1 => buffer.WriteShort(0xabcdef),
        2 => buffer.WriteShortLE(0xabcdef),
        3 => buffer.WriteMedium(0xabcdef),
        4 => buffer.WriteMediumLE(0xabcdef),
        5 => buffer.WriteInt(unchecked((int)0x89abcdef)),
        6 => buffer.WriteIntLE(unchecked((int)0x89abcdef)),
        7 => buffer.WriteLong(unchecked((long)0x8899aabbccddeeffUL)),
        _ => buffer.WriteLongLE(unchecked((long)0x8899aabbccddeeffUL))
    };

    public static IEnumerable<object[]> PrimitiveCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int operation = 0; operation < 9; ++operation)
        for (int scenario = 0; scenario < 3; ++scenario)
        foreach (bool reject in new[] { false, true }) yield return [direct, operation, scenario, reject];
    }

    // AbstractByteBuf primitive writes call final ensureWritable0, independently
    // of public ensureWritable overrides. Bulk/zero writes have their own contracts.
    [Theory]
    [MemberData(nameof(PrimitiveCases))]
    public void PrimitiveWritesUseInternalReservationAndPreserveWireAndMarks(bool direct, int operation, int scenario, bool reject)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls(reject);
        ByteBuf buffer = Create(direct, calls, scenario, native);
        int capacity = buffer.Capacity, writer = buffer.WriterIndex;
        byte[] before = buffer.AsReadOnlySpan(0, capacity).ToArray(), wire = WireBytes(operation);
        try
        {
            if (scenario == 2) Assert.Throws<ArgumentOutOfRangeException>(() => Write(buffer, operation));
            else Assert.Same(buffer, Write(buffer, operation));
            Assert.Equal(0, calls.Count);
            Assert.Equal(scenario == 1 ? 64 : capacity, buffer.Capacity);
            Assert.Equal(1, buffer.ReaderIndex);
            Assert.Equal(scenario == 2 ? writer : writer + wire.Length, buffer.WriterIndex);
            byte[] expectedPrefix = (byte[])before.Clone();
            if (scenario != 2)
            {
                wire.AsSpan(0, Math.Min(wire.Length, capacity - writer)).CopyTo(expectedPrefix.AsSpan(writer));
                Assert.Equal(wire, buffer.AsReadOnlySpan(writer, wire.Length).ToArray());
            }
            Assert.Equal(expectedPrefix, buffer.AsReadOnlySpan(0, capacity).ToArray());
            buffer.SetIndex(0, buffer.Capacity).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); Assert.Equal(0, native.ReservedBytes); }
    }

    public static IEnumerable<object[]> PublicCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int scenario = 0; scenario < 3; ++scenario)
        foreach (bool reject in new[] { false, true }) yield return [direct, scenario, reject];
    }

    [Theory]
    [MemberData(nameof(PublicCases))]
    public void ExplicitEnsureWritableRetainsItsPublicOverride(bool direct, int scenario, bool reject)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls(reject);
        ByteBuf buffer = Create(direct, calls, scenario, native);
        int capacity = buffer.Capacity, writer = buffer.WriterIndex;
        byte[] before = buffer.AsReadOnlySpan(0, capacity).ToArray();
        try
        {
            if (reject) Assert.Same(calls.Error, Assert.Throws<Failure>(() => buffer.EnsureWritable(8)));
            else if (scenario == 2) Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(8));
            else Assert.Same(buffer, buffer.EnsureWritable(8));
            Assert.Equal(1, calls.Count);
            Assert.Equal(!reject && scenario == 1 ? 64 : capacity, buffer.Capacity);
            Assert.Equal(before, buffer.AsReadOnlySpan(0, capacity).ToArray());
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
            buffer.SetIndex(0, buffer.Capacity).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); Assert.Equal(0, native.ReservedBytes); }
    }

    private static ByteBuf NewBuffer(bool direct, int length, NativeMemoryAllocator native)
        => direct ? new UnpooledDirectByteBuf(length, int.MaxValue, native) : new UnpooledHeapByteBuf(length);

    // AbstractByteBufTest.ensureWritableWithForceDoesNotThrow / ensureWritableWithOutForceDoesNotThrow.
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void EnsureWritableDoesNotThrow(bool direct, bool force)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(direct, 8, native);
        try
        {
            buffer.WriterIndex = buffer.Capacity;
            buffer.EnsureWritable(8, force);
            Assert.True(buffer.Capacity >= 16); Assert.Equal(8, buffer.WriterIndex);
        }
        finally { buffer.Release(); Assert.Equal(0, native.ReservedBytes); }
    }

    // AbstractByteBufTest.ensureWritableWithForceAsReadyOnly / ensureWritableWithOutForceAsReadOnly.
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void EnsureWritableReadOnly(bool direct, bool force)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(direct, 8, native);
        try
        {
            buffer.WriterIndex = buffer.Capacity;
            Assert.Equal(1, buffer.AsReadOnly().EnsureWritable(8, force));
            Assert.Equal(8, buffer.Capacity); Assert.Equal(8, buffer.WriterIndex);
        }
        finally { buffer.Release(); Assert.Equal(0, native.ReservedBytes); }
    }

    // AbstractByteBufTest.testEnsureWritableAfterRelease and its releasedBuffer helper.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EnsureWritableAfterRelease(bool direct)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(direct, 8, native);
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        buffer.Clear();
        Assert.True(buffer.Release());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.EnsureWritable(16));
        Assert.Equal(0, native.ReservedBytes);
    }

    // AbstractByteBufTest.testEnsureWritableIntegerOverflow.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EnsureWritableIntegerOverflow(bool direct)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf buffer = NewBuffer(direct, 4096, native);
        buffer.WriterIndex = buffer.ReaderIndex;
        buffer.WriteByte(1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(int.MaxValue));
            Assert.Equal(4096, buffer.Capacity); Assert.Equal(1, buffer.WriterIndex);
        }
        finally { buffer.Release(); Assert.Equal(0, native.ReservedBytes); }
    }
}
