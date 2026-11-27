using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

public class LocalCoalescingContractTest
{
    [Fact]
    public async Task LateEmptyPeerReadNotificationDoesNotConsumeTheNextManualRead()
    {
        var writers = new DefaultEventLoopGroup(1);
        var readers = new DefaultEventLoopGroup(1);
        var sender = new LocalChannel();
        using var resume = new ManualResetEventSlim();
        var completionHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The release in ChannelOutboundBuffer.remove() runs inside doWrite, before finishPeerRead(peer) posts the
        // peer-read notification; holding it there delays that notification past the receiver's own reads.
        var held = new LocalChannelOwnershipContractTest.ReleaseHookMessage(() =>
        {
            completionHeld.TrySetResult();
            if (!resume.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out releasing the delayed peer-read notification.");
        });
        IChannel server = null;
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            server = await new ServerBootstrap().Group(readers).Channel<LocalServerChannel>()
                .ChildOption(ChannelOption.AUTO_READ, false)
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel =>
                {
                    channel.Pipeline.AddLast(new ReceiveTwo(received));
                    accepted.TrySetResult(channel);
                })).BindAsync(LocalAddress.ANY, TestContext.Current.CancellationToken);
            var client = await new Bootstrap().Group(writers).ChannelFactory(() => sender).Handler(new ChannelInboundHandlerAdapter())
                .ConnectAsync(server.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            var peer = await accepted.Task;
            Task first = client.WriteAndFlushAsync(held, TestContext.Current.CancellationToken);
            await completionHeld.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await peer.EventLoop.SubmitAsync(() =>
            {
                peer.Read(); // Consume the already enqueued message before the sender posts its notification.
                peer.Read(); // Arm the next explicit read while the receiver queue is empty.
            }, TestContext.Current.CancellationToken);
            resume.Set();
            await first;
            await sender.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            await peer.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            await client.WriteAndFlushAsync(2, TestContext.Current.CancellationToken);
            Assert.Equal(new[] { 1, 2 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            resume.Set();
#pragma warning disable xUnit1051 // Close both owners even after a failing baseline or test cancellation.
            await sender.CloseAsync();
            if (server != null)
                await server.CloseAsync();
#pragma warning restore xUnit1051
            await Task.WhenAll(writers.StopAsync(), readers.StopAsync()).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task CoalescingReadsEverySegmentWithoutConsumingRetainedSourceIndices()
    {
        var group = new DefaultEventLoopGroup(1);
        IChannel server = null, client = null;
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[] { 0, 1, 2 }), true)
            .AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4, 9 }), true);
        first.SetIndex(1, 5);
        var second = Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[] { 5 }), true)
            .AddComponent(Unpooled.WrappedBuffer(new byte[] { 6 }), true);
        var last = Unpooled.WrappedBuffer(new byte[] { 7 });
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildOption(ChannelOption.AUTO_READ, false)
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel =>
                {
                    channel.Pipeline.AddLast(new Receive(received));
                    accepted.TrySetResult(channel);
                })).BindAsync(LocalAddress.ANY, TestContext.Current.CancellationToken);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter())
                .ConnectAsync(server.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            var peer = await accepted.Task;
            // Caller references keep the source indices observable after the receiver's ownership is released.
            Task firstWrite = client.WriteAsync(first.Retain(), TestContext.Current.CancellationToken);
            Task secondWrite = client.WriteAsync(second.Retain(), TestContext.Current.CancellationToken);
            Task lastWrite = client.WriteAsync(last, TestContext.Current.CancellationToken);
            client.Flush();
            await Task.WhenAll(firstWrite, secondWrite, lastWrite);
            await peer.EventLoop.SubmitAsync(() => peer.Read(), TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(1, first.ReaderIndex);
            Assert.Equal(0, second.ReaderIndex);
            Assert.Equal(1, first.RefCnt);
            Assert.Equal(1, second.RefCnt);
            Assert.Equal(0, last.RefCnt);
        }
        finally
        {
#pragma warning disable xUnit1051 // Cleanup must complete even after test cancellation.
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
#pragma warning restore xUnit1051
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // Also release leaked references when running this regression against the broken baseline.
            while (first.RefCnt > 0)
                first.Release();
            while (second.RefCnt > 0)
                second.Release();
            while (last.RefCnt > 0)
                last.Release();
        }
    }

    private sealed class Receive(TaskCompletionSource<byte[]> received) : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            var bytes = new byte[msg.ReadableBytes];
            msg.GetBytes(msg.ReaderIndex, bytes);
            received.TrySetResult(bytes);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            received.TrySetException(cause);
        }
    }
    private sealed class ReceiveTwo(TaskCompletionSource<int[]> received) : ChannelInboundHandlerAdapter
    {
        private readonly List<int> _values = new();
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _values.Add(msg is LocalChannelOwnershipContractTest.ReleaseHookMessage ? 1 : (int)msg);
            if (_values.Count == 2)
                received.TrySetResult(_values.ToArray());
        }
    }
}
