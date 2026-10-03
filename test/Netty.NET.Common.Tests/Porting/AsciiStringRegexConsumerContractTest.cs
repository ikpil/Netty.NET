using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiStringRegexConsumerContractTest
{
    [Theory]
    [InlineData("a", "a", true)]
    [InlineData("a", "xa", false)]
    [InlineData("a", "ax", false)]
    [InlineData("a|ab", "ab", true)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    [InlineData(".*", "ab\n", false)]
    [InlineData("(?s:.*)", "ab\n", true)]
    public void FullMatchingConsumesOnlyTheLogicalView(string pattern, string text, bool expected)
    {
        var value = new AsciiString(Encoding.Latin1.GetBytes("!!" + text + "!!"), 2, text.Length, false);
        // Use native regex syntax/options. Absolute anchors request full matching;
        // grouping preserves alternation and allows backtracking to consume all input.
        var regex = new Regex("\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant);
        Assert.Equal(expected, regex.IsMatch(value.ToString()));
    }

    [Fact]
    public void NativeRegexConsumesLosslessLatin1Views()
    {
        var value = new AsciiString(new byte[] { 9, 0xe9, 0x80, 0xff, 9 }, 1, 3, false);
        var regex = new Regex("\\Aé\\x80\\xff\\z", RegexOptions.CultureInvariant);
        Assert.True(regex.IsMatch(value.ToString()));
    }

    [Fact]
    public void ReusableNativeRegexObservesBackingChangesAfterCacheInvalidation()
    {
        byte[] backing = Encoding.ASCII.GetBytes("!ax!");
        var value = new AsciiString(backing, 1, 2, false);
        var regex = new Regex("\\Aab\\z", RegexOptions.CultureInvariant);
        Assert.False(regex.IsMatch(value.ToString()));
        backing[2] = (byte)'b';
        value.ArrayChanged();
        Assert.True(regex.IsMatch(value.ToString()));
    }

    [Fact]
    public void CallerSelectsNativeCaptureAndTrailingFieldPolicies()
    {
        var value = new AsciiString(Encoding.ASCII.GetBytes("!!a,b,!!"), 2, 4, false);
        Assert.Equal(new[] { "a", ",", "b", ",", "" }, new Regex("(,)").Split(value.ToString()));
        Assert.Equal(new[] { "a", ",", "b," }, new Regex("(,)").Split(value.ToString(), 2));
        Assert.Equal(new[] { "a", "b", "" }, value.ToString().Split(','));
        Assert.Equal(new[] { "a", "b" }, value.ToString().TrimEnd(',').Split(','));
        Assert.Equal(new[] { "a", "b," }, value.ToString().Split(',', 2, StringSplitOptions.None));
    }

    [Fact]
    public void ZeroWidthSplittingUsesNativeRegexSemantics()
    {
        var value = new AsciiString("!ab!").SubSequence(1, 3, false);
        Assert.Equal(new[] { "", "a", "b", "" }, new Regex("").Split(value.ToString()));
    }

    [Fact]
    public void CallerSelectsInvariantCaseAndAsciiCharacterClasses()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var regex = new Regex("\\Ai\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.True(regex.IsMatch(new AsciiString("I").ToString()));
            // Native \w is Unicode; explicit ASCII ranges preserve byte-protocol grammar.
            var latin1 = new AsciiString(new byte[] { 0xe9 });
            Assert.True(new Regex("\\A\\w+\\z").IsMatch(latin1.ToString()));
            Assert.False(new Regex("\\A[A-Za-z0-9_]+\\z").IsMatch(latin1.ToString()));
            // Native dot excludes LF; Java also excludes other line terminators.
            Assert.True(new Regex("\\A.*\\z").IsMatch(new AsciiString("a\r").ToString()));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
