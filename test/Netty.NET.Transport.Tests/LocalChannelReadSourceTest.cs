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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve original read/cancellation timing and finish channel cleanup independently.
#pragma warning disable CS0612 // Original tests intentionally exercise the deprecated MAX_MESSAGES_PER_READ option.
public partial class LocalChannelSourceTest
{
    [Fact]
    public async Task TestNotLeakBuffersWhenCloseByRemotePeer()
    {
        await using var groups = new Groups();
        var cb = new Bootstrap().Group(groups.Shared).Channel<LocalChannel>().Handler(new DropEchoClient());
        var sb = new ServerBootstrap().Group(groups.Shared).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new SplitEchoAndClose())));
        IChannel server = null;
        LocalChannel client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            // Connect to the server
            client = (LocalChannel)await cb.ConnectAsync(server.LocalAddress);
            // Close the channel
            await CloseChannel(client);
            Assert.True(client.IsInboundBufferEmpty);
            await CloseChannel(server);
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact(Timeout = 5000)]
    public async Task TestAutoReadDisabledSharedGroup()
    {
        await using var groups = new Groups();
        await CheckAutoReadDisabled(groups.Shared, groups.Shared, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task TestAutoReadDisabledDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckAutoReadDisabled(groups.First, groups.Second, TestContext.Current.CancellationToken);
    }

    private static async Task CheckAutoReadDisabled(IEventLoopGroup serverGroup, IEventLoopGroup clientGroup, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var counter = new EchoCounter(completed, 100);
        var cb = new Bootstrap().Group(serverGroup).Channel<LocalChannel>()
            .Option(ChannelOption.AUTO_READ, false).Handler(new ManualEchoClient());
        var sb = new ServerBootstrap().Group(clientGroup).Channel<LocalServerChannel>()
            .ChildOption(ChannelOption.AUTO_READ, false)
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new ManualEchoServer(counter))));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            client = await cb.ConnectAsync(TestAddress);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.Shared, groups.Shared, true, TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithoutAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.Shared, groups.Shared, false, TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.First, groups.Second, true, TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 5000)]
    public async Task TestMaxMessagesPerReadRespectedWithoutAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckMaxMessagesPerRead(groups.First, groups.Second, false, TestContext.Current.CancellationToken);
    }

    private static async Task CheckMaxMessagesPerRead(IEventLoopGroup serverGroup, IEventLoopGroup clientGroup, bool autoRead, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cb = new Bootstrap().Group(serverGroup).Channel<LocalChannel>()
            .Option(ChannelOption.AUTO_READ, autoRead).Option(ChannelOption.MAX_MESSAGES_PER_READ, 1)
            .Handler(new ChannelReadHandler(completed, autoRead));
        var sb = new ServerBootstrap().Group(clientGroup).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new WriteTenMessages())));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            client = await cb.ConnectAsync(TestAddress);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally { await CloseChannel(client); await CloseChannel(server); }
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.Shared, groups.Shared, true, TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithoutAutoReadSharedGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.Shared, groups.Shared, false, TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.First, groups.Second, true, TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithoutAutoReadDifferentGroup()
    {
        await using var groups = new Groups();
        await CheckServerMaxMessagesPerRead(groups.First, groups.Second, false, TestContext.Current.CancellationToken);
    }

    private static async Task CheckServerMaxMessagesPerRead(IEventLoopGroup serverGroup, IEventLoopGroup clientGroup, bool autoRead, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cb = new Bootstrap().Group(serverGroup).Channel<LocalChannel>().Handler(new ActionChannelInitializer<IChannel>(_ => { /* NOOP */ }));
        var sb = new ServerBootstrap().Group(clientGroup).Channel<LocalServerChannel>()
            .Option(ChannelOption.AUTO_READ, autoRead).Option(ChannelOption.MAX_MESSAGES_PER_READ, 1)
            .Handler(new ChannelReadHandler(completed, autoRead)).ChildHandler(new ActionChannelInitializer<IChannel>(_ => { /* NOOP */ }));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            for (int i = 0; i < 5; i++)
            {
                try
                { client = await cb.ConnectAsync(TestAddress); }
                finally { await CloseChannel(client); }
            }
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally { await CloseChannel(server); }
    }

    [Fact(Timeout = 5000)]
    public async Task TestServerMaxMessagesPerReadRespectedWithQueuedConnections()
    {
        await using var groups = new Groups();
        const int maxMessagesPerRead = 2;
        const int connections = 5;
        int remaining = connections;
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCompletes = System.Threading.Channels.Channel.CreateUnbounded<int>();
        var connectFutures = new List<Task<IChannel>>();
        // CLR: Task<IChannel> exposes the channel on completion, so capture allocated clients for failure cleanup.
        var clients = new ConcurrentQueue<LocalChannel>();
        var cb = new Bootstrap().Group(groups.Shared).ChannelFactory(() =>
        {
            var client = new LocalChannel();
            clients.Enqueue(client);
            return client;
        }).Handler(new ActionChannelInitializer<IChannel>(_ => { /* NOOP */ }));
        var sb = new ServerBootstrap().Group(groups.Shared).ChannelFactory(() => new QueuedLocalServer(() =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                queued.TrySetResult();
        })).Option(ChannelOption.AUTO_READ, false).Option(ChannelOption.MAX_MESSAGES_PER_READ, maxMessagesPerRead)
            .Handler(new ReadCompleteCounts(readCompletes.Writer)).ChildHandler(new ActionChannelInitializer<IChannel>(_ => { /* NOOP */ }));
        IChannel server = null;
        try
        {
            server = await sb.BindAsync(TestAddress);
            for (int i = 0; i < connections; i++)
                connectFutures.Add(cb.ConnectAsync(TestAddress));
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            server.Config.SetAutoRead(true);
            Assert.Equal(maxMessagesPerRead, await readCompletes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(maxMessagesPerRead, await readCompletes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(connections - 2 * maxMessagesPerRead, await readCompletes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            await Task.WhenAll(connectFutures);
        }
        finally
        {
            foreach (var client in clients)
                await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    [Fact]
    public async Task TestReadCompleteCalledOnHandle()
    {
        await using var groups = new Groups();
        var serverCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cb = new Bootstrap().Group(groups.Shared).Channel<LocalChannel>().Handler(new ActionChannelInitializer<IChannel>(_ => { /* NOOP */ }));
        var sb = new ServerBootstrap().Group(groups.Shared).Channel<LocalServerChannel>()
            .Option(ChannelOption.RECVBUF_ALLOCATOR, new ReadCompleteRecvAllocator(serverCompleted))
            .ChildHandler(new ActionChannelInitializer<IChannel>(_ => { /* NOOP */ }))
            .ChildOption(ChannelOption.RECVBUF_ALLOCATOR, new ReadCompleteRecvAllocator(childCompleted));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await sb.BindAsync(TestAddress);
            try
            {
                client = await cb.ConnectAsync(TestAddress);
                await client.WriteAndFlushAsync("msg");
            }
            finally { await CloseChannel(client); }
            await Task.WhenAll(serverCompleted.Task, childCompleted.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await CloseChannel(server); }
    }

    private sealed class DropEchoClient : SimpleChannelInboundHandler<ByteBuf>
    {
        public override void ChannelActive(ChannelHandlerContext context) => _ = context.WriteAndFlushAsync(context.Allocator.Buffer().WriteZero(100));
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            // Just drop the buffer
        }
    }
    private sealed class SplitEchoAndClose : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            while (message.IsReadable)
            {
                // Fill the ChannelOutboundBuffer with multiple buffers
                _ = context.WriteAsync(message.ReadRetainedSlice(1));
            }
            // Flush and so transfer the written buffers to the inboundBuffer of the remote peer.
            // After this point the remote peer is responsible to release all the buffers.
            context.Flush();
            // This close call will trigger the remote peer close as well.
            _ = context.CloseAsync();
        }
    }
    private static void WriteAndFlushReadOnSuccess(ChannelHandlerContext context, object message)
    {
        var write = context.WriteAndFlushAsync(message);
        if (write.IsCompletedSuccessfully)
            context.Read();
        else
            _ = ReadAfterWriteAsync();
        async Task ReadAfterWriteAsync()
        {
            try
            { await write.ConfigureAwait(false); }
            catch { return; }
            // CLR: a Task continuation does not confer ownership of the original executor.
            await context.Executor.SubmitAsync(() => context.Read()).ConfigureAwait(false);
        }
    }
    private sealed class ManualEchoClient : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) => WriteAndFlushReadOnSuccess(context, "test");
        public override void ChannelRead(ChannelHandlerContext context, object message) => WriteAndFlushReadOnSuccess(context, message);
    }
    private sealed class EchoCounter(TaskCompletionSource completed, int remaining)
    {
        internal bool Signal()
        {
            if (--remaining > 0)
                return true;
            completed.TrySetResult();
            return false;
        }
    }
    private sealed class ManualEchoServer(EchoCounter counter) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) => context.Read();
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { if (counter.Signal()) WriteAndFlushReadOnSuccess(context, message); }
    }
    private sealed class WriteTenMessages : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context)
        {
            for (int i = 0; i < 10; i++)
                _ = context.WriteAsync(i);
            context.Flush();
        }
    }
    private sealed class ChannelReadHandler(TaskCompletionSource completed, bool autoRead) : ChannelInboundHandlerAdapter
    {
        private int _read;
        private int _remaining = 5;
        public override void ChannelActive(ChannelHandlerContext context)
        {
            if (!autoRead)
                context.Read();
            context.FireChannelActive();
        }
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            Assert.Equal(0, _read);
            _read++;
            context.FireChannelRead(message);
        }
        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            Assert.Equal(1, _read);
            if (_remaining > 0 && --_remaining == 0)
                completed.TrySetResult();
            if (_remaining > 0)
            {
                if (!autoRead)
                {
                    // The read will be scheduled 100ms in the future to ensure we not receive any
                    // channelRead calls in the meantime.
                    _ = context.Executor.ScheduleAsync(() => { _read = 0; context.Read(); }, TimeSpan.FromMilliseconds(100));
                }
                else
                    _read = 0;
            }
            else
                _read = 0;
            context.FireChannelReadComplete();
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error)
        {
            // CLR: surface original callback assertion failures to the awaiting test as well as the pipeline.
            completed.TrySetException(error);
            context.FireExceptionCaught(error);
            _ = context.CloseAsync();
        }
    }
    private sealed class QueuedLocalServer(Action queued) : LocalServerChannel
    {
        protected override LocalChannel NewLocalChannel(LocalChannel peer)
        { var child = base.NewLocalChannel(peer); queued(); return child; }
    }
    private sealed class ReadCompleteCounts(System.Threading.Channels.ChannelWriter<int> counts) : ChannelInboundHandlerAdapter
    {
        private int _messagesRead;
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { _messagesRead++; context.FireChannelRead(message); }
        public override void ChannelReadComplete(ChannelHandlerContext context)
        { counts.TryWrite(_messagesRead); _messagesRead = 0; context.FireChannelReadComplete(); }
    }
    private sealed class ReadCompleteRecvAllocator(TaskCompletionSource completed) : DefaultMaxMessagesRecvByteBufAllocator
    {
        public override IRecvByteBufAllocator.IHandle NewHandle() => new Handle(this, completed);
        private sealed class Handle(DefaultMaxMessagesRecvByteBufAllocator owner, TaskCompletionSource completed) : MaxMessageHandle(owner)
        {
            public override int Guess() => 128;
            public override void ReadComplete() { base.ReadComplete(); completed.TrySetResult(); }
        }
    }
}
