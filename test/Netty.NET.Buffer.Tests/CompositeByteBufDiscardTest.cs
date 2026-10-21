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

public class CompositeByteBufDiscardTest
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
    public static IEnumerable<object[]> Discards()
    {
        foreach (object[] p in Providers())
        foreach (bool some in new[] { false, true }) yield return [p[0], p[1], some];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void BorrowedSliceChainReleasesAllFourTransferredClaims(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf buffer = s.Allocator.Buffer(16, 16); // 1
        buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        ByteBuf s1 = buffer.ReadSlice(2).Retain(); // 2
        ByteBuf s2 = s1.ReadSlice(2).Retain(); // 3
        ByteBuf s3 = s2.ReadSlice(2).Retain(); // 4
        ByteBuf s4 = s3.ReadSlice(2).Retain(); // 5
        CompositeByteBuf composite = s.Allocator.CompositeBuffer().AddComponent(s1).AddComponents(s2, s3, s4);
        try
        {
            Assert.Equal(1, composite.ReferenceCount);
            Assert.Equal(5, buffer.ReferenceCount);
            Assert.Equal(4, composite.NumComponents);
            Assert.Equal(2, composite.Capacity);
            Assert.Equal(0, composite.WriterIndex);
            // releasing composite should release the 4 components
            Assert.True(composite.Release());
            Assert.Equal(0, composite.ReferenceCount);
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(1, s4.ReferenceCount);
            Assert.Equal(2, buffer.ReaderIndex);
            // last remaining ref to buffer
            Assert.True(buffer.Release());
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.All(new[] { s1, s2, s3, s4 }, view => Assert.Equal(0, view.ReferenceCount));
        }
        finally
        {
            if (composite.ReferenceCount > 0) composite.Release();
            if (buffer.ReferenceCount > 0) buffer.Release(buffer.ReferenceCount);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void RetainedSliceChainDeallocationFollowsEachImmediateParent(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        // It is important to use a pooled allocator here to ensure
        // the slices returned by readRetainedSlice are of type
        // PooledSlicedByteBuf, which maintains an independent refcount
        // (so that we can be sure to cover this case)
        // CLR: additionally verify the shared-counter unpooled alternative.
        ByteBuf buffer = s.Allocator.Buffer(16, 16); // 1
        buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        // use readRetainedSlice this time - produces different kind of slices
        ByteBuf s1 = buffer.ReadRetainedSlice(2); // 2
        ByteBuf s2 = s1.ReadRetainedSlice(2); // 3
        ByteBuf s3 = s2.ReadRetainedSlice(2); // 4
        ByteBuf s4 = s3.ReadRetainedSlice(2); // 5
        CompositeByteBuf composite = s.Allocator.CompositeBuffer().AddComponent(s1).AddComponents(s2, s3, s4);
        try
        {
            Assert.Equal(1, composite.ReferenceCount);
            Assert.Equal(pooled ? 2 : 5, buffer.ReferenceCount);
            Assert.All(new[] { s1, s2, s3 }, view => Assert.Equal(pooled ? 2 : 5, view.ReferenceCount));
            Assert.Equal(pooled ? 1 : 5, s4.ReferenceCount);
            Assert.Equal(4, composite.NumComponents);
            Assert.Equal(2, composite.Capacity);
            // releasing composite should release the 4 components
            Assert.True(composite.Release());
            Assert.Equal(0, composite.ReferenceCount);
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.All(new[] { s1, s2, s3, s4 }, view => Assert.Equal(pooled ? 0 : 1, view.ReferenceCount));
            // last remaining ref to buffer
            Assert.True(buffer.Release());
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.All(new[] { s1, s2, s3, s4 }, view => Assert.Equal(0, view.ReferenceCount));
        }
        finally
        {
            if (composite.ReferenceCount > 0) composite.Release();
            if (buffer.ReferenceCount > 0) buffer.Release(buffer.ReferenceCount);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ShrinkingReleasesTheRemovedTailAndKeepsTheRemainingClaim(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf b1 = s.Allocator.Buffer(2, 2).WriteShort(1);
        ByteBuf b2 = s.Allocator.Buffer(2, 2).WriteShort(2);
        // composite takes ownership of s1 and s2
        CompositeByteBuf composite = s.Allocator.CompositeBuffer().AddComponents(b1, b2);
        try
        {
            Assert.Equal(4, composite.Capacity);
            // reduce capacity down to two, will drop the second component
            composite.Capacity = 2;
            Assert.Equal(2, composite.Capacity);
            Assert.Equal(1, composite.NumComponents);
            Assert.Equal(1, b1.ReferenceCount);
            Assert.Equal(0, b2.ReferenceCount);
            Assert.Equal(1, composite.GetShort(0));
            // releasing composite should release the components
            Assert.True(composite.Release());
            Assert.Equal(0, composite.ReferenceCount);
            Assert.Equal(0, b1.ReferenceCount);
            Assert.Equal(0, b2.ReferenceCount);
        }
        finally { if (composite.ReferenceCount > 0) composite.Release(); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ShrinkingNestedRetainedComponentsReleasesOnlyTheRemovedChild(bool pooled, bool direct)
    {
        using var s = new Storage(pooled, direct);
        // It is important to use a pooled allocator here to ensure
        // the slices returned by readRetainedSlice are of type
        // PooledSlicedByteBuf, which maintains an independent refcount
        // (so that we can be sure to cover this case)
        ByteBuf buffer = s.Allocator.Buffer(4, 4).WriteShort(1).WriteShort(2);
        ByteBuf b1 = buffer.ReadRetainedSlice(2);
        ByteBuf b2 = b1.RetainedSlice(b1.ReaderIndex, 2);
        // composite takes ownership of b1 and b2
        CompositeByteBuf composite = s.Allocator.CompositeBuffer().AddComponents(b1, b2);
        try
        {
            Assert.Equal(4, composite.Capacity);
            Assert.Equal(pooled ? 2 : 3, buffer.ReferenceCount);
            Assert.Equal(pooled ? 2 : 3, b1.ReferenceCount);
            // reduce capacity down to two, will drop the second component
            composite.Capacity = 2;
            Assert.Equal(2, composite.Capacity);
            Assert.Equal(1, b1.GetShort(0));
            Assert.Equal(pooled ? 1 : 2, b1.ReferenceCount);
            Assert.Equal(pooled ? 0 : 2, b2.ReferenceCount);
            // releasing composite should release the components
            Assert.True(composite.Release());
            Assert.Equal(0, composite.ReferenceCount);
            Assert.Equal(pooled ? 0 : 1, b1.ReferenceCount);
            Assert.Equal(pooled ? 0 : 1, b2.ReferenceCount);
            // release last remaining ref to buffer
            Assert.True(buffer.Release());
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.Equal(0, b1.ReferenceCount);
            Assert.Equal(0, b2.ReferenceCount);
        }
        finally
        {
            if (composite.ReferenceCount > 0) composite.Release();
            if (buffer.ReferenceCount > 0) buffer.Release(buffer.ReferenceCount);
        }
    }

    [Theory]
    [MemberData(nameof(Discards))]
    public void RepeatedDiscardKeepsSubsequentComponentReadsAndReleasesCorrect(bool pooled, bool direct, bool some)
    {
        using var s = new Storage(pooled, direct);
        CompositeByteBuf composite = s.Allocator.CompositeBuffer();
        var sources = new List<ByteBuf>();
        void Discard() { if (some) composite.DiscardSomeReadBytes(); else composite.DiscardReadBytes(); }
        try
        {
            for (int i = 0; i < 4; ++i)
            {
                ByteBuf source = s.Allocator.Buffer(4, 4).WriteInt(i);
                sources.Add(source); composite.AddComponent(source, true);
            }
            // Skip the first 2 bytes which means even if we call discard*ReadBytes() later we can no drop the first
            // component as it is still used.
            composite.SkipBytes(2); Discard();
            Assert.Equal(4, composite.NumComponents);
            Assert.Equal(some ? 2 : 0, composite.ReaderIndex);
            Assert.Equal(some ? 16 : 14, composite.Capacity);
            // Now skip 3 bytes which means we should be able to drop the first component on the next discard*ReadBytes()
            // call.
            composite.SkipBytes(3); Discard();
            Assert.Equal(3, composite.NumComponents);
            Assert.Equal(0, sources[0].ReferenceCount);
            // Now skip again 3 bytes which should bring our readerIndex == start of the 3 component.
            composite.SkipBytes(3);
            // Read one int (4 bytes) which should bring our readerIndex == start of the 4 component.
            Assert.Equal(2, composite.ReadInt()); Discard();
            // Now all except the last component should have been dropped / released.
            Assert.Equal(1, composite.NumComponents);
            Assert.Equal(3, composite.ReadInt()); Discard();
            Assert.Equal(0, composite.NumComponents);
            Assert.Equal(0, composite.Capacity);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(0, composite.WriterIndex);
            // These should have been released already.
            Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
            Assert.True(composite.Release());
        }
        finally { if (composite.ReferenceCount > 0) composite.Release(); }
    }
}
