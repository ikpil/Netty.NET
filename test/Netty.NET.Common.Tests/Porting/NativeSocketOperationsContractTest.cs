using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;

namespace Netty.NET.Common.Tests.Porting;

public class NativeSocketOperationsContractTest
{
    [Fact]
    public void JavaPrivilegeAndConnectionFacadesAreRetired()
    {
        Assert.Null(typeof(NetUtil).Assembly.GetType("Netty.NET.Common.Internal.SocketUtils"));
    }

    [Fact]
    public async Task EndpointPortAndCancellationAreSeparateNativeInputs()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endpoint = (IPEndPoint)listener.LocalEndPoint;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Task<Socket> accepting = listener.AcceptAsync(deadline.Token).AsTask();
        await client.ConnectAsync(endpoint, deadline.Token);
        using Socket accepted = await accepting;
        Assert.True(client.Connected);
        Assert.Equal(endpoint.Port, ((IPEndPoint)client.RemoteEndPoint).Port);
        Assert.Equal(client.LocalEndPoint, accepted.RemoteEndPoint);
    }

    [Fact]
    public async Task ConnectionFailurePropagatesInsteadOfPretendingToBePending()
    {
        using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<SocketException>(async () => await client.ConnectAsync(reserved.LocalEndPoint, deadline.Token));
        Assert.False(client.Connected);
    }

    [Fact]
    public async Task NativeAcceptHonorsCancellationWithoutClosingTheListener()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await listener.AcceptAsync(cancelled.Token));
        Assert.False(listener.SafeHandle.IsClosed);
    }

    [Fact]
    public async Task DisposedSocketsRetainTheirNativeFailure()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 1), CancellationToken.None));
    }

    [Fact]
    public void UnresolvedHostsRetainTheirNameUntilTheOwningResolverRuns()
    {
        var endpoint = new DnsEndPoint("netty-port-does-not-exist.invalid", 4321);
        Assert.Equal("netty-port-does-not-exist.invalid", endpoint.Host);
        Assert.Equal(4321, endpoint.Port);
        Assert.Equal(AddressFamily.Unspecified, endpoint.AddressFamily);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DnsEndPoint("localhost", 65536));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void LiteralResolutionRetainsTheNativeAddressFamily(string text)
    {
        Assert.Equal(new[] { IPAddress.Parse(text) }, Dns.GetHostAddresses(text));
    }

    [Fact]
    public void LoopbackDiscoveryHandlesNativeInterfaceFailures()
    {
        var address = new Mock<UnicastIPAddressInformation>();
        address.SetupGet(x => x.Address).Returns(IPAddress.Parse("192.0.2.1"));
        var addresses = new Mock<UnicastIPAddressInformationCollection>();
        addresses.SetupGet(x => x.Count).Returns(1);
        addresses.Setup(x => x[0]).Returns(address.Object);
        addresses.Setup(x => x.GetEnumerator()).Returns(() =>
            ((IEnumerable<UnicastIPAddressInformation>)new[] { address.Object }).GetEnumerator());
        var properties = new Mock<IPInterfaceProperties>();
        properties.SetupGet(x => x.UnicastAddresses).Returns(addresses.Object);
        var iface = new Mock<NetworkInterface>();
        iface.SetupGet(x => x.NetworkInterfaceType).Returns(NetworkInterfaceType.Loopback);
        iface.SetupSequence(x => x.GetIPProperties())
            .Returns(properties.Object).Returns(properties.Object)
            .Throws(new NetworkInformationException(1))
            .Throws(new NetworkInformationException(1));

        var selected = NetUtilInitializations.DetermineLoopback(new[] { iface.Object }, IPAddress.Loopback, IPAddress.IPv6Loopback,
            _ => { iface.Object.GetIPProperties(); return false; });
        Assert.Null(selected.Iface);
        Assert.Equal(IPAddress.Loopback, selected.Address);
    }
}
