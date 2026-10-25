/*
 * Copyright 2016 The Netty Project
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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class DerivedByteBufTextDispatchTest
{
    private sealed class TextFailure : Exception { }
    private sealed class Observation
    {
        internal readonly TextFailure Failure = new();
        internal bool Fail;
        internal readonly List<(int Index, int Length, Encoding Encoding)> Calls = [];
        internal string Read(int index, int length, Encoding encoding)
        {
            Calls.Add((index, length, encoding));
            if (Fail) throw Failure;
            return $"parent:{index}:{length}";
        }
    }
    private sealed class Heap(Observation o) : UnpooledHeapByteBuf(64, 128)
    {
        public override string GetString(int index, int length, Encoding encoding) => o.Read(index, length, encoding);
    }
    private sealed class Direct(Observation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(64, 128, native)
    {
        public override string GetString(int index, int length, Encoding encoding) => o.Read(index, length, encoding);
    }
    private sealed class Context : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        internal readonly Observation Observation = new();
        internal readonly ByteBuf Root, View;
        private readonly List<ByteBuf> _owners = [];
        internal readonly int Offset;
        internal readonly bool Forward;
        internal Context(bool direct, int form)
        {
            Root = direct ? new Direct(Observation, Native) : new Heap(Observation);
            for (int i = 0; i < 64; ++i) Root.WriteByte('A' + i % 26);
            Root.SetIndex(3, 40);
            ByteBuf Slice() => Own(PooledDerivedByteBuf.NewSlice(Root, Root, 2, 30));
            ByteBuf Duplicate() => Own(PooledDerivedByteBuf.NewDuplicate(Root, Root, 3, 40));
            View = form switch
            {
                0 => Root.Slice(4, 16), 1 => Root.Duplicate(), 2 => Root.AsReadOnly(),
                3 => Root.Slice(4, 16).AsReadOnly(),
                4 => Own(PooledDerivedByteBuf.NewSlice(Root, Root, 4, 16)), 5 => Duplicate(),
                6 => Slice().Slice(2, 16), 7 => Duplicate().Duplicate(),
                8 => Duplicate().Slice(4, 16), 9 => Slice().Slice(2, 16).Duplicate(),
                10 => Root.AsReadOnly().Slice(4, 16), 11 => Root.Slice(2, 30).Slice(2, 16),
                12 => Root.Slice(4, 16).Duplicate(), 13 => Root.AsReadOnly().Duplicate(),
                14 => Own(PooledDerivedByteBuf.NewSlice(Root, Root, 4, 16)).Duplicate(),
                _ => Own(Own(PooledDerivedByteBuf.NewSlice(Root, Root, 4, 16)).RetainedDuplicate())
            };
            Offset = form is 1 or 2 or 5 or 7 or 9 or 13 ? 0 : 4;
            Forward = form is 0 or 6 or 8 or 11 or 12 or 14;
            View.SetIndex(1, 9).MarkReaderIndex().MarkWriterIndex();
        }
        private ByteBuf Own(ByteBuf b) { _owners.Add(b); return b; }
        public void Dispose()
        {
            for (int i = _owners.Count - 1; i >= 0; --i) if (_owners[i].ReferenceCount > 0) _owners[i].Release();
            if (Root.ReferenceCount > 0) Root.Release();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 16; ++form)
        for (int operation = 0; operation < 3; ++operation)
        for (int encoding = 0; encoding < 3; ++encoding)
        foreach (bool fail in new[] { false, true })
        foreach (bool empty in new[] { false, true }) yield return [direct, form, operation, encoding, fail, empty];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void TextReadsKeepSliceParentDispatchSeparateFromOwnedAndReadOnlyLocalDecoding(bool direct, int form, int operation, int encoding, bool fail, bool empty)
    {
        using var c = new Context(direct, form);
        Encoding codec = encoding == 0 ? Encoding.UTF8 : encoding == 1 ? Encoding.ASCII : Encoding.Latin1;
        c.Observation.Fail = fail;
        if (empty && operation != 0) c.View.SetIndex(9, 9).MarkReaderIndex().MarkWriterIndex();
        int reader = c.View.ReaderIndex, writer = c.View.WriterIndex, claims = c.Root.ReferenceCount;
        int index = operation == 0 ? empty ? c.View.Capacity : 2 : reader;
        int length = empty ? 0 : operation == 1 ? writer - reader : 5;
        string Request() => operation == 0 ? c.View.GetString(index, length, codec)
            : operation == 1 ? c.View.GetString(codec) : c.View.ReadString(length, codec);
        if (c.Forward && fail) Assert.Same(c.Observation.Failure, Assert.Throws<TextFailure>(() => Request()));
        else
        {
            string actual = Request();
            string expected = c.Forward ? $"parent:{c.Offset + index}:{length}" :
                new string(Array.ConvertAll(ByteBufUtil.GetBytes(c.Root, c.Offset + index, length), b => (char)b));
            Assert.Equal(expected, actual);
        }
        if (c.Forward)
        {
            var call = Assert.Single(c.Observation.Calls);
            Assert.Equal((c.Offset + index, length), (call.Index, call.Length)); Assert.Same(codec, call.Encoding);
        }
        else Assert.Empty(c.Observation.Calls);
        Assert.Equal(reader + (operation == 2 && !(c.Forward && fail) ? length : 0), c.View.ReaderIndex);
        Assert.Equal(writer, c.View.WriterIndex); Assert.Equal((3, 40, claims), (c.Root.ReaderIndex, c.Root.WriterIndex, c.Root.ReferenceCount));
        c.View.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((reader, writer), (c.View.ReaderIndex, c.View.WriterIndex));
    }
    public static IEnumerable<object[]> BoundsCases()
    {
        foreach (bool direct in new[] { false, true })
        for (int form = 0; form < 16; ++form)
        for (int range = 0; range < 4; ++range) yield return [direct, form, range];
    }
    [Theory]
    [MemberData(nameof(BoundsCases))]
    public void TextRangeGuardsPrecedeCustomParentDispatch(bool direct, int form, int range)
    {
        using var c = new Context(direct, form);
        int index = range == 0 ? -1 : range == 1 ? c.View.Capacity : range == 2 ? 0 : int.MaxValue;
        int length = range == 2 ? -1 : 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => c.View.GetString(index, length, Encoding.UTF8));
        Assert.Empty(c.Observation.Calls); Assert.Equal((1, 9), (c.View.ReaderIndex, c.View.WriterIndex));
    }
    [Theory]
    [MemberData(nameof(Forms))]
    public void NullEncodingDoesNotReachTheCustomParent(bool direct, int form)
    {
        using var c = new Context(direct, form);
        Assert.Throws<ArgumentNullException>(() => c.View.GetString(2, 5, null));
        Assert.Empty(c.Observation.Calls); Assert.Equal((1, 9), (c.View.ReaderIndex, c.View.WriterIndex));
    }
    public static IEnumerable<object[]> Forms()
    { foreach (bool direct in new[] { false, true }) for (int form = 0; form < 16; ++form) yield return [direct, form]; }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void DeadBorrowedViewsCannotReadThroughTheirStillLiveRoot(bool direct, bool sliced)
    {
        var native = new NativeMemoryAllocator(); var observation = new Observation();
        ByteBuf root = direct ? new Direct(observation, native) : new Heap(observation);
        root.WriteZero(64); ByteBuf owner = PooledDerivedByteBuf.NewDuplicate(root, root, 0, 64);
        ByteBuf view = sliced ? owner.Slice(4, 16) : owner.Duplicate();
        try
        {
            Assert.True(owner.Release()); Assert.Equal(1, root.ReferenceCount); Assert.Equal(0, view.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => view.GetString(0, 5, Encoding.UTF8));
            Assert.Empty(observation.Calls);
        }
        finally { if (owner.ReferenceCount > 0) owner.Release(); root.Release(); Assert.Equal(0, native.ReservedBytes); }
    }
}
