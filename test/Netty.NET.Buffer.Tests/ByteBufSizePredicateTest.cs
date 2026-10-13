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

public class ByteBufSizePredicateTest
{
    private static readonly int[] Counts = [int.MinValue, -1, 0, 1, 2, 3, 4, 7, 8, 9, 10, 11, 13, 16, 64, int.MaxValue];

    public static IEnumerable<object[]> BackingAndViewCases()
    {
        for (int backing = 0; backing < 6; backing++)
            for (int view = 0; view < 6; view++) yield return [backing, view];
    }

    [Theory]
    [MemberData(nameof(BackingAndViewCases))]
    public void OriginalSizeQueriesUseIndependentViewIndicesAndCurrentCapacity(int backing, int viewKind)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf owner = Owner(backing, native, out IDisposable allocator);
        ByteBuf view = null;
        bool retained = viewKind >= 4;
        try
        {
            owner.SetIndex(3, 13).MarkReaderIndex().MarkWriterIndex();
            view = viewKind switch
            {
                0 => owner, 1 => owner.Slice(2, 8), 2 => owner.Duplicate(),
                3 => owner.AsReadOnly(), 4 => owner.RetainedSlice(2, 8), _ => owner.RetainedDuplicate()
            };
            bool slice = viewKind is 1 or 4;
            int reader = slice ? 0 : 3, writer = slice ? 8 : 13;
            // Literal readable/writable budgets distinguish capacity from MaxCapacity (64).
            Check(view, slice ? 8 : 10, slice ? 0 : 3, backing == 5 || viewKind == 3);
            int references = owner.ReferenceCount;
            view.MarkReaderIndex().MarkWriterIndex();
            Check(view, slice ? 8 : 10, slice ? 0 : 3, backing == 5 || viewKind == 3);
            Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
            Assert.Equal(references, owner.ReferenceCount);
            view.SetIndex(1, 7);
            Check(view, 6, slice ? 1 : 9, backing == 5 || viewKind == 3);
            view.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
            if (viewKind != 0 && !(backing == 5 && viewKind == 3))
            {
                owner.SetIndex(0, 16);
                Check(view, slice ? 8 : 10, slice ? 0 : 3, backing == 5 || viewKind == 3);
            }
        }
        finally
        {
            if (retained && view != null) view.Release();
            owner.Release(); allocator?.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    private static void Check(ByteBuf b, int readable, int writable, bool readOnly)
    {
        foreach (int count in Counts)
        {
            Assert.Equal(count <= readable, b.CanRead(count));
            Assert.Equal(!readOnly && count <= writable, b.CanWrite(count));
        }
        Assert.Equal(readable > 0, b.IsReadable);
        Assert.Equal(!readOnly && writable > 0, b.IsWritable);
    }

    private static ByteBuf Owner(int kind, NativeMemoryAllocator native, out IDisposable disposable)
    {
        disposable = null;
        var a = new UnpooledByteBufAllocator(false, true, native);
        if (kind == 0) return a.HeapBuffer(16, 64);
        if (kind == 1) return a.DirectBuffer(16, 64);
        if (kind is 2 or 3)
        {
            var pooled = new PooledByteBufAllocator(false, maxOrder: 1, nativeAllocator: native);
            disposable = pooled;
            return kind == 2 ? pooled.HeapBuffer(16, 64) : pooled.DirectBuffer(16, 64);
        }
        ByteBuf x = a.HeapBuffer(8, 8).SetIndex(0, 8), y = a.HeapBuffer(8, 8).SetIndex(0, 8);
        return kind == 4 ? new CompositeByteBuf(a, false, 4).AddComponents([x, y], true)
            : Unpooled.WrappedUnmodifiableBuffer(x, y);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void SharedEmptySentinelRemainsFalseThroughTransparentWrappers(int kind)
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        var tracker = new Tracker();
        ByteBuf b = kind switch
        {
            0 => empty, 1 => new PlainWrapper(empty), 2 => new SimpleLeakAwareByteBuf(empty, tracker),
            3 => new AdvancedLeakAwareByteBuf(empty, tracker), 4 => Unpooled.UnreleasableBuffer(empty),
            _ => new PlainWrapper(new AdvancedLeakAwareByteBuf(empty, tracker))
        };
        foreach (int count in Counts) { Assert.False(b.CanRead(count)); Assert.False(b.CanWrite(count)); }
        Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex);
        Assert.Equal(1, b.ReferenceCount); Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OwnedZeroCapacityAndReadOnlyEmptyViewsKeepOrdinaryIndexComparisons(bool readOnly)
    {
        ByteBuf owner = new UnpooledHeapByteBuf(0, 64);
        ByteBuf b = readOnly ? owner.AsReadOnly() : owner;
        try { Check(b, 0, 0, readOnly); }
        finally { owner.Release(); }
        ByteBuf sentinelReadOnly = Unpooled.EmptyBuffer.AsReadOnly();
        Check(sentinelReadOnly, 0, 0, true);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void OrdinaryWrappersForwardVirtualQueriesWithoutLeakRecording(int kind)
    {
        var root = new PredicateBuffer(); var tracker = new Tracker();
        ByteBuf b = kind switch
        {
            0 => new PlainWrapper(root), 1 => new SimpleLeakAwareByteBuf(root, tracker),
            2 => new AdvancedLeakAwareByteBuf(root, tracker), 3 => Unpooled.UnreleasableBuffer(root),
            _ => new PlainWrapper(new AdvancedLeakAwareByteBuf(root, tracker))
        };
        Assert.True(b.CanRead(123)); Assert.False(b.CanRead(0));
        Assert.True(b.CanWrite(456)); Assert.False(b.CanWrite(0));
        Assert.Equal(2, root.ReadQueries); Assert.Equal(2, root.WriteQueries);
        Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeWrappersForwardVirtualQueriesWithoutLeakRecording(bool advanced)
    {
        var root = new PredicateComposite(); var tracker = new Tracker();
        CompositeByteBuf b = advanced ? new AdvancedLeakAwareCompositeByteBuf(root, tracker)
            : new SimpleLeakAwareCompositeByteBuf(root, tracker);
        try
        {
            Assert.True(b.CanRead(123)); Assert.False(b.CanRead(0));
            Assert.True(b.CanWrite(456)); Assert.False(b.CanWrite(0));
            Assert.Equal(2, root.ReadQueries); Assert.Equal(2, root.WriteQueries);
            Assert.Equal(0, tracker.Records); Assert.Equal(0, tracker.Closes);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void ReadabilityQueriesDoNotAccessCapacityStorageOrReferenceCount()
    {
        var b = new IndicesOnlyBuffer();
        foreach (int count in Counts) Assert.Equal(count <= 0, b.CanRead(count));
    }

    [Fact]
    public void ReadabilityQueriesRemainMetadataOnlyAfterFinalRelease()
    {
        ByteBuf owner = new UnpooledHeapByteBuf(16, 64).SetIndex(3, 13);
        ByteBuf slice = owner.Slice(2, 8), duplicate = owner.Duplicate();
        Assert.True(owner.Release());
        foreach (int count in Counts)
        {
            Assert.Equal(count <= 10, owner.CanRead(count));
            Assert.Equal(count <= 8, slice.CanRead(count));
            Assert.Equal(count <= 10, duplicate.CanRead(count));
        }
    }

    private sealed class PlainWrapper(ByteBuf b) : WrappedByteBuf(b);
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        public void Record() => Records++; public void Record(object hint) => Records++;
        public bool Close(ByteBuf b) { Closes++; return true; }
        public Exception GetCloseStackTraceIfAny() => null;
    }
    private sealed class PredicateBuffer : IndicesOnlyBuffer
    {
        internal int ReadQueries, WriteQueries;
        public override bool CanRead(int count) { ReadQueries++; return count == 123; }
        public override bool CanWrite(int count) { WriteQueries++; return count == 456; }
    }
    private sealed class PredicateComposite : CompositeByteBuf
    {
        internal int ReadQueries, WriteQueries;
        public override bool CanRead(int count) { ReadQueries++; return count == 123; }
        public override bool CanWrite(int count) { WriteQueries++; return count == 456; }
    }
    private class IndicesOnlyBuffer() : ByteBuf(64)
    {
        public override int Capacity { get => throw new Exception("Capacity accessed"); set => throw new NotSupportedException(); }
        public override int ReferenceCount => throw new Exception("Reference count accessed");
        public override bool IsDirect => false;
        protected override Memory<byte> GetMemoryCore(int index, int length) => throw new Exception("Content accessed");
        public override ByteBuf Retain(int increment = 1) => throw new NotSupportedException();
        public override bool Release(int decrement = 1) => throw new NotSupportedException();
    }
}
