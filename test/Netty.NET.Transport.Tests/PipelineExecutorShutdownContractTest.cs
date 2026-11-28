using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

// CLR supplemental: records the Java behavior of pipelines whose handlers run on a separate EventExecutorGroup when
// that group or the channel's event loop shuts down first. Netty 4.2.19 gives no shutdown ownership across the two
// executors: DefaultChannelPipeline.destroyUp/destroyDown and the inbound invokers hand work over with a plain
// execute(), and a rejection is not retried. These cases keep that parity; they are not completion guarantees.
#pragma warning disable xUnit1051 // Independently owned loop and group teardown.
public class PipelineExecutorShutdownContractTest
{
    private sealed class Recorder : ChannelInboundHandlerAdapter
    {
        private readonly string _name;
        private readonly ConcurrentQueue<string> _log;
        private readonly Action _onUnregistered;

        public Recorder(string name, ConcurrentQueue<string> log, Action onUnregistered = null)
        {
            _name = name;
            _log = log;
            _onUnregistered = onUnregistered;
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            _log.Enqueue(_name + ".added");
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            _log.Enqueue(_name + ".removed");
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            _log.Enqueue(_name + ".unregistered");
            _onUnregistered?.Invoke();
            ctx.FireChannelUnregistered();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _log.Enqueue(_name + ".exception:" + cause.GetType().Name);
        }
    }

    private static string[] HandlerNames(IChannel channel)
    {
        return channel.Pipeline.Names().Where(name => !name.Contains("TailContext", StringComparison.Ordinal)).ToArray();
    }

