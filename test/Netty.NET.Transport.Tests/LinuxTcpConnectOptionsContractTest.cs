using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.LinuxTcpMd5SourceTest;

namespace Netty.NET.Transport.Tests;

public class LinuxTcpConnectOptionsContractTest
{
    [Fact]
    public async Task BootstrapRejectsAnUnsupportedAuthenticationOption()
    {
        var token = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        try
        {
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(() => channel)
                .Handler(new ChannelInboundHandlerAdapter())
                .Option(ChannelOption.TCP_MD5SIG, Keys(IPAddress.Loopback, "abc"u8.ToArray()));
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => bootstrap.RegisterAsync(token));
            Assert.False(channel.IsOpen);
        }
        finally { await group.StopAsync(); }
    }

    [Fact]
    public Task InvalidReplacementLeavesTheOriginalKeyAndEmptyReplacementRestoresUnauthenticatedConnect() =>
        WithServer(async (group, server, token) =>
        {
            var initial = Keys(IPAddress.Loopback, "abc"u8.ToArray());
            byte[] next = "def"u8.ToArray();
            SetServerKeys(server, initial);
            Assert.Throws<ArgumentException>(() => server.Config.SetOption(ChannelOption.TCP_MD5SIG,
                new Dictionary<IPAddress, byte[]> { [IPAddress.Loopback] = next, [IPAddress.Parse("127.0.0.2")] = [] }));
            Assert.Throws<ArgumentException>(() => server.Config.SetOption(ChannelOption.TCP_MD5SIG,
                Keys(IPAddress.Loopback, new byte[81])));
            Assert.False(server.Config.TryGetOption(ChannelOption.TCP_MD5SIG, out _));
            Assert.DoesNotContain(ChannelOption.TCP_MD5SIG, server.Config.GetOptions().Keys);

            await (await NewClient(group).Option(ChannelOption.TCP_MD5SIG, initial)
                .ConnectAsync(server.LocalAddress, cancellationToken: token)).CloseAsync();

            var replacement = Keys(IPAddress.Loopback, next);
            SetServerKeys(server, replacement);
            await (await NewClient(group).Option(ChannelOption.TCP_MD5SIG, replacement)
                .ConnectAsync(server.LocalAddress, cancellationToken: token)).CloseAsync();

            SetServerKeys(server, new Dictionary<IPAddress, byte[]>());
            await (await NewClient(group).ConnectAsync(server.LocalAddress, cancellationToken: token)).CloseAsync();
        });

    [Fact]
    public Task MatchingIpv6KeyAuthenticatesAConnection() => WithServer(async (group, server, token) =>
    {
        var keys = Keys(IPAddress.IPv6Loopback, "abc"u8.ToArray());
        SetServerKeys(server, keys);
        var client = await NewClient(group).Option(ChannelOption.TCP_MD5SIG, keys)
            .ConnectAsync(server.LocalAddress, cancellationToken: token);
        await client.CloseAsync();
    }, ipv6: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAddressNoPortAssignsThePortWhenConnecting(bool ipv6)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("IP_BIND_ADDRESS_NO_PORT requires Linux.");
        if (ipv6 && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 unavailable.");
        var token = TestContext.Current.CancellationToken;
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var local = new IPEndPoint(address, 0);
        using var listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(local);
        listener.Listen(1);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var client = await new Bootstrap().Group(group)
                .ChannelFactory(() => new TcpSocketChannel(address.AddressFamily))
                .Option(ChannelOption.IP_BIND_ADDRESS_NO_PORT, true)
                .Handler(new ChannelInboundHandlerAdapter()).BindAsync(local, token);
            Assert.True(client.Config.TryGetOption(ChannelOption.IP_BIND_ADDRESS_NO_PORT, out bool enabled));
            Assert.True(enabled);
            Assert.Equal(local, client.LocalAddress);
            await client.ConnectAsync(listener.LocalEndPoint, cancellationToken: token);
            using var accepted = await listener.AcceptAsync(token);
            var assigned = Assert.IsType<IPEndPoint>(client.LocalAddress);
            Assert.Equal(address, assigned.Address);
            Assert.True(assigned.Port > 0);
            Assert.Equal(accepted.RemoteEndPoint, assigned);
        }
        finally { await group.StopAsync(); }
    }
}
