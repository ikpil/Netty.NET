using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class HexDecodingContractTest
{
    [Fact]
    public void OnlyAsciiHexDigitsDecodeAcrossTheEntireUtf16AndByteDomains()
    {
        const string digits = "0123456789abcdef";
        const string upperDigits = "0123456789ABCDEF";
        for (int codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
        {
            char value = (char)codeUnit;
            int expected = Math.Max(digits.IndexOf(value), upperDigits.IndexOf(value));
            Assert.Equal(expected, StringUtil.DecodeHexNibble(value));
            if (codeUnit <= byte.MaxValue) Assert.Equal(expected, StringUtil.DecodeHexNibble((byte)codeUnit));
        }
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            string pair = string.Concat(upperDigits[value / 16], digits[value % 16]);
            Assert.Equal((byte)value, StringUtil.DecodeHexByte(pair, 0));
            Assert.Equal((byte)value, StringUtil.DecodeHexByte(new StringCharSequence(pair), 0));
            Assert.Equal(new byte[] { (byte)value }, StringUtil.DecodeHexDump(pair));
        }
    }

    [Theory]
    [InlineData(" f")]
    [InlineData("f ")]
    [InlineData("\tf")]
    [InlineData("f\n")]
    public void StringAndSequenceRejectWhitespaceWithTheSamePairDiagnostic(string pair)
    {
        string input = "xx" + pair + "yy";
        var sequence = new StringCharSequence(input);
        var expected = Assert.Throws<ArgumentException>(() => StringUtil.DecodeHexByte(sequence, 2));
        var actual = Assert.Throws<ArgumentException>(() => StringUtil.DecodeHexByte(input, 2));
        Assert.Equal(expected.Message, actual.Message);
    }

    [Theory]
    [InlineData(" f:01:23:45:67:89")]
    [InlineData("f :01:23:45:67:89")]
    [InlineData("01:23:45:67:89: f")]
    [InlineData("01-23-45-67-89-f ")]
    public void MacAddressConsumerRejectsWhitespaceInsideHexPairs(string address)
    {
        Assert.Throws<ArgumentException>(() => MacAddressUtil.ParseMAC(address));
    }

    [Fact]
    public void ByteDecodersRejectNullAndInvalidPositionsBeforeAccess()
    {
        Assert.Equal("str", Assert.Throws<ArgumentNullException>(() => StringUtil.DecodeHexByte((string)null, 0)).ParamName);
        Assert.Equal("s", Assert.Throws<ArgumentNullException>(() => StringUtil.DecodeHexByte((ICharSequence)null, 0)).ParamName);
        foreach (int position in new[] { -1, 1, 2, int.MaxValue })
        {
            Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexByte("ff", position)).ParamName);
            Assert.Equal("pos", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexByte(new StringCharSequence("ff"), position)).ParamName);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexByte("", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexByte("f", 0));
    }

    [Fact]
    public void DumpSlicesUseLogicalBoundsAndKeepTheOriginalEmptyShortCircuit()
    {
        var slice = new StringCharSequence("xx00aFffyy", 2, 6);
        Assert.Equal(new byte[] { 0, 175, 255 }, StringUtil.DecodeHexDump(slice));
        Assert.Equal(new byte[] { 175 }, StringUtil.DecodeHexDump(slice, 2, 2));
        Assert.Equal("fromIndex", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexDump(slice, -1, 2)).ParamName);
        Assert.Equal("fromIndex", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexDump(slice, int.MaxValue, 2)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.DecodeHexDump(slice, 4, 4)).ParamName);
        foreach (int length in new[] { -2, -1, 1, 3 })
            Assert.Equal("length: " + length, Assert.Throws<ArgumentException>(() => StringUtil.DecodeHexDump(slice, 0, length)).Message);
        Assert.Same(EmptyArrays.EMPTY_BYTES, StringUtil.DecodeHexDump((ICharSequence)null, int.MinValue, 0));
        Assert.Same(EmptyArrays.EMPTY_BYTES, StringUtil.DecodeHexDump(slice, int.MaxValue, 0));
        Assert.Same(EmptyArrays.EMPTY_BYTES, StringUtil.DecodeHexDump(""));
        Assert.Equal("hexDump", Assert.Throws<ArgumentNullException>(() => StringUtil.DecodeHexDump((ICharSequence)null)).ParamName);
        Assert.Equal("hexDump", Assert.Throws<ArgumentNullException>(() => StringUtil.DecodeHexDump((string)null)).ParamName);
    }

    [Fact]
    public void NativeStringDecodingHasNoPerPairTemporaryAllocation()
    {
        const string input = "00aFff";
        for (int i = 0; i < 1000; i++) StringUtil.DecodeHexByte(input, 2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 10000; i++) sum += StringUtil.DecodeHexByte(input, 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1750000, sum);
        Assert.Equal(0, allocated);
        Assert.Equal(new byte[] { 0, 175, 255 }, StringUtil.DecodeHexDump(input));
        Assert.Equal("invalid hex byte 'g1' at index 2 of '00g1'",
            Assert.Throws<ArgumentException>(() => StringUtil.DecodeHexDump("00g1")).Message);
    }
}
