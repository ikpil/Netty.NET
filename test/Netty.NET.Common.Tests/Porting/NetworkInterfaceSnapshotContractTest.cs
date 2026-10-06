using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class NetworkInterfaceSnapshotContractTest
{
    [Fact]
    public void DefaultProviderReturnsAnUnmodifiableNativeCollection()
    {
        IReadOnlyList<NetworkInterface> snapshot = NetUtilInitializations.NetworkInterfaces();
        AssertReadOnly(snapshot);
    }

    [Fact]
    public void SnapshotRetainsProviderOrderAndIdentityWithoutRetainingItsArray()
    {
        var first = new NamedInterface("first");
        var second = new NamedInterface("second");
        NetworkInterface[] input = { second, first, second };
        int calls = 0;
        IReadOnlyList<NetworkInterface> snapshot = NetUtilInitializations.NetworkInterfaces(() =>
        {
            calls++;
            return input;
        });
        input[0] = first;
        Assert.Equal(1, calls);
        Assert.Equal(3, snapshot.Count);
        Assert.Same(second, snapshot[0]);
        Assert.Same(first, snapshot[1]);
        Assert.Same(second, snapshot[2]);
        AssertReadOnly(snapshot);
    }

    [Fact]
    public void NetworkInformationFailureDuringRetrievalReturnsAnEmptySnapshot()
    {
        var failure = new NetworkInformationException(1);
        IReadOnlyList<NetworkInterface> snapshot = NetUtilInitializations.NetworkInterfaces(() => throw failure);
        Assert.Empty(snapshot);
        AssertReadOnly(snapshot);
    }

    [Fact]
    public void NetworkInformationFailureDuringEnumerationRetainsTheObservedPrefix()
    {
        var first = new NamedInterface("first");
        IReadOnlyList<NetworkInterface> snapshot = NetUtilInitializations.NetworkInterfaces(() => FailAfter(first));
        Assert.Single(snapshot);
        Assert.Same(first, snapshot[0]);
        AssertReadOnly(snapshot);
    }

    [Fact]
    public void NonNetworkFailuresPropagateTheirOriginalIdentity()
    {
        var failure = new InvalidOperationException("provider defect");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            NetUtilInitializations.NetworkInterfaces(() => throw failure)));
    }

    [Fact]
    public void EnumerationIsDisposedOnSuccessAndNetworkFailure()
    {
        foreach (bool fail in new[] { false, true })
        {
            bool disposed = false;
            IReadOnlyList<NetworkInterface> snapshot = NetUtilInitializations.NetworkInterfaces(() => Enumerate());
            Assert.Single(snapshot);
            Assert.True(disposed);

            IEnumerable<NetworkInterface> Enumerate()
            {
                try
                {
                    yield return new NamedInterface("first");
                    if (fail) throw new NetworkInformationException(1);
                }
                finally
                {
                    disposed = true;
                }
            }
        }
    }

    [Fact]
    public void EmptyProviderAndNullProviderHaveDistinctContracts()
    {
        AssertReadOnly(NetUtilInitializations.NetworkInterfaces(() => Array.Empty<NetworkInterface>()));
        Assert.Equal("getInterfaces", Assert.Throws<ArgumentNullException>(() =>
            NetUtilInitializations.NetworkInterfaces(null)).ParamName);
    }

    [Fact]
    public void PublishedSnapshotsDoNotAffectOtherInitializations()
    {
        var first = new NamedInterface("first");
        var input = new List<NetworkInterface> { first };
        IReadOnlyList<NetworkInterface> prior = NetUtilInitializations.NetworkInterfaces(() => input);
        input.Clear();
        IReadOnlyList<NetworkInterface> current = NetUtilInitializations.NetworkInterfaces(() => input);
        Assert.Single(prior);
        Assert.Same(first, prior[0]);
        Assert.Empty(current);
    }

    private static void AssertReadOnly(IReadOnlyList<NetworkInterface> snapshot)
    {
        var list = Assert.IsAssignableFrom<IList<NetworkInterface>>(snapshot);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(new NamedInterface("extra")));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        if (list.Count != 0)
            Assert.Throws<NotSupportedException>(() => list[0] = new NamedInterface("replacement"));
    }

    private static IEnumerable<NetworkInterface> FailAfter(NetworkInterface first)
    {
        yield return first;
        throw new NetworkInformationException(1);
    }

    private sealed class NamedInterface(string name) : NetworkInterface
    {
        public override string Name => name;
    }
}
