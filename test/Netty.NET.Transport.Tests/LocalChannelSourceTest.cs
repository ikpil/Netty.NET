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
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Common.Concurrent;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Tests;

public class LocalChannelSourceTest
{
#pragma warning disable xUnit1051 // Source cancellation/close order is explicit; cleanup must complete independently.
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
            var e = await Assert.ThrowsAnyAsync<ClosedChannelException>(() => client.WriteAndFlushAsync(new object()));
            // Ensure that the actual write attempt on a closed channel was never made by asserting that
            // the ClosedChannelException has been created by AbstractUnsafe rather than transport implementations.
            // CLR: AbstractUnsafe is folded into AbstractChannel; its origin is the first, remote stack frame.
            if (!string.IsNullOrEmpty(e.StackTrace))
            {
                Assert.StartsWith("   at " + typeof(AbstractChannel).FullName + ".", e.StackTrace);
            }
            // CLR: also observe the transport override itself.
            Assert.Equal(0, client.WriteAttempts);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
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
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
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
                .Handler(new ActionChannelInitializer<IChannel>(_ =>
                { /* Do nothing */
                }));
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
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
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
            await client.EventLoop.SubmitAsync(() =>
            {
                connect = copy.ConnectAsync(server.LocalAddress);
            });
            await asserted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await connect;
            Assert.True(connect.IsCompletedSuccessfully);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
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
            await client.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(client.IsOpen);
            Task close = client.CloseAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            await ObserveCompletion(connection, TimeSpan.FromSeconds(5));
        }
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
            await server.EventLoop.SubmitAsync(() =>
            {
                _ = server.CloseAsync();
                return client.CloseAsync();
            }).WaitAsync(TimeSpan.FromSeconds(5));
            // CLR: await the native bootstrap's asynchronous result propagation after the source close order.
            await Assert.ThrowsAnyAsync<ClosedChannelException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(connection.IsCompleted);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            await ObserveCompletion(connection, TimeSpan.FromSeconds(5));
        }
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
            await Assert.ThrowsAnyAsync<ClosedChannelException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            // Accepting the queued connection now must not reopen or activate the client, and the accepted
            // channel must be closed.
            server.Config.SetAutoRead(true);
            await child.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(child.IsActive);
            Assert.False(client.IsOpen);
            Assert.False(client.IsActive);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            await ObserveCompletion(connection, TimeSpan.FromSeconds(5));
        }
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
            await client.EventLoop.SubmitAsync(() =>
            {
            });
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
        var children = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        await children.StopAsync();
        var server = await new ServerBootstrap().Group(groups.Shared, children).Channel<LocalServerChannel>()
            .ChildHandler(new TestHandler()).BindAsync(TestAddress);
        var (client, connection) = ConnectPendingClient(groups.Shared);
        try
        {
            var error = await Assert.ThrowsAsync<SocketException>(() => connection.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            await client.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(client.IsOpen);
            Task close = client.CloseAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            await ObserveCompletion(connection, TimeSpan.FromSeconds(5));
        }
    }

    // Client and server share the single event loop of sharedGroup, so the connection is queued by the time
    // newLocalChannel(...) returns.
    private static Task<IChannel> BindServerWithAutoReadDisabled(IEventLoopGroup group, TaskCompletionSource<LocalChannel> accepted)
    {
        return new ServerBootstrap().Group(group).ChannelFactory(() => new ObservedLocalServer(accepted))
            .Option(ChannelOption.AUTO_READ, false).ChildHandler(new TestHandler()).BindAsync(TestAddress);
    }

    private static (LocalChannel Channel, Task<IChannel> Completion) ConnectPendingClient(IEventLoopGroup group)
    {
        // CLR: capture the actual channel through its factory instead of exposing a fake ChannelFuture.channel().
        var channel = new LocalChannel();
        return (channel, new Bootstrap().Group(group).ChannelFactory(() => channel).Handler(new TestHandler()).ConnectAsync(TestAddress));
    }

    private static async Task ObserveCompletion(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout);
        }
        catch when (task.IsCompleted)
        {
            /* Source Future.await() observes completion without requiring success. */
        }
    }
    private sealed class CountingWriteLocalChannel : LocalChannel
    {
        internal int WriteAttempts;
        protected override void DoWrite(ChannelOutboundBuffer @in)
        {
            WriteAttempts++;
            base.DoWrite(@in);
        }
    }
    private sealed class CheckConnectCompletion(Func<Task> connect, TaskCompletionSource asserted) : TestHandler
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
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
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            _ = ctx.CloseAsync();
            closed.TrySetResult();
        }
    }
    private sealed class DiscardHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            // discard
        }
    }
    private sealed class ObservedLocalServer(TaskCompletionSource<LocalChannel> accepted) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        {
            var child = base.NewLocalChannel(peer);
            accepted.TrySetResult(child);
            return child;
        }
    }
    private sealed class ArrivingLocalServer(TaskCompletionSource arrived) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        {
            arrived.TrySetResult();
            return base.NewLocalChannel(peer);
        }
    }
    private sealed class LocalTaskGatedGroup(ManualResetEventSlim closeLatch) : DefaultEventLoopGroup(1)
    {
        private readonly ManualResetEventSlim _closeLatch = closeLatch;
        protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
        {
            return new LocalTaskGatedLoop(this, executor, _closeLatch);
        }
    }
    private sealed class LocalTaskGatedLoop(IEventLoopGroup parent, Action<Action> executor, ManualResetEventSlim closeLatch)
        : DefaultEventLoop(parent, executor)
    {
        public override void Execute(Action task)
        {
            var owner = task.Method.DeclaringType;
            /* Only slow down the anonymous class in LocalChannel#doRegister() */
            if (owner == typeof(LocalChannel) || owner?.DeclaringType == typeof(LocalChannel))
                base.Execute(() =>
                {
                    Assert.True(closeLatch.Wait(TimeSpan.FromSeconds(5)));
                    task();
                });
            else
                base.Execute(task);
        }
    }
