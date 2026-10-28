/*
 * Copyright 2016 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051, xUnit1069 // Helpers preserve original timeouts and use the test cancellation token.
public class ChannelInitializerTest : IAsyncLifetime
{
    private const int TIMEOUT_MILLIS = 1000;
    private static readonly LocalAddress SERVER_ADDRESS = new(typeof(ChannelInitializerTest));
    private readonly DefaultEventLoopGroup _group = new(1);
    private readonly ServerBootstrap _server;
    private readonly Bootstrap _client;
    private readonly InspectableHandler _testHandler = new();
    public ChannelInitializerTest()
    {
        _server = new ServerBootstrap().Group(_group).Channel<LocalServerChannel>().LocalAddress(SERVER_ADDRESS);
        _client = new Bootstrap().Group(_group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter());
    }
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync() => await _group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(TIMEOUT_MILLIS));
    [Fact]
    public async Task TestInitChannelThrowsRegisterFirst() => await TestInitChannelThrows(true);
    [Fact]
    public async Task TestInitChannelThrowsRegisterAfter() => await TestInitChannelThrows(false);
    private async Task TestInitChannelThrows(bool registerFirst)
    {
        var error = new Exception();
        Exception cause = null;
        var pipeline = new LocalChannel().Pipeline;
        if (registerFirst)
            await _group.RegisterAsync(pipeline.Channel);
        pipeline.AddFirst(new ThrowingInitializer(error, value => cause = value));
        if (!registerFirst)
            await _group.RegisterAsync(pipeline.Channel);
        await pipeline.Channel.CloseAsync();
        await pipeline.Channel.CloseCompletion;
        Assert.Same(error, cause);
    }
    [Fact]
    public async Task TestChannelInitializerInInitializerCorrectOrdering()
    {
        var handler1 = new ChannelInboundHandlerAdapter();
        var handler2 = new ChannelInboundHandlerAdapter();
        var handler3 = new ChannelInboundHandlerAdapter();
        var handler4 = new ChannelInboundHandlerAdapter();
        _client.Handler(new ActionChannelInitializer<IChannel>(channel =>
        {
            channel.Pipeline.AddLast(handler1);
            channel.Pipeline.AddLast(new ActionChannelInitializer<IChannel>(ch => { ch.Pipeline.AddLast(handler2); ch.Pipeline.AddLast(handler3); }));
            channel.Pipeline.AddLast(handler4);
        })).LocalAddress(LocalAddress.ANY);
        IChannel channel = await _client.BindAsync();
        try
        {
            // Execute some task on the EventLoop and wait until its done to be sure all handlers are added to the
            // pipeline.
            await channel.EventLoop.SubmitAsync(() =>
            {
                // NOOP
            });
            using var handlers = channel.Pipeline.GetEnumerator();
            Assert.True(handlers.MoveNext());
            Assert.Same(handler1, handlers.Current.Value);
            Assert.True(handlers.MoveNext());
            Assert.Same(handler2, handlers.Current.Value);
            Assert.True(handlers.MoveNext());
            Assert.Same(handler3, handlers.Current.Value);
            Assert.True(handlers.MoveNext());
            Assert.Same(handler4, handlers.Current.Value);
            Assert.False(handlers.MoveNext());
        }
        finally { await channel.CloseAsync(); }
    }
    [Fact]
    public async Task TestChannelInitializerReentrance()
    {
        int registeredCalled = 0, initChannelCalled = 0;
        var handler = new RegisteredHandler(() => Interlocked.Increment(ref registeredCalled));
        _client.Handler(new ActionChannelInitializer<IChannel>(channel =>
        { Interlocked.Increment(ref initChannelCalled); channel.Pipeline.AddLast(handler); channel.Pipeline.FireChannelRegistered(); }))
            .LocalAddress(LocalAddress.ANY);
        IChannel channel = await _client.BindAsync();
        try
        {
            // Execute some task on the EventLoop and wait until its done to be sure all handlers are added to the
            // pipeline.
            await channel.EventLoop.SubmitAsync(() =>
            {
                // NOOP
            });
            Assert.Equal(1, initChannelCalled);
            Assert.Equal(2, registeredCalled);
        }
        finally { await channel.CloseAsync(); }
    }
    [Fact(Timeout = TIMEOUT_MILLIS)]
    public async Task FirstHandlerInPipelineShouldReceiveChannelRegisteredEvent() => await TestChannelRegisteredEventPropagation(
        new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddFirst(_testHandler)));
    [Fact(Timeout = TIMEOUT_MILLIS)]
    public async Task LastHandlerInPipelineShouldReceiveChannelRegisteredEvent() => await TestChannelRegisteredEventPropagation(
        new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(_testHandler)));
    [Fact]
    public void TestAddFirstChannelInitializer() => TestAddChannelInitializer(true);
    [Fact]
    public void TestAddLastChannelInitializer() => TestAddChannelInitializer(false);
    private static void TestAddChannelInitializer(bool first)
    {
        bool called = false;
        var channel = new EmbeddedChannel(new ActionChannelInitializer<IChannel>(ch =>
        {
            var handler = new ActionChannelInitializer<IChannel>(_ => called = true);
            if (first)
                ch.Pipeline.AddFirst(handler);
            else
                ch.Pipeline.AddLast(handler);
        }));
        channel.Finish();
        Assert.True(called);
    }
    private async Task TestChannelRegisteredEventPropagation(ChannelInitializer<LocalChannel> initializer)
    {
        IChannel clientChannel = null, serverChannel = null;
        try
        {
            serverChannel = await _server.ChildHandler(initializer).BindAsync();
            clientChannel = await _client.ConnectAsync(SERVER_ADDRESS);
            Assert.Equal(1, _testHandler.ChannelRegisteredCount);
        }
        finally { await CloseChannel(clientChannel); await CloseChannel(serverChannel); }
    }
    [Fact(Timeout = 10000)]
    public async Task TestChannelInitializerEventExecutor()
    {
        int invokeCount = 0, completeCount = 0;
        Exception error = null;
        var address = new LocalAddress(typeof(ChannelInitializerTest));
        var executor = new AlternatingExecutor();
        using var latch = new CountdownEvent(1);
        IChannel server = null, client = null;
        try
        {
            var bootstrap = new ServerBootstrap().Channel<LocalServerChannel>().Group(_group).LocalAddress(address)
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(ch =>
                    ch.Pipeline.AddLast(executor, "initializer", new ExecutorInitializer(
                        () => Interlocked.Increment(ref invokeCount), () => Interlocked.Increment(ref completeCount),
                        value => error = value, latch))));
            server = await bootstrap.BindAsync();
            client = await new Bootstrap().Channel<LocalChannel>().Group(_group).RemoteAddress(address)
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync();
            await client.WriteAndFlushAsync("Hello World");
            await client.CloseAsync();
            await server.CloseAsync();
            await client.CloseCompletion;
            await server.CloseCompletion;
            // Wait until the handler is removed from the pipeline and so no more events are handled by it.
            Assert.True(latch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(1, invokeCount);
            Assert.Equal(invokeCount, completeCount);
            if (error != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
        Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
    }
    private static Task CloseChannel(IChannel channel) => channel?.CloseAsync() ?? Task.CompletedTask;
    private sealed class ThrowingInitializer(Exception error, Action<Exception> capture) : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel channel) => throw error;
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) { capture(cause); base.ExceptionCaught(context, cause); }
    }
    private sealed class RegisteredHandler(Action callback) : ChannelInboundHandlerAdapter
    { public override void ChannelRegistered(ChannelHandlerContext context) => callback(); }
    private sealed class InspectableHandler : ChannelDuplexHandler
    {
        internal int ChannelRegisteredCount;
        public override void ChannelRegistered(ChannelHandlerContext context)
        { Interlocked.Increment(ref ChannelRegisteredCount); context.FireChannelRegistered(); }
    }
    private sealed class ExecutorInitializer(Action invoked, Action complete,
        Action<Exception> capture, CountdownEvent latch) : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel channel)
        {
            invoked();
            var context = channel.Pipeline.Context(this);
            Assert.NotNull(context);
            channel.Pipeline.AddAfter(context.Executor, context.Name, null, new RemovedHandler(latch));
            complete();
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => capture(cause);
    }
    private sealed class RemovedHandler(CountdownEvent latch) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            // just drop on the floor.
        }
        public override void HandlerRemoved(ChannelHandlerContext context) => latch.Signal();
    }
    private sealed class AlternatingExecutor : AbstractEventExecutor
    {
        private readonly DefaultEventLoop _worker = new();
        private int _calls;
        public override bool InEventLoop(Thread thread)
        {
            // Return false every other time which will ensure we hit the execute(...) path
            // CLR: retain the intended alternating admission check using a real single worker instead of
            // the original Java helper's ScheduledExecutorService and shadowed thread parameter.
            return _worker.InEventLoop(thread) && (Interlocked.Increment(ref _calls) & 1) == 0;
        }
        public override void Execute(Action task) => _worker.Execute(task);
        public override bool IsShuttingDown() => false;
        public override bool IsShutdown() => _worker.IsShutdown();
        public override bool IsTerminated() => _worker.IsTerminated();
        public override Task Termination => _worker.Termination;
        public override bool AwaitTermination(TimeSpan timeout) => _worker.AwaitTermination(timeout);
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => _worker.ShutdownGracefullyAsync(quietPeriod, timeout);
        [Obsolete]
        public override void Shutdown() => _ = _worker.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }
}
#pragma warning restore xUnit1051, xUnit1069
