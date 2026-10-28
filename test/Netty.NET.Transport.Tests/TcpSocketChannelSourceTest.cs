/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051, xUnit1069 // Original default-token operations use bounded waits; registration helpers consume the test token.
public class TcpSocketChannelSourceTest
{
    [Fact]
    /**
     * Reproduces the issue #1679
     */
    public async Task TestFlushAfterGatheredFlush()
    {
        var group = new DefaultEventLoopGroup(1);
        IChannel server = null;
        try
        {
            var bootstrap = new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>();
            bootstrap.ChildHandler(new FlushAfterGatheredHandler());
            server = await bootstrap.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)server.LocalAddress).Port);
            var bytes = new byte[3];
            await client.GetStream().ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("abc", Encoding.ASCII.GetString(bytes));
        }
        finally
        {
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private sealed class FlushAfterGatheredHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context)
        {
            // Trigger a gathering write by writing two buffers.
            _ = context.WriteAsync(Unpooled.Buffer(1).WriteByte('a'));
            Task write = context.WriteAsync(Unpooled.Buffer(1).WriteByte('b'));
            _ = WriteAfterCompletionAsync(write, context);
            context.Flush();
        }
        private static async Task WriteAfterCompletionAsync(Task write, ChannelHandlerContext context)
        {
            await write.ConfigureAwait(false);
            // This message must be flushed
            await context.WriteAndFlushAsync(Unpooled.Buffer(1).WriteByte('c')).ConfigureAwait(false);
        }
    }

    [Fact]
    /**
     * Reproduces the issue #1600
     */
    public async Task TestFlushCloseReentrance()
    {
        var group = new DefaultEventLoopGroup(1);
        var futures = new ConcurrentQueue<Task>();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null;
        try
        {
            var bootstrap = new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .ChildOption(ChannelOption.SO_SNDBUF, 1024)
                .ChildHandler(new FlushCloseHandler(futures, closed));
            server = await bootstrap.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)server.LocalAddress).Port);
            var bytes = new byte[8192];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (; ; )
            {
                if (await client.GetStream().ReadAsync(bytes, timeout.Token) == 0)
                    break;
                // Wait a little bit so that the write attempts are split into multiple flush attempts.
                await Task.Delay(10, timeout.Token);
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(3, futures.Count);
            Assert.True(futures.TryDequeue(out var first));
            Assert.True(futures.TryDequeue(out var second));
            Assert.True(futures.TryDequeue(out var third));
            Assert.True(first.IsCompletedSuccessfully);
            Assert.True(second.IsCompleted);
            Assert.False(second.IsCompletedSuccessfully);
            await Assert.ThrowsAsync<IOException>(() => second);
            Assert.True(third.IsCompleted);
            Assert.False(third.IsCompletedSuccessfully);
            await Assert.ThrowsAsync<IOException>(() => third);
        }
        finally
        {
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    // Test for https://github.com/netty/netty/issues/4805
    [Fact(Timeout = 30000)]
    public async Task TestChannelReRegisterReadSameEventLoop() => await TestChannelReRegisterRead(true);

    [Fact(Timeout = 30000)]
    public async Task TestChannelReRegisterReadDifferentEventLoop() => await TestChannelReRegisterRead(false);

    private static async Task TestChannelReRegisterRead(bool sameEventLoop)
    {
        var group = new DefaultEventLoopGroup(2);
        using var latch = new CountdownEvent(1);
        // The source creates its completion promise on next(); retain that chooser step while the
        // completion itself is a native TaskCompletionSource with no executor ownership.
        group.Next();
        var eventLoopCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Just some random bytes
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);
        IChannel server = null, client = null;
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .ChildOption(ChannelOption.SO_KEEPALIVE, true)
                .ChildHandler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(
                    new ReRegisterHandler(group, sameEventLoop, latch, eventLoopCheck))))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            client = await new Bootstrap().Group(group).Channel<TcpSocketChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            await client.WriteAndFlushAsync(Unpooled.WrappedBuffer(bytes), TestContext.Current.CancellationToken);
            Assert.True(latch.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            await eventLoopCheck.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }
    private sealed class ReRegisterHandler(DefaultEventLoopGroup group, bool sameEventLoop, CountdownEvent latch,
        TaskCompletionSource eventLoopCheck) : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            // We was able to read something from the Channel after reregister.
            if (!latch.IsSet)
                latch.Signal();
        }
        public override void ChannelActive(ChannelHandlerContext context)
        {
            var loop = group.Next();
            if (sameEventLoop)
                _ = Deregister(context, loop);
            else
                loop.Execute(() => _ = Deregister(context, loop));
        }
        private async Task Deregister(ChannelHandlerContext context, IEventLoop loop)
        {
            // As soon as the channel becomes active re-register it to another
            // EventLoop. After this is done we should still receive the data that
            // was written to the channel.
            try
            {
                await context.DeregisterAsync().ConfigureAwait(false);
                Assert.NotSame(loop, context.Channel.EventLoop);
                eventLoopCheck.TrySetResult();
                await group.Next().RegisterAsync(context.Channel).ConfigureAwait(false);
            }
            catch (Exception error) { eventLoopCheck.TrySetException(error); }
        }
    }
    [Fact(Timeout = 30000)]
    public async Task TestShutdownOutputAndClose()
    {
        var group = new DefaultEventLoopGroup(1);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        TcpClient accepted = null;
        try
        {
            var channel = (TcpSocketChannel)await new Bootstrap().Group(group).Channel<TcpSocketChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(listener.LocalEndpoint,
                    cancellationToken: TestContext.Current.CancellationToken);
            accepted = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await channel.ShutdownOutputAsync();
            await channel.CloseAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            try
            { accepted?.Dispose(); }
            catch (IOException)
            {
                // ignore
            }
            try
            { listener.Stop(); }
            catch (IOException)
            {
                // ignore
            }
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }
    private sealed class FlushCloseHandler(ConcurrentQueue<Task> futures, TaskCompletionSource closed) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context)
        {
            // Write a large enough data so that it is split into two loops.
            Task first = context.WriteAsync(context.Allocator.Buffer().WriteZero(1048576));
            futures.Enqueue(first);
            _ = CloseAfterFirstAsync(first, context);
            futures.Enqueue(context.WriteAsync(context.Allocator.Buffer().WriteZero(1048576)));
            context.Flush();
            futures.Enqueue(context.WriteAsync(context.Allocator.Buffer().WriteZero(1048576)));
            context.Flush();
        }
        private async Task CloseAfterFirstAsync(Task first, ChannelHandlerContext context)
        {
            try
            { await first.ConfigureAwait(false); await context.CloseAsync().ConfigureAwait(false); closed.TrySetResult(); }
            catch (Exception error) { closed.TrySetException(error); }
        }
    }
}
