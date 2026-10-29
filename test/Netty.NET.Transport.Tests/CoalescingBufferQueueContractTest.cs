using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

public class CoalescingBufferQueueContractTest
{
    [Fact]
    public void ReleaseFailureStillDrainsBuffersAndNotifiesRemainingProducers()
    {
        var channel = new EmbeddedChannel();
        var queue = new CoalescingBufferQueue(channel, 4, true);
        var first = Unpooled.Buffer(1).WriteByte(1);
        var second = Unpooled.Buffer(1).WriteByte(2);
        var callbackError = new InvalidOperationException("Callback failed.");
        var writeError = new IOException("Write failed.");
        queue.Add(first, _ => throw callbackError);
        var producer = new TaskCompletionSource();
        queue.Add(second, producer);
        var failure = Assert.Throws<InvalidOperationException>(() => queue.ReleaseAndFailAll(writeError));
        Assert.Same(callbackError, failure.InnerException);
        Assert.Same(writeError, producer.Task.Exception.InnerException);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.ReadableBytes);
        Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void AggregateCallbackFailureDoesNotPreventOtherProducerCompletion()
    {
        var channel = new EmbeddedChannel();
        var queue = new CoalescingBufferQueue(channel);
        var first = Unpooled.Buffer(1).WriteByte(1);
        var second = Unpooled.Buffer(1).WriteByte(2);
        queue.Add(first, _ => throw new InvalidOperationException("Callback failed."));
        var producer = new TaskCompletionSource();
        queue.Add(second, producer);
        var aggregate = new TaskCompletionSource();
        var data = queue.Remove(2, aggregate);
        Assert.Equal(1, data.ReadByte());
        Assert.Equal(2, data.ReadByte());
        data.Release();
        aggregate.SetResult();
        Assert.True(producer.Task.IsCompletedSuccessfully);
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.False(channel.Finish());
    }
    [Fact]
    public async Task RejectedCompletionDispatchDoesNotLeaveTheNativeProducerPending()
    {
        var channel = new DelayedChannel();
        var executor = new RejectableExecutor();
        var handler = new EmptyHandler();
        channel.Pipeline.AddLast(executor, "context", handler);
        var context = channel.Pipeline.Context(handler);
        // Retain a context whose executor can stop independently, as with offloaded pipeline handlers.
        channel.Pipeline.Remove(handler);
        var queue = new CoalescingBufferQueue(channel);
        var producer = new TaskCompletionSource();
        var message = Unpooled.Buffer(1).WriteByte(42);
        queue.Add(message, producer);
        Task write = queue.WriteAndRemoveAllAsync(context);
        channel.Flush();
        executor.Stop();
        channel.AllowWrite.SetResult();
        await Assert.ThrowsAsync<RejectedExecutionException>(() => write.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(producer.Task.IsCompleted);
        await Assert.ThrowsAsync<RejectedExecutionException>(() => producer.Task);
        Assert.Same(message, channel.ReadOutbound());
        message.Release();
        Assert.Equal(0, message.ReferenceCount);
        Assert.False(channel.Finish());
    }
    private sealed class DelayedChannel : EmbeddedChannel
    {
        internal readonly TaskCompletionSource AllowWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task DoWriteAsync(object message)
        { await AllowWrite.Task.ConfigureAwait(false); await base.DoWriteAsync(message).ConfigureAwait(false); }
    }
    private sealed class EmptyHandler : ChannelHandlerAdapter { }
    private sealed class RejectableExecutor : AbstractEventExecutor
    {
        private readonly Thread _owner = Thread.CurrentThread;
        private bool _stopped;
        internal void Stop() => _stopped = true;
        public override bool InEventLoop(Thread thread) => !_stopped && ReferenceEquals(thread, _owner);
        public override void Execute(Action task) { if (_stopped) throw new RejectedExecutionException(); task(); }
        public override Task Termination => Task.CompletedTask;
        public override bool IsShuttingDown() => _stopped;
        public override bool IsShutdown() => _stopped;
        public override bool IsTerminated() => _stopped;
        public override bool AwaitTermination(TimeSpan timeout) => _stopped;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) { Stop(); return Task.CompletedTask; }
        [Obsolete]
        public override void Shutdown() => Stop();
    }
}
