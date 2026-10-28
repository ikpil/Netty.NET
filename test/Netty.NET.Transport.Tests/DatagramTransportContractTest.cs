using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Groups;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051
public class DatagramTransportContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(4096)]
    public async Task ReceivePreservesDatagramBoundarySenderAndConfiguredTruncation(int length)
    {
        var group = new DefaultEventLoopGroup(1);
        IChannel channel = null;
        var received = new TaskCompletionSource<(byte[], IPEndPoint)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            channel = await new Bootstrap().Group(group).Channel<DatagramChannel>().Handler(new CaptureHandler(received))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            using var peer = new UdpClient(AddressFamily.InterNetwork);
            byte[] bytes = Enumerable.Range(0, length).Select(i => unchecked((byte)i)).ToArray();
            await peer.SendAsync(bytes, (IPEndPoint)channel.LocalAddress);
            var packet = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(bytes.Take(Math.Min(2048, length)), packet.Item1);
            Assert.Equal(((IPEndPoint)peer.Client.LocalEndPoint).Port, packet.Item2.Port);
        }
        finally
        {
            if (channel != null)
                await channel.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(1024, false)]
    [InlineData(1024, true)]
    public async Task SendPreservesDatagramBytesAndReleasesPayload(int length, bool composite)
    {
        var group = new DefaultEventLoopGroup(1);
        IChannel channel = null;
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        try
        {
            channel = await new Bootstrap().Group(group).Channel<DatagramChannel>().Handler(new ChannelInboundHandlerAdapter())
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            byte[] bytes = new byte[length];
            Random.Shared.NextBytes(bytes);
            // Use an owned empty buffer too; Unpooled.Empty's permanent reference count is deliberately one.
            ByteBuf buffer = composite
                ? Unpooled.CompositeBuffer().AddComponent(Unpooled.CopiedBuffer(bytes.AsSpan(0, length / 2)), true)
                    .AddComponent(Unpooled.CopiedBuffer(bytes.AsSpan(length / 2)), true)
                : Unpooled.Buffer(Math.Max(1, length)).WriteBytes(bytes);
            await channel.WriteAndFlushAsync(new DatagramPacket(buffer, (IPEndPoint)peer.Client.LocalEndPoint));
            var packet = await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(bytes, packet.Buffer);
            Assert.Equal(0, buffer.ReferenceCount);
        }
        finally
        {
            if (channel != null)
                await channel.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    [Fact]
    public async Task GroupWritesUseIndependentBufferIndicesAndReleaseEveryOwner()
    {
        var first = new EmbeddedChannel();
        var second = new EmbeddedChannel();
        var group = new DefaultChannelGroup();
        group.Add(first);
        group.Add(second);
        var buffer = Unpooled.Buffer(3).WriteBytes(new byte[] { 1, 2, 3 });
        await group.WriteAndFlushAsync(buffer);
        var one = first.ReadOutbound<ByteBuf>();
        var two = second.ReadOutbound<ByteBuf>();
        Assert.NotSame(one, two);
        one.ReadByte();
        Assert.Equal(0, two.ReaderIndex);
        one.Release();
        two.Release();
        Assert.Equal(0, buffer.ReferenceCount);
        await group.CloseAsync();
        Assert.Empty(group);
    }
    private sealed class CaptureHandler(TaskCompletionSource<(byte[], IPEndPoint)> received) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet) => received.TrySetResult((packet.Content.ReadableMemory.ToArray(), packet.Sender));
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => received.TrySetException(error);
    }
}
