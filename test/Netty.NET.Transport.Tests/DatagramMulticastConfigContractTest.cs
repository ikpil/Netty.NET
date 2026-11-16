using Netty.NET.Transport.Channels.Nio;
using System;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independently owned channel cleanup.
public class DatagramMulticastConfigContractTest
{
    // DefaultDatagramChannelConfig delegates to MulticastSocket.getTimeToLive/setTimeToLive.
    // Check the actual CLR socket as well as the config round trip: Socket.Ttl is unicast TTL.
    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public async Task MulticastTtlUsesMulticastOptionWithoutChangingUnicastTtl(AddressFamily family)
    {
        var channel = new NioDatagramChannel(family);
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var socket = (Socket)typeof(AbstractNioChannel).GetMethod("JavaChannel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(channel, null)!;
        var level = family == AddressFamily.InterNetworkV6 ? SocketOptionLevel.IPv6 : SocketOptionLevel.IP;
        try
        {
            await channel.RegisterAsync(group.Next());
            short unicastTtl = socket.Ttl;
            Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_TTL, out int configured));
            Assert.Equal((int)socket.GetSocketOption(level, SocketOptionName.MulticastTimeToLive),
                configured);
            foreach (int ttl in new[] { 0, 17, 255 })
            {
                Assert.True(channel.Config.SetOption(ChannelOption.IP_MULTICAST_TTL, ttl));
                Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_TTL, out configured));
                Assert.Equal(ttl, configured);
                Assert.Equal(ttl, (int)socket.GetSocketOption(level, SocketOptionName.MulticastTimeToLive));
                Assert.Equal(unicastTtl, socket.Ttl);
            }
            foreach (int ttl in new[] { -1, 256, int.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => channel.Config.SetOption(ChannelOption.IP_MULTICAST_TTL, ttl));
            Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_TTL, out configured));
            Assert.Equal(255, configured);
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
}
