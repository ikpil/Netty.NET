using Netty.NET.Transport.Channels.Nio;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0612, CS0618, xUnit1051 // Original registration option and independently owned cleanup.
public class DatagramLifecycleContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationReflectsNativeInterfaceAndRegistrationState(bool ipv6)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var iface = NetworkInterface.GetAllNetworkInterfaces().First(n => n.OperationalStatus == OperationalStatus.Up &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == family));
        var address = iface.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == family).Address;
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var channel = new NioDatagramChannel(family);
        var socket = (Socket)typeof(AbstractNioChannel).GetMethod("JavaChannel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(channel, null)!;
        try
        {
            Assert.True(channel.Config.SetOption(ChannelOption.IP_MULTICAST_IF, iface));
            Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_IF, out var actual));
            Assert.Equal(iface.Id, actual.Id);
            if (ipv6)
                Assert.Equal(iface.GetIPProperties().GetIPv6Properties().Index, (int)socket.GetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface));
            else
                Assert.Equal(address, new IPAddress(socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, 4)));
            Assert.True(channel.Config.SetOption(ChannelOption.IP_MULTICAST_ADDR, address));
            Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_ADDR, out var selected));
            Assert.Equal(address, selected);
            foreach (bool disabled in new[] { false, true })
            {
                channel.Config.SetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, disabled);
                Assert.Equal(!disabled, socket.MulticastLoopback);
            }
            var options = channel.Config.GetOptions();
            if (ipv6 && OperatingSystem.IsLinux())
            {
                Assert.True(channel.Config.SetOption(ChannelOption.IP_TOS, 184));
                byte[] trafficClass = new byte[sizeof(int)];
                Assert.Equal(sizeof(int), socket.GetRawSocketOption((int)SocketOptionLevel.IPv6, 67, trafficClass));
                Assert.Equal(184, BitConverter.ToInt32(trafficClass));
                socket.SetRawSocketOption((int)SocketOptionLevel.IPv6, 67, BitConverter.GetBytes(32));
                Assert.True(channel.Config.TryGetOption(ChannelOption.IP_TOS, out int actualTrafficClass));
                Assert.Equal(32, actualTrafficClass);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => channel.Config.SetOption(ChannelOption.IP_TOS, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => channel.Config.SetOption(ChannelOption.IP_TOS, 256));
            foreach (var option in new ChannelOption[] { ChannelOption.IP_MULTICAST_IF, ChannelOption.IP_MULTICAST_ADDR,
                ChannelOption.IP_MULTICAST_TTL, ChannelOption.IP_MULTICAST_LOOP_DISABLED, ChannelOption.IP_TOS,
                ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION }) Assert.True(options.ContainsKey(option));
            channel.Config.SetOption(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true);
            await channel.RegisterAsync(group.Next());
            Assert.True(channel.IsActive);
            Assert.Null(channel.LocalAddress);
            Assert.Throws<InvalidOperationException>(() => channel.Config.SetOption(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, false));
            await channel.DeregisterAsync();
            Assert.False(channel.IsActive);
            await channel.RegisterAsync(group.Next());
            Assert.True(channel.IsActive);
        }
        finally
        {
            if (channel.IsRegistered) await channel.CloseAsync();
            else socket.Dispose();
            await group.StopAsync();
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MembershipIsIdempotentAcrossAddressAndInterfaceOverloads(bool ipv6)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var iface = NetworkInterface.GetAllNetworkInterfaces().First(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == family));
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        NioDatagramChannel channel = null;
        try
        {
            channel = (NioDatagramChannel)await new Bootstrap().Group(group).ChannelFactory(() => new NioDatagramChannel(family))
                .Handler(new ChannelInboundHandlerAdapter()).Option(ChannelOption.IP_MULTICAST_IF, iface)
                .BindAsync(new IPEndPoint(ipv6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            var multicast = new IPEndPoint(IPAddress.Parse(ipv6 ? "ff01::101" : "230.0.0.1"), 0);
            await channel.LeaveGroupAsync(multicast, iface);
            await channel.JoinGroupAsync(multicast, iface);
            await channel.JoinGroupAsync(multicast, iface);
            if (ipv6)
                await channel.LeaveGroupAsync(multicast.Address, (long)iface.GetIPProperties().GetIPv6Properties().Index);
            else
                await channel.LeaveGroupAsync(multicast.Address, iface.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == family).Address);
            await channel.LeaveGroupAsync(multicast, iface);
            await channel.JoinGroupAsync(multicast, iface);
            await channel.LeaveGroupAsync(multicast, iface);
            if (!ipv6)
            {
                await channel.JoinGroupAsync(multicast.Address, IPAddress.Any);
                await channel.LeaveGroupAsync(multicast, iface);
            }
        }
        finally { if (channel != null) await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Theory(Timeout = 10000)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task DisconnectKeepsTheBoundSocketAndReceivingAcrossReconnect(bool ipv6, bool wildcard, bool implicitBind)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var loopback = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var any = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;
        var cancellation = TestContext.Current.CancellationToken;
        var messages = Channel.CreateUnbounded<int>();
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannel channel = null;
        using var peer = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        using var otherPeer = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        peer.Bind(new IPEndPoint(loopback, 0));
        otherPeer.Bind(new IPEndPoint(loopback, 0));
        try
        {
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(() => new NioDatagramChannel(family))
                .Handler(new Capture(messages.Writer)).Option(ChannelOption.IP_MULTICAST_TTL, 23);
            channel = implicitBind ? await bootstrap.RegisterAsync() : await bootstrap.BindAsync(new IPEndPoint(wildcard ? any : loopback, 0));
            int port = (channel.LocalAddress as IPEndPoint)?.Port ?? 0;
            for (int i = 0; i < 5; i++)
            {
                bool connected = i % 2 == 0;
                if (connected) await channel.ConnectAsync(peer.LocalEndPoint, cancellationToken: cancellation);
                else await channel.DisconnectAsync(cancellation);
                var local = Assert.IsType<IPEndPoint>(channel.LocalAddress);
                if (port == 0) port = local.Port;
                Assert.NotEqual(0, port);
                Assert.Equal(port, local.Port);
                if (!wildcard || OperatingSystem.IsLinux())
                    Assert.Equal(!connected && wildcard ? any : loopback, local.Address);
                Assert.True(channel.IsActive);
                Assert.Equal(connected, ((NioDatagramChannel)channel).IsConnected);
                Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_TTL, out var ttl));
                Assert.Equal(23, ttl);
                var sender = connected ? peer : otherPeer;
                await sender.SendToAsync(new byte[] { (byte)i }, SocketFlags.None, new IPEndPoint(loopback, port), cancellation);
                Assert.Equal(i, await messages.Reader.ReadAsync(cancellation));
            }
        }
        finally { if (channel != null) await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task NativeConnectFailureClosesTheDatagramChannel()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux rejects UDP connect to broadcast without SO_BROADCAST.");
        var cancellation = TestContext.Current.CancellationToken;
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        try
        {
            var channel = await new Bootstrap().Group(group).Channel<NioDatagramChannel>()
                .Option(ChannelOption.SO_BROADCAST, false).Handler(new ChannelInboundHandlerAdapter())
                .BindAsync(new IPEndPoint(IPAddress.Any, 0), cancellation);
            await Assert.ThrowsAsync<SocketException>(() => channel.ConnectAsync(new IPEndPoint(IPAddress.Broadcast, 9), cancellationToken: cancellation));
            await channel.CloseFuture.WaitAsync(cancellation);
            Assert.False(channel.IsOpen);
        }
        finally { await group.StopAsync(); }
    }

    // DefaultDatagramChannelConfig accessors read and write the native UDP socket; getOption/setOption
    // dispatch the standard options to the same accessors.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccessorsRoundTripThroughTheNativeSocket(bool ipv6)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var level = ipv6 ? SocketOptionLevel.IPv6 : SocketOptionLevel.IP;
        var iface = NetworkInterface.GetAllNetworkInterfaces().First(n => n.OperationalStatus == OperationalStatus.Up &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == family));
        var address = iface.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == family).Address;
        var channel = new NioDatagramChannel(family);
        var socket = (Socket)typeof(AbstractNioChannel).GetMethod("JavaChannel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(channel, null)!;
        try
        {
            var config = Assert.IsType<DefaultDatagramChannelConfig>(channel.Config);

            Assert.Same(config, config.SetBroadcast(true));
            Assert.True(config.IsBroadcast());
            Assert.True(socket.EnableBroadcast);
            Assert.True(Get(config, ChannelOption.SO_BROADCAST));
            Assert.True(config.SetOption(ChannelOption.SO_BROADCAST, false));
            Assert.False(config.IsBroadcast());
            Assert.False(socket.EnableBroadcast);

            Assert.Same(config, config.SetReuseAddress(true));
            Assert.True(config.IsReuseAddress());
            Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress));
            Assert.True(Get(config, ChannelOption.SO_REUSEADDR));
            Assert.True(config.SetOption(ChannelOption.SO_REUSEADDR, false));
            Assert.False(config.IsReuseAddress());

            Assert.Same(config, config.SetReceiveBufferSize(32768));
            Assert.Equal(socket.ReceiveBufferSize, config.GetReceiveBufferSize());
            Assert.Equal(config.GetReceiveBufferSize(), Get(config, ChannelOption.SO_RCVBUF));
            Assert.Same(config, config.SetSendBufferSize(32768));
            Assert.Equal(socket.SendBufferSize, config.GetSendBufferSize());
            Assert.Equal(config.GetSendBufferSize(), Get(config, ChannelOption.SO_SNDBUF));

            Assert.Same(config, config.SetTimeToLive(17));
            Assert.Equal(17, config.GetTimeToLive());
            Assert.Equal(17, (int)socket.GetSocketOption(level, SocketOptionName.MulticastTimeToLive));
            Assert.Equal(17, Get(config, ChannelOption.IP_MULTICAST_TTL));
            Assert.True(config.SetOption(ChannelOption.IP_MULTICAST_TTL, 3));
            Assert.Equal(3, config.GetTimeToLive());
            Assert.Throws<ArgumentOutOfRangeException>(() => config.SetTimeToLive(256));

            Assert.Same(config, config.SetTrafficClass(0x28));
            Assert.Equal(0x28, config.GetTrafficClass());
            Assert.Equal(0x28, Get(config, ChannelOption.IP_TOS));
            Assert.Throws<ArgumentOutOfRangeException>(() => config.SetTrafficClass(-1));

            foreach (bool disabled in new[] { true, false })
            {
                Assert.Same(config, config.SetLoopbackModeDisabled(disabled));
                Assert.Equal(disabled, config.IsLoopbackModeDisabled());
                Assert.Equal(!disabled, socket.MulticastLoopback);
                Assert.Equal(disabled, Get(config, ChannelOption.IP_MULTICAST_LOOP_DISABLED));
            }

            Assert.Null(config.GetNetworkInterface());
            Assert.Null(config.GetInterface());
            Assert.Same(config, config.SetNetworkInterface(iface));
            Assert.Equal(iface.Id, config.GetNetworkInterface().Id);
            Assert.Equal(iface.Id, Get(config, ChannelOption.IP_MULTICAST_IF).Id);
            if (ipv6)
                Assert.Equal(iface.GetIPProperties().GetIPv6Properties().Index, (int)socket.GetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface));
            else
                Assert.Equal(address, new IPAddress(socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, 4)));
            Assert.Same(config, config.SetInterface(address));
            Assert.Equal(address, config.GetInterface());
            Assert.Equal(address, Get(config, ChannelOption.IP_MULTICAST_ADDR));
        }
        finally { socket.Dispose(); }
    }

    // DefaultDatagramChannelConfig wraps java.net.SocketException/IOException in ChannelException.
    // The multicast TTL option is rejected natively on a stream socket.
    [Fact]
    public void AccessorsWrapNativeSocketErrorsInChannelException()
    {
        var channel = new NioDatagramChannel();
        var socket = (Socket)typeof(AbstractNioChannel).GetMethod("JavaChannel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(channel, null)!;
        using var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var config = new DefaultDatagramChannelConfig(channel, tcp);
            var error = Assert.Throws<ChannelException>(() => config.SetTimeToLive(3));
            Assert.IsType<SocketException>(error.InnerException);
            error = Assert.Throws<ChannelException>(() => config.SetOption(ChannelOption.IP_MULTICAST_TTL, 3));
            Assert.IsType<SocketException>(error.InnerException);
        }
        finally { socket.Dispose(); }
    }

    private static T Get<T>(IChannelConfig config, ChannelOption<T> option)
    { Assert.True(config.TryGetOption(option, out T value)); return value; }

    private sealed class Capture(ChannelWriter<int> writer) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg) => writer.TryWrite(msg.Content.ReadByte());
    }
}
