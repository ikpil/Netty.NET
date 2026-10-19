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
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// CLR mapping of the concrete NoCleaner capacity specialization. Raw-address
// reallocation becomes allocate/copy/publish so existing CLR pins remain valid.
public class DirectByteBufReallocationTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native;
        public readonly UnpooledByteBufAllocator Allocator;
        public readonly ByteBuf Buffer;
        public readonly bool Factory;

        public Storage(bool factory, int initial = 8, long budget = 256)
        {
            Factory = factory;
            Native = new NativeMemoryAllocator(budget);
            Allocator = new UnpooledByteBufAllocator(true, Native);
            Buffer = factory ? Allocator.DirectBuffer(initial, 64)
                : new UnpooledDirectByteBuf(Allocator, initial, 64, Native);
            Buffer.SetBytes(0, Prefix(initial)).SetIndex(initial / 2, initial)
                .MarkReaderIndex().MarkWriterIndex();
            Assert.Same(Allocator, Buffer.Allocator);
            Assert.True(Buffer.IsDirect);
            Assert.True(Buffer.IsContiguous);
        }

        public void Accounting(int capacity, long reserved)
        {
            Assert.Equal(reserved, Native.ReservedBytes);
            Assert.Equal(Factory ? capacity : 0, Allocator.Metric.UsedDirectMemory);
        }

        public void Dispose()
        {
            if (Buffer.ReferenceCount != 0) Assert.True(Buffer.Release());
            Assert.Equal(0, Native.ReservedBytes);
            Assert.Equal(0, Allocator.Metric.UsedDirectMemory);
        }
    }

    private static byte[] Prefix(int length) => Enumerable.Range(1, length).Select(i => (byte)i).ToArray();

    public static IEnumerable<object[]> CapacityCases()
    {
        foreach (bool factory in new[] { false, true })
        foreach (var (initial, target) in new[] { (0, 0), (0, 8), (8, 0), (8, 8), (8, 16), (16, 8), (16, 4), (16, 32) })
            yield return new object[] { factory, initial, target };
    }

    [Theory]
    [MemberData(nameof(CapacityCases))]
    public void CapacityPreservesPrefixTrimsIndicesAndRetainsMarks(bool factory, int initial, int target)
    {
        using var s = new Storage(factory, initial);
        ByteBuf b = s.Buffer;
        Memory<byte> old = b.AsMemory(0, initial);
        Assert.True(NativeMemoryOwner.TryGetOwner(old, out var previous));
        b.Capacity = target;
        Assert.Equal(target, b.Capacity);
        Assert.Equal(64, b.MaxCapacity);
        Assert.Equal(1, b.ReferenceCount);
        Assert.Same(s.Allocator, b.Allocator);
        Assert.Equal(Prefix(Math.Min(initial, target)), b.AsMemory(0, Math.Min(initial, target)).ToArray());
        Assert.Equal(Math.Min(initial / 2, target), b.ReaderIndex);
        Assert.Equal(Math.Min(initial, target), b.WriterIndex);
        s.Accounting(target, Math.Max(1, target));
        Assert.True(NativeMemoryOwner.TryGetOwner(b.AsMemory(0, target), out var replacement));
        if (initial == target)
        {
            Assert.Same(previous, replacement);
            Assert.Equal(Prefix(initial), old.ToArray());
        }
        else
        {
            Assert.NotSame(previous, replacement);
            Assert.Throws<ObjectDisposedException>(() => old.ToArray());
        }
        b.Capacity = 64;
        b.SetIndex(0, 64).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(initial / 2, b.ReaderIndex);
        Assert.Equal(initial, b.WriterIndex);
        s.Accounting(64, 64);
    }

    [Theory]
    [InlineData(false, 4, 8, false)]
    [InlineData(true, 4, 8, false)]
    [InlineData(false, 16, 16, false)]
    [InlineData(true, 16, 16, false)]
    [InlineData(false, 16, 24, true)]
    [InlineData(true, 16, 24, true)]
    public void ReplacementNeedsPeakBudgetAndFailureLeavesStateIntact(bool factory, int target, long budget, bool succeeds)
    {
        using var s = new Storage(factory, budget: budget);
        ByteBuf b = s.Buffer;
        b.ReaderIndex = 3;
        b.MarkReaderIndex();
        Memory<byte> old = b.AsMemory(0, 8);
        if (succeeds)
        {
            b.Capacity = target;
            Assert.Throws<ObjectDisposedException>(() => old.ToArray());
        }
        else
        {
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = target);
            Assert.Equal(Prefix(8), old.ToArray());
        }
        int capacity = succeeds ? target : 8;
        Assert.Equal(capacity, b.Capacity);
        Assert.Equal(Prefix(8), b.AsMemory(0, 8).ToArray());
        Assert.Equal(3, b.ReaderIndex);
        Assert.Equal(8, b.WriterIndex);
        b.SetIndex(0, capacity).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(3, b.ReaderIndex);
        Assert.Equal(8, b.WriterIndex);
        s.Accounting(capacity, capacity);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    public void InvalidCapacityLeavesOwnerDataAndMarksIntact(bool factory, int target)
    {
        using var s = new Storage(factory);
        ByteBuf b = s.Buffer;
        Memory<byte> old = b.AsMemory(0, 8);
        Assert.True(NativeMemoryOwner.TryGetOwner(old, out var previous));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Capacity = target);
        Assert.Equal(8, b.Capacity);
        Assert.Equal(4, b.ReaderIndex);
        Assert.Equal(8, b.WriterIndex);
        Assert.Equal(Prefix(8), old.ToArray());
        Assert.True(NativeMemoryOwner.TryGetOwner(b.AsMemory(0, 8), out var current));
        Assert.Same(previous, current);
        b.SetIndex(0, 8).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(4, b.ReaderIndex);
        Assert.Equal(8, b.WriterIndex);
        s.Accounting(8, 8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedSameCapacityRequestStillChecksAccessibility(bool factory)
    {
        using var s = new Storage(factory);
        Assert.True(s.Buffer.Release());
        Assert.Throws<IllegalReferenceCountException>(() => s.Buffer.Capacity = 8);
        s.Accounting(0, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void OldPinKeepsItsAllocationThroughReplacementAndFinalRelease(bool factory)
    {
        using var s = new Storage(factory, budget: 32);
        ByteBuf b = s.Buffer;
        Memory<byte> old = b.AsMemory(0, 8);
        MemoryHandle pin = old.Pin();
        try
        {
            b.Capacity = 16;
            Assert.Equal(Prefix(8), new ReadOnlySpan<byte>(pin.Pointer, 8).ToArray());
            Assert.Equal(Prefix(8), b.AsMemory(0, 8).ToArray());
            b.SetByte(0, 99);
            Assert.Equal(1, *(byte*)pin.Pointer);
            s.Accounting(16, 24);
            Assert.True(b.Release());
            Assert.Equal(0, b.ReferenceCount);
            Assert.Throws<ObjectDisposedException>(() => old.ToArray());
            Assert.Equal(Prefix(8), new ReadOnlySpan<byte>(pin.Pointer, 8).ToArray());
            s.Accounting(0, 8);
        }
        finally { pin.Dispose(); }
        s.Accounting(0, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void BorrowedNativeMemoryDetachesWithoutDisposingExternalOwner(int offset)
    {
        var external = new NativeMemoryAllocator(24);
        var replacement = new NativeMemoryAllocator(32);
        var creator = new UnpooledByteBufAllocator(true, replacement);
        using (NativeMemoryOwner owner = external.Allocate(24))
        {
            Prefix(24).CopyTo(owner.Memory.Span);
            Memory<byte> borrowed = owner.Memory.Slice(offset, 8);
            ByteBuf b = new UnpooledDirectByteBuf(creator, borrowed, 64, replacement);
            try
            {
                b.Capacity = 16;
                Assert.Same(creator, b.Allocator);
                Assert.Equal(Prefix(24).Skip(offset).Take(8), b.AsMemory(0, 8).ToArray());
                Assert.Equal(0, b.ReaderIndex);
                Assert.Equal(8, b.WriterIndex);
                b.SetByte(0, 99);
                Assert.Equal(Prefix(24).Skip(offset).Take(8), borrowed.ToArray());
                Assert.Equal(24, external.ReservedBytes);
                Assert.Equal(16, replacement.ReservedBytes);
                Assert.Equal(0, creator.Metric.UsedDirectMemory);
            }
            finally { b.Release(); }
            Assert.Equal(0, replacement.ReservedBytes);
            Assert.Equal(24, external.ReservedBytes);
            Assert.Equal(Prefix(24), owner.Memory.ToArray());
        }
        Assert.Equal(0, external.ReservedBytes);
    }
}
