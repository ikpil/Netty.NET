using System;
using System.Runtime.InteropServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

// Consumer contracts from AsciiStringUtil and buffer's UnsafeHeapSwappedByteBuf,
// HeapByteBufUtil and ByteBufUtil. Native byte order is distinct from wire order.
public class HeapMemoryContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void PrimitiveWritesMatchIndependentNativeOrderBytes(int offset)
    {
        byte[] data = new byte[24];
        Array.Fill(data, (byte)0xcc);
        byte[] expected = (byte[])data.Clone();
        const long value = unchecked((long)0x80ff0123fedcba98UL);

        MemoryMarshal.Write(data.AsSpan(offset, sizeof(long)), value);
        BitConverter.GetBytes(value).CopyTo(expected, offset);
        Assert.Equal(expected, data);
        Assert.Equal(value, MemoryMarshal.Read<long>(data.AsSpan(offset, sizeof(long))));

        const int intValue = unchecked((int)0x80abcdef);
        MemoryMarshal.Write(data.AsSpan(offset, sizeof(int)), intValue);
        BitConverter.GetBytes(intValue).CopyTo(expected, offset);
        Assert.Equal(expected, data);
        Assert.Equal(intValue, MemoryMarshal.Read<int>(data.AsSpan(offset, sizeof(int))));

        const short shortValue = unchecked((short)0x80ff);
        MemoryMarshal.Write(data.AsSpan(offset, sizeof(short)), shortValue);
        BitConverter.GetBytes(shortValue).CopyTo(expected, offset);
        Assert.Equal(expected, data);
        Assert.Equal(shortValue, MemoryMarshal.Read<short>(data.AsSpan(offset, sizeof(short))));

        data[offset] = 0xfe;
        expected[offset] = 0xfe;
        Assert.Equal(expected, data);
        Assert.Equal((byte)0xfe, data[offset]);
        Assert.Equal((byte)0xfe, data[(long)offset]);
    }

    [Theory]
    [InlineData(0, 2, 8)]
    [InlineData(2, 0, 8)]
    [InlineData(1, 1, 9)]
    [InlineData(10, 10, 0)]
    public void OverlappingCopyHasSnapshotMeaning(int source, int destination, int count)
    {
        byte[] data = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        byte[] expected = (byte[])data.Clone();
        byte[] snapshot = (byte[])data.Clone();
        for (int i = 0; i < count; i++)
            expected[destination + i] = snapshot[source + i];
        PlatformDependent.CopyMemory(data, source, data, destination, count);
        Assert.Equal(expected, data);
    }

    [Fact]
    public void CopyAndFillRespectLogicalRanges()
    {
        byte[] source = { 90, 91, 0x80, 0xff, 3, 92 };
        byte[] destination = { 9, 9, 9, 9, 9, 9 };
        PlatformDependent.CopyMemory(source, 2, destination, 1, 3);
        Assert.Equal(new byte[] { 9, 0x80, 0xff, 3, 9, 9 }, destination);
        PlatformDependent.SetMemory(destination, 2, 2, 0xab);
        Assert.Equal(new byte[] { 9, 0x80, 0xab, 0xab, 9, 9 }, destination);
        PlatformDependent.SetMemory(destination, destination.Length, 0, 0);
        Assert.Equal(new byte[] { 90, 91, 0x80, 0xff, 3, 92 }, source);
    }

    [Fact]
    public void InvalidRangesFailBeforeChangingDestination()
    {
        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8 };
        byte[] original = (byte[])data.Clone();
        const long longValue = 9;
        const short shortValue = 9;
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryMarshal.Write(data.AsSpan(1, sizeof(long)), longValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryMarshal.Write(data.AsSpan(-1, sizeof(short)), shortValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryMarshal.Read<int>(data.AsSpan(6, sizeof(int))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.CopyMemory(data, 0, data, 1, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.CopyMemory(data, -1, data, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.SetMemory(data, 7, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.SetMemory(data, 0, -1, 0));
        Assert.Throws<OverflowException>(() => PlatformDependent.CopyMemory(data, 0, data, 0, long.MaxValue));
        Assert.Throws<OverflowException>(() => PlatformDependent.SetMemory(data, 0, long.MaxValue, 0));
        Assert.Equal(original, data);
    }

    [Fact]
    public void NullArraysAreRejectedEvenForEmptyCopyAndFill()
    {
        Assert.Throws<ArgumentNullException>(() => PlatformDependent.CopyMemory(null, 0, Array.Empty<byte>(), 0, 0));
        Assert.Throws<ArgumentNullException>(() => PlatformDependent.CopyMemory(Array.Empty<byte>(), 0, null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => PlatformDependent.SetMemory(null, 0, 0, 0));
    }

    [Fact]
    public void TypedArrayIndexesAreElementIndexesAndCannotTruncateLongs()
    {
        Assert.Equal(-2, new[] { 1, -2 }[1L]);
        Assert.Equal(long.MinValue, new[] { 1L, long.MinValue }[1L]);
        // Native arrays reject an out-of-range long index without narrowing it.
        Assert.Throws<IndexOutOfRangeException>(() => (new byte[1])[1L << 32]);
        Assert.Throws<IndexOutOfRangeException>(() => (new int[1])[1L << 32]);
        Assert.Throws<IndexOutOfRangeException>(() => (new long[1])[1L << 32]);
    }
}
