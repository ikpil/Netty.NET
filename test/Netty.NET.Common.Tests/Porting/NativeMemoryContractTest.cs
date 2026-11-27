using System;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class NativeMemoryContractTest
{
    [Theory]
    [InlineData(0)] [InlineData(8)]
    public void OwnerLookupPreservesIdentityForSlicesWithoutAcquiringLifetime(int length)
    {
        var allocator = new NativeMemoryAllocator();
        var owner = allocator.Allocate(length);
        ReadOnlyMemory<byte> memory = owner.Memory;
        Assert.True(NativeMemoryOwner.TryGetOwner(memory, out var found));
        Assert.Same(owner, found);
        Assert.True(NativeMemoryOwner.TryGetOwner(memory.Slice(length / 2), out found));
        Assert.Same(owner, found);
        Assert.True(NativeMemoryOwner.TryGetOwner(memory.Slice(length, 0), out found));
        Assert.Same(owner, found);
        owner.Dispose();
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.True(NativeMemoryOwner.TryGetOwner(memory, out found));
        Assert.Same(owner, found);
        Assert.Throws<ObjectDisposedException>(() => _ = memory.Span.Length);
        Assert.False(NativeMemoryOwner.TryGetOwner(new byte[8], out found));
        Assert.Null(found);
        Assert.False(NativeMemoryOwner.TryGetOwner(ReadOnlyMemory<byte>.Empty, out found));
        Assert.Null(found);
    }

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
        Assert.Equal(Address(owner.Memory) + 5, Address(slice));
        Assert.Equal(Address(owner.Memory) + 32, Address(owner.Memory.Slice(32, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Memory.Slice(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Memory.Slice(31, 2));
    }

    [Fact]
    public void DisposedOwnerReleasesTheReservationAndInvalidatesViews()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(16, clear: true);
        Memory<byte> view = owner.Memory;
        using var pin = view.Pin();
        Span<byte> bytes = view.Span;
        bytes[3] = 0xc7;
        owner.Dispose();
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => _ = view.Span.Length);
        Assert.Equal(0xc7, bytes[3]);
    }

    [Fact]
    public void AllocationFailuresAndInvalidArgumentsLeaveTheReservationUnchanged()
    {
        var allocator = new NativeMemoryAllocator(8);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeMemoryAllocator(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(-1));
        // CLR: the reservation limit reports PlatformDependent.incrementMemoryCounter's
        // OutOfDirectMemoryError and message.
        Assert.Equal("failed to allocate 9 byte(s) of direct memory (used: 0, max: 8)",
            Assert.Throws<OutOfDirectMemoryError>(() => allocator.Allocate(9)).Message);
        Assert.Equal(0, allocator.ReservedBytes);
        using var owner = allocator.Allocate(8);
        Assert.Equal("failed to allocate 1 byte(s) of direct memory (used: 8, max: 8)",
            Assert.Throws<OutOfDirectMemoryError>(() => allocator.Allocate(0)).Message);
        Assert.Equal(8, allocator.ReservedBytes);
        Assert.Throws<OutOfDirectMemoryError>(() => new NativeMemoryAllocator(0).Allocate(0));
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
        Assert.NotEqual(0, Address(empty.Memory));
    }

    [Fact]
    public void FailedReallocationPreservesTheOriginalOwnerBytesAndQuota()
    {
        var allocator = new NativeMemoryAllocator(8);
        using var owner = allocator.Allocate(8);
        owner.Memory.Span.Fill(0x7b);
        Assert.Throws<OutOfDirectMemoryError>(() => allocator.Reallocate(owner, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Reallocate(owner, -1));
        Assert.Throws<ArgumentException>(() => new NativeMemoryAllocator().Reallocate(owner, 1));
        Assert.Throws<ArgumentNullException>(() => allocator.Reallocate(null, 1));
        Assert.Equal(8, allocator.ReservedBytes);
        Assert.True(owner.Memory.Span.IndexOfAnyExcept((byte)0x7b) < 0);
    }

    [Fact]
    public void ReallocationWithARuntimePinKeepsSameSizeOwnershipCoherent()
    {
        var allocator = new NativeMemoryAllocator();
        var owner = allocator.Allocate(8, clear: true);
        using var pin = owner.Memory.Pin();
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
        Assert.Equal(0, (long)Address(aligned) & (alignment - 1));
        int offset = checked((int)(Address(aligned) - Address(owner.Memory)));
        Assert.InRange(offset, 0, alignment - 1);
        aligned.Span.Slice(0, logicalLength).Fill(0xa7);
        Assert.Equal(0xa7, owner.Memory.Span[offset + logicalLength - 1]);
    }

    [Fact]
    public void AlignedMemoryKeepsItsAlignmentAndAliasAcrossACompactingCollection()
    {
        const int alignment = 4096;
        using var owner = new NativeMemoryAllocator().Allocate(2 * alignment, clear: true);
        Memory<byte> aligned = owner.GetAlignedMemory(alignment);
        aligned.Span[0] = 42;

        // A diagnostic address lookup also pins the array, so defer it until after collection.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        nint address = Address(aligned);
        Assert.Equal(0, (long)address & (alignment - 1));
        int offset = checked((int)(address - Address(owner.Memory)));
        Assert.InRange(offset, 0, alignment - 1);
        Assert.Equal(42, owner.Memory.Span[offset]);
        owner.Memory.Span[offset] = 7;
        Assert.Equal(7, aligned.Span[0]);
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
        var view = new NativeMemoryView(owner.Memory.Slice(3, 5));
        Memory<byte> memory = view.Memory;
        memory.Span.Fill(0xd8);
        view.Dispose();
        view.Dispose();
        Assert.Equal(32, allocator.ReservedBytes);
        Assert.Equal(0xd8, owner.Memory.Span[7]);
        Assert.Throws<ObjectDisposedException>(() => _ = memory.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => memory.Pin());
    }

    [Fact]
    public void EmptyManagedViewSupportsEmptySlices()
    {
        using var empty = new NativeMemoryView(Memory<byte>.Empty);
        Assert.True(empty.Memory.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Memory.Slice(0, 1));
        using var pin = empty.Memory.Pin();
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
    public async Task BorrowedMemoryCanBeUsedAcrossAsyncCompletionWhileTheOwnerLives()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(8, clear: true);
        Memory<byte> bytes = owner.Memory;
        await Task.Run(() => bytes.Span[4] = 0xa9);
        Assert.Equal(0xa9, bytes.Span[4]);
        Assert.Equal(8, allocator.ReservedBytes);
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
    public void PinAndDisposeRaceEitherRejectsThePinOrReturnsARuntimeHandle()
    {
        var allocator = new NativeMemoryAllocator();
        for (int i = 0; i < 256; i++)
        {
            var owner = allocator.Allocate(16, clear: true);
            Memory<byte> memory = owner.Memory;
            System.Buffers.MemoryHandle pin = default;
            using var barrier = new Barrier(2);
            Parallel.Invoke(() =>
            {
                barrier.SignalAndWait();
                try { pin = memory.Pin(); }
                catch (ObjectDisposedException) { }
            }, () => { barrier.SignalAndWait(); owner.Dispose(); });
            try
            {
                Assert.Equal(0, allocator.ReservedBytes);
            }
            finally { pin.Dispose(); owner.Dispose(); }
            Assert.Equal(0, allocator.ReservedBytes);
        }
    }

    [Fact]
    public async Task AbandonedOwnersReleaseTheirReservationAtFinalization()
    {
        var allocator = new NativeMemoryAllocator();
        WeakReference first = AbandonAllocation(allocator);
        WeakReference second = AbandonAllocation(allocator);
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
    private static WeakReference AbandonAllocation(NativeMemoryAllocator allocator)
    {
        var owner = allocator.Allocate(32);
        _ = owner.GetAlignedMemory(16);
        return new WeakReference(owner);
    }

    private static nint Address(ReadOnlyMemory<byte> memory)
    {
        Assert.True(NativeMemoryOwner.TryGetAddress(memory, out nint address));
        return address;
    }
}
