/*
 * Copyright 2026 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR cases for the AbstractChannelHandlerContext, DefaultChannelPipeline and EmbeddedChannel structure
// ported from the pinned Java sources (2026-11-17). Each case names the Java member it pins; none maps an original
// fixture method.
#pragma warning disable xUnit1051 // Manually driven embedded operations complete synchronously.
public class ContextPipelineEmbeddedAlignmentContractTest
{
    // DefaultChannelPipeline.PendingHandlerAddedTask.execute(): a rejected handlerAdded() removes the handler.
    [Fact]
    public void PendingHandlerAddedTaskRejectedByTheExecutorRemovesTheHandler()
    {
        var channel = new EmbeddedChannel(false, false);
        var handler = new LifecycleHandler();
        channel.Pipeline.AddLast(new RejectingExecutor(0), "rejected", handler);
        IChannelHandlerContext ctx = channel.Pipeline.Context("rejected");
        channel.Register();
        Assert.Null(channel.Pipeline.Get("rejected"));
        Assert.True(ctx.IsRemoved);
        Assert.Equal(0, handler.Added);
        Assert.Equal(0, handler.Removed);
        Assert.False(channel.Finish());
    }

    // DefaultChannelPipeline.PendingHandlerRemovedTask.execute(): remove0(...) was called before, so a rejected
    // handlerRemoved() only marks the context removed.
    [Fact]
    public void PendingHandlerRemovedTaskRejectedByTheExecutorOnlyMarksTheContextRemoved()
    {
        var channel = new EmbeddedChannel(false, false);
        var handler = new LifecycleHandler();
        channel.Pipeline.AddLast(new RejectingExecutor(1), "rejected", handler);
        IChannelHandlerContext ctx = channel.Pipeline.Context("rejected");
        channel.Pipeline.Remove("rejected");
        channel.Register();
        Assert.Equal(1, handler.Added);
        Assert.Equal(0, handler.Removed);
        Assert.True(ctx.IsRemoved);
        Assert.False(channel.Finish());
    }

    // AbstractChannelHandlerContext.flush(): safeExecute(executor, flushTask, channel().voidPromise(), ...) fires
    // exceptionCaught for a rejected flush instead of throwing it to the caller.
    [Fact]
    public void FlushRejectedByTheHandlerExecutorFiresExceptionCaught()
    {
        var errors = new List<Exception>();
        var channel = new EmbeddedChannel(new ExceptionRecorder(errors));
        var flushes = new FlushCounter();
        var executor = new RejectingExecutor(1);
        channel.Pipeline.AddLast(executor, "flush", flushes);
        channel.Pipeline.Flush();
        Assert.IsType<RejectedExecutionException>(Assert.Single(errors));
        Assert.Equal(0, flushes.Flushes);
        executor.Accepts = int.MaxValue;
        Assert.False(channel.Finish());
    }

    // AbstractChannelHandlerContext.fireExceptionCaught(Throwable): a rejected submission is logged, not thrown.
    [Fact]
    public void FireExceptionCaughtRejectedByTheHandlerExecutorIsLogged()
    {
        var channel = new EmbeddedChannel();
        var errors = new List<Exception>();
        var executor = new RejectingExecutor(1);
        channel.Pipeline.AddLast(executor, "caught", new ExceptionRecorder(errors));
        channel.Pipeline.FireExceptionCaught(new InvalidOperationException());
        Assert.Empty(errors);
        executor.Accepts = int.MaxValue;
        Assert.False(channel.Finish());
    }

    // DefaultChannelPipeline.callHandlerAdded0(ctx): the source message names the handler class.
    [Fact]
    public void HandlerAddedFailureUsesTheSourceMessage()
    {
        var errors = new List<Exception>();
        var channel = new EmbeddedChannel(new ExceptionRecorder(errors));
        var cause = new InvalidOperationException();
        channel.Pipeline.AddFirst(new ThrowingAddedHandler(cause));
        var error = Assert.IsType<ChannelPipelineException>(Assert.Single(errors));
        Assert.Equal(typeof(ThrowingAddedHandler).FullName + ".handlerAdded() has thrown an exception; removed.", error.Message);
        Assert.Same(cause, error.InnerException);
        Assert.Null(channel.Pipeline.Get<ThrowingAddedHandler>());
        Assert.False(channel.Finish());
    }

    // DefaultChannelPipeline.touch(msg, next) and AbstractChannelHandlerContext.toHintString().
    [Fact]
    public void InboundMessagesAreTouchedWithTheNextContext()
    {
        if (!ResourceLeakDetector.IsEnabled())
        {
            Assert.Skip("Resource leak detection is disabled, so the pipeline does not touch messages.");
        }
        var forwarder = new ReadForwarder();
        var channel = new EmbeddedChannel(forwarder);
        var msg = new HintRecordingMessage();
        channel.Pipeline.FireChannelRead(msg);
        IChannelHandlerContext ctx = channel.Pipeline.Context(forwarder);
        Assert.Same(ctx, msg.Hints[0]);
        var hint = Assert.IsAssignableFrom<IResourceLeakHint>(msg.Hints[0]);
        Assert.Equal("'" + ctx.Name + "' will handle the message from this point.", hint.ToHintString());
        Assert.Same(msg, channel.ReadInbound());
        Assert.Equal("Caller of readInbound() will handle the message from this point", msg.Hints[^1]);
        Assert.False(channel.Finish());
    }

    // DefaultChannelPipeline.checkDuplicateName(name): only handler contexts are searched, not head or tail.
    [Fact]
    public void HandlerNamesAreCheckedAgainstHandlerContextsOnly()
    {
        var channel = new EmbeddedChannel();
        var handler = new ChannelInboundHandlerAdapter();
        channel.Pipeline.AddLast("DefaultChannelPipeline$TailContext#0", handler);
        Assert.Same(handler, channel.Pipeline.Get("DefaultChannelPipeline$TailContext#0"));
        Assert.Throws<ArgumentException>(() =>
            channel.Pipeline.AddLast("DefaultChannelPipeline$TailContext#0", new ChannelInboundHandlerAdapter()));
        Assert.False(channel.Finish());
    }

    // DefaultChannelPipeline.internalAdd(...): checkMultiplicity(handler) runs before getContextOrDie(baseName).
    [Fact]
    public void AddBeforeAnUnknownBaseNameStillMarksTheHandlerAdded()
    {
        var channel = new EmbeddedChannel();
        var handler = new ChannelInboundHandlerAdapter();
        Assert.Throws<KeyNotFoundException>(() => channel.Pipeline.AddBefore("missing", "handler", handler));
        Assert.Throws<ChannelPipelineException>(() => channel.Pipeline.AddLast(handler));
        Assert.False(channel.Finish());
    }

    // AbstractChannelHandlerContext.fireChannelRead(msg) does not check the message; the handler receives it.
    [Fact]
    public void FireChannelReadForwardsTheMessageUnchecked()
    {
        var reads = new List<object>();
        var channel = new EmbeddedChannel(new ReadRecorder(reads));
        channel.Pipeline.FireChannelRead(null);
        Assert.Equal(new object[] { null }, reads);
        Assert.False(channel.Finish());
    }

    // EmbeddedChannel.Builder: parent, channelId, register, hasDisconnect, handlers(handler), config, ticker, build().
    [Fact]
    public void BuilderBuildsTheConfiguredChannel()
    {
        var parent = new EmbeddedChannel();
        IChannelId id = new CustomChannelId(7);
        var config = new DefaultChannelConfig(parent);
        MockTicker ticker = Ticker.NewMockTicker();
        var handler = new ChannelInboundHandlerAdapter();
        var channel = new EmbeddedChannel.Builder()
            .Parent(parent)
            .ChannelId(id)
            .Register(false)
            .HasDisconnect(true)
            .Handlers(handler)
            .Config(config)
            .Ticker(ticker)
            .Build();
        Assert.Same(parent, channel.Parent);
        Assert.Same(id, channel.Id);
        Assert.False(channel.IsRegistered);
        Assert.True(channel.Metadata.HasDisconnect());
        Assert.Same(config, channel.Config);
        // handlers(ChannelHandler) adds the handler itself, not through the setup initializer.
        Assert.Same(handler, channel.Pipeline.First);
        channel.Register();
        Assert.True(channel.IsRegistered);
        Assert.Same(ticker, channel.EventLoop.Ticker());
        Assert.Throws<ArgumentNullException>(() => new EmbeddedChannel.Builder().ChannelId(null));
        Assert.Throws<ArgumentNullException>(() => new EmbeddedChannel.Builder().Handlers((IChannelHandler)null));
        Assert.Throws<ArgumentNullException>(() => new EmbeddedChannel.Builder().Handlers((IChannelHandler[])null));
        Assert.Throws<ArgumentNullException>(() => new EmbeddedChannel.Builder().Config(null));
        Assert.False(channel.Finish());
        Assert.False(parent.Finish());
    }

    // EmbeddedChannel.setup(boolean, ChannelHandler...): the handlers are added by a ChannelInitializer when the
    // channel registers.
    [Fact]
    public void HandlersAreAddedByAnInitializerOnRegistration()
    {
        var handler = new ChannelInboundHandlerAdapter();
        var channel = new EmbeddedChannel(false, false, handler);
        Assert.IsAssignableFrom<ChannelInitializer<IChannel>>(channel.Pipeline.First);
        Assert.Null(channel.Pipeline.Context(handler));
        channel.Register();
        Assert.Same(handler, channel.Pipeline.First);
        Assert.Single(channel.Pipeline.ToMap());
        Assert.False(channel.Finish());
    }

    // EmbeddedChannel.writeOneInbound/writeOneOutbound(msg, promise): checkOpen(true) records the
    // ClosedChannelException and checkException(promise) fails the returned future instead of throwing.
    [Fact]
    public async Task WriteOneOnAClosedChannelFailsTheReturnedTask()
    {
        var channel = new EmbeddedChannel();
        await channel.CloseAsync();
        Task inbound = channel.WriteOneInboundAsync("inbound");
        await Assert.ThrowsAsync<ClosedChannelException>(() => inbound);
        Task outbound = channel.WriteOneOutboundAsync("outbound");
        await Assert.ThrowsAsync<ClosedChannelException>(() => outbound);
        channel.CheckException();
    }

    // EmbeddedChannel.disconnect(promise): without disconnect support the scheduled tasks are cancelled, as close does.
    [Fact]
    public async Task DisconnectWithoutDisconnectSupportCancelsScheduledTasks()
    {
        var channel = new EmbeddedChannel(false);
        Task future = channel.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromDays(1));
        await channel.DisconnectAsync();
        Assert.True(future.IsCanceled);

        var disconnectable = new EmbeddedChannel(true);
        Task kept = disconnectable.EventLoop.ScheduleAsync(() => { }, TimeSpan.FromDays(1));
        await disconnectable.DisconnectAsync();
        Assert.False(kept.IsCompleted);
        Assert.False(disconnectable.Finish());
        Assert.True(kept.IsCanceled);
    }

    // EmbeddedChannel.write(msg): the channel-level operations run the pending tasks they scheduled.
    [Fact]
    public async Task ChannelWriteRunsTheTasksItScheduled()
    {
        var channel = new EmbeddedChannel(new DeferringWriteHandler());
        Task write = channel.WriteAsync("message");
        Assert.False(channel.HasPendingTasks());
        Assert.False(write.IsCompleted);
        channel.FlushOutbound();
        await write.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("message", channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    // EmbeddedUnsafe.wrapped.register(eventLoop, promise): registration runs the tasks that handlerAdded() scheduled.
    [Fact]
    public void RegisterRunsTheTasksHandlerAddedScheduled()
    {
        bool ran = false;
        var channel = new EmbeddedChannel(false, false, new AddedHandler(ctx => ctx.Executor.Execute(() => ran = true)));
        channel.Register();
        Assert.True(ran);
        Assert.False(channel.HasPendingTasks());
        Assert.False(channel.Finish());
    }

    // Runs accepted tasks inline (in its "event loop" for their duration) and rejects once Accepts is used up.
    private sealed class RejectingExecutor(int accepts) : AbstractEventExecutor
    {
        internal int Accepts = accepts;
        private Thread _running;

        public override bool InEventLoop(Thread thread) => ReferenceEquals(thread, _running);

        public override void Execute(Action task)
        {
            if (Accepts-- <= 0)
            {
                throw new RejectedExecutionException();
            }
            Thread previous = _running;
            _running = Thread.CurrentThread;
            try
            {
                task();
            }
            finally
            {
                _running = previous;
            }
        }

        public override Task TerminationFuture => Task.CompletedTask;
        public override bool IsShuttingDown() => false;
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;

        [Obsolete]
        public override void Shutdown()
        {
        }
    }

    private sealed class LifecycleHandler : ChannelHandlerAdapter
    {
        internal int Added, Removed;

        public override void HandlerAdded(IChannelHandlerContext ctx) => Added++;

        public override void HandlerRemoved(IChannelHandlerContext ctx) => Removed++;
    }

    private sealed class ExceptionRecorder(List<Exception> errors) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause) => errors.Add(cause);
    }

    private sealed class ReadRecorder(List<object> reads) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg) => reads.Add(msg);
    }

    private sealed class ReadForwarder : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg) => ctx.FireChannelRead(msg);
    }

    private sealed class FlushCounter : ChannelOutboundHandlerAdapter
    {
        internal int Flushes;

        public override void Flush(IChannelHandlerContext ctx)
        {
            Flushes++;
            ctx.Flush();
        }
    }

    private sealed class ThrowingAddedHandler(Exception cause) : ChannelInboundHandlerAdapter
    {
        public override void HandlerAdded(IChannelHandlerContext ctx) => throw cause;
    }

    private sealed class AddedHandler(Action<IChannelHandlerContext> added) : ChannelHandlerAdapter
    {
        public override void HandlerAdded(IChannelHandlerContext ctx) => added(ctx);
    }

    private sealed class DeferringWriteHandler : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            var promise = new TaskCompletionSource();
            ctx.Executor.Execute(() => ctx.WriteAsync(msg, cancellationToken).ContinueWith(write =>
            {
                if (write.IsCompletedSuccessfully)
                {
                    promise.TrySetResult();
                }
                else
                {
                    promise.TrySetException(write.Exception?.InnerExceptions ?? (IEnumerable<Exception>)new[] { new TaskCanceledException() });
                }
            }, TaskContinuationOptions.ExecuteSynchronously));
            return promise.Task;
        }
    }

    private sealed class HintRecordingMessage : IReferenceCounted
    {
        internal readonly List<object> Hints = new();

        public int RefCnt => 1;

        public IReferenceCounted Retain() => this;

        public IReferenceCounted Retain(int increment) => this;

        public IReferenceCounted Touch() => this;

        public IReferenceCounted Touch(object hint)
        {
            Hints.Add(hint);
            return this;
        }

        public bool Release() => false;

        public bool Release(int decrement) => false;
    }
}
#pragma warning restore xUnit1051
