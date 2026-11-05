using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent cleanup.
public class DomainSocketContractTest
{
    [Fact(Timeout = 10000)]
    public async Task BootstrapExchangesBytesAfterHalfCloseAndRemovesTheListenerPath()
    {
        DomainSocketSourceTest.RequireDomainSockets();
        var endpoint = DomainSocketSourceTest.NewAddress();
        var group = new DefaultEventLoopGroup(2);
        var child = new ExchangeHandler();
        var clientHandler = new ExchangeHandler();
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).Channel<DomainServerSocketChannel>()
                .ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true).ChildHandler(child)
                .BindAsync(endpoint, cancellation);
            Assert.True(File.Exists(endpoint.ToString()));
            var client = (DomainSocketChannel)await new Bootstrap().Group(group).Channel<DomainSocketChannel>()
                .Handler(clientHandler).ConnectAsync(endpoint, cancellationToken: cancellation);
            var accepted = Assert.IsType<DomainSocketChannel>(await child.Active.Task.WaitAsync(cancellation));
            Assert.Same(server, accepted.Parent);
            Assert.Equal(endpoint.ToString(), client.RemoteAddress.ToString());
            Assert.Equal(endpoint.ToString(), accepted.LocalAddress.ToString());
            await client.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 42 }), cancellation);
            Assert.Equal(42, await child.ByteRead.Task.WaitAsync(cancellation));
            await client.ShutdownOutputAsync();
            await child.InputClosed.Task.WaitAsync(cancellation);
            Assert.True(accepted.IsActive);
            await accepted.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 73 }), cancellation);
            Assert.Equal(73, await clientHandler.ByteRead.Task.WaitAsync(cancellation));
            await group.StopAsync();
            Assert.False(File.Exists(endpoint.ToString()));
        }
        finally { await group.StopAsync(); }
    }

    [Fact(Timeout = 10000)]
    public async Task AFailedBindLeavesTheFirstListenersPathAndConnectionIntact()
    {
        DomainSocketSourceTest.RequireDomainSockets();
        var endpoint = DomainSocketSourceTest.NewAddress();
        var group = new DefaultEventLoopGroup(1);
        var accepted = new ExchangeHandler();
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            await new ServerBootstrap().Group(group).Channel<DomainServerSocketChannel>()
                .ChildHandler(accepted).BindAsync(endpoint, cancellation);
            await Assert.ThrowsAsync<SocketException>(() => new ServerBootstrap().Group(group)
                .Channel<DomainServerSocketChannel>().ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(endpoint, cancellation));
            Assert.True(File.Exists(endpoint.ToString()));
            using var peer = DomainSocketSourceTest.NewSocket();
            await peer.ConnectAsync(endpoint, cancellation);
            await accepted.Active.Task.WaitAsync(cancellation);
        }
        finally { await group.StopAsync(); }
    }

    [Fact]
    public async Task DomainOptionsDoNotApplyTcpOrIpControlsToTheSocket()
    {
        DomainSocketSourceTest.RequireDomainSockets();
        var group = new DefaultEventLoopGroup(1);
        var client = new DomainSocketChannel();
        var server = new DomainServerSocketChannel();
        try
        {
            await group.RegisterAsync(client);
            await group.RegisterAsync(server);
            Assert.False(client.Config.SetOption(ChannelOption.TCP_NODELAY, true));
            Assert.False(client.Config.TryGetOption(ChannelOption.IP_TOS, out _));
            Assert.False(server.Config.TryGetOption(ChannelOption.ALLOW_HALF_CLOSURE, out _));
            Assert.True(server.Config.TryGetOption(ChannelOption.SO_BACKLOG, out int defaultBacklog));
            Assert.Equal(NetUtil.DefaultListenBacklog, defaultBacklog);
            Assert.True(server.Config.SetOption(ChannelOption.SO_BACKLOG, 32));
            Assert.True(server.Config.TryGetOption(ChannelOption.SO_BACKLOG, out int backlog));
            Assert.Equal(32, backlog);
            Assert.Contains(ChannelOption.SO_SNDBUF, client.Config.GetOptions().Keys);
        }
        finally { await group.StopAsync(); }
    }

    private sealed class ExchangeHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal readonly TaskCompletionSource<IChannel> Active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<int> ByteRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource InputClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void ChannelActive(ChannelHandlerContext ctx) => Active.TrySetResult(ctx.Channel);
        protected override void ChannelRead0(ChannelHandlerContext ctx, ByteBuf message) => ByteRead.TrySetResult(message.ReadByte());
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            if (evt is ChannelInputShutdownEvent) InputClosed.TrySetResult();
        }
    }
}
