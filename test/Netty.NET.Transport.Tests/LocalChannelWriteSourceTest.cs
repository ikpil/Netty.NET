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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve original per-operation timing and independently complete cleanup.
public partial class LocalChannelSourceTest
{
    [Fact]
    public async Task TestCloseInWritePromiseCompletePreservesOrder()
    {
        await using var groups = new Groups();
        var messages = new MessageCount(2);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(new MatchRead(data, messages, inactive: true))));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            IChannel copy = client;
            // Make sure a write operation is executed in the eventloop
            var completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () => copy.Pipeline.LastContext.CloseAsync());
            await messages.WaitAsync();
            await completion;
            Assert.False(client.IsOpen);
        }
        finally { await CloseChannel(client); await CloseChannel(server); data.Release(); }
    }

    [Fact]
    public async Task TestCloseAfterWriteInSameEventLoopPreservesOrder()
    {
        await using var groups = new Groups();
        var messages = new MessageCount(3);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        var echo = new EchoThenClose(data, messages);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.Shared).Channel<LocalChannel>().Handler(new MatchRead(data, messages, writeOnActive: true));
            var sb = new ServerBootstrap().Group(groups.Shared).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(echo)));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            await messages.WaitAsync();
            Assert.False(client.IsOpen);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
            // The source callback leaves this inbound duplicate owned by the fixture. Clean it up only after
            // all original assertions and close ordering, without changing the original callback's data flow.
            ReferenceCountUtil.SafeRelease(echo.Unreleased);
            data.Release();
        }
    }

    [Fact]
    public async Task TestWriteInWritePromiseCompletePreservesOrder()
    {
        await using var groups = new Groups();
        var messages = new MessageCount(2);
        var data = Unpooled.Buffer().WriteInt(sizeof(int)).WriteInt(2);
        var data2 = Unpooled.Buffer().WriteInt(sizeof(int)).WriteInt(1);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => child.Pipeline.AddLast(new ReadFrames(messages))));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            IChannel copy = client;
            // Make sure a write operation is executed in the eventloop
            Task completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () => copy.WriteAndFlushAsync(data2.RetainedDuplicate()));
            await messages.WaitAsync();
            await completion;
        }
        finally { await CloseChannel(client); await CloseChannel(server); data.Release(); data2.Release(); }
    }

    [Fact]
    public async Task TestPeerWriteInWritePromiseCompleteDifferentEventLoopPreservesOrder()
    {
        await using var groups = new Groups();
        await CheckPeerWriteInCompletion(groups.First, groups.Second, false);
    }

    [Fact]
    public async Task TestPeerWriteInWritePromiseCompleteSameEventLoopPreservesOrder()
    {
        await using var groups = new Groups();
        await CheckPeerWriteInCompletion(groups.Shared, groups.Shared, true);
    }

    private static async Task CheckPeerWriteInCompletion(IEventLoopGroup clients, IEventLoopGroup servers, bool enforceOrder)
    {
        var messages = new MessageCount(2);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        var data2 = Unpooled.WrappedBuffer(new byte[512]);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(clients).Channel<LocalChannel>()
                .Handler(new MatchRead(data2, messages, expectedCount: enforceOrder ? 1 : null));
            var sb = new ServerBootstrap().Group(servers).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child =>
                {
                    child.Pipeline.AddLast(new MatchRead(data, messages, expectedCount: enforceOrder ? 2 : null));
                    accepted.TrySetResult(child);
                }));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Make sure a write operation is executed in the eventloop
            var completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () => peer.WriteAndFlushAsync(data2.RetainedDuplicate()));
            await messages.WaitAsync();
            await completion;
        }
        finally { await CloseChannel(client); await CloseChannel(server); data.Release(); data2.Release(); }
    }

    [Fact]
    public async Task TestWriteWhilePeerIsClosedReleaseObjectAndFailPromise()
    {
        await using var groups = new Groups();
        var messageRead = new MessageCount(1);
        var writeFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = Unpooled.WrappedBuffer(new byte[1024]);
        var data2 = Unpooled.WrappedBuffer(new byte[512]);
        IChannel server = null, client = null;
        try
        {
            var cb = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var sb = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(child =>
                {
                    child.Pipeline.AddLast(new MatchRead(data, messageRead));
                    accepted.TrySetResult(child);
                }));
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = await cb.ConnectAsync(server.LocalAddress);
            var peer = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            IChannel copy = client;
            Task peerWrite = null;
            // Make sure a write operation is executed in the eventloop
            var completion = WriteThenOnOwner(client, data.RetainedDuplicate(), () =>
            {
                peerWrite = peer.EventLoop.SubmitAsync(async () =>
                {
                    // The point of this test is to write while the peer is closed, so we should
                    // ensure the peer is actually closed before we write.
                    int waitCount = 0;
                    while (copy.IsOpen)
                    {
                        try
                        { Thread.Sleep(50); }
                        catch (ThreadInterruptedException)
                        {
                            // ignored
                        }
                        Assert.True(++waitCount <= 5);
                    }
                    try
                    { await peer.WriteAndFlushAsync(data2.RetainedDuplicate()); }
                    catch (IOException) { writeFailed.TrySetResult(); }
                });
                return copy.CloseAsync();
            });
            await messageRead.WaitAsync();
            await writeFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await peer.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await client.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await completion;
            if (peerWrite != null)
                await peerWrite;
            Assert.False(client.IsOpen);
            Assert.False(peer.IsOpen);
        }
        finally { await CloseChannel(client); await CloseChannel(server); data.Release(); data2.Release(); }
    }

    private static Task WriteThenOnOwner(IChannel channel, ByteBuf message, Func<Task> completed)
    {
        // Native Task consumers cannot install a caller-owned ChannelPromise. Queue Write without flushing,
        // attach its continuation first, then Flush on the original owner. Explicitly dispatch callback state
        // access back to that owner; ExecuteSynchronously cannot override an asynchronous producer's policy.
        return channel.EventLoop.SubmitAsync(() =>
        {
            Task write = channel.WriteAsync(message);
            Task callback = write.ContinueWith(_ => channel.EventLoop.SubmitAsync(completed), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            channel.Flush();
            return callback;
        });
    }
    private sealed class MessageCount(int remaining)
    {
        private int _remaining = remaining;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Current => Math.Max(0, Volatile.Read(ref _remaining));
        internal void Signal() { if (Interlocked.Decrement(ref _remaining) == 0) _done.TrySetResult(); }
        internal Task WaitAsync() => _done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class MatchRead(ByteBuf expected, MessageCount count, bool inactive = false, bool writeOnActive = false, int? expectedCount = null)
        : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context)
        { if (writeOnActive) _ = context.WriteAndFlushAsync(expected.RetainedDuplicate()); else context.FireChannelActive(); }
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            if (expected.Equals(message) && (!expectedCount.HasValue || count.Current == expectedCount.Value))
            { ReferenceCountUtil.SafeRelease(message); count.Signal(); }
            else
                context.FireChannelRead(message);
        }
        public override void ChannelInactive(ChannelHandlerContext context)
        { if (inactive) count.Signal(); context.FireChannelInactive(); }
    }
    private sealed class EchoThenClose(ByteBuf data, MessageCount count) : ChannelInboundHandlerAdapter
    {
        internal object Unreleased;
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            if (data.Equals(message))
            {
                Unreleased = message;
                count.Signal();
                _ = context.WriteAndFlushAsync(data.RetainedDuplicate());
                _ = context.CloseAsync();
            }
            else
                context.FireChannelRead(message);
        }
        public override void ChannelInactive(ChannelHandlerContext context) { count.Signal(); context.FireChannelInactive(); }
    }
    private sealed class ReadFrames(MessageCount count) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            if (message is not ByteBuf buffer)
            { context.FireChannelRead(message); return; }
            while (buffer.IsReadable)
            {
                int size = buffer.ReadInt();
                var slice = buffer.ReadRetainedSlice(size);
                try
                { if (slice.ReadInt() == count.Current) count.Signal(); }
                finally { slice.Release(); }
            }
            buffer.Release();
        }
    }
}
