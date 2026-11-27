using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
using Skip = Netty.NET.Transport.Channels.ChannelHandlerMask.SkipAttribute;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Complete executor/channel cleanup independently from individual waits.
public class PipelineMaskAndTaskContractTest
{
    [Fact]
    public void OverrideOfSkippedMethodIsInvokedWithoutRepeatingItsAttribute()
    {
        var handler = new ActiveOverride();
        var channel = new EmbeddedChannel(handler);
        try
        {
            Assert.True(channel.WriteInbound("message"));
            Assert.Equal(1, handler.Reads);
            Assert.Equal("message", channel.ReadInbound());
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void ExplicitInterfaceImplementationUsesItsOwnSkipAttribute()
    {
        var handler = new ExplicitSkippedRead();
        var channel = new EmbeddedChannel(handler);
        try
        {
            Assert.True(channel.WriteInbound("message"));
            Assert.Equal(0, handler.Reads);
            Assert.Equal("message", channel.ReadInbound());
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public async Task SkippedForwarderKeepsItsExecutorBoundaryBehindAnEarlierEvent()
    {
        var group = new DefaultEventLoopGroup(1);
        var executor = new DefaultEventLoop();
        var channel = new LocalChannel();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRemoved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastRemoved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = Channel.CreateUnbounded<string>();
        bool installed = false;
        try
        {
            await channel.RegisterAsync(group.Next());
            channel.Pipeline.AddLast(executor, null, new BlockUserEvent(entered, release, firstRemoved)).AddLast(new RecordEvents(received.Writer, lastRemoved));
            installed = true;
            channel.Pipeline.FireUserEventTriggered("event");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            channel.Pipeline.FireChannelRead("message");
            await channel.EventLoop.SubmitAsync(() => { });
            Assert.False(received.Reader.TryPeek(out _));
            release.Set();
            Assert.Equal("event", await received.Reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal("message", await received.Reader.ReadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
            await channel.CloseAsync();
            // Close completion precedes cross-executor destruction. Finish both owners' callbacks before stopping them.
            if (installed)
                await Task.WhenAll(firstRemoved.Task, lastRemoved.Task).WaitAsync(TimeSpan.FromSeconds(5));
            await group.StopAsync();
            await executor.StopAsync();
        }
    }

    [Fact]
    public async Task SixOperationObserversRegainTheirOwnerAfterForeignTaskCompletion()
    {
        var group = new DefaultEventLoopGroup(1);
        var producer = new DefaultEventExecutor();
        var channel = new LocalChannel();
        var callbacks = Channel.CreateBounded<bool>(6);
        try
        {
            channel.Pipeline.AddLast(new ForeignCompletion(producer), new OwnerObserver(callbacks.Writer));
            await channel.RegisterAsync(group.Next());
            await channel.BindAsync(new LocalAddress("foreign-bind"));
            await channel.ConnectAsync(new LocalAddress("foreign-connect"));
            await channel.DisconnectAsync();
            await channel.CloseAsync();
            await channel.DeregisterAsync();
            await channel.WriteAsync("");
        }
        finally
        {
            // Remove the handlers before closing so we don't intercept it.
            while (channel.Pipeline.LastContext != null)
                channel.Pipeline.RemoveLast();
            await channel.CloseAsync();
            await group.StopAsync();
            await producer.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
        for (int i = 0; i < 6; i++)
            Assert.True(await callbacks.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(callbacks.Reader.TryPeek(out _));
    }

    [Fact]
    public async Task ReturnedWriteTaskExposesItsOriginalFailure()
    {
        var error = new InvalidOperationException();
        var handler = new ThrowWrite(error);
        var channel = new EmbeddedChannel(handler);
        try
        {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.WriteAsync("test"));
            Assert.Same(error, observed);
            Assert.Equal(0, handler.PipelineErrors);
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }
    private class SkippedRead : ChannelInboundHandlerAdapter
    {
        [Skip]
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);
        }
    }
    private sealed class ActiveOverride : SkippedRead
    {
        internal int Reads;
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Reads++;
            ctx.FireChannelRead(msg);
        }
    }
    private sealed class ExplicitSkippedRead : ChannelInboundHandlerAdapter, IChannelInboundHandler
    {
        internal int Reads;
        [Skip]
        void IChannelInboundHandler.ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Reads++;
            ctx.FireChannelRead(msg);
        }
    }
    private sealed class BlockUserEvent(TaskCompletionSource entered, ManualResetEventSlim release, TaskCompletionSource removed) : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            ctx.FireUserEventTriggered(evt);
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            removed.TrySetResult();
        }
    }
    private sealed class RecordEvents(ChannelWriter<string> events, TaskCompletionSource removed) : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            events.TryWrite((string)evt);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            events.TryWrite((string)msg);
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            removed.TrySetResult();
        }
    }
    private sealed class ForeignCompletion(IEventExecutor executor) : ChannelOutboundHandlerAdapter
    {
        private Task Complete()
        {
            var producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            executor.Execute(() => producer.SetResult());
            return producer.Task;
        }

        public override Task BindAsync(IChannelHandlerContext c, EndPoint address, CancellationToken token)
        {
            return Complete();
        }

        public override Task ConnectAsync(IChannelHandlerContext c, EndPoint remote, EndPoint local, CancellationToken token)
        {
            return Complete();
        }

        public override Task DisconnectAsync(IChannelHandlerContext c, CancellationToken token)
        {
            return Complete();
        }

        public override Task CloseAsync(IChannelHandlerContext c, CancellationToken token)
        {
            return Complete();
        }

        public override Task DeregisterAsync(IChannelHandlerContext c, CancellationToken token)
        {
            return Complete();
        }

        public override Task WriteAsync(IChannelHandlerContext c, object msg, CancellationToken token)
        {
            return Complete();
        }
    }
    private sealed class OwnerObserver(ChannelWriter<bool> callbacks) : ChannelOutboundHandlerAdapter
    {
        private async Task Observe(IChannelHandlerContext c, Task operation)
        {
            await operation.ConfigureAwait(false);
            // Native Task consumers choose scheduling. Restore explicit ownership before touching handler state.
            await c.Executor.SubmitAsync(() => callbacks.TryWrite(c.Executor.InEventLoop())).ConfigureAwait(false);
        }

        public override Task BindAsync(IChannelHandlerContext c, EndPoint address, CancellationToken token)
        {
            return Observe(c, c.BindAsync(address, token));
        }

        public override Task ConnectAsync(IChannelHandlerContext c, EndPoint remote, EndPoint local, CancellationToken token)
        {
            return Observe(c, c.ConnectAsync(remote, local, token));
        }

        public override Task DisconnectAsync(IChannelHandlerContext c, CancellationToken token)
        {
            return Observe(c, c.DisconnectAsync(token));
        }

        public override Task CloseAsync(IChannelHandlerContext c, CancellationToken token)
        {
            return Observe(c, c.CloseAsync(token));
        }

        public override Task DeregisterAsync(IChannelHandlerContext c, CancellationToken token)
        {
            return Observe(c, c.DeregisterAsync(token));
        }

        public override Task WriteAsync(IChannelHandlerContext c, object msg, CancellationToken token)
        {
            return Observe(c, c.WriteAsync(msg, token));
        }
    }
    private sealed class ThrowWrite(Exception error) : ChannelDuplexHandler
    {
        internal int PipelineErrors;
        public override Task WriteAsync(IChannelHandlerContext c, object msg, CancellationToken token)
        {
            throw error;
        }

        public override void ExceptionCaught(IChannelHandlerContext c, Exception cause)
        {
            PipelineErrors++;
        }
    }
}
