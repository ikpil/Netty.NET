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
using System.Buffers;
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufFlatteningTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly IByteBufAllocator Allocator;

        public Storage(bool pooled, bool direct)
            => Allocator = pooled
                ? new PooledByteBufAllocator(direct, pageSize: 4096, maxOrder: 1, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(direct, Native);

        public void Dispose()
        {
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Providers()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true }) yield return [pooled, direct];
    }

    public static IEnumerable<object[]> Wrappers()
    {
        foreach (object[] provider in Providers())
        foreach (bool wrapped in new[] { false, true }) yield return [provider[0], provider[1], wrapped];
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void FlatteningMixedRetainedSlicesPreservesTheirOwnCountersAndReadableIntersections(
        bool pooled, bool direct, bool wrapped)
    {
        using var storage = new Storage(pooled, direct);
        // The original composites/prefix are unpooled; only the retained-slice root must be pooled.
        var compositeAllocator = new UnpooledByteBufAllocator(direct, storage.Native);
        CompositeByteBuf target = compositeAllocator.CompositeBuffer();
        CompositeByteBuf source = null;
        ByteBuf root = null, copy = null, prefix = null;
        ByteBuf s1 = null, s2 = null, s3 = null, s4 = null;
        bool rootOwned = false;
        try
        {
            prefix = compositeAllocator.Buffer(3, 3).WriteBytes(new byte[] { 1, 2, 3 });
            target.AddComponent(prefix, true).AddFlattenedComponents(prefix.Retain(), true)
                .AddFlattenedComponents(Unpooled.EmptyBuffer, true);
            Assert.Equal(2, target.NumComponents);
            Assert.Equal(6, target.Capacity);
            Assert.Equal(6, target.WriterIndex);
            // It is important to use a pooled allocator here to ensure
            // the slices returned by readRetainedSlice are of type
            // PooledSlicedByteBuf, which maintains an independent refcount
            // (so that we can be sure to cover this case)
            // CLR: also verify the shared-counter unpooled alternative.
            root = storage.Allocator.Buffer().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            rootOwned = true;
            // use mixture of slice and retained slice
            s1 = root.ReadRetainedSlice(2);
            s2 = s1.RetainedSlice(0, 2);
            s3 = root.Slice(0, 2).Retain();
            s4 = s2.RetainedSlice(0, 2);
            bool deallocated = root.Release();
            rootOwned = false;
            Assert.False(deallocated);
            source = compositeAllocator.CompositeBuffer().AddComponent(s1).AddComponent(Unpooled.EmptyBuffer)
                .AddComponents(s2, s3, s4);
            // set readable range to be from middle of first component
            // to middle of penultimate component
            source.SetIndex(1, 5);
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(pooled ? 1 : 4, s4.ReferenceCount);
            copy = source.Copy();
            CompositeByteBuf input = wrapped ? new WrappedCompositeByteBuf(source) : source;
            target.AddFlattenedComponents(input, true);
            // verify that added range matches
            Assert.Equal(copy.ReadableMemory.ToArray(), target.AsReadOnlySequence(6, 4).ToArray());
            Assert.Equal(new byte[] { 1, 2, 3, 1, 2, 3, 2, 1, 2, 1 }, target.ReadableSequence.ToArray());
            // should not include empty component or last component
            // (latter outside of the readable range)
            Assert.Equal(5, target.NumComponents);
            Assert.Equal(10, target.Capacity);
            Assert.Equal(10, target.WriterIndex);
            Assert.Equal(0, input.ReferenceCount);
            Assert.Equal(0, source.ReferenceCount);
            // s4 wasn't in added range so should have been jettisoned
            Assert.Equal(pooled ? 0 : 3, s4.ReferenceCount);
            Assert.Equal(pooled ? 2 : 3, s1.ReferenceCount);
            Assert.Equal(pooled ? 1 : 3, s2.ReferenceCount);
            Assert.Equal(pooled ? 2 : 3, s3.ReferenceCount);
            Assert.Equal(1, target.ReferenceCount);
            s2.SetByte(1, 99);
            Assert.Equal(new byte[] { 1, 2, 3, 1, 2, 3, 99, 1, 99, 1 }, target.ReadableSequence.ToArray());
            Assert.Equal(new byte[] { 2, 1, 2, 1 }, copy.ReadableMemory.ToArray());
            // releasing composite should release the remaining components
            Assert.True(target.Release());
            Assert.Equal(0, target.ReferenceCount);
            Assert.All(new[] { root, prefix, s1, s2, s3, s4 }, buffer => Assert.Equal(0, buffer.ReferenceCount));
        }
        finally
        {
            copy?.Release(); // CLR: the original temporary copy must also release its owned storage.
            if (target.ReferenceCount > 0) target.Release();
            if (source?.ReferenceCount > 0) source.Release();
            if (rootOwned) root.Release();
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void NestedSliceExposesTwoBorrowedSingleByteMemorySegments(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf inner = storage.Allocator.CompositeBuffer(), outer = storage.Allocator.CompositeBuffer();
        ByteBuf first = storage.Allocator.Buffer(2, 2).WriteBytes(new byte[] { 1, 2 });
        ByteBuf second = storage.Allocator.Buffer(2, 2).WriteBytes(new byte[] { 3, 4 });
        try
        {
            inner.AddComponent(first).AddComponent(second);
            outer.AddComponent(inner.Slice(1, 2));
            // CLR: ReadOnlySequence preserves the original nioBuffers segment boundaries without Java ByteBuffer objects.
            ReadOnlySequence<byte> sequence = outer.AsReadOnlySequence(0, 2);
            Assert.False(sequence.IsSingleSegment);
            var segments = new List<byte[]>();
            foreach (ReadOnlyMemory<byte> segment in sequence) segments.Add(segment.ToArray());
            Assert.Equal(2, segments.Count);
            Assert.Equal(new byte[] { 2 }, segments[0]);
            Assert.Equal(new byte[] { 3 }, segments[1]);
            Assert.Equal(2, outer.Capacity);
            Assert.Equal(0, outer.WriterIndex);
            Assert.Equal(0, inner.WriterIndex);
            Assert.Equal(1, inner.ReferenceCount);
            first.SetByte(1, 9);
            Assert.Equal(new byte[] { 9, 3 }, sequence.ToArray());
        }
        finally
        {
            outer.Release();
            if (inner.ReferenceCount > 0) inner.Release();
        }
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, inner.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ForwardAndReverseVisitorsSeeEveryByteOfTheNestedComposite(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf inner = storage.Allocator.CompositeBuffer(), outer = storage.Allocator.CompositeBuffer();
        try
        {
            inner.AddComponents(new[]
            {
                storage.Allocator.Buffer(3, 3).WriteBytes(new byte[] { 1, 2, 3 }),
                storage.Allocator.Buffer(3, 3).WriteBytes(new byte[] { 4, 5, 6 })
            }, true);
            outer.AddComponent(inner, true);
            var ascending = new byte[6];
            var descending = new byte[6];
            int forward = 0, reverse = 0;
            Assert.Equal(-1, outer.ForEachByte(value => { ascending[forward++] = value; return true; }));
            Assert.Equal(-1, outer.ForEachByteDesc(value => { descending[reverse++] = value; return true; }));
            Assert.Equal(6, forward);
            Assert.Equal(6, reverse);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, ascending);
            Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1 }, descending);
            Assert.Equal(0, inner.ReaderIndex);
            Assert.Equal(0, outer.ReaderIndex);
            Assert.Equal(6, inner.WriterIndex);
            Assert.Equal(6, outer.WriterIndex);
        }
        finally
        {
            outer.Release();
            if (inner.ReferenceCount > 0) inner.Release();
        }
        Assert.Equal(0, inner.ReferenceCount);
    }
}
