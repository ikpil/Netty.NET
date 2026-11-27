using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// CLR-specific integration checks for AbstractNioChannel.clearReadPending and read-ready admission.
// A completed native receive has already consumed input, so withholding delivery must preserve that input.
public class NativeReadAdmissionContractTest
{
    [Fact]
    public async Task TcpCompletedReceiveWaitsForReadAfterAutoReadIsDisabled()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new CaptureBytes(received);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        IChannel channel = null;
        try
        {
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();
            channel = await new Bootstrap().Group(group).Channel<NioSocketChannel>().Handler(capture)
                .ConnectAsync(listener.LocalEndpoint, cancellationToken: TestContext.Current.CancellationToken);
            using var peer = await accepting;
            // The connect callback queued the receive before this barrier; no input has been sent yet.
            await channel.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            await channel.EventLoop.SubmitAsync(() => channel.Config.SetAutoRead(false), TestContext.Current.CancellationToken);
            await peer.GetStream().WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(received.Task.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref capture.ReadCalls));
            channel.Read();
            Assert.Equal(new byte[] { 1, 2, 3 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally { if (channel != null) await channel.CloseAsync(TestContext.Current.CancellationToken); await Stop(group); }
    }
    [Fact]
    public async Task UdpCompletedReceiveWaitsForReadAndPreservesItsDatagram()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel channel = null;
        try
        {
            channel = await new Bootstrap().Group(group).Channel<NioDatagramChannel>().Handler(new CapturePacket(received))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            await channel.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            await channel.EventLoop.SubmitAsync(() => channel.Config.SetAutoRead(false), TestContext.Current.CancellationToken);
            using var peer = new UdpClient(AddressFamily.InterNetwork);
            await peer.SendAsync(new byte[] { 4, 5, 6 }, (IPEndPoint)channel.LocalAddress, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(received.Task.IsCompleted);
            channel.Read();
            Assert.Equal(new byte[] { 4, 5, 6 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally { if (channel != null) await channel.CloseAsync(TestContext.Current.CancellationToken); await Stop(group); }
    }
    [Fact]
    public async Task TcpCompletedAcceptWaitsForServerReadAfterAutoReadIsDisabled()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, child = null;
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<NioServerSocketChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel => accepted.TrySetResult(channel)))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            await server.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            await server.EventLoop.SubmitAsync(() => server.Config.SetAutoRead(false), TestContext.Current.CancellationToken);
            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)server.LocalAddress, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(accepted.Task.IsCompleted);
            server.Read();
            child = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(child.IsRegistered);
        }
        finally
        {
            if (child != null)
                await child.CloseAsync(TestContext.Current.CancellationToken);
            if (server != null)
                await server.CloseAsync(TestContext.Current.CancellationToken);
            await Stop(group);
        }
    }
    [Fact]
    public async Task TcpCompletedReceiveWaitsForRegistrationAndMovesToItsNewWorker()
    {
        var group = new MultiThreadIoEventLoopGroup(2, NioIoHandler.NewFactory());
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        IChannel channel = null;
        try
        {
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();
            channel = await new Bootstrap().Group(group).Channel<NioSocketChannel>().Handler(new CaptureBytes(received))
                .ConnectAsync(listener.LocalEndpoint, cancellationToken: TestContext.Current.CancellationToken);
            using var peer = await accepting;
            await channel.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            var oldLoop = channel.EventLoop;
            var next = group.Next();
            if (ReferenceEquals(next, oldLoop))
                next = group.Next();
            Assert.NotSame(oldLoop, next);
            await channel.DeregisterAsync(TestContext.Current.CancellationToken);
            await peer.GetStream().WriteAsync(new byte[] { 7, 8, 9 }, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(received.Task.IsCompleted);
            await next.RegisterAsync(channel, TestContext.Current.CancellationToken);
            Assert.Same(next, channel.EventLoop);
            Assert.Equal(new byte[] { 7, 8, 9 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally { if (channel != null) await channel.CloseAsync(TestContext.Current.CancellationToken); await Stop(group); }
    }
    private static Task Stop(MultiThreadIoEventLoopGroup group)
    {
        return group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }
    private sealed class CaptureBytes(TaskCompletionSource<byte[]> received, int expectedLength = 3) : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly List<byte> _bytes = new();
        internal int ReadCalls;
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            Assert.True(ctx.Channel.IsRegistered);
            Assert.True(ctx.Executor.InEventLoop());
            Interlocked.Increment(ref ReadCalls);
            _bytes.AddRange(msg.ReadableMemory.ToArray());
            if (_bytes.Count >= expectedLength)
                received.TrySetResult(_bytes.ToArray());
        }
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            received.TrySetException(cause);
        }
    }
    private sealed class CapturePacket(TaskCompletionSource<byte[]> received) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket packet)
        { Assert.True(ctx.Channel.IsRegistered); Assert.True(ctx.Executor.InEventLoop()); received.TrySetResult(packet.Content.ReadableMemory.ToArray()); }
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            received.TrySetException(cause);
        }
    }
}
