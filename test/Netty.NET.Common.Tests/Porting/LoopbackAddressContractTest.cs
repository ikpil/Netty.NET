using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Moq;

namespace Netty.NET.Common.Tests.Porting;

public class LoopbackAddressContractTest
{
    [Theory]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.255")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:127.0.0.2")]
    [InlineData("::ffff:127.255.255.255")]
    [InlineData("::ffff:127.0.0.2%37")]
    [InlineData("::1")]
    [InlineData("::1%37")]
    public void LoopbackAddressesWinBeforeInterfaceTypeAndGlobalLookup(string literal)
    {
        IPAddress address = IPAddress.Parse(literal);
        var iface = Interface(address);
        iface.SetupGet(x => x.NetworkInterfaceType).Throws(new InvalidOperationException("type fallback must not run"));
        var result = NetUtilInitializations.DetermineLoopback(new[] { iface.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback,
            _ => throw new InvalidOperationException("global fallback must not run"));
        Assert.Same(iface.Object, result.Iface);
        Assert.Same(address, result.Address);
        Assert.Equal(IPAddress.Parse(literal).ScopeIdOrZero(), address.ScopeIdOrZero());
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::%37")]
    [InlineData("::127.0.0.2")]
    [InlineData("64:ff9b::7f00:2")]
    [InlineData("::ffff:128.0.0.1")]
    [InlineData("::fffe:7f00:2")]
    public void NonLoopbackPrefixesDoNotPreemptALaterInterface(string literal)
    {
        var ordinary = Interface(IPAddress.Parse(literal));
        var loopback = Interface(IPAddress.Loopback);
        var result = NetUtilInitializations.DetermineLoopback(new[] { ordinary.Object, loopback.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback,
            _ => throw new InvalidOperationException("global fallback must not run"));
        Assert.Same(loopback.Object, result.Iface);
        Assert.Same(IPAddress.Loopback, result.Address);
    }

    [Fact]
    public void TheFirstScopedLoopbackKeepsAddressAndInterfaceOrder()
    {
        IPAddress address = IPAddress.Parse("::1%37");
        var first = Interface(IPAddress.Parse("192.0.2.1"), address, IPAddress.Loopback);
        var second = Interface(IPAddress.IPv6Loopback);
        var result = NetUtilInitializations.DetermineLoopback(new[] { first.Object, second.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback,
            _ => throw new InvalidOperationException("global fallback must not run"));
        Assert.Same(first.Object, result.Iface);
        Assert.Same(address, result.Address);
    }

    [Theory]
    [InlineData("::1", "::1%37", true)]
    [InlineData("::1", "::2%37", false)]
    [InlineData("fe80::1%1", "fe80::1%2", true)]
    [InlineData("fe80::1%1", "fe80::2%2", false)]
    [InlineData("127.0.0.2", "::ffff:127.0.0.2", true)]
    [InlineData("::ffff:127.0.0.2%37", "127.0.0.2", true)]
    [InlineData("::1", "::ffff:127.0.0.1", false)]
    [InlineData("0.0.0.0", "::", false)]
    public void AssignmentLookupUsesJavaAddressBytesWithoutChangingScopes(string target, string candidate, bool assigned)
    {
        IPAddress requested = IPAddress.Parse(target);
        IPAddress observed = IPAddress.Parse(candidate);
        long requestedScope = requested.ScopeIdOrZero(), observedScope = observed.ScopeIdOrZero();
        Assert.Equal(assigned, NetUtilInitializations.IsAddressAssigned(requested,
            new[] { Interface(IPAddress.Parse("192.0.2.1"), observed).Object }));
        Assert.Equal(requestedScope, requested.ScopeIdOrZero());
        Assert.Equal(observedScope, observed.ScopeIdOrZero());
    }

    [Fact]
    public void APositiveAssignmentAvoidsLaterProviderFailuresAndSuppliesIpv6Fallback()
    {
        var assigned = Interface(IPAddress.Parse("::1%37"));
        var later = Interface();
        later.Setup(x => x.GetIPProperties()).Throws(new InvalidOperationException("later provider must not run"));
        var result = NetUtilInitializations.DetermineLoopback(Array.Empty<NetworkInterface>(), IPAddress.Loopback, IPAddress.IPv6Loopback,
            address => NetUtilInitializations.IsAddressAssigned(address, new[] { assigned.Object, later.Object }));
        Assert.Null(result.Iface);
        Assert.Same(IPAddress.IPv6Loopback, result.Address);
        later.Verify(x => x.GetIPProperties(), Times.Never);
    }

    [Fact]
    public void AssignmentProviderFailureStillUsesTheExistingIpv4Fallback()
    {
        var failed = Interface();
        var error = new NetworkInformationException(1);
        failed.Setup(x => x.GetIPProperties()).Throws(error);
        Assert.Same(error, Assert.Throws<NetworkInformationException>(() =>
            NetUtilInitializations.IsAddressAssigned(IPAddress.IPv6Loopback, new[] { failed.Object })));
        var result = NetUtilInitializations.DetermineLoopback(Array.Empty<NetworkInterface>(), IPAddress.Loopback, IPAddress.IPv6Loopback,
            address => NetUtilInitializations.IsAddressAssigned(address, new[] { failed.Object }));
        Assert.Null(result.Iface);
        Assert.Same(IPAddress.Loopback, result.Address);
    }

    [Fact]
    public void NativeBoundaryNullArgumentsNameTheirParameters()
    {
        Assert.Equal("address", Assert.Throws<ArgumentNullException>(() => NetUtilInitializations.IsLoopbackAddress(null)).ParamName);
        Assert.Equal("address", Assert.Throws<ArgumentNullException>(() => NetUtilInitializations.IsAddressAssigned(null, Array.Empty<NetworkInterface>())).ParamName);
        Assert.Equal("interfaces", Assert.Throws<ArgumentNullException>(() => NetUtilInitializations.IsAddressAssigned(IPAddress.IPv6Loopback, null)).ParamName);
    }

    private static Mock<NetworkInterface> Interface(params IPAddress[] values)
    {
        var information = new UnicastIPAddressInformation[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            var info = new Mock<UnicastIPAddressInformation>();
            info.SetupGet(x => x.Address).Returns(values[index]);
            information[index] = info.Object;
        }
        var addresses = new Mock<UnicastIPAddressInformationCollection>();
        addresses.SetupGet(x => x.Count).Returns(values.Length);
        addresses.Setup(x => x.GetEnumerator()).Returns(() => ((IEnumerable<UnicastIPAddressInformation>)information).GetEnumerator());
        var properties = new Mock<IPInterfaceProperties>();
        properties.SetupGet(x => x.UnicastAddresses).Returns(addresses.Object);
        var iface = new Mock<NetworkInterface>();
        iface.Setup(x => x.GetIPProperties()).Returns(properties.Object);
        return iface;
    }
}

internal static class LoopbackAddressTestExtensions
{
    internal static long ScopeIdOrZero(this IPAddress address) => address.AddressFamily == AddressFamily.InterNetworkV6 ? address.ScopeId : 0;
}
