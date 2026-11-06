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
        var group = new DefaultEventLoopGroup(1);
        var channel = new DatagramChannel(family);
        var socket = (Socket)typeof(DatagramChannel).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(channel)!;
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
        var group = new DefaultEventLoopGroup(1);
        DatagramChannel channel = null;
        try
        {
            channel = (DatagramChannel)await new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(family))
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectKeepsTheBoundSocketAndReceivingAcrossReconnect(bool ipv6)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var loopback = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var messages = Channel.CreateUnbounded<int>();
        var group = new DefaultEventLoopGroup(1);
        IChannel channel = null;
        using var peer = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        using var otherPeer = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        peer.Bind(new IPEndPoint(loopback, 0));
        otherPeer.Bind(new IPEndPoint(loopback, 0));
        try
        {
            channel = await new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(family))
                .Handler(new Capture(messages.Writer)).BindAsync(new IPEndPoint(loopback, 0));
            var bound = channel.LocalAddress;
            for (int i = 0; i < 3; i++)
            {
                if (i != 1) await channel.ConnectAsync(peer.LocalEndPoint);
                else await channel.DisconnectAsync();
                Assert.Equal(bound, channel.LocalAddress);
                Assert.Equal(i != 1, ((DatagramChannel)channel).IsConnected);
                var sender = i == 1 ? otherPeer : peer;
                await sender.SendToAsync(new byte[] { (byte)i }, SocketFlags.None, bound, TestContext.Current.CancellationToken);
                Assert.Equal(i, await messages.Reader.ReadAsync(TestContext.Current.CancellationToken));
            }
        }
        finally { if (channel != null) await channel.CloseAsync(); await group.StopAsync(); }
    }
    private sealed class Capture(ChannelWriter<int> writer) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket message) => writer.TryWrite(message.Content.ReadByte());
    }
}
