using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class ByteRangeComparisonContractTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(65)]
    public void LogicalSlicesCompareEveryByteAndIgnoreSurroundingStorage(int length)
    {
        byte[] left = new byte[length + 7];
        byte[] right = new byte[length + 11];
        left.AsSpan().Fill(0x5a);
        right.AsSpan().Fill(0xa5);
        for (int i = 0; i < length; i++) left[i + 3] = right[i + 5] = unchecked((byte)(i * 73 + 128));
        var leftView = new AsciiString(left, 3, length, false);
        var rightView = new AsciiString(right, 5, length, false);
        Assert.True(leftView.ContentEquals(rightView));
        Assert.Equal(leftView.GetHashCode(), rightView.GetHashCode());
        Assert.True(PlatformDependent.Equals(left, 3, right, 5, length));
        Assert.Equal(1, PlatformDependent.EqualsConstantTime(left, 3, right, 5, length));
        Assert.Equal(1, ConstantTimeUtils.EqualsConstantTime(left, 3, right, 5, length));
        for (int i = 0; i < length; i++)
        {
            right[i + 5] ^= 0x80;
            Assert.False(PlatformDependent.Equals(left, 3, right, 5, length));
            Assert.Equal(0, PlatformDependent.EqualsConstantTime(left, 3, right, 5, length));
            Assert.Equal(0, ConstantTimeUtils.EqualsConstantTime(left, 3, right, 5, length));
            right[i + 5] ^= 0x80;
        }
        left.AsSpan(3, length).Clear();
        Assert.True(PlatformDependent.IsZero(left, 3, length));
        for (int i = 0; i < length; i++)
        {
            left[i + 3] = 0x80;
            Assert.False(PlatformDependent.IsZero(left, 3, length));
            left[i + 3] = 0;
        }
    }

    [Theory]
    [InlineData("Equality")]
    [InlineData("FixedTime")]
    [InlineData("UtilityFixedTime")]
    [InlineData("Zero")]
    [InlineData("Hash")]
    public void PositiveRangesAreValidatedBeforeContentOrOverflowCanHideInvalidBounds(string operation)
    {
        byte[] bytes = new byte[16];
        bytes.AsSpan().Fill(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoke(operation, bytes, 15, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoke(operation, bytes, int.MaxValue, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoke(operation, bytes, -1, 1));
    }

    [Fact]
    public void InvalidSecondComparisonRangeIsCheckedBeforeAnEarlyMismatch()
    {
        byte[] left = new byte[16];
        byte[] right = new byte[16];
        left.AsSpan().Fill(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.Equals(left, 0, right, 15, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.EqualsConstantTime(left, 0, right, 15, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConstantTimeUtils.EqualsConstantTime(left, 0, right, 15, 2));
    }

    [Fact]
    public void NonPositiveComparisonLengthsRetainTheExistingEmptyResult()
    {
        byte[] bytes = new byte[16];
        foreach (int length in new[] { 0, -1, int.MinValue })
        {
            Assert.True(PlatformDependent.Equals(bytes, 0, bytes, 0, length));
            Assert.Equal(1, PlatformDependent.EqualsConstantTime(bytes, 0, bytes, 0, length));
            Assert.Equal(1, ConstantTimeUtils.EqualsConstantTime(bytes, 0, bytes, 0, length));
            Assert.True(PlatformDependent.IsZero(bytes, 0, length));
        }
        Assert.Equal(PlatformDependent.HashCodeAscii(Array.Empty<byte>(), 0, 0),
            PlatformDependent.HashCodeAscii(bytes, bytes.Length, 0));
    }

    [Fact]
    public void HashRangesAreBoundedEvenWhenNoWordWouldBeRead()
    {
        byte[] bytes = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.HashCodeAscii(bytes, 17, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformDependent.HashCodeAscii(bytes, 0, -8));
    }

    [Fact]
    public void NullStorageFailsExplicitlyIncludingEmptyRanges()
    {
        foreach (string operation in new[] { "Equality", "FixedTime", "UtilityFixedTime", "Zero", "Hash" })
            Assert.Throws<ArgumentNullException>(() => Invoke(operation, null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => PlatformDependent.Equals(new byte[1], 0, null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => PlatformDependent.EqualsConstantTime(new byte[1], 0, null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ConstantTimeUtils.EqualsConstantTime(new byte[1], 0, null, 0, 0));
    }

    private static object Invoke(string operation, byte[] bytes, int start, int length) => operation switch
    {
        "Equality" => PlatformDependent.Equals(bytes, start, new byte[16], 0, length),
        "FixedTime" => PlatformDependent.EqualsConstantTime(bytes, start, new byte[16], 0, length),
        "UtilityFixedTime" => ConstantTimeUtils.EqualsConstantTime(bytes, start, new byte[16], 0, length),
        "Zero" => PlatformDependent.IsZero(bytes, start, length),
        "Hash" => PlatformDependent.HashCodeAscii(bytes, start, length),
        _ => throw new ArgumentException(nameof(operation))
    };
}
