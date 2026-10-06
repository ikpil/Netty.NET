using System;
using System.Net;
using System.Net.NetworkInformation;
using Moq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class MacScoringContractTest
{
    [Theory]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.0", false)]
    [InlineData("239.255.255.255", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("192.0.2.2", true)]
    public void MappedIpv4RetainsItsNativeIpv4PriorityBeforeTheMacLengthTie(string literal, bool candidateWins)
    {
        IPAddress native = IPAddress.Parse(literal);
        AssertCandidatePriority(native, candidateWins);
        IPAddress mapped = native.MapToIPv6();
        Assert.True(mapped.IsIPv4MappedToIPv6);
        AssertCandidatePriority(mapped, candidateWins);
    }

    [Theory]
    [InlineData("::ffff:127.0.0.2")]
    [InlineData("::ffff:127.255.255.255")]
    [InlineData("::ffff:127.0.0.2%37")]
    [InlineData("::1%37")]
    public void MappedLoopbackIsFilteredBeforeHardwareDiscovery(string literal)
    {
        var candidate = Interface(IPAddress.Parse(literal), "001234567890abcd");
        Assert.Null(MacAddressUtil.BestAvailableMac(new[] { candidate.Object }));
        candidate.Verify(x => x.GetPhysicalAddress(), Times.Never);
    }

    [Theory]
    [InlineData("::", false)]
    [InlineData("::%37", false)]
    [InlineData("ff02::1%37", false)]
    [InlineData("fe80::1%37", false)]
    [InlineData("febf::1", false)]
    [InlineData("fec0::1%37", false)]
    [InlineData("feff::1", false)]
    [InlineData("fc00::1", true)]
    [InlineData("fdff::1", true)]
    [InlineData("::10.1.2.3", true)]
    [InlineData("64:ff9b::a01:203", true)]
    [InlineData("::fffe:a01:203", true)]
    [InlineData("::ffff:10.1.2.3%37", false)]
    public void NativeIpv6PrefixesAndScopesKeepTheOriginalCategory(string literal, bool candidateWins)
    {
        AssertCandidatePriority(IPAddress.Parse(literal), candidateWins);
    }

    private static void AssertCandidatePriority(IPAddress address, bool candidateWins)
    {
        var current = Interface(IPAddress.Parse("192.0.2.1"), "00abcdef0123");
        var candidate = Interface(address, "001234567890abcd");
        byte[] selected = MacAddressUtil.BestAvailableMac(new[] { current.Object, candidate.Object });
        Assert.Equal(Convert.FromHexString(candidateWins ? "001234567890abcd" : "00abcdfffeef0123"), selected);
    }

    private static Mock<NetworkInterface> Interface(IPAddress address, string mac)
    {
        var information = new Mock<UnicastIPAddressInformation>();
        information.SetupGet(x => x.Address).Returns(address);
        var addresses = new Mock<UnicastIPAddressInformationCollection>();
        addresses.SetupGet(x => x.Count).Returns(1);
        addresses.Setup(x => x[0]).Returns(information.Object);
        var properties = new Mock<IPInterfaceProperties>();
        properties.SetupGet(x => x.UnicastAddresses).Returns(addresses.Object);
        var iface = new Mock<NetworkInterface>();
        iface.Setup(x => x.GetIPProperties()).Returns(properties.Object);
        iface.Setup(x => x.GetPhysicalAddress()).Returns(new PhysicalAddress(Convert.FromHexString(mac)));
        return iface;
    }
}
