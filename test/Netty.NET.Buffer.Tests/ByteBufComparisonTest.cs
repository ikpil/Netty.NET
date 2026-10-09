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

using System;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufComparisonTest
{
    private static ByteBuf Owner(int kind, byte[] data)
    {
        int split = Math.Min(7, data.Length);
        return kind switch
        {
            0 => Unpooled.WrappedBuffer(data),
            1 => Unpooled.DirectBuffer(data.Length, data.Length).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..split]), true)
                .AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.WrappedBuffer(data[split..]), true),
            3 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(data[..split]), Unpooled.WrappedBuffer(data[split..])),
            4 => Unpooled.WrappedBuffer(new byte[] { 9 }.Concat(data).Append((byte)9).ToArray()).Slice(1, data.Length),
            5 => Unpooled.WrappedBuffer(data).AsReadOnly(),
            6 => Unpooled.WrappedBuffer(data).Duplicate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void OriginalSubsectionsEqualityDifferenceOverflowAndUnderflow(int kind)
    {
        byte[] first = new byte[128], second = new byte[256];
        new Random(123).NextBytes(first); new Random(456).NextBytes(second);
        Array.Copy(first, 64, second, 192, 64);
        ByteBuf a = Owner(kind, first), b = Owner(kind, second);
        try
        {
            Assert.True(ByteBufUtil.Equals(a, 64, b, 192, 64));
            Assert.False(ByteBufUtil.Equals(a, 64, b, 192, 512));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Equals(a, 0, b, 0, -1));
        }
        finally { a.Release(); b.Release(); }

        first = new byte[50]; second = new byte[256];
        new Random(789).NextBytes(first); new Random(1011).NextBytes(second);
        Array.Copy(first, 25, second, 75, 25);
        // Randomly pick an index in the range that will be compared and make the value at that index differ between
        // the 2 arrays.
        first[25 + new Random(1213).Next(25)] ^= 1;
        a = Owner(kind, first); b = Owner(kind, second);
        try { Assert.False(ByteBufUtil.Equals(a, 25, b, 75, 25)); }
        finally { a.Release(); b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ReadableContentIgnoresAbsoluteIndicesAndBackingAndKeepsMarks(int kind)
    {
        byte[] content = Enumerable.Range(0, 32).Select(i => unchecked((byte)(i * 17))).ToArray();
        ByteBuf a = Owner(kind, new byte[] { 99 }.Concat(content).Append((byte)88).ToArray());
        try
        {
            a.SetIndex(1, 33).MarkReaderIndex().MarkWriterIndex();
            for (int otherKind = 0; otherKind < 7; otherKind++)
            {
                ByteBuf b = Owner(otherKind, new byte[] { 55, 66, 77 }.Concat(content).ToArray());
                try
                {
                    b.SetIndex(3, 35).MarkReaderIndex().MarkWriterIndex();
                    Assert.True(ByteBufUtil.Equals(a, b)); Assert.True(a.Equals(b)); Assert.True(b.Equals((object)a));
                    Assert.True(EqualityComparer<ByteBuf>.Default.Equals(a, b));
                    Assert.Equal(a.GetHashCode(), b.GetHashCode()); Assert.Equal(0, a.CompareTo(b));
                    Assert.Equal(0, ByteBufUtil.Compare(b, a));
                    Assert.False(a == b); // C# reference identity remains available separately from content equality.
                    b.SetIndex(4, 34).ResetReaderIndex().ResetWriterIndex();
                    Assert.Equal(3, b.ReaderIndex); Assert.Equal(35, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
                }
                finally { b.Release(); }
            }
            a.SetIndex(2, 34).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, a.ReaderIndex); Assert.Equal(33, a.WriterIndex); Assert.Equal(1, a.ReferenceCount);
            Assert.False(a.Equals((object)null)); Assert.False(a.Equals(new object())); Assert.False(a.Equals((ByteBuf)null));
        }
        finally { a.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void OriginalContentComparisonPrefixesAndUnsignedOrdering(int kind)
    {
        // Fill the random stuff
        byte[] value = new byte[32]; new Random(12345).NextBytes(value);
        // Prevent overflow / underflow
        if (value[0] == 0) value[0]++;
        else if (value[0] == 255) value[0]--;
        ByteBuf a = Owner(kind, value.ToArray());
        try
        {
            value[0]++; ByteBuf higher = Owner(kind, value.ToArray());
            value[0] -= 2; ByteBuf lower = Owner(kind, value.ToArray());
            value[0]++; ByteBuf prefix = Owner(kind, value[..31]);
            try
            {
                Assert.True(a.CompareTo(higher) < 0); Assert.True(a.CompareTo(lower) > 0);
                Assert.False(a.Equals(higher)); Assert.True(a.CompareTo(prefix) > 0);
                Assert.True(a.Slice(0, 31).CompareTo(a) < 0);
                ByteBuf retained = a.RetainedSlice(0, 31);
                try { Assert.True(retained.CompareTo(a) < 0); }
                finally { retained.Release(); }
            }
            finally { higher.Release(); lower.Release(); prefix.Release(); }
        }
        finally { a.Release(); }

        a = Owner(kind, new byte[] { 1, 2, 3, 4 }); ByteBuf b = Owner(kind, new byte[] { 4, 3, 2, 1 });
        try { Assert.Equal(-50396925, a.CompareTo(b)); Assert.Equal(50396925, b.CompareTo(a)); }
        finally { a.Release(); b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void HashPreservesSignedTailsWrappingAndZeroCoercion(int kind)
    {
        (byte[] Bytes, int Hash)[] vectors =
        {
            (Array.Empty<byte>(), 1), (new byte[] { 128 }, -97), (new byte[] { 255 }, 30),
            (new byte[] { 225 }, 1), (new byte[] { 255, 255, 255, 225 }, 1),
            (new byte[] { 255, 255, 255, 255 }, 30), (new byte[8], 961),
            (new byte[] { 127, 255, 255, 255, 127, 255, 255, 255 }, 929),
            (new byte[] { 128, 0, 0, 0, 128, 0, 0, 0, 255 }, 29790)
        };
        foreach (var vector in vectors)
        {
            ByteBuf b = Owner(kind, new byte[] { 3 }.Concat(vector.Bytes).Append((byte)4).ToArray());
            try
            {
                b.SetIndex(1, 1 + vector.Bytes.Length);
                Assert.Equal(vector.Hash, ByteBufUtil.HashCode(b)); Assert.Equal(vector.Hash, b.GetHashCode());
            }
            finally { b.Release(); }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ExactUnsignedWordClampingByteAndPrefixDifferences(int kind)
    {
        (byte[] A, byte[] B, int Difference)[] vectors =
        {
            (new byte[] { 255, 255, 255, 255 }, new byte[4], int.MaxValue),
            (new byte[4], new byte[] { 255, 255, 255, 255 }, int.MinValue),
            (new byte[] { 128, 0, 0, 0 }, new byte[4], int.MaxValue),
            (new byte[4], new byte[] { 128, 0, 0, 0 }, int.MinValue),
            (new byte[] { 0, 0, 0, 1 }, new byte[4], 1),
            (new byte[] { 255 }, new byte[] { 0 }, 255),
            (new byte[] { 0 }, new byte[] { 255 }, -255),
            (new byte[] { 0, 0, 0, 0, 128 }, new byte[5], 128),
            (new byte[] { 127, 255, 255, 255 }, new byte[] { 128, 0, 0, 0 }, -1),
            (new byte[9], new byte[5], 4), (new byte[3], new byte[9], -6)
        };
        foreach (var vector in vectors)
        {
            ByteBuf a = Owner(kind, vector.A), b = Owner(kind, vector.B);
            try { Assert.Equal(vector.Difference, ByteBufUtil.Compare(a, b)); Assert.Equal(vector.Difference, a.CompareTo(b)); }
            finally { a.Release(); b.Release(); }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void AbsoluteEqualityUsesWriterBoundBeforeReaderAndAvoidsOverflow(int kind)
    {
        ByteBuf a = Owner(kind, new byte[32]), b = Owner(kind, new byte[32]);
        try
        {
            a.SetIndex(8, 16); b.SetIndex(9, 17);
            Assert.True(ByteBufUtil.Equals(a, 0, b, 1, 16));
            Assert.True(ByteBufUtil.Equals(a, 16, b, 17, 0));
            Assert.False(ByteBufUtil.Equals(a, 17, b, 17, 0));
            Assert.False(ByteBufUtil.Equals(a, 0, b, 0, 17));
            Assert.False(ByteBufUtil.Equals(a, 0, b, 0, int.MaxValue));
            Assert.False(ByteBufUtil.Equals(a, int.MaxValue, b, int.MaxValue, 0));
            Assert.False(ByteBufUtil.Equals(a, int.MaxValue, b, 0, int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Equals(a, -1, b, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Equals(a, 0, b, -1, 0));
        }
        finally { a.Release(); b.Release(); }
    }

    [Fact]
    public void OriginalHashSetScenarioAndClrDefaultSortUseContent()
    {
        ByteBuf a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5 });
        ByteBuf b = Unpooled.DirectBuffer(15).WriteBytes(new byte[] { 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 });
        ByteBuf aCopy = a.Copy(), bCopy = b.Copy(), probe = Unpooled.Buffer(15);
        try
        {
            var set = new HashSet<ByteBuf> { a, b };
            Assert.Equal(2, set.Count); Assert.Contains(aCopy, set); Assert.Contains(bCopy, set);
            probe.WriteBytes(a.Duplicate(), a.ReadableBytes); Assert.True(set.Remove(probe)); Assert.DoesNotContain(a, set); Assert.Single(set);
            probe.Clear().WriteBytes(b.Duplicate(), b.ReadableBytes); Assert.True(set.Remove(probe)); Assert.DoesNotContain(b, set); Assert.Empty(set);
            var list = new List<ByteBuf> { b, a, null }; list.Sort();
            Assert.Null(list[0]); Assert.Same(a, list[1]); Assert.Same(b, list[2]); Assert.Equal(1, a.CompareTo(null));
            Assert.Equal(1, Unpooled.EmptyBuffer.GetHashCode());
        }
        finally { a.Release(); b.Release(); aCopy.Release(); bCopy.Release(); probe.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void NullAndReleasedContractsAreExplicitIncludingEmpty(int length)
    {
        ByteBuf a = Unpooled.Buffer(1).WriteBytes(new byte[length]), b = Unpooled.Buffer(1).WriteBytes(new byte[length]);
        a.Release(); b.Release();
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.HashCode(null));
        Assert.True(ByteBufUtil.Equals(null, null)); Assert.Equal(0, ByteBufUtil.Compare(null, null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Equals(a, null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Compare(null, a));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Equals(null, 0, b, 0, 0));
        Assert.True(a.Equals(a)); Assert.Equal(0, a.CompareTo(a));
        Assert.Throws<IllegalReferenceCountException>(() => a.GetHashCode());
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Equals(a, b));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Compare(a, b));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Equals(a, 0, a, 0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Equals(a, Unpooled.EmptyBuffer));
    }
}
