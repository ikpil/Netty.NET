using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Finish both owners and release retained data independently of test cancellation.
public class LocalChannelOwnershipContractTest
{
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
            await peer.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
            await group.StopAsync();
            await executor.StopAsync();
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
            await child.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
        var sender = new ReentrantWriteCompletionChannel();
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
            // Force that same frame boundary without changing the native Task producer's dispatch policy.
            sender.BeforeCompletion = () => reply = peer.WriteAndFlushAsync(2);
            await client.WriteAndFlushAsync(1);
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
            await Assert.ThrowsAsync<IOException>(() => write);
            await sender.EventLoop.SubmitAsync(() => { });
            Assert.True(peer.IsInboundBufferEmpty);
            Assert.Equal(0, message.ReferenceCount);
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
            while (message.ReferenceCount > 0)
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
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { if (Equals("Hello World", message)) Reads++; }
        public override void HandlerRemoved(ChannelHandlerContext context)
        { Removals++; removed.TrySetResult(); }
    }
    private sealed class ReentrantWriteCompletionChannel : LocalChannel
    {
        internal Action BeforeCompletion;
        protected override void DoWriteComplete()
        {
            Interlocked.Exchange(ref BeforeCompletion, null)?.Invoke();
            base.DoWriteComplete();
        }
    }
    private sealed class RecordRead(string name, List<string> events, TaskCompletionSource<string[]> received) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { events.Add(name); if (events.Count == 2) received.TrySetResult(events.ToArray()); }
    }
    private sealed class ReentrantRetainMessage(Action retained) : AbstractReferenceCounted
    {
        private int _retains;
        internal int Deallocations;
        public override IReferenceCounted Retain(int increment)
        {
            base.Retain(increment);
            if (Interlocked.Increment(ref _retains) == 2)
                retained();
            return this;
        }
        public override IReferenceCounted Touch(object hint) => this;
        protected override void Deallocate() => Interlocked.Increment(ref Deallocations);
    }
}
