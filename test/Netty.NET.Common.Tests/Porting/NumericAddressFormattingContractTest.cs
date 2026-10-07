using System;
using System.Net;
using System.Net.Sockets;

namespace Netty.NET.Common.Tests.Porting;

public class NumericAddressFormattingContractTest
{
    [Theory]
    [InlineData("192.0.2.1", "192.0.2.1")]
    [InlineData("255.255.255.255", "255.255.255.255")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("fe80::1%4294967295", "fe80::1")]
    [InlineData("::ffff:192.0.2.128%4294967295", "::ffff:c000:280")]
    [InlineData("ffff:ffff:ffff:ffff:0:5efe:ffff:ffff%4294967295", "ffff:ffff:ffff:ffff:0:5efe:ffff:ffff")]
    public void AddressAndSocketFormattingUseTheNativeValue(string literal, string expected)
    {
        foreach (bool throws in new[] { false, true })
        {
            IPAddress reference = IPAddress.Parse(literal);
            DisplayAddress address = Create(reference, throws);
            Assert.Equal(expected, NetUtil.ToAddressString(address));
            string mapped = address.IsIPv4MappedToIPv6 ? "::ffff:192.0.2.128" : expected;
            Assert.Equal(mapped, NetUtil.ToAddressString(address, true));
            string socket = address.AddressFamily == AddressFamily.InterNetwork
                ? expected + ":443" : "[" + expected + "]:443";
            Assert.Equal(socket, NetUtil.ToSocketAddressString(new IPEndPoint(address, 443)));
            Assert.Equal(0, address.DisplayCalls);
            Assert.Equal(reference.GetAddressBytes(), address.GetAddressBytes());
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                Assert.Equal(reference.ScopeId, address.ScopeId);
        }
    }

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("255.255.255.255")]
    [InlineData("2001:db8::1")]
    [InlineData("fe80::1%4294967295")]
    [InlineData("::ffff:192.0.2.128%4294967295")]
    [InlineData("ffff:ffff:ffff:ffff:0:5efe:ffff:ffff%4294967295")]
    public void ResolvedHostFormattingUsesNativeNumericTextIncludingScope(string literal)
    {
        IPAddress reference = IPAddress.Parse(literal);
        foreach (bool throws in new[] { false, true })
        {
            DisplayAddress address = Create(reference, throws);
            Assert.Equal(reference.ToString(), NetUtil.GetHostname(new IPEndPoint(address, 443)));
            Assert.Equal(0, address.DisplayCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormattingReadsCurrentNativeStateAfterMutation(bool ipv6)
    {
        IPAddress reference = IPAddress.Parse(ipv6 ? "fe80::1%42" : "192.0.2.1");
        DisplayAddress address = Create(reference, throws: true);
        EndPoint endpoint = new IPEndPoint(address, 443);
        string before = NetUtil.GetHostname(endpoint);
        if (ipv6)
        {
            address.ScopeId = reference.ScopeId = uint.MaxValue;
        }
        else
        {
            // Exercise the native setter without introducing an obsolete-call warning.
            typeof(IPAddress).GetProperty("Address").SetValue(address, 0L);
            typeof(IPAddress).GetProperty("Address").SetValue(reference, 0L);
        }
        string after = NetUtil.GetHostname(endpoint);
        Assert.NotEqual(before, after);
        Assert.Equal(reference.ToString(), after);
        Assert.Equal(ipv6 ? "fe80::1" : "0.0.0.0", NetUtil.ToAddressString(address));
        Assert.Equal(0, address.DisplayCalls);
    }

    private static DisplayAddress Create(IPAddress reference, bool throws) =>
        reference.AddressFamily == AddressFamily.InterNetworkV6
            ? new DisplayAddress(reference.GetAddressBytes(), reference.ScopeId, throws)
            : new DisplayAddress(reference.GetAddressBytes(), throws);

    private sealed class DisplayAddress : IPAddress
    {
        private readonly bool throws;
        public int DisplayCalls { get; private set; }

        public DisplayAddress(byte[] bytes, bool throws) : base(bytes) => this.throws = throws;
        public DisplayAddress(byte[] bytes, long scope, bool throws) : base(bytes, scope) => this.throws = throws;

        public override string ToString()
        {
            DisplayCalls++;
            if (throws) throw new InvalidOperationException("Display override must not format a numeric address.");
            return "display.example.invalid";
        }
    }
}
