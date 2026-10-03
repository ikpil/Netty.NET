using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiStringIntegerContractTest
{
    public static IEnumerable<object[]> Radices()
    {
        for (int radix = 2; radix <= 36; radix++) yield return new object[] { radix };
    }

    [Theory]
    [InlineData(16, 0, 3)]
    [InlineData(32, 0, 3)]
    [InlineData(64, 0, 3)]
    [InlineData(16, 1, 0)]
    [InlineData(32, 1, 0)]
    [InlineData(64, 1, 0)]
    public void ParsingCannotReadOutsideTheLogicalView(int width, int start, int end)
    {
        var value = new AsciiString(Encoding.ASCII.GetBytes("!123!"), 1, 2, false);
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse(value, width, start, end));
        Assert.Throws<ArgumentOutOfRangeException>(() => TryParse(value, width, start, end, out _));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(3, 3)]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void InvalidSlicesAreArgumentErrorsForEveryWidth(int start, int end)
    {
        var value = new AsciiString("12");
        foreach (int width in new[] { 16, 32, 64 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Parse(value, width, start, end));
            Assert.Throws<ArgumentOutOfRangeException>(() => TryParse(value, width, start, end, out _));
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(int.MaxValue)]
    public void InvalidRadicesAreArgumentErrors(int radix)
    {
        var value = new AsciiString("1");
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseInt16(radix));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseInt32(radix));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.ParseInt64(radix));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseInt16(out _, radix));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseInt32(out _, radix));
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TryParseInt64(out _, radix));
    }

    [Theory]
    [MemberData(nameof(Radices))]
    public void EveryRadixPreservesBoundariesAndRejectsOverflow(int radix)
    {
        foreach (int width in new[] { 16, 32, 64 })
        {
            BigInteger minimum = -(BigInteger.One << (width - 1));
            BigInteger maximum = -minimum - 1;
            foreach (BigInteger expected in new[] { minimum, minimum + 1, -BigInteger.One, BigInteger.Zero, BigInteger.One, maximum })
            {
                string text = Format(expected, radix);
                foreach (string input in new[] { text, text.ToUpperInvariant(), text.Insert(text[0] == '-' ? 1 : 0, "0000") })
                {
                    // Nonzero backing offset and numeric subrange. Sentinels must never be consumed.
                    var value = new AsciiString(Encoding.ASCII.GetBytes("!!x" + input + "y!!"), 2, input.Length + 2, false);
                    Assert.Equal((long)expected, Parse(value, width, 1, input.Length + 1, radix));
                    Assert.True(TryParse(value, width, 1, input.Length + 1, out long actual, radix));
                    Assert.Equal((long)expected, actual);
                }
            }

            foreach (BigInteger invalid in new[] { minimum - 1, maximum + 1, minimum * radix, maximum * radix })
            {
                var value = new AsciiString(Format(invalid, radix));
                Assert.Throws<OverflowException>(() => Parse(value, width, 0, value.Count, radix));
                Assert.False(TryParse(value, width, 0, value.Count, out long result, radix));
                Assert.Equal(0L, result);
            }
        }
    }

    [Fact]
    public void AllLatin1BytesUseThePinnedAsciiDigitGrammar()
    {
        for (int radix = 2; radix <= 36; radix++)
        {
            for (int b = 0; b <= 255; b++)
            {
                // Independent grammar oracle: the digit alphabet, not the production decoder.
                int expected = "0123456789abcdefghijklmnopqrstuvwxyz".IndexOf(char.ToLowerInvariant((char)b));
                bool valid = expected >= 0 && expected < radix;
                var value = new AsciiString(new byte[] { 255, (byte)b, 255 }, 1, 1, false);
                foreach (int width in new[] { 16, 32, 64 })
                {
                    Assert.Equal(valid, TryParse(value, width, 0, 1, out long result, radix));
                    Assert.Equal(valid ? expected : 0L, result);
                    if (valid) Assert.Equal(expected, Parse(value, width, 0, 1, radix));
                    else Assert.Throws<FormatException>(() => Parse(value, width, 0, 1, radix));
                }
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("\t1")]
    [InlineData("1\n")]
    [InlineData("--1")]
    [InlineData("1-2")]
    [InlineData("1.0")]
    [InlineData("1,000")]
    [InlineData("0x10")]
    [InlineData("1_000")]
    [InlineData("1\0")]
    [InlineData("é")]
    public void MalformedNumbersReturnFalseAndParseThrowsFormat(string input)
    {
        var value = new AsciiString(input);
        Assert.False(value.TryParseInt16(out short small));
        Assert.False(value.TryParseInt32(out int medium));
        Assert.False(value.TryParseInt64(out long large));
        Assert.Equal((short)0, small);
        Assert.Equal(0, medium);
        Assert.Equal(0L, large);
        Assert.Throws<FormatException>(() => value.ParseInt16());
        Assert.Throws<FormatException>(() => value.ParseInt32());
        Assert.Throws<FormatException>(() => value.ParseInt64());
    }

    [Fact]
    public void EmptySliceAndNegativeZeroRespectTheLogicalView()
    {
        var value = new AsciiString("!-0!").subSequence(1, 3, false);
        Assert.Equal((short)0, value.ParseInt16());
        Assert.Equal(0, value.ParseInt32());
        Assert.Equal(0L, value.ParseInt64());
        foreach (int width in new[] { 16, 32, 64 })
        {
            Assert.True(TryParse(value, width, 0, 2, out long result));
            Assert.Equal(0L, result);
            Assert.False(TryParse(value, width, 2, 2, out result));
            Assert.Equal(0L, result);
            Assert.Throws<FormatException>(() => Parse(value, width, 2, 2));
        }
    }

    [Fact]
    public void ParsingIsIndependentOfCurrentCultureAndTheExistingConsumerUsesTheNativeApi()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "~";
            culture.NumberFormat.PositiveSign = "!";
            CultureInfo.CurrentCulture = culture;
            Assert.Equal((short)-12, new AsciiString("-12").ParseInt16());
            Assert.Equal(-12, new AsciiString("-12").ParseInt32());
            Assert.Equal(-12L, new AsciiString("-12").ParseInt64());
            Assert.False(new AsciiString("~12").TryParseInt64(out _));
            Assert.Equal(long.MinValue, new AsciiString("-9223372036854775808").ParseInt64());
            Assert.Equal(255L, new AsciiString("ff").ParseInt64(16));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RepeatedSuccessfulParsesDoNotAllocate()
    {
        var value = new AsciiString("-12345");
        long sum = 0;
        for (int i = 0; i < 1000; i++) sum += ParseAllWidths(value);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) sum += ParseAllWidths(value);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
        Assert.Equal(-12345L * 6 * 2000, sum);
    }

    private static long ParseAllWidths(AsciiString value)
    {
        long sum = value.ParseInt16() + value.ParseInt32() + value.ParseInt64();
        value.TryParseInt16(out short small);
        value.TryParseInt32(out int medium);
        value.TryParseInt64(out long large);
        return sum + small + medium + large;
    }

    // BigInteger is only a test reference; production uses fixed-width generic math.
    private static string Format(BigInteger value, int radix)
    {
        if (value.IsZero) return "0";
        bool negative = value.Sign < 0;
        value = BigInteger.Abs(value);
        string result = "";
        while (value > 0)
        {
            value = BigInteger.DivRem(value, radix, out BigInteger remainder);
            result = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)remainder] + result;
        }
        return negative ? "-" + result : result;
    }

    private static long Parse(AsciiString value, int width, int start, int end, int radix = 10) => width switch
    {
        16 => value.ParseInt16(start, end, radix),
        32 => value.ParseInt32(start, end, radix),
        _ => value.ParseInt64(start, end, radix)
    };

    private static bool TryParse(AsciiString value, int width, int start, int end, out long result, int radix = 10)
    {
        if (width == 16)
        {
            bool success = value.TryParseInt16(start, end, out short parsed, radix);
            result = parsed;
            return success;
        }
        if (width == 32)
        {
            bool success = value.TryParseInt32(start, end, out int parsed, radix);
            result = parsed;
            return success;
        }
        return value.TryParseInt64(start, end, out result, radix);
    }
}
