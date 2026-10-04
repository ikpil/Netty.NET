using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class StringBoundaryContractTest
{
    [Theory]
    [InlineData('\u0085', false)]
    [InlineData('\u00a0', false)]
    [InlineData('\u2007', false)]
    [InlineData('\u202f', false)]
    [InlineData('\u001c', true)]
    [InlineData('\u001d', true)]
    [InlineData('\u001e', true)]
    [InlineData('\u001f', true)]
    public void DelimiterPolicyPreservesCharactersWhereJavaAndClrWhitespaceDiffer(char value, bool whitespace)
    {
        string input = "\t" + value + "x";
        Assert.Equal(whitespace ? 1 : -1, StringUtil.IndexOfWhiteSpace(input, 1));
        Assert.Equal(whitespace ? 2 : 1, StringUtil.IndexOfNonWhiteSpace(input, 1));
        Assert.Equal(-1, StringUtil.IndexOfWhiteSpace(input, 2));
        Assert.Equal(2, StringUtil.IndexOfNonWhiteSpace(input, 2));
    }

    [Fact]
    public void ResolverStyleLabelScanningKeepsNonBreakingCharactersInsideTokens()
    {
        const string label = "nameserver";
        foreach (char separator in new[] { '\u0085', '\u00a0', '\u2007', '\u202f' })
        {
            string line = label + separator + "127.0.0.1";
            int start = StringUtil.IndexOfNonWhiteSpace(line, label.Length);
            Assert.Equal(label.Length, start);
            Assert.Equal(-1, StringUtil.IndexOfWhiteSpace(line, start));
            Assert.False(NetUtil.IsValidIpV4Address(line.Substring(start)));
        }
        const string ordinary = "nameserver\u001f127.0.0.1 # note";
        int addressStart = StringUtil.IndexOfNonWhiteSpace(ordinary, label.Length);
        int addressEnd = StringUtil.IndexOfWhiteSpace(ordinary, addressStart);
        Assert.Equal("127.0.0.1", ordinary.Substring(addressStart, addressEnd - addressStart));
        Assert.Equal('#', ordinary[StringUtil.IndexOfNonWhiteSpace(ordinary, addressEnd)]);
    }

    [Fact]
    public void NativeStringArgumentsAndNegativeSearchOffsetsRejectExplicitly()
    {
        Assert.Equal("seq", Assert.Throws<ArgumentNullException>(() => StringUtil.IndexOfWhiteSpace(null, 0)).ParamName);
        Assert.Equal("seq", Assert.Throws<ArgumentNullException>(() => StringUtil.IndexOfNonWhiteSpace(null, int.MaxValue)).ParamName);
        foreach (int offset in new[] { -1, int.MinValue })
        {
            Assert.Equal("offset", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.IndexOfWhiteSpace("", offset)).ParamName);
            Assert.Equal("offset", Assert.Throws<ArgumentOutOfRangeException>(() => StringUtil.IndexOfNonWhiteSpace(" \tx", offset)).ParamName);
        }
        Assert.Equal("s", Assert.Throws<ArgumentNullException>(() => StringUtil.EndsWith(null, '.')).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => StringUtil.SubstringBefore(null, ':')).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => StringUtil.SubstringAfter(null, ':')).ParamName);
    }

    [Fact]
    public void EndAndOversizeOffsetsKeepTheOriginalNoMatchSentinel()
    {
        foreach (int offset in new[] { 0, 1, int.MaxValue })
        {
            Assert.Equal(-1, StringUtil.IndexOfWhiteSpace("", offset));
            Assert.Equal(-1, StringUtil.IndexOfNonWhiteSpace("", offset));
        }
        foreach (int offset in new[] { 3, 4, int.MaxValue })
        {
            Assert.Equal(-1, StringUtil.IndexOfWhiteSpace("x\t ", offset));
            Assert.Equal(-1, StringUtil.IndexOfNonWhiteSpace("x\t ", offset));
        }
    }

    [Fact]
    public void SurrogatesAndCharacterSuffixesUseUtf16CodeUnits()
    {
        for (int codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
            Assert.Equal(codeUnit >= 0xd800 && codeUnit <= 0xdfff, StringUtil.IsSurrogate((char)codeUnit));
        Assert.True(StringUtil.EndsWith("example.org.", '.'));
        Assert.False(StringUtil.EndsWith("example.org\uff0e", '.'));
        Assert.False(StringUtil.EndsWith("", '\0'));
        Assert.True(StringUtil.EndsWith("x\0", '\0'));
        Assert.True(StringUtil.EndsWith("\ud83d\ude00", '\ude00'));
        Assert.Equal("", StringUtil.SubstringBefore(":x", ':'));
        Assert.Equal("", StringUtil.SubstringAfter("x:", ':'));
        Assert.Null(StringUtil.SubstringBefore("x", ':'));
        Assert.Null(StringUtil.SubstringAfter("x", ':'));
    }
}