#pragma warning restore xUnit1051

#pragma warning disable xUnit1051 // Preserve original read/cancellation timing and finish channel cleanup independently.
#pragma warning disable CS0612 // Original tests intentionally exercise the deprecated MAX_MESSAGES_PER_READ option.
    [Fact]
    public async Task TestNotLeakBuffersWhenCloseByRemotePeer()
    {
        await using var groups = new Groups();
        var cb = new Bootstrap().Group(groups.Shared).Channel<LocalChannel>().Handler(new DropEchoClient());
        var sb = new ServerBootstrap().Group(groups.Shared).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new SplitEchoAndClose())));
        IChannel server = null;
        LocalChannel client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = (LocalChannel)await cb.ConnectAsync(server.LocalAddress);
            // Close the channel
            await CloseChannel(client);
            Assert.True(client.inboundBuffer.IsEmpty);
            await CloseChannel(server);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestAutoReadDisabledSharedGroup()
    {
        await using var groups = new Groups();
        await CheckAutoReadDisabled(groups.Shared, groups.Shared, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestAutoReadDisabledDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckAutoReadDisabled(groups.First, groups.Second, TestContext.Current.CancellationToken);
    }

    private static async Task CheckAutoReadDisabled(IEventLoopGroup serverGroup, IEventLoopGroup clientGroup, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var counter = new EchoCounter(completed, 100);
        var cb = new Bootstrap().Group(serverGroup).Channel<LocalChannel>()
            .Option(ChannelOption.AUTO_READ, false).Handler(new ManualEchoClient());
        var sb = new ServerBootstrap().Group(clientGroup).Channel<LocalServerChannel>()
            .ChildOption(ChannelOption.AUTO_READ, false)
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new ManualEchoServer(counter))));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            client = await cb.ConnectAsync(TestAddress);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.Shared, groups.Shared, true, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithoutAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.Shared, groups.Shared, false, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.First, groups.Second, true, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithoutAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.First, groups.Second, false, TestContext.Current.CancellationToken);
    }

    private static async Task CheckMaxMessagesPerRead(IEventLoopGroup serverGroup, IEventLoopGroup clientGroup, bool autoRead, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cb = new Bootstrap().Group(serverGroup).Channel<LocalChannel>()
            .Option(ChannelOption.AUTO_READ, autoRead).Option(ChannelOption.MAX_MESSAGES_PER_READ, 1)
            .Handler(new ChannelReadHandler(completed, autoRead));
        var sb = new ServerBootstrap().Group(clientGroup).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new WriteTenMessages())));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            client = await cb.ConnectAsync(TestAddress);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.Shared, groups.Shared, true, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithoutAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.Shared, groups.Shared, false, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.First, groups.Second, true, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithoutAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.First, groups.Second, false, TestContext.Current.CancellationToken);
    }

    private static async Task CheckServerMaxMessagesPerRead(IEventLoopGroup serverGroup, IEventLoopGroup clientGroup, bool autoRead, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cb = new Bootstrap().Group(serverGroup).Channel<LocalChannel>().Handler(new ActionChannelInitializer<IChannel>(_ =>
        { /* NOOP */
        }));
        var sb = new ServerBootstrap().Group(clientGroup).Channel<LocalServerChannel>()
            .Option(ChannelOption.AUTO_READ, autoRead).Option(ChannelOption.MAX_MESSAGES_PER_READ, 1)
            .Handler(new ChannelReadHandler(completed, autoRead)).ChildHandler(new ActionChannelInitializer<IChannel>(_ =>
            { /* NOOP */
            }));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    client = await cb.ConnectAsync(TestAddress);
                }
                finally
                {
                    await CloseChannel(client);
                }
            }
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            await CloseChannel(server);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithQueuedConnections()
    {
        await using var groups = new Groups();
        const int maxMessagesPerRead = 2;
        const int connections = 5;
        int remaining = connections;
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCompletes = System.Threading.Channels.Channel.CreateUnbounded<int>();
        var connectFutures = new List<Task<IChannel>>();
        // CLR: Task<IChannel> exposes the channel on completion, so capture allocated clients for failure cleanup.
        var clients = new ConcurrentQueue<LocalChannel>();
        var cb = new Bootstrap().Group(groups.Shared).ChannelFactory(() =>
        {
            var client = new LocalChannel();
            clients.Enqueue(client);
            return client;
        }).Handler(new ActionChannelInitializer<IChannel>(_ =>
        { /* NOOP */
        }));
        var sb = new ServerBootstrap().Group(groups.Shared).ChannelFactory(() => new QueuedLocalServer(() =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                queued.TrySetResult();
        })).Option(ChannelOption.AUTO_READ, false).Option(ChannelOption.MAX_MESSAGES_PER_READ, maxMessagesPerRead)
            .Handler(new ReadCompleteCounts(readCompletes.Writer)).ChildHandler(new ActionChannelInitializer<IChannel>(_ =>
            { /* NOOP */
            }));
        IChannel server = null;
        try
        {
            server = await sb.BindAsync(TestAddress);
            for (int i = 0; i < connections; i++)
                connectFutures.Add(cb.ConnectAsync(TestAddress));
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            server.Config.SetAutoRead(true);
            Assert.Equal(maxMessagesPerRead, await readCompletes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(maxMessagesPerRead, await readCompletes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(connections - 2 * maxMessagesPerRead, await readCompletes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            await Task.WhenAll(connectFutures);
        }
        finally
        {
            foreach (var client in clients)
                await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    [Fact]
    public async Task TestReadCompleteCalledOnHandle()
    {
        await using var groups = new Groups();
        var serverCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cb = new Bootstrap().Group(groups.Shared).Channel<LocalChannel>().Handler(new ActionChannelInitializer<IChannel>(_ =>
        { /* NOOP */
        }));
        var sb = new ServerBootstrap().Group(groups.Shared).Channel<LocalServerChannel>()
            .Option(ChannelOption.RECVBUF_ALLOCATOR, new ReadCompleteRecvAllocator(serverCompleted))
            .ChildHandler(new ActionChannelInitializer<IChannel>(_ =>
            { /* NOOP */
            }))
            .ChildOption(ChannelOption.RECVBUF_ALLOCATOR, new ReadCompleteRecvAllocator(childCompleted));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            try
            {
                client = await cb.ConnectAsync(TestAddress);
                await client.WriteAndFlushAsync("msg");
            }
            finally
            {
                await CloseChannel(client);
            }
            await Task.WhenAll(serverCompleted.Task, childCompleted.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await CloseChannel(server);
        }
    }

    private sealed class DropEchoClient : SimpleChannelInboundHandler<ByteBuf>
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer().WriteZero(100));
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf buffer)
        {
            // Just drop the buffer
        }
    }
    private sealed class SplitEchoAndClose : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf buffer)
        {
            while (buffer.IsReadable())
            {
                // Fill the ChannelOutboundBuffer with multiple buffers
                _ = ctx.WriteAsync(buffer.ReadRetainedSlice(1));
            }
            // Flush and so transfer the written buffers to the inboundBuffer of the remote peer.
            // After this point the remote peer is responsible to release all the buffers.
            ctx.Flush();
            // This close call will trigger the remote peer close as well.
            _ = ctx.CloseAsync();
        }
    }
    private static void WriteAndFlushReadOnSuccess(IChannelHandlerContext ctx, object msg)
    {
        var write = ctx.WriteAndFlushAsync(msg);
        if (write.IsCompletedSuccessfully)
            ctx.Read();
        else
            _ = ReadAfterWriteAsync();

        async Task ReadAfterWriteAsync()
        {
            try
            {
                await write.ConfigureAwait(false);
            }
            catch
            {
                return;
            }
            // CLR: a Task continuation does not confer ownership of the original executor.
            await ctx.Executor.SubmitAsync(() => ctx.Read()).ConfigureAwait(false);
        }
    }
    private sealed class ManualEchoClient : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            WriteAndFlushReadOnSuccess(ctx, "test");
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            WriteAndFlushReadOnSuccess(ctx, msg);
        }
    }
    private sealed class EchoCounter(TaskCompletionSource completed, int remaining)
    {
        internal bool Signal()
        {
            if (--remaining > 0)
                return true;
            completed.TrySetResult();
            return false;
        }
    }
    private sealed class ManualEchoServer(EchoCounter counter) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.Read();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (counter.Signal())
                WriteAndFlushReadOnSuccess(ctx, msg);
        }
    }
    private sealed class WriteTenMessages : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            for (int i = 0; i < 10; i++)
                _ = ctx.WriteAsync(i);
            ctx.Flush();
        }
    }
    private sealed class ChannelReadHandler(TaskCompletionSource completed, bool autoRead) : ChannelInboundHandlerAdapter
    {
        private int _read;
        private int _remaining = 5;
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!autoRead)
                ctx.Read();
            ctx.FireChannelActive();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Assert.Equal(0, _read);
            _read++;
            ctx.FireChannelRead(msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            Assert.Equal(1, _read);
            if (_remaining > 0 && --_remaining == 0)
                completed.TrySetResult();
            if (_remaining > 0)
            {
                if (!autoRead)
                {
                    // The read will be scheduled 100ms in the future to ensure we not receive any
                    // channelRead calls in the meantime.
                    _ = ctx.Executor.ScheduleAsync(() =>
                    {
                        _read = 0;
                        ctx.Read();
                    }, TimeSpan.FromMilliseconds(100));
                }
                else
                    _read = 0;
            }
            else
                _read = 0;
            ctx.FireChannelReadComplete();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            // CLR: surface original callback assertion failures to the awaiting test as well as the pipeline.
            completed.TrySetException(cause);
            ctx.FireExceptionCaught(cause);
            _ = ctx.CloseAsync();
        }
    }
    private sealed class QueuedLocalServer(Action queued) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        {
            var child = base.NewLocalChannel(peer);
            queued();
            return child;
        }
    }
    private sealed class ReadCompleteCounts(System.Threading.Channels.ChannelWriter<int> counts) : ChannelInboundHandlerAdapter
    {
        private int _messagesRead;
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _messagesRead++;
            ctx.FireChannelRead(msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            counts.TryWrite(_messagesRead);
            _messagesRead = 0;
            ctx.FireChannelReadComplete();
        }
    }
    private sealed class ReadCompleteRecvAllocator(TaskCompletionSource completed) : DefaultMaxMessagesRecvByteBufAllocator
    {
        public override IRecvByteBufAllocator.IHandle NewHandle()
        {
            return new Handle(this, completed);
        }
        private sealed class Handle(DefaultMaxMessagesRecvByteBufAllocator owner, TaskCompletionSource completed) : MaxMessageHandle(owner)
        {
            public override int Guess()
            {
                return 128;
            }

            public override void ReadComplete()
            {
                base.ReadComplete();
                completed.TrySetResult();
            }
        }
    }
