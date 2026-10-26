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

public class UnpooledNativeResizeOwnershipTest
{
    private sealed class Wrapper(ByteBuf b) : WrappedByteBuf(b) { }
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes;
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf value) { Assert.Same(expected, value); ++Closes; return true; }
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool borrowed in new[] { false, true })
        foreach (bool pinned in new[] { false, true })
        for (int facade = 0; facade < 5; ++facade)
        foreach (int capacity in new[] { 2, 8, 12 }) yield return [borrowed, pinned, facade, capacity];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public unsafe void FailedShrinkPreservesStateAndRetryDetachesOrFreesOnlyOwnedStorage(bool borrowed, bool pinned, int facade, int capacity)
    {
        var native = new NativeMemoryAllocator(32); var externalDomain = new NativeMemoryAllocator(24);
        var allocator = new UnpooledByteBufAllocator(true, disableLeakDetector: true, native);
        using NativeMemoryOwner external = externalDomain.Allocate(24);
        external.Memory.Span.Fill(0xee);
        ByteBuf root = borrowed ? new UnpooledDirectByteBuf(allocator, external.Memory.Slice(4, 16), 32, native)
            : allocator.DirectBuffer(16, 32);
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        root.SetBytes(0, bytes.AsSpan()).SetIndex(1, 6).MarkReaderIndex().MarkWriterIndex().SetIndex(3, 14);
        var tracker = new Tracker(root);
        ByteBuf b = facade switch
        {
            0 => root, 1 => new Wrapper(root), 2 => new SimpleLeakAwareByteBuf(root, tracker),
            3 => new AdvancedLeakAwareByteBuf(root, tracker), _ => root.Duplicate()
        };
        if (facade == 4) b.SetIndex(1, 6).MarkReaderIndex().MarkWriterIndex().SetIndex(4, 12);
        ReadOnlyMemory<byte> old = root.AsReadOnlyMemory(0, 16);
        MemoryHandle pin = pinned ? old.Pin() : default;
        NativeMemoryOwner pressure = native.Allocate(borrowed ? 31 : 15);
        try
        {
            Assert.Equal(31, native.ReservedBytes);
            b.Capacity = 16; // A no-op does not need a second reservation.
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = capacity);
            Assert.Equal(16, root.Capacity); Assert.Equal(bytes, ByteBufUtil.GetBytes(root, 0, 16));
            Assert.Equal((3, 14, 1), (root.ReaderIndex, root.WriterIndex, root.ReferenceCount));
            Assert.Equal(facade == 4 ? (4, 12) : (3, 14), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(borrowed ? 0 : 16, allocator.Metric.UsedDirectMemory);
            Assert.Equal(31, native.ReservedBytes);
            b.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((1, 6), (b.ReaderIndex, b.WriterIndex));
            root.SetIndex(3, 14); if (facade == 4) b.SetIndex(4, 12);
            pressure.Dispose();
            b.Capacity = capacity;
            Assert.Equal(capacity, root.Capacity); Assert.Equal(bytes.AsSpan(0, capacity).ToArray(), ByteBufUtil.GetBytes(root, 0, capacity));
            Assert.Equal((Math.Min(3, capacity), capacity, 1), (root.ReaderIndex, root.WriterIndex, root.ReferenceCount));
            // Duplicate capacity follows its parent, but shrinking the parent does
            // not repair that duplicate's independent cursors (the original rule).
            Assert.Equal(facade == 4 ? (4, 12) : (Math.Min(3, capacity), capacity), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(borrowed ? 0 : capacity, allocator.Metric.UsedDirectMemory);
            Assert.Equal(capacity + (!borrowed && pinned ? 16 : 0), native.ReservedBytes);
            if (borrowed)
            {
                Assert.Equal(bytes, old.ToArray());
                Assert.Equal(0xee, external.Memory.Span[3]); Assert.Equal(0xee, external.Memory.Span[20]);
                root.SetByte(0, 91); Assert.Equal(1, external.Memory.Span[4]);
            }
            else Assert.Throws<ObjectDisposedException>(() => old.Span.ToArray());
            if (pinned) Assert.Equal(bytes, new ReadOnlySpan<byte>(pin.Pointer, 16).ToArray());
            if (capacity < 6)
            {
                // Shrink preserves marks; a writer mark outside the new capacity
                // remains invalid, while the old reader mark can still be restored.
                Assert.Throws<ArgumentOutOfRangeException>(() => b.ResetWriterIndex());
                if (facade == 4)
                {
                    // CLR validates the entire index pair. Repair the stale
                    // duplicate atomically before restoring its reader mark.
                    Assert.Throws<ArgumentOutOfRangeException>(() => b.ResetReaderIndex());
                    b.SetIndex(0, capacity);
                }
                b.ResetReaderIndex(); Assert.Equal(1, b.ReaderIndex);
                if (facade == 4) { Assert.Throws<ArgumentOutOfRangeException>(() => root.ResetWriterIndex()); root.ResetReaderIndex(); }
                Assert.Equal((1, capacity), (root.ReaderIndex, root.WriterIndex));
            }
            else
            {
                b.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((1, 6), (b.ReaderIndex, b.WriterIndex));
                if (facade == 4) root.ResetWriterIndex().ResetReaderIndex();
                Assert.Equal((1, 6), (root.ReaderIndex, root.WriterIndex));
            }
            Assert.True(b.Release()); Assert.Equal(facade is 2 or 3 ? 1 : 0, tracker.Closes);
            Assert.Equal(0, allocator.Metric.UsedDirectMemory);
            Assert.Equal(!borrowed && pinned ? 16 : 0, native.ReservedBytes);
            Assert.Equal(24, externalDomain.ReservedBytes);
            external.Dispose();
            Assert.Equal(borrowed && pinned ? 24 : 0, externalDomain.ReservedBytes);
        }
        finally
        {
            pressure.Dispose();
            if (root.ReferenceCount > 0) b.Release();
            external.Dispose(); pin.Dispose();
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, externalDomain.ReservedBytes);
        }
    }
}
