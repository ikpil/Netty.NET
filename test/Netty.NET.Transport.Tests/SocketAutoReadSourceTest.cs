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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Explicit source read routes and independently owned cleanup.
#pragma warning disable CS0612 // Preserve the original custom receive allocator contract.
public class SocketAutoReadSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestAutoReadOffDuringReadOnlyReadsOneTime(bool adaptive, bool readOutsideEventLoopThread)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var serverHandler = new AutoReadHandler(!readOutsideEventLoopThread);
        var clientHandler = new AutoReadHandler(!readOutsideEventLoopThread);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await fixture.Server.Option(ChannelOption.SO_BACKLOG, 1024).Option(ChannelOption.AUTO_READ, true)
            .ChildOption(ChannelOption.AUTO_READ, true)
            // We want to ensure that we attempt multiple individual read operations per read loop so we can
            // test the auto read feature being turned off when data is first read.
            .ChildOption(ChannelOption.RECVBUF_ALLOCATOR, new ReadStateAllocator())
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => { channel.Pipeline.AddLast(serverHandler); accepted.SetResult(channel); }))
            .BindAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, true)
                // We want to ensure that we attempt multiple individual read operations per read loop so we can
                // test the auto read feature being turned off when data is first read.
                .Option(ChannelOption.RECVBUF_ALLOCATOR, new ReadStateAllocator()).Handler(clientHandler).ConnectAsync(server.LocalAddress);
            // 3 bytes means 3 independent reads for TestRecvByteBufAllocator
            await client.WriteAndFlushAsync(ReadStateAllocator.RandomBuffer(client.Allocator, 3));
            await serverHandler.AssertSingleRead();
            // 3 bytes means 3 independent reads for TestRecvByteBufAllocator
            IChannel child = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await child.WriteAndFlushAsync(ReadStateAllocator.RandomBuffer(child.Allocator, 3));
            await clientHandler.AssertSingleRead();
            if (readOutsideEventLoopThread) child.Read();
            await serverHandler.AssertSingleReadSecondTry();
            if (readOutsideEventLoopThread) client.Read();
            await clientHandler.AssertSingleReadSecondTry();
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class AutoReadHandler(bool callRead) : ChannelInboundHandlerAdapter
    {
        private int _count, _cycles;
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _last = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            ReferenceCountUtil.Release(message);
            if (Interlocked.Increment(ref _count) == 1) context.Channel.Config.SetAutoRead(false);
            if (callRead)
            {
                // Test calling read in the EventLoop thread to ensure a read is eventually done.
                context.Read();
            }
        }
        public override void ChannelReadComplete(ChannelHandlerContext context)
        { _first.TrySetResult(); if (++_cycles == (callRead ? 3 : 2)) _last.TrySetResult(); }
        internal async Task AssertSingleRead()
        { await _first.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(Volatile.Read(ref _count) > 0); }
        internal async Task AssertSingleReadSecondTry()
        { await _last.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(callRead ? 3 : 2, Volatile.Read(ref _count)); }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore CS0612
