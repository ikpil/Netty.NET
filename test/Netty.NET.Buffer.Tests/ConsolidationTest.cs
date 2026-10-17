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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests buffer consolidation
 */
public class ConsolidationTest
{
    private sealed class Graph : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        internal readonly List<ByteBuf> Leaves = new();
        internal readonly List<ByteBuf> Nodes = new();
        internal readonly string Expected;
        internal CompositeByteBuf Current;
        internal Graph(int kind, int pairs, int? limit = null, bool padded = false)
        {
            ByteBuf Segment(string text)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                if (padded) bytes = new byte[] { 170 }.Concat(bytes).Append((byte)187).ToArray();
                bool direct = kind == 1 || kind == 2 && Leaves.Count % 2 == 1;
                ByteBuf b = direct ? new UnpooledDirectByteBuf(bytes.Length, bytes.Length, Native).WriteBytes(bytes) : Unpooled.WrappedBuffer(bytes);
                if (padded) b.SetIndex(1, bytes.Length - 1);
                Leaves.Add(b); return b;
            }
            void Wrap(params ByteBuf[] components)
            {
                Current = (CompositeByteBuf)(limit.HasValue ? Unpooled.WrappedBuffer(limit.Value, components) : Unpooled.WrappedBuffer(components));
                Nodes.Add(Current);
            }
            for (int pair = 0; pair < pairs; pair++)
            {
                string key = ((char)('a' + pair)).ToString(), value = (pair + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (Current == null) Wrap(Segment(key), Segment("=")); else Wrap(Current, Segment(key), Segment("="));
                Wrap(Current, Segment(value), Segment("&"));
            }
            Expected = string.Concat(Enumerable.Range(0, pairs).Select(pair => $"{(char)('a' + pair)}={pair + 1}&"));
        }
        public void Dispose()
        {
            if (Current.ReferenceCount > 0) Current.Release();
            Assert.All(Leaves, leaf => Assert.Equal(0, leaf.ReferenceCount));
            Assert.All(Nodes, node => Assert.Equal(0, node.ReferenceCount)); Assert.Equal(0, Native.ReservedBytes);
        }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ShouldWrapInSequence(int kind)
    {
        using var graph = new Graph(kind, 1); ByteBuf copy = graph.Current.Copy();
        try { Assert.Equal("a=1&", copy.GetString(Encoding.ASCII)); }
        finally { copy.Release(); }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ShouldConsolidationInSequence(int kind)
    {
        using var graph = new Graph(kind, 5); ByteBuf copy = graph.Current.Copy();
        try { Assert.Equal("a=1&b=2&c=3&d=4&e=5&", copy.GetString(Encoding.ASCII)); }
        finally { copy.Release(); }
    }

    public static IEnumerable<object[]> Limits()
    {
        for (int kind = 0; kind < 3; kind++) foreach (int limit in new[] { 1, 2, 3, 16 }) yield return [kind, limit];
    }

    [Theory] [MemberData(nameof(Limits))]
    public void ComponentLimitDeterminesWhenNestedOwnersAreCopiedAndReleased(int kind, int limit)
    {
        using var graph = new Graph(kind, 5, limit); CompositeByteBuf current = graph.Current;
        Assert.Equal(graph.Expected, current.GetString(Encoding.ASCII)); Assert.Equal(limit <= 2 ? 1 : 3, current.NumComponents);
        Assert.Equal(limit, current.MaxNumComponents); Assert.Equal(0, current.ReaderIndex); Assert.Equal(20, current.WriterIndex);
        Assert.All(graph.Leaves, leaf => Assert.Equal(limit <= 2 ? 0 : 1, leaf.ReferenceCount));
        Assert.All(graph.Nodes.Take(graph.Nodes.Count - 1), node => Assert.Equal(limit <= 2 ? 0 : 1, node.ReferenceCount));
        ByteBuf copy = current.Copy();
        try
        {
            Assert.False(copy.IsDirect); current.SetByte(0, 'z');
            Assert.Equal(graph.Expected, copy.GetString(Encoding.ASCII)); Assert.Equal((byte)'z', current.GetByte(0));
        }
        finally { copy.Release(); }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ConsolidationUsesCapturedRangesAndKeepsCursorsMarksAndIndependentCopies(int kind)
    {
        using var graph = new Graph(kind, 5, padded: true); CompositeByteBuf current = graph.Current;
        current.SetIndex(2, 18).MarkReaderIndex().MarkWriterIndex(); ByteBuf copy = current.Copy(0, 20);
        try
        {
            foreach (ByteBuf leaf in graph.Leaves) leaf.SetIndex(0, 1);
            Assert.Equal(graph.Expected, current.GetString(0, 20, Encoding.ASCII));
            graph.Leaves[0].SetByte(1, 'z'); Assert.Equal((byte)'z', current.GetByte(0));
            Assert.Same(current, current.Consolidate()); Assert.Equal(1, current.NumComponents); Assert.False(current.IsDirect);
            Assert.Equal("z=1&b=2&c=3&d=4&e=5&", current.GetString(0, 20, Encoding.ASCII));
            Assert.Equal(graph.Expected, copy.GetString(Encoding.ASCII)); Assert.All(graph.Leaves, leaf => Assert.Equal(0, leaf.ReferenceCount));
            current.SetIndex(3, 17).ResetReaderIndex().ResetWriterIndex(); Assert.Equal(2, current.ReaderIndex); Assert.Equal(18, current.WriterIndex);
            Assert.Equal(1, current.ReferenceCount); Assert.Equal(1, copy.ReferenceCount); Assert.Equal(0, graph.Native.ReservedBytes);
        }
        finally { copy.Release(); }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public unsafe void OutstandingPinKeepsOnlyPhysicalStorageAliveAfterConsolidation(int kind)
    {
        using var graph = new Graph(kind, 5); CompositeByteBuf current = graph.Current;
        ByteBuf leaf = graph.Leaves.FirstOrDefault(value => value.IsDirect) ?? graph.Leaves[0];
        bool direct = leaf.IsDirect; byte original = leaf.GetByte(0);
        using (var pin = leaf.AsReadOnlyMemory(0, 1).Pin())
        {
            current.Consolidate(); Assert.All(graph.Leaves, value => Assert.Equal(0, value.ReferenceCount));
            Assert.Equal(graph.Expected, current.GetString(Encoding.ASCII)); Assert.Equal(original, *(byte*)pin.Pointer);
            current.Release(); Assert.Equal(original, *(byte*)pin.Pointer);
            if (direct) Assert.True(graph.Native.ReservedBytes > 0); else Assert.Equal(0, graph.Native.ReservedBytes);
        }
        Assert.Equal(0, graph.Native.ReservedBytes);
    }
}