    [Fact(Timeout = 10000)]
    public async Task AStoppedSeparateExecutorRemovesItsPendingHandlerWithoutCallbacks()
    {
        // DefaultChannelPipeline.PendingHandlerAddedTask.execute: "Can't invoke handlerAdded() as the EventExecutor {}
        // rejected it, removing handler {}." The handler is unlinked and neither callback runs.
        CancellationToken token = TestContext.Current.CancellationToken;
        var log = new ConcurrentQueue<string>();
        var loop = new DefaultEventLoop();
        var executors = new DefaultEventExecutorGroup(1);

        try
        {
            var channel = new LocalChannel();
            channel.Pipeline.AddLast(executors, "h", new Recorder("h", log));
            await executors.StopAsync().WaitAsync(token);

            await loop.RegisterAsync(channel).WaitAsync(token);
            await loop.SubmitAsync(() =>
            {
            }, token);

            Assert.Empty(HandlerNames(channel));
            Assert.Empty(log);
            await channel.CloseAsync().WaitAsync(token);
        }
        finally
        {
            await loop.StopAsync();
            await executors.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AStoppedSeparateExecutorAbortsPipelineDestroyOnAnIoEventLoop()
    {
        // fireChannelUnregistered reaches "h" through AbstractChannelHandlerContext.invokeChannelUnregistered, whose
        // execute() is rejected inside "a" (exceptionCaught). HeadContext.channelUnregistered then calls destroy(),
        // whose destroyUp() execute() is rejected before any handlerRemoved(). The I/O loop runs the task through
        // safeExecute (runAllTasks), so it only logs and keeps running.
        CancellationToken token = TestContext.Current.CancellationToken;
        var log = new ConcurrentQueue<string>();
        var group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        var executors = new DefaultEventExecutorGroup(1);

        try
        {
            var channel = new LocalChannel();
            channel.Pipeline.AddLast("a", new Recorder("a", log));
            channel.Pipeline.AddLast(executors, "h", new Recorder("h", log));
            channel.Pipeline.AddLast("b", new Recorder("b", log));
            await group.RegisterAsync(channel).WaitAsync(token);
            await channel.EventLoop.SubmitAsync(() =>
            {
            }, token);
            await executors.SubmitAsync(() =>
            {
            }, token);
            await executors.StopAsync().WaitAsync(token);

            await channel.CloseAsync().WaitAsync(token);
            await channel.CloseFuture.WaitAsync(token);
            await channel.EventLoop.SubmitAsync(() =>
            {
            }, token);

            Assert.False(group.IsShuttingDown());
            Assert.Equal(new[] { "a", "h", "b" }, HandlerNames(channel));
            Assert.Contains("a.unregistered", log);
            Assert.Contains("a.exception:" + nameof(RejectedExecutionException), log);
            Assert.DoesNotContain(log, entry => entry.EndsWith(".removed", StringComparison.Ordinal));
            Assert.DoesNotContain("b.unregistered", log);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AStoppedSeparateExecutorTerminatesADefaultEventLoop()
    {
        // As above, but DefaultEventLoop.run() calls runTask(), not safeExecute(): the rejection from destroy()
        // escapes the deregistration task and ends the loop with that cause, as on the JVM.
        CancellationToken token = TestContext.Current.CancellationToken;
        var log = new ConcurrentQueue<string>();
        var loop = new DefaultEventLoop();
        var executors = new DefaultEventExecutorGroup(1);

        try
        {
            var channel = new LocalChannel();
            channel.Pipeline.AddLast("a", new Recorder("a", log));
            channel.Pipeline.AddLast(executors, "h", new Recorder("h", log));
            await loop.RegisterAsync(channel).WaitAsync(token);
            await loop.SubmitAsync(() =>
            {
            }, token);
            await executors.SubmitAsync(() =>
            {
            }, token);
            await executors.StopAsync().WaitAsync(token);

            await channel.CloseAsync().WaitAsync(token);
            Exception terminated = await Assert.ThrowsAsync<RejectedExecutionException>(
                () => loop.TerminationFuture.WaitAsync(token));

            Assert.Equal("event executor terminated", terminated.Message);
            Assert.Equal(new[] { "a", "h" }, HandlerNames(channel));
            Assert.DoesNotContain(log, entry => entry.EndsWith(".removed", StringComparison.Ordinal));
        }
        finally
        {
            await loop.StopAsync().ContinueWith(_ =>
            {
            }, TaskScheduler.Default);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AZeroQuietShutdownRejectsTheSeparateExecutorReturnHop()
    {
        // shutdownGracefully(0, ...) closes the channel, then terminates without waiting for the separate executor.
        // When "h" passes channelUnregistered on to "b" after termination, the loop rejects it (exceptionCaught on
        // "h"). The queued destroyUp() hop is rejected the same way; DefaultEventExecutor.run() calls runTask(), so
        // that rejection also ends the separate executor, and no handler is removed.
        CancellationToken token = TestContext.Current.CancellationToken;
        var log = new ConcurrentQueue<string>();
        var loop = new DefaultEventLoop();
        var executors = new DefaultEventExecutorGroup(1);
        var reachedSeparateExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var channel = new LocalChannel();
            channel.Pipeline.AddLast(executors, "h", new Recorder("h", log, () =>
            {
                reachedSeparateExecutor.TrySetResult();
                loop.TerminationFuture.ContinueWith(_ =>
                {
                }, TaskScheduler.Default).Wait(token);
            }));
            channel.Pipeline.AddLast("b", new Recorder("b", log));
            await loop.RegisterAsync(channel).WaitAsync(token);
            await loop.SubmitAsync(() =>
            {
            }, token);
            await executors.SubmitAsync(() =>
            {
            }, token);

            Task shutdown = loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            await reachedSeparateExecutor.Task.WaitAsync(token);
            await shutdown.WaitAsync(token);
            // MultithreadEventExecutorGroup's termination listener succeeds once every child has terminated, whatever
            // ended the child.
            await executors.TerminationFuture.WaitAsync(token);
            Assert.True(executors.IsTerminated());

            Assert.False(channel.IsOpen);
            Assert.Equal(new[] { "h", "b" }, HandlerNames(channel));
            Assert.Contains("h.exception:" + nameof(RejectedExecutionException), log);
            Assert.DoesNotContain("b.unregistered", log);
            Assert.DoesNotContain(log, entry => entry.EndsWith(".removed", StringComparison.Ordinal));
        }
        finally
        {
            await executors.StopAsync().ContinueWith(_ =>
            {
            }, TaskScheduler.Default);
            await loop.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AQuietPeriodAdmitsTheSeparateExecutorReturnHop()
    {
        // During the quiet period the loop is ST_SHUTTING_DOWN and still accepts tasks, so the hand-backs from the
        // separate executor run and every handler is removed before termination.
        CancellationToken token = TestContext.Current.CancellationToken;
        var log = new ConcurrentQueue<string>();
        var loop = new DefaultEventLoop();
        var executors = new DefaultEventExecutorGroup(1);

        try
        {
            var channel = new LocalChannel();
            channel.Pipeline.AddLast(executors, "h", new Recorder("h", log, () => Thread.Sleep(100)));
            channel.Pipeline.AddLast("b", new Recorder("b", log));
            await loop.RegisterAsync(channel).WaitAsync(token);
            await loop.SubmitAsync(() =>
            {
            }, token);
            await executors.SubmitAsync(() =>
            {
            }, token);

            await loop.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)).WaitAsync(token);
            await executors.SubmitAsync(() =>
            {
            }, token);

            Assert.False(channel.IsOpen);
            Assert.Empty(HandlerNames(channel));
            Assert.Contains("b.unregistered", log);
            Assert.Contains("b.removed", log);
            Assert.Contains("h.removed", log);
        }
        finally
        {
            await executors.StopAsync();
            await loop.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ARegistrationDuringTheQuietPeriodIsAdmittedAndClosedAtTermination()
    {
        // SingleThreadEventExecutor.execute admits tasks while the state is ST_SHUTTING_DOWN, so the registration
        // completes and the shutdown closes the channel.
        CancellationToken token = TestContext.Current.CancellationToken;
        var log = new ConcurrentQueue<string>();
        var loop = new DefaultEventLoop();

        try
        {
            await loop.SubmitAsync(() =>
            {
            }, token);
            Task shutdown = loop.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
            Assert.True(loop.IsShuttingDown());
            var channel = new LocalChannel();
            channel.Pipeline.AddLast("b", new Recorder("b", log));

            await loop.RegisterAsync(channel).WaitAsync(token);
            await shutdown.WaitAsync(token);
            await channel.CloseFuture.WaitAsync(token);

            Assert.False(channel.IsOpen);
            Assert.Empty(HandlerNames(channel));
            Assert.Equal(new[] { "b.added", "b.unregistered", "b.removed" }, log.ToArray());
        }
        finally
        {
            await loop.StopAsync();
        }
    }
}
