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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body; the source's latch windows stay fixed.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and latch waits do not observe the test cancellation.
public class SocketReadPendingSourceTest : AbstractSocketTest
{
    [Fact(Timeout = 60000)]
    public async Task TestReadPendingIsResetAfterEachRead()
    {
        await Run(TestContext.Current, TestReadPendingIsResetAfterEachRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestReadPendingIsResetAfterEachRead test name.
    private async Task TestReadPendingIsResetAfterEachRead0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            ReadPendingInitializer serverInitializer = new ReadPendingInitializer();
            ReadPendingInitializer clientInitializer = new ReadPendingInitializer();
            sb.Option(ChannelOption.SO_BACKLOG, 1024)
              .Option(ChannelOption.AUTO_READ, true)
              .ChildOption(ChannelOption.AUTO_READ, false)
              // We intend to do 2 reads per read loop wakeup
              .ChildOption(ChannelOption.RECVBUF_ALLOCATOR, new TestNumReadsRecvByteBufAllocator(2))
              .ChildHandler(serverInitializer);

            serverChannel = await sb.BindAsync();

            cb.Option(ChannelOption.AUTO_READ, false)
              // We intend to do 2 reads per read loop wakeup
              .Option(ChannelOption.RECVBUF_ALLOCATOR, new TestNumReadsRecvByteBufAllocator(2))
              .Handler(clientInitializer);
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);

            // 4 bytes means 2 read loops for TestNumReadsRecvByteBufAllocator
            // CLR: the source does not wait for this write; its returned Task is not observed either.
            _ = clientChannel.WriteAndFlushAsync(RandomBufferType(clientChannel.Alloc(), new byte[4], 0, 4));

            // 4 bytes means 2 read loops for TestNumReadsRecvByteBufAllocator
            Assert.True(await Await(serverInitializer.channelInitLatch, TimeSpan.FromSeconds(5)));
            _ = serverInitializer.channel.WriteAndFlushAsync(
                    RandomBufferType(serverInitializer.channel.Alloc(), new byte[4], 0 , 4));

            serverInitializer.channel.Read();
            await serverInitializer.readPendingHandler.AssertAllRead();

            clientChannel.Read();
            await clientInitializer.readPendingHandler.AssertAllRead();
        }
        finally
        {
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
        }
    }

    // CLR: a CountDownLatch is a TaskCompletionSource here. countDown() is TrySetResult(), which returns false
    // instead of failing once the Task is complete, as countDown() does nothing at a zero count
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetresult,
    // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/CountDownLatch.html); a latch
    // with a larger count keeps its remaining count in an AtomicInteger and completes the Task when it reaches zero.
    // await(timeout, unit) "Returns: true if the count reached zero and false if the waiting time elapsed", so the
    // latch Task races Task.Delay; Task.WhenAny's "Result is the task that completed first"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenany).
    private static async Task<bool> Await(TaskCompletionSource latch, TimeSpan timeout)
    {
        return await Task.WhenAny(latch.Task, Task.Delay(timeout)) == latch.Task;
    }

    private class ReadPendingInitializer : ChannelInitializer<IChannel>
    {
        internal readonly ReadPendingReadHandler readPendingHandler = new ReadPendingReadHandler();
        internal readonly TaskCompletionSource channelInitLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile IChannel channel;

        protected override void InitChannel(IChannel ch)
        {
            channel = ch;
            ch.Pipeline.AddLast(readPendingHandler);
            channelInitLatch.TrySetResult();
        }
    }

    private sealed class ReadPendingReadHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger count = new AtomicInteger();
        private readonly TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AtomicInteger latch2Count = new AtomicInteger(2);

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            if (count.IncrementAndGet() == 1)
            {
                // Call read the first time, to ensure it is not reset the second time.
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            latch.TrySetResult();
            if (latch2Count.DecrementAndGet() == 0)
            {
                latch2.TrySetResult();
            }
        }

        internal async Task AssertAllRead()
        {
            Assert.True(await Await(latch, TimeSpan.FromSeconds(5)));
            // We should only do 1 read loop, because we only called read() on the first channelRead.
            Assert.False(await Await(latch2, TimeSpan.FromSeconds(1)));
            Assert.Equal(2, count.Get());
        }
    }

    /**
     * Designed to read a single byte at a time to control the number of reads done at a fine granularity.
     */
    private sealed class TestNumReadsRecvByteBufAllocator : IRecvByteBufAllocator
    {
        private readonly int numReads;
        internal TestNumReadsRecvByteBufAllocator(int numReads)
        {
            this.numReads = numReads;
        }

        // CLR: the source's covariant ExtendedHandle newHandle() returns IRecvByteBufAllocator.IHandle here, and its
        // anonymous ExtendedHandle is the nested ExtendedHandle class, which reads numReads from its allocator.
        public IRecvByteBufAllocator.IHandle NewHandle()
        {
            return new ExtendedHandle(this);
        }

        private sealed class ExtendedHandle(TestNumReadsRecvByteBufAllocator allocator) : IRecvByteBufAllocator.IExtendedHandle
        {
            private int attemptedBytesRead;
            private int lastBytesRead;
            private int numMessagesRead;

            public ByteBuf Allocate(IByteBufAllocator alloc)
            {
                return alloc.IoBuffer(Guess(), Guess());
            }

            public int Guess()
            {
                return 1; // only ever allocate buffers of size 1 to ensure the number of reads is controlled.
            }

            public void Reset(IChannelConfig config)
            {
                numMessagesRead = 0;
            }

            public void IncMessagesRead(int numMessages)
            {
                numMessagesRead += numMessages;
            }

            public void LastBytesRead(int bytes)
            {
                lastBytesRead = bytes;
            }

            public int LastBytesRead()
            {
                return lastBytesRead;
            }

            public void AttemptedBytesRead(int bytes)
            {
                attemptedBytesRead = bytes;
            }

            public int AttemptedBytesRead()
            {
                return attemptedBytesRead;
            }

            public bool ContinueReading()
            {
                return numMessagesRead < allocator.numReads;
            }

            public bool ContinueReading(Func<bool> maybeMoreDataSupplier)
            {
                return ContinueReading();
            }

            public void ReadComplete()
            {
                // Nothing needs to be done or adjusted after each read cycle is completed.
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
