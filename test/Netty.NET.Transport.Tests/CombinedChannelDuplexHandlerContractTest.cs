using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independently owned cleanup and controlled cancellation inputs.
public class CombinedChannelDuplexHandlerContractTest
{
    [Fact]
    public void ContextsKeepOuterIdentityAndIndependentIdempotentRemoval()
    {
        var inbound = new CaptureInbound { RemovedCallback = context => Assert.True(context.IsRemoved) };
        var outbound = new CaptureOutbound { RemovedCallback = context => Assert.True(context.IsRemoved) };
        var combined = new CombinedChannelDuplexHandler<CaptureInbound, CaptureOutbound>(inbound, outbound);
        var channel = new EmbeddedChannel(combined);
        try
        {
            var parent = channel.Pipeline.Context(combined);
            var key = AttributeKey<string>.ValueOf(nameof(ContextsKeepOuterIdentityAndIndependentIdempotentRemoval));
            foreach (var context in new[] { inbound.Context, outbound.Context })
            {
                Assert.NotSame(parent, context);
                Assert.Same(channel, context.Channel);
                Assert.Same(parent.Pipeline, context.Pipeline);
                Assert.Same(parent.Executor, context.Executor);
                Assert.Same(parent.Allocator, context.Allocator);
                Assert.Equal(parent.Name, context.Name);
                // The source delegator's handler() returns the OUTER context's handler, not its owned child.
                Assert.Same(combined, context.Handler);
                Assert.Same(channel.Attr(key), context.Attr(key));
                Assert.True(context.HasAttr(key));
                Assert.False(context.IsRemoved);
            }
            combined.RemoveInboundHandler();
            combined.RemoveInboundHandler();
            Assert.Equal(1, inbound.Removals);
            Assert.True(inbound.Context.IsRemoved);
            Assert.False(outbound.Context.IsRemoved);
            Assert.False(parent.IsRemoved);
            channel.Pipeline.Remove(combined);
            Assert.Equal(1, inbound.Removals);
            Assert.Equal(1, outbound.Removals);
            Assert.True(outbound.Context.IsRemoved);
            Assert.True(parent.IsRemoved);
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    [Fact]
    public async Task SavedChildContextsFollowCurrentPipelineNeighbors()
    {
        var inbound = new CaptureInbound();
        var outbound = new CaptureOutbound();
        var combined = new CombinedChannelDuplexHandler<CaptureInbound, CaptureOutbound>(inbound, outbound);
        var channel = new EmbeddedChannel();
        channel.Pipeline.AddLast("combined", combined);
        var reads = new List<object>();
        var writes = new List<object>();
        var readHandler = new CaptureInbound { ReadCallback = (_, message) => reads.Add(message) };
        var writeHandler = new CaptureOutbound { WriteCallback = (_, message, _) => { writes.Add(message); return Task.CompletedTask; } };
        try
        {
            channel.Pipeline.AddAfter("combined", "read", readHandler);
            channel.Pipeline.AddBefore("combined", "write", writeHandler);
            var first = new object();
            Assert.Same(inbound.Context, inbound.Context.FireChannelRead(first));
            await outbound.Context.WriteAndFlushAsync(first);
            Assert.Equal(new[] { first }, reads);
            Assert.Equal(new[] { first }, writes);
            channel.Pipeline.Remove(readHandler);
            channel.Pipeline.Remove(writeHandler);
            var second = new object();
            inbound.Context.FireChannelRead(second);
            await outbound.Context.WriteAndFlushAsync(second);
            Assert.Same(second, channel.ReadInbound());
            Assert.Same(second, channel.ReadOutbound());
            Assert.Single(reads);
            Assert.Single(writes);
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    [Fact]
    public async Task PartialRemovalFromForeignThreadRunsOnTheContextExecutor()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inbound = new CaptureInbound();
        var outbound = new CaptureOutbound();
        var combined = new CombinedChannelDuplexHandler<CaptureInbound, CaptureOutbound>(inbound, outbound);
        try
        {
            channel.Pipeline.AddLast(combined);
            await channel.RegisterAsync(group.Next());
            channel.EventLoop.Execute(() =>
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(channel.EventLoop.InEventLoop());
            combined.RemoveInboundHandler();
            combined.RemoveOutboundHandler();
            Assert.False(inbound.Removed.Task.IsCompleted);
            Assert.False(outbound.Removed.Task.IsCompleted);
            release.Set();
            Assert.True(await inbound.Removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(await outbound.Removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(1, inbound.Removals);
            Assert.Equal(1, outbound.Removals);
        }
        finally
        {
            release.Set();
            await channel.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact]
    public void FailedInboundAdditionStillAddsAndRemovesBothChildren()
    {
        var failure = new InvalidOperationException("inbound addition");
        var events = new List<string>();
        var inbound = new CaptureInbound { AddedCallback = _ => { events.Add("in-added"); throw failure; }, RemovedCallback = _ => events.Add("in-removed") };
        var outbound = new CaptureOutbound { AddedCallback = _ => events.Add("out-added"), RemovedCallback = _ => events.Add("out-removed") };
        var combined = new CombinedChannelDuplexHandler<CaptureInbound, CaptureOutbound>(inbound, outbound);
        var failures = new List<Exception>();
        var errors = new CaptureInbound { ErrorCallback = (_, cause) => failures.Add(cause) };
        var channel = new EmbeddedChannel();
        try
        {
            channel.Pipeline.AddLast("errors", errors);
            channel.Pipeline.AddBefore("errors", "combined", combined);
            Assert.Equal(new[] { "in-added", "out-added", "in-removed", "out-removed" }, events);
            Assert.Same(failure, Assert.IsType<ChannelPipelineException>(Assert.Single(failures)).InnerException);
            Assert.True(inbound.Context.IsRemoved);
            Assert.True(outbound.Context.IsRemoved);
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    [Fact]
    public void PartialRemovalFailureIsWrappedAndDoesNotRepeatCallbacks()
    {
        var failure = new InvalidOperationException("inbound removal");
        var inbound = new CaptureInbound { RemovedCallback = _ => throw failure };
        var outbound = new CaptureOutbound();
        var combined = new CombinedChannelDuplexHandler<CaptureInbound, CaptureOutbound>(inbound, outbound);
        var failures = new List<Exception>();
        var errors = new CaptureInbound { ErrorCallback = (_, cause) => failures.Add(cause) };
        var channel = new EmbeddedChannel(combined, errors);
        try
        {
            combined.RemoveInboundHandler();
            combined.RemoveInboundHandler();
            Assert.Same(failure, Assert.IsType<ChannelPipelineException>(Assert.Single(failures)).InnerException);
            Assert.True(inbound.Context.IsRemoved);
            Assert.False(outbound.Context.IsRemoved);
            Assert.Equal(1, inbound.Removals);
            combined.RemoveOutboundHandler();
            var afterRemoval = new Exception("after removal");
            channel.Pipeline.FireExceptionCaught(afterRemoval);
            Assert.Same(afterRemoval, failures[1]);
            channel.Pipeline.Remove(combined);
            Assert.Equal(1, inbound.Removals);
            Assert.Equal(1, outbound.Removals);
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    // Additional CLR pending/fault/cancellation checks behind the original completion-plumbing test.
    [Fact(Timeout = 3000)]
    public async Task SixOutboundTasksPreservePendingSuccessFailureAndCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var terminal = new ControlledOutbound();
        var channel = new EmbeddedChannel(terminal,
            new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(new ChannelInboundHandlerAdapter(), new ChannelOutboundHandlerAdapter()));
        var local = new IPEndPoint(IPAddress.Any, 0);
        var remote = new IPEndPoint(IPAddress.Any, 0);
        object message = new();
        Func<Task>[] operations = [() => channel.Pipeline.BindAsync(local, cancellation.Token),
            () => channel.Pipeline.ConnectAsync(remote, local, cancellation.Token),
            () => channel.Pipeline.CloseAsync(cancellation.Token), () => channel.Pipeline.DisconnectAsync(cancellation.Token),
            () => channel.Pipeline.WriteAsync(message, cancellation.Token), () => channel.Pipeline.DeregisterAsync(cancellation.Token)];
        try
        {
            foreach (var operation in operations)
            {
                foreach (string result in new[] { "success", "failure", "cancellation" })
                {
                    terminal.Producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var observed = operation();
                    Assert.Equal(cancellation.Token, terminal.Token);
                    Assert.False(observed.IsCompleted);
                    var failure = new InvalidOperationException("outbound failure");
                    if (result == "success") { terminal.Producer.SetResult(); await observed; }
                    else if (result == "failure")
                    {
                        terminal.Producer.SetException(failure);
                        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => observed));
                    }
                    else
                    {
                        terminal.Producer.SetCanceled(cancellation.Token);
                        Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observed)).CancellationToken);
                        Assert.True(observed.IsCanceled);
                    }
                }
            }
        }
        finally
        {
            channel.Pipeline.Remove(terminal);
            channel.FinishAndReleaseAll();
        }
    }

    private sealed class CaptureInbound : ChannelInboundHandlerAdapter
    {
        internal ChannelHandlerContext Context;
        internal int Removals;
        internal readonly TaskCompletionSource<bool> Removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action<ChannelHandlerContext> AddedCallback, RemovedCallback;
        internal Action<ChannelHandlerContext, object> ReadCallback;
        internal Action<ChannelHandlerContext, Exception> ErrorCallback;
        public override void HandlerAdded(ChannelHandlerContext context) { Context = context; AddedCallback?.Invoke(context); }
        public override void HandlerRemoved(ChannelHandlerContext context)
        { Removals++; RemovedCallback?.Invoke(context); Removed.TrySetResult(context.Executor.InEventLoop() && context.IsRemoved); }
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { if (ReadCallback != null) ReadCallback(context, message); else base.ChannelRead(context, message); }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        { if (ErrorCallback != null) ErrorCallback(context, cause); else base.ExceptionCaught(context, cause); }
    }
    private sealed class CaptureOutbound : ChannelOutboundHandlerAdapter
    {
        internal ChannelHandlerContext Context;
        internal int Removals;
        internal readonly TaskCompletionSource<bool> Removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action<ChannelHandlerContext> AddedCallback, RemovedCallback;
        internal Func<ChannelHandlerContext, object, CancellationToken, Task> WriteCallback;
        public override void HandlerAdded(ChannelHandlerContext context) { Context = context; AddedCallback?.Invoke(context); }
        public override void HandlerRemoved(ChannelHandlerContext context)
        { Removals++; RemovedCallback?.Invoke(context); Removed.TrySetResult(context.Executor.InEventLoop() && context.IsRemoved); }
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken cancellationToken)
            => WriteCallback != null ? WriteCallback(context, message, cancellationToken) : base.WriteAsync(context, message, cancellationToken);
    }
    private sealed class ControlledOutbound : ChannelOutboundHandlerAdapter
    {
        internal TaskCompletionSource Producer;
        internal CancellationToken Token;
        private Task Complete(CancellationToken token) { Token = token; return Producer.Task; }
        public override Task BindAsync(ChannelHandlerContext context, EndPoint address, CancellationToken token) => Complete(token);
        public override Task ConnectAsync(ChannelHandlerContext context, EndPoint remote, EndPoint local, CancellationToken token) => Complete(token);
        public override Task CloseAsync(ChannelHandlerContext context, CancellationToken token) => Complete(token);
        public override Task DisconnectAsync(ChannelHandlerContext context, CancellationToken token) => Complete(token);
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token) => Complete(token);
        public override Task DeregisterAsync(ChannelHandlerContext context, CancellationToken token) => Complete(token);
    }
}
