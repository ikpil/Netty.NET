using System;
using System.Globalization;
using System.Text;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiStringFloatingPointContractTest
{
    [Theory]
    [InlineData("0x1.8p1", 3.0)]
    [InlineData("1.25f", 1.25)]
    [InlineData("1.25d", 1.25)]
    [InlineData("\0\u001f1.25\u001f\0", 1.25)]
    public void JavaNumericFormsRemainAccepted(string text, double expected)
    {
        var value = new AsciiString(text);
        Assert.Equal((float)expected, value.ParseSingle());
        Assert.Equal(expected, value.ParseDouble());
        Assert.True(value.TryParseSingle(out float single));
        Assert.True(value.TryParseDouble(out double twice));
        Assert.Equal((float)expected, single);
        Assert.Equal(expected, twice);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("nan")]
    [InlineData("infinity")]
    public void ClrOnlyNumericFormsAreRejected(string text)
    {
        var value = new AsciiString(text);
        Assert.Throws<FormatException>(() => value.ParseSingle());
        Assert.Throws<FormatException>(() => value.ParseDouble());
        Assert.False(value.TryParseSingle(out float single));
        Assert.False(value.TryParseDouble(out double twice));
        Assert.Equal(0, BitConverter.SingleToInt32Bits(single));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(twice));
    }

    [Fact]
    public void ParsingDoesNotUseCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ",";
            culture.NumberFormat.NumberGroupSeparator = ".";
            CultureInfo.CurrentCulture = culture;
            var value = new AsciiString("1.25");
            Assert.Equal(1.25f, value.ParseSingle());
            Assert.Equal(1.25, value.ParseDouble());
            Assert.True(value.TryParseSingle(out float single));
            Assert.True(value.TryParseDouble(out double twice));
            Assert.Equal(1.25f, single);
            Assert.Equal(1.25, twice);
            Assert.False(new AsciiString("1,25").TryParseDouble(out _));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void EmptyRangesStillValidateTheLogicalView(int position)
    {
        var value = new AsciiString("12");
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseSingle(position, position));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseDouble(position, position));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseSingle(position, position, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseDouble(position, position, out _));
    }

    [Theory]
    [InlineData(".5", 0.5)]
    [InlineData("1.", 1.0)]
    [InlineData("+1E+2F", 100.0)]
    [InlineData("-.5D", -0.5)]
    [InlineData("0x.8p1", 1.0)]
    [InlineData("0X1.P4d", 16.0)]
    [InlineData("-0x1.2P+3F", -9.0)]
    public void NumericSlicesRespectBackingOffsetsAndDoNotConsumeSentinels(string text, double expected)
    {
        var value = new AsciiString(Encoding.ASCII.GetBytes("!!x" + text + "y!!"), 2, text.Length + 2, false);
        Assert.Equal((float)expected, value.ParseSingle(1, text.Length + 1));
        Assert.Equal(expected, value.ParseDouble(1, text.Length + 1));
        Assert.True(value.TryParseSingle(1, text.Length + 1, out float single));
        Assert.True(value.TryParseDouble(1, text.Length + 1, out double twice));
        Assert.Equal((float)expected, single);
        Assert.Equal(expected, twice);
        Assert.False(value.TryParseDouble(out _));
    }

    [Theory]
    [InlineData("0x1.000001p0", 0x3f800000u)]
    [InlineData("0x1.000003p0", 0x3f800002u)]
    [InlineData("0x1.0000010000000000000000000000000001p0", 0x3f800001u)]
    [InlineData("1.000000059604644775390625000000000000001", 0x3f800001u)]
    [InlineData("0x1p-149", 0x00000001u)]
    [InlineData("0x1p-150", 0x00000000u)]
    [InlineData("-0x1p-150", 0x80000000u)]
    [InlineData("0x1.0000000001p-150", 0x00000001u)]
    [InlineData("0x3p-150", 0x00000002u)]
    [InlineData("0x0.ffffffp-126", 0x00800000u)]
    [InlineData("0x1.fffffep127", 0x7f7fffffu)]
    [InlineData("0x1.fffffeffffffffp127", 0x7f7fffffu)]
    [InlineData("0x1.ffffffp127", 0x7f800000u)]
    [InlineData("-0x1.ffffffp127", 0xff800000u)]
    public void SingleRoundsDirectlyToNearestEvenAcrossAllIeeeBoundaries(string text, uint expected)
    {
        var value = new AsciiString(text);
        Assert.Equal(expected, unchecked((uint)BitConverter.SingleToInt32Bits(value.ParseSingle())));
        Assert.True(value.TryParseSingle(out float result));
        Assert.Equal(expected, unchecked((uint)BitConverter.SingleToInt32Bits(result)));
    }

    [Theory]
    [InlineData("0x1.00000000000008p0", 0x3ff0000000000000UL)]
    [InlineData("0x1.00000000000018p0", 0x3ff0000000000002UL)]
    [InlineData("0x1.0000000000000800000000000001p0", 0x3ff0000000000001UL)]
    [InlineData("0x1p-1074", 0x0000000000000001UL)]
    [InlineData("0x1p-1075", 0x0000000000000000UL)]
    [InlineData("-0x1p-1075", 0x8000000000000000UL)]
    [InlineData("0x1.0000000001p-1075", 0x0000000000000001UL)]
    [InlineData("0x3p-1075", 0x0000000000000002UL)]
    [InlineData("0x0.fffffffffffff8p-1022", 0x0010000000000000UL)]
    [InlineData("0x0.fffffffffffff7ffffffffp-1022", 0x000fffffffffffffUL)]
    [InlineData("0x1.fffffffffffffp1023", 0x7fefffffffffffffUL)]
    [InlineData("0x1.fffffffffffff7ffffffffp1023", 0x7fefffffffffffffUL)]
    [InlineData("0x1.fffffffffffff8p1023", 0x7ff0000000000000UL)]
    [InlineData("-0x1.fffffffffffff8p1023", 0xfff0000000000000UL)]
    public void DoubleRoundsDirectlyToNearestEvenAcrossAllIeeeBoundaries(string text, ulong expected)
    {
        var value = new AsciiString(text);
        Assert.Equal(expected, unchecked((ulong)BitConverter.DoubleToInt64Bits(value.ParseDouble())));
        Assert.True(value.TryParseDouble(out double result));
        Assert.Equal(expected, unchecked((ulong)BitConverter.DoubleToInt64Bits(result)));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("+NaN")]
    [InlineData("-NaN")]
    public void NaNUsesTheNativeCanonicalRepresentation(string text)
    {
        var value = new AsciiString(text);
        Assert.True(float.IsNaN(value.ParseSingle()));
        Assert.True(double.IsNaN(value.ParseDouble()));
        Assert.True(value.TryParseSingle(out float single));
        Assert.True(value.TryParseDouble(out double twice));
        Assert.Equal(BitConverter.SingleToInt32Bits(float.NaN), BitConverter.SingleToInt32Bits(single));
        Assert.Equal(BitConverter.DoubleToInt64Bits(double.NaN), BitConverter.DoubleToInt64Bits(twice));
    }

    [Theory]
    [InlineData("Infinity", false)]
    [InlineData("+Infinity", false)]
    [InlineData("-Infinity", true)]
    [InlineData("1e99999999999999999999999", false)]
    [InlineData("-1e99999999999999999999999", true)]
    [InlineData("0x1p99999999999999999999999", false)]
    [InlineData("-0x1p99999999999999999999999", true)]
    public void InfinityAndNumericOverflowAreSuccessfulParses(string text, bool negative)
    {
        var value = new AsciiString(text);
        Assert.Equal(negative ? float.NegativeInfinity : float.PositiveInfinity, value.ParseSingle());
        Assert.Equal(negative ? double.NegativeInfinity : double.PositiveInfinity, value.ParseDouble());
        Assert.True(value.TryParseSingle(out float single));
        Assert.True(value.TryParseDouble(out double twice));
        Assert.Equal(negative ? float.NegativeInfinity : float.PositiveInfinity, single);
        Assert.Equal(negative ? double.NegativeInfinity : double.PositiveInfinity, twice);
    }

    [Theory]
    [InlineData("-0")]
    [InlineData("-0.0f")]
    [InlineData("-0e99999999999999999999999999")]
    [InlineData("-1e-99999999999999999999999999")]
    [InlineData("-0x0p99999999999999999999999999")]
    [InlineData("-0x1p-99999999999999999999999999")]
    public void ZeroAndUnderflowPreserveTheSign(string text)
    {
        var value = new AsciiString(text);
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(value.ParseSingle()));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(value.ParseDouble()));
        Assert.True(value.TryParseSingle(out float single));
        Assert.True(value.TryParseDouble(out double twice));
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(single));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(twice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0 \t")]
    [InlineData("+")]
    [InlineData(".")]
    [InlineData("e1")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e2.3")]
    [InlineData("1 2")]
    [InlineData("1\0.2")]
    [InlineData("1_000")]
    [InlineData("1ff")]
    [InlineData("1fd")]
    [InlineData("NaNf")]
    [InlineData("Infinityd")]
    [InlineData("NAN")]
    [InlineData("INF")]
    [InlineData("0x1")]
    [InlineData("0x.p1")]
    [InlineData("0x1p")]
    [InlineData("0x1p+")]
    [InlineData("0x1p1.0")]
    [InlineData("0x1.2.3p1")]
    [InlineData("0x1g.p1")]
    [InlineData("0x1p999999999999999999999x")]
    [InlineData("0x1p 2")]
    public void MalformedNumbersReturnFalseAndPositiveZero(string text)
    {
        var value = new AsciiString(text);
        Assert.False(value.TryParseSingle(out float single));
        Assert.False(value.TryParseDouble(out double twice));
        Assert.Equal(0, BitConverter.SingleToInt32Bits(single));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(twice));
        Assert.Throws<FormatException>(() => value.ParseSingle());
        Assert.Throws<FormatException>(() => value.ParseDouble());
    }

    [Fact]
    public void JavaTrimAcceptsEveryAsciiControlButNoLatin1Whitespace()
    {
        for (int b = 0; b <= 32; b++)
        {
            var value = new AsciiString(new byte[] { (byte)b, (byte)'1', (byte)'.', (byte)'5', (byte)b });
            Assert.Equal(1.5f, value.ParseSingle());
            Assert.Equal(1.5, value.ParseDouble());
        }
        for (int b = 128; b <= 255; b++)
        {
            var value = new AsciiString(new byte[] { (byte)b, (byte)'1', (byte)'.', (byte)'5', (byte)b });
            Assert.False(value.TryParseSingle(out _));
            Assert.False(value.TryParseDouble(out _));
        }
    }

    [Fact]
    public void LongMantissasAndExponentsDoNotRequireUnboundedArithmeticStorage()
    {
        string zeros = new string('0', 10000);
        string exponent = new string('9', 10000);
        foreach (string text in new[] { "0x1." + zeros + "1p0", "0x0." + zeros + "1p40004" })
        {
            var value = new AsciiString(text);
            Assert.Equal(1.0f, value.ParseSingle());
            Assert.Equal(1.0, value.ParseDouble());
        }
        Assert.Equal(float.PositiveInfinity, new AsciiString("0x1p" + exponent).ParseSingle());
        Assert.Equal(double.PositiveInfinity, new AsciiString("1e" + exponent).ParseDouble());
        Assert.Equal(0.0, new AsciiString("0x1p-" + exponent).ParseDouble());
        Assert.False(new AsciiString("0x1p" + exponent + "x").TryParseDouble(out _));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 3)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void InvalidSlicesAreRejectedBeforeParsing(int start, int end)
    {
        var value = new AsciiString("12");
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseSingle(start, end));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseDouble(start, end));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseSingle(start, end, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseDouble(start, end, out _));
    }

    [Fact]
    public void EmptyValidSlicesAreNumericFailures()
    {
        var value = new AsciiString("1");
        Assert.Throws<FormatException>(() => value.ParseSingle(1, 1));
        Assert.Throws<FormatException>(() => value.ParseDouble(1, 1));
        Assert.False(value.TryParseSingle(1, 1, out _));
        Assert.False(value.TryParseDouble(1, 1, out _));
    }

    [Fact]
    public void SuccessfulDecimalAndHexadecimalSpanParsesDoNotAllocate()
    {
        var decimalValue = new AsciiString("-123.25e1f");
        var hexValue = new AsciiString("0x1.8p1");
        double sum = 0;
        for (int i = 0; i < 1000; i++) sum += ParseAll(decimalValue) + ParseAll(hexValue);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) sum += ParseAll(decimalValue) + ParseAll(hexValue);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal((-1232.5 + 3.0) * 4 * 2000, sum);
    }

    private static double ParseAll(AsciiString value)
    {
        value.TryParseSingle(out float single);
        value.TryParseDouble(out double twice);
        return value.ParseSingle() + value.ParseDouble() + single + twice;
    }
}
