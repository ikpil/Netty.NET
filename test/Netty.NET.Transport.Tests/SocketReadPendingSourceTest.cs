/*
 * Copyright 2016 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Source read windows and independently owned cleanup.
#pragma warning disable CS0612 // Preserve the original custom receive allocator contract.
public class SocketReadPendingSourceTest
{
    [Theory(Timeout = 60000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestReadPendingIsResetAfterEachRead(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var serverHandler = new PendingReadHandler();
        var clientHandler = new PendingReadHandler();
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await fixture.Server.Option(ChannelOption.SO_BACKLOG, 1024).Option(ChannelOption.AUTO_READ, true)
            .ChildOption(ChannelOption.AUTO_READ, false)
            // We intend to do 2 reads per read loop wakeup
            .ChildOption(ChannelOption.RECVBUF_ALLOCATOR, new ReadStateAllocator(2))
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => { channel.Pipeline.AddLast(serverHandler); accepted.SetResult(channel); }))
            .BindAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, false)
                // We intend to do 2 reads per read loop wakeup
                .Option(ChannelOption.RECVBUF_ALLOCATOR, new ReadStateAllocator(2)).Handler(clientHandler).ConnectAsync(server.LocalAddress);
            // 4 bytes means 2 read loops for TestNumReadsRecvByteBufAllocator
            await client.WriteAndFlushAsync(ReadStateAllocator.RandomBuffer(client.Allocator, 4));
            // 4 bytes means 2 read loops for TestNumReadsRecvByteBufAllocator
            IChannel child = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await child.WriteAndFlushAsync(ReadStateAllocator.RandomBuffer(child.Allocator, 4));
            child.Read();
            await serverHandler.AssertAllRead();
            client.Read();
            await clientHandler.AssertAllRead();
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class PendingReadHandler : ChannelInboundHandlerAdapter
    {
        private int _count, _cycles;
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            ReferenceCountUtil.Release(message);
            if (Interlocked.Increment(ref _count) == 1)
            {
                // Call read the first time, to ensure it is not reset the second time.
                context.Read();
            }
        }
        public override void ChannelReadComplete(ChannelHandlerContext context)
        { _first.TrySetResult(); if (++_cycles == 2) _second.TrySetResult(); }
        internal async Task AssertAllRead()
        {
            await _first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // We should only do 1 read loop, because we only called read() on the first channelRead.
            Assert.NotSame(_second.Task, await Task.WhenAny(_second.Task, Task.Delay(1000)));
            Assert.Equal(2, Volatile.Read(ref _count));
        }
    }
}

/**
 * Designed to read a single byte at a time to control the number of reads done at a fine granularity.
 */
/**
 * Designed to keep reading as long as autoread is enabled.
 */
internal sealed class ReadStateAllocator(int? numReads = null) : IRecvByteBufAllocator
{
    public IRecvByteBufAllocator.IHandle NewHandle() => new Handle(numReads);
    internal static ByteBuf RandomBuffer(IByteBufAllocator allocator, int length) =>
        (Random.Shared.Next(2) == 0 ? allocator.DirectBuffer(length) : allocator.HeapBuffer(length)).WriteZero(length);
    private sealed class Handle(int? numReads) : IRecvByteBufAllocator.IExtendedHandle
    {
        private int _attempted, _last, _messages;
        private IChannelConfig _config;
        public ByteBuf Allocate(IByteBufAllocator allocator) => allocator.IoBuffer(Guess(), Guess());
        public int Guess() => 1; // only ever allocate buffers of size 1 to ensure the number of reads is controlled.
        public void Reset(IChannelConfig config) { _config = config; _messages = 0; }
        public void IncMessagesRead(int count)
        {
            if (numReads.HasValue) _messages += count;
            // No need to track the number of messages read because it is not used.
            // CLR: the auto-read mode intentionally ignores message counts, as in TestRecvByteBufAllocator.
        }
        public void LastBytesRead(int bytes) => _last = bytes;
        public int LastBytesRead() => _last;
        public void AttemptedBytesRead(int bytes) => _attempted = bytes;
        public int AttemptedBytesRead() => _attempted;
        public bool ContinueReading() => numReads.HasValue ? _messages < numReads.Value : _config.IsAutoRead();
        public bool ContinueReading(Func<bool> maybeMoreDataSupplier) => ContinueReading();
        public void ReadComplete()
        {
            // Nothing needs to be done or adjusted after each read cycle is completed.
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore CS0612
