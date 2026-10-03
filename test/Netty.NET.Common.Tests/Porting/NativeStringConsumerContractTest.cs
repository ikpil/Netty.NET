using System;
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class NativeStringConsumerContractTest
{
    [Theory]
    [InlineData("abc", "zbc", 2, true)]
    [InlineData("abc", "zbc", 3, false)]
    [InlineData("abc", "abc", 4, false)]
    [InlineData("abc", "abc", -1, false)]
    [InlineData("abc", "abc", int.MinValue, false)]
    [InlineData("abc", "abc", int.MaxValue, false)]
    [InlineData("abc", "ABC", 2, false)]
    [InlineData("", "", 0, true)]
    [InlineData("abc", "", 0, true)]
    [InlineData("", "abc", 1, false)]
    [InlineData(null, "abc", 0, false)]
    [InlineData("abc", null, 0, false)]
    [InlineData("x\0z", "y\0z", 2, true)]
    [InlineData("a\ud800", "b\ud800", 1, true)]
    public void SuffixComparisonUsesCodeUnitsAndRejectsInvalidLengths(string text, string suffix, int length, bool expected)
    {
        Assert.Equal(expected, StringUtil.CommonSuffixOfLength(text, suffix, length));
    }

    [Theory]
    [InlineData("*.example.com", "example.com", true)]
    [InlineData("*.example.com", "a.example.com", true)]
    [InlineData("*.example.com", "a.b.example.com", true)]
    [InlineData("*.example.com", "example", true)]
    [InlineData("*.example.com", "", true)]
    [InlineData("*.example.com", "example.com.extra", false)]
    [InlineData("*.example.com", "aexample.com", false)]
    [InlineData("*.example.com", "EXAMPLE.COM", false)]
    [InlineData("example.com", "example.com", true)]
    [InlineData("example.com", null, false)]
    public void DomainComparisonRetainsPinnedPrefixAndSuffixRules(string pattern, string host, bool expected)
    {
        Assert.Equal(expected, DomainNameMapping<object>.Matches(pattern, host));
    }

    [Theory]
    [InlineData("111.22.")]
    [InlineData("111.22.3.")]
    [InlineData("1.1.1..")]
    [InlineData("111.22.%")]
    public void TruncatedIpv4AddressesReturnFalseWithoutOutOfRangeSearch(string address)
    {
        Assert.False(NetUtil.IsValidIpV4Address(address));
    }

    [Theory]
    [InlineData("::ffff:1.")]
    [InlineData("[::ffff:1.]")]
    [InlineData("::ffff:111.22.")]
    [InlineData("[::ffff:111.22.%1]")]
    public void TruncatedEmbeddedIpv4AddressesReturnFalseWithoutOutOfRangeSearch(string address)
    {
        Assert.False(NetUtil.IsValidIpV6Address(address));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void IgnorableCharactersCannotTurnLiteralDomainPatternsIntoWildcards(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.False(DomainNameMapping<object>.Matches("*\u00ad.example", "a.example"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
