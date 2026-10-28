/*
 * Copyright 2014 The Netty Project
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// EmbeddedChannelTest.java: native Task completion replaces Future listeners and explicit promises.
// CLR TimeSpan has 100 ns resolution, so the two 1 ns boundary tests use one CLR tick while retaining
// the exact-before/exact-at deadline assertions. No elapsed-time assertion is removed.
#pragma warning disable xUnit1051, xUnit1069 // Original manually driven event-loop operations use their own lifecycle cancellation.
public class EmbeddedChannelTest
{
    [Fact]
    public void TestParent()
    {
        var parent = new EmbeddedChannel();
        var channel = new EmbeddedChannel(parent, new CustomChannelId(1), true, false);
        Assert.Same(parent, channel.Parent);
        Assert.Null(parent.Parent);
        Assert.False(channel.Finish());
        Assert.False(parent.Finish());
    }
    [Fact]
    public void TestNotRegistered()
    {
        var channel = new EmbeddedChannel(false, false);
        Assert.False(channel.IsRegistered);
        channel.Register();
        Assert.True(channel.IsRegistered);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestRegistered()
    {
        var channel = new EmbeddedChannel(true, false);
        Assert.True(channel.IsRegistered);
        Assert.Throws<InvalidOperationException>(channel.Register);
        Assert.False(channel.Finish());
    }
    [Fact(Timeout = 2000)]
    public async Task PromiseDoesNotInfiniteLoop()
    {
        var channel = new EmbeddedChannel();
        Task listener = channel.CloseCompletion.ContinueWith(_ => channel.CloseAsync(),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
        await channel.CloseAsync();
        await listener;
    }
    [Fact]
    public void TestConstructWithChannelInitializer()
    {
        object first = 1, second = 2;
        var handler = new InboundActions { ReadAction = (ctx, _) => { ctx.FireChannelRead(first); ctx.FireChannelRead(second); } };
        var channel = new EmbeddedChannel(new Bootstrapping.ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(handler)));
        Assert.Same(handler, channel.Pipeline.FirstContext.Handler);
        Assert.True(channel.WriteInbound(3));
        Assert.True(channel.Finish());
        Assert.Same(first, channel.ReadInbound());
        Assert.Same(second, channel.ReadInbound());
        Assert.Null(channel.ReadInbound());
    }
    [Fact]
    public async Task TestScheduling()
    {
        var channel = new EmbeddedChannel(new ChannelInboundHandlerAdapter());
        using var latch = new CountdownEvent(2);
        Task future = channel.EventLoop.ScheduleAsync(() => latch.Signal(), TimeSpan.FromSeconds(1));
        Task listener = future.ContinueWith(_ => latch.Signal(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        long next = channel.RunScheduledPendingTasks();
        Assert.True(next > 0);
        // Sleep for the nanoseconds but also give extra 50ms as the clock my not be very precise and so fail the test
        // otherwise.
        await Task.Delay(TimeSpan.FromMilliseconds(next / 1_000_000 + 50), TestContext.Current.CancellationToken);
        Assert.Equal(-1, channel.RunScheduledPendingTasks());
        Assert.True(latch.Wait(TimeSpan.FromSeconds(3)));
        await listener;
    }
    [Fact]
    public void TestScheduledCancelled()
    {
        var channel = new EmbeddedChannel(new ChannelInboundHandlerAdapter());
        Task future = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromDays(1));
        channel.Finish();
        Assert.True(future.IsCanceled);
    }
    [Fact(Timeout = 3000)]
    public void TestHandlerAddedExecutedInEventLoop()
    {
        using var latch = new CountdownEvent(1);
        Exception error = null;
        var handler = new AddedHandler(ctx =>
        {
            try
            { Assert.True(ctx.Executor.InEventLoop()); }
            catch (Exception cause) { error = cause; }
            finally { latch.Signal(); }
        });
        var channel = new EmbeddedChannel(handler);
        Assert.False(channel.Finish());
        Assert.True(latch.Wait(TimeSpan.FromSeconds(3)));
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void TestConstructWithOutHandler()
    {
        var channel = new EmbeddedChannel();
        object input = 1, output = 2;
        Assert.True(channel.WriteInbound(input));
        Assert.True(channel.WriteOutbound(output));
        Assert.True(channel.Finish());
        Assert.Same(input, channel.ReadInbound());
        Assert.Null(channel.ReadInbound());
        Assert.Same(output, channel.ReadOutbound());
        Assert.Null(channel.ReadOutbound());
    }
    [Fact]
    public void TestConstructWithChannelId()
    {
        var id = new CustomChannelId(1);
        var channel = new EmbeddedChannel(id);
        Assert.Same(id, channel.Id);
    }
    // See https://github.com/netty/netty/issues/4316.
    [Fact(Timeout = 2000)]
    public async Task TestFireChannelInactiveAndUnregisteredOnClose()
    {
        // Explicit ChannelPromise overloads collapse into the native Task-returning operation.
        await TestFireChannelInactiveAndUnregistered(ch => ch.CloseAsync());
        await TestFireChannelInactiveAndUnregistered(ch => ch.CloseAsync(CancellationToken.None));
    }
    [Fact(Timeout = 2000)]
    public async Task TestFireChannelInactiveAndUnregisteredOnDisconnect()
    {
        await TestFireChannelInactiveAndUnregistered(ch => ch.DisconnectAsync());
        await TestFireChannelInactiveAndUnregistered(ch => ch.DisconnectAsync(CancellationToken.None));
    }
    private static async Task TestFireChannelInactiveAndUnregistered(Func<IChannel, Task> action)
    {
        using var latch = new CountdownEvent(3);
        var channel = new EmbeddedChannel(new InboundActions
        {
            Inactive = ctx =>
            {
                latch.Signal();
                ctx.Executor.Execute(() =>
            {
                // Should be executed.
                latch.Signal();
            });
            },
            Unregistered = _ => latch.Signal()
        });
        await action(channel);
        Assert.True(latch.Wait(TimeSpan.FromSeconds(2)));
    }
    [Fact]
    public async Task TestHasDisconnect()
    {
        var handler = new EventOutboundHandler();
        var channel = new EmbeddedChannel(true, handler);
        await channel.DisconnectAsync();
        await channel.CloseAsync();
        Assert.Equal(EventOutboundHandler.DISCONNECT, handler.PollEvent());
        Assert.Equal(EventOutboundHandler.CLOSE, handler.PollEvent());
        Assert.Null(handler.PollEvent());
    }
    [Fact]
    public async Task TestHasNoDisconnect()
    {
        var handler = new EventOutboundHandler();
        var channel = new EmbeddedChannel(false, handler);
        await channel.DisconnectAsync();
        await channel.CloseAsync();
        Assert.Equal(EventOutboundHandler.CLOSE, handler.PollEvent());
        Assert.Equal(EventOutboundHandler.CLOSE, handler.PollEvent());
        Assert.Null(handler.PollEvent());
    }
    [Fact]
    public async Task TestHasNoDisconnectSkipDisconnect()
    {
        var channel = new EmbeddedChannel(false, new FailingCloseHandler());
        await Assert.ThrowsAsync<Exception>(() => channel.DisconnectAsync());
    }
    [Fact]
    public void TestFinishAndReleaseAll()
    {
        var input = Unpooled.Buffer();
        var output = Unpooled.Buffer();
        try
        {
            var channel = new EmbeddedChannel();
            Assert.True(channel.WriteInbound(input));
            Assert.Equal(1, input.ReferenceCount);
            Assert.True(channel.WriteOutbound(output));
            Assert.Equal(1, output.ReferenceCount);
            Assert.True(channel.FinishAndReleaseAll());
            Assert.Equal(0, input.ReferenceCount);
            Assert.Equal(0, output.ReferenceCount);
            Assert.Null(channel.ReadInbound());
            Assert.Null(channel.ReadOutbound());
        }
        finally { Release(input, output); }
    }
    [Fact]
    public void TestReleaseInbound()
    {
        var input = Unpooled.Buffer();
        var output = Unpooled.Buffer();
        try
        {
            var channel = new EmbeddedChannel();
            Assert.True(channel.WriteInbound(input));
            Assert.Equal(1, input.ReferenceCount);
            Assert.True(channel.WriteOutbound(output));
            Assert.Equal(1, output.ReferenceCount);
            Assert.True(channel.ReleaseInbound());
            Assert.Equal(0, input.ReferenceCount);
            Assert.Equal(1, output.ReferenceCount);
            Assert.True(channel.Finish());
            Assert.Null(channel.ReadInbound());
            var buffer = channel.ReadOutbound<ByteBuf>();
            Assert.Same(output, buffer);
            buffer.Release();
            Assert.Null(channel.ReadOutbound());
        }
        finally { Release(input, output); }
    }
    [Fact]
    public void TestReleaseOutbound()
    {
        var input = Unpooled.Buffer();
        var output = Unpooled.Buffer();
        try
        {
            var channel = new EmbeddedChannel();
            Assert.True(channel.WriteInbound(input));
            Assert.Equal(1, input.ReferenceCount);
            Assert.True(channel.WriteOutbound(output));
            Assert.Equal(1, output.ReferenceCount);
            Assert.True(channel.ReleaseOutbound());
            Assert.Equal(1, input.ReferenceCount);
            Assert.Equal(0, output.ReferenceCount);
            Assert.True(channel.Finish());
            Assert.Null(channel.ReadOutbound());
            var buffer = channel.ReadInbound<ByteBuf>();
            Assert.Same(input, buffer);
            buffer.Release();
            Assert.Null(channel.ReadInbound());
        }
        finally { Release(input, output); }
    }
    [Fact]
    public void TestWriteLater()
    {
        var channel = new EmbeddedChannel(new DeferredWriteHandler());
        var message = new object();
        Assert.True(channel.WriteOutbound(message));
        Assert.True(channel.Finish());
        Assert.Same(message, channel.ReadOutbound());
        Assert.Null(channel.ReadOutbound());
    }
    [Fact]
    public async Task TestWriteScheduled()
    {
        const int delay = 500;
        var channel = new EmbeddedChannel(new DeferredWriteHandler(delay));
        var message = new object();
        Assert.False(channel.WriteOutbound(message));
        await Task.Delay(delay * 2, TestContext.Current.CancellationToken);
        Assert.True(channel.Finish());
        Assert.Same(message, channel.ReadOutbound());
        Assert.Null(channel.ReadOutbound());
    }
    [Fact]
    public void TestFlushInbound()
    {
        using var latch = new CountdownEvent(1);
        var channel = new EmbeddedChannel(new InboundActions { ReadComplete = _ => latch.Signal() });
        channel.FlushInbound();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)), "Nobody called #channelReadComplete() in time.");
    }
    [Fact]
    public async Task TestWriteOneInbound()
    {
        using var latch = new CountdownEvent(1);
        int flushCount = 0;
        var channel = new EmbeddedChannel(new InboundActions
        {
            ReadAction = (_, message) => { ReferenceCountUtil.Release(message); latch.Signal(); },
            ReadComplete = _ => Interlocked.Increment(ref flushCount)
        });
        await channel.WriteOneInboundAsync("Hello, Netty!");
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)), "Nobody called #channelRead() in time.");
        await channel.CloseAsync();
        // There was no #flushInbound() call so nobody should have called
        // #channelReadComplete()
        Assert.Equal(0, flushCount);
    }
    [Fact]
    public void TestFlushOutbound()
    {
        using var latch = new CountdownEvent(1);
        var channel = new EmbeddedChannel(new OutboundActions { FlushAction = _ => latch.Signal() });
        channel.FlushOutbound();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)), "Nobody called #flush() in time.");
    }
    [Fact]
    public async Task TestWriteOneOutbound()
    {
        using var latch = new CountdownEvent(1);
        int flushCount = 0;
        var channel = new EmbeddedChannel(new OutboundActions
        {
            WriteAction = (ctx, message, token) => { Task task = ctx.WriteAsync(message, token); latch.Signal(); return task; },
            FlushAction = _ => Interlocked.Increment(ref flushCount)
        });
        // This shouldn't trigger a #flush()
        Task write = channel.WriteOneOutboundAsync("Hello, Netty!");
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)), "Nobody called #write() in time.");
        await channel.CloseAsync();
        // There was no #flushOutbound() call so nobody should have called #flush()
        Assert.Equal(0, flushCount);
        // Observe the native Task representing the unflushed write failed by close.
        await Assert.ThrowsAsync<IOException>(() => write);
    }
    [Fact]
    public async Task TestEnsureOpen()
    {
        var channel = new EmbeddedChannel();
        await channel.CloseAsync();
        Assert.Throws<IOException>(() => channel.WriteOutbound("Hello, Netty!"));
        Assert.Throws<IOException>(() => channel.WriteInbound("Hello, Netty!"));
    }
    [Fact]
    public async Task TestHandleInboundMessage()
    {
        using var latch = new CountdownEvent(1);
        var channel = new MessageHandlingChannel(true, latch);
        await channel.WriteOneInboundAsync("Hello, Netty!");
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)), "Nobody called #handleInboundMessage() in time.");
    }
    [Fact]
    public void TestHandleOutboundMessage()
    {
        using var latch = new CountdownEvent(1);
        var channel = new MessageHandlingChannel(false, latch);
        Task write = channel.WriteOneOutboundAsync("Hello, Netty!");
        Assert.False(latch.Wait(TimeSpan.FromMilliseconds(50)), "Somebody called unexpectedly #flush()");
        channel.FlushOutbound();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)), "Nobody called #handleOutboundMessage() in time.");
        Assert.True(write.IsCompletedSuccessfully);
    }
    [Fact(Timeout = 5000)]
    public void TestChannelInactiveFired()
    {
        bool inactive = false;
        var channel = new EmbeddedChannel(new InboundActions
        { ExceptionAction = (ctx, cause) => { _ = ctx.CloseAsync(); }, Inactive = _ => inactive = true });
        channel.Pipeline.FireExceptionCaught(new InvalidOperationException());
        Assert.True(inactive);
    }
    [Fact]
    public async Task TestReRegisterEventLoop()
    {
        using var unregistered = new CountdownEvent(1);
        using var registered = new CountdownEvent(2);
        var channel = new EmbeddedChannel(new InboundActions
        { Unregistered = _ => unregistered.Signal(), Registered = _ => registered.Signal() });
        var loop = new EmbeddedEventLoop();
        await channel.DeregisterAsync();
        await loop.RegisterAsync(channel);
        Assert.True(unregistered.Wait(TimeSpan.FromSeconds(5)), "Channel was not unregistered in time.");
        Assert.True(registered.Wait(TimeSpan.FromSeconds(5)), "Channel was not registered in time.");
        using var taskLatch = new CountdownEvent(1);
        channel.EventLoop.Execute(() => taskLatch.Signal());
        channel.RunPendingTasks();
        Assert.True(taskLatch.Wait(TimeSpan.FromSeconds(5)), "Task was not executed in time.");
    }
    [Fact]
    public void TestRunPendingTasksForNotRegisteredChannel()
    {
        var channel = new EmbeddedChannel(false, false);
        Assert.Equal(-1, channel.RunScheduledPendingTasks());
        channel.CheckException();
        channel.RunPendingTasks();
        channel.CheckException();
    }
    [Fact(Timeout = 30000)] // generous timeout, just make sure we don't actually wait for the full 10 mins...
    public void TestAdvanceTime()
    {
        var channel = new EmbeddedChannel();
        Task future10 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(10));
        Task future20 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(20));
        channel.RunPendingTasks();
        Assert.False(future10.IsCompleted);
        Assert.False(future20.IsCompleted);
        channel.AdvanceTimeBy(TimeSpan.FromMinutes(10));
        channel.RunPendingTasks();
        Assert.True(future10.IsCompleted);
        Assert.False(future20.IsCompleted);
    }
    [Fact(Timeout = 30000)] // generous timeout, just make sure we don't actually wait for the full 10 mins...
    public void TestFreezeTime()
    {
        var channel = new EmbeddedChannel();
        channel.FreezeTime();
        // this future will complete after 10min
        Task future10 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(10));
        // this future will complete after 10min + 1ns
        // Adaptation: one TimeSpan tick is the smallest native CLR scheduling interval (100 ns).
        Task future101 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1));
        // this future will complete after 20min
        Task future20 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(20));
        channel.RunPendingTasks();
        Assert.False(future10.IsCompleted);
        Assert.False(future101.IsCompleted);
        Assert.False(future20.IsCompleted);
        channel.AdvanceTimeBy(TimeSpan.FromMinutes(10));
        channel.RunPendingTasks();
        Assert.True(future10.IsCompleted);
        Assert.False(future101.IsCompleted);
        Assert.False(future20.IsCompleted);
        channel.UnfreezeTime();
        channel.RunPendingTasks();
        Assert.True(future101.IsCompleted);
        Assert.False(future20.IsCompleted);
    }
    [Fact(Timeout = 30000)] // generous timeout, just make sure we don't actually wait for the full 10 mins...
    public void TestCustomTicker()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        var channel = new EmbeddedChannel(ticker);
        // this future will complete after 10min
        Task future10 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(10));
        // this future will complete after 10min + 1ns
        // Adaptation: one TimeSpan tick retains the original strict next-deadline boundary.
        Task future101 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1));
        // this future will complete after 20min
        Task future20 = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromMinutes(20));
        channel.RunPendingTasks();
        Assert.False(future10.IsCompleted);
        Assert.False(future101.IsCompleted);
        Assert.False(future20.IsCompleted);
        ticker.Advance(TimeSpan.FromMinutes(10));
        channel.RunPendingTasks();
        Assert.True(future10.IsCompleted);
        Assert.False(future101.IsCompleted);
        Assert.False(future20.IsCompleted);
        ticker.Advance(TimeSpan.FromTicks(1));
        channel.RunPendingTasks();
        Assert.True(future101.IsCompleted);
        Assert.False(future20.IsCompleted);
    }
    [Fact]
    public void TestDefaultTickerCannotSleep()
    {
        var ticker = new EmbeddedChannel().EventLoop.Ticker();
        Assert.Throws<NotSupportedException>(() => ticker.Sleep(TimeSpan.FromSeconds(1)));
    }
    [Fact]
    public void TestHasPendingTasks()
    {
        var channel = new EmbeddedChannel();
        channel.FreezeTime();
        // simple execute
        Assert.False(channel.HasPendingTasks);
        channel.EventLoop.Execute(() => { });
        Assert.True(channel.HasPendingTasks);
        channel.RunPendingTasks();
        Assert.False(channel.HasPendingTasks);
        // schedule in the future (note: time is frozen above)
        channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromSeconds(1));
        Assert.False(channel.HasPendingTasks);
        channel.RunPendingTasks();
        Assert.False(channel.HasPendingTasks);
        channel.AdvanceTimeBy(TimeSpan.FromSeconds(1));
        Assert.True(channel.HasPendingTasks);
        channel.RunPendingTasks();
        Assert.False(channel.HasPendingTasks);
    }
    [Fact]
    public void TestReentrantClose()
    {
        var channel = new EmbeddedChannel(new ReentrantCloseHandler());
        channel.WriteInbound("foo");
        channel.CheckException();
    }
    private static void Release(params ByteBuf[] buffers)
    { foreach (var buffer in buffers) if (buffer.ReferenceCount > 0) buffer.Release(); }
    private sealed class InboundActions : ChannelInboundHandlerAdapter
    {
        internal Action<ChannelHandlerContext, object> ReadAction;
        internal Action<ChannelHandlerContext> ReadComplete, Inactive, Registered, Unregistered;
        internal Action<ChannelHandlerContext, Exception> ExceptionAction;
        public override void ChannelRead(ChannelHandlerContext ctx, object message) => ReadAction(ctx, message);
        public override void ChannelReadComplete(ChannelHandlerContext ctx) { if (ReadComplete == null) base.ChannelReadComplete(ctx); else ReadComplete(ctx); }
        public override void ChannelInactive(ChannelHandlerContext ctx) { if (Inactive == null) base.ChannelInactive(ctx); else Inactive(ctx); }
        public override void ChannelRegistered(ChannelHandlerContext ctx) { if (Registered == null) base.ChannelRegistered(ctx); else Registered(ctx); }
        public override void ChannelUnregistered(ChannelHandlerContext ctx) { if (Unregistered == null) base.ChannelUnregistered(ctx); else Unregistered(ctx); }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception error) { if (ExceptionAction == null) base.ExceptionCaught(ctx, error); else ExceptionAction(ctx, error); }
    }
    private sealed class AddedHandler(Action<ChannelHandlerContext> callback) : ChannelHandlerAdapter
    { public override void HandlerAdded(ChannelHandlerContext ctx) => callback(ctx); }
    private sealed class OutboundActions : ChannelOutboundHandlerAdapter
    {
        internal Func<ChannelHandlerContext, object, CancellationToken, Task> WriteAction;
        internal Action<ChannelHandlerContext> FlushAction;
        public override Task WriteAsync(ChannelHandlerContext ctx, object message, CancellationToken token) => WriteAction(ctx, message, token);
        public override void Flush(ChannelHandlerContext ctx) => FlushAction(ctx);
    }
    private sealed class DeferredWriteHandler(int delayMillis = 0) : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext ctx, object message, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task WriteLater()
            {
                try
                { if (delayMillis == 0) await ctx.WriteAsync(message, token); else await ctx.WriteAndFlushAsync(message, token); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
            }
            if (delayMillis == 0)
                ctx.Executor.Execute(() => _ = WriteLater());
            else
                ctx.Executor.ScheduleAsync(() => _ = WriteLater(), TimeSpan.FromMilliseconds(delayMillis));
            return completion.Task;
        }
    }
    private sealed class EventOutboundHandler : ChannelOutboundHandlerAdapter
    {
        internal const int DISCONNECT = 0, CLOSE = 1;
        private readonly Queue<int> _queue = new();
        public override Task DisconnectAsync(ChannelHandlerContext ctx, CancellationToken token) { _queue.Enqueue(DISCONNECT); return Task.CompletedTask; }
        public override Task CloseAsync(ChannelHandlerContext ctx, CancellationToken token) { _queue.Enqueue(CLOSE); return Task.CompletedTask; }
        internal int? PollEvent() => _queue.TryDequeue(out var result) ? result : null;
    }
    private sealed class FailingCloseHandler : ChannelOutboundHandlerAdapter
    { public override Task CloseAsync(ChannelHandlerContext ctx, CancellationToken token) => Task.FromException(new Exception()); }
    private sealed class MessageHandlingChannel(bool inbound, CountdownEvent latch) : EmbeddedChannel
    {
        protected override void HandleInboundMessage(object message) { if (inbound) latch.Signal(); else base.HandleInboundMessage(message); }
        protected override void HandleOutboundMessage(object message) { if (!inbound) latch.Signal(); else base.HandleOutboundMessage(message); }
    }
    private sealed class ReentrantCloseHandler : ChannelInboundHandlerAdapter
    {
        private bool _runningRead;
        public override void ChannelRead(ChannelHandlerContext ctx, object message)
        { _runningRead = true; try { _ = ctx.Channel.CloseAsync(); } finally { _runningRead = false; } }
        public override void HandlerRemoved(ChannelHandlerContext ctx)
        { if (_runningRead) throw new InvalidOperationException("Reentrant handlerRemoved"); }
    }
}
#pragma warning restore xUnit1051, xUnit1069
