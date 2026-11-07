using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramOriginalDestinationContractTest
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task WildcardReceivesTheDestinationOfEachPacket(bool receiveOriginalDestination, bool scatterAndGro, bool dualMode)
    {
        if (receiveOriginalDestination && !OperatingSystem.IsLinux()) Assert.Skip("Original destination metadata requires Linux.");
        if (dualMode && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 unavailable.");
        if (scatterAndGro) SegmentedDatagramSourceTest.RequireGro(AddressFamily.InterNetwork);

        var token = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        var messages = Channel.CreateUnbounded<(IPEndPoint Recipient, IPEndPoint Sender, int Length)>();
        var any = dualMode ? IPAddress.IPv6Any : IPAddress.Any;
        try
        {
            var receiver = new Bootstrap().Group(group)
                .ChannelFactory(() => new DatagramChannel(any.AddressFamily, dualMode))
                .Option(ChannelOption.AUTO_READ, false)
                .Option(ChannelOption.IP_RECVORIGDSTADDR, receiveOriginalDestination)
                .Handler(new Capture(messages.Writer));
            if (scatterAndGro)
                receiver.Option(ChannelOption.UDP_GRO, true)
                    .Option(ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, 2048)
                    .Option(ChannelOption.RECVBUF_ALLOCATOR, new FixedRecvByteBufAllocator(4096));
            var channel = await receiver.BindAsync(new IPEndPoint(any, 0), token);
            var local = (IPEndPoint)channel.LocalAddress;
            Assert.Equal(any, local.Address);

            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sender.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var first = new IPEndPoint(IPAddress.Loopback, local.Port);
            var second = new IPEndPoint(IPAddress.Parse("127.0.0.2"), local.Port);
            if (scatterAndGro)
                Assert.True(LinuxUdpOffload.TrySend(sender, new byte[1025], 512, first));
            else
                Assert.Equal(1, await sender.SendToAsync(new byte[1], SocketFlags.None, first, token));
            Assert.Equal(2, await sender.SendToAsync(new byte[2], SocketFlags.None, second, token));
            channel.Config.SetAutoRead(true);

            foreach (int length in scatterAndGro ? new[] { 512, 512, 1 } : new[] { 1 })
                await AssertPacketAsync(first, length);
            await AssertPacketAsync(second, 2);
            Assert.Equal(local, channel.LocalAddress);

            async Task AssertPacketAsync(IPEndPoint recipient, int length)
            {
                var packet = await messages.Reader.ReadAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
                Assert.Equal(receiveOriginalDestination ? recipient : local, packet.Recipient);
                Assert.Equal(sender.LocalEndPoint, packet.Sender);
                Assert.Equal(length, packet.Length);
            }
        }
        finally { await group.StopAsync(); }
    }

    [Fact]
    public async Task FreeBindAllowsAnAddressThatIsNotLocal()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("IP_FREEBIND requires Linux.");
        var token = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        var address = IPAddress.Parse("192.0.2.1");
        try
        {
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(static () => new DatagramChannel())
                .Handler(new ChannelInboundHandlerAdapter());
            var channel = await bootstrap.Option(ChannelOption.IP_FREEBIND, true).BindAsync(new IPEndPoint(address, 0), token);
            Assert.Equal(address, ((IPEndPoint)channel.LocalAddress).Address);
        }
        finally { await group.StopAsync(); }
    }

    private sealed class Capture(ChannelWriter<(IPEndPoint, IPEndPoint, int)> writer) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket message) =>
            writer.TryWrite((message.Recipient, message.Sender, message.Content.ReadableBytes));
    }
}