#pragma warning restore CS0612, xUnit1051

#pragma warning disable xUnit1051
    private static readonly LocalAddress TestAddress = new(typeof(LocalChannelSourceTest));
    [Fact]
    public async Task TestLocalAddressReuse()
    {
        await using var groups = new Groups();
        for (int i = 0; i < 2; i++)
        {
            var clientBootstrap = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var serverBootstrap = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new TestHandler())));
            IChannel server = null, client = null;
            try
            {
                // Start server
                server = await serverBootstrap.BindAsync(TestAddress);
                var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Connect to the server
                client = await clientBootstrap.ConnectAsync(server.LocalAddress).WaitAsync(TimeSpan.FromSeconds(10));
                IChannel clientCopy = client;
                client.EventLoop.Execute(() =>
                {
                    // Send a message event up the pipeline.
                    clientCopy.Pipeline.FireChannelRead("Hello, World");
                    latch.TrySetResult();
                });
                await latch.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Close the channel
                await CloseChannel(client);
                await CloseChannel(server);
                await server.CloseFuture;
                Assert.Null(LocalChannelRegistry.Get(TestAddress));
            }
            finally
            {
                await CloseChannel(client);
                await CloseChannel(server);
            }
        }
    }

    [Fact]
    public async Task TestConnectionRefused()
    {
        await using var groups = new Groups();
        var bootstrap = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
        var error = await Assert.ThrowsAsync<SocketException>(() => bootstrap.ConnectAsync(LocalAddress.ANY));
        Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
    }

    [Fact]
    public async Task TestConnectedAlready()
    {
        await using var groups = new Groups();
        Exception cause = null;
        var clientBootstrap = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new ExceptionCatcher(error => cause = error));
        var serverBootstrap = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new TestHandler())));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await serverBootstrap.BindAsync(TestAddress);
            // Connect to the server
            client = await clientBootstrap.ConnectAsync(server.LocalAddress).WaitAsync(TimeSpan.FromSeconds(10));
            var error = await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(server.LocalAddress));
            // CLR: SocketError.IsConnected stands for AlreadyConnectedException.
            Assert.Equal(SocketError.IsConnected, error.SocketErrorCode);
            await client.CloseAsync();
            Assert.Null(cause);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    private static Task CloseChannel(IChannel channel)
    {
        return channel?.CloseAsync() ?? Task.CompletedTask;
    }
    private class TestHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.SafeRelease(msg);
        }
    }
    private sealed class ExceptionCatcher(Action<Exception> caught) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            caught(cause);
        }
    }
    private sealed class Groups : IAsyncDisposable
    {
        internal readonly MultiThreadIoEventLoopGroup First = new(2, LocalIoHandler.NewFactory());
        internal readonly MultiThreadIoEventLoopGroup Second = new(2, LocalIoHandler.NewFactory());
        internal readonly MultiThreadIoEventLoopGroup Shared = new(1, LocalIoHandler.NewFactory());
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(First.StopAsync(), Second.StopAsync(), Shared.StopAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
#pragma warning restore xUnit1051

#pragma warning disable xUnit1051 // Preserve original per-operation timing and independently complete cleanup.
    [Fact]
    public async Task TestCloseInWritePromiseCompletePreservesOrder()
    {
        await using var groups = new Groups();
        var messages = new MessageCount(2);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(new MatchRead(data, messages, inactive: true))));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            IChannel copy = client;
            // Make sure a write operation is executed in the eventloop
            var completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () => copy.Pipeline.LastContext.CloseAsync());
            await messages.WaitAsync();
            await completion;
            Assert.False(client.IsOpen);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            data.Release();
        }
    }

    [Fact]
    public async Task TestCloseAfterWriteInSameEventLoopPreservesOrder()
    {
        await using var groups = new Groups();
        var messages = new MessageCount(3);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        var echo = new EchoThenClose(data, messages);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.Shared).Channel<LocalChannel>().Handler(new MatchRead(data, messages, writeOnActive: true));
            var sb = new ServerBootstrap().Group(groups.Shared).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(echo)));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            await messages.WaitAsync();
            Assert.False(client.IsOpen);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            // The source callback leaves this inbound duplicate owned by the fixture. Clean it up only after
            // all original assertions and close ordering, without changing the original callback's data flow.
            ReferenceCountUtil.SafeRelease(echo.Unreleased);
            data.Release();
        }
    }

    [Fact]
    public async Task TestWriteInWritePromiseCompletePreservesOrder()
    {
        await using var groups = new Groups();
        var messages = new MessageCount(2);
        var data = Unpooled.Buffer().WriteInt(sizeof(int)).WriteInt(2);
        var data2 = Unpooled.Buffer().WriteInt(sizeof(int)).WriteInt(1);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(new ReadFrames(messages))));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            IChannel copy = client;
            // Make sure a write operation is executed in the eventloop
            Task completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () => copy.WriteAndFlushAsync(data2.RetainedDuplicate()));
            await messages.WaitAsync();
            await completion;
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            data.Release();
            data2.Release();
        }
    }

    [Fact]
    public async Task TestPeerWriteInWritePromiseCompleteDifferentEventLoopPreservesOrder()
    {
        await using var groups = new Groups();
        await CheckPeerWriteInCompletion(groups.First, groups.Second, false);
    }

    [Fact]
    public async Task TestPeerWriteInWritePromiseCompleteSameEventLoopPreservesOrder()
    {
        await using var groups = new Groups();
        await CheckPeerWriteInCompletion(groups.Shared, groups.Shared, true);
    }

    private static async Task CheckPeerWriteInCompletion(IEventLoopGroup clients, IEventLoopGroup servers, bool enforceOrder)
    {
        var messages = new MessageCount(2);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        var data2 = Unpooled.WrappedBuffer(new byte[512]);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(clients).Channel<LocalChannel>()
                .Handler(new MatchRead(data2, messages, expectedCount: enforceOrder ? 1 : null));
            var sb = new ServerBootstrap().Group(servers).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child =>
                {
                    child.Pipeline.AddLast(new MatchRead(data, messages, expectedCount: enforceOrder ? 2 : null));
                    accepted.TrySetResult(child);
                }));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Make sure a write operation is executed in the eventloop
            var completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () => peer.WriteAndFlushAsync(data2.RetainedDuplicate()));
            await messages.WaitAsync();
            await completion;
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            data.Release();
            data2.Release();
        }
    }

    [Fact]
    public async Task TestWriteWhilePeerIsClosedReleaseObjectAndFailPromise()
    {
        await using var groups = new Groups();
        var messageRead = new MessageCount(1);
        var writeFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        var data2 = Unpooled.WrappedBuffer(new byte[512]);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child =>
                {
                    child.Pipeline.AddLast(new MatchRead(data, messageRead));
                    accepted.TrySetResult(child);
                }));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            IChannel copy = client;
            Task peerWrite = null;
            // Make sure a write operation is executed in the eventloop
            var completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () =>
            {
                peerWrite = peer.EventLoop.SubmitAsync(async () =>
                {
                    // The point of this test is to write while the peer is closed, so we should
                    // ensure the peer is actually closed before we write.
                    int waitCount = 0;
                    while (copy.IsOpen)
                    {
                        try
                        {
                            Thread.Sleep(50);
                        }
                        catch (ThreadInterruptedException)
                        {
                            // ignored
                        }
                        Assert.True(++waitCount <= 5);
                    }
                    try
                    {
                        await peer.WriteAndFlushAsync(data2.RetainedDuplicate());
                    }
                    catch (ClosedChannelException)
                    {
                        writeFailed.TrySetResult();
                    }
                });
                return copy.CloseAsync();
            });
            await messageRead.WaitAsync();
            await writeFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await peer.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5));
            await client.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5));
            await completion;
            if (peerWrite != null)
                await peerWrite;
            Assert.False(client.IsOpen);
            Assert.False(peer.IsOpen);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            data.Release();
            data2.Release();
        }
    }

    private static Task WriteThenOnOwner(IChannel channel, ByteBuf message, Func<Task> completed)
    {
        // Native Task consumers cannot install a caller-owned ChannelPromise. Queue Write without flushing,
        // attach its continuation first, then Flush on the original owner. Explicitly dispatch callback state
        // access back to that owner; ExecuteSynchronously cannot override an asynchronous producer's policy.
        return channel.EventLoop.SubmitAsync(() =>
        {
            Task write = channel.WriteAsync(message);
            Task callback = write.ContinueWith(_ => channel.EventLoop.SubmitAsync(completed), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            channel.Flush();
            return callback;
        });
    }
    private sealed class MessageCount(int remaining)
    {
        private int _remaining = remaining;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Current => Math.Max(0, Volatile.Read(ref _remaining));
        internal void Signal()
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
                _done.TrySetResult();
        }

        internal Task WaitAsync()
        {
            return _done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
    private sealed class MatchRead(ByteBuf expected, MessageCount count, bool inactive = false, bool writeOnActive = false, int? expectedCount = null)
        : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (writeOnActive)
                _ = ctx.WriteAndFlushAsync(expected.RetainedDuplicate());
            else
                ctx.FireChannelActive();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (expected.Equals(msg) && (!expectedCount.HasValue || count.Current == expectedCount.Value))
            {
                ReferenceCountUtil.SafeRelease(msg);
                count.Signal();
            }
            else
            {
                ctx.FireChannelRead(msg);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            if (inactive)
                count.Signal();
            ctx.FireChannelInactive();
        }
    }
    private sealed class EchoThenClose(ByteBuf data, MessageCount count) : ChannelInboundHandlerAdapter
    {
        internal object Unreleased;
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (data.Equals(msg))
            {
                Unreleased = msg;
                count.Signal();
                _ = ctx.WriteAndFlushAsync(data.RetainedDuplicate());
                _ = ctx.CloseAsync();
            }
            else
                ctx.FireChannelRead(msg);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            count.Signal();
            ctx.FireChannelInactive();
        }
    }
    private sealed class ReadFrames(MessageCount count) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is not ByteBuf buffer)
            {
                ctx.FireChannelRead(msg);
                return;
            }
            while (buffer.IsReadable())
            {
                int size = buffer.ReadInt();
                var slice = buffer.ReadRetainedSlice(size);
                try
                {
                    if (slice.ReadInt() == count.Current)
                    {
                        count.Signal();
                    }
                }
                finally
                {
                    slice.Release();
                }
            }
            buffer.Release();
        }
    }
#pragma warning restore xUnit1051
}
