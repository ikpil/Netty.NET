using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class StringUtilOwsContractTest
{
    private static readonly (string Input, string Expected)[] TrimCases =
    {
        ("", ""), (" \t ", ""), ("\t\t", ""), (" \ta\t ", "a"),
        ("a\t b", "a\t b"), (" \t\r\na\r\n\t ", "\r\na\r\n"),
        ("\t\u00a0a\u00a0 ", "\u00a0a\u00a0"), (" \u0085a\u0085\t", "\u0085a\u0085"),
        ("\t\u2003a\u2003 ", "\u2003a\u2003"), (" \0a\0\t", "\0a\0"),
        ("\t\ud800a\udfff ", "\ud800a\udfff"), (" \t\" a \"\t ", "\" a \""),
        ("\t, ", ","), ("\t\u200ba\u200b ", "\u200ba\u200b")
    };

    private static readonly (string Input, string Expected)[] CsvCases =
    {
        ("", ""), (" \t ", ""), ("\t a,b \t", "\"a,b\""),
        ("\t \"a,b\" \t", "\"a,b\""), ("\t a\"b \t", "\"a\"\"b\""),
        ("\t \"a\"\"b\" \t", "\"a\"\"b\""), ("\t a\r\nb \t", "\"a\r\nb\""),
        ("\t \u00a0a\u00a0 \t", "\u00a0a\u00a0"), ("\t a\t b \t", "a\t b"),
        ("\t \" \t \" \t", "\" \t \""), ("\t \0a \t", "\0a"),
        ("\t \ud800 \t", "\ud800")
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)]
    public void TrimRemovesOnlySpaceAndTabAtTheEdges(int index)
    {
        var row = TrimCases[index];
        string actual = StringUtil.TrimOws(row.Input);
        Assert.Equal(row.Expected, actual);
        if (row.Input == row.Expected) Assert.Same(row.Input, actual);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void CsvTrimmingKeepsQuoteAndEmbeddedWhitespaceSemantics(int index)
    {
        var row = CsvCases[index];
        Assert.Equal(row.Expected, StringUtil.EscapeCsv(row.Input, true));
    }

    [Fact]
    public void TrimPreservesEveryOtherUtf16CodeUnit()
    {
        for (int code = 0; code <= char.MaxValue; code++)
        {
            string text = new((char)code, 1);
            string expected = code is ' ' or '\t' ? "" : text;
            Assert.Equal(expected, StringUtil.TrimOws(" \t" + text + "\t "));
            string actual = StringUtil.TrimOws(text);
            Assert.Equal(expected, actual);
            if (expected.Length != 0) Assert.Same(text, actual);
        }
    }

    [Fact]
    public void UnchangedCsvKeepsTheInputReferenceWithEitherTrimPolicy()
    {
        foreach (string input in new[] { "a\t b", "\u00a0a\u00a0", "\" a,b \"" })
        {
            string value = new(input.AsSpan());
            Assert.Same(value, StringUtil.EscapeCsv(value, true));
            Assert.Same(value, StringUtil.EscapeCsv(value, false));
        }
    }

    [Fact]
    public void NullTrimUsesTheNativeArgumentException()
    {
        var error = Assert.Throws<ArgumentNullException>(() => StringUtil.TrimOws(null));
        Assert.Equal("value", error.ParamName);
    }
}
