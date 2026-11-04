/*
 * Copyright 2017 The Netty Project
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051, xUnit1031 // Preserve original owner-blocking leader and independent cleanup.
public partial class SocketHalfClosedSourceTest
{
    private static int MaxReadCompleteWithNoDataAfterInputShutdown()
        => 2; // nio needs read flag to detect full closure.

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestHalfClosureReceiveDataOnFinalWait2StateWhenSoLingerSet(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Server.ChildOption(ChannelOption.SO_LINGER, 1).ChildHandler(new FinWaitReader(done));
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Handler(new FinWaitWriter());
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        await done.Task.WaitAsync(TestContext.Current.CancellationToken);
        await client.CloseAsync(); await server.CloseAsync();
    }
    private sealed class FinWaitReader(TaskCompletionSource done) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext ctx) => _ = ((TcpSocketChannel)ctx.Channel).ShutdownOutputAsync();
        public override void ChannelRead(ChannelHandlerContext ctx, object message)
        { ((ByteBuf)message).Release(); done.TrySetResult(); }
    }
    private sealed class FinWaitWriter : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE)) _ = ctx.WriteAndFlushAsync(ctx.Allocator.Buffer().WriteZero(16));
            if (ReferenceEquals(evt, ChannelInputShutdownReadComplete.INSTANCE)) _ = ctx.CloseAsync();
        }
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestHalfClosureOnlyOneEventWhenAutoRead(bool adaptive)
        => SingleEvent(adaptive, false).WaitAsync(TestContext.Current.CancellationToken);

    internal static async Task SingleEvent(bool adaptive, bool actualHalfClose)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var handler = new SingleEventReader();
        // The original casts ctx, rather than ctx.channel(), to DuplexChannel. Its exceptionCaught
        // closes the peer. Preserve that observable FIN path here; a separate native test uses a real half-close.
        fixture.Server.ChildHandler(new SingleEventPeer(actualHalfClose));
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Option(ChannelOption.AUTO_READ, true).Handler(handler);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        await client.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.ShutdownEvents); Assert.Equal(1, handler.FinalEvents);
        await server.CloseAsync();
    }
    private sealed class SingleEventPeer(bool half) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            if (half) _ = ((TcpSocketChannel)ctx.Channel).ShutdownOutputAsync();
            else _ = ctx.CloseAsync();
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => _ = ctx.CloseAsync();
    }
    private sealed class SingleEventReader : ChannelInboundHandlerAdapter
    {
        internal int ShutdownEvents, FinalEvents;
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE)) Interlocked.Increment(ref ShutdownEvents);
            else if (ReferenceEquals(evt, ChannelInputShutdownReadComplete.INSTANCE))
            {
                Interlocked.Increment(ref FinalEvents);
                _ = ctx.Executor.ScheduleAsync(() => { _ = ctx.CloseAsync(); }, TimeSpan.FromMilliseconds(100));
            }
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => _ = ctx.CloseAsync();
    }

    [Theory(Timeout = 30000)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestAllDataReadAfterHalfClosure(bool adaptive, bool autoRead)
    {
        const int totalServerBytesWritten = 1024 * 16, numReadsPerReadLoop = 2;
        await using var fixture = new ShutdownFixture(adaptive);
        var writer = new HalfClosingWriter(totalServerBytesWritten);
        var reader = new HalfDataReader(totalServerBytesWritten, autoRead);
        fixture.Server.ChildHandler(writer);
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Option(ChannelOption.AUTO_READ, autoRead)
            .Option(ChannelOption.RECVBUF_ALLOCATOR, new TestNumReadsRecvByteBufAllocator(numReadsPerReadLoop)).Handler(reader);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        client.Read();
        await writer.Initialized.Task.WaitAsync(TestContext.Current.CancellationToken);
        await reader.AllData.Task.WaitAsync(TestContext.Current.CancellationToken);
        await reader.HalfClosed.Task.WaitAsync(TestContext.Current.CancellationToken);
        // In practice this should be much less, as we allow numReadsPerReadLoop per wakeup, but we limit the
        // number of bytes to 1 per read so in theory we may need more. We check below that readComplete is called
        // when data is actually read.
        Assert.True(totalServerBytesWritten > reader.ReadCompletes, "too many read complete events: " + reader.ReadCompletes);
        Assert.True(reader.EmptyReadCompletes <= MaxReadCompleteWithNoDataAfterInputShutdown(),
            "too many readComplete with no data: " + reader.EmptyReadCompletes + " readComplete: " + reader.ReadCompletes);
        await client.CloseAsync(); await server.CloseAsync();
    }
    private sealed class HalfClosingWriter(int count) : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource Initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            var buffer = ctx.Allocator.Buffer(count); buffer.WriterIndex = buffer.Capacity;
            _ = WriteAndHalfClose(ctx, buffer); Initialized.TrySetResult();
        }
        private static async Task WriteAndHalfClose(ChannelHandlerContext ctx, ByteBuf buffer)
        { try { await ctx.WriteAndFlushAsync(buffer); } finally { await ((TcpSocketChannel)ctx.Channel).ShutdownOutputAsync(); } }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => _ = ctx.CloseAsync();
    }
    private sealed class HalfDataReader(int total, bool autoRead) : ChannelInboundHandlerAdapter
    {
        private int _bytesRead, _sinceComplete;
        internal int ReadCompletes, EmptyReadCompletes;
        internal readonly TaskCompletionSource AllData = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource HalfClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelRead(ChannelHandlerContext ctx, object message)
        {
            var buffer = (ByteBuf)message; _bytesRead += buffer.ReadableBytes; _sinceComplete += buffer.ReadableBytes; buffer.Release();
        }
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE)) HalfClosed.TrySetResult();
            else if (ReferenceEquals(evt, ChannelInputShutdownReadComplete.INSTANCE)) _ = ctx.CloseAsync();
        }
        public override void ChannelReadComplete(ChannelHandlerContext ctx)
        {
            if (_sinceComplete == 0) Interlocked.Increment(ref EmptyReadCompletes); else _sinceComplete = 0;
            Interlocked.Increment(ref ReadCompletes);
            if (_bytesRead == total) AllData.TrySetResult();
            if (!autoRead) ctx.Read();
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => _ = ctx.CloseAsync();
    }

    [Theory(Timeout = 30000)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task TestAutoCloseFalseDoesShutdownOutput(bool adaptive, bool allowHalfClosed, bool clientIsLeader)
    {
        // This test only works on Linux / BSD / MacOS as we assume some semantics that are not true for Windows.
        if (OperatingSystem.IsWindows()) Assert.Skip("Original SO_LINGER=0 reset/read ordering requires Linux / BSD / MacOS.");
        const int expectedBytes = 100;
        await using var fixture = new ShutdownFixture(adaptive);
        using var followerClosed = new ManualResetEventSlim();
        var state = new ResetState();
        var leader = new AutoCloseFalseLeader(expectedBytes, followerClosed, state, TestContext.Current.CancellationToken);
        var follower = new AutoCloseFalseFollower(expectedBytes, followerClosed, state);
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, allowHalfClosed).Option(ChannelOption.AUTO_CLOSE, false)
            .Option(ChannelOption.SO_LINGER, 0).Handler(clientIsLeader ? leader : follower);
        fixture.Server.ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, allowHalfClosed).ChildOption(ChannelOption.AUTO_CLOSE, false)
            .ChildOption(ChannelOption.SO_LINGER, 0).ChildHandler(clientIsLeader ? follower : leader);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        await state.Done.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Null(state.Cause); Assert.True(leader.SeenOutputShutdown);
        await client.CloseAsync(); await server.CloseAsync();
    }
    private sealed class ResetState
    {
        internal Exception Cause;
        private int _remaining = 2;
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Signal() { if (Interlocked.Decrement(ref _remaining) == 0) Done.TrySetResult(); }
        internal void Fail(Exception cause) { Interlocked.Exchange(ref Cause, cause); Signal(); }
    }
    private sealed class AutoCloseFalseFollower(int expected, ManualResetEventSlim followerClosed, ResetState state) : SimpleChannelInboundHandler<ByteBuf>
    {
        private int _bytesRead;
        public override void ChannelInactive(ChannelHandlerContext ctx) => CheckPrematureClose();
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) { _ = ctx.CloseAsync(); CheckPrematureClose(); }
        protected override void ChannelRead0(ChannelHandlerContext ctx, ByteBuf message)
        {
            _bytesRead += message.ReadableBytes;
            if (_bytesRead >= expected)
            {
                // We write a reply and immediately close our end of the socket.
                var buffer = ctx.Allocator.Buffer(expected); buffer.WriterIndex += expected;
                _ = ReplyAndClose(ctx, buffer);
            }
        }
        private async Task ReplyAndClose(ChannelHandlerContext ctx, ByteBuf buffer)
        {
            try { await ctx.WriteAndFlushAsync(buffer); } catch { }
            try { await ctx.CloseAsync(); } catch { }
            // This is a bit racy but there is no better way how to handle this in Java11.
            // The problem is that on close() the underlying FD will not actually be closed directly
            // but the close will be done after the Selector did process all events. Because of
            // this we will need to give it a bit time to ensure the FD is actual closed before we
            // count down the latch and try to write.
            // CLR Socket.Dispose closes directly; retain the original 200 ms sequencing barrier.
            await ctx.Executor.ScheduleAsync(followerClosed.Set, TimeSpan.FromMilliseconds(200));
        }
        private void CheckPrematureClose()
        { if (_bytesRead < expected) state.Fail(new InvalidOperationException("follower premature close")); }
    }
    private sealed class AutoCloseFalseLeader(int expected, ManualResetEventSlim followerClosed, ResetState state, CancellationToken token) : SimpleChannelInboundHandler<ByteBuf>
    {
        private int _bytesRead;
        internal bool SeenOutputShutdown;
        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            var buffer = ctx.Allocator.Buffer(expected); buffer.WriterIndex += expected;
            _ = ctx.WriteAndFlushAsync(buffer.RetainedDuplicate());
            // We wait here to ensure that we write before we have a chance to process the outbound
            // shutdown event.
            try { followerClosed.Wait(token); }
            catch { buffer.Release(); throw; }
            // This write should fail, but we should still be allowed to read the peer's data
            _ = CheckWrite(ctx.WriteAndFlushAsync(buffer));
        }
        private async Task CheckWrite(Task write)
        {
            try { await write; state.Fail(new InvalidOperationException("second write should have failed!")); }
            catch { }
        }
        protected override void ChannelRead0(ChannelHandlerContext ctx, ByteBuf message)
        { _bytesRead += message.ReadableBytes; if (_bytesRead >= expected) state.Signal(); }
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        { if (evt is ChannelOutputShutdownEvent) { SeenOutputShutdown = true; state.Signal(); } }
        public override void ChannelInactive(ChannelHandlerContext ctx) => CheckPrematureClose();
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) { _ = ctx.CloseAsync(); CheckPrematureClose(); }
        private void CheckPrematureClose()
        { if (_bytesRead < expected || !SeenOutputShutdown) state.Fail(new InvalidOperationException("leader premature close")); }
    }
}
