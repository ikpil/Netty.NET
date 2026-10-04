using System;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class HexEncodingContractTest
{
    [Theory]
    [InlineData(-1, 1, "offset")]
    [InlineData(int.MinValue, 1, "offset")]
    [InlineData(2, 2, "length")]
    [InlineData(int.MaxValue, 1, "offset")]
    [InlineData(1, int.MaxValue, "length")]
    [InlineData(0, int.MaxValue, "length")]
    [InlineData(3, 1, "length")]
    public void InvalidRangesRejectBeforeAppending(int offset, int length, string parameter)
    {
        byte[] source = { 0, 15, 255 };
        var padded = new StringBuilder("prefix:");
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexStringPadded(padded, source, offset, length)).ParamName);
        Assert.Equal("prefix:", padded.ToString());
        var unpadded = new StringBuilder("prefix:");
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexString(unpadded, source, offset, length)).ParamName);
        Assert.Equal("prefix:", unpadded.ToString());
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexStringPadded(source, offset, length)).ParamName);
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexString(source, offset, length)).ParamName);
    }

    [Fact]
    public void NegativeLengthsUseTheNativeRangeErrorWithoutTouchingTheDestination()
    {
        byte[] source = { 255 };
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexStringPadded(source, 0, -1)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexString(source, 0, -1)).ParamName);
        var destination = new StringBuilder("prefix:");
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexStringPadded(destination, source, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexString(destination, source, 0, -1));
        Assert.Equal("prefix:", destination.ToString());
    }

    [Fact]
    public void NullInputsAndDestinationsUseNativeArgumentErrorsEvenForEmptyRanges()
    {
        Assert.Equal("src", Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexStringPadded((byte[])null)).ParamName);
        Assert.Equal("src", Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexString((byte[])null)).ParamName);
        Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexStringPadded(null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexString(null, 0, 0));
        Assert.Equal("dst", Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexStringPadded(null, Array.Empty<byte>())).ParamName);
        Assert.Equal("dst", Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexString(null, Array.Empty<byte>())).ParamName);
        Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexStringPadded(new StringBuilder(), null));
        Assert.Throws<ArgumentNullException>(() => StringUtil.ToHexString(new StringBuilder(), null));
        Assert.Equal("buf", Assert.Throws<ArgumentNullException>(() => StringUtil.ByteToHexStringPadded(null, 0)).ParamName);
        Assert.Equal("buf", Assert.Throws<ArgumentNullException>(() => StringUtil.ByteToHexString(null, 0)).ParamName);
    }

    [Fact]
    public void EmptySlicesStillValidateTheirSourceAndOffset()
    {
        byte[] source = { 255 };
        var destination = new StringBuilder("prefix:");
        Assert.Equal("", StringUtil.ToHexStringPadded(source, 1, 0));
        Assert.Equal("", StringUtil.ToHexString(source, 1, 0));
        Assert.Same(destination, StringUtil.ToHexStringPadded(destination, source, 1, 0));
        Assert.Same(destination, StringUtil.ToHexString(destination, source, 1, 0));
        Assert.Equal("prefix:", destination.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexStringPadded(source, int.MaxValue, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexString(source, -1, 0));
    }

    [Theory]
    [InlineData(0, 6, "00000f80ff00", "f80ff00")]
    [InlineData(0, 2, "0000", "0")]
    [InlineData(2, 3, "0f80ff", "f80ff")]
    [InlineData(3, 2, "80ff", "80ff")]
    [InlineData(6, 0, "", "")]
    public void StringAndAppendOutputsPreserveNumericLeadingZeroAndSliceSemantics(int offset, int length, string padded, string unpadded)
    {
        byte[] source = { 0, 0, 15, 128, 255, 0 };
        Assert.Equal(padded, StringUtil.ToHexStringPadded(source, offset, length));
        Assert.Equal(unpadded, StringUtil.ToHexString(source, offset, length));
        var destination = new StringBuilder("prefix:");
        Assert.Same(destination, StringUtil.ToHexStringPadded(destination, source, offset, length));
        Assert.Equal("prefix:" + padded, destination.ToString());
        destination.Clear().Append("prefix:");
        Assert.Same(destination, StringUtil.ToHexString(destination, source, offset, length));
        Assert.Equal("prefix:" + unpadded, destination.ToString());
        Assert.Equal(new byte[] { 0, 0, 15, 128, 255, 0 }, source);
    }

    [Fact]
    public void SingleByteFormattingMasksSignedAndOversizedIntegers()
    {
        foreach (int value in new[] { int.MinValue, int.MaxValue, -257, -256, -255, -1, 0, 15, 16, 255, 256, 511 })
        {
            string expected = (value & 255).ToString("x");
            Assert.Equal(expected.PadLeft(2, '0'), StringUtil.ByteToHexStringPadded(value));
            Assert.Equal(expected, StringUtil.ByteToHexString(value));
            var destination = new StringBuilder("p:");
            Assert.Same(destination, StringUtil.ByteToHexString(destination, value));
            Assert.Equal("p:" + expected, destination.ToString());
            destination.Clear().Append("p:");
            Assert.Same(destination, StringUtil.ByteToHexStringPadded(destination, value));
            Assert.Equal("p:" + expected.PadLeft(2, '0'), destination.ToString());
        }
    }

    [Fact]
    public void NativeBuilderCapacityFailurePropagatesWithItsExistingPartialAppend()
    {
        var destination = new StringBuilder(1, 3).Append('p');
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.ToHexStringPadded(destination, new byte[] { 171, 205 }));
        Assert.Equal("pab", destination.ToString());
    }
}
