using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Netty.NET.Common.Tests.Porting;

public class IpScopeParsingContractTest
{
    [Theory]
    [InlineData("", -1L)]
    [InlineData("eth0", -1L)]
    [InlineData("netty-no-such-interface", -1L)]
    [InlineData("0", 0L)]
    [InlineData("-0", 0L)]
    [InlineData("+42", 42L)]
    [InlineData("00042", 42L)]
    [InlineData("2147483647", 2147483647L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("4294967295", 4294967295L)]
    [InlineData("4294967296", -1L)]
    [InlineData("-1", -1L)]
    [InlineData("-2147483648", -1L)]
    [InlineData(" 42", -1L)]
    [InlineData("42 ", -1L)]
    [InlineData("\u00a042\u2003", -1L)]
    [InlineData("42\0", -1L)]
    [InlineData("42\0ignored", -1L)]
    [InlineData("\u0664\u0662", -1L)]
    [InlineData("\uff14\uff12", -1L)]
    [InlineData("0x2a", -1L)]
    [InlineData("4,2", -1L)]
    [InlineData("+", -1L)]
    [InlineData("42%1", -1L)]
    public void NumericZonesUseNativeBoundsAndMalformedZonesReturnNull(string zone, long expectedScope)
    {
        foreach (string host in new[] { "fe80::1%" + zone, "[fe80::1%" + zone + "]" })
        {
            IPAddress address = NetUtil.CreateInetAddressFromIpAddressString(host);
            if (expectedScope < 0)
            {
                Assert.Null(address);
            }
            else
            {
                Assert.NotNull(address);
                Assert.Equal(AddressFamily.InterNetworkV6, address.AddressFamily);
                Assert.Equal(expectedScope, address.ScopeId);
                Assert.Equal(IPAddress.Parse("fe80::1").GetAddressBytes(), address.GetAddressBytes());
                Assert.Equal("[fe80::1]:80", NetUtil.ToSocketAddressString(new IPEndPoint(address, 80)));
            }
            // Scope syntax has no bearing on the sixteen wire-address bytes.
            Assert.Equal(IPAddress.Parse("fe80::1").GetAddressBytes(), NetUtil.CreateByteArrayFromIpAddressString(host));
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public void ScopeSyntaxIsInvariantAndDoesNotAdmitCultureSignsOrWhitespace(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        culture.NumberFormat.PositiveSign = "POS";
        culture.NumberFormat.NegativeSign = "NEG";
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal(42L, NetUtil.CreateInetAddressFromIpAddressString("fe80::1%+42").ScopeId);
            Assert.Null(NetUtil.CreateInetAddressFromIpAddressString("fe80::1%POS42"));
            Assert.Null(NetUtil.CreateInetAddressFromIpAddressString("fe80::1%NEG42"));
            Assert.Null(NetUtil.CreateInetAddressFromIpAddressString("fe80::1% 42"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("127.0.0.1", "7F000001", AddressFamily.InterNetwork, 0L)]
    [InlineData("::1", "00000000000000000000000000000001", AddressFamily.InterNetworkV6, 0L)]
    [InlineData("[fe80::1]", "FE800000000000000000000000000001", AddressFamily.InterNetworkV6, 0L)]
    [InlineData("::ffff:192.0.2.128", "00000000000000000000FFFFC0000280", AddressFamily.InterNetworkV6, 0L)]
    [InlineData("[::ffff:192.0.2.128%42]", "00000000000000000000FFFFC0000280", AddressFamily.InterNetworkV6, 42L)]
    public void SuccessfulLiteralParsingOwnsAddressPayloadAndRetainsFamily(string host, string hex, AddressFamily family, long scope)
    {
        IPAddress first = NetUtil.CreateInetAddressFromIpAddressString(host);
        IPAddress second = NetUtil.CreateInetAddressFromIpAddressString(host);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(family, first.AddressFamily);
        Assert.Equal(Convert.FromHexString(hex), first.GetAddressBytes());
        if (family == AddressFamily.InterNetworkV6)
        {
            Assert.Equal(scope, first.ScopeId);
            first.ScopeId = 17;
            Assert.Equal(scope, second.ScopeId);
        }
    }

    [Theory]
    [InlineData("Example.invalid")]
    [InlineData("127.1")]
    [InlineData("0x7f000001")]
    [InlineData("::gg%42")]
    [InlineData("[fe80::1%42")]
    public void NonLiteralAndMalformedAddressInputsRemainParseFailures(string host)
    {
        Assert.Null(NetUtil.CreateInetAddressFromIpAddressString(host));
    }

    [Fact]
    public void NullLiteralUsesNativeArgumentValidation()
    {
        Assert.Equal("ipAddressString", Assert.Throws<ArgumentNullException>(() => NetUtil.CreateInetAddressFromIpAddressString(null)).ParamName);
    }
}
