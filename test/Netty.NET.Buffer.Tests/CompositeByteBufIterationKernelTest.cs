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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public partial class CompositeByteBufIterationKernelTest
{
    private sealed class Observation { public int Gets, Iterations; }
    private sealed class HeapProbe(Observation observation) : UnpooledHeapByteBuf(32, 64)
    {
        public override byte GetByte(int index) { ++observation.Gets; return (byte)(base.GetByte(index) ^ 0x80); }
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        { ++observation.Iterations; throw new InvalidOperationException("component public iteration"); }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        { ++observation.Iterations; throw new InvalidOperationException("component public iteration"); }
    }
    private sealed class NativeProbe(Observation observation, NativeMemoryAllocator native) : UnpooledDirectByteBuf(32, 64, native)
    {
        public override byte GetByte(int index) { ++observation.Gets; return (byte)(base.GetByte(index) ^ 0x80); }
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        { ++observation.Iterations; throw new InvalidOperationException("component public iteration"); }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        { ++observation.Iterations; throw new InvalidOperationException("component public iteration"); }
    }
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Observation Observation = new();
        public readonly ByteBuf First, Second, Owner, View, Retained;
        public readonly int Offset;
        public readonly bool Fixed;
        public Storage(bool direct, int form)
        {
            First = direct ? new NativeProbe(Observation, Native) : new HeapProbe(Observation);
            Second = direct ? new NativeProbe(Observation, Native) : new HeapProbe(Observation);
            for (int i = 0; i < 32; ++i) { First.WriteByte(i + 1); Second.WriteByte(i + 65); }
            First.SetIndex(5, 21); Second.SetIndex(3, 19); Fixed = form == 4;
            Owner = Fixed ? Unpooled.WrappedUnmodifiableBuffer(First, Second)
                : new CompositeByteBuf().AddComponent(First, true).AddComponent(Second, true);
            Offset = form is 1 or 5 ? 4 : 0;
            View = form switch
            {
                1 => Owner.Slice(4, 24), 2 => Owner.Duplicate(), 3 => Owner.AsReadOnly(),
                5 => PooledDerivedByteBuf.NewSlice(Owner, Owner, 4, 24),
                6 => PooledDerivedByteBuf.NewDuplicate(Owner, Owner, 0, 32), _ => Owner
            };
            if (form >= 5) Retained = View;
        }
        public byte[] Expected(int rootIndex, int length, bool logical)
            => Enumerable.Range(rootIndex, length).Select(i => (byte)((i < 16 ? i + (Fixed ? 1 : 6)
                : i - 16 + (Fixed ? 65 : 68)) ^ (logical ? 0x80 : 0))).ToArray();
        public void Dispose()
        {
            if (Retained?.ReferenceCount > 0) Retained.Release();
            if (Owner.ReferenceCount > 0) Owner.Release();
            if (First.ReferenceCount > 0) First.Release(); if (Second.ReferenceCount > 0) Second.Release();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }
    public static IEnumerable<object[]> Iterations()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        foreach (bool descending in new[] { false, true })
        foreach (int rootIndex in new[] { 8, 14, 20 })
        for (int mode = 0; mode < 3; ++mode) yield return [direct, form, descending, rootIndex, mode];
    }
    [Theory]
    [MemberData(nameof(Iterations))]
    public void IterationPreservesStorageDispatchCoordinatesAndEarlyStop(bool direct, int form, bool descending, int rootIndex, int mode)
    {
        using var s = new Storage(direct, form);
        int index = rootIndex - s.Offset;
        s.View.SetIndex(index, index + 5).MarkReaderIndex().MarkWriterIndex();
        var visited = new List<byte>();
        Func<byte, bool> processor = value => { visited.Add(value); return mode != 2 || visited.Count < 3; };
        int result = mode == 0 ? descending ? s.View.ForEachByteDesc(processor) : s.View.ForEachByte(processor)
            : descending ? s.View.ForEachByteDesc(index, 5, processor) : s.View.ForEachByte(index, 5, processor);
        Assert.Equal(mode == 2 ? index + 2 : -1, result);
        bool logical = s.Fixed || mode == 0 && form != 0;
        IEnumerable<byte> expected = s.Expected(rootIndex, 5, logical);
        if (descending) expected = expected.Reverse();
        Assert.Equal(expected.Take(mode == 2 ? 3 : 5), visited);
        Assert.Equal(logical ? visited.Count : 0, s.Observation.Gets); Assert.Equal(0, s.Observation.Iterations);
        Assert.Equal(index, s.View.ReaderIndex); Assert.Equal(index + 5, s.View.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(index, s.View.ReaderIndex); Assert.Equal(index + 5, s.View.WriterIndex);
        Assert.Equal(5, s.First.ReaderIndex); Assert.Equal(21, s.First.WriterIndex);
        Assert.Equal(3, s.Second.ReaderIndex); Assert.Equal(19, s.Second.WriterIndex);
    }
    public static IEnumerable<object[]> Searches()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 7; ++form)
        foreach (bool descending in new[] { false, true })
        foreach (int rootIndex in new[] { 8, 14, 20 }) yield return [direct, form, descending, rootIndex];
    }
    [Theory]
    [MemberData(nameof(Searches))]
    public void ByteAndPatternSearchUseLogicalScalarBytes(bool direct, int form, bool descending, int rootIndex)
    {
        using var s = new Storage(direct, form);
        int index = rootIndex - s.Offset; byte[] logical = s.Expected(rootIndex, 5, true);
        s.View.SetIndex(index, index + 5).MarkReaderIndex().MarkWriterIndex();
        Assert.Equal(index + 2, descending ? s.View.IndexOf(index + 5, index, logical[2]) : s.View.IndexOf(index, index + 5, logical[2]));
        Assert.Equal(-1, descending ? s.View.IndexOf(index + 5, index, 0xFF) : s.View.IndexOf(index, index + 5, 0xFF));
        Assert.Equal(2, s.View.BytesBefore(logical[2])); Assert.Equal(2, s.View.BytesBefore(5, logical[2]));
        Assert.Equal(2, s.View.BytesBefore(index, 5, logical[2]));
        ByteBuf needle = Unpooled.WrappedBuffer(new byte[] { 0, logical[1], logical[2], 0 }).SetIndex(1, 3);
        try { Assert.Equal(index + 1, ByteBufUtil.IndexOf(needle, s.View)); Assert.Equal(1, needle.ReaderIndex); Assert.Equal(3, needle.WriterIndex); }
        finally { needle.Release(); }
        Assert.Equal(index, s.View.ReaderIndex); Assert.Equal(index + 5, s.View.WriterIndex);
        Assert.True(s.Observation.Gets > 0); Assert.Equal(0, s.Observation.Iterations);
    }
}
