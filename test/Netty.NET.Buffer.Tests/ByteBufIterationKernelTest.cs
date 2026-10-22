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

public class ByteBufIterationKernelTest
{
    private sealed class Failure : Exception;
    private sealed class Calls
    {
        public int Indexed, Gets;
        public bool RejectIndexed;
        public readonly Failure Error = new();
        public void BeforeIndexed() { ++Indexed; if (RejectIndexed) throw Error; }
        public byte Get() { ++Gets; throw Error; }
    }

    private sealed class Heap(Calls calls) : UnpooledHeapByteBuf(8, 32)
    {
        public override byte GetByte(int index) => calls.Get();
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        { calls.BeforeIndexed(); return base.ForEachByte(index, length, processor); }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        { calls.BeforeIndexed(); return base.ForEachByteDesc(index, length, processor); }
    }

    private sealed class Direct(Calls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 32, native)
    {
        public override byte GetByte(int index) => calls.Get();
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        { calls.BeforeIndexed(); return base.ForEachByte(index, length, processor); }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        { calls.BeforeIndexed(); return base.ForEachByteDesc(index, length, processor); }
    }

    private static ByteBuf Create(bool direct, Calls calls, NativeMemoryAllocator native)
    {
        ByteBuf result = direct ? new Direct(calls, native) : new Heap(calls);
        result.SetBytes(0, new byte[] { 128, 129, 130, 131, 132, 133, 134, 135 });
        return result.SetIndex(1, 7).MarkReaderIndex().MarkWriterIndex();
    }
    private static int Scan(ByteBuf buffer, bool descending, Func<byte, bool> processor)
        => descending ? buffer.ForEachByteDesc(processor) : buffer.ForEachByte(processor);
    private static int Scan(ByteBuf buffer, bool descending, int index, int length, Func<byte, bool> processor)
        => descending ? buffer.ForEachByteDesc(index, length, processor) : buffer.ForEachByte(index, length, processor);

