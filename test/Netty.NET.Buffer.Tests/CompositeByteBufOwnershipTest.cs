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
    public static IEnumerable<object[]> Ranges()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        foreach (int range in new[] { 0, 1, 2 })
        foreach (bool rootFirst in new[] { false, true }) yield return [pooled, direct, range, rootFirst];
    }
    public static IEnumerable<object[]> ComponentCounts()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        foreach (int count in new[] { 1, 2 }) yield return [pooled, direct, count];
    }
    public static IEnumerable<object[]> Removals()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true })
        foreach (int shape in new[] { 0, 1, 2 }) yield return [pooled, direct, shape];
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void DecomposeMatchesTheOriginalWindowWithoutAcquiringReferences(bool pooled, bool direct, int range, bool rootFirst)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf root = s.Allocator.Buffer(1024, 1024);
        CompositeByteBuf composite = s.Allocator.CompositeBuffer();
        var components = new List<ByteBuf>();
        IReadOnlyList<ByteBuf> parts = Array.Empty<ByteBuf>();
        try
        {
            byte[] bytes = new byte[1024];
            for (int i = 0; i < bytes.Length; ++i) bytes[i] = unchecked((byte)(i * 37 + 11));
            root.WriteBytes(bytes);
            foreach ((int offset, int length) in new[] { (100, 200), (300, 400), (700, 100) })
            {
                ByteBuf component = root.RetainedSlice(offset, length);
                components.Add(component); composite.AddComponent(component, true);
            }
            (int start, int count, int expectedParts) = range switch
            {
                0 => (150, 500, 3), 1 => (310, 50, 1), _ => (310, 0, 0)
            };
            parts = composite.Decompose(start, count);
            Assert.Equal(expectedParts, parts.Count);
            Assert.Equal(4, root.ReferenceCount);
            Assert.All(components, component => Assert.Equal(pooled ? 1 : 4, component.ReferenceCount));
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
                Assert.Equal(1, root.ReferenceCount);
                if (pooled) Assert.All(parts, part => Assert.Equal(0, part.ReferenceCount));
                else Assert.All(parts, part => Assert.Equal(1, part.ReferenceCount));
                Assert.True(root.Release());
            }
            Assert.Equal(0, root.ReferenceCount);
            Assert.All(components, component => Assert.Equal(0, component.ReferenceCount));
            Assert.All(parts, part => Assert.Equal(0, part.ReferenceCount));
        }
        finally
        {
            if (composite.ReferenceCount > 0) composite.Release();
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
        }
    }

    [Theory]
    [MemberData(nameof(ComponentCounts))]
    public void DecomposeDelegatesReferenceOperationsToTheOriginalRetainedComponents(bool pooled, bool direct, int count)
    {
        using var s = new Storage(pooled, direct);
        ByteBuf root = s.Allocator.Buffer(1024, 1024).WriteZero(1024);
        CompositeByteBuf composite = s.Allocator.CompositeBuffer();
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
            Assert.Equal(pooled ? 1 + count : 1 + 2 * count, root.ReferenceCount);
            for (int i = 0; i < count; ++i)
            {
                Assert.Equal(components[i].ReferenceCount, parts[i].ReferenceCount);
                Assert.Equal(pooled ? 2 : 1 + 2 * count, parts[i].ReferenceCount);
            }
            // Consume the explicit Retain claim, not the composite's transferred claim.
            foreach (ByteBuf part in parts) part.Release();
            for (int i = 0; i < count; ++i)
                Assert.Equal(components[i].ReferenceCount, parts[i].ReferenceCount);
            Assert.All(components, component => Assert.Equal(pooled ? 1 : 1 + count, component.ReferenceCount));
            Assert.True(composite.Release());
            Assert.Equal(1, root.ReferenceCount);
            if (pooled)
                foreach (ByteBuf part in parts)
                    Assert.Throws<IllegalReferenceCountException>(() => part.GetByte(0));
            else Assert.All(parts, part => Assert.Equal(0, part.GetByte(0)));
            Assert.True(root.Release());
            Assert.All(parts, part => Assert.Equal(0, part.ReferenceCount));
            Assert.All(components, component => Assert.Equal(0, component.ReferenceCount));
        }
        finally
        {
            if (composite.ReferenceCount > 0) composite.Release();
            foreach (ByteBuf component in components)
                if (pooled && component.ReferenceCount > 0) component.Release(component.ReferenceCount);
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
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
                Assert.Equal(removed ? 0 : 1, sources[i].ReferenceCount);
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
        Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
    }
}
