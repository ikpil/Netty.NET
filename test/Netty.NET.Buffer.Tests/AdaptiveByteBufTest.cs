/*
 * Copyright 2024 The Netty Project
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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Microsoft.Win32.SafeHandles;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveByteBufTest
{
    public static IEnumerable<object[]> Configurations()
    {
        foreach (bool direct in new[] { false, true })
            foreach (bool lowMemory in new[] { false, true }) yield return [direct, lowMemory];
    }
    private static AdaptiveByteBuf Buffer(AdaptiveBufferTestAllocator allocator, bool direct, int capacity, int max)
        => (AdaptiveByteBuf)(direct ? allocator.DirectBuffer(capacity, max) : allocator.HeapBuffer(capacity, max));
    private static byte[] Pattern(int count) => Enumerable.Range(0, count).Select(i => (byte)(i % 251)).ToArray();

    [Theory]
    [MemberData(nameof(Configurations))]
    public void LogicalResizePreservesReservationHiddenBytesIndicesAndMarks(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        ByteBuf guard = Buffer(allocator, direct, 7, 1000);
        var buffer = Buffer(allocator, direct, 7, 1000);
        try
        {
            AdaptiveChunkAllocation original = buffer.Allocation;
            Assert.NotEqual(0, original.Offset); Assert.Equal(32, original.Length);
            buffer.SetBytes(0, Pattern(7)); buffer.SetIndex(2, 5).MarkReaderIndex().MarkWriterIndex();
            buffer.SetIndex(3, 6); buffer.Capacity = 32;
            Assert.Same(original, buffer.Allocation); Assert.Equal((3, 6), (buffer.ReaderIndex, buffer.WriterIndex));
            buffer.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((2, 5), (buffer.ReaderIndex, buffer.WriterIndex));
            buffer.SetBytes(0, Pattern(32)); buffer.Capacity = 0;
            Assert.Same(original, buffer.Allocation); Assert.Equal((0, 0), (buffer.ReaderIndex, buffer.WriterIndex));
            buffer.Capacity = 32; Assert.Equal(Pattern(32), buffer.AsSpan(0, 32).ToArray());
            buffer.SetIndex(3, 7).MarkReaderIndex().MarkWriterIndex(); buffer.SetIndex(4, 9);
            ByteBuf view = buffer.Slice(2, 5), duplicate = buffer.Duplicate();
            buffer.Capacity = 33;
            Assert.NotSame(original, buffer.Allocation); Assert.Equal(64, buffer.Allocation.Length);
            Assert.Equal(Pattern(32), buffer.AsSpan(0, 32).ToArray());
            Assert.Equal((4, 9), (buffer.ReaderIndex, buffer.WriterIndex));
            Assert.Equal(Pattern(7)[2..], view.AsSpan(0, 5).ToArray()); Assert.Equal(33, duplicate.Capacity);
            buffer.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((3, 7), (buffer.ReaderIndex, buffer.WriterIndex));
            buffer.Capacity = 5; Assert.Equal((3, 5), (buffer.ReaderIndex, buffer.WriterIndex));
        }
        finally { buffer.Release(); guard.Release(); }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory);
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void FastWritableHintClampsPublicMaximumAndEnsureWritableUsesReservation(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        foreach (int requested in new[] { 0, 7, 512, 16897, 32769, 1048577 })
        {
            int maximum = requested + 3;
            var buffer = Buffer(allocator, direct, requested, maximum);
            try
            {
                int fast = new AdaptiveAllocationPolicy(lowMemory).ComputeBufferCapacity(requested, maximum);
                Assert.Equal(fast, buffer.Allocation.Length);
                Assert.Equal(Math.Min(fast, maximum), buffer.MaxFastWritableBytes);
                buffer.WriterIndex = requested; AdaptiveChunkAllocation before = buffer.Allocation;
                buffer.EnsureWritable(3);
                Assert.Equal(maximum, buffer.Capacity); Assert.Equal(3, buffer.WritableBytes);
                Assert.Equal(requested, buffer.WriterIndex);
                if (fast >= maximum) Assert.Same(before, buffer.Allocation);
                else Assert.NotSame(before, buffer.Allocation);
                Assert.Equal(1, buffer.EnsureWritable(4, false));
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(4));
            }
            finally { buffer.Release(); }
        }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void SliceOrDuplicateUnwrapLetNotEscapeRootParent(bool slice, bool wrapped)
    {
        using var allocator = new AdaptiveBufferTestAllocator();
        AdaptiveByteBuf root = Buffer(allocator, false, 8, 1000);
        ByteBuf buffer = wrapped ? new SimpleLeakAwareByteBuf(root, ByteBuf.LeakDetector.TrackForcibly(root)) : root;
        try
        {
            // Unwrap if this is wrapped by a leak aware buffer.
            Assert.Null(wrapped ? buffer.Unwrap().Unwrap() : buffer.Unwrap());
            ByteBuf derived = slice ? buffer.Slice(0, 4) : buffer.Duplicate();
            // When we unwrap the derived buffer we should get our original buffer of type AdaptiveByteBuf back.
            ByteBuf unwrapped = derived is SimpleLeakAwareByteBuf ? derived.Unwrap().Unwrap() : derived.Unwrap();
            Assert.IsType<AdaptiveByteBuf>(unwrapped); Assert.Same(root, unwrapped);
            ByteBuf retained = slice ? buffer.RetainedSlice(0, 4) : buffer.RetainedDuplicate();
            // When we unwrap the derived buffer we should get our original buffer of type AdaptiveByteBuf back.
            ByteBuf unwrappedRetained = retained is SimpleLeakAwareByteBuf ? retained.Unwrap().Unwrap() : retained.Unwrap();
            Assert.IsType<AdaptiveByteBuf>(unwrappedRetained); Assert.Same(root, unwrappedRetained);
            Assert.Equal(2, buffer.ReferenceCount); Assert.False(retained.Release());
        }
        finally { Assert.True(buffer.Release()); }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void RetainedViewsShareTheAdaptiveRootCounterAndFollowReallocation(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        var root = Buffer(allocator, direct, 7, 1000); root.SetBytes(0, Pattern(7));
        ByteBuf slice = root.RetainedSlice(2, 5), duplicate = slice.RetainedDuplicate();
        Assert.Equal(3, root.ReferenceCount); Assert.False(root.Release());
        Assert.Equal(2, slice.ReferenceCount); Assert.Same(root, slice.Unwrap());
        var nested = duplicate.Slice(1, 3); root.Capacity = 100;
        Assert.Equal(new byte[] { 3, 4, 5 }, nested.AsSpan(0, 3).ToArray());
        Assert.False(duplicate.Release()); Assert.True(slice.Release());
        Assert.Equal(0, nested.ReferenceCount); Assert.Null(root.Allocation);
        Assert.Throws<IllegalReferenceCountException>(() => nested.GetByte(0));
        Assert.Throws<IllegalReferenceCountException>(() => root.Allocator);
        Assert.Throws<IllegalReferenceCountException>(() => root.Capacity = 8);
        ByteBuf fresh = Buffer(allocator, direct, 7, 1000);
        Assert.NotSame(root, fresh); Assert.Equal(0, root.ReferenceCount); fresh.Release();
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CopiesPreserveChunkDelegateMaximumStorageKindAndIndependentOwnership(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        foreach (int requested in new[] { 7, 16897, 1048577 })
        {
            int max = requested + 19;
            var buffer = Buffer(allocator, direct, requested, max);
            buffer.SetBytes(0, Pattern(7)); buffer.SetIndex(2, 6);
            ByteBuf copy = buffer.Copy(1, 5);
            try
            {
                int expectedMax = new AdaptiveAllocationPolicy(lowMemory).IsPooled(requested)
                    ? new AdaptiveAllocationPolicy(lowMemory).ComputeChunkSize(requested) : max;
                Assert.Equal(expectedMax, copy.MaxCapacity); Assert.Equal(direct, copy.IsDirect);
                Assert.Same(allocator, copy.Allocator); Assert.Equal((0, 5), (copy.ReaderIndex, copy.WriterIndex));
                Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, copy.AsSpan(0, 5).ToArray());
                copy.SetByte(0, 99); Assert.Equal(1, buffer.GetByte(1));
                Assert.Equal((2, 6), (buffer.ReaderIndex, buffer.WriterIndex));
                buffer.Release(); buffer = null; Assert.Equal(99, copy.GetByte(0));
            }
            finally { buffer?.Release(); copy.Release(); }
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CurrentKernelUsesTranslatedRegionForPrimitiveBulkTextSequenceAndOverlap(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        ByteBuf guard = Buffer(allocator, direct, 64, 1000); guard.SetZero(0, 64);
        var b = Buffer(allocator, direct, 64, 1000);
        try
        {
            Assert.Equal(64, b.Allocation.Offset);
            b.SetShort(0, 0x1234).SetMediumLE(2, 0x123456).SetInt(5, 0x12345678).SetLongLE(9, 0x0123456789ABCDEFL);
            Assert.Equal(0x1234, b.GetShort(0)); Assert.Equal(0x123456, b.GetUnsignedMediumLE(2));
            Assert.Equal(0x12345678, b.GetInt(5)); Assert.Equal(0x0123456789ABCDEFL, b.GetLongLE(9));
            Assert.Equal(Convert.FromHexString("123456341212345678EFCDAB8967452301"), b.AsSpan(0, 17).ToArray());
            b.SetBytes(0, Pattern(32)); b.SetBytes(3, b, 0, 20);
            byte[] expected = Pattern(32); expected.AsSpan(0, 20).CopyTo(expected.AsSpan(3));
            Assert.Equal(expected, b.AsReadOnlySequence(0, 32).ToArray());
            Assert.Equal(!direct, MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)b.AsMemory(0, 32), out var array));
            if (!direct) Assert.Equal(64, array.Offset);
            b.Clear(); string text = "A\uD55C\U0001F600"; int written = ByteBufUtil.WriteUtf8(b, text);
            Assert.Equal(Encoding.UTF8.GetBytes(text), b.AsSpan(0, written).ToArray());
            Assert.Equal(text, b.GetString(0, written, Encoding.UTF8));
            Assert.Equal(new byte[64], guard.AsSpan(0, 64).ToArray());
        }
        finally { b.Release(); guard.Release(); }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void BoundsUseLogicalCapacityRatherThanTheLargerReservation(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        var b = Buffer(allocator, direct, 7, 100); b.SetBytes(0, Pattern(7));
        try
        {
            Assert.Equal(32, b.Allocation.Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.AsMemory(4, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.AsReadOnlySequence(4, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Copy(4, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetZero(4, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetInt(4));
            // capacity 7，4+8=12 <= maxFastCapacity 32
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetBytes(4, (SafeFileHandle)null, 0L, 8));
            using var stream = new MemoryStream(new byte[32]);
            // capacity 7，4+8=12 <= maxFastCapacity 32
            // CLR: Stream supplies the portable ordinary-channel transfer here.
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetBytes(4, stream, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetBytes(4, stream, 8));
            Assert.Equal(Pattern(7), b.AsSpan(0, 7).ToArray()); Assert.Equal((0, 0), (b.ReaderIndex, b.WriterIndex));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StreamsAndPositionedFilesUseRegionCoordinatesWithoutChangingCursors(bool direct)
    {
        using var allocator = new AdaptiveBufferTestAllocator();
        ByteBuf guard = Buffer(allocator, direct, 64, 1000);
        var b = Buffer(allocator, direct, 64, 1000); b.SetBytes(0, Pattern(64)); b.SetIndex(3, 61);
        string path = Path.GetTempFileName();
        try
        {
            using var stream = new MemoryStream();
            await b.GetBytesAsync(5, stream, 19, TestContext.Current.CancellationToken); Assert.Equal(Pattern(64)[5..24], stream.ToArray());
            b.SetZero(5, 19); stream.Position = 0;
            Assert.Equal(19, await b.SetBytesAsync(5, stream, 19, TestContext.Current.CancellationToken)); Assert.Equal(Pattern(64), b.AsSpan(0, 64).ToArray());
            using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(19, b.GetBytes(5, file, 3, 19)); b.SetZero(5, 19);
            Assert.Equal(19, b.SetBytes(5, file, 3, 19));
            Assert.Equal(19, await b.GetBytesAsync(5, file, 29, 19, TestContext.Current.CancellationToken)); b.SetZero(5, 19);
            Assert.Equal(19, await b.SetBytesAsync(5, file, 29, 19, TestContext.Current.CancellationToken));
            Assert.Equal(Pattern(64), b.AsSpan(0, 64).ToArray()); Assert.Equal((3, 61), (b.ReaderIndex, b.WriterIndex));
        }
        finally { b.Release(); guard.Release(); File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void AliasedGrowingWritesAndPinsKeepOldRegionsAliveUntilTheirClaimsReturn(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        var b = Buffer(allocator, direct, 32, 1000); b.WriteBytes(Pattern(32));
        AdaptiveChunkAllocation original = b.Allocation; Memory<byte> old = b.AsMemory(0, 32);
        var pin = old.Pin(); var copied = pin; var lease = b.AcquireReadLease();
        var held = new List<ByteBuf>();
        try
        {
            b.WriteBytes(old.Span); Assert.Equal(Pattern(32).Concat(Pattern(32)), b.AsSpan(0, 64).ToArray());
            Assert.NotSame(original, b.Allocation); Assert.Equal(0, Marshal.ReadByte(Pointer(pin)));
            Assert.Throws<ObjectDisposedException>(() => old.Span.Clear());
            // Exhaust the other FIFO slots before asking for reuse of the returned slot.
            for (int i = 1; i < AdaptiveAllocationPolicy.MinChunkSize / 32; i++)
            {
                var first = Buffer(allocator, direct, 32, 1000); held.Add(first);
                Assert.NotEqual(original.Offset, first.Allocation.Offset);
            }
            pin.Dispose(); copied.Dispose(); lease.Dispose();
            var next = Buffer(allocator, direct, 32, 1000); Assert.Equal(original.Offset, next.Allocation.Offset);
            next.Release();
        }
        finally { pin.Dispose(); copied.Dispose(); lease.Dispose(); b.Release(); foreach (var item in held) item.Release(); }
    }

    private static unsafe nint Pointer(MemoryHandle pin) => (nint)pin.Pointer;

    [Fact]
    public void NativeAllocationFailurePreservesDataCapacityIndicesMarksAndOldReservation()
    {
        var native = new NativeMemoryAllocator(131072); using var allocator = new AdaptiveBufferTestAllocator(native: native);
        var b = Buffer(allocator, true, 7, 1000); b.SetBytes(0, Pattern(7)); b.SetIndex(2, 5).MarkReaderIndex().MarkWriterIndex();
        b.SetIndex(3, 6); AdaptiveChunkAllocation original = b.Allocation;
        try
        {
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = 33);
            Assert.Equal(7, b.Capacity); Assert.Same(original, b.Allocation);
            Assert.Equal(Pattern(7), b.AsSpan(0, 7).ToArray()); Assert.Equal((3, 6), (b.ReaderIndex, b.WriterIndex));
            b.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((2, 5), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(131072, allocator.UsedDirectMemory); Assert.Equal(131072, native.ReservedBytes);
        }
        finally { b.Release(); }
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void AllocatorRetirementLeavesViewsPinsAndLiveDataOwnedUntilFinalReturn(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        var root = Buffer(allocator, direct, 7, 1000); root.SetBytes(0, Pattern(7));
        ByteBuf child = root.RetainedSlice(1, 5); var pin = child.AsMemory(0, 5).Pin();
        root.Release(); allocator.Dispose(); Assert.Equal(1, child.ReferenceCount);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, child.AsSpan(0, 5).ToArray());
        root.Capacity = 32; Assert.Throws<ObjectDisposedException>(() => root.Capacity = 33);
        child.Release(); Assert.Equal(131072, allocator.UsedHeapMemory + allocator.UsedDirectMemory);
        Assert.Equal(1, Marshal.ReadByte(Pointer(pin))); pin.Dispose();
        Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Fact]
    public void InvalidOrUndersizedStorageBindingsRollBackTheirReservation()
    {
        Assert.Throws<ArgumentNullException>(() => new AdaptiveByteBuf(7, 7, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveByteBuf(8, 7, (_, _) => throw new Exception()));
        Assert.Throws<InvalidOperationException>(() => new AdaptiveByteBuf(7, 7, (_, _) => null));
        var registry = new AdaptiveChunkRegistry(); var raw = new UnpooledHeapByteBuf(3, 3);
        using var chunk = new AdaptiveChunk(raw, registry);
        Assert.Throws<InvalidOperationException>(() => new AdaptiveByteBuf(7, 7, (_, _) => chunk.TryAllocate(3)));
        chunk.Dispose(); Assert.Equal(0, raw.ReferenceCount); Assert.Equal(0, registry.TotalCapacity);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void ReallocationAcrossFixedBuddyAndDedicatedRegionsCopiesTheEntireLogicalPrefix(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory);
        var b = Buffer(allocator, direct, 7, 2097152); b.SetBytes(0, Pattern(7)); b.SetIndex(2, 5);
        int previousLength = 7;
        try
        {
            foreach (int capacity in new[] { 33, 641, 1153, 4097, 16897, 32769, 1048577 })
            {
                b.Capacity = capacity;
                Assert.Equal(Pattern(previousLength), b.AsSpan(0, previousLength).ToArray());
                Assert.Equal((2, 5), (b.ReaderIndex, b.WriterIndex)); Assert.Equal(direct, b.IsDirect);
                b.SetBytes(0, Pattern(capacity)); previousLength = capacity;
            }
        }
        finally { b.Release(); }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory);
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void PhysicalOwnerLocalChunksSupplyTheSameAdaptiveBufferContracts(bool direct, bool lowMemory)
    {
        using var allocator = new AdaptiveBufferTestAllocator(lowMemory, threadLocal: true);
        ByteBuf guard = Buffer(allocator, direct, 513, 2000);
        var b = Buffer(allocator, direct, 513, 2000);
        try
        {
            // 640-byte segments leave a 512-byte tail, shifting local offsets.
            Assert.Equal(1152, b.Allocation.Offset); b.SetBytes(0, Pattern(513));
            b.Capacity = 640; b.Capacity = 641;
            Assert.Equal(Pattern(513), b.AsSpan(0, 513).ToArray());
            var retained = b.RetainedSlice(1, 5); b.Release(); b = null;
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, retained.AsSpan(0, 5).ToArray()); retained.Release();
        }
        finally { b?.Release(); guard.Release(); }
    }

    [Fact]
    public void CopyFailureBeforePublicationReleasesReplacementAndPreservesOldState()
    {
        using var allocator = new AdaptiveBufferTestAllocator(); FaultingMemory manager = null;
        allocator.StorageFactory = (_, capacity, max) => capacity > 1048576
            ? new FaultingStorage(allocator, manager = new FaultingMemory(capacity), max)
            : new UnpooledHeapByteBuf(allocator, capacity, max);
        allocator.AfterReservation = (size, _) => { if (size > 1048576) manager.Fail = true; };
        var b = Buffer(allocator, false, 7, 2097152); b.SetBytes(0, Pattern(7)); b.SetIndex(2, 5).MarkReaderIndex().MarkWriterIndex();
        AdaptiveChunkAllocation old = b.Allocation;
        try
        {
            Assert.Throws<InvalidOperationException>(() => b.Capacity = 1048577);
            Assert.True(manager.Disposed); Assert.Same(old, b.Allocation); Assert.Equal(7, b.Capacity);
            Assert.Equal(Pattern(7), b.AsSpan(0, 7).ToArray()); Assert.Equal((2, 5), (b.ReaderIndex, b.WriterIndex));
            b.SetIndex(3, 6).ResetWriterIndex().ResetReaderIndex(); Assert.Equal((2, 5), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(131072, allocator.UsedHeapMemory);
            allocator.AfterReservation = null; b.Capacity = 1048577;
            Assert.Equal(Pattern(7), b.AsSpan(0, 7).ToArray()); Assert.Equal((2, 5), (b.ReaderIndex, b.WriterIndex));
        }
        finally { b.Release(); }
        Assert.True(manager.Disposed); allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory);
    }

    private sealed class FaultingMemory(int length) : MemoryManager<byte>
    {
        private readonly byte[] _array = new byte[length];
        internal bool Fail;
        internal bool Disposed;
        public override Span<byte> GetSpan() => Fail ? throw new InvalidOperationException("injected copy failure") : _array;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() => throw new NotSupportedException();
        protected override void Dispose(bool disposing) => Disposed = true;
    }

    private sealed class FaultingStorage(IByteBufAllocator allocator, FaultingMemory memory, int max) : AbstractReferenceCountedByteBuf(max)
    {
        public override IByteBufAllocator Allocator => allocator;
        public override int Capacity { get => memory.Memory.Length; set => throw new NotSupportedException(); }
        public override bool IsDirect => false;
        protected override Memory<byte> GetMemoryCore(int index, int length) => memory.Memory.Slice(index, length);
        protected override void Deallocate() => ((IDisposable)memory).Dispose();
    }
}
