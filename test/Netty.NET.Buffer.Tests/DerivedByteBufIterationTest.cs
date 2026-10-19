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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Indexed iteration follows DuplicatedByteBuf/ReadOnlyByteBuf and sliced/pooled derived sources.
// Readable iteration follows AbstractByteBuf's own loop rather than its indexed override.
public class DerivedByteBufIterationTest
{
    private sealed class Observation
    {
        public int Calls, Index, Length;
        public int? Result;
        public Func<byte, bool> Processor;
        public void Visit(int index, int length, Func<byte, bool> processor)
        { ++Calls; Index = index; Length = length; Processor = processor; }
    }

    private sealed class ObservedHeap(Observation o) : UnpooledHeapByteBuf(16, 64)
    {
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            o.Visit(index, length, processor);
            return o.Result ?? base.ForEachByte(index, length, processor);
        }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            o.Visit(index, length, processor);
            return o.Result ?? base.ForEachByteDesc(index, length, processor);
        }
    }

    private sealed class ObservedDirect(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(16, 64, native)
    {
        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            o.Visit(index, length, processor);
            return o.Result ?? base.ForEachByte(index, length, processor);
        }
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            o.Visit(index, length, processor);
            return o.Result ?? base.ForEachByteDesc(index, length, processor);
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
            for (int i = 0; i < 16; ++i) Root.WriteByte(i);
            Root.SetIndex(2, 14).MarkReaderIndex().MarkWriterIndex();
            if (kind is >= 5 and <= 9)
                Owner = kind is 6 or 8 ? PooledDerivedByteBuf.NewDuplicate(Root, Root, 2, 14)
                    : PooledDerivedByteBuf.NewSlice(Root, Root, 3, 10);
            View = kind switch
            {
                0 => Root.Duplicate(),
                1 => Root.Slice(3, 10),
                2 => Root.AsReadOnly(),
                3 => Root.Slice(3, 10).Slice(2, 6),
                4 => Root.Slice(3, 10).AsReadOnly(),
                5 or 6 => Owner,
                7 => Owner.Slice(2, 6),
                8 or 9 => Owner.Duplicate(),
                _ => Root.Slice(0, 16)
            };
            Offset = kind is 3 or 7 ? 5 : kind is 1 or 4 or 5 or 9 ? 3 : 0;
            View.SetIndex(Offset == 0 ? 3 : 1, Offset == 0 ? 13 : View.Capacity - 1)
                .MarkReaderIndex().MarkWriterIndex();
        }
        public void Dispose()
        {
            if (Owner?.ReferenceCount > 0) Assert.True(Owner.Release());
            if (Root.ReferenceCount > 0) Assert.True(Root.Release());
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int kind = 0; kind < 11; ++kind)
        foreach (bool descending in new[] { false, true }) yield return [direct, kind, descending];
    }
    private static int Scan(ByteBuf b, bool descending, Func<byte, bool> p)
        => descending ? b.ForEachByteDesc(p) : b.ForEachByte(p);
    private static int Scan(ByteBuf b, bool descending, int index, int length, Func<byte, bool> p)
        => descending ? b.ForEachByteDesc(index, length, p) : b.ForEachByte(index, length, p);

    [Theory]
    [MemberData(nameof(Cases))]
    public void IndexedIterationTranslatesCoordinatesWhileReadableIterationKeepsItsOwnLoop(bool direct, int kind, bool descending)
    {
        using var s = new Storage(direct, kind);
        int reader = s.View.ReaderIndex, writer = s.View.WriterIndex;
        var visited = new List<byte>();
        Func<byte, bool> stop = value => { visited.Add(value); return value != s.Offset + 3; };
        Assert.Equal(3, Scan(s.View, descending, 2, 3, stop));
        Assert.Equal(descending ? new byte[] { (byte)(s.Offset + 4), (byte)(s.Offset + 3) }
            : new byte[] { (byte)(s.Offset + 2), (byte)(s.Offset + 3) }, visited);
        Assert.Equal(1, s.Observation.Calls);
        Assert.Equal(s.Offset + 2, s.Observation.Index); Assert.Equal(3, s.Observation.Length);
        Assert.Same(stop, s.Observation.Processor);

        s.Observation.Calls = 0; visited.Clear();
        Assert.Equal(-1, Scan(s.View, descending, 2, 3, value => { visited.Add(value); return true; }));
        Assert.Equal(1, s.Observation.Calls);
        Assert.Equal(descending ? new byte[] { (byte)(s.Offset + 4), (byte)(s.Offset + 3), (byte)(s.Offset + 2) }
            : new byte[] { (byte)(s.Offset + 2), (byte)(s.Offset + 3), (byte)(s.Offset + 4) }, visited);

        s.Observation.Calls = 0; visited.Clear();
        Assert.Equal(-1, Scan(s.View, descending, value => { visited.Add(value); return true; }));
        Assert.Equal(0, s.Observation.Calls);
        IEnumerable<byte> range = Enumerable.Range(s.Offset + reader, writer - reader).Select(x => (byte)x);
        Assert.Equal(descending ? range.Reverse() : range, visited);

        var failure = new InvalidOperationException("processor failure");
        int calls = 0; s.Observation.Calls = 0;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Scan(s.View, descending, 2, 3, _ =>
        { if (++calls == 2) throw failure; return true; })));
        Assert.Equal(2, calls); Assert.Equal(1, s.Observation.Calls);
        Assert.Equal(-1, Scan(s.View, descending, s.View.Capacity, 0, _ => throw failure));
        Assert.Throws<ArgumentOutOfRangeException>(() => Scan(s.View, descending, s.View.Capacity, 1, _ => throw failure));

        if (direct)
        {
            visited.Clear(); s.Observation.Calls = 0;
            Assert.Equal(3, Scan(s.View, descending, 2, 3, value =>
            {
                visited.Add(value);
                if (visited.Count == 1)
                {
                    s.Root.Capacity = 32;
                    s.Root.SetByte(s.Offset + 3, 99);
                }
                return value != 99;
            }));
            Assert.Equal(descending ? new byte[] { (byte)(s.Offset + 4), 99 }
                : new byte[] { (byte)(s.Offset + 2), 99 }, visited);
            Assert.Equal(1, s.Observation.Calls); Assert.Equal(32, s.Native.ReservedBytes);
        }
        Assert.Equal(reader, s.View.ReaderIndex); Assert.Equal(writer, s.View.WriterIndex);
        Assert.Equal(2, s.Root.ReaderIndex); Assert.Equal(14, s.Root.WriterIndex);
        s.View.SetIndex(0, s.View.Capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(reader, s.View.ReaderIndex); Assert.Equal(writer, s.View.WriterIndex);
        if (s.Owner != null)
        {
            Assert.Equal(2, s.Root.ReferenceCount); Assert.Equal(1, s.Owner.ReferenceCount);
            Assert.True(s.Owner.Release()); Assert.Equal(1, s.Root.ReferenceCount);
            // A live root cannot make an independently released pooled view accessible again.
            Assert.Throws<IllegalReferenceCountException>(() => Scan(s.View, descending, 2, 3, _ => true));
            Assert.Throws<IllegalReferenceCountException>(() => Scan(s.View, descending, _ => true));
            Assert.Equal(0, s.Root.GetByte(0));
        }
        else Assert.Equal(1, s.Root.ReferenceCount);
    }

    public static IEnumerable<object[]> Results()
    {
        for (int kind = 0; kind < 11; ++kind)
        foreach (bool descending in new[] { false, true })
        foreach (int result in new[] { -2, -1, 1 }) yield return [kind, descending, result];
    }

    [Theory]
    [MemberData(nameof(Results))]
    public void SliceResultsAreRelativeAndNegativeParentResultsStayNotFound(int kind, bool descending, int result)
    {
        using var s = new Storage(false, kind);
        s.Observation.Result = result;
        // A duplicate/read-only wrapper forwards directly; a slice rejects results below its adjustment.
        bool sliced = kind is 1 or 3 or 4 or 5 or 7 or 9 or 10;
        int expected = sliced ? result < s.Offset ? -1 : result - s.Offset : result;
        Assert.Equal(expected, Scan(s.View, descending, 2, 3, _ => throw new InvalidOperationException()));
        Assert.Equal(1, s.Observation.Calls);
    }
}
