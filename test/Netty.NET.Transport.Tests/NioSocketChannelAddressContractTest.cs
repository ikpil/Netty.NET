using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// CLR supplemental: NioSocketChannel.remoteAddress0() and localAddress0() return javaChannel().socket()
// .getRemoteSocketAddress() and getLocalSocketAddress(), which are the SocketChannelImpl.remoteAddress and localAddress
// fields. The JDK records the remote address when the channel is accepted and when it begins to connect (after
// checkRemote replaced a wildcard address with loopback), and the local address when the channel is accepted, bound or
// its connect completes; close clears neither. Socket.RemoteEndPoint and LocalEndPoint throw once the socket is closed,
// so these cases read the channel under test only after close, before any earlier read could fill AbstractChannel's
// cache.
#pragma warning disable xUnit1051 // Independently owned channel and group teardown.
public class NioSocketChannelAddressContractTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConnectedChannelKeepsRemoteAddressAfterClose()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannel server = null, client = null;

        try
        {
            server = await NewServer(group, IPAddress.Loopback, null);
            client = await NewClient(group).ConnectAsync(server.LocalAddress).WaitAsync(Timeout);
            await client.CloseAsync().WaitAsync(Timeout);
            Assert.False(client.IsOpen);
            Assert.Equal(server.LocalAddress, client.RemoteAddress);
        }
        finally
        {
            await CloseAll(group, client, server);
        }
    }

    [Fact]
    public async Task AcceptedChannelKeepsRemoteAddressAfterClose()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;

        try
        {
            server = await NewServer(group, IPAddress.Loopback, accepted);
            client = await NewClient(group).ConnectAsync(server.LocalAddress).WaitAsync(Timeout);
            // The client closes when it reads the child's close, so its address is read first.
            EndPoint clientAddress = client.LocalAddress;
            Assert.NotNull(clientAddress);
            IChannel child = await accepted.Task.WaitAsync(Timeout);
            await child.CloseAsync().WaitAsync(Timeout);
            Assert.False(child.IsOpen);
            Assert.Equal(clientAddress, child.RemoteAddress);
        }
        finally
        {
            await CloseAll(group, client, server);
        }
    }

    [Fact]
    public async Task FailedConnectKeepsTargetRemoteAddress()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var channel = new NioSocketChannel();
        IPEndPoint endpoint = UnusedLoopbackEndPoint();

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => new Bootstrap().Group(group).ChannelFactory(() => channel)
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(endpoint).WaitAsync(Timeout));
            Assert.False(channel.IsOpen);
            Assert.Equal(endpoint, channel.RemoteAddress);
        }
        finally
        {
            await group.StopAsync().WaitAsync(Timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WildcardConnectTargetsLoopback(bool ipv6)
    {
        Assert.SkipWhen(ipv6 && !Socket.OSSupportsIPv6, "IPv6 is not supported on this host.");
        IPAddress loopback = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        IPAddress wildcard = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannel server = null, client = null;

        try
        {
            server = await NewServer(group, loopback, null);
            int port = ((IPEndPoint)server.LocalAddress).Port;
            client = await NewClient(group).ConnectAsync(new IPEndPoint(wildcard, port)).WaitAsync(Timeout);
            Assert.True(client.IsActive);
            Assert.Equal(new IPEndPoint(loopback, port), client.RemoteAddress);
        }
        finally
        {
            await CloseAll(group, client, server);
        }
    }

    [Fact]
    public async Task ConnectedChannelKeepsLocalAddressAfterClose()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;

        try
        {
            server = await NewServer(group, IPAddress.Loopback, accepted);
            client = await NewClient(group).ConnectAsync(server.LocalAddress).WaitAsync(Timeout);
            IChannel child = await accepted.Task.WaitAsync(Timeout);
            EndPoint clientAddress = child.RemoteAddress;
            Assert.NotNull(clientAddress);
            await client.CloseAsync().WaitAsync(Timeout);
            Assert.False(client.IsOpen);
            Assert.Equal(clientAddress, client.LocalAddress);
        }
        finally
        {
            await CloseAll(group, client, server);
        }
    }

    [Fact]
    public async Task AcceptedChannelKeepsLocalAddressAfterClose()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;

        try
        {
            server = await NewServer(group, IPAddress.Loopback, accepted);
            client = await NewClient(group).ConnectAsync(server.LocalAddress).WaitAsync(Timeout);
            IChannel child = await accepted.Task.WaitAsync(Timeout);
            await child.CloseAsync().WaitAsync(Timeout);
            Assert.False(child.IsOpen);
            Assert.Equal(server.LocalAddress, child.LocalAddress);
        }
        finally
        {
            await CloseAll(group, client, server);
        }
    }

    [Fact]
    public async Task BoundChannelKeepsLocalAddressAfterClose()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannel channel = null;

        try
        {
            channel = await NewClient(group).BindAsync(new IPEndPoint(IPAddress.Loopback, 0)).WaitAsync(Timeout);
            await channel.CloseAsync().WaitAsync(Timeout);
            Assert.False(channel.IsOpen);
            IPEndPoint local = Assert.IsType<IPEndPoint>(channel.LocalAddress);
            Assert.Equal(IPAddress.Loopback, local.Address);
            Assert.NotEqual(0, local.Port);
        }
        finally
        {
            await CloseAll(group, channel, null);
        }
    }

    [Fact]
    public async Task FailedConnectKeepsBoundLocalAddress()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var channel = new NioSocketChannel();
        IPEndPoint endpoint = UnusedLoopbackEndPoint();
        IPEndPoint local = UnusedLoopbackEndPoint();

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => new Bootstrap().Group(group).ChannelFactory(() => channel)
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(endpoint, local).WaitAsync(Timeout));
            Assert.False(channel.IsOpen);
            Assert.Equal(local, channel.LocalAddress);
        }
        finally
        {
            await group.StopAsync().WaitAsync(Timeout);
        }
    }

    private static IPEndPoint UnusedLoopbackEndPoint()
    {
        using var listener = new TcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        return (IPEndPoint)listener.LocalEndpoint;
    }

    private static Task<IChannel> NewServer(IEventLoopGroup group, IPAddress address,
                                            TaskCompletionSource<IChannel> accepted)
    {
        return new ServerBootstrap().Group(group).Channel<NioServerSocketChannel>()
            .ChildHandler(new ActionChannelInitializer<IChannel>(ch => accepted?.TrySetResult(ch)))
            .BindAsync(new IPEndPoint(address, 0)).WaitAsync(Timeout);
    }

    private static Bootstrap NewClient(IEventLoopGroup group)
    {
        return new Bootstrap().Group(group).Channel<NioSocketChannel>().Handler(new ChannelInboundHandlerAdapter());
    }

    private static async Task CloseAll(MultiThreadIoEventLoopGroup group, IChannel client, IChannel server)
    {
        if (client != null)
            await client.CloseAsync();

        if (server != null)
            await server.CloseAsync();

        await group.StopAsync().WaitAsync(Timeout);
    }
}
