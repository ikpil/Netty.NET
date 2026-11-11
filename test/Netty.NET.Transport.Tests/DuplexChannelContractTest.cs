using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// CLR interface coverage for DuplexChannel and NioSocketChannel.isShutdown, separate from source mappings.
#pragma warning disable xUnit1051 // Socket and channel cleanup have independent ownership.
public class DuplexChannelContractTest
{
    [Theory(Timeout = 15000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothSocketFamiliesExposeIndependentShutdownThroughTheCommonInterface(bool domain)
    {
        var cancellation = TestContext.Current.CancellationToken;
        if (domain) DomainSocketSourceTest.RequireDomainSockets();
        await using var fixture = new ShutdownFixture(false, domain);
        var received = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        EndPoint address = domain ? DomainSocketSourceTest.NewAddress() : new IPEndPoint(IPAddress.Loopback, 0);
        var server = await fixture.Server.ChildOption(ChannelOption.AUTO_READ, false)
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => accepted.TrySetResult(channel))).BindAsync(address, cancellation);
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, false).Handler(new Signal(received)).RegisterAsync(cancellation);
            var duplex = Assert.IsAssignableFrom<IDuplexChannel>(client);
            Assert.True(duplex.IsShutdown); // The original reports shutdown for inactive channels.
            await client.ConnectAsync(server.LocalAddress, cancellationToken: cancellation);
            var child = await accepted.Task.WaitAsync(cancellation);
            Assert.IsAssignableFrom<IDuplexChannel>(child);
            Assert.False(duplex.IsInputShutdown); Assert.False(duplex.IsOutputShutdown); Assert.False(duplex.IsShutdown);
            await duplex.ShutdownOutputAsync();
            Assert.False(duplex.IsInputShutdown); Assert.True(duplex.IsOutputShutdown); Assert.False(duplex.IsShutdown);
            await child.WriteAndFlushAsync(child.Allocator.Buffer(1).WriteByte(42), cancellation);
            client.Read();
            Assert.Equal(42, await received.Task.WaitAsync(cancellation));
            // The source reports the already-shut output failure, but still shuts down the input.
            await Assert.ThrowsAsync<IOException>(() => duplex.ShutdownAsync());
            Assert.True(duplex.IsInputShutdown); Assert.True(duplex.IsOutputShutdown); Assert.True(duplex.IsShutdown);
            Assert.True(duplex.IsOpen);
            await duplex.CloseAsync();
            Assert.True(duplex.IsShutdown);
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class Signal(TaskCompletionSource<byte> received) : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf buffer) => received.TrySetResult((byte)buffer.ReadByte());
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => received.TrySetException(error);
    }
}
#pragma warning restore xUnit1051
