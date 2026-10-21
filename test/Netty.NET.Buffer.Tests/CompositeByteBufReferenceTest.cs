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

public class CompositeByteBufReferenceTest
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

    public static IEnumerable<object[]> Ownerships()
    {
        foreach (object[] provider in Providers())
        foreach (int mode in new[] { 0, 1, 2 }) yield return [provider[0], provider[1], mode];
    }

    public static IEnumerable<object[]> Alternatives()
    {
        foreach (object[] provider in Providers())
        foreach (bool alternative in new[] { false, true }) yield return [provider[0], provider[1], alternative];
    }

    [Theory]
    [MemberData(nameof(Ownerships))]
    public void ArrayEnumerableAndNestedTransfersPreserveOriginalClaimsUntilTheirReleaseOrder(
        bool pooled, bool direct, int mode)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf c1 = storage.Allocator.Buffer(16, 16).WriteByte(1);
        ByteBuf c2 = storage.Allocator.Buffer(16, 16).WriteByte(2).Retain();
        ByteBuf c3 = storage.Allocator.Buffer(16, 16).WriteByte(3).Retain(2);
        ByteBuf[] sources = [c1, c2, c3];
        CompositeByteBuf inner = storage.Allocator.CompositeBuffer(), outer = null;
        try
        {
            Assert.Equal(1, inner.ReferenceCount);
            if (mode == 1) inner.AddComponents((IEnumerable<ByteBuf>)new List<ByteBuf>(sources));
            else inner.AddComponents(sources);
            Assert.Equal(1, inner.ReferenceCount);
            Assert.Equal(0, inner.WriterIndex);
            if (mode == 2)
            {
                inner.WriterIndex = 3;
                outer = storage.Allocator.CompositeBuffer().AddComponents(inner);
                // Ensure that bufA.refCnt() did not change.
                Assert.Equal(1, inner.ReferenceCount);
                // Ensure that c[123]'s refCnt did not change.
                Assert.Equal(new[] { 1, 2, 3 }, new[] { c1.ReferenceCount, c2.ReferenceCount, c3.ReferenceCount });
            }
            // Ensure that c[123]'s refCount did not change.
            for (int i = 0; i < sources.Length; ++i)
            {
                Assert.Equal(i + 1, sources[i].ReferenceCount);
                Assert.Equal(i + 1, inner.Component(i).ReferenceCount);
            }
            if (mode == 2)
            {
                // This should decrease bufA.refCnt().
                Assert.True(outer.Release());
                Assert.Equal(0, outer.ReferenceCount);
                // Ensure bufA.refCnt() changed.
                Assert.Equal(0, inner.ReferenceCount);
                // Ensure that c[123]'s refCnt also changed due to the deallocation of bufA.
                Assert.Equal(new[] { 0, 1, 2 }, new[] { c1.ReferenceCount, c2.ReferenceCount, c3.ReferenceCount });
                Assert.True(c3.Release(2));
                Assert.True(c2.Release());
            }
            else
            {
                Assert.False(c3.Release(2));
                Assert.False(c2.Release());
                Assert.All(sources, source => Assert.Equal(1, source.ReferenceCount));
                Assert.True(inner.Release());
            }
            Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
        }
        finally
        {
            if (outer?.ReferenceCount > 0) outer.Release();
            if (inner.ReferenceCount > 0) inner.Release();
            foreach (ByteBuf source in sources)
                if (source.ReferenceCount > 0) source.Release(source.ReferenceCount);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ComponentDuplicatePreservesSourceCapacityLimitsAndIndependentIndices(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf source = storage.Allocator.Buffer(4, 6).SetIndex(1, 3);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer().AddComponent(source);
        ByteBuf view = null;
        try
        {
            view = composite.Component(0);
            // CLR verifies the duplicate contract without requiring Java's AbstractDerivedByteBuf class hierarchy.
            Assert.NotSame(source, view);
            Assert.Equal(4, view.Capacity);
            Assert.Equal(6, view.MaxCapacity);
            Assert.Equal(2, view.ReadableBytes);
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(3, view.WriterIndex);
            view.ReaderIndex = 2;
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(1, composite.Component(0).ReaderIndex);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(1, source.ReferenceCount);
            view.Retain();
            Assert.Equal(2, source.ReferenceCount);
            Assert.False(view.Release());
        }
        finally { composite.Release(); }
        Assert.Equal(0, source.ReferenceCount);
        Assert.Equal(0, view.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Alternatives))]
    public void OwnedEmptySourcesAreTransferredAndReleasedEvenWithoutReadableBytes(bool pooled, bool direct, bool multiple)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf empty = storage.Allocator.Buffer(16, 16);
        ByteBuf[] sources = multiple
            ? [empty, storage.Allocator.Buffer(16, 16).WriteInt(1), storage.Allocator.Buffer(16, 16)]
            : [empty];
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer();
        try
        {
            Assert.All(sources, source => Assert.Equal(1, source.ReferenceCount));
            if (multiple) composite.AddComponents(sources);
            else composite.AddComponent(empty);
            Assert.Equal(multiple ? 3 : 1, composite.NumComponents);
            Assert.Equal(multiple ? 4 : 0, composite.Capacity);
            Assert.Equal(0, composite.WriterIndex);
            Assert.All(sources, source => Assert.Equal(1, source.ReferenceCount));
            if (multiple) Assert.Equal(1, composite.GetInt(0));
        }
        finally { composite.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
    }

    [Theory]
    [MemberData(nameof(Alternatives))]
    public void AppendedAndInsertedEmptySingletonsKeepSlotsButConsumeNoByteOffsets(bool pooled, bool direct, bool insert)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf first = storage.Allocator.Buffer(1, 1).WriteByte(1);
        ByteBuf second = storage.Allocator.Buffer(1, 1).WriteByte(2);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer().AddComponent(first, true);
        try
        {
            if (insert)
            {
                composite.AddComponent(second, true);
                // insert empty one between the first two
                composite.AddComponent(1, Unpooled.EmptyBuffer, true);
            }
            else composite.AddComponent(Unpooled.EmptyBuffer, true).AddComponent(second, true);
            Assert.Equal(2, composite.ReadableBytes);
            Assert.Equal(1, composite.ReadByte());
            Assert.Equal(2, composite.ReadByte());
            Assert.Same(Unpooled.EmptyBuffer, composite.InternalComponent(1));
            Assert.NotSame(Unpooled.EmptyBuffer, composite.InternalComponentAtOffset(1));
            Assert.Equal(2, composite.Capacity);
            Assert.Equal(3, composite.NumComponents);
            var destination = new byte[2];
            // should skip over the empty one, not throw a java.lang.Error :)
            composite.GetBytes(0, destination);
            Assert.Equal(new byte[] { 1, 2 }, destination);
        }
        finally { composite.Release(); }
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(1, Unpooled.EmptyBuffer.ReferenceCount);
    }
}
