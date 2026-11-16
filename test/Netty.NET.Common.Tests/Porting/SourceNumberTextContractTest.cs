using System;
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

// Expected texts were printed by Double.toString/Float.toString and the unmodified pinned ObjectUtil on JDK 21.
public class SourceNumberTextContractTest
{
    [Theory]
    [InlineData(0x8000000000000000UL, "-0.0")]
    [InlineData(0x3ff0000000000000UL, "1.0")]
    [InlineData(0x3f50624dd2f1a9fcUL, "0.001")]
    [InlineData(0x3f50624dd2f1a9fbUL, "9.999999999999998E-4")]
    [InlineData(0x416312d000000000UL, "1.0E7")]
    [InlineData(0x44b52d02c7e14af6UL, "1.0E23")]
    [InlineData(0x1UL, "4.9E-324")]
    [InlineData(0x2UL, "9.9E-324")]
    [InlineData(0x3UL, "1.5E-323")]
    [InlineData(0x10000000000000UL, "2.2250738585072014E-308")]
    [InlineData(0xfffffffffffffUL, "2.225073858507201E-308")]
    [InlineData(0x7fefffffffffffffUL, "1.7976931348623157E308")]
    [InlineData(0x7ff8000000000000UL, "NaN")]
    [InlineData(0x3fd5555555555555UL, "0.3333333333333333")]
    // .NET "R" does not round-trip these powers of two; the source still prints the shortest round-tripping text.
    [InlineData(0x0410000000000000UL, "4.1045368012983762E-289")]
    [InlineData(0x3e60000000000000UL, "2.9802322387695312E-8")]
    public void DoubleTextMatchesDoubleToString(ulong bits, string expected)
    {
        Assert.Equal(expected, JdkDecimal.DoubleToString(BitConverter.Int64BitsToDouble((long)bits)));
    }

    [Theory]
    [InlineData(0x80000000U, "-0.0")]
    [InlineData(0x3f800000U, "1.0")]
    [InlineData(0x3dcccccdU, "0.1")]
    [InlineData(0x3a83126fU, "0.001")]
    [InlineData(0x38d1b717U, "1.0E-4")]
    [InlineData(0x4b18967fU, "9999999.0")]
    [InlineData(0x501502f9U, "1.0E10")]
    [InlineData(0x1U, "1.4E-45")]
    [InlineData(0x2U, "2.8E-45")]
    [InlineData(0x3U, "4.2E-45")]
    [InlineData(0x800000U, "1.1754944E-38")]
    [InlineData(0x7f7fffffU, "3.4028235E38")]
    [InlineData(0x3eaaaaabU, "0.33333334")]
    [InlineData(0x4b800000U, "1.6777216E7")]
    public void FloatTextMatchesFloatToString(uint bits, string expected)
    {
        Assert.Equal(expected, JdkDecimal.FloatToString(BitConverter.Int32BitsToSingle((int)bits)));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void ObjectUtilMessagesDoNotDependOnTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("d : -0.5 (expected: > 0)", Message(() => ObjectUtil.CheckPositive(-0.5, "d")));
            Assert.Equal("d : -1.0E-4 (expected: >= 0)", Message(() => ObjectUtil.CheckPositiveOrZero(-1e-4, "d")));
            Assert.Equal("d: 1.1 (expected: 0.0-1.0)", Message(() => ObjectUtil.CheckInRange(1.1, 0.0, 1.0, "d")));
            Assert.Equal("d: 5.0 (expected: 0.25-1.0E-4)", Message(() => ObjectUtil.CheckInRange(5.0, 0.25, 1e-4, "d")));
            Assert.Equal("f : -1.4E-45 (expected: > 0)", Message(() => ObjectUtil.CheckPositive(-float.Epsilon, "f")));
            Assert.Equal("f : -1.0E7 (expected: >= 0)", Message(() => ObjectUtil.CheckPositiveOrZero(-1e7f, "f")));
            Assert.Equal("i : -1 (expected: > 0)", Message(() => ObjectUtil.CheckPositive(-1, "i")));
            Assert.Equal("l : -2 (expected: >= 0)", Message(() => ObjectUtil.CheckPositiveOrZero(-2L, "l")));
            Assert.Equal("s : -3 (expected: > 0)", Message(() => ObjectUtil.CheckPositive((short)-3, "s")));
            Assert.Equal("i: -1 (expected: -5--2)", Message(() => ObjectUtil.CheckInRange(-1, -5, -2, "i")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static string Message(Action action) => Assert.Throws<ArgumentException>(action).Message;
}
