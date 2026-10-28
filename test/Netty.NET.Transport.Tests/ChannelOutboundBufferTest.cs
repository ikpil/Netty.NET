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
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// CLR: borrowed ReadOnlyMemory<byte> views replace NIO ByteBuffer views, and cancellation tokens claim
// native Task results. The original queue/flush boundaries and cancelled empty-entry assertions remain.
#pragma warning disable xUnit1051, xUnit1069 // Original default-token calls are bounded by fixture timeouts/latches.
public class ChannelOutboundBufferTest
{
    [Fact]
    public void TestEmptyNioBuffers()
    {
        var buffer = new ChannelOutboundBuffer(new TestChannel());
        Assert.Equal(0, buffer.MemoryCount);
        var memories = buffer.GetReadableMemories();
        Assert.NotNull(memories);
        foreach (var memory in memories)
            Assert.True(memory.IsEmpty);
        Assert.Equal(0, buffer.MemoryCount);
        Release(buffer);
    }
    [Fact]
    public void TestNioBuffersCancelledRemoveBytes()
    {
        var buffer = new ChannelOutboundBuffer(new TestChannel());
        var b1 = Unpooled.WrappedBuffer(new byte[] { 0 });
        int r1 = b1.ReadableBytes;
        Task p1 = buffer.AddMessage(b1, r1);
        var b2 = Unpooled.WrappedBuffer(new byte[] { 0, 1 });
        int r2 = b2.ReadableBytes;
        using var cancellation = new CancellationTokenSource();
        Task p2 = buffer.AddMessage(b2, r2, cancellation.Token);
        cancellation.Cancel();
        var b3 = Unpooled.WrappedBuffer(new byte[] { 0 });
        int r3 = b3.ReadableBytes;
        Task p3 = buffer.AddMessage(b3, r3);
        buffer.AddFlush();
        var memories = buffer.GetReadableMemories();
        Assert.Equal(2, buffer.MemoryCount);
        Assert.NotNull(memories);
        Assert.Equal(r1, memories[0].Length);
        Assert.Equal(r3, memories[1].Length);
        buffer.RemoveBytes(r1 + r3);
        Assert.Equal(0, b1.ReferenceCount);
        Assert.Equal(0, b2.ReferenceCount);
        Assert.Equal(0, b3.ReferenceCount);
        Assert.True(buffer.IsEmpty);
        Assert.True(p1.IsCompletedSuccessfully);
        Assert.True(p2.IsCanceled);
        Assert.True(p3.IsCompletedSuccessfully);
        Release(buffer);
    }
    [Fact]
    public void TestNioBuffersSingleBacked()
    {
        var buffer = new ChannelOutboundBuffer(new TestChannel());
        Assert.Equal(0, buffer.MemoryCount);
        var data = Unpooled.CopiedBuffer("buf1", Encoding.ASCII);
        var memory = data.ReadableMemory;
        buffer.AddMessage(data, data.ReadableBytes);
        Assert.Equal(0, buffer.MemoryCount);
        buffer.AddFlush();
        var memories = buffer.GetReadableMemories();
        Assert.NotNull(memories);
        Assert.Equal(1, buffer.MemoryCount);
        for (int i = 0; i < buffer.MemoryCount; i++)
            if (i == 0)
                Assert.True(memories[i].Span.SequenceEqual(memory.Span));
            else
                Assert.True(memories[i].IsEmpty);
        Release(buffer);
    }
    [Fact]
    public void TestNioBuffersExpand()
    {
        var buffer = new ChannelOutboundBuffer(new TestChannel());
        var data = Unpooled.DirectBuffer().WriteBytes(Encoding.ASCII.GetBytes("buf1"));
        for (int i = 0; i < 64; i++)
            buffer.AddMessage(data.Copy(), data.ReadableBytes);
        Assert.Equal(0, buffer.MemoryCount);
        buffer.AddFlush();
        var memories = buffer.GetReadableMemories();
        Assert.Equal(64, buffer.MemoryCount);
        for (int i = 0; i < buffer.MemoryCount; i++)
            Assert.True(memories[i].Span.SequenceEqual(data.ReadableMemory.Span));
        Release(buffer);
        data.Release();
    }
    [Fact]
    public void TestNioBuffersExpand2()
    {
        var buffer = new ChannelOutboundBuffer(new TestChannel());
        var composite = Unpooled.CompositeBuffer(256);
        var data = Unpooled.DirectBuffer().WriteBytes(Encoding.ASCII.GetBytes("buf1"));
        for (int i = 0; i < 65; i++)
            composite.AddComponent(data.Copy(), true);
        buffer.AddMessage(composite, composite.ReadableBytes);
        Assert.Equal(0, buffer.MemoryCount);
        buffer.AddFlush();
        var memories = buffer.GetReadableMemories();
        Assert.Equal(65, buffer.MemoryCount);
        for (int i = 0; i < buffer.MemoryCount; i++)
            if (i < 65)
                Assert.True(memories[i].Span.SequenceEqual(data.ReadableMemory.Span));
            else
                Assert.True(memories[i].IsEmpty);
        Release(buffer);
        data.Release();
    }
    [Fact]
    public void TestNioBuffersMaxCount()
    {
        var buffer = new ChannelOutboundBuffer(new TestChannel());
        var composite = Unpooled.CompositeBuffer(256);
        var data = Unpooled.DirectBuffer().WriteBytes(Encoding.ASCII.GetBytes("buf1"));
        for (int i = 0; i < 65; i++)
            composite.AddComponent(data.Copy(), true);
        int segments = 0;
        foreach (var _ in composite.ReadableSequence)
            segments++;
        Assert.Equal(65, segments);
        buffer.AddMessage(composite, composite.ReadableBytes);
        Assert.Equal(0, buffer.MemoryCount);
        buffer.AddFlush();
        const int maxCount = 10; // less than comp.nioBufferCount()
        var memories = buffer.GetReadableMemories(maxCount, int.MaxValue);
        Assert.True(buffer.MemoryCount <= maxCount, "Should not be greater than maxCount");
        for (int i = 0; i < buffer.MemoryCount; i++)
            Assert.True(memories[i].Span.SequenceEqual(data.ReadableMemory.Span));
        Release(buffer);
        data.Release();
    }
    [Fact]
    public void TestWritability()
    {
        var text = new StringBuilder();
        var channel = NewRecordingChannel(text);
        channel.Config.SetWriteBufferLowWaterMark(128 + ChannelOutboundBuffer.EntryOverhead);
        channel.Config.SetWriteBufferHighWaterMark(256 + ChannelOutboundBuffer.EntryOverhead);
        _ = channel.WriteAsync(Unpooled.Buffer().WriteZero(128));
        // Ensure exceeding the low watermark does not make channel unwritable.
        _ = channel.WriteAsync(Unpooled.Buffer().WriteZero(2));
        Assert.Equal("", text.ToString());
        channel.OutboundBuffer.AddFlush();
        // Ensure exceeding the high watermark makes channel unwritable.
        _ = channel.WriteAsync(Unpooled.Buffer().WriteZero(127));
        Assert.Equal("false ", text.ToString());
        // Ensure going down to the low watermark makes channel writable again by flushing the first write.
        Assert.True(channel.OutboundBuffer.Remove());
        Assert.True(channel.OutboundBuffer.Remove());
        Assert.Equal(127L + ChannelOutboundBuffer.EntryOverhead, channel.OutboundBuffer.TotalPendingWriteBytes);
        Assert.Equal("false true ", text.ToString());
        SafeClose(channel);
    }
    [Fact]
    public void TestUserDefinedWritability()
    {
        var text = new StringBuilder();
        var channel = NewRecordingChannel(text);
        channel.Config.SetWriteBufferLowWaterMark(128);
        channel.Config.SetWriteBufferHighWaterMark(256);
        var buffer = channel.OutboundBuffer;
        // Ensure that the default value of a user-defined writability flag is true.
        for (int i = 1; i <= 30; i++)
            Assert.True(buffer.GetUserDefinedWritability(i));
        // Ensure that setting a user-defined writability flag to false affects channel.isWritable();
        buffer.SetUserDefinedWritability(1, false);
        channel.RunPendingTasks();
        Assert.Equal("false ", text.ToString());
        // Ensure that setting a user-defined writability flag to true affects channel.isWritable();
        buffer.SetUserDefinedWritability(1, true);
        channel.RunPendingTasks();
        Assert.Equal("false true ", text.ToString());
        SafeClose(channel);
    }
    [Fact]
    public void TestUserDefinedWritability2()
    {
        var text = new StringBuilder();
        var channel = NewRecordingChannel(text);
        channel.Config.SetWriteBufferLowWaterMark(128);
        channel.Config.SetWriteBufferHighWaterMark(256);
        var buffer = channel.OutboundBuffer;
        // Ensure that setting a user-defined writability flag to false affects channel.isWritable()
        buffer.SetUserDefinedWritability(1, false);
        channel.RunPendingTasks();
        Assert.Equal("false ", text.ToString());
        // Ensure that setting another user-defined writability flag to false does not trigger
        // channelWritabilityChanged.
        buffer.SetUserDefinedWritability(2, false);
        channel.RunPendingTasks();
        Assert.Equal("false ", text.ToString());
        // Ensure that setting only one user-defined writability flag to true does not affect channel.isWritable()
        buffer.SetUserDefinedWritability(1, true);
        channel.RunPendingTasks();
        Assert.Equal("false ", text.ToString());
        // Ensure that setting all user-defined writability flags to true affects channel.isWritable()
        buffer.SetUserDefinedWritability(2, true);
        channel.RunPendingTasks();
        Assert.Equal("false true ", text.ToString());
        SafeClose(channel);
    }
    [Fact]
    public void TestMixedWritability()
    {
        var text = new StringBuilder();
        var channel = NewRecordingChannel(text);
        channel.Config.SetWriteBufferLowWaterMark(128);
        channel.Config.SetWriteBufferHighWaterMark(256);
        var buffer = channel.OutboundBuffer;
        // Trigger channelWritabilityChanged() by writing a lot.
        _ = channel.WriteAsync(Unpooled.Buffer().WriteZero(257));
        Assert.Equal("false ", text.ToString());
        // Ensure that setting a user-defined writability flag to false does not trigger channelWritabilityChanged()
        buffer.SetUserDefinedWritability(1, false);
        channel.RunPendingTasks();
        Assert.Equal("false ", text.ToString());
        // Ensure reducing the totalPendingWriteBytes down to zero does not trigger channelWritabilityChanged()
        // because of the user-defined writability flag.
        channel.Flush();
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        Assert.Equal("false ", text.ToString());
        // Ensure that setting the user-defined writability flag to true triggers channelWritabilityChanged()
        buffer.SetUserDefinedWritability(1, true);
        channel.RunPendingTasks();
        Assert.Equal("false true ", text.ToString());
        SafeClose(channel);
    }
    [Fact]
    public void TestWriteAndFlushFromWritabilityChangedCausedByCancelledWrite()
    {
        var writes = new List<Task>();
        var channel = new EmbeddedChannel(new WritabilityHandler(ctx =>
        { if (ctx.Channel.IsWritable && writes.Count == 0) writes.Add(ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 2 }))); ctx.FireChannelWritabilityChanged(); }));
        channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(256, 512));
        var cancelledBuffer = Unpooled.Buffer().WriteZero(512);
        using var cancellation = new CancellationTokenSource();
        Task cancelled = channel.WriteAsync(cancelledBuffer, cancellation.Token);
        Task second = channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 1 }));
        Assert.False(channel.IsWritable);
        cancellation.Cancel();
        Assert.True(cancelled.IsCanceled);
        // Releasing the cancelled write makes the channel writable while addFlush() runs.
        channel.Flush();
        Assert.True(channel.IsWritable);
        Assert.Single(writes);
        Assert.Equal(0, cancelledBuffer.ReferenceCount);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.True(writes[0].IsCompletedSuccessfully);
        var buffer = channel.OutboundBuffer;
        Assert.True(buffer.IsEmpty);
        Assert.Equal(0, buffer.Size);
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        // The cancelled write is replaced by an empty buffer.
        AssertOutbound(channel, 0);
        AssertOutbound(channel, 1);
        AssertOutbound(channel, 2);
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
        Assert.False(channel.IsOpen);
    }
    [Fact]
    public async Task TestCloseFromWritabilityChangedCausedByCancelledWrite()
    {
        int inactive = 0;
        var channel = new EmbeddedChannel(new WritabilityHandler(ctx =>
        { if (ctx.Channel.IsWritable) _ = ctx.CloseAsync(); ctx.FireChannelWritabilityChanged(); }, () => Interlocked.Increment(ref inactive)));
        channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(256, 512));
        var cancelledBuffer = Unpooled.Buffer().WriteZero(512);
        using var cancellation = new CancellationTokenSource();
        Task cancelled = channel.WriteAsync(cancelledBuffer, cancellation.Token);
        var second = Unpooled.WrappedBuffer(new byte[] { 1 });
        Task secondFuture = channel.WriteAsync(second);
        var third = Unpooled.WrappedBuffer(new byte[] { 2 });
        Task thirdFuture = channel.WriteAsync(third);
        Assert.False(channel.IsWritable);
        cancellation.Cancel();
        Assert.True(cancelled.IsCanceled);
        // Releasing the cancelled write makes the channel writable while addFlush() runs.
        channel.Flush();
        channel.RunPendingTasks();
        Assert.False(channel.IsOpen);
        Assert.Equal(1, inactive);
        Assert.Equal(0, cancelledBuffer.ReferenceCount);
        await Assert.ThrowsAsync<IOException>(() => secondFuture);
        await Assert.ThrowsAsync<IOException>(() => thirdFuture);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, third.ReferenceCount);
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestMultipleCancelledWritesWithEarlierFlush()
    {
        int writable = 0, sizeAtEvent = -1;
        long pendingAtEvent = -1;
        object currentAtEvent = null;
        var channel = new EmbeddedChannel();
        var buffer = channel.OutboundBuffer;
        channel.Pipeline.AddLast(new WritabilityHandler(ctx =>
        {
            if (ctx.Channel.IsWritable)
            { writable++; sizeAtEvent = buffer.Size; pendingAtEvent = buffer.TotalPendingWriteBytes; currentAtEvent = buffer.Current; }
            ctx.FireChannelWritabilityChanged();
        }));
        channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(400, 800));
        // Use the ChannelOutboundBuffer directly so nothing is written and flushedEntry stays set after the flush.
        var live0 = Unpooled.WrappedBuffer(new byte[] { 0 });
        buffer.AddMessage(live0, 1);
        buffer.AddFlush();
        Assert.Equal(1, buffer.Size);
        Assert.Same(live0, buffer.Current);
        // Cancelled writes at the head, in the middle and at the tail of the second batch.
        var cancelledBuffers = new ByteBuf[3];
        var liveBuffers = new ByteBuf[2];
        var cancellations = new CancellationTokenSource[3];
        for (int i = 0; i < 3; i++)
        {
            cancelledBuffers[i] = Unpooled.Buffer().WriteZero(300);
            cancellations[i] = new CancellationTokenSource();
            buffer.AddMessage(cancelledBuffers[i], 300, cancellations[i].Token);
            if (i < 2)
            { liveBuffers[i] = Unpooled.WrappedBuffer(new byte[] { (byte)(i + 1) }); buffer.AddMessage(liveBuffers[i], 1); }
        }
        Assert.False(channel.IsWritable);
        foreach (var cancellation in cancellations)
            cancellation.Cancel();
        buffer.AddFlush();
        Assert.Equal(1, writable);
        // The buffer must already be consistent when the event is fired.
        Assert.Equal(6, sizeAtEvent);
        Assert.Equal(buffer.TotalPendingWriteBytes, pendingAtEvent);
        Assert.Same(live0, currentAtEvent);
        Assert.Equal(6, buffer.Size);
        Assert.True(channel.IsWritable);
        foreach (var data in cancelledBuffers)
            Assert.Equal(0, data.ReferenceCount);
        Assert.Equal(1, live0.ReferenceCount);
        foreach (var data in liveBuffers)
            Assert.Equal(1, data.ReferenceCount);
        Assert.True(channel.CloseAsync().IsCompletedSuccessfully);
        Assert.Equal(0, live0.ReferenceCount);
        foreach (var data in liveBuffers)
            Assert.Equal(0, data.ReferenceCount);
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        foreach (var cancellation in cancellations)
            cancellation.Dispose();
    }
    [Fact]
    public void TestWriteAndFlushFromReleaseOfCancelledWrite()
    {
        var channel = new EmbeddedChannel();
        var writes = new List<Task>();
        var message = new ReentrantMessage(() =>
        {
            // Release of a cancelled write runs user code which writes and flushes on the same channel.
            writes.Add(channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 2 })));
        });
        using var cancellation = new CancellationTokenSource();
        Task cancelled = channel.WriteAsync(message, cancellation.Token);
        Task second = channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 1 }));
        cancellation.Cancel();
        Assert.True(cancelled.IsCanceled);
        channel.Flush();
        Assert.Single(writes);
        Assert.Equal(0, message.ReferenceCount);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.True(writes[0].IsCompletedSuccessfully);
        var buffer = channel.OutboundBuffer;
        Assert.True(buffer.IsEmpty);
        Assert.Equal(0, buffer.Size);
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        // The cancelled write is replaced by an empty buffer.
        AssertOutbound(channel, 0);
        AssertOutbound(channel, 1);
        AssertOutbound(channel, 2);
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
        Assert.False(channel.IsOpen);
    }
    [Fact(Timeout = 5000)]
    public async Task TestWriteTaskRejected()
    {
        var executor = new OneSlotExecutor();
        using var added = new CountdownEvent(1);
        using var removed = new CountdownEvent(1);
        var channel = new EmbeddedChannel();
        channel.Pipeline.AddLast(executor, "handler", new RejectWriteHandler(added, removed));
        // Lets wait until we are sure the handler was added.
        Assert.True(added.Wait(TimeSpan.FromSeconds(5)));
        using var execute = new ManualResetEventSlim();
        using var running = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { running.Set(); execute.Wait(); });
            Assert.True(running.Wait(TimeSpan.FromSeconds(5)));
            executor.Execute(() => { }); // Will not be executed but ensure the pending count is 1.
            Assert.Equal(1, executor.PendingTasks());
            Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
            var data = Unpooled.Buffer(128).WriteZero(128);
            Task future = channel.WriteAsync(data);
            channel.RunPendingTasks();
            await Assert.ThrowsAsync<RejectedExecutionException>(() => future);
            Assert.Equal(0, data.ReferenceCount);
            // In case of rejected task we should not have anything pending.
            Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
            execute.Set();
            while (executor.PendingTasks() != 0)
            {
                // Wait until there is no more pending task left.
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            channel.Pipeline.Remove("handler");
            // Ensure we do not try to shutdown the executor before we handled everything for the Channel. Otherwise
            // the Executor may reject when the Channel tries to add a task to it.
            Assert.True(removed.Wait(TimeSpan.FromSeconds(5)));
            SafeClose(channel);
        }
        finally { execute.Set(); await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)); }
    }
    private static EmbeddedChannel NewRecordingChannel(StringBuilder text) => new(new WritabilityHandler(ctx =>
    { text.Append(ctx.Channel.IsWritable ? "true" : "false").Append(' '); }));
    private static void Release(ChannelOutboundBuffer buffer) { while (buffer.Remove()) { } }
    private static void SafeClose(EmbeddedChannel channel)
    { channel.Finish(); while (channel.ReadOutbound<ByteBuf>() is { } message) message.Release(); }
    private static void AssertOutbound(EmbeddedChannel channel, int expected)
    {
        var buffer = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(buffer);
        try
        { if (expected == 0) Assert.Equal(0, buffer.ReadableBytes); else { Assert.Equal(1, buffer.ReadableBytes); Assert.Equal(expected, buffer.ReadByte()); } }
        finally { buffer.Release(); }
    }
    private sealed class WritabilityHandler(Action<ChannelHandlerContext> callback, Action inactive = null) : ChannelInboundHandlerAdapter
    {
        public override void ChannelWritabilityChanged(ChannelHandlerContext context) => callback(context);
        public override void ChannelInactive(ChannelHandlerContext context) { inactive?.Invoke(); context.FireChannelInactive(); }
    }
    private sealed class ReentrantMessage(Action deallocate) : AbstractReferenceCounted
    { protected override void Deallocate() => deallocate(); public override IReferenceCounted Touch(object hint) => this; }
    private sealed class RejectWriteHandler(CountdownEvent added, CountdownEvent removed) : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token) => Task.FromException(new InvalidOperationException("Should not be called"));
        public override void HandlerAdded(ChannelHandlerContext context) => added.Signal();
        public override void HandlerRemoved(ChannelHandlerContext context) => removed.Signal();
    }
    private sealed class OneSlotExecutor() : SingleThreadEventExecutor(null, new DefaultThreadFactory("executorPool"), true, 1, RejectedExecutionHandlers.Reject())
    {
        protected override IQueue<Action> NewTaskQueue(int maxPendingTasks) => base.NewTaskQueue(1);
        protected override void Run()
        {
            do
            { var task = TakeTask(); if (task != null) { task(); UpdateLastExecutionTime(); } } while (!ConfirmShutdown());
        }
    }
    private sealed class TestChannel : AbstractChannel
    {
        internal TestChannel() : base(null, new ChannelMetadata(false)) { SetActive(true); }
        public override EndPoint LocalAddress => throw new NotSupportedException();
        public override EndPoint RemoteAddress => throw new NotSupportedException();
        protected override bool IsCompatible(IEventLoop loop) => false;
        protected override Task DoBindAsync(EndPoint address, CancellationToken token) => throw new NotSupportedException();
        protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token) => throw new NotSupportedException();
        protected override void DoClose() => throw new NotSupportedException();
        protected override void DoBeginRead() => throw new NotSupportedException();
        protected override Task DoWriteAsync(object message) => throw new NotSupportedException();
    }
}
#pragma warning restore xUnit1051, xUnit1069
