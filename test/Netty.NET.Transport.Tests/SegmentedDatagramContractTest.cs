using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class SegmentedDatagramContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SegmentBoundariesSurviveCompositeOffsetsAndTheLastShortSegment(bool bindSender)
    {
        var token = TestContext.Current.CancellationToken;
        using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var local = new IPEndPoint(IPAddress.Loopback, 0);
        receiver.Bind(local);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var bootstrap = NewSender(group, AddressFamily.InterNetwork);
#pragma warning disable CS0612 // The source unbound datagram path uses active-on-registration.
            var sender = bindSender ? await bootstrap.BindAsync(local, token) :
                await bootstrap.Option(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true).RegisterAsync(token);
#pragma warning restore CS0612
            var content = Unpooled.CompositeBuffer(2);
            content.AddComponent(Unpooled.WrappedBuffer(new byte[] { 99, 1, 2 }), true);
            content.AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4, 5, 6, 7 }), true);
            content.SkipBytes(1);
            var packet = new SegmentedDatagramPacket(content, 3, (IPEndPoint)receiver.LocalEndPoint);
            var duplicate = packet.RetainedDuplicate();
            try
            {
                Assert.Equal(packet.SegmentSize, duplicate.SegmentSize);
                Assert.Equal(packet.Recipient, duplicate.Recipient);
                await sender.WriteAndFlushAsync(packet, token);
                byte[] data = new byte[8];
                foreach (byte[] expected in new byte[][] { [1, 2, 3], [4, 5, 6], [7] })
                {
                    var result = await receiver.ReceiveFromAsync(data.AsMemory(), SocketFlags.None, local, token)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
                    Assert.Equal(expected, data[..result.ReceivedBytes]);
                }
                Assert.Equal(1, duplicate.Content.ReaderIndex);
            }
            finally { duplicate.Release(); }
            Assert.Equal(0, content.ReferenceCount);
        }
        finally { await group.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundChannelSendsOneKernelAggregateWithoutChangingTheFollowingWrite(bool ipv6)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        SegmentedDatagramSourceTest.RequireGro(family);
        var token = TestContext.Current.CancellationToken;
        using var receiver = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        var local = new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0);
        receiver.Bind(local);
        LinuxUdpOffload.SetGro(receiver, true);

        var group = new DefaultEventLoopGroup(1);
        try
        {
            var sender = await NewSender(group, family).BindAsync(local, token);
            var target = (IPEndPoint)receiver.LocalEndPoint;
            byte[] expected = new byte[1025];
            Random.Shared.NextBytes(expected);
            await sender.WriteAndFlushAsync(new SegmentedDatagramPacket(Unpooled.WrappedBuffer(expected), 512, target), token);
            await sender.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer().WriteByte(33), target), token);

            byte[] actual = new byte[4096];
            var result = await LinuxUdpOffload.ReceiveAsync(receiver, actual, local, slotSize: 2048)
                .WaitAsync(TimeSpan.FromSeconds(3), token);
            Assert.Equal(2, result.Batch.Length);
            var aggregate = result.Batch[0];
            Assert.Equal(512, aggregate.SegmentSize);
            Assert.Equal(sender.LocalAddress, aggregate.Sender);
            Assert.Equal(expected, actual[..aggregate.Length]);

            var ordinary = result.Batch[1];
            Assert.Equal(0, ordinary.SegmentSize);
            Assert.Equal(1, ordinary.Length);
            Assert.Equal(2048, ordinary.Offset);
            Assert.Equal(33, actual[ordinary.Offset]);
        }
        finally { await group.StopAsync(); }
    }

    private static Bootstrap NewSender(DefaultEventLoopGroup loop, AddressFamily family) =>
        new Bootstrap().Group(loop).ChannelFactory(() => new DatagramChannel(family)).Handler(new ChannelInboundHandlerAdapter());
}
