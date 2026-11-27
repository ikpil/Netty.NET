using System;
using System.Net.Sockets;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramOriginalDestinationContractTest
{
    [Fact]
    public void ManagedDatagramChannelRejectsUnavailableReceiveOffloadOptions()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var channel = new NioDatagramChannel(socket);
        Assert.True(channel.Config.SetOption(ChannelOption.IP_RECVORIGDSTADDR, false));
        Assert.True(channel.Config.SetOption(ChannelOption.UDP_GRO, false));
        Assert.True(channel.Config.SetOption(ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, 0));

        Assert.Throws<PlatformNotSupportedException>(() => channel.Config.SetOption(ChannelOption.IP_RECVORIGDSTADDR, true));
        Assert.Throws<PlatformNotSupportedException>(() => channel.Config.SetOption(ChannelOption.UDP_GRO, true));
        Assert.Throws<PlatformNotSupportedException>(() => channel.Config.SetOption(ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, 1024));
        Assert.True(channel.Config.TryGetOption(ChannelOption.IP_RECVORIGDSTADDR, out bool originalDestination));
        Assert.False(originalDestination);
        Assert.True(channel.Config.TryGetOption(ChannelOption.UDP_GRO, out bool gro));
        Assert.False(gro);
        Assert.True(channel.Config.TryGetOption(ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, out int slotSize));
        Assert.Equal(0, slotSize);
    }
}
