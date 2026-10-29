using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class PendingWriteQueueContractTest
{
    [Fact]
    public async Task CancelledQueuedWriteRetainsOwnershipUntilRemovalAndIsNotForwarded()
    {
        var channel = new EmbeddedChannel(new ChannelOutboundHandlerAdapter());
        var queue = new PendingWriteQueue(channel.Pipeline.LastContext);
        var message = Unpooled.Buffer(1).WriteByte(1);
        using var cancellation = new CancellationTokenSource();
        Task write = queue.Add(message, cancellation.Token);
        cancellation.Cancel();
        Assert.True(write.IsCanceled);
        Assert.Equal(1, message.ReferenceCount);
        Assert.False(queue.IsEmpty);
        Task forwarded = queue.RemoveAndWriteAsync();
        channel.Flush();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => forwarded);
        Assert.Equal(0, message.ReferenceCount);
        Assert.Equal(0, queue.Bytes);
        Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }
    [Fact]
    public async Task CancellationAfterTransferStillCancelsAnUnflushedTransportWrite()
    {
        var channel = new EmbeddedChannel(new ChannelOutboundHandlerAdapter());
        var queue = new PendingWriteQueue(channel.Pipeline.LastContext);
        var message = Unpooled.Buffer(1).WriteByte(1);
        using var cancellation = new CancellationTokenSource();
        Task write = queue.Add(message, cancellation.Token);
        Task forwarded = queue.RemoveAndWriteAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Same(write, forwarded);
        channel.Flush();
        Assert.Equal(0, message.ReferenceCount);
        var placeholder = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(placeholder);
        Assert.Equal(0, placeholder.ReadableBytes);
        placeholder.Release();
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }
    [Fact]
    public async Task CancellationAfterFlushDoesNotCancelAnInFlightTransportWrite()
    {
        var channel = new DelayedEmbeddedChannel();
        channel.Pipeline.AddLast(new ChannelOutboundHandlerAdapter());
        var queue = new PendingWriteQueue(channel.Pipeline.LastContext);
        var message = Unpooled.Buffer(1).WriteByte(42);
        using var cancellation = new CancellationTokenSource();
        Task write = queue.Add(message, cancellation.Token);
        _ = queue.RemoveAndWriteAsync();
        channel.Flush();
        Assert.False(write.IsCompleted);
        cancellation.Cancel();
        Assert.False(write.IsCompleted);
        channel.AllowWrite.TrySetResult();
        await write.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(write.IsCompletedSuccessfully);
        var output = channel.ReadOutbound<ByteBuf>();
        Assert.Same(message, output);
        output.Release();
        Assert.Equal(0, message.ReferenceCount);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void RemoveReturnsProducerOwnershipAndWithdrawsQueueCancellation()
    {
        var channel = new EmbeddedChannel(new ChannelOutboundHandlerAdapter());
        var queue = new PendingWriteQueue(channel.Pipeline.LastContext);
        var message = Unpooled.Buffer(1).WriteByte(1);
        var producer = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();
        queue.Add(message, producer, cancellation.Token);
        Assert.Same(producer, queue.Remove());
        cancellation.Cancel();
        Assert.False(producer.Task.IsCompleted);
        Assert.Equal(0, message.ReferenceCount);
        Assert.True(queue.IsEmpty);
        producer.SetResult();
        Assert.True(producer.Task.IsCompletedSuccessfully);
        Assert.False(channel.Finish());
    }
    [Fact]
    public async Task NativeSocketCompletionReturnsProducerCompletionToItsExecutor()
    {
        var group = new DefaultEventLoopGroup(1);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var handler = new QueueHandler();
        IChannel channel = null;
        try
        {
            var accepting = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            channel = await new Bootstrap().Group(group).Channel<TcpSocketChannel>().Handler(handler)
                .ConnectAsync(listener.LocalEndpoint, cancellationToken: TestContext.Current.CancellationToken);
            using var peer = await accepting;
            var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task write = null;
            await channel.EventLoop.SubmitAsync(() =>
            {
                var producer = new TaskCompletionSource();
                _ = producer.Task.ContinueWith(_ => callback.TrySetResult(channel.EventLoop.InEventLoop()),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                handler.Queue.Add(Unpooled.Buffer(1).WriteByte(42), producer);
                write = handler.Queue.RemoveAndWriteAsync();
                channel.Flush();
            }, TestContext.Current.CancellationToken);
            var data = new byte[1];
            await peer.GetStream().ReadExactlyAsync(data, TestContext.Current.CancellationToken);
            Assert.Equal(42, data[0]);
            Assert.True(await callback.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            await write.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            if (channel != null)
                await channel.CloseAsync(TestContext.Current.CancellationToken);
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }
    private sealed class DelayedEmbeddedChannel : EmbeddedChannel
    {
        internal readonly TaskCompletionSource AllowWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task DoWriteAsync(object message)
        { await AllowWrite.Task.ConfigureAwait(false); await base.DoWriteAsync(message).ConfigureAwait(false); }
    }
    [Fact]
    public async Task OutputShutdownDetachesPendingAccountingAndMakesTheChannelUnwritable()
    {
        var group = new DefaultEventLoopGroup(1);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        IChannel channel = null;
        try
        {
            var accepting = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            channel = await new Bootstrap().Group(group).Channel<TcpSocketChannel>().Handler(new ChannelInboundHandlerAdapter())
                .ConnectAsync(listener.LocalEndpoint, cancellationToken: TestContext.Current.CancellationToken);
            using var peer = await accepting;
            await ((TcpSocketChannel)channel).ShutdownOutputAsync();
            Assert.True(channel.IsOpen);
            Assert.False(channel.IsWritable);
            Assert.Equal(0, channel.BytesBeforeUnwritable);
            Assert.Equal(long.MaxValue, channel.BytesBeforeWritable);
            Task pending = null;
            await channel.EventLoop.SubmitAsync(() =>
            {
                var queue = new PendingWriteQueue(channel);
                var message = Unpooled.Buffer(1).WriteByte(1);
                pending = queue.Add(message);
                Assert.True(queue.Bytes > 0);
                Assert.Equal(0, ((AbstractChannel)channel).OutboundBuffer.TotalPendingWriteBytes);
                queue.RemoveAndFailAll(new IOException("Pending owner cleanup."));
                Assert.Equal(0, message.ReferenceCount);
                Assert.Equal(0, queue.Bytes);
                Assert.Equal(0, ((AbstractChannel)channel).OutboundBuffer.TotalPendingWriteBytes);
            }, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<IOException>(() => pending);
            Assert.Equal(0, await peer.GetStream().ReadAsync(new byte[1], TestContext.Current.CancellationToken));
        }
        finally
        {
            if (channel != null)
                await channel.CloseAsync(TestContext.Current.CancellationToken);
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }
    private sealed class QueueHandler : ChannelOutboundHandlerAdapter
    {
        internal PendingWriteQueue Queue;
        public override void HandlerAdded(ChannelHandlerContext context) => Queue = new PendingWriteQueue(context);
    }
}
