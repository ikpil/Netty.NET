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
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve original operation deadlines and independently finish cleanup.
#pragma warning disable CS0612 // Original unordered-executor fixture deliberately exercises the deprecated source API.
public class DefaultChannelPipelineEventSourceTest
{
    [Fact(Timeout = 3000)]
    public async Task TestThrowInExceptionCaught()
    {
        await using var fixture = new Fixture();
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ThrowReadComplete(latch);
        await fixture.Register();
        fixture.Channel.Pipeline.AddLast(handler);
        fixture.Channel.Pipeline.FireChannelReadComplete();
        await latch.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Count);
    }
    [Fact(Timeout = 3000)]
    public async Task TestThrowInOtherHandlerAfterInvokedFromExceptionCaught()
    {
        await using var fixture = new Fixture();
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ThrowReadComplete(latch);
        await fixture.Register();
        fixture.Channel.Pipeline.AddLast(new ReadCompleteOnException(), handler);
        fixture.Channel.Pipeline.FireExceptionCaught(new Exception());
        await latch.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task TestFireChannelRegistered()
    {
        await using var fixture = new Fixture();
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Channel.Pipeline.AddLast(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new RegisteredSignal(latch))));
        var registration = fixture.Register();
        await latch.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await registration;
    }
    [Fact]
    public async Task TestFreeCalled()
    {
        var group = new DefaultEventLoopGroup(1);
        var freed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = new ReleasedSignal(freed);
        var handler = new StringInboundHandler();
        IChannel server = null, self = null, peer = null;
        try
        {
            var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildHandler(new PeerReceiver(accepted)).BindAsync(LocalAddress.ANY);
            self = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ActionChannelInitializer<LocalChannel>(ch => ch.Pipeline.AddLast(handler))).ConnectAsync(server.LocalAddress);
            peer = await accepted.Task;
            await server.CloseAsync();
            await peer.WriteAndFlushAsync(holder);
            await freed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(handler.Called);
        }
        finally
        {
            if (peer != null)
                await peer.CloseAsync();
            if (self != null)
                await self.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync();
            if (holder.ReferenceCount != 0)
                holder.Release();
        }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestInboundOperationsViaContext(bool inEventLoop)
    {
        await using var fixture = new Fixture();
        var anchor = new ContextAnchor();
        fixture.Channel.Pipeline.AddLast(anchor);
        await fixture.Register();
        var events = Channel.CreateUnbounded<string>();
        fixture.Channel.Pipeline.AddLast(new InboundEvents(events.Writer));
        var context = fixture.Channel.Pipeline.Context(anchor);
        if (inEventLoop)
            await fixture.Channel.EventLoop.SubmitAsync(() => ExecuteInboundOperations(context));
        else
            ExecuteInboundOperations(context);
        foreach (string name in new[] { "channelRegistered", "channelUnregistered", "channelActive", "channelInactive", "channelRead", "channelReadComplete", "userEventTriggered", "channelWritabilityChanged", "exceptionCaught" })
            Assert.Equal(name, await events.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(events.Reader.TryPeek(out _));
        fixture.Channel.Pipeline.RemoveLast();
    }
    private static void ExecuteInboundOperations(ChannelHandlerContext context)
    {
        context.FireChannelRegistered();
        context.FireChannelUnregistered();
        context.FireChannelActive();
        context.FireChannelInactive();
        context.FireChannelRead("");
        context.FireChannelReadComplete();
        context.FireUserEventTriggered("");
        context.FireChannelWritabilityChanged();
        context.FireExceptionCaught(new Exception());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestOutboundOperationsViaContext(bool inEventLoop)
    {
        await using var fixture = new Fixture();
        var anchor = new ContextAnchor();
        fixture.Channel.Pipeline.AddLast(anchor);
        await fixture.Register();
        var events = Channel.CreateUnbounded<string>();
        fixture.Channel.Pipeline.AddFirst(new OutboundEvents(events.Writer));
        var context = fixture.Channel.Pipeline.Context(anchor);
        Task[] operations = null;
        if (inEventLoop)
            await fixture.Channel.EventLoop.SubmitAsync(() => { operations = ExecuteOutboundOperations(context); });
        else
            operations = ExecuteOutboundOperations(context);
        foreach (string name in new[] { "bind", "connect", "close", "deregister", "read", "write", "flush" })
            Assert.Equal(name, await events.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(events.Reader.TryPeek(out _));
        await Task.WhenAll(operations);
        fixture.Channel.Pipeline.RemoveFirst();
    }
    private static Task[] ExecuteOutboundOperations(ChannelHandlerContext context)
    {
        var bind = context.BindAsync(new LocalAddress("context-bind"));
        var connect = context.ConnectAsync(new LocalAddress("context-connect"));
        var close = context.CloseAsync();
        var deregister = context.DeregisterAsync();
        context.Read();
        var write = context.WriteAsync("");
        context.Flush();
        return new[] { bind, connect, close, deregister, write };
    }
    [Fact(Timeout = 5000)]
    public async Task TestChannelInitializerException()
    {
        var exception = new InvalidOperationException();
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new EmbeddedChannel(new FailingInitializer(exception, error));
        try
        { Assert.Same(exception, await error.Task.WaitAsync(TestContext.Current.CancellationToken)); Assert.False(channel.IsActive); }
        finally { channel.FinishAndReleaseAll(); }
    }
    [Fact]
    public async Task TestChannelUnregistrationWithCustomExecutor()
    {
        await using var fixture = new Fixture();
        var executor = new DefaultEventLoop();
        var unregistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            fixture.Channel.Pipeline.AddLast(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(executor, null, new UnregistrationSignals(unregistered, removed))));
            var registration = fixture.Register();
            var close = fixture.Channel.CloseAsync();
            var deregister = fixture.Channel.DeregisterAsync();
            await unregistered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await Task.WhenAll(registration, close, deregister);
        }
        finally { await executor.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestHandlerAddedExceptionFromChildHandlerIsPropagated()
    {
        await using var fixture = new Fixture();
        var group1 = new DefaultEventLoopGroup(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exception = new Exception();
        bool added = false;
        try
        {
            fixture.Channel.Pipeline.AddLast(group1, null, new CheckExceptionHandler(exception, completed));
            fixture.Channel.Pipeline.AddFirst(new ThrowAddedRemoved(exception, null, () => added = true));
            Assert.False(added);
            var registration = fixture.Register();
            await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await registration;
        }
        finally { await fixture.Channel.CloseAsync(); await group1.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestHandlerRemovedExceptionFromChildHandlerIsPropagated()
    {
        await using var fixture = new Fixture();
        var group1 = new DefaultEventLoopGroup(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exception = new Exception();
        try
        {
            fixture.Channel.Pipeline.AddLast("foo", new ThrowAddedRemoved(null, exception));
            fixture.Channel.Pipeline.AddLast(group1, null, new CheckExceptionHandler(exception, completed));
            await fixture.Register();
            fixture.Channel.Pipeline.Remove("foo");
            await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { await fixture.Channel.CloseAsync(); await group1.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestHandlerAddedThrowsAndRemovedThrowsException()
    {
        await using var fixture = new Fixture();
        var group1 = new DefaultEventLoopGroup(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exceptionAdded = new Exception();
        var exceptionRemoved = new Exception();
        try
        {
            fixture.Channel.Pipeline.AddLast(group1, null, new CheckExceptionHandler(exceptionAdded, completed));
            fixture.Channel.Pipeline.AddFirst("foo", new ThrowAddedRemoved(exceptionAdded, exceptionRemoved, removed: latch));
            await fixture.Register();
            await latch.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Null(fixture.Channel.Pipeline.Context("foo"));
            await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { await fixture.Channel.CloseAsync(); await group1.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestUnorderedEventExecutor()
    {
        await using var fixture = new Fixture();
        var executors = new UnorderedThreadPoolEventExecutor(2);
        using var unblock = new ManualResetEventSlim();
        var triggered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await fixture.Register();
            fixture.Channel.Pipeline.AddLast(executors, null, new BlockingAdded(unblock, triggered));
            // Trigger an event, as we use UnorderedEventExecutor userEventTriggered should be called even when
            // handlerAdded(...) blocks.
            fixture.Channel.Pipeline.FireUserEventTriggered("");
            await triggered.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            // CLR workers are released explicitly; Java shutdown interrupts LockSupport.park().
            unblock.Set();
            await fixture.Channel.CloseAsync();
            await executors.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
    }
    // Test for https://github.com/netty/netty/issues/8676.
    [Fact]
    public async Task TestHandlerRemovedOnlyCalledWhenHandlerAddedCalled()
    {
        var group = new DefaultEventLoopGroup(1);
        Exception error = null;
        try
        {
            // As this only happens via a race we will verify 500 times. This was good enough to have it failed most of
            // the time.
            for (int i = 0; i < 500; i++)
            {
                var channel = new LocalChannel();
                await channel.RegisterAsync(group.Next());
                using var latch = new ManualResetEventSlim();
                channel.Pipeline.AddLast(new DelayedRemoval(latch));
                // Close the pipeline which will call destroy0(). This will remove each handler in the pipeline and
                // should call handlerRemoved(...) if and only if handlerAdded(...) was called for the handler before.
                var close = channel.Pipeline.CloseAsync();
                channel.Pipeline.AddLast(new VerifyAddedBeforeRemoved(cause => error = cause));
                latch.Set();
                await channel.CloseCompletion;
                await close;
                // Schedule something on the EventLoop to ensure all other scheduled tasks had a chance to complete.
                await channel.EventLoop.SubmitAsync(() =>
                {
                    // NOOP
                });
                if (error != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 5000)]
    public async Task TestHandlerAddedFailedButHandlerStillRemoved() => await HandlerAddedFailedButRemoved(false, TestContext.Current.CancellationToken);
    [Fact(Timeout = 5000)]
    public async Task TestHandlerAddedFailedButHandlerStillRemovedWithLaterRegister() => await HandlerAddedFailedButRemoved(true, TestContext.Current.CancellationToken);
    private static async Task HandlerAddedFailedButRemoved(bool lateRegister, CancellationToken token)
    {
        await using var fixture = new Fixture();
        var executors = new DefaultEventExecutorGroup(16);
        const int numHandlers = 32;
        try
        {
            fixture.Channel.Config.SetOption(ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP, false);
            if (!lateRegister)
                await fixture.Register();
            fixture.Channel.Pipeline.AddFirst(new ChannelDuplexHandler());
            var signals = new List<TaskCompletionSource>(numHandlers);
            for (int i = 0; i < numHandlers; i++)
            {
                var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Channel.Pipeline.AddFirst(executors, "h" + i, new BadChannelHandler(latch));
                signals.Add(latch);
            }
            if (lateRegister)
                await fixture.Register();
            for (int i = 0; i < numHandlers; i++)
            {
                // Wait until the latch was countDown which means handlerRemoved(...) was called.
                await signals[i].Task.WaitAsync(token);
                Assert.Null(fixture.Channel.Pipeline.Get("h" + i));
            }
        }
        finally { await fixture.Channel.CloseAsync(); await executors.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)); }
    }
    [Fact(Timeout = 5000)]
    public async Task HandlerAddedStateUpdatedBeforeHandlerAddedDoneForceEventLoop()
    { await HandlerAddedStateUpdatedBeforeDone(true, TestContext.Current.CancellationToken); }
    [Fact(Timeout = 5000)]
    public async Task HandlerAddedStateUpdatedBeforeHandlerAddedDoneOnCallingThread()
    { await HandlerAddedStateUpdatedBeforeDone(false, TestContext.Current.CancellationToken); }
    private static async Task HandlerAddedStateUpdatedBeforeDone(bool executeInEventLoop, CancellationToken token)
    {
        await using var fixture = new Fixture();
        var userEvent = new object();
        var writeObject = new object();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = fixture.Register();
        void Run()
        {
            fixture.Channel.Pipeline.AddLast(new WriteOnUserEvent(userEvent, writeObject));
            fixture.Channel.Pipeline.AddFirst(new EventFromAdded(userEvent, writeObject, done));
        }
        if (executeInEventLoop)
            await fixture.Channel.EventLoop.SubmitAsync(Run);
        else
            Run();
        await done.Task.WaitAsync(token);
        await registration;
    }
    // Tests for https://github.com/netty/netty/issues/2349
    [Fact]
    public async Task TestCancelBind() => await CheckCanceledOperation((p, t) => p.BindAsync(new LocalAddress(typeof(DefaultChannelPipelineEventSourceTest)), t));
    [Fact]
    public async Task TestCancelConnect() => await CheckCanceledOperation((p, t) => p.ConnectAsync(new LocalAddress(typeof(DefaultChannelPipelineEventSourceTest)), cancellationToken: t));
    [Fact]
    public async Task TestCancelDisconnect() => await CheckCanceledOperation((p, t) => p.DisconnectAsync(t));
    [Fact]
    public async Task TestCancelClose() => await CheckCanceledOperation((p, t) => p.CloseAsync(t));
    [Fact]
    public async Task TestCancelDeregister() => await CheckCanceledOperation((p, t) => p.DeregisterAsync(t));
    [Fact]
    public async Task TestCancelWrite() => await CheckCanceledWrite(false);
    [Fact]
    public async Task TestCancelWriteAndFlush() => await CheckCanceledWrite(true);
    private static async Task CheckCanceledOperation(Func<DefaultChannelPipeline, CancellationToken, Task> operation)
    {
        await using var fixture = new Fixture();
        var registration = fixture.Register();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.True(cancellation.IsCancellationRequested);
        Task result = operation(fixture.Channel.Pipeline, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.True(result.IsCanceled);
        await registration;
    }
    private static async Task CheckCanceledWrite(bool flush)
    {
        await using var fixture = new Fixture();
        var registration = fixture.Register();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.True(cancellation.IsCancellationRequested);
        var buffer = Unpooled.Buffer();
        Assert.Equal(1, buffer.ReferenceCount);
        Task result = flush ? fixture.Channel.Pipeline.WriteAndFlushAsync(buffer, cancellation.Token) : fixture.Channel.Pipeline.WriteAsync(buffer, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.True(result.IsCanceled);
        Assert.Equal(0, buffer.ReferenceCount);
        await registration;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DefaultEventLoopGroup _group = new(1);
        internal readonly LocalChannel Channel = new();
        internal Task Register() => Channel.RegisterAsync(_group.Next());
        public async ValueTask DisposeAsync() { await Channel.CloseAsync(); await _group.StopAsync(); }
    }
    private sealed class ContextAnchor : ChannelHandlerAdapter { }
    private sealed class TestException : Exception { }
    private sealed class ThrowReadComplete(TaskCompletionSource latch) : ChannelInboundHandlerAdapter
    {
        internal int Count;
        public override void ChannelReadComplete(ChannelHandlerContext context) => throw new TestException();
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            if (cause is TestException)
                context.Executor.Execute(() => latch.TrySetResult());
            Count++;
            throw new Exception();
        }
    }
    private sealed class ReadCompleteOnException : ChannelInboundHandlerAdapter
    { public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => context.FireChannelReadComplete(); }
    private sealed class RegisteredSignal(TaskCompletionSource signal) : ChannelInboundHandlerAdapter
    { public override void ChannelRegistered(ChannelHandlerContext context) => signal.TrySetResult(); }
    private sealed class ReleasedSignal(TaskCompletionSource signal) : AbstractReferenceCounted
    {
        public override IReferenceCounted Touch(object hint) => this;
        protected override void Deallocate() => signal.TrySetResult();
    }
    private sealed class StringInboundHandler : ChannelInboundHandlerAdapter
    {
        internal bool Called;
        public override void ChannelRead(ChannelHandlerContext context, object message) { Called = true; if (message is not string) context.FireChannelRead(message); }
    }
    private sealed class PeerReceiver(TaskCompletionSource<IChannel> peer) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRegistered(ChannelHandlerContext context) => peer.TrySetResult(context.Channel);
        public override void ChannelRead(ChannelHandlerContext context, object message) => ReferenceCountUtil.Release(message);
    }
    private sealed class InboundEvents(ChannelWriter<string> events) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRegistered(ChannelHandlerContext context) => events.TryWrite("channelRegistered");
        public override void ChannelUnregistered(ChannelHandlerContext context) => events.TryWrite("channelUnregistered");
        public override void ChannelActive(ChannelHandlerContext context) => events.TryWrite("channelActive");
        public override void ChannelInactive(ChannelHandlerContext context) => events.TryWrite("channelInactive");
        public override void ChannelRead(ChannelHandlerContext context, object message) => events.TryWrite("channelRead");
        public override void ChannelReadComplete(ChannelHandlerContext context) => events.TryWrite("channelReadComplete");
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => events.TryWrite("userEventTriggered");
        public override void ChannelWritabilityChanged(ChannelHandlerContext context) => events.TryWrite("channelWritabilityChanged");
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => events.TryWrite("exceptionCaught");
    }
    private sealed class OutboundEvents(ChannelWriter<string> events) : ChannelOutboundHandlerAdapter
    {
        public override Task BindAsync(ChannelHandlerContext context, EndPoint address, CancellationToken token) { events.TryWrite("bind"); return Task.CompletedTask; }
        public override Task ConnectAsync(ChannelHandlerContext context, EndPoint remote, EndPoint local, CancellationToken token) { events.TryWrite("connect"); return Task.CompletedTask; }
        public override Task CloseAsync(ChannelHandlerContext context, CancellationToken token) { events.TryWrite("close"); return Task.CompletedTask; }
        public override Task DeregisterAsync(ChannelHandlerContext context, CancellationToken token) { events.TryWrite("deregister"); return Task.CompletedTask; }
        public override void Read(ChannelHandlerContext context) => events.TryWrite("read");
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token) { events.TryWrite("write"); return Task.CompletedTask; }
        public override void Flush(ChannelHandlerContext context) { events.TryWrite("flush"); context.Flush(); }
    }
    private sealed class FailingInitializer(Exception exception, TaskCompletionSource<Exception> error) : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel channel) => throw exception;
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) { base.ExceptionCaught(context, cause); error.TrySetResult(cause); }
    }
    private sealed class UnregistrationSignals(TaskCompletionSource unregistered, TaskCompletionSource removed) : ChannelInboundHandlerAdapter
    {
        public override void ChannelUnregistered(ChannelHandlerContext context) => unregistered.TrySetResult();
        public override void HandlerRemoved(ChannelHandlerContext context) => removed.TrySetResult();
    }
    private sealed class CheckExceptionHandler(Exception expected, TaskCompletionSource promise) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        { if (cause is ChannelPipelineException && ReferenceEquals(cause.InnerException, expected)) promise.SetResult(); else promise.SetException(new InvalidOperationException("cause not the expected instance")); }
    }
    private sealed class ThrowAddedRemoved(Exception added, Exception removedException, Action adding = null, TaskCompletionSource removed = null) : ChannelHandlerAdapter
    {
        public override void HandlerAdded(ChannelHandlerContext context) { adding?.Invoke(); if (added != null) throw added; }
        public override void HandlerRemoved(ChannelHandlerContext context)
        {
            // Execute this later so we are sure the exception is handled first.
            if (removed != null)
                context.Executor.Execute(() => removed.TrySetResult());
            if (removedException != null)
                throw removedException;
        }
    }
    private sealed class BlockingAdded(ManualResetEventSlim unblock, TaskCompletionSource triggered) : ChannelInboundHandlerAdapter
    {
        public override void HandlerAdded(ChannelHandlerContext context)
        {
            // Just block one of the two threads.
            unblock.Wait();
        }
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => triggered.TrySetResult();
    }
    private sealed class DelayedRemoval(ManualResetEventSlim latch) : ChannelInboundHandlerAdapter
    {
        public override void HandlerRemoved(ChannelHandlerContext context)
        {
            // Block just for a bit so we have a chance to trigger the race mentioned in the issue.
            latch.Wait(TimeSpan.FromMilliseconds(50));
        }
    }
    private sealed class VerifyAddedBeforeRemoved(Action<Exception> error) : ChannelInboundHandlerAdapter
    {
        private bool _handlerAddedCalled;
        public override void HandlerAdded(ChannelHandlerContext context) => _handlerAddedCalled = true;
        public override void HandlerRemoved(ChannelHandlerContext context)
        { if (!_handlerAddedCalled) error(new InvalidOperationException("handlerRemoved(...) called without handlerAdded(...) before")); }
    }
    private sealed class BadChannelHandler(TaskCompletionSource latch) : ChannelHandlerAdapter
    {
        public override void HandlerAdded(ChannelHandlerContext context) { Thread.Sleep(10); throw new Exception(); }
        public override void HandlerRemoved(ChannelHandlerContext context) => latch.TrySetResult();
    }
    private sealed class WriteOnUserEvent(object userEvent, object writeObject) : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(ChannelHandlerContext context, object evt)
        { if (ReferenceEquals(evt, userEvent)) _ = context.WriteAsync(writeObject); context.FireUserEventTriggered(evt); }
    }
    private sealed class EventFromAdded(object userEvent, object writeObject, TaskCompletionSource done) : ChannelDuplexHandler
    {
        public override void HandlerAdded(ChannelHandlerContext context) => context.FireUserEventTriggered(userEvent);
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        { if (ReferenceEquals(message, writeObject)) done.TrySetResult(); return context.WriteAsync(message, token); }
    }
}