    public static IEnumerable<object[]> OwnerCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool descending in new[] { false, true })
        foreach (bool indexed in new[] { false, true })
        for (int scenario = 0; scenario < 6; ++scenario) yield return [direct, descending, indexed, scenario];
    }

    // AbstractByteBuf's readable loops do not call the indexed virtual overload.
    // Both forms read the storage kernel rather than the independent public getByte override.
    [Theory]
    [MemberData(nameof(OwnerCases))]
    public void LoopUsesStoredOctetsAndOriginalRangeDespiteOverridesOrCallbacks(bool direct, bool descending, bool indexed, int scenario)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls { RejectIndexed = !indexed };
        ByteBuf owner = Create(direct, calls, native);
        var visited = new List<byte>();
        int start = indexed ? 2 : 1, end = indexed ? 6 : 7;
        int first = descending ? end - 1 : start, next = first + (descending ? -1 : 1);
        var failure = new Failure();
        try
        {
            Func<byte, bool> processor = value =>
            {
                visited.Add(value);
                if (scenario == 2 && visited.Count == 2) throw failure;
                if (visited.Count == 1)
                {
                    if (scenario == 4) owner.Capacity = 16;
                    if (scenario is 3 or 4) owner.SetByte(next, 99);
                    if (scenario == 5) owner.SetIndex(0, 8);
                }
                return scenario switch { 0 => value != 132, 3 or 4 => value != 99, _ => true };
            };
            int Invoke() => indexed ? Scan(owner, descending, start, end - start, processor) : Scan(owner, descending, processor);
            if (scenario == 2) Assert.Same(failure, Assert.Throws<Failure>(() => Invoke()));
            else Assert.Equal(scenario == 0 ? 4 : scenario is 3 or 4 ? next : -1, Invoke());
            var indices = Enumerable.Range(start, end - start);
            if (descending) indices = indices.Reverse();
            byte[] expected = indices.Select(i => (byte)(128 + i)).ToArray();
            if (scenario == 0) expected = expected[..(Array.IndexOf(expected, (byte)132) + 1)];
            if (scenario == 2) expected = expected[..2];
            if (scenario is 3 or 4) expected = [expected[0], 99];
            Assert.Equal(expected, visited); Assert.Equal(indexed ? 1 : 0, calls.Indexed); Assert.Equal(0, calls.Gets);
            Assert.Equal(scenario == 5 ? 0 : 1, owner.ReaderIndex); Assert.Equal(scenario == 5 ? 8 : 7, owner.WriterIndex);
            Assert.Equal(scenario == 4 ? 16 : 8, owner.Capacity); Assert.Equal(1, owner.ReferenceCount);
            byte[] bytes = [128, 129, 130, 131, 132, 133, 134, 135];
            if (scenario is 3 or 4) bytes[next] = 99;
            Assert.Equal(bytes, ByteBufUtil.GetBytes(owner, 0, 8));
            owner.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(1, owner.ReaderIndex); Assert.Equal(7, owner.WriterIndex);
        }
        finally { Assert.True(owner.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    public static IEnumerable<object[]> ViewCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool descending in new[] { false, true })
        for (int kind = 0; kind < 6; ++kind) yield return [direct, descending, kind];
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public void ReadableDerivedLoopKeepsItsCoordinatesAndStorageKernel(bool direct, bool descending, int kind)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls { RejectIndexed = true };
        ByteBuf root = Create(direct, calls, native), retained = null;
        ByteBuf view = kind switch
        {
            0 => root.Duplicate(),
            1 => root.Slice(2, 5),
            2 => root.Slice(2, 5).Slice(1, 3),
            3 => root.AsReadOnly(),
            4 => retained = PooledDerivedByteBuf.NewDuplicate(root, root, 1, 7),
            _ => retained = PooledDerivedByteBuf.NewSlice(root, root, 2, 5)
        };
        int offset = kind == 2 ? 3 : kind is 1 or 5 ? 2 : 0;
        view.SetIndex(offset == 0 ? 1 : kind == 2 ? 0 : 1, offset == 0 ? 7 : kind == 2 ? 3 : 4)
            .MarkReaderIndex().MarkWriterIndex();
        int reader = view.ReaderIndex, writer = view.WriterIndex;
        try
        {
            var visited = new List<byte>();
            Assert.Equal(-1, Scan(view, descending, value => { visited.Add(value); return true; }));
            IEnumerable<byte> expected = Enumerable.Range(offset + reader, writer - reader).Select(i => (byte)(128 + i));
            Assert.Equal(descending ? expected.Reverse() : expected, visited);
            Assert.Equal(0, calls.Indexed); Assert.Equal(0, calls.Gets);
            Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
            view.SetIndex(0, view.Capacity).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
            Assert.Equal(retained == null ? 1 : 2, root.ReferenceCount);
        }
        finally { if (retained != null) Assert.True(retained.Release()); Assert.True(root.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void EmptyInvalidNullAndReleasedRangesPreserveClrValidation(bool direct, bool descending)
    {
        var native = new NativeMemoryAllocator(); var calls = new Calls();
        ByteBuf root = Create(direct, calls, native);
        try
        {
            root.SetIndex(8, 8);
            Assert.Equal(-1, Scan(root, descending, _ => throw calls.Error));
            Assert.Equal(0, calls.Indexed); Assert.Equal(0, calls.Gets);
            Assert.Throws<ArgumentNullException>(() => Scan(root, descending, null));
            Assert.Equal(-1, Scan(root, descending, 8, 0, _ => throw calls.Error));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scan(root, descending, 8, 1, _ => true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scan(root, descending, 0, -1, _ => true));
            Assert.Throws<ArgumentNullException>(() => Scan(root, descending, 0, 0, null));
            Assert.Equal(4, calls.Indexed); Assert.Equal(0, calls.Gets);
            Assert.True(root.Release()); calls.Indexed = 0;
            Assert.Throws<IllegalReferenceCountException>(() => Scan(root, descending, _ => throw calls.Error));
            Assert.Equal(0, calls.Indexed);
            Assert.Throws<IllegalReferenceCountException>(() => Scan(root, descending, 0, 0, _ => throw calls.Error));
            Assert.Equal(1, calls.Indexed); Assert.Equal(0, calls.Gets);
        }
        finally { if (root.ReferenceCount != 0) root.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    public static IEnumerable<object[]> ReleaseCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool descending in new[] { false, true })
        foreach (bool indexed in new[] { false, true })
        foreach (bool stop in new[] { false, true }) yield return [direct, descending, indexed, stop];
    }

    [Theory]
    [MemberData(nameof(ReleaseCases))]
    public void CallbackReleaseStopsCleanlyOrRejectsTheNextStorageRead(bool direct, bool descending, bool indexed, bool stop)
    {
        var native = new NativeMemoryAllocator(); var calls = new Calls { RejectIndexed = !indexed };
        ByteBuf root = Create(direct, calls, native); int visits = 0;
        Func<byte, bool> processor = _ => { ++visits; Assert.True(root.Release()); return !stop; };
        try
        {
            int Invoke() => indexed ? Scan(root, descending, 2, 4, processor) : Scan(root, descending, processor);
            if (stop) Assert.Equal(descending ? indexed ? 5 : 6 : indexed ? 2 : 1, Invoke());
            else Assert.Throws<IllegalReferenceCountException>(() => Invoke());
            Assert.Equal(1, visits); Assert.Equal(indexed ? 1 : 0, calls.Indexed); Assert.Equal(0, calls.Gets);
            Assert.Equal(0, root.ReferenceCount);
        }
        finally { if (root.ReferenceCount != 0) root.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
