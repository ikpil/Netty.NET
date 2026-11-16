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

// Selected original decompose/removal contracts, including independent pooled
// source counters. A decomposition borrows references; it does not acquire them.
public class CompositeByteBufOwnershipTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly IByteBufAllocator Allocator;
        public Storage(bool pooled, bool direct)
        {
            Allocator = pooled
                ? new PooledByteBufAllocator(direct, pageSize: 4096, maxOrder: 1, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(direct, Native);
        }
        public void Dispose()
        {
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }
    private static byte[] Read(ByteBuf buffer, int index, int length)
    {
        byte[] result = new byte[length]; buffer.GetBytes(index, result); return result;
    }
    public static IEnumerable<object[]> ReleaseOrders()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        foreach (bool rootFirst in new[] { false, true }) yield return [pooled, direct, rootFirst];
    }
    public static IEnumerable<object[]> Factories()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true }) yield return [pooled, direct];
    }
    public static IEnumerable<object[]> Removals()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        foreach (int shape in new[] { 0, 1, 2 }) yield return [pooled, direct, shape];
    }

    // CLR: heap/native pooled/unpooled factories and both root/composite release orders.
    [Theory]
    [MemberData(nameof(ReleaseOrders))]
    public void TestDecomposeMultipleAcrossProviders(bool pooled, bool direct, bool rootFirst)
    {
        TestDecompose(pooled, direct, rootFirst, 150, 500, 3);
    }

    [Theory]
    [MemberData(nameof(ReleaseOrders))]
    public void TestDecomposeOneAcrossProviders(bool pooled, bool direct, bool rootFirst)
    {
        TestDecompose(pooled, direct, rootFirst, 310, 50, 1);
    }

    [Theory]
    [MemberData(nameof(ReleaseOrders))]
    public void TestDecomposeNoneAcrossProviders(bool pooled, bool direct, bool rootFirst)
    {
        TestDecompose(pooled, direct, rootFirst, 310, 0, 0);
    }

    // CLR: the root is an allocator buffer instead of wrappedBuffer(bytes); concatenated borrowed contents
    // replace the temporary wrapping composite, and the decomposition acquires no references.
    private static void TestDecompose(bool pooled, bool direct, bool rootFirst, int offset, int length, int expectedListSize)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf root = s.Allocator.Buffer(1024, 1024);
        CompositeByteBuf composite = s.Allocator.CompositeBuffer();
        var components = new List<ByteBuf>();
        IReadOnlyList<ByteBuf> parts = Array.Empty<ByteBuf>();
        try
        {
            byte[] bytes = new byte[1024];
            Random.Shared.NextBytes(bytes);
            root.WriteBytes(bytes);
            foreach ((int sliceIndex, int sliceLength) in new[] { (100, 200), (300, 400), (700, 100) })
            {
                ByteBuf component = root.RetainedSlice(sliceIndex, sliceLength);
                components.Add(component); composite.AddComponent(component, true);
            }
            (int start, int count, int expectedParts) = (offset, length, expectedListSize);
            parts = composite.Decompose(start, count);
            Assert.Equal(expectedParts, parts.Count);
            Assert.Equal(4, root.RefCnt);
            Assert.All(components, component => Assert.Equal(pooled ? 1 : 4, component.RefCnt));
            var actual = new List<byte>();
            foreach (ByteBuf part in parts)
            {
                Assert.Equal(0, part.ReaderIndex);
                Assert.Equal(part.Capacity, part.WriterIndex);
                actual.AddRange(Read(part, 0, part.ReadableBytes));
            }
            byte[] expected = new byte[count];
            for (int i = 0; i < count; ++i) expected[i] = bytes[100 + start + i];
            Assert.Equal(expected, actual.ToArray());
            Assert.Equal(expected, Read(composite.Slice(start, count), 0, count));
            if (count != 0)
            {
                parts[0].SetByte(0, 0xa5);
                Assert.Equal(0xa5, root.GetByte(100 + start));
                Assert.Equal(0xa5, composite.GetByte(start));
                root.SetByte(100 + start, bytes[100 + start]);
            }
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(700, composite.WriterIndex);
            Assert.Equal(0, root.ReaderIndex);
            Assert.Equal(1024, root.WriterIndex);
            if (rootFirst)
            {
                Assert.False(root.Release());
                Assert.Equal(expected, Read(composite, start, count));
                Assert.True(composite.Release());
            }
            else
            {
                Assert.True(composite.Release());
                Assert.Equal(1, root.RefCnt);
                if (pooled) Assert.All(parts, part => Assert.Equal(0, part.RefCnt));
                else Assert.All(parts, part => Assert.Equal(1, part.RefCnt));
                Assert.True(root.Release());
            }
            Assert.Equal(0, root.RefCnt);
            Assert.All(components, component => Assert.Equal(0, component.RefCnt));
            Assert.All(parts, part => Assert.Equal(0, part.RefCnt));
        }
        finally
        {
            if (composite.RefCnt > 0) composite.Release();
            if (root.RefCnt > 0) root.Release(root.RefCnt);
        }
    }

    // CLR: the original pooled root plus heap/native unpooled shared-counter variants.
    [Theory]
    [MemberData(nameof(Factories))]
    public void TestDecomposeReturnNonUnwrappedBufferAcrossProviders(bool pooled, bool direct)
    {
        TestDecomposeReturnNonUnwrapped(pooled, direct, 1);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestDecomposeReturnNonUnwrappedBuffers(bool pooled, bool direct)
    {
        TestDecomposeReturnNonUnwrapped(pooled, direct, 2);
    }

    // CLR: shared body of the two originals, which differ only in the number of retained slices
    // (sliced = retainedSlice(100, 200) with decompose(0, 100), or also sliced2 = retainedSlice(400, 100)
    // with decompose(100, 150)).
    private static void TestDecomposeReturnNonUnwrapped(bool pooled, bool direct, int count)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf root = s.Allocator.Buffer(1024).WriteZero(1024);
        // Original pooled retained components are decomposed by an unpooled composite.
        CompositeByteBuf composite = new UnpooledByteBufAllocator(direct, s.Native).CompositeBuffer();
        var components = new List<ByteBuf>();
        try
        {
            for (int i = 0; i < count; ++i)
            {
                ByteBuf component = root.RetainedSlice(i == 0 ? 100 : 400, i == 0 ? 200 : 100).Retain();
                components.Add(component); composite.AddComponent(component, true);
            }
            IReadOnlyList<ByteBuf> parts = composite.Decompose(count == 1 ? 0 : 100, count == 1 ? 100 : 150);
            Assert.Equal(count, parts.Count);
            Assert.Equal(pooled ? 1 + count : 1 + 2 * count, root.RefCnt);
            for (int i = 0; i < count; ++i)
            {
                Assert.Equal(components[i].RefCnt, parts[i].RefCnt);
                Assert.Equal(pooled ? 2 : 1 + 2 * count, parts[i].RefCnt);
            }
            // Consume the explicit Retain claim, not the composite's transferred claim.
            foreach (ByteBuf part in parts) part.Release();
            for (int i = 0; i < count; ++i)
                Assert.Equal(components[i].RefCnt, parts[i].RefCnt);
            Assert.All(components, component => Assert.Equal(pooled ? 1 : 1 + count, component.RefCnt));
            Assert.True(composite.Release());
            Assert.Equal(1, root.RefCnt);
            if (pooled)
                foreach (ByteBuf part in parts)
                    Assert.Throws<IllegalReferenceCountException>(() => part.GetByte(0));
            else Assert.All(parts, part => Assert.Equal(0, part.GetByte(0)));
            Assert.True(root.Release());
            Assert.All(parts, part => Assert.Equal(0, part.RefCnt));
            Assert.All(components, component => Assert.Equal(0, component.RefCnt));
        }
        finally
        {
            if (composite.RefCnt > 0) composite.Release();
            foreach (ByteBuf component in components)
                if (pooled && component.RefCnt > 0) component.Release(component.RefCnt);
            if (root.RefCnt > 0) root.Release(root.RefCnt);
        }
    }

    [Theory]
    [MemberData(nameof(Removals))]
    public void ExplicitRemovalReleasesOnlyTheTransferredClaimsAndRebuildsOffsets(bool pooled, bool direct, int shape)
    {
        using var s = new Storage(pooled, direct);
        CompositeByteBuf composite = s.Allocator.CompositeBuffer();
        var sources = new List<ByteBuf>();
        int count = shape == 0 ? 1 : shape == 1 ? 2 : 10;
        int removeAt = shape == 0 ? 0 : shape == 1 ? 1 : 4, removeCount = shape == 2 ? 3 : 1;
        try
        {
            for (int i = 0; i < count; ++i)
            {
                ByteBuf source = s.Allocator.Buffer(4, 4)
                    .WriteBytes(new byte[] { 0xcc, unchecked((byte)(2 * i + 1)), unchecked((byte)(2 * i + 2)), 0xdd })
                    .SetIndex(1, 3);
                sources.Add(source); composite.AddComponent(source, true);
            }
            composite.SetIndex(1, count * 2);
            Assert.Equal(count, composite.NumComponents);
            Assert.Equal(count * 2, composite.Capacity);
            if (removeCount == 1) Assert.Same(composite, composite.RemoveComponent(removeAt));
            else Assert.Same(composite, composite.RemoveComponents(removeAt, removeCount));
            Assert.Equal(count - removeCount, composite.NumComponents);
            Assert.Equal((count - removeCount) * 2, composite.Capacity);
            Assert.Equal(1, composite.ReaderIndex);
            Assert.Equal(count * 2, composite.WriterIndex);
            var expected = new List<byte>();
            int remainingIndex = 0;
            for (int i = 0; i < count; ++i)
            {
                bool removed = i >= removeAt && i < removeAt + removeCount;
                Assert.Equal(removed ? 0 : 1, sources[i].RefCnt);
                if (removed) continue;
                Assert.Equal(remainingIndex * 2, composite.ToByteIndex(remainingIndex));
                Assert.Equal(remainingIndex, composite.ToComponentIndex(remainingIndex * 2));
                Assert.Equal(1, sources[i].ReaderIndex);
                Assert.Equal(3, sources[i].WriterIndex);
                expected.Add(unchecked((byte)(2 * i + 1))); expected.Add(unchecked((byte)(2 * i + 2)));
                remainingIndex++;
            }
            Assert.Equal(expected.ToArray(), Read(composite, 0, composite.Capacity));
            Assert.Same(composite, composite.RemoveComponents(composite.NumComponents, 0));
        }
        finally { composite.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
    }

    [Fact]
    public void TestRemoveLastComponent()
    {
        using var storage = new Storage(false, false);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();
        try
        {
            buf.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
            Assert.Equal(1, buf.NumComponents);
            buf.RemoveComponent(0);
            Assert.Equal(0, buf.NumComponents);
        }
        finally { buf.Release(); }
    }

    [Fact]
    public void TestRemoveLastComponentWithOthersLeft()
    {
        using var storage = new Storage(false, false);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();
        try
        {
            buf.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
            buf.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
            Assert.Equal(2, buf.NumComponents);
            buf.RemoveComponent(1);
            Assert.Equal(1, buf.NumComponents);
        }
        finally { buf.Release(); }
    }

    [Fact]
    public void TestRemoveComponents()
    {
        using var storage = new Storage(false, false);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();
        try
        {
            for (int i = 0; i < 10; i++) buf.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
            Assert.Equal(10, buf.NumComponents);
            Assert.Equal(20, buf.Capacity);
            buf.RemoveComponents(4, 3);
            Assert.Equal(7, buf.NumComponents);
            Assert.Equal(14, buf.Capacity);
        }
        finally { buf.Release(); }
    }
}
