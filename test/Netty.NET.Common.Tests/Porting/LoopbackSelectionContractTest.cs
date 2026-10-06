using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using Moq;

namespace Netty.NET.Common.Tests.Porting;

public class LoopbackSelectionContractTest
{
    [Fact]
    public void AddressPassKeepsInterfaceAndAddressOrderAndSkipsTheTypeProbe()
    {
        IPAddress first = IPAddress.Parse("127.0.0.2");
        var iface = Interface(IPAddress.Parse("192.0.2.1"), first, IPAddress.IPv6Loopback);
        iface.SetupGet(x => x.NetworkInterfaceType).Throws(new InvalidOperationException("type probe must not run"));
        var later = Interface(IPAddress.IPv6Loopback);
        var selected = NetUtilInitializations.DetermineLoopback(new[] { iface.Object, later.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback);
        Assert.Same(iface.Object, selected.Iface);
        Assert.Same(first, selected.Address);
    }

    [Fact]
    public void EmptyInterfacesCannotWinTheTypeFallback()
    {
        var empty = Interface();
        empty.SetupGet(x => x.NetworkInterfaceType).Returns(NetworkInterfaceType.Loopback);
        IPAddress address = IPAddress.Parse("192.0.2.9");
        var usable = Interface(address);
        usable.SetupGet(x => x.NetworkInterfaceType).Returns(NetworkInterfaceType.Loopback);
        var selected = NetUtilInitializations.DetermineLoopback(new[] { empty.Object, usable.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback);
        Assert.Same(usable.Object, selected.Iface);
        Assert.Same(address, selected.Address);
    }

    [Fact]
    public void FailedAddressFilteringDoesNotDiscardHealthyInterfaces()
    {
        var failed = Interface();
        failed.Setup(x => x.GetIPProperties()).Throws(new NetworkInformationException(1));
        var healthy = Interface(IPAddress.IPv6Loopback);
        var selected = NetUtilInitializations.DetermineLoopback(new[] { failed.Object, healthy.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback);
        Assert.Same(healthy.Object, selected.Iface);
        Assert.Same(IPAddress.IPv6Loopback, selected.Address);
    }

    [Fact]
    public void FailedAddressScanDoesNotDiscardHealthyInterfaces()
    {
        var failed = Interface(IPAddress.Parse("192.0.2.1"));
        IPInterfaceProperties properties = failed.Object.GetIPProperties();
        failed.SetupSequence(x => x.GetIPProperties()).Returns(properties).Throws(new NetworkInformationException(1));
        var healthy = Interface(IPAddress.Loopback);
        var selected = NetUtilInitializations.DetermineLoopback(new[] { failed.Object, healthy.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback);
        Assert.Same(healthy.Object, selected.Iface);
        Assert.Same(IPAddress.Loopback, selected.Address);
    }

    [Fact]
    public void FailedTypeFallbackContinuesToTheNextUsableInterface()
    {
        var failed = Interface(IPAddress.Parse("192.0.2.1"));
        failed.SetupGet(x => x.NetworkInterfaceType).Throws(new NetworkInformationException(1));
        IPAddress address = IPAddress.Parse("192.0.2.2");
        var healthy = Interface(address);
        healthy.SetupGet(x => x.NetworkInterfaceType).Returns(NetworkInterfaceType.Loopback);
        var selected = NetUtilInitializations.DetermineLoopback(new[] { failed.Object, healthy.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback);
        Assert.Same(healthy.Object, selected.Iface);
        Assert.Same(address, selected.Address);
    }

    [Fact]
    public void ProgrammingFailuresInTheAddressPassRetainTheirIdentity()
    {
        var failure = new InvalidOperationException("provider defect");
        var iface = Interface();
        iface.Setup(x => x.GetIPProperties()).Throws(failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            NetUtilInitializations.DetermineLoopback(new[] { iface.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoMatchingSnapshotInterfaceUsesAnIndependentCurrentAddressLookup(bool assigned)
    {
        int lookups = 0;
        var selected = NetUtilInitializations.DetermineLoopback(Array.Empty<NetworkInterface>(), IPAddress.Loopback, IPAddress.IPv6Loopback,
            address => { Assert.Same(IPAddress.IPv6Loopback, address); lookups++; return assigned; });
        Assert.Null(selected.Iface);
        Assert.Same(assigned ? IPAddress.IPv6Loopback : IPAddress.Loopback, selected.Address);
        Assert.Equal(1, lookups);
    }

    [Fact]
    public void AddressLookupFailureStillProducesTheSuppliedIpv4Fallback()
    {
        var selected = NetUtilInitializations.DetermineLoopback(Array.Empty<NetworkInterface>(), IPAddress.Loopback, IPAddress.IPv6Loopback,
            _ => throw new InvalidOperationException("lookup failed"));
        Assert.Null(selected.Iface);
        Assert.Same(IPAddress.Loopback, selected.Address);
    }

    [Fact]
    public void ASelectedInterfaceDoesNotTriggerTheFallbackLookup()
    {
        var iface = Interface(IPAddress.IPv6Loopback);
        var selected = NetUtilInitializations.DetermineLoopback(new[] { iface.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback,
            _ => throw new InvalidOperationException("lookup must not run"));
        Assert.Same(iface.Object, selected.Iface);
    }

    [Fact]
    public void FatalMemoryFailuresDoNotBecomeSuccessfulIpv4Fallbacks()
    {
        var failure = new OutOfMemoryException("injected memory failure");
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
            NetUtilInitializations.DetermineLoopback(Array.Empty<NetworkInterface>(), IPAddress.Loopback, IPAddress.IPv6Loopback,
                _ => throw failure)));
    }

    [Fact]
    public void TheResultIsAnInternalNativeValueTuple()
    {
        Assert.Equal(typeof(ValueTuple<NetworkInterface, IPAddress>), typeof(NetUtilInitializations)
            .GetMethod("DetermineLoopback", new[] { typeof(IReadOnlyList<NetworkInterface>), typeof(IPAddress), typeof(IPAddress) }).ReturnType);
        Assert.Null(typeof(NetUtil).Assembly.GetType("Netty.NET.Common.NetworkIfaceAndInetAddress"));
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
