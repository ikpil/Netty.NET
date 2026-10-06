using System;
using System.Net;
using System.Net.NetworkInformation;
using Moq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class MacProviderIdentityContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValueEqualProviderObjectsRemainIndependentCandidates(bool reverse)
    {
        var local = new ProviderInterface("82abcdef0123");
        var unique = new ProviderInterface("801234567890");
        NetworkInterface[] input = reverse ? new NetworkInterface[] { unique, local } : new NetworkInterface[] { local, unique };
        Assert.Equal(Convert.FromHexString("801234fffe567890"), MacAddressUtil.BestAvailableMac(input));
        Assert.Equal(1, local.HardwareCalls);
        Assert.Equal(1, unique.HardwareCalls);
        Assert.Equal(0, local.EqualityCalls + unique.EqualityCalls + local.HashCalls + unique.HashCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProviderEqualityAndHashCallbacksDoNotParticipateInSelection(bool throwHash)
    {
        var first = new ProviderInterface("82abcdef0123") { ThrowHash = throwHash, ThrowEquality = true };
        var second = new ProviderInterface("801234567890") { ThrowHash = throwHash, ThrowEquality = true };
        Assert.Equal(Convert.FromHexString("801234fffe567890"),
            MacAddressUtil.BestAvailableMac(new NetworkInterface[] { first, second }));
        Assert.Equal(0, first.EqualityCalls + second.EqualityCalls + first.HashCalls + second.HashCalls);
    }

    [Fact]
    public void ChangingValueHashDoesNotTurnTheSameProviderIntoTwoCandidates()
    {
        var iface = new ProviderInterface("801234567890") { ChangingHash = true };
        Assert.Equal(Convert.FromHexString("801234fffe567890"),
            MacAddressUtil.BestAvailableMac(new NetworkInterface[] { iface, iface }));
        Assert.Equal(2, iface.AddressCalls);
        Assert.Equal(1, iface.HardwareCalls);
        Assert.Equal(0, iface.HashCalls + iface.EqualityCalls);
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet)]
    [InlineData(NetworkInterfaceType.Tunnel)]
    [InlineData(NetworkInterfaceType.Loopback)]
    [InlineData(NetworkInterfaceType.Unknown)]
    [InlineData(NetworkInterfaceType.Wireless80211)]
    public void AdapterMetadataCannotSubstituteForTheJavaSubinterfaceFlag(NetworkInterfaceType type)
    {
        var iface = new ProviderInterface("801234567890", type);
        Assert.Equal(Convert.FromHexString("801234fffe567890"),
            MacAddressUtil.BestAvailableMac(new NetworkInterface[] { iface }));
        Assert.Equal(0, iface.MetadataCalls);
    }

    [Fact]
    public void NativeSnapshotDiscoveryRetainsDistinctValueEqualProviderObjects()
    {
        var first = new ProviderInterface("82abcdef0123");
        var second = new ProviderInterface("801234567890");
        var snapshot = NetUtilInitializations.NetworkInterfaces(() => new NetworkInterface[] { first, second });
        Assert.Equal(2, snapshot.Count);
        Assert.Same(first, snapshot[0]);
        Assert.Same(second, snapshot[1]);
        Assert.Equal(Convert.FromHexString("801234fffe567890"), MacAddressUtil.DefaultMachineId(snapshot));
        Assert.Equal(1, second.HardwareCalls);
    }

    [Fact]
    public void AFailedProviderCannotHideItsDistinctValueEqualHealthyPeer()
    {
        var failed = new ProviderInterface("82abcdef0123") { FailHardware = true };
        var healthy = new ProviderInterface("801234567890");
        Assert.Equal(Convert.FromHexString("801234fffe567890"),
            MacAddressUtil.BestAvailableMac(new NetworkInterface[] { failed, healthy }));
        Assert.Equal(1, failed.HardwareCalls);
        Assert.Equal(1, healthy.HardwareCalls);
    }

    private sealed class ProviderInterface : NetworkInterface
    {
        private readonly IPInterfaceProperties properties;
        private readonly PhysicalAddress physical;
        private readonly NetworkInterfaceType type;
        public int AddressCalls, HardwareCalls, EqualityCalls, HashCalls, MetadataCalls;
        public bool ThrowHash, ThrowEquality, ChangingHash, FailHardware;

        public ProviderInterface(string mac, NetworkInterfaceType type = NetworkInterfaceType.Ethernet)
        {
            this.type = type;
            physical = new PhysicalAddress(Convert.FromHexString(mac));
            var info = new Mock<UnicastIPAddressInformation>();
            info.SetupGet(x => x.Address).Returns(IPAddress.Parse("192.0.2.1"));
            var addresses = new Mock<UnicastIPAddressInformationCollection>();
            addresses.SetupGet(x => x.Count).Returns(1);
            addresses.Setup(x => x[0]).Returns(info.Object);
            var ip = new Mock<IPInterfaceProperties>();
            ip.SetupGet(x => x.UnicastAddresses).Returns(addresses.Object);
            properties = ip.Object;
        }

        public override IPInterfaceProperties GetIPProperties() { AddressCalls++; return properties; }
        public override PhysicalAddress GetPhysicalAddress()
        {
            HardwareCalls++;
            if (FailHardware) throw new NetworkInformationException(1);
            return physical;
        }

        public override bool Equals(object obj)
        {
            EqualityCalls++;
            if (ThrowEquality) throw new InvalidOperationException("provider equality must not run");
            return obj is ProviderInterface;
        }

        public override int GetHashCode()
        {
            HashCalls++;
            if (ThrowHash) throw new InvalidOperationException("provider hashing must not run");
            return ChangingHash ? HashCalls : 0;
        }

        public override string Id { get { MetadataCalls++; return "shared-provider-id"; } }
        public override string Name { get { MetadataCalls++; return "eth0:1"; } }
        public override string Description { get { MetadataCalls++; return "virtual adapter"; } }
        public override NetworkInterfaceType NetworkInterfaceType { get { MetadataCalls++; return type; } }
        public override OperationalStatus OperationalStatus { get { MetadataCalls++; return OperationalStatus.Down; } }
    }
}
