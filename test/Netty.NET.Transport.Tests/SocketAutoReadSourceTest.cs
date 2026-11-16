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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and latch waits do not observe the test cancellation.
public class SocketAutoReadSourceTest : AbstractSocketTest
{
    [Fact]
    public async Task TestAutoReadOffDuringReadOnlyReadsOneTime()
    {
        await Run(TestContext.Current, TestAutoReadOffDuringReadOnlyReadsOneTime0);
    }

    // CLR: xUnit1024 forbids overloading the TestAutoReadOffDuringReadOnlyReadsOneTime test name, so the source's
    // runner body and its private (readOutsideEventLoopThread, sb, cb) overload are both named
    // TestAutoReadOffDuringReadOnlyReadsOneTime0.
    private async Task TestAutoReadOffDuringReadOnlyReadsOneTime0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestAutoReadOffDuringReadOnlyReadsOneTime0(true, sb, cb);
        await TestAutoReadOffDuringReadOnlyReadsOneTime0(false, sb, cb);
    }

    private static async Task TestAutoReadOffDuringReadOnlyReadsOneTime0(bool readOutsideEventLoopThread,
                                                           ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            AutoReadInitializer serverInitializer = new AutoReadInitializer(!readOutsideEventLoopThread);
            AutoReadInitializer clientInitializer = new AutoReadInitializer(!readOutsideEventLoopThread);
            sb.Option(ChannelOption.SO_BACKLOG, 1024)
                    .Option(ChannelOption.AUTO_READ, true)
                    .ChildOption(ChannelOption.AUTO_READ, true)
                    // We want to ensure that we attempt multiple individual read operations per read loop so we can
                    // test the auto read feature being turned off when data is first read.
                    .ChildOption(ChannelOption.RECVBUF_ALLOCATOR, new TestRecvByteBufAllocator())
                    .ChildHandler(serverInitializer);

            serverChannel = await sb.BindAsync();

            cb.Option(ChannelOption.AUTO_READ, true)
                    // We want to ensure that we attempt multiple individual read operations per read loop so we can
                    // test the auto read feature being turned off when data is first read.
                    .Option(ChannelOption.RECVBUF_ALLOCATOR, new TestRecvByteBufAllocator())
                    .Handler(clientInitializer);

            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);

            // 3 bytes means 3 independent reads for TestRecvByteBufAllocator
            // CLR: the source does not wait for these writes; their returned Tasks are not observed either.
            _ = clientChannel.WriteAndFlushAsync(RandomBufferType(clientChannel.Alloc(), new byte[3], 0, 3));
            await serverInitializer.autoReadHandler.AssertSingleRead();

            // 3 bytes means 3 independent reads for TestRecvByteBufAllocator
            _ = serverInitializer.channel.WriteAndFlushAsync(
                    RandomBufferType(serverInitializer.channel.Alloc(), new byte[3], 0, 3));
            await clientInitializer.autoReadHandler.AssertSingleRead();

            if (readOutsideEventLoopThread)
            {
                serverInitializer.channel.Read();
            }
            await serverInitializer.autoReadHandler.AssertSingleReadSecondTry();

            if (readOutsideEventLoopThread)
            {
                clientChannel.Read();
            }
            await clientInitializer.autoReadHandler.AssertSingleReadSecondTry();
        }
        finally
        {
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
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

    private class AutoReadInitializer : ChannelInitializer<IChannel>
    {
        internal readonly AutoReadHandler autoReadHandler;
        internal volatile IChannel channel;

        internal AutoReadInitializer(bool readInEventLoop)
        {
            autoReadHandler = new AutoReadHandler(readInEventLoop);
        }

        protected override void InitChannel(IChannel ch)
        {
            channel = ch;
            ch.Pipeline.AddLast(autoReadHandler);
        }
    }

    private sealed class AutoReadHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger count = new AtomicInteger();
        private readonly TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AtomicInteger latch2Count;
        private readonly bool callRead;

        internal AutoReadHandler(bool callRead)
        {
            this.callRead = callRead;
            latch2Count = new AtomicInteger(callRead ? 3 : 2);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            if (count.IncrementAndGet() == 1)
            {
                ctx.Channel.Config.SetAutoRead(false);
            }
            if (callRead)
            {
                // Test calling read in the EventLoop thread to ensure a read is eventually done.
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

        internal async Task AssertSingleRead()
        {
            Assert.True(await Await(latch, TimeSpan.FromSeconds(5)));
            Assert.True(count.Get() > 0);
        }

        internal async Task AssertSingleReadSecondTry()
        {
            Assert.True(await Await(latch2, TimeSpan.FromSeconds(5)));
            Assert.Equal(callRead ? 3 : 2, count.Get());
        }
    }

    /**
     * Designed to keep reading as long as autoread is enabled.
     */
    private sealed class TestRecvByteBufAllocator : IRecvByteBufAllocator
    {
        // CLR: the source's covariant ExtendedHandle newHandle() returns IRecvByteBufAllocator.IHandle here, and its
        // anonymous ExtendedHandle is the nested ExtendedHandle class.
        public IRecvByteBufAllocator.IHandle NewHandle()
        {
            return new ExtendedHandle();
        }

        private sealed class ExtendedHandle : IRecvByteBufAllocator.IExtendedHandle
        {
            private IChannelConfig config;
            private int attemptedBytesRead;
            private int lastBytesRead;

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
                this.config = config;
            }

            public void IncMessagesRead(int numMessages)
            {
                // No need to track the number of messages read because it is not used.
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
                return config.IsAutoRead();
            }

            public bool ContinueReading(Func<bool> maybeMoreDataSupplier)
            {
                return config.IsAutoRead();
            }

            public void ReadComplete()
            {
                // Nothing needs to be done or adjusted after each read cycle is completed.
            }
        }
    }
}
#pragma warning restore xUnit1051
