using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Netty.NET.Common.Tests.Porting;

public class NativeMemoryAccessContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(15)]
    public void BorrowedAndOwnedWordsShareBytesWithoutAlignmentOrEndianAssumptions(int offset)
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(32, clear: true);
        using var view = new NativeMemoryView(owner.Memory.Slice(offset, 8));
        short shortValue = short.MinValue;
        int intValue = unchecked((int)0x80abcdef);
        long longValue = unchecked((long)0x8070605040302010);

        owner.Memory.Span.Fill(0x5a);
        MemoryMarshal.Write(view.Memory.Span, in shortValue);
        Assert.Equal(BitConverter.GetBytes(shortValue), owner.Memory.Slice(offset, 2).ToArray());
        Assert.Equal(shortValue, MemoryMarshal.Read<short>(owner.Memory.Span.Slice(offset, 2)));
        MemoryMarshal.Write(view.Memory.Span, in intValue);
        Assert.Equal(BitConverter.GetBytes(intValue), owner.Memory.Slice(offset, 4).ToArray());
        Assert.Equal(intValue, MemoryMarshal.Read<int>(owner.Memory.Span.Slice(offset, 4)));
        MemoryMarshal.Write(view.Memory.Span, in longValue);
        Assert.Equal(BitConverter.GetBytes(longValue), owner.Memory.Slice(offset, 8).ToArray());
        Assert.Equal(longValue, MemoryMarshal.Read<long>(owner.Memory.Span.Slice(offset, 8)));
        Assert.True(owner.Memory.Span.Slice(0, offset).IndexOfAnyExcept((byte)0x5a) < 0);
        Assert.True(owner.Memory.Span.Slice(offset + 8).IndexOfAnyExcept((byte)0x5a) < 0);

        BinaryPrimitives.WriteInt64BigEndian(view.Memory.Span, longValue);
        Assert.Equal(new byte[] { 0x80, 0x70, 0x60, 0x50, 0x40, 0x30, 0x20, 0x10 }, view.Memory.ToArray());
        Assert.Equal(longValue, BinaryPrimitives.ReadInt64BigEndian(owner.Memory.Span.Slice(offset)));
        BinaryPrimitives.WriteInt64LittleEndian(view.Memory.Span, longValue);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 }, view.Memory.ToArray());
        Assert.Equal(longValue, BinaryPrimitives.ReadInt64LittleEndian(owner.Memory.Span.Slice(offset)));

        using var shortView = new NativeMemoryView(owner.Memory.Slice(offset, 3));
        byte[] before = owner.Memory.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryMarshal.Read<int>(shortView.Memory.Span));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryMarshal.Write(shortView.Memory.Span, in intValue));
        Assert.Equal(before, owner.Memory.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CopyAndFillPreserveBoundsAcrossHeapAndNativeStorage(bool nativeSource, bool nativeDestination)
    {
        var allocator = new NativeMemoryAllocator();
        using var sourceOwner = allocator.Allocate(20, clear: true);
        using var destinationOwner = allocator.Allocate(20, clear: true);
        Memory<byte> source = nativeSource ? sourceOwner.Memory : new byte[20];
        Memory<byte> destination = nativeDestination ? destinationOwner.Memory : new byte[20];

        for (int i = 0; i < source.Length; i++)
        {
            source.Span[i] = (byte)(0x80 + i);
        }

        destination.Span.Fill(0x5a);
        source.Slice(3, 11).CopyTo(destination.Slice(5, 11));
        Assert.Equal(source.Slice(3, 11).ToArray(), destination.Slice(5, 11).ToArray());
        Assert.True(destination.Span.Slice(0, 5).IndexOfAnyExcept((byte)0x5a) < 0);
        Assert.True(destination.Span.Slice(16).IndexOfAnyExcept((byte)0x5a) < 0);
        byte[] before = destination.ToArray();
        Assert.Throws<ArgumentException>(() => source.Slice(3, 11).CopyTo(destination.Slice(5, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Slice(19, 2).CopyTo(destination));
        Assert.Equal(before, destination.ToArray());
        source.Slice(20, 0).CopyTo(destination.Slice(20, 0));
        Assert.Equal(before, destination.ToArray());
        destination.Slice(5, 11).Span.Fill(0xff);
        Assert.True(destination.Span.Slice(5, 11).IndexOfAnyExcept((byte)0xff) < 0);
        Assert.True(destination.Span.Slice(0, 5).IndexOfAnyExcept((byte)0x5a) < 0);
        Assert.True(destination.Span.Slice(16).IndexOfAnyExcept((byte)0x5a) < 0);
    }

    [Fact]
    public void BorrowedAliasesCopyOverlappingRegionsInBothDirections()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(32);
        using var source = new NativeMemoryView(owner.Memory[..24]);
        using var destination = new NativeMemoryView(owner.Memory.Slice(3, 24));
        byte[] expected = new byte[32];

        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)i;
        }

        expected.CopyTo(owner.Memory);
        Array.Copy(expected, 0, expected, 3, 24);
        source.Memory.CopyTo(destination.Memory);
        Assert.Equal(expected, owner.Memory.ToArray());
        Array.Copy(expected, 3, expected, 0, 24);
        destination.Memory.CopyTo(source.Memory);
        Assert.Equal(expected, owner.Memory.ToArray());
    }

    [Fact]
    public void BorrowedManagedViewsRemainBoundedAtTheEndOfTheirRange()
    {
        using var view = new NativeMemoryView(new byte[8].AsMemory(1, 7));
        Assert.True(view.Memory.Slice(7, 0).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Memory.Slice(7, 1));
    }

    [Fact]
    public void RawAddressViewsRejectTheAddressBeforeAttemptingToAccessIt()
    {
#pragma warning disable CS0618 // Exercise the old API's explicit failure.
        Assert.Throws<PlatformNotSupportedException>(() => new NativeMemoryView(nint.MaxValue - 3, 8));
        Assert.Throws<PlatformNotSupportedException>(() => new NativeMemoryView(0, 0));
#pragma warning restore CS0618
    }

}
