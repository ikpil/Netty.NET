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

public class CompositeByteBufTrimFailureTest
{
    private sealed class SliceFailure : Exception { }
    private sealed class Observation
    {
        internal readonly SliceFailure Failure = new();
        internal int Mode, Calls, Index, Length;
    }
    private sealed class SliceProbe(ByteBuf buffer, Observation o) : WrappedByteBuf(buffer)
    {
        public override ByteBuf Slice(int index, int length)
        {
            ++o.Calls; o.Index = index; o.Length = length;
            if (o.Mode == 1) throw o.Failure;
            ByteBuf result = base.Slice(index, length);
            // A borrowed slice has no new owner claim to release on failure.
            if (o.Mode == 2) throw o.Failure;
            return result;
        }
    }
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes;
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf b) { Assert.Same(expected, b); ++Closes; return true; }
    }
    private sealed class ReleaseProbe(ByteBuf b, List<int> releases, int id) : WrappedByteBuf(b)
    {
        public override bool Release(int decrement = 1)
        { releases.Add(id); return base.Release(decrement); }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        for (int facade = 0; facade < 4; ++facade)
        for (int tails = 0; tails < 3; ++tails)
        for (int mode = 0; mode < 3; ++mode) yield return [pooled, direct, facade, tails, mode];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void CachedSlicePreparationFailurePreservesTailOwnershipLayoutIndicesAndRetry(bool pooled, bool direct, int facade, int tails, int mode)
    {
        var native = new NativeMemoryAllocator();
        IByteBufAllocator allocator = pooled ? new PooledByteBufAllocator(false, maxOrder: 4, nativeAllocator: native)
            : new UnpooledByteBufAllocator(false, native);
        ByteBuf raw = direct ? allocator.DirectBuffer(16, 16) : allocator.HeapBuffer(16, 16);
        for (int i = 0; i < 16; ++i) raw.WriteByte(i + 1);
        ByteBuf child = raw.RetainedSlice(2, 12); Assert.False(raw.Release());
        var observation = new Observation(); var first = new SliceProbe(child, observation);
        var root = new CompositeByteBuf(allocator, direct, 16); root.AddComponent(first, true);
        var trailing = new List<ByteBuf>(); var releases = new List<int>();
        for (int i = 0; i < tails; ++i)
        {
            ByteBuf tail = direct ? allocator.DirectBuffer(6, 6) : allocator.HeapBuffer(6, 6);
            tail.WriteBytes(new byte[] { 51, 52, 53, 54, 55, 56 });
            tail = new ReleaseProbe(tail, releases, i); trailing.Add(tail); root.AddComponent(tail, true);
        }
        var tracker = new Tracker(root);
        CompositeByteBuf owner = facade switch
        {
            0 => root, 1 => new WrappedCompositeByteBuf(root),
            2 => new SimpleLeakAwareCompositeByteBuf(root, tracker),
            _ => new AdvancedLeakAwareCompositeByteBuf(root, tracker)
        };
        try
        {
            Assert.Same(first, root.ComponentSlice(0));
            int capacity = root.Capacity; byte[] before = ByteBufUtil.GetBytes(root, 0, capacity);
            owner.SetIndex(1, 6).MarkReaderIndex().MarkWriterIndex().SetIndex(2, capacity - 1);
            observation.Mode = mode;
            if (mode == 0) owner.Capacity = 7;
            else
            {
                Assert.Same(observation.Failure, Assert.Throws<SliceFailure>(() => owner.Capacity = 7));
                Assert.Equal(capacity, owner.Capacity); Assert.Equal(tails + 1, root.NumComponents);
                Assert.Equal((2, capacity - 1), (owner.ReaderIndex, owner.WriterIndex));
                Assert.Equal(before, ByteBufUtil.GetBytes(root, 0, capacity)); Assert.Same(first, root.ComponentSlice(0));
                Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, raw.ReferenceCount);
                Assert.All(trailing, b => Assert.Equal(1, b.ReferenceCount));
                Assert.Empty(releases);
                owner.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((1, 6), (owner.ReaderIndex, owner.WriterIndex));
                owner.SetIndex(2, capacity - 1);
            }
            Assert.Equal((1, 0, 7), (observation.Calls, observation.Index, observation.Length));
            if (mode != 0) { observation.Mode = 0; owner.Capacity = 7; Assert.Equal(2, observation.Calls); }
            Assert.Equal((7, 1, 2, 7), (owner.Capacity, root.NumComponents, owner.ReaderIndex, owner.WriterIndex));
            Assert.Equal(before.AsSpan(0, 7).ToArray(), ByteBufUtil.GetBytes(owner, 0, 7));
            Assert.Equal(7, root.ComponentSlice(0).Capacity); Assert.All(trailing, b => Assert.Equal(0, b.ReferenceCount));
            Assert.Equal(tails == 2 ? new[] { 1, 0 } : tails == 1 ? new[] { 0 } : Array.Empty<int>(), releases);
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, raw.ReferenceCount);
            owner.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((1, 6), (owner.ReaderIndex, owner.WriterIndex));
        }
        finally
        {
            if (owner.ReferenceCount > 0) owner.Release();
            if (first.ReferenceCount > 0) first.Release();
            foreach (ByteBuf b in trailing) if (b.ReferenceCount > 0) b.Release();
            (allocator as IDisposable)?.Dispose(); Assert.Equal(0, native.ReservedBytes);
        }
        Assert.Equal(0, child.ReferenceCount); Assert.Equal(0, raw.ReferenceCount);
        Assert.Equal(facade >= 2 ? 1 : 0, tracker.Closes);
    }
}
