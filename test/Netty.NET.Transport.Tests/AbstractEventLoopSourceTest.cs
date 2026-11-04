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
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independently owned source group/channel cleanup.
public class AbstractEventLoopSourceTest
{
    /**
     * Test for https://github.com/netty/netty/issues/803
     */
    [Fact(Timeout = 10000)]
    public async Task TestReregister()
    {
        var group = new DefaultEventLoopGroup(2);
        var group2 = new DefaultEventLoopGroup(2);
        var executors = new DefaultEventExecutorGroup(2);
        IChannel server = null;
        try
        {
            server = await new ServerBootstrap().Channel<TcpServerSocketChannel>().Group(group)
                .ChildHandler(new ChannelInboundHandlerAdapter()).Handler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    ch.Pipeline.AddLast("handler", new TestChannelHandler());
                    ch.Pipeline.AddLast(executors, "other", new TestChannelHandler2());
                })).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            var executor = server.Pipeline.Context(typeof(TestChannelHandler2)).Executor;
            var executor1 = server.Pipeline.Context(typeof(TestChannelHandler)).Executor;
            await server.DeregisterAsync();
            await group2.RegisterAsync(server);
            var executorNew = server.Pipeline.Context(typeof(TestChannelHandler)).Executor;
            Assert.NotSame(executor1, executorNew);
            Assert.Same(executor, server.Pipeline.Context(typeof(TestChannelHandler2)).Executor);
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (server != null) await server.CloseAsync();
            await group.StopAsync(); await group2.StopAsync(); await executors.StopAsync();
        }
    }
    /**
     * Test for https://github.com/netty/netty/issues/14923
     */
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestReregisterOnChannelHandlerContext(bool throughChannel)
    {
        var group = new DefaultEventLoopGroup(2);
        Exception failure = null;
        IChannel server = null, client = null;
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    ch.Pipeline.AddLast("logging", new LoggingHandler());
                    ch.Pipeline.AddLast(new ReRegisterHandler(group, throughChannel, error => failure = error));
                })).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            client = await new Bootstrap().Group(group).Channel<TcpSocketChannel>().Handler(new ClientReader(error => failure = error))
                .ConnectAsync(server.LocalAddress);
            _ = CloseServerAfterClient(client, server);
            await client.WriteAndFlushAsync(Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes("hello")));
            await server.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Null(failure);
        }
        finally
        {
            if (client != null) await client.CloseAsync();
            if (server != null) await server.CloseAsync();
            await group.StopAsync();
        }
    }
    private static async Task CloseServerAfterClient(IChannel client, IChannel server)
    { await client.CloseCompletion; await server.CloseAsync(); }
    private sealed class LoggingHandler : ChannelInboundHandlerAdapter { }
    private sealed class ReRegisterHandler(DefaultEventLoopGroup group, bool throughChannel, Action<Exception> failed) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext ctx, object message) => _ = ReRegister(ctx, message);
        private async Task ReRegister(ChannelHandlerContext outCtx, object message)
        {
            bool handedOff = false;
            try
            {
                var oldLoop = outCtx.Channel.EventLoop;
                IEventLoop next; do { next = group.Next(); } while (ReferenceEquals(next, oldLoop));
                // Source listener callbacks run on their owner; Task continuations regain that owner explicitly.
                if (throughChannel) await outCtx.Channel.DeregisterAsync(); else await outCtx.DeregisterAsync();
                await oldLoop.SubmitAsync(() => outCtx.Channel.Pipeline.FireUserEventTriggered("Unregistered user event"));
                await next.RegisterAsync(outCtx.Channel);
                await next.SubmitAsync(() =>
                {
                    outCtx.Pipeline.AddLast(new EchoThroughOriginalContext(outCtx));
                    handedOff = true; outCtx.FireChannelRead(message);
                });
            }
            catch (Exception error)
            {
                if (!handedOff) ((ByteBuf)message).Release();
                failed(error); await outCtx.CloseAsync();
            }
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause)
        { ctx.FireExceptionCaught(cause); failed(cause); _ = ctx.CloseAsync(); }
    }
    private sealed class EchoThroughOriginalContext(ChannelHandlerContext outCtx) : ChannelInboundHandlerAdapter
    { public override void ChannelRead(ChannelHandlerContext ctx, object message) => _ = outCtx.WriteAndFlushAsync(message); }
    private sealed class ClientReader(Action<Exception> failed) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext ctx, object message) { ((ByteBuf)message).Release(); _ = ctx.CloseAsync(); }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause)
        { failed(cause); ctx.FireExceptionCaught(cause); _ = ctx.CloseAsync(); }
    }

    [Fact(Timeout = 5000)]
    public async Task TestShutdownGracefullyNoQuietPeriod()
    {
        var group = new DefaultEventLoopGroup(2);
        try
        {
            // Not close the Channel to ensure the EventLoop is still shutdown in time.
            await new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>().ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            var shutdown = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromMinutes(1));
            await shutdown.WaitAsync(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
            Assert.True(shutdown.IsCompletedSuccessfully); Assert.True(group.IsShutdown()); Assert.True(group.IsTerminated());
        }
        finally { await group.StopAsync(); }
    }
    private sealed class TestChannelHandler : ChannelDuplexHandler { }
    private sealed class TestChannelHandler2 : ChannelDuplexHandler
    { public override void ChannelRead(ChannelHandlerContext ctx, object message) { } }
}
