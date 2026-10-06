using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using Moq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class MacSelectionContractTest
{
    [Fact]
    public void FailedAddressDiscoveryContinuesWithHealthyInterfaces()
    {
        var failed = Interface("00:12:34:56:78:90", "192.0.2.1");
        failed.Setup(x => x.GetIPProperties()).Throws(new NetworkInformationException(1));
        var healthy = Interface("00:ab:cd:ef:01:23", "192.0.2.2");
        Assert.Equal(MacAddressUtil.ParseMAC("00:ab:cd:ff:fe:ef:01:23"), Select(failed, healthy));
        failed.Verify(x => x.GetPhysicalAddress(), Times.Never);
    }

    [Fact]
    public void FailedHardwareDiscoveryContinuesWithHealthyInterfaces()
    {
        var failed = Interface("00:12:34:56:78:90", "192.0.2.1");
        failed.Setup(x => x.GetPhysicalAddress()).Throws(new NetworkInformationException(1));
        Assert.Equal(MacAddressUtil.ParseMAC("00:ab:cd:ff:fe:ef:01:23"),
            Select(failed, Interface("00:ab:cd:ef:01:23", "192.0.2.2")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammingFailuresRetainTheirIdentity(bool hardware)
    {
        var failure = new InvalidOperationException("provider defect");
        var iface = Interface("00:12:34:56:78:90", "192.0.2.1");
        if (hardware) iface.Setup(x => x.GetPhysicalAddress()).Throws(failure);
        else iface.Setup(x => x.GetIPProperties()).Throws(failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Select(iface)));
    }

    [Theory]
    [InlineData()]
    [InlineData("127.0.0.2", "192.0.2.1")]
    [InlineData("::1", "2001:db8::1")]
    public void EmptyOrFirstLoopbackAddressesAvoidHardwareDiscovery(params string[] addresses)
    {
        var iface = Interface("00:12:34:56:78:90", addresses);
        Assert.Null(Select(iface));
        iface.Verify(x => x.GetPhysicalAddress(), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(6)]
    public void EmptyShortAndOnlyZeroOneMacsCannotWin(int length)
    {
        var iface = Interface(new byte[length], "192.0.2.1");
        Assert.Null(Select(iface));
    }

    [Fact]
    public void MulticastMacCannotWinDespiteItsPublicIpAddress()
    {
        Assert.Null(Select(Interface("81:ab:cd:ef:01:23", "192.0.2.1")));
    }

    [Fact]
    public void GloballyUniqueMacWinsBeforeIpQualityAndLength()
    {
        Assert.Equal(MacAddressUtil.ParseMAC("80:12:34:ff:fe:56:78:90"), Select(
            Interface("82:ab:cd:ef:01:23:45:67", "192.0.2.1"),
            Interface("80:12:34:56:78:90", "169.254.1.1")));
    }

    [Theory]
    [InlineData("0.0.0.0", "224.0.0.1")]
    [InlineData("224.0.0.1", "169.254.1.1")]
    [InlineData("169.254.1.1", "10.1.2.3")]
    [InlineData("10.1.2.3", "192.0.2.1")]
    [InlineData("::", "ff02::1")]
    [InlineData("ff02::1", "fe80::1")]
    [InlineData("fe80::1", "fec0::1")]
    [InlineData("fec0::1", "2001:db8::1")]
    public void IpQualityWinsBeforeMacLength(string worse, string better)
    {
        Assert.Equal(MacAddressUtil.ParseMAC("00:12:34:ff:fe:56:78:90"), Select(
            Interface("00:ab:cd:ef:01:23:45:67", worse), Interface("00:12:34:56:78:90", better)));
    }

    [Fact]
    public void EqualQualityAndLengthRetainTheFirstInterface()
    {
        Assert.Equal(MacAddressUtil.ParseMAC("00:ab:cd:ff:fe:ef:01:23"), Select(
            Interface("00:ab:cd:ef:01:23", "192.0.2.1"), Interface("00:12:34:56:78:90", "192.0.2.2")));
    }

    [Fact]
    public void LongerMacWinsOnlyAfterMacAndIpQualityTie()
    {
        Assert.Equal(MacAddressUtil.ParseMAC("00:12:34:56:78:90:ab:cd"), Select(
            Interface("00:ab:cd:ef:01:23", "192.0.2.1"), Interface("00:12:34:56:78:90:ab:cd", "192.0.2.2")));
    }

    [Fact]
    public void RepeatedInterfaceUpdatesItsAddressWithoutMovingItsInsertionPosition()
    {
        var first = Interface("00:ab:cd:ef:01:23", "10.1.2.3");
        var updated = Interface("00:ab:cd:ef:01:23", "192.0.2.1");
        IPInterfaceProperties initial = first.Object.GetIPProperties();
        first.SetupSequence(x => x.GetIPProperties()).Returns(initial).Returns(updated.Object.GetIPProperties());
        Assert.Equal(MacAddressUtil.ParseMAC("00:ab:cd:ff:fe:ef:01:23"),
            Select(first, Interface("00:12:34:56:78:90", "192.0.2.2"), first));
        first.Verify(x => x.GetPhysicalAddress(), Times.Once);
    }

    [Theory]
    [InlineData(6, "80abcdef0123", "80abcdfffeef0123")]
    [InlineData(7, "80abcdef012345", "80abcdef01234500")]
    [InlineData(8, "80abcdef01234567", "80abcdef01234567")]
    [InlineData(9, "80abcdef0123456789", "80abcdef01234567")]
    public void NormalizationProducesEightOwnedBytesWithoutFlippingTheUniqueBit(int length, string input, string expected)
    {
        byte[] providerBytes = Convert.FromHexString(input);
        Assert.Equal(length, providerBytes.Length);
        var iface = Interface(providerBytes, "192.0.2.1");
        byte[] first = Select(iface);
        Assert.Equal(Convert.FromHexString(expected), first);
        first[0] = 0;
        Assert.Equal(Convert.FromHexString(expected), Select(iface));
        Assert.Equal(Convert.FromHexString(input), providerBytes);
    }

    [Fact]
    public void DefaultMachineIdKeepsTheSelectedHardwareIdentity()
    {
        var iface = Interface("80:ab:cd:ef:01:23", "192.0.2.1");
        Assert.Equal(MacAddressUtil.ParseMAC("80:ab:cd:ff:fe:ef:01:23"),
            MacAddressUtil.DefaultMachineId(new[] { iface.Object }));
        iface.Verify(x => x.GetPhysicalAddress(), Times.Once);
    }

    [Fact]
    public void MissingHardwareProducesAnIndependentEightByteFallbackPerCall()
    {
        var interfaces = Array.Empty<NetworkInterface>();
        byte[] first = MacAddressUtil.DefaultMachineId(interfaces);
        byte[] second = MacAddressUtil.DefaultMachineId(interfaces);
        Assert.Equal(8, first.Length);
        Assert.Equal(8, second.Length);
        Assert.NotSame(first, second);
        byte[] saved = (byte[])second.Clone();
        Array.Fill(first, (byte)0);
        Assert.Equal(saved, second);
    }

    [Fact]
    public void TheTestComparatorIsInternalRatherThanAnExportedApi()
    {
        Assert.Null(typeof(MacAddressUtil).GetMethod("CompareAddresses", new[] { typeof(byte[]), typeof(byte[]) }));
    }

    [Fact]
    public void NullNativeInterfaceInputNamesTheParameter()
    {
        Assert.Equal("interfaces", Assert.Throws<ArgumentNullException>(() => MacAddressUtil.BestAvailableMac(null)).ParamName);
        Assert.Equal("interfaces", Assert.Throws<ArgumentNullException>(() => MacAddressUtil.DefaultMachineId(null)).ParamName);
    }

    private static byte[] Select(params Mock<NetworkInterface>[] interfaces)
    {
        return MacAddressUtil.BestAvailableMac(Array.ConvertAll(interfaces, iface => iface.Object));
    }

    private static Mock<NetworkInterface> Interface(string mac, params string[] addresses)
    {
        return Interface(MacAddressUtil.ParseMAC(mac), addresses);
    }

    private static Mock<NetworkInterface> Interface(byte[] mac, params string[] addresses)
    {
        var collection = new Mock<UnicastIPAddressInformationCollection>();
        collection.SetupGet(x => x.Count).Returns(addresses.Length);
        if (addresses.Length != 0)
        {
            var info = new Mock<UnicastIPAddressInformation>();
            info.SetupGet(x => x.Address).Returns(IPAddress.Parse(addresses[0]));
            collection.Setup(x => x[0]).Returns(info.Object);
        }
        var properties = new Mock<IPInterfaceProperties>();
        properties.SetupGet(x => x.UnicastAddresses).Returns(collection.Object);
        var iface = new Mock<NetworkInterface>();
        iface.Setup(x => x.GetIPProperties()).Returns(properties.Object);
        iface.Setup(x => x.GetPhysicalAddress()).Returns(new PhysicalAddress(mac));
        return iface;
    }
}
