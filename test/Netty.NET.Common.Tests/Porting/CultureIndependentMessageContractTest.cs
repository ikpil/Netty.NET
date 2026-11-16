using System;
using System.Globalization;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Porting;

// Java string concatenation, StringBuilder.append and the SLF4J-style MessageFormatter never consult a locale.
// Expected texts were printed by the unmodified pinned MessageFormatter and AsciiString on JDK 21 under en-US,
// ar-SA, sv-SE and de-DE default locales, which all produced the same output.
public class CultureIndependentMessageContractTest
{
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void LoggerTemplatesRenderNumbersLikeTheSource(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("-5 1.0E7 0.5 -3 -2", MessageFormatter.ArrayFormat("{} {} {} {} {}",
                new object[] { -5, 1e7, 0.5f, -3L, (short)-2 }).GetMessage());
            Assert.Equal("[-1, 2][-3][-4][-1][1.0E-4, 2.0][0.5, 1.0E7, -0.0]", MessageFormatter.ArrayFormat("{}{}{}{}{}{}",
                new object[]
                {
                    new[] { -1, 2 }, new long[] { -3 }, new short[] { -4 }, new byte[] { 0xff },
                    new[] { 1e-4f, 2f }, new[] { 0.5, 1e7, -0.0 },
                }).GetMessage());
        });
    }

    [Fact]
    public void LoggerTemplatesRenderBooleansAndNullLikeTheSource()
    {
        // MessageFormatter.deeplyAppendParameter appends "null" for null and Boolean.toString otherwise.
        Assert.Equal("true false null", MessageFormatter.ArrayFormat("{} {} {}", new object[] { true, false, null }).GetMessage());
        Assert.Equal("[true, false]", MessageFormatter.Format("{}", new[] { true, false }).GetMessage());
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    public void IndexMessagesUseAsciiDigitsAndSign(string culture)
    {
        InCulture(culture, () =>
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new AsciiString(new char[4], -1, 2));
            Assert.Null(e.ParamName);
            Assert.Equal("expected: 0 <= start(-1) <= start + length(2) <= value.length(4)", e.Message);
        });
    }

    private static void InCulture(string culture, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
