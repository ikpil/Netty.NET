using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent socket/group cleanup.
public class SocketShutdownContractTest
{
    [Fact(Timeout = 10000)]
    public async Task ShutdownAttemptsOutputBeforeInputAndPreservesTheFirstFailure()
    {
        await using var fixture = new ShutdownFixture(false);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var outputFailure = new IOException("output failure");
        var inputFailure = new IOException("input failure");
        var channel = (FailingShutdownChannel)await new Bootstrap().Group(fixture.Client.Config.Group).ChannelFactory(() => new FailingShutdownChannel(outputFailure, inputFailure))
            .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(listener.LocalEndpoint);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        Assert.Same(outputFailure, await Assert.ThrowsAsync<IOException>(() => channel.ShutdownAsync()));
        Assert.Equal(new[] { "output", "input" }, channel.Calls);
        Assert.True(channel.AllOnOwner);
        Assert.True(channel.IsOpen);
    }
    private sealed class FailingShutdownChannel(Exception output, Exception input) : TcpSocketChannel
    {
        internal readonly List<string> Calls = new();
        internal bool AllOnOwner = true;
        protected override void DoShutdownOutput() { AllOnOwner &= EventLoop.InEventLoop(); Calls.Add("output"); throw output; }
        protected override void DoShutdownInput() { AllOnOwner &= EventLoop.InEventLoop(); Calls.Add("input"); throw input; }
    }

    [Fact(Timeout = 10000)]
    public async Task OutputShutdownReleasesQueuedWritesAndKeepsInboundCommunicationOpen()
    {
        await using var fixture = new ShutdownFixture(false);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var handler = new ShutdownTestHandler();
        var channel = (TcpSocketChannel)await fixture.Client.Handler(handler).ConnectAsync(listener.LocalEndpoint);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var queued = channel.Allocator.Buffer(1).WriteByte(1);
        Task write = null;
        await channel.EventLoop.SubmitAsync(() => { write = channel.WriteAsync(queued); });
        Assert.False(write.IsCompleted);
        await channel.ShutdownOutputAsync();
        await Assert.ThrowsAsync<ChannelOutputShutdownException>(() => write);
        Assert.Equal(0, queued.ReferenceCount);
        var rejected = channel.Allocator.Buffer(1).WriteByte(2);
        await Assert.ThrowsAsync<IOException>(() => channel.WriteAndFlushAsync(rejected));
        Assert.Equal(0, rejected.ReferenceCount);
        await Assert.ThrowsAsync<IOException>(() => channel.ShutdownOutputAsync());
        Assert.True(channel.IsOpen); Assert.True(channel.IsActive); Assert.False(channel.IsInputShutdown);
        var bytes = new byte[1];
        Assert.Equal(0, await peer.GetStream().ReadAsync(bytes, TestContext.Current.CancellationToken));
        await peer.GetStream().WriteAsync(new byte[] { 3 }, TestContext.Current.CancellationToken);
        Assert.Equal(3, await handler.Bytes.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 10000)]
    public async Task FinalInputReadCompletionRequiresManualReadAndFiresOnceOnTheOwner()
    {
        await using var fixture = new ShutdownFixture(false);
        var handler = new FinalReadHandler();
        var server = await fixture.Server.ChildHandler(handler).ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true)
            .ChildOption(ChannelOption.AUTO_READ, false).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)server.LocalAddress, TestContext.Current.CancellationToken);
        var channel = await handler.Active.Task.WaitAsync(TestContext.Current.CancellationToken);
        peer.Client.Shutdown(SocketShutdown.Send);
        channel.Read();
        await handler.Half.Task.WaitAsync(TestContext.Current.CancellationToken);
        await channel.EventLoop.SubmitAsync(() => { });
        Assert.False(handler.Final.Task.IsCompleted);
        channel.Read();
        await handler.Final.Task.WaitAsync(TestContext.Current.CancellationToken);
        channel.Read();
        await channel.EventLoop.SubmitAsync(() => { });
        Assert.Equal(new[] { "readComplete", "half", "readComplete", "final" }, handler.Events);
        Assert.True(handler.AllOnOwner);
        Assert.True(channel.IsOpen); Assert.True(channel.IsActive); Assert.True(((TcpSocketChannel)channel).IsInputShutdown);
    }
    private sealed class FinalReadHandler : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource<IChannel> Active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Half = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Final = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Events = new();
        internal bool AllOnOwner = true;
        public override void ChannelActive(ChannelHandlerContext ctx) => Active.TrySetResult(ctx.Channel);
        public override void ChannelReadComplete(ChannelHandlerContext ctx)
        { AllOnOwner &= ctx.Executor.InEventLoop(); Events.Add("readComplete"); }
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            AllOnOwner &= ctx.Executor.InEventLoop();
            if (evt is ChannelInputShutdownEvent) { Events.Add("half"); Half.TrySetResult(); }
            else if (evt is ChannelInputShutdownReadComplete) { Events.Add("final"); Final.TrySetResult(); }
        }
    }
}
