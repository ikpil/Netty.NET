using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent event-loop cleanup.
public class DatagramMulticastIsolationContractTest
{
    [Theory(Timeout = 10000)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task SharedPortReceivesOnlyOnTheSocketWithTheMembership(bool ipv6Group, bool dualMode, bool sourceSpecific)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Exercises the Linux multicast membership options.");
        if ((ipv6Group || dualMode) && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 is not supported.");
        var groupFamily = ipv6Group ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var socketFamily = dualMode ? AddressFamily.InterNetworkV6 : groupFamily;
        var iface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(network =>
            network.OperationalStatus == OperationalStatus.Up && network.SupportsMulticast &&
            HasAddress(network, groupFamily) && (!dualMode || HasAddress(network, socketFamily)));
        if (iface == null) Assert.Skip("An active multicast interface with the socket and group address families is required.");

        var source = iface.GetIPProperties().UnicastAddresses.First(address => address.Address.AddressFamily == groupFamily).Address;
        var multicast = IPAddress.Parse(ipv6Group ? "ff12::17c" : "239.192.41.97");
        if (ipv6Group) multicast.ScopeId = iface.GetIPProperties().GetIPv6Properties().Index;
        var any = socketFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
        var cancellation = TestContext.Current.CancellationToken;
        var memberPackets = Channel.CreateUnbounded<int>();
        var otherPackets = Channel.CreateUnbounded<int>();
        var group = new DefaultEventLoopGroup(1);
        using var peer = new Socket(groupFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            peer.Bind(new IPEndPoint(source, 0));
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(socketFamily, dualMode))
                .Option(ChannelOption.IP_MULTICAST_IF, iface).Option(ChannelOption.SO_REUSEADDR, true);
            var member = (DatagramChannel)await bootstrap.Clone().Handler(new Capture(memberPackets.Writer)).BindAsync(new IPEndPoint(any, 0), cancellation);
            var local = Assert.IsType<IPEndPoint>(member.LocalAddress);
            var other = (DatagramChannel)await bootstrap.Clone().Handler(new Capture(otherPackets.Writer)).BindAsync(local, cancellation);
            var sender = await bootstrap.Clone().Handler(new ChannelInboundHandlerAdapter())
                .BindAsync(new IPEndPoint(dualMode ? any : source, 0), cancellation);
            var target = new IPEndPoint(multicast, local.Port);

            await JoinAsync(member);
            await member.ConnectAsync(peer.LocalEndPoint, cancellationToken: cancellation);
            await member.DisconnectAsync(cancellation);
            Assert.Equal(local, member.LocalAddress);

            await SendAsync(1);
            Assert.Equal(1, await memberPackets.Reader.ReadAsync(cancellation));
            await JoinAsync(other);
            if (sourceSpecific)
                await member.LeaveGroupAsync(multicast, iface, source);
            else
                await member.LeaveGroupAsync(target, iface);
            await SendAsync(2);
            Assert.Equal(2, await otherPackets.Reader.ReadAsync(cancellation));
            await Task.Delay(200, cancellation);
            // A duplicated multicast loopback may repeat the first payload on its original receiver.
            while (memberPackets.Reader.TryRead(out int memberValue)) Assert.Equal(1, memberValue);
            while (otherPackets.Reader.TryRead(out int otherValue)) Assert.Equal(2, otherValue);

            Task JoinAsync(DatagramChannel channel) => sourceSpecific
                ? channel.JoinGroupAsync(multicast, iface, source) : channel.JoinGroupAsync(target, iface);
            Task SendAsync(int value) => sender.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer(4).WriteInt(value), target), cancellation);
        }
        finally { await group.StopAsync(); }
    }

    private static bool HasAddress(NetworkInterface network, AddressFamily family) =>
        network.GetIPProperties().UnicastAddresses.Any(info => info.Address.AddressFamily == family);

    private sealed class Capture(ChannelWriter<int> writer) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket message) => writer.TryWrite(message.Content.ReadInt());
    }
}
