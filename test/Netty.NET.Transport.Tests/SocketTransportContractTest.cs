using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Resolver;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051
public class SocketTransportContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TcpBootstrapResolvesHostAndEchoesActualBufferData(bool composite)
    {
        var group = new DefaultEventLoopGroup(2);
        IChannel server = null, client = null;
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] bytes = Enumerable.Range(0, 65536).Select(i => unchecked((byte)i)).ToArray();
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new EchoHandler())))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            client = await new Bootstrap().Group(group).Channel<TcpSocketChannel>()
                .Handler(new ReceiveHandler(bytes.Length, received))
                .RemoteAddress("localhost", ((IPEndPoint)server.LocalAddress).Port)
                .Resolver(new LoopbackResolverGroup())
                .ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10));
            ByteBuf outgoing = composite
                ? Unpooled.CompositeBuffer().AddComponent(Unpooled.CopiedBuffer(bytes.AsSpan(0, 32768)), true)
                    .AddComponent(Unpooled.CopiedBuffer(bytes.AsSpan(32768)), true)
                : Unpooled.WrappedBuffer(bytes);
            await client.WriteAndFlushAsync(outgoing).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, outgoing.ReferenceCount);
            Assert.Equal(bytes, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(client.IsActive);
        }
        finally
        {
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    [Fact]
    public async Task TcpManualReadWaitsUntilRequestedAndAutoReadCanResume()
    {
        var group = new DefaultEventLoopGroup(1);
        IChannel channel = null;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            channel = await new Bootstrap().Group(group).Channel<TcpSocketChannel>()
                .Option(ChannelOption.AUTO_READ, false).Handler(new ReceiveHandler(3, received))
                .ConnectAsync(listener.LocalEndpoint);
            using var peer = await accepting;
            await peer.GetStream().WriteAsync(new byte[] { 1, 2, 3 });
            await channel.EventLoop.SubmitAsync(() => Assert.False(received.Task.IsCompleted));
            channel.Read();
            Assert.Equal(new byte[] { 1, 2, 3 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            if (channel != null)
                await channel.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    [Fact]
    public async Task FailedConnectClosesCreatedChannel()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new TcpSocketChannel();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        using (var listener = new TcpListener(endpoint))
        { listener.Start(); endpoint = (IPEndPoint)listener.LocalEndpoint; }
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => new Bootstrap().Group(group).ChannelFactory(() => channel)
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(endpoint));
            Assert.False(channel.IsOpen);
            Assert.True(channel.CloseCompletion.IsCompletedSuccessfully);
        }
        finally { await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    private sealed class EchoHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            if (!context.Executor.InEventLoop())
                throw new InvalidOperationException("Wrong event loop.");
            _ = context.WriteAndFlushAsync(message.Retain());
        }
    }
    private sealed class ReceiveHandler(int size, TaskCompletionSource<byte[]> received) : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly List<byte> _bytes = new();
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            if (!context.Executor.InEventLoop())
                throw new InvalidOperationException("Wrong event loop.");
            _bytes.AddRange(message.ReadableMemory.ToArray());
            if (_bytes.Count >= size)
                received.TrySetResult(_bytes.ToArray());
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => received.TrySetException(cause);
    }
    private sealed class LoopbackResolverGroup : AddressResolverGroup<IPEndPoint>
    {
        protected override IAddressResolver<IPEndPoint> NewResolver(IEventExecutor executor) => new InetSocketAddressResolver(executor, new LoopbackNameResolver(executor));
    }
    private sealed class LoopbackNameResolver(IEventExecutor executor) : InetNameResolver(executor)
    {
        protected override Task<IPAddress> DoResolveAsync(string host, CancellationToken token) => Task.FromResult(IPAddress.Loopback);
        protected override Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string host, CancellationToken token) => Task.FromResult<IReadOnlyList<IPAddress>>(new[] { IPAddress.Loopback });
    }
}
