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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Source cancellation/close order is explicit; cleanup must complete independently.
public partial class LocalChannelSourceTest
{
    [Fact]
    public async Task TestWriteFailsFastOnClosedChannel()
    {
        await using var groups = new Groups();
        var client = new CountingWriteLocalChannel();
        var cb = new Bootstrap().Group(groups.First).ChannelFactory(() => client).Handler(new TestHandler());
        var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(child => child.Pipeline.AddLast(new TestHandler())));
        IChannel server = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            await cb.ConnectAsync(server.LocalAddress);
            // Close the channel and write something.
            await client.CloseAsync();
            await Assert.ThrowsAsync<IOException>(() => client.WriteAndFlushAsync(new object()));
            // Ensure that the actual write attempt on a closed channel was never made by asserting that
            // the ClosedChannelException has been created by AbstractUnsafe rather than transport implementations.
            // CLR: a returned IOException has no JVM synthetic stack. Observe the transport override itself.
            Assert.Equal(0, client.WriteAttempts);
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact]
    public async Task TestServerCloseChannelSameEventLoop()
    {
        await using var groups = new Groups();
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(child => child.Pipeline.AddLast(new CloseOnRead(latch))));
        IChannel server = null, client = null;
        try
        {
            server = await sb.BindAsync(TestAddress);
            var cb = new Bootstrap().Group(groups.Second).Channel<LocalChannel>().Handler(new DiscardHandler());
            client = await cb.ConnectAsync(server.LocalAddress);
            _ = client.WriteAndFlushAsync(new object());
            await latch.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact]
    public async Task LocalChannelRaceCondition()
    {
        await using var groups = new Groups();
        using var closeLatch = new ManualResetEventSlim();
        var clients = new LocalTaskGatedGroup(closeLatch);
        IChannel server = null;
        var client = new LocalChannel();
        try
        {
            server = await new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child =>
                {
                    _ = child.CloseAsync();
                    closeLatch.Set();
                })).BindAsync(TestAddress);
            var cb = new Bootstrap().Group(clients).ChannelFactory(() => client)
                .Handler(new ActionChannelInitializer<IChannel>(_ => { /* Do nothing */ }));
            var future = cb.ConnectAsync(server.LocalAddress);
            await ObserveCompletion(future, TimeSpan.FromMilliseconds(2000));
            Assert.True(future.IsCompleted, "Connection should finish, not time out");
        }
        finally
        {
            closeLatch.Set();
            await CloseChannel(client);
            await CloseChannel(server);
            await clients.StopAsync();
        }
    }

    [Fact]
    public async Task TestReRegister()
    {
        await using var groups = new Groups();
        var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
        var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(child => child.Pipeline.AddLast(new TestHandler())));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            await client.DeregisterAsync();
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact(Timeout = 3000)]
    public async Task TestConnectFutureBeforeChannelActive()
    {
        await using var groups = new Groups();
        var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter());
        var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(new TestHandler())));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            client = await cb.RegisterAsync();
            Task connect = null;
            var asserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Pipeline.AddLast(new CheckConnectCompletion(() => connect, asserted));
            // CLR: a native consumer Task is obtained when starting the operation, rather than supplied
            // beforehand as a mutable ChannelPromise. Start on the channel owner to capture that actual
            // operation Task before the asynchronous local handshake; executor submission has its own Task.
            IChannel copy = client;
            // Connect to the server
            await client.EventLoop.SubmitAsync(() => { connect = copy.ConnectAsync(server.LocalAddress); });
            await asserted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await connect;
            Assert.True(connect.IsCompletedSuccessfully);
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact]
    public async Task TestConnectFailsWhenServerClosedWithQueuedConnection()
    {
        await using var groups = new Groups();
        var accepted = new TaskCompletionSource<LocalChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await BindServerWithAutoReadDisabled(groups.Shared, accepted);
        var (client, connection) = ConnectPendingClient(groups.Shared);
        try
        {
            Assert.NotNull(await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            // The connection is queued but was not accepted yet.
            await server.CloseAsync();
            var error = await Assert.ThrowsAsync<SocketException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            // The connect promise is failed before the channel is closed (as for any refused connection), and
            // await() may return before the close, so wait for it.
            await client.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(client.IsOpen);
            Task close = client.CloseAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
        }
        finally { await CloseChannel(client); await CloseChannel(server); await ObserveCompletion(connection, TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task TestClientCloseAfterServerClosedWithQueuedConnection()
    {
        await using var groups = new Groups();
        var accepted = new TaskCompletionSource<LocalChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await BindServerWithAutoReadDisabled(groups.Shared, accepted);
        var (client, connection) = ConnectPendingClient(groups.Shared);
        try
        {
            Assert.NotNull(await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            // Close the server and then the client in one event loop task, so the client is closed before it
            // learns that the queued connection was closed: closing the server only schedules that notification on
            // the client's event loop. Calling both from this thread would let the event loop run the server's
            // close, and so the notification, before the client's close is submitted.
            await server.EventLoop.SubmitAsync(() => { _ = server.CloseAsync(); return client.CloseAsync(); }).WaitAsync(TimeSpan.FromSeconds(5));
            // CLR: await the native bootstrap's asynchronous result propagation after the source close order.
            await Assert.ThrowsAsync<IOException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(connection.IsCompleted);
        }
        finally { await CloseChannel(client); await CloseChannel(server); await ObserveCompletion(connection, TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task TestClientCloseWhileConnectionIsQueued()
    {
        await using var groups = new Groups();
        var accepted = new TaskCompletionSource<LocalChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await BindServerWithAutoReadDisabled(groups.Shared, accepted);
        var (client, connection) = ConnectPendingClient(groups.Shared);
        try
        {
            var child = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(child);
            Task close = client.CloseAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
            await Assert.ThrowsAsync<IOException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            // Accepting the queued connection now must not reopen or activate the client, and the accepted
            // channel must be closed.
            server.Config.SetAutoRead(true);
            await child.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(child.IsActive);
            Assert.False(client.IsOpen);
            Assert.False(client.IsActive);
        }
        finally { await CloseChannel(client); await CloseChannel(server); await ObserveCompletion(connection, TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task TestConnectFailsWhenServerClosesBeforeConnectionArrives()
    {
        await using var groups = new Groups();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await new ServerBootstrap().Group(groups.Second)
            .ChannelFactory(() => new ArrivingLocalServer(arrived)).ChildHandler(new TestHandler()).BindAsync(TestAddress);
        using var release = new ManualResetEventSlim();
        LocalChannel client = null;
        Task<IChannel> connection = null;
        try
        {
            // Block the server's event loop, so the close below is queued and the server stays in the registry.
            server.EventLoop.Execute(() => Assert.True(release.Wait(TimeSpan.FromSeconds(5))));
            var close = server.CloseAsync();
            (client, connection) = ConnectPendingClient(groups.First);
            // Wait until the client found the server and handed the connection to the server's event loop, behind
            // the close.
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await client.EventLoop.SubmitAsync(() => { });
            release.Set();
            await close;
            var error = await Assert.ThrowsAsync<SocketException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            Task clientClose = client.CloseAsync();
            await clientClose;
            Assert.True(clientClose.IsCompletedSuccessfully);
        }
        finally
        {
            release.Set();
            await CloseChannel(client);
            await CloseChannel(server);
            if (connection != null)
                await ObserveCompletion(connection, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task TestConnectFailsWhenAcceptedChannelCannotBeRegistered()
    {
        await using var groups = new Groups();
        // The child group is terminated, so registering the accepted channel fails. This logs a WARN
        // ("Force-closing a channel whose registration task was not accepted by an event loop") and an ERROR
        // (a listener notification that cannot be submitted to the terminated event loop). Both are expected.
        var children = new DefaultEventLoopGroup(1);
        await children.StopAsync();
        var server = await new ServerBootstrap().Group(groups.Shared, children).Channel<LocalServerChannel>()
            .ChildHandler(new TestHandler()).BindAsync(TestAddress);
        var (client, connection) = ConnectPendingClient(groups.Shared);
        try
        {
            var error = await Assert.ThrowsAsync<SocketException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            await client.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(client.IsOpen);
            Task close = client.CloseAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
        }
        finally { await CloseChannel(client); await CloseChannel(server); await ObserveCompletion(connection, TimeSpan.FromSeconds(5)); }
    }

    // Client and server share the single event loop of sharedGroup, so the connection is queued by the time
    // newLocalChannel(...) returns.
    private static Task<IChannel> BindServerWithAutoReadDisabled(IEventLoopGroup group, TaskCompletionSource<LocalChannel> accepted)
        => new ServerBootstrap().Group(group).ChannelFactory(() => new ObservedLocalServer(accepted))
            .Option(ChannelOption.AUTO_READ, false).ChildHandler(new TestHandler()).BindAsync(TestAddress);

    private static (LocalChannel Channel, Task<IChannel> Completion) ConnectPendingClient(IEventLoopGroup group)
    {
        // CLR: capture the actual channel through its factory instead of exposing a fake ChannelFuture.channel().
        var channel = new LocalChannel();
        return (channel, new Bootstrap().Group(group).ChannelFactory(() => channel).Handler(new TestHandler()).ConnectAsync(TestAddress));
    }
    private static async Task ObserveCompletion(Task task, TimeSpan timeout)
    {
        try
        { await task.WaitAsync(timeout); }
        catch when (task.IsCompleted) { /* Source Future.await() observes completion without requiring success. */ }
    }
    private sealed class CountingWriteLocalChannel : LocalChannel
    {
        internal int WriteAttempts;
        protected override Task DoWriteAsync(object message) { WriteAttempts++; return base.DoWriteAsync(message); }
    }
    private sealed class CheckConnectCompletion(Func<Task> connect, TaskCompletionSource asserted) : TestHandler
    {
        public override void ChannelActive(ChannelHandlerContext context)
        {
            // Ensure the promise was done before the handler method is triggered.
            if (connect()?.IsCompleted == true)
                asserted.TrySetResult();
            else
                asserted.TrySetException(new InvalidOperationException("connect promise should be done"));
        }
    }
    private sealed class CloseOnRead(TaskCompletionSource closed) : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        { _ = context.CloseAsync(); closed.TrySetResult(); }
    }
    private sealed class DiscardHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        {
            // discard
        }
    }
    private sealed class ObservedLocalServer(TaskCompletionSource<LocalChannel> accepted) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        { var child = base.NewLocalChannel(peer); accepted.TrySetResult(child); return child; }
    }
    private sealed class ArrivingLocalServer(TaskCompletionSource arrived) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        { arrived.TrySetResult(); return base.NewLocalChannel(peer); }
    }
    private sealed class LocalTaskGatedGroup(ManualResetEventSlim closeLatch) : DefaultEventLoopGroup(1)
    {
        private readonly ManualResetEventSlim _closeLatch = closeLatch;
        protected override IEventExecutor NewChild(Action<Action> executor, params object[] args) => new LocalTaskGatedLoop(this, executor, _closeLatch);
    }
    private sealed class LocalTaskGatedLoop(IEventExecutorGroup parent, Action<Action> executor, ManualResetEventSlim closeLatch)
        : DefaultEventLoop(parent, executor)
    {
        public override void Execute(Action task)
        {
            var owner = task.Method.DeclaringType;
            /* Only slow down the anonymous class in LocalChannel#doRegister() */
            if (owner == typeof(LocalChannel) || owner?.DeclaringType == typeof(LocalChannel))
                base.Execute(() => { Assert.True(closeLatch.Wait(TimeSpan.FromSeconds(5))); task(); });
            else
                base.Execute(task);
        }
    }
}
