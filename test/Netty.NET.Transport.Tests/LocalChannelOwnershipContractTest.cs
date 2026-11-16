using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Finish both owners and release retained data independently of test cancellation.
public class LocalChannelOwnershipContractTest
{
    [Fact]
    public async Task SecondConnectWhileTheFirstIsPendingFailsWithConnectionPending()
    {
        var group = new DefaultEventLoopGroup(1);
        IChannel server = null, client = null;
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .Option(ChannelOption.AUTO_READ, false).ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(LocalAddress.ANY);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            Task first = null, second = null;
            // The server does not accept yet, so LocalUnsafe.connect keeps its connectPromise pending.
            IChannel copy = client;
            await client.EventLoop.SubmitAsync(() =>
            {
                first = copy.ConnectAsync(server.LocalAddress);
                second = copy.ConnectAsync(server.LocalAddress);
            });
            var error = await Assert.ThrowsAsync<SocketException>(() => second);
            // CLR: SocketError.AlreadyInProgress stands for ConnectionPendingException.
            Assert.Equal(SocketError.AlreadyInProgress, error.SocketErrorCode);
            Assert.False(first.IsCompleted);
            server.Config.SetAutoRead(true);
            await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(client.IsActive);
        }
        finally
        {
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task RefusedConnectionsCarryTheSourceMessages()
    {
        var group = new DefaultEventLoopGroup(1);
        var accepted = new TaskCompletionSource<LocalChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null;
        var unbound = new LocalAddress(Guid.NewGuid().ToString());
        try
        {
            var refused = await Assert.ThrowsAsync<SocketException>(() => new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(unbound));
            Assert.Equal(SocketError.ConnectionRefused, refused.SocketErrorCode);
            Assert.Equal("connection refused: " + unbound, refused.Message);

            server = await new ServerBootstrap().Group(group).ChannelFactory(() => new ObservedQueuedServer(accepted))
                .Option(ChannelOption.AUTO_READ, false).ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(LocalAddress.ANY);
            Task<IChannel> connect = new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress);
            await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // Closing the server closes the queued accepted channel, which fails the connect in LocalChannel.tryClose.
            await server.CloseAsync();
            var closed = await Assert.ThrowsAsync<SocketException>(() => connect.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SocketError.ConnectionRefused, closed.SocketErrorCode);
            Assert.Equal("connection refused: the accepted channel was closed", closed.Message);
        }
        finally
        {
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task ReentrantReadsAreBoundedByTheReaderStackDepth()
    {
        const int messages = 20;
        var group = new DefaultEventLoopGroup(1);
        var reader = new ReadAgainOnRead(messages);
        IChannel server = null, client = null;
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildOption(ChannelOption.AUTO_READ, false)
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(child => child.Pipeline.AddLast(reader)))
                .BindAsync(LocalAddress.ANY);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress);
            await reader.Active.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            for (int i = 0; i < messages; i++)
                _ = client.WriteAsync(i);
            client.Flush();
            await reader.Done.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // Each channelRead() calls read() again, so doBeginRead() nests readInbound() until
            // MAX_READER_STACK_DEPTH (8) and then continues through readTask from a new event-loop task.
            Assert.Equal(messages, reader.Depths.Count);
            Assert.Equal(8, reader.Depths.Max());
            Assert.Contains(0, reader.Depths.GetRange(1, messages - 1));
            Assert.Equal(0, await group.SubmitAsync(() => InternalThreadLocalMap.Get().LocalChannelReaderStackDepth()));
        }
        finally
        {
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task PendingInitializerReceivesRegisteredBeforePipelineDestruction()
    {
        var group = new DefaultEventLoopGroup(1);
        var executor = new DefaultEventLoop();
        using var release = new ManualResetEventSlim();
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = new RemovedReadMarker(removed);
        int initializationCount = 0;
        IChannel server = null, client = null;
        executor.Execute(() => Assert.True(release.Wait(TimeSpan.FromSeconds(5))));
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(child =>
                {
                    child.Pipeline.AddLast(executor, "initializer", new ActionChannelInitializer<LocalChannel>(channel =>
                    {
                        Interlocked.Increment(ref initializationCount);
                        var context = channel.Pipeline.Context("initializer");
                        channel.Pipeline.AddAfter(context.Executor, context.Name, "marker", marker);
                        initialized.TrySetResult(channel.IsRegistered);
                    }));
                    accepted.TrySetResult(child);
                })).BindAsync(LocalAddress.ANY);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await client.WriteAndFlushAsync("Hello World");
            await client.CloseAsync();
            await peer.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // AbstractUnsafe.close completes closeFuture before its deferred deregistration (#4435) runs on the loop.
            var deadline = Stopwatch.StartNew();
            while (peer.IsRegistered && deadline.Elapsed < TimeSpan.FromSeconds(5))
                await peer.EventLoop.SubmitAsync(() => { });
            Assert.False(peer.IsRegistered);
            // Source ChannelInitializerTest requires registration, read and removal to survive an offloaded
            // initializer. Force HandlerAdded to run only after close so it must receive queued Registered.
            release.Set();
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(await initialized.Task);
            Assert.Equal(1, initializationCount);
            Assert.Equal(1, marker.Reads);
            Assert.Equal(1, marker.Removals);
        }
        finally
        {
            release.Set();
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            // CLR: fireChannelRegistered hops to the offloaded initializer's executor first (as in the source), which
            // may still forward one queued event to the channel loop; drain that executor while the group still runs.
            await executor.StopAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task CanceledPendingConnectKeepsCanceledTaskStatus()
    {
        var group = new DefaultEventLoopGroup(1);
        using var cancellation = new CancellationTokenSource();
        var queued = new TaskCompletionSource<LocalChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;
        try
        {
            server = await new ServerBootstrap().Group(group).ChannelFactory(() => new ObservedQueuedServer(queued))
                .Option(ChannelOption.AUTO_READ, false).ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(LocalAddress.ANY);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            var submitted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.EventLoop.Execute(() => submitted.SetResult(client.ConnectAsync(server.LocalAddress,
                cancellationToken: cancellation.Token)));
            // Observe the operation itself, rather than an off-owner async dispatch wrapper.
            Task connect = await submitted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.True(connect.IsCanceled);
            Assert.False(client.IsOpen);
            Assert.False(client.IsActive);
            var child = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            server.Config.SetAutoRead(true);
            await child.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(child.IsOpen);
            Assert.False(child.IsActive);
            Assert.False(client.IsActive);
        }
        finally
        {
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task ReentrantPeerWriteCannotDeliverTheReplyBeforeTheOriginalWrite()
    {
        var group = new DefaultEventLoopGroup(1);
        var sender = new LocalChannel();
        IChannel server = null;
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(child =>
                {
                    child.Pipeline.AddLast(new RecordRead("server", events, received));
                    accepted.TrySetResult(child);
                })).BindAsync(LocalAddress.ANY);
            var client = await new Bootstrap().Group(group).ChannelFactory(() => sender)
                .Handler(new RecordRead("client", events, received)).ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task;
            Task reply = null;
            // The source promise listener can write to the peer before the original doWrite exits.
            // Native Task continuations run asynchronously, so run the reply from the release in in.remove(), the
            // same frame boundary: doWrite still has writeInProgress set and has not called finishPeerRead(peer).
            var message = new ReleaseHookMessage(() => reply = peer.WriteAndFlushAsync(2));
            await client.WriteAndFlushAsync(message);
            Assert.Equal(new[] { "server", "client" }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            if (reply != null)
                await reply;
        }
        finally
        {
            await sender.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task CloseDuringPeerRetainCannotPublishIntoAnAlreadyDrainedInboundQueue()
    {
        var writers = new DefaultEventLoopGroup(1);
        var readers = new DefaultEventLoopGroup(1);
        var sender = new LocalChannel();
        IChannel server = null;
        var accepted = new TaskCompletionSource<LocalChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerRetained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownCloseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowOwnClose = new ManualResetEventSlim();
        using var allowPublish = new ManualResetEventSlim();
        var message = new ReentrantRetainMessage(() =>
        {
            peerRetained.TrySetResult();
            Assert.True(allowOwnClose.Wait(TimeSpan.FromSeconds(5)));
            _ = sender.CloseAsync();
            ownCloseStarted.TrySetResult();
            Assert.True(allowPublish.Wait(TimeSpan.FromSeconds(5)));
        });
        try
        {
            server = await new ServerBootstrap().Group(readers).Channel<LocalServerChannel>()
                .ChildOption(ChannelOption.AUTO_READ, false)
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(child => accepted.TrySetResult(child)))
                .BindAsync(LocalAddress.ANY);
            await new Bootstrap().Group(writers).ChannelFactory(() => sender).Handler(new ChannelInboundHandlerAdapter())
                .ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task;
            Task write = sender.WriteAndFlushAsync(message);
            await peerRetained.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await peer.CloseAsync();
            allowOwnClose.Set();
            await ownCloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // Force all close/read notifications to drain before Retain returns and enqueue can resume.
            await peer.EventLoop.SubmitAsync(() => { });
            allowPublish.Set();
            await Assert.ThrowsAnyAsync<ClosedChannelException>(() => write);
            await sender.EventLoop.SubmitAsync(() => { });
            Assert.True(peer.inboundBuffer.IsEmpty);
            Assert.Equal(0, message.RefCnt);
            Assert.Equal(1, message.Deallocations);
        }
        finally
        {
            allowOwnClose.Set();
            allowPublish.Set();
            await sender.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await Task.WhenAll(writers.StopAsync(), readers.StopAsync());
            while (message.RefCnt > 0)
                message.Release();
        }
    }

    private sealed class ObservedQueuedServer(TaskCompletionSource<LocalChannel> queued) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        { var child = base.NewLocalChannel(peer); queued.TrySetResult(child); return child; }
    }
    private sealed class RemovedReadMarker(TaskCompletionSource removed) : ChannelInboundHandlerAdapter
    {
        internal int Reads, Removals;
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        { if (Equals("Hello World", msg)) Reads++; }
        public override void HandlerRemoved(IChannelHandlerContext ctx)
        { Removals++; removed.TrySetResult(); }
    }
    [Sharable]
    private sealed class ReadAgainOnRead(int expected) : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource Active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<int> Depths = new();
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelActive();
            ctx.Read();
            Active.TrySetResult();
        }
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Depths.Add(InternalThreadLocalMap.Get().LocalChannelReaderStackDepth());
            if (Depths.Count == expected)
                Done.TrySetResult();
            ctx.Read();
        }
    }

    // Runs a callback on its first release, which is ChannelOutboundBuffer.remove() releasing the written message.
    internal sealed class ReleaseHookMessage(Action released) : IReferenceCounted
    {
        private Action _released = released;
        private int _refCnt = 1;
        public int RefCnt => Volatile.Read(ref _refCnt);
        public IReferenceCounted Retain() => Retain(1);
        public IReferenceCounted Retain(int increment) { Interlocked.Add(ref _refCnt, increment); return this; }
        public IReferenceCounted Touch() => this;
        public IReferenceCounted Touch(object hint) => this;
        public bool Release() => Release(1);
        public bool Release(int decrement)
        {
            bool deallocated = Interlocked.Add(ref _refCnt, -decrement) == 0;
            Interlocked.Exchange(ref _released, null)?.Invoke();
            return deallocated;
        }
    }
    private sealed class RecordRead(string name, List<string> events, TaskCompletionSource<string[]> received) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        { events.Add(name); if (events.Count == 2) received.TrySetResult(events.ToArray()); }
    }
    private sealed class ReentrantRetainMessage(Action retained) : AbstractReferenceCounted
    {
        private int _retains;
        internal int Deallocations;
        public override IReferenceCounted Retain(int increment)
        {
            base.Retain(increment);
            // The first retain is doWrite publishing the message to the peer (peer.inboundBuffer.add(retain(msg))).
            if (Interlocked.Increment(ref _retains) == 1)
                retained();
            return this;
        }
        public override IReferenceCounted Touch(object hint) => this;
        protected override void Deallocate() => Interlocked.Increment(ref Deallocations);
    }
}
