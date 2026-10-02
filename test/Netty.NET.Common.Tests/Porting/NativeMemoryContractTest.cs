using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class NativeMemoryContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(4097)]
    public void OwnershipAndReservationAreExplicitAndDisposeIsIdempotent(int length)
    {
        var allocator = new NativeMemoryAllocator();
        var owner = allocator.Allocate(length, clear: true);
        Memory<byte> memory = owner.Memory;
        Assert.Equal(length, memory.Length);
        Assert.Equal(Math.Max(1, length), allocator.ReservedBytes);
        bool hasArray = MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out var array);
        // MemoryMarshal canonically represents any empty memory as an empty array
        // segment; that does not change the owner's native pin address.
        Assert.Equal(length == 0, hasArray);
        if (hasArray) Assert.Equal(0, array.Count);
        Assert.True(memory.Span.IndexOfAnyExcept((byte)0) < 0);
        owner.Dispose();
        owner.Dispose();
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => _ = owner.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = memory.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => memory.Pin());
    }

    [Fact]
    public void SlicesShareStorageAndNativePinOffsetsStayBounded()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(32, clear: true);
        Memory<byte> slice = owner.Memory.Slice(5, 8);
        slice.Span.Fill(0xa5);
        Assert.Equal(0, owner.Memory.Span[4]);
        Assert.Equal(0xa5, owner.Memory.Span[5]);
        Assert.Equal(0, owner.Memory.Span[13]);
        using var rootPin = owner.Memory.Pin();
        using var slicePin = slice.Pin();
        Assert.Equal(Address(rootPin) + 5, Address(slicePin));
        using var endPin = owner.Memory.Slice(32, 0).Pin();
        Assert.Equal(Address(rootPin) + 32, Address(endPin));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Memory.Slice(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Memory.Slice(31, 2));
    }

    [Fact]
    public void CopiedPinHandlesReleaseTheirLeaseOnlyOnceAndKeepDisposedStorageAlive()
    {
        var allocator = new NativeMemoryAllocator();
        var owner = allocator.Allocate(16, clear: true);
        var first = owner.Memory.Pin();
        var copiedFirst = first;
        var second = owner.Memory.Slice(3).Pin();
        owner.Dispose();
        Assert.Equal(16, allocator.ReservedBytes);
        Marshal.WriteByte(Address(second), 0xc7);
        Assert.Equal(0xc7, Marshal.ReadByte(Address(first) + 3));
        first.Dispose();
        copiedFirst.Dispose();
        Assert.Equal(16, allocator.ReservedBytes);
        second.Dispose();
        second.Dispose();
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void AllocationFailuresAndInvalidArgumentsLeaveTheReservationUnchanged()
    {
        var allocator = new NativeMemoryAllocator(8);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeMemoryAllocator(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(-1));
        Assert.Throws<OutOfMemoryException>(() => allocator.Allocate(9));
        Assert.Equal(0, allocator.ReservedBytes);
        using var owner = allocator.Allocate(8);
        Assert.Throws<OutOfMemoryException>(() => allocator.Allocate(0));
        Assert.Equal(8, allocator.ReservedBytes);
        Assert.Throws<OutOfMemoryException>(() => new NativeMemoryAllocator(0).Allocate(0));
    }

    [Fact]
    public void ReallocationPreservesThePrefixAndInvalidatesOldViews()
    {
        var allocator = new NativeMemoryAllocator(32);
        var original = allocator.Allocate(8);
        byte[] expected = { 0xfe, 1, 2, 3, 4, 5, 6, 0x80 };
        expected.CopyTo(original.Memory);
        Memory<byte> oldView = original.Memory.Slice(2, 3);
        using var grown = allocator.Reallocate(original, 20);
        Assert.Equal(expected, grown.Memory.Span.Slice(0, 8).ToArray());
        Assert.Equal(20, allocator.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => _ = oldView.Span.Length);
        original.Dispose();
        Assert.Equal(20, allocator.ReservedBytes);
        using var shrunk = allocator.Reallocate(grown, 3);
        Assert.Equal(new byte[] { 0xfe, 1, 2 }, shrunk.Memory.ToArray());
        Assert.Equal(3, allocator.ReservedBytes);
        using var empty = allocator.Reallocate(shrunk, 0);
        Assert.Equal(0, empty.Memory.Length);
        Assert.Equal(1, allocator.ReservedBytes);
        using var pin = empty.Memory.Pin();
        Assert.NotEqual(0, Address(pin));
    }

    [Fact]
    public void FailedReallocationPreservesTheOriginalOwnerBytesAndQuota()
    {
        var allocator = new NativeMemoryAllocator(8);
        using var owner = allocator.Allocate(8);
        owner.Memory.Span.Fill(0x7b);
        Assert.Throws<OutOfMemoryException>(() => allocator.Reallocate(owner, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Reallocate(owner, -1));
        Assert.Throws<ArgumentException>(() => new NativeMemoryAllocator().Reallocate(owner, 1));
        Assert.Throws<ArgumentNullException>(() => allocator.Reallocate(null, 1));
        Assert.Equal(8, allocator.ReservedBytes);
        Assert.True(owner.Memory.Span.IndexOfAnyExcept((byte)0x7b) < 0);
        using var pin = owner.Memory.Pin();
        Assert.Throws<InvalidOperationException>(() => allocator.Reallocate(owner, 4));
        Assert.Equal(8, allocator.ReservedBytes);
    }

    [Fact]
    public void ReallocationBecomesAvailableAfterTheLastPinAndKeepsSameSizeOwnershipCoherent()
    {
        var allocator = new NativeMemoryAllocator();
        var owner = allocator.Allocate(8, clear: true);
        var pin = owner.Memory.Pin();
        Assert.Throws<InvalidOperationException>(() => allocator.Reallocate(owner, 8));
        pin.Dispose();
        Memory<byte> old = owner.Memory;
        using var replacement = allocator.Reallocate(owner, 8);
        Assert.Equal(8, allocator.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => _ = old.Span.Length);
        Assert.True(replacement.Memory.Span.IndexOfAnyExcept((byte)0) < 0);
        owner.Dispose();
        Assert.Equal(8, allocator.ReservedBytes);
    }

    [Fact]
    public void ReallocationAndDisposalRaceTransfersOrReleasesExactlyOneAllocation()
    {
        var allocator = new NativeMemoryAllocator();
        for (int i = 0; i < 128; i++)
        {
            var original = allocator.Allocate(8, clear: true);
            original.Memory.Span.Fill(0xa3);
            NativeMemoryOwner replacement = null;
            using var barrier = new Barrier(2);
            Parallel.Invoke(() =>
            {
                barrier.SignalAndWait();
                try { replacement = allocator.Reallocate(original, 16); }
                catch (ObjectDisposedException) { }
            }, () => { barrier.SignalAndWait(); original.Dispose(); });
            try
            {
                Assert.Equal(replacement == null ? 0 : 16, allocator.ReservedBytes);
                if (replacement != null)
                    Assert.True(replacement.Memory.Span.Slice(0, 8).IndexOfAnyExcept((byte)0xa3) < 0);
            }
            finally { replacement?.Dispose(); original.Dispose(); }
            Assert.Equal(0, allocator.ReservedBytes);
        }
    }

    [Fact]
    public void ConcurrentReallocationsHaveOnlyOneOwnershipTransfer()
    {
        var allocator = new NativeMemoryAllocator();
        for (int i = 0; i < 128; i++)
        {
            var original = allocator.Allocate(8, clear: true);
            NativeMemoryOwner first = null;
            NativeMemoryOwner second = null;
            using var barrier = new Barrier(2);
            Parallel.Invoke(() =>
            {
                barrier.SignalAndWait();
                try { first = allocator.Reallocate(original, 16); }
                catch (ObjectDisposedException) { }
            }, () =>
            {
                barrier.SignalAndWait();
                try { second = allocator.Reallocate(original, 32); }
                catch (ObjectDisposedException) { }
            });
            try
            {
                Assert.True((first != null) != (second != null));
                Assert.Equal(first != null ? 16 : 32, allocator.ReservedBytes);
            }
            finally { first?.Dispose(); second?.Dispose(); original.Dispose(); }
            Assert.Equal(0, allocator.ReservedBytes);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(4096)]
    public void PoolChunkStyleAlignmentRetainsTheBaseOwnerAndLogicalPayload(int alignment)
    {
        const int payloadLength = 256;
        int logicalLength = Math.Max(payloadLength, alignment);
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(logicalLength + alignment, clear: true);
        Memory<byte> aligned = owner.GetAlignedMemory(alignment);
        Assert.True(aligned.Length >= logicalLength);
        Assert.Equal(0, aligned.Length % alignment);
        using var basePin = owner.Memory.Pin();
        using var alignedPin = aligned.Pin();
        Assert.Equal(0, (long)Address(alignedPin) & (alignment - 1));
        int offset = checked((int)(Address(alignedPin) - Address(basePin)));
        Assert.InRange(offset, 0, alignment - 1);
        aligned.Span.Slice(0, logicalLength).Fill(0xa7);
        Assert.Equal(0xa7, owner.Memory.Span[offset + logicalLength - 1]);
    }

    [Fact]
    public void InvalidAlignmentDoesNotChangeTheOwnerOrReservation()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(8);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.GetAlignedMemory(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.GetAlignedMemory(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.GetAlignedMemory(-2));
        Assert.Equal(8, allocator.ReservedBytes);
    }

    [Fact]
    public void BorrowedViewNeverFreesItsExternalAllocation()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(32, clear: true);
        using var externalLifetime = owner.Memory.Pin();
        var view = new NativeMemoryView(Address(externalLifetime) + 3, 5);
        Memory<byte> memory = view.Memory;
        memory.Span.Fill(0xd8);
        using var borrowedPin = memory.Slice(2).Pin();
        Assert.Equal(Address(externalLifetime) + 5, Address(borrowedPin));
        view.Dispose();
        view.Dispose();
        Assert.Equal(32, allocator.ReservedBytes);
        Assert.Equal(0xd8, owner.Memory.Span[7]);
        Assert.Throws<ObjectDisposedException>(() => _ = memory.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => memory.Pin());
    }

    [Fact]
    public void NullAndNegativeViewBoundsFailBeforePointerAccess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeMemoryView(1, -1));
        Assert.Throws<ArgumentException>(() => new NativeMemoryView(0, 1));
        using var empty = new NativeMemoryView(0, 0);
        Assert.Empty(empty.Memory.ToArray());
        using var pin = empty.Memory.Pin();
        Assert.Equal(0, Address(pin));
    }

    [Fact]
    public void MemoryCopySupportsOverlappingRegionsAndTypedPayloads()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(32, clear: true);
        for (int i = 0; i < 16; i++) owner.Memory.Span[i] = (byte)i;
        owner.Memory.Slice(0, 12).CopyTo(owner.Memory.Slice(3, 12));
        Assert.Equal(new byte[] { 0, 1, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            owner.Memory.Span.Slice(0, 15).ToArray());
        BinaryPrimitives.WriteInt64BigEndian(owner.Memory.Span.Slice(19), unchecked((long)0x8070605040302010));
        Assert.Equal(unchecked((long)0x8070605040302010),
            BinaryPrimitives.ReadInt64BigEndian(owner.Memory.Span.Slice(19)));
        owner.Memory.Slice(3, 12).CopyTo(owner.Memory.Slice(0, 12));
        Assert.Equal((byte)11, owner.Memory.Span[11]);
    }

    [Fact]
    public void AsciiStringCopiesNonArrayNativeMemoryBeforeOwnershipEnds()
    {
        var allocator = new NativeMemoryAllocator();
        AsciiString text;
        using (var owner = allocator.Allocate(8))
        {
            new byte[] { 99, 0x80, 0xff, 65, 66, 0, 88, 77 }.CopyTo(owner.Memory);
            text = new AsciiString(owner.Memory.Slice(1, 5), copy: false);
        }
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.Equal(new byte[] { 0x80, 0xff, 65, 66, 0 }, text.AsSpan().ToArray());
    }

    [Fact]
    public async Task AScopedPinKeepsNativeStorageValidAcrossAsyncCompletionAndOwnerDisposal()
    {
        var allocator = new NativeMemoryAllocator();
        var owner = allocator.Allocate(8, clear: true);
        var pin = owner.Memory.Pin();
        try
        {
            owner.Dispose();
            nint address = Address(pin);
            await Task.Run(() => Marshal.WriteByte(address + 4, 0xa9));
            Assert.Equal(0xa9, Marshal.ReadByte(address + 4));
            Assert.Equal(8, allocator.ReservedBytes);
        }
        finally { pin.Dispose(); owner.Dispose(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentReservationsCannotOvercommitTheAllocatorLimit()
    {
        var allocator = new NativeMemoryAllocator(64);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        int accepted = 0;
        var work = new Task[32];
        for (int i = 0; i < work.Length; i++)
        {
            work[i] = Task.Run(async () =>
            {
                NativeMemoryOwner owner = null;
                try
                {
                    try { owner = allocator.Allocate(8); Interlocked.Increment(ref accepted); }
                    catch (OutOfMemoryException) { }
                    finally { if (Interlocked.Increment(ref attempts) == work.Length) ready.TrySetResult(); }
                    if (owner != null) await release.Task;
                }
                finally { owner?.Dispose(); }
            });
        }
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(8, accepted);
            Assert.Equal(64, allocator.ReservedBytes);
        }
        finally { release.TrySetResult(); await Task.WhenAll(work); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void PinAndDisposeRaceEitherRejectsThePinOrRetainsTheAllocation()
    {
        var allocator = new NativeMemoryAllocator();
        for (int i = 0; i < 256; i++)
        {
            var owner = allocator.Allocate(16, clear: true);
            Memory<byte> memory = owner.Memory;
            MemoryHandle pin = default;
            bool acquired = false;
            using var barrier = new Barrier(2);
            Parallel.Invoke(() =>
            {
                barrier.SignalAndWait();
                try { pin = memory.Pin(); acquired = true; }
                catch (ObjectDisposedException) { }
            }, () => { barrier.SignalAndWait(); owner.Dispose(); });
            try
            {
                Assert.Equal(acquired ? 16 : 0, allocator.ReservedBytes);
                if (acquired) Assert.Equal(0, Marshal.ReadByte(Address(pin)));
            }
            finally { pin.Dispose(); owner.Dispose(); }
            Assert.Equal(0, allocator.ReservedBytes);
        }
    }

    [Fact]
    public async Task AbandonedOwnersAndPinLeasesHaveGcFallbackWithoutLeakingQuota()
    {
        var allocator = new NativeMemoryAllocator();
        WeakReference first = AbandonAllocation(allocator, false);
        WeakReference second = AbandonAllocation(allocator, true);
        var timeout = Stopwatch.StartNew();
        while (allocator.ReservedBytes != 0 && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.False(first.IsAlive);
        Assert.False(second.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonAllocation(NativeMemoryAllocator allocator, bool pin)
    {
        var owner = allocator.Allocate(32);
        if (pin) _ = owner.Memory.Pin();
        return new WeakReference(owner);
    }

    private static unsafe nint Address(MemoryHandle handle) => (nint)handle.Pointer;
}
