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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// CLR: TaskCompletionSource is the original producer role. ExecuteSynchronously continuations on
// default producers retain the original reentrant listener scenarios; consumer-only Tasks are awaited
// where the CLR completion bridge is asynchronous. No JVM Future/Promise facade is introduced.
#pragma warning disable xUnit1051 // Source operations use default tokens; waits are bounded and consume the test token.
public class PendingWriteQueueTest
{
    [Fact]
    public async Task TestRemoveAndWrite() => await AssertWrite(new TestHandler((queue, context, observations) =>
    {
        Assert.False(context.Channel.IsWritable, "Should not be writable anymore");
        Task future = queue.RemoveAndWriteAsync();
        observations.Add(OnCompleted(future, () => AssertQueueEmpty(queue)));
        context.Flush();
    }), 1);
    [Fact]
    public async Task TestRemoveAndWriteAll() => await AssertWrite(new TestHandler((queue, context, observations) =>
    {
        Assert.False(context.Channel.IsWritable, "Should not be writable anymore");
        Task future = queue.RemoveAndWriteAllAsync();
        observations.Add(OnCompleted(future, () => AssertQueueEmpty(queue)));
        context.Flush();
    }), 3);
    [Fact]
    public async Task TestRemoveAndFail() => await AssertWriteFails(new TestHandler((queue, context, _) =>
    { queue.RemoveAndFail(new TestException()); context.Flush(); }), 1);
    [Fact]
    public async Task TestRemoveAndFailAll() => await AssertWriteFails(new TestHandler((queue, context, _) =>
    { queue.RemoveAndFailAll(new TestException()); context.Flush(); }), 3);
    [Fact]
    public void ShouldFireChannelWritabilityChangedAfterRemoval()
    {
        var message = Unpooled.CopiedBuffer("test", Encoding.ASCII);
        var handler = new RemovalOnWritabilityHandler();
        var channel = new EmbeddedChannel(handler);
        channel.Config.SetWriteBufferLowWaterMark(1);
        channel.Config.SetWriteBufferHighWaterMark(3);
        // Trigger channelWritabilityChanged() by adding a message that's larger than the high watermark.
        handler.Queue.Add(message, new TaskCompletionSource());
        channel.Finish();
        Assert.Equal(0, message.ReferenceCount);
    }
    private static async Task AssertWrite(TestHandler handler, int count)
    {
        var buffer = Unpooled.CopiedBuffer("Test", Encoding.ASCII);
        var channel = new EmbeddedChannel(handler);
        channel.Config.SetWriteBufferLowWaterMark(1);
        channel.Config.SetWriteBufferHighWaterMark(3);
        var buffers = new ByteBuf[count];
        for (int i = 0; i < count; i++)
            buffers[i] = buffer.RetainedDuplicate();
        Assert.True(channel.WriteOutbound(buffers));
        Assert.True(channel.Finish());
        await channel.CloseCompletion;
        await Task.WhenAll(handler.Observations).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        for (int i = 0; i < count; i++)
            AssertBuffer(channel, buffer);
        buffer.Release();
        Assert.Null(channel.ReadOutbound());
    }
    private static void AssertBuffer(EmbeddedChannel channel, ByteBuf expected)
    {
        var written = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(written);
        Assert.Equal(expected.ReadableMemory.ToArray(), written.ReadableMemory.ToArray());
        written.Release();
    }
    private static void AssertQueueEmpty(PendingWriteQueue queue)
    {
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Size);
        Assert.Equal(0, queue.Bytes);
        Assert.Null(queue.Current);
        Assert.Null(queue.RemoveAndWriteAsync());
        Assert.Null(queue.RemoveAndWriteAllAsync());
    }
    private static async Task AssertWriteFails(TestHandler handler, int count)
    {
        var buffer = Unpooled.CopiedBuffer("Test", Encoding.ASCII);
        var channel = new EmbeddedChannel(handler);
        var buffers = new ByteBuf[count];
        for (int i = 0; i < count; i++)
            buffers[i] = buffer.RetainedDuplicate();
        Assert.Throws<TestException>(() => Assert.False(channel.WriteOutbound(buffers)));
        Assert.False(channel.Finish());
        await channel.CloseCompletion;
        buffer.Release();
        Assert.Null(channel.ReadOutbound());
    }
    private static EmbeddedChannel NewChannel()
    {
        // Add a handler so we can access a ChannelHandlerContext via the ChannelPipeline.
        return new EmbeddedChannel(new EmptyHandler());
    }
    [Fact]
    public void TestRemoveAndFailAllReentrantFailAll()
    {
        var channel = NewChannel();
        var queue = new PendingWriteQueue(channel.Pipeline.FirstContext);
        var promise = new TaskCompletionSource();
        Task listener = OnCompleted(promise.Task, () => queue.RemoveAndFailAll(new InvalidOperationException()));
        queue.Add(1L, promise);
        var second = new TaskCompletionSource();
        queue.Add(2L, second);
        queue.RemoveAndFailAll(new Exception());
        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.True(second.Task.IsCompleted);
        Assert.False(second.Task.IsCompletedSuccessfully);
        Assert.True(listener.IsCompletedSuccessfully);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestRemoveAndFailReentrantRemoveAndFail()
    {
        var channel = NewChannel();
        var queue = new PendingWriteQueue(channel.Pipeline.FirstContext);
        var firstBuffer = Unpooled.Buffer().WriteZero(8);
        var secondBuffer = Unpooled.Buffer().WriteZero(8);
        var thirdBuffer = Unpooled.Buffer().WriteZero(8);
        var first = new TaskCompletionSource();
        Task listener = OnCompleted(first.Task, () => queue.RemoveAndFail(new InvalidOperationException()));
        var second = new TaskCompletionSource();
        var third = new TaskCompletionSource();
        queue.Add(firstBuffer, first);
        queue.Add(secondBuffer, second);
        queue.Add(thirdBuffer, third);
        long bytesPerWrite = queue.Bytes / 3;
        queue.RemoveAndFail(new Exception());
        // The listener of promise1 removed and failed the second write.
        Assert.True(first.Task.IsCompleted);
        Assert.True(second.Task.IsCompleted);
        Assert.False(second.Task.IsCompletedSuccessfully);
        Assert.Equal(0, firstBuffer.ReferenceCount);
        Assert.Equal(0, secondBuffer.ReferenceCount);
        Assert.False(third.Task.IsCompleted);
        Assert.Equal(1, thirdBuffer.ReferenceCount);
        Assert.Equal(1, queue.Size);
        Assert.Equal(bytesPerWrite, queue.Bytes);
        Assert.True(listener.IsCompletedSuccessfully);
        queue.RemoveAndFailAll(new Exception());
        Assert.True(third.Task.IsCompleted);
        Assert.Equal(0, thirdBuffer.ReferenceCount);
        Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestRemoveAndWriteAllReentrantWrite()
    {
        var channel = new EmbeddedChannel(new FlushEveryWrite(), new ChannelOutboundHandlerAdapter());
        var queue = new PendingWriteQueue(channel.Pipeline.LastContext);
        var first = new TaskCompletionSource();
        var third = new TaskCompletionSource();
        Task listener = OnCompleted(first.Task, () => queue.Add(3L, third));
        queue.Add(1L, first);
        var second = new TaskCompletionSource();
        queue.Add(2L, second);
        queue.RemoveAndWriteAllAsync();
        Assert.True(first.Task.IsCompleted);
        Assert.True(first.Task.IsCompletedSuccessfully);
        Assert.True(second.Task.IsCompleted);
        Assert.True(second.Task.IsCompletedSuccessfully);
        Assert.True(third.Task.IsCompleted);
        Assert.True(third.Task.IsCompletedSuccessfully);
        Assert.True(listener.IsCompletedSuccessfully);
        Assert.True(channel.Finish());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Equal(2L, channel.ReadOutbound<long>());
        Assert.Equal(3L, channel.ReadOutbound<long>());
    }
    [Fact]
    public void TestRemoveAndWriteAllWithVoidPromise()
    {
        var channel = new EmbeddedChannel(new FlushEveryWrite(), new ChannelOutboundHandlerAdapter());
        var queue = new PendingWriteQueue(channel.Pipeline.LastContext);
        var promise = new TaskCompletionSource();
        queue.Add(1L, promise);
        // CLR: discarding a returned Task replaces voidPromise; the aggregate remains a native Task.WhenAll.
        _ = queue.Add(2L);
        queue.RemoveAndWriteAllAsync();
        Assert.True(channel.Finish());
        Assert.True(promise.Task.IsCompleted);
        Assert.True(promise.Task.IsCompletedSuccessfully);
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Equal(2L, channel.ReadOutbound<long>());
    }
    [Fact]
    public void TestRemoveAndFailAllReentrantWrite()
    {
        var failOrder = new List<int>();
        var channel = NewChannel();
        var queue = new PendingWriteQueue(channel.Pipeline.FirstContext);
        var first = new TaskCompletionSource();
        var third = new TaskCompletionSource();
        Task thirdListener = OnCompleted(third.Task, () => failOrder.Add(3));
        Task firstListener = OnCompleted(first.Task, () => { failOrder.Add(1); queue.Add(3L, third); });
        queue.Add(1L, first);
        var second = new TaskCompletionSource();
        Task secondListener = OnCompleted(second.Task, () => failOrder.Add(2));
        queue.Add(2L, second);
        queue.RemoveAndFailAll(new Exception());
        Assert.True(first.Task.IsCompleted);
        Assert.False(first.Task.IsCompletedSuccessfully);
        Assert.True(second.Task.IsCompleted);
        Assert.False(second.Task.IsCompletedSuccessfully);
        Assert.True(third.Task.IsCompleted);
        Assert.False(third.Task.IsCompletedSuccessfully);
        Assert.False(channel.Finish());
        Assert.True(firstListener.IsCompletedSuccessfully);
        Assert.True(secondListener.IsCompletedSuccessfully);
        Assert.True(thirdListener.IsCompletedSuccessfully);
        Assert.Equal(1, failOrder[0]);
        Assert.Equal(2, failOrder[1]);
        Assert.Equal(3, failOrder[2]);
    }
    [Fact]
    public async Task TestRemoveAndWriteAllReentrance()
    {
        var channel = NewChannel();
        var queue = new PendingWriteQueue(channel.Pipeline.FirstContext);
        var first = new TaskCompletionSource();
        Task listener = OnCompleted(first.Task, () => queue.RemoveAndWriteAllAsync());
        queue.Add(1L, first);
        var second = new TaskCompletionSource();
        queue.Add(2L, second);
        _ = queue.RemoveAndWriteAllAsync();
        channel.Flush();
        // Native result propagation can cross a Task continuation; await it without weakening queue/output assertions.
        await Task.WhenAll(first.Task, second.Task, listener).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(first.Task.IsCompletedSuccessfully);
        Assert.True(second.Task.IsCompletedSuccessfully);
        Assert.True(channel.Finish());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Equal(2L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.Null(channel.ReadInbound());
    }
    // See https://github.com/netty/netty/issues/3967
    [Fact]
    public void TestCloseChannelOnCreation()
    {
        var channel = NewChannel();
        var context = channel.Pipeline.FirstContext;
        Assert.True(channel.CloseAsync().IsCompletedSuccessfully);
        var queue = new PendingWriteQueue(context);
        var error = new InvalidOperationException();
        var promise = new TaskCompletionSource();
        queue.Add(1L, promise);
        queue.RemoveAndFailAll(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => promise.Task.GetAwaiter().GetResult()));
    }
    private static Task OnCompleted(Task task, Action callback) => task.ContinueWith(completed =>
    {
        // Observe native faults even when the source listener only inspects completion state.
        _ = completed.Exception;
        callback();
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private sealed class TestHandler(Action<PendingWriteQueue, ChannelHandlerContext, List<Task>> flush) : ChannelDuplexHandler
    {
        private PendingWriteQueue _queue;
        private int _expectedSize;
        internal readonly List<Task> Observations = new();
        public override void HandlerAdded(ChannelHandlerContext context) => _queue = new PendingWriteQueue(context);
        public override void ChannelActive(ChannelHandlerContext context)
        { base.ChannelActive(context); AssertQueueEmpty(_queue); Assert.True(context.Channel.IsWritable, "Should be writable"); }
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        {
            Task result = _queue.Add(message, token);
            Assert.False(_queue.IsEmpty);
            Assert.Equal(++_expectedSize, _queue.Size);
            Assert.NotNull(_queue.Current);
            return result;
        }
        public override void Flush(ChannelHandlerContext context) => flush(_queue, context, Observations);
    }
    private sealed class RemovalOnWritabilityHandler : ChannelInboundHandlerAdapter
    {
        internal PendingWriteQueue Queue;
        public override void HandlerAdded(ChannelHandlerContext context) => Queue = new PendingWriteQueue(context);
        public override void ChannelWritabilityChanged(ChannelHandlerContext context)
        {
            if (Queue.Current is not ByteBuf message)
                return;
            Assert.Equal(1, message.ReferenceCount);
            // This call will trigger another channelWritabilityChanged() event because the number of
            // pending bytes will go below the low watermark.
            //
            // If PendingWriteQueue.remove() did not remove the current entry before triggering
            // channelWritabilityChanged() event, we will end up with attempting to remove the same
            // element twice, resulting in the double release.
            Queue.Remove();
            Assert.Equal(0, message.ReferenceCount);
        }
    }
    private sealed class FlushEveryWrite : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        {
            // Convert to writeAndFlush(...) so the promise will be notified by the transport.
            return context.WriteAndFlushAsync(message, token);
        }
    }
    private sealed class TestException : Exception { }
    private sealed class EmptyHandler : ChannelHandlerAdapter { }
}
#pragma warning restore xUnit1051
