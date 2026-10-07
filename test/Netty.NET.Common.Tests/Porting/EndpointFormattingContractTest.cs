using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Netty.NET.Common.Tests.Porting;

public class EndpointFormattingContractTest
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public void HostPortTextUsesAsciiSignsRegardlessOfCulture(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        culture.NumberFormat.NegativeSign = "NEG";
        try
        {
            CultureInfo.CurrentCulture = culture;
            var ports = new (int Value, string Text)[]
            {
                (-1, "-1"), (int.MinValue, "-2147483648"), (0, "0"),
                (65535, "65535"), (65536, "65536"), (int.MaxValue, "2147483647")
            };
            foreach (var port in ports)
            {
                Assert.Equal("Example.invalid:" + port.Text, NetUtil.ToSocketAddressString("Example.invalid", port.Value));
                Assert.Equal("[2001:DB8::1]:" + port.Text, NetUtil.ToSocketAddressString("2001:DB8::1", port.Value));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void NullHostUsesNativeArgumentValidation()
    {
        Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => NetUtil.ToSocketAddressString(null, 80)).ParamName);
    }

    [Theory]
    [InlineData("127.0.0.1", 0, "127.0.0.1:0")]
    [InlineData("192.0.2.1", 65535, "192.0.2.1:65535")]
    [InlineData("::1", 80, "[::1]:80")]
    [InlineData("fe80::1%42", 8080, "[fe80::1]:8080")]
    [InlineData("::ffff:192.0.2.128", 1234, "[::ffff:c000:280]:1234")]
    [InlineData("2001:0DB8:0:0:1:0:0:1", 443, "[2001:db8::1:0:0:1]:443")]
    public void ResolvedAddressesKeepCanonicalFormattingAndNativeHostText(string host, int port, string expected)
    {
        var address = IPAddress.Parse(host);
        var endpoint = new IPEndPoint(address, port);
        Assert.Equal(expected, NetUtil.ToSocketAddressString(endpoint));
        Assert.Equal(address.ToString(), NetUtil.GetHostname(endpoint));
        Assert.Same(address, endpoint.Address);
        Assert.Equal(port, endpoint.Port);
    }

    [Fact]
    public void PublicEndpointApiAcceptsTheNativeBaseType()
    {
        foreach (string name in new[] { "ToSocketAddressString", "GetHostname" })
        {
            MethodInfo method = typeof(NetUtil).GetMethod(name, new[] { typeof(EndPoint) });
            Assert.NotNull(method);
            Assert.Equal(typeof(string), method.ReturnType);
            Assert.True(method.IsStatic);
        }
    }

    [Theory]
    [InlineData("Example.invalid", "Example.invalid:443")]
    [InlineData("bücher.invalid", "bücher.invalid:443")]
    [InlineData("192.0.2.1", "192.0.2.1:443")]
    [InlineData("2001:DB8::1", "[2001:DB8::1]:443")]
    [InlineData("[2001:DB8::1]", "[2001:DB8::1]:443")]
    [InlineData("fe80::1%42", "[fe80::1%42]:443")]
    [InlineData("[fe80::1%42]", "[fe80::1%42]:443")]
    [InlineData("not:an:ip", "not:an:ip:443")]
    public void UnresolvedEndpointsRetainHostTextWithoutResolution(string host, string expected)
    {
        foreach (var family in new[] { AddressFamily.Unspecified, AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
        {
            EndPoint endpoint = new DnsEndPoint(host, 443, family);
            Assert.Equal(expected, NetUtil.ToSocketAddressString(endpoint));
            Assert.Equal(host, NetUtil.GetHostname(endpoint));
            Assert.Equal(family, endpoint.AddressFamily);
            Assert.Equal(host, ((DnsEndPoint)endpoint).Host);
        }
    }

    [Fact]
    public void EndpointArgumentErrorsDoNotReadUnknownEndpointMembers()
    {
        Assert.Equal("addr", Assert.Throws<ArgumentNullException>(() => NetUtil.ToSocketAddressString((EndPoint)null)).ParamName);
        Assert.Equal("addr", Assert.Throws<ArgumentNullException>(() => NetUtil.GetHostname(null)).ParamName);
        EndPoint unsupported = new UnsupportedEndpoint();
        Assert.Equal("addr", Assert.Throws<ArgumentException>(() => NetUtil.ToSocketAddressString(unsupported)).ParamName);
        Assert.Equal("addr", Assert.Throws<ArgumentException>(() => NetUtil.GetHostname(unsupported)).ParamName);
    }

    private sealed class UnsupportedEndpoint : EndPoint
    {
        public override AddressFamily AddressFamily => throw new InvalidOperationException("Unexpected family query.");
        public override string ToString() => throw new InvalidOperationException("Unexpected endpoint formatting.");
    }
}
