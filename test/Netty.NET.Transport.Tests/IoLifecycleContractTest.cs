/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independently owned native sockets/groups.
public class IoLifecycleContractTest
{
    [Fact(Timeout = 10000)]
    public async Task RegistrationSnapshotsFollowMigrationClosureAndSuspensionAdmission()
    {
        var group1 = new DefaultEventLoopGroup(1); var group2 = new DefaultEventLoopGroup(1);
        var a = (DefaultEventLoop)group1.Next(); var b = (DefaultEventLoop)group2.Next();
        var channel = new LocalChannel();
        try
        {
            await a.RegisterAsync(channel, TestContext.Current.CancellationToken);
            // Submit to the EventLoop so we are sure its really executed in a non-async manner.
            var snapshot = await a.SubmitAsync(() => a.RegisteredChannelsIterator().ToArray());
            Assert.Single(snapshot); Assert.Same(channel, snapshot[0]); Assert.False(a.TrySuspend());
            Assert.Throws<InvalidOperationException>(() => a.RegisteredChannelsIterator());
            await channel.DeregisterAsync(); await b.RegisterAsync(channel);
            Assert.Equal(0, a.RegisteredChannels()); Assert.Equal(1, b.RegisteredChannels());
            Assert.Single(snapshot); Assert.Same(channel, snapshot[0]);
            var readOnly = await b.SubmitAsync(() => b.RegisteredChannelsIterator());
            Assert.Throws<NotSupportedException>(() => ((IList<IChannel>)readOnly).Clear());
            await channel.CloseAsync();
            Assert.Equal(0, b.RegisteredChannels());
        }
        finally { await channel.CloseAsync(); await group1.StopAsync(); await group2.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task FailedRegistrationDoesNotLeaveAPhantomChannel()
    {
        var group = new DefaultEventLoopGroup(1); var loop = (DefaultEventLoop)group.Next();
        var channel = new BrokenRegistration();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => loop.RegisterAsync(channel));
            Assert.False(channel.IsOpen); Assert.False(channel.IsRegistered);
            Assert.Equal(0, await loop.SubmitAsync(loop.RegisteredChannels).WaitAsync(TestContext.Current.CancellationToken));
        }
        finally { await group.StopAsync(); }
    }
    private sealed class BrokenRegistration : LocalChannel
    { protected override void DoRegister() => throw new IOException("registration failed"); }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AListenerCanOwnAnExistingUnboundOrListeningSocket(bool bound)
    {
        var raw = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (bound) { raw.Bind(new IPEndPoint(IPAddress.Loopback, 0)); raw.Listen(128); }
        var server = new TcpServerSocketChannel(raw);
        var group = new DefaultEventLoopGroup(1);
        var collector = new CollectChildren(); server.Pipeline.AddLast(collector);
        Assert.Equal(bound, server.IsActive);
        try
        {
            await group.RegisterAsync(server);
            if (!bound) await server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)server.LocalAddress, TestContext.Current.CancellationToken);
            await collector.First.Task.WaitAsync(TestContext.Current.CancellationToken);
            await server.CloseAsync();
            Assert.Throws<ObjectDisposedException>(() => raw.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress));
        }
        finally { await server.CloseAsync(); await group.StopAsync(); raw.Dispose(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RecoverableAcceptFailuresCompleteTheReadAndRetainItsPermission(bool autoRead, bool socketError)
    {
        var group = new DefaultEventLoopGroup(2);
        Exception error = socketError ? new SocketException((int)SocketError.ConnectionAborted) : new IOException("accept failure");
        var observer = new ObserveParent(); var child = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FaultingAccept(error); IChannel listener = null;
        try
        {
            listener = await new ServerBootstrap().Group(group).ChannelFactory(() => server).Handler(observer)
                .Option(ChannelOption.AUTO_READ, autoRead).ChildHandler(new ActionChannelInitializer<IChannel>(_ => child.TrySetResult()))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            if (!autoRead) listener.Read();
            Assert.Same(error, await observer.Error.Task.WaitAsync(TestContext.Current.CancellationToken));
            Assert.True(listener.IsOpen); Assert.True(listener.IsActive);
            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)listener.LocalAddress, TestContext.Current.CancellationToken);
            await child.Task.WaitAsync(TestContext.Current.CancellationToken);
            var events = await listener.EventLoop.SubmitAsync(() => observer.Events.ToArray());
            Assert.Equal(new[] { "complete", "error", "read", "complete" }, events);
        }
        finally { if (listener != null) await listener.CloseAsync(); await group.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task FatalAcceptFailureClosesAfterReadCompleteAndException()
    {
        var group = new DefaultEventLoopGroup(1); var error = new ArgumentException("invalid accept state");
        var observer = new ObserveParent(); var server = new FaultingAccept(error);
        Assert.True(server.ShouldClose(new IOException("inactive listener")));
        try
        {
            await new ServerBootstrap().Group(group).ChannelFactory(() => server).Handler(observer)
                .ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            await server.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "complete", "error", "inactive" }, observer.Events);
            Assert.Equal(1, server.Attempts); Assert.False(server.IsOpen);
        }
        finally { await group.StopAsync(); }
    }
    private sealed class FaultingAccept(Exception error) : TcpServerSocketChannel
    {
        internal int Attempts;
        internal bool ShouldClose(Exception failure) => CloseOnReadError(failure);
        protected override Task<Socket> AcceptSocketAsync()
            => Interlocked.Increment(ref Attempts) == 1 ? Task.FromException<Socket>(error) : base.AcceptSocketAsync();
    }
    private sealed class ObserveParent : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource<Exception> Error = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Events = new();
        public override void ChannelRead(ChannelHandlerContext ctx, object message) { Events.Add("read"); ctx.FireChannelRead(message); }
        public override void ChannelReadComplete(ChannelHandlerContext ctx) { Events.Add("complete"); ctx.FireChannelReadComplete(); }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception error) { Events.Add("error"); Error.TrySetResult(error); }
        public override void ChannelInactive(ChannelHandlerContext ctx) { Events.Add("inactive"); ctx.FireChannelInactive(); }
    }
    [Fact(Timeout = 10000)]
    public async Task TheServerAllocatorBatchesQueuedAcceptsByMessagesRatherThanBytes()
    {
        var raw = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0)); raw.Listen(128);
        var server = new TcpServerSocketChannel(raw); var group = new DefaultEventLoopGroup(1);
        var collector = new CollectChildren(3); server.Pipeline.AddLast(collector);
        server.Config.SetAutoRead(false);
        var allocator = Assert.IsType<ServerChannelRecvByteBufAllocator>(server.Config.GetRecvByteBufAllocator());
        allocator.MaxMessagesPerRead(2);
        var peers = new List<TcpClient>();
        try
        {
            await group.RegisterAsync(server);
            for (int i = 0; i < 3; i++)
            { var peer = new TcpClient(); peers.Add(peer); await peer.ConnectAsync((IPEndPoint)server.LocalAddress, TestContext.Current.CancellationToken); }
            server.Config.SetAutoRead(true);
            await collector.Done.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { 2, 1 }, collector.Batches);
        }
        finally { await server.CloseAsync(); await group.StopAsync(); foreach (var peer in peers) peer.Dispose(); }
    }
    private sealed class CollectChildren(int expected = 1) : ChannelInboundHandlerAdapter
    {
        private int _total, _batch;
        internal readonly TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<int> Batches = new();
        public override void ChannelRead(ChannelHandlerContext ctx, object message)
        {
            var child = (IChannel)message; _total++; _batch++;
            _ = RegisterAndClose(child, ctx.Channel.EventLoop); First.TrySetResult();
            if (_total == expected) ctx.Channel.Config.SetAutoRead(false);
        }
        private static async Task RegisterAndClose(IChannel child, IEventLoop owner) { await owner.RegisterAsync(child); await child.CloseAsync(); }
        public override void ChannelReadComplete(ChannelHandlerContext ctx)
        { Batches.Add(_batch); _batch = 0; if (_total == expected) Done.TrySetResult(); }
    }

    [Fact(Timeout = 10000)]
    public async Task APartialAcceptBatchIsDeliveredBeforeItsErrorAndRequiresANewManualRead()
    {
        var raw = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0)); raw.Listen(128);
        var server = new SecondAcceptFails(raw); var group = new DefaultEventLoopGroup(1);
        var collector = new PartialCollector(); server.Pipeline.AddLast(collector); server.Config.SetAutoRead(false);
        using var first = new TcpClient(); using var second = new TcpClient();
        try
        {
            await group.RegisterAsync(server);
            await first.ConnectAsync((IPEndPoint)server.LocalAddress, TestContext.Current.CancellationToken);
            await second.ConnectAsync((IPEndPoint)server.LocalAddress, TestContext.Current.CancellationToken);
            server.Config.SetAutoRead(true);
            await collector.Error.Task.WaitAsync(TestContext.Current.CancellationToken);
            await server.EventLoop.SubmitAsync(() => { });
            Assert.Equal(new[] { "read", "complete", "error" }, collector.Events); Assert.Equal(2, server.Attempts);
            server.Read();
            await collector.Done.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "read", "complete", "error", "read", "complete" }, collector.Events);
            Assert.Equal(3, server.Attempts);
        }
        finally { await server.CloseAsync(); await group.StopAsync(); }
    }
    private sealed class SecondAcceptFails(Socket socket) : TcpServerSocketChannel(socket)
    {
        internal int Attempts;
        protected override Task<Socket> AcceptSocketAsync() => Interlocked.Increment(ref Attempts) == 2
            ? Task.FromException<Socket>(new IOException("second accept failure")) : base.AcceptSocketAsync();
    }
    private sealed class PartialCollector : ChannelInboundHandlerAdapter
    {
        private int _read;
        internal readonly List<string> Events = new();
        internal readonly TaskCompletionSource Error = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelRead(ChannelHandlerContext ctx, object message)
        {
            Events.Add("read"); _read++; ctx.Channel.Config.SetAutoRead(false);
            _ = RegisterAndClose((IChannel)message, ctx.Channel.EventLoop);
        }
        private static async Task RegisterAndClose(IChannel child, IEventLoop owner) { await owner.RegisterAsync(child); await child.CloseAsync(); }
        public override void ChannelReadComplete(ChannelHandlerContext ctx) { Events.Add("complete"); if (_read == 2) Done.TrySetResult(); }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception error) { Events.Add("error"); Error.TrySetResult(); }
    }

    [Fact(Timeout = 10000)]
    public async Task InterruptionDoesNotReplaceTheWorkerOrPreventImmediateAndDelayedWork()
    {
        var group = new DefaultEventLoopGroup(1); var loop = group.Next(); int worker = 0, count = 0;
        try
        {
            await loop.SubmitAsync(() =>
            {
                worker = Environment.CurrentManagedThreadId;
                // Interrupt the thread which should not end-up in a busy spin and
                // so the selector should not have been rebuild.
                Thread.CurrentThread.Interrupt();
            });
            await loop.SubmitAsync(() => { Assert.Equal(worker, Environment.CurrentManagedThreadId); count++; });
            await loop.ScheduleAsync(() => { Assert.Equal(worker, Environment.CurrentManagedThreadId); count++; }, TimeSpan.FromSeconds(2))
                .WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, count);
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task RawSocketConnectionCompletionCanDispatchToTheOwnerWithoutASelectorFacade()
    {
        var group = new DefaultEventLoopGroup(1); var server = new TcpServerSocketChannel();
        server.Pipeline.AddLast(new CollectChildren());
        try
        {
            await group.RegisterAsync(server); await server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(server.LocalAddress, TestContext.Current.CancellationToken);
            Assert.True(await server.EventLoop.SubmitAsync(() => server.EventLoop.InEventLoop()));
        }
        finally { await server.CloseAsync(); await group.StopAsync(); }
    }
    [Fact]
    public async Task UnsupportedTypedOptionsKeepClrValidationAndDoNotFabricateAJdkOption()
    {
        var group = new DefaultEventLoopGroup(1); var server = new TcpServerSocketChannel();
        try
        {
            Assert.False(server.Config.TryGetOption(ChannelOption.IP_MULTICAST_IF, out var value)); Assert.Null(value);
            Assert.Throws<ArgumentNullException>(() => server.Config.SetOption(ChannelOption.IP_MULTICAST_IF, null));
        }
        finally { await group.RegisterAsync(server); await server.CloseAsync(); await group.StopAsync(); }
    }
}
