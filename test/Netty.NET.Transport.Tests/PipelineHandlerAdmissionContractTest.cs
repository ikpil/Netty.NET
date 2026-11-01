using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0612, xUnit1051 // Exercise the original unordered-executor contract; finish owner cleanup.
public class PipelineHandlerAdmissionContractTest
{
    [Fact]
    public void RemovedHandlerDoesNotReceiveItsOwnReadFailure()
    {
        // Source invokeExceptionCaught checks invokeHandler again: a read callback may unlink itself before
        // throwing, so its failure must continue to the next context instead of reentering a removed handler.
        var failure = new InvalidOperationException();
        var removing = new RemoveThenThrow(failure);
        var following = new CaptureFailure();
        var channel = new Netty.NET.Transport.Channels.Embedded.EmbeddedChannel(removing, following);
        try
        {
            Assert.False(channel.WriteInbound("message"));
            Assert.Equal(0, removing.Errors);
            Assert.Same(failure, following.Cause);
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    [Fact]
    public async Task UnorderedEventCanRunBeforeTheQueuedHandlerAddedStarts()
    {
        var group = new DefaultEventLoopGroup(1);
        var executor = new HeldAdditionExecutor();
        var channel = new LocalChannel();
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AdmissionHandler(added, received);
        try
        {
            await channel.RegisterAsync(group.Next());
            channel.Pipeline.AddLast(executor, "handler", handler);
            Assert.False(added.Task.IsCompleted);
            // Source invokeHandler admits ADD_PENDING only for an unordered executor. Hold the first submitted
            // addition, run the later event on the real pool, then release the original addition afterwards.
            channel.Pipeline.FireUserEventTriggered("event");
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(added.Task.IsCompleted);
            Assert.Equal(1, handler.Events);
            executor.ReleaseAddition();
            await added.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            executor.ReleaseAddition();
            await channel.CloseAsync();
            await group.StopAsync();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }
    private sealed class RemoveThenThrow(Exception failure) : ChannelInboundHandlerAdapter
    {
        internal int Errors;
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { context.Pipeline.Remove(this); throw failure; }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => Errors++;
    }
    private sealed class CaptureFailure : ChannelInboundHandlerAdapter
    { internal Exception Cause; public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => Cause = cause; }
    private sealed class AdmissionHandler(TaskCompletionSource added, TaskCompletionSource received) : ChannelInboundHandlerAdapter
    {
        internal int Events;
        public override void HandlerAdded(ChannelHandlerContext context) => added.TrySetResult();
        public override void UserEventTriggered(ChannelHandlerContext context, object evt)
        { Interlocked.Increment(ref Events); received.TrySetResult(); }
    }
    private sealed class HeldAdditionExecutor : AbstractEventExecutor
    {
        private readonly UnorderedThreadPoolEventExecutor _pool = new(2);
        private Action _addition;
        private int _submissions;
        public override bool InEventLoop(Thread thread) => _pool.InEventLoop(thread);
        public override void Execute(Action task)
        { if (Interlocked.Increment(ref _submissions) == 1) _addition = task; else _pool.Execute(task); }
        internal void ReleaseAddition()
        { var task = Interlocked.Exchange(ref _addition, null); if (task != null) _pool.Execute(task); }
        public override bool IsShuttingDown() => _pool.IsShuttingDown();
        public override bool IsShutdown() => _pool.IsShutdown();
        public override bool IsTerminated() => _pool.IsTerminated();
        public override Task Termination => _pool.Termination;
        public override bool AwaitTermination(TimeSpan timeout) => _pool.AwaitTermination(timeout);
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => _pool.ShutdownGracefullyAsync(quietPeriod, timeout);
        [Obsolete]
        public override void Shutdown() => _pool.Shutdown();
    }
}
