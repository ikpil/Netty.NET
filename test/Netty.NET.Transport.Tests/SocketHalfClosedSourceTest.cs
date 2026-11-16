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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The Runner bodies observe TestContext.Current.CancellationToken in their waits.
#pragma warning disable xUnit1051 // Channel operations and closes run independently of the test cancellation, as in the source.
// CLR: the source's class-level @Timeout(value = 20000, unit = MILLISECONDS) is the Timeout of every [Fact]. The
// source's anonymous ChannelInitializer subclasses are ActionChannelInitializer delegates, and its anonymous handler
// classes are named nested classes whose constructors receive the captured locals: C# anonymous types derive from
// Object and "can't add methods, events, or custom operators"
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/anonymous-types).
// AtomicInteger/AtomicReference locals are StrongBox values ("Holds a reference to a value",
// https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.strongbox-1) updated through
// Interlocked/Volatile, and the source's ChannelFutureListener callbacks are Task.ContinueWith continuations, which run
// "when the target Task completes" whether it ran to completion, faulted or was canceled
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.continuewith).
public partial class SocketHalfClosedSourceTest : AbstractSocketTest
{
    [Fact(Timeout = 20000)]
    public async Task TestAllDataReadClosure()
    {
        await Run(TestContext.Current, TestAllDataReadClosure0);
    }

    // CLR: xUnit1024 forbids overloading the TestAllDataReadClosure test name; both source overloads keep the 0 suffix.
    private async Task TestAllDataReadClosure0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestAllDataReadClosure0(true, false, sb, cb);
        await TestAllDataReadClosure0(true, true, sb, cb);
        await TestAllDataReadClosure0(false, false, sb, cb);
        await TestAllDataReadClosure0(false, true, sb, cb);
    }

    private static async Task TestAllDataReadClosure0(bool autoRead, bool allowHalfClosed,
                                               ServerBootstrap sb, Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        const int totalServerBytesWritten = 1024 * 16;
        const int numReadsPerReadLoop = 2;
        CountDownLatch serverInitializedLatch = new CountDownLatch(1);
        CountDownLatch clientReadAllDataLatch = new CountDownLatch(1);
        CountDownLatch clientHalfClosedLatch = new CountDownLatch(1);
        StrongBox<int> clientReadCompletes = new StrongBox<int>();
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, allowHalfClosed)
                    .Option(ChannelOption.AUTO_READ, autoRead)
                    .Option(ChannelOption.RECVBUF_ALLOCATOR, new TestNumReadsRecvByteBufAllocator(numReadsPerReadLoop));

            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new AllDataReadClosureServerHandler(
                        totalServerBytesWritten, serverInitializedLatch));
            }));

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new AllDataReadClosureClientHandler(autoRead, allowHalfClosed,
                        totalServerBytesWritten, clientReadAllDataLatch, clientHalfClosedLatch, clientReadCompletes));
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            clientChannel.Read();

            await serverInitializedLatch.AwaitAsync(cancellation);
            await clientReadAllDataLatch.AwaitAsync(cancellation);
            await clientHalfClosedLatch.AwaitAsync(cancellation);
            Assert.True(totalServerBytesWritten / numReadsPerReadLoop + 10 > Volatile.Read(ref clientReadCompletes.Value),
                "too many read complete events: " + Volatile.Read(ref clientReadCompletes.Value));
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

    private sealed class AllDataReadClosureServerHandler(
            int totalServerBytesWritten, CountDownLatch serverInitializedLatch) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ByteBuf buf = ctx.Alloc().Buffer(totalServerBytesWritten);
            buf.WriterIndex = buf.Capacity;
            // CLR: ChannelFutureListener.CLOSE closes future.channel() when the write completes.
            _ = ctx.WriteAndFlushAsync(buf).ContinueWith(future => ctx.Channel.CloseAsync(), TaskScheduler.Default);
            serverInitializedLatch.CountDown();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    private sealed class AllDataReadClosureClientHandler(
            bool autoRead, bool allowHalfClosed, int totalServerBytesWritten, CountDownLatch clientReadAllDataLatch,
            CountDownLatch clientHalfClosedLatch, StrongBox<int> clientReadCompletes) : ChannelInboundHandlerAdapter
    {
        private int bytesRead;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            bytesRead += buf.ReadableBytes;
            buf.Release();
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownEvent.INSTANCE && allowHalfClosed)
            {
                clientHalfClosedLatch.CountDown();
            }
            else if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                _ = ctx.CloseAsync();
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            if (!allowHalfClosed)
            {
                clientHalfClosedLatch.CountDown();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            Interlocked.Increment(ref clientReadCompletes.Value);
            if (bytesRead == totalServerBytesWritten)
            {
                clientReadAllDataLatch.CountDown();
            }
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    [Fact(Timeout = 20000)]
    public async Task TestShutdownInputAllowsOutboundWrites()
    {
        await Run(TestContext.Current, async (serverBootstrap, bootstrap) =>
        {
            await TestShutdownInputAllowsOutboundWrites0(true, serverBootstrap, bootstrap);
            await TestShutdownInputAllowsOutboundWrites0(false, serverBootstrap, bootstrap);
        });
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownInputAllowsOutboundWrites test name.
    private static async Task TestShutdownInputAllowsOutboundWrites0(bool clientInitiates, ServerBootstrap sb,
                                                              Bootstrap cb)
    {
        byte[] expectedBytes = new byte[100];
        // CLR: Random.Shared, "a thread-safe Random instance that may be used concurrently from any thread"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared), replaces ThreadLocalRandom.current().
        Random.Shared.NextBytes(expectedBytes);
        CountDownLatch readDoneLatch = new CountDownLatch(1);

        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            ShutdownInputWriter shutdownInputWriter = new ShutdownInputWriter(expectedBytes);
            ShutdownInputReader shutdownInputReader =
                    new ShutdownInputReader(expectedBytes.Length, readDoneLatch);
            sb.ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true);
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true);
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientInitiates? shutdownInputReader : (IChannelHandler) shutdownInputWriter);
            }));
            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientInitiates? shutdownInputWriter : (IChannelHandler) shutdownInputReader);
            }));
            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            Assert.True(await readDoneLatch.AwaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                       "Timed out waiting for inbound data after input shutdown");
            Assert.Equal(expectedBytes, shutdownInputReader.ReceivedBytes());
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

    private sealed class ShutdownInputReader : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly CountDownLatch readDoneLatch;
        private readonly int expectedBytesCount;
        private readonly ByteBuf receivedMsgs;
        private byte[] actualBytes;

        internal ShutdownInputReader(int expectedBytesCount,
                            CountDownLatch readDoneLatch)
        {
            this.readDoneLatch = readDoneLatch;
            this.expectedBytesCount = expectedBytesCount;
            this.receivedMsgs = Unpooled.Buffer(expectedBytesCount);
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            receivedMsgs.WriteBytes(msg);
            if (receivedMsgs.ReadableBytes == expectedBytesCount)
            {
                actualBytes = ByteBufUtil.GetBytes(receivedMsgs);
                readDoneLatch.CountDown();
            }
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            receivedMsgs.Release();
        }

        internal byte[] ReceivedBytes()
        {
            return actualBytes;
        }
    }

    private sealed class ShutdownInputWriter : ChannelInboundHandlerAdapter
    {
        private readonly byte[] expectedBytes;

        internal ShutdownInputWriter(byte[] expectedBytes)
        {
            this.expectedBytes = expectedBytes;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ((IDuplexChannel) ctx.Channel).ShutdownInputAsync()
                                           .ContinueWith(f =>
                                           {
                                               _ = ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(expectedBytes));
                                           }, TaskScheduler.Default);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task TestShutdownOutputAllowsInboundReads()
    {
        await Run(TestContext.Current, async (serverBootstrap, bootstrap) =>
        {
            await TestShutdownOutputAllowsInboundReads0(true, serverBootstrap, bootstrap);
            await TestShutdownOutputAllowsInboundReads0(false, serverBootstrap, bootstrap);
        });
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownOutputAllowsInboundReads test name.
    private static async Task TestShutdownOutputAllowsInboundReads0(bool clientInitiates, ServerBootstrap sb,
                                                             Bootstrap cb)
    {
        byte[] expectedBytes = new byte[100];
        Random.Shared.NextBytes(expectedBytes);
        CountDownLatch readDoneLatch = new CountDownLatch(1);

        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            ShutdownOutputWriter shutdownOutputWriter = new ShutdownOutputWriter(expectedBytes);
            ShutdownOutputReader
                    shutdownOutputReader =
                    new ShutdownOutputReader(expectedBytes.Length, readDoneLatch);
            sb.ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true);
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true);
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientInitiates? shutdownOutputWriter : (IChannelHandler) shutdownOutputReader);
            }));
            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientInitiates? shutdownOutputReader : (IChannelHandler) shutdownOutputWriter);
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            Assert.True(await readDoneLatch.AwaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                       "Timed out waiting for inbound data after output shutdown");
            Assert.Equal(expectedBytes, shutdownOutputReader.ReceivedBytes());
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

    private sealed class ShutdownOutputWriter : ChannelInboundHandlerAdapter
    {
        private readonly byte[] expectedBytes;

        internal ShutdownOutputWriter(byte[] expectedBytes)
        {
            this.expectedBytes = expectedBytes;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownEvent.INSTANCE)
            {
                _ = ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(expectedBytes));
            }
            ctx.FireUserEventTriggered(evt);
        }
    }

    private sealed class ShutdownOutputReader : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly CountDownLatch readDoneLatch;
        private readonly int expectedBytesCount;
        private readonly ByteBuf receivedMsgs;
        private byte[] actualbytes;

        internal ShutdownOutputReader(int expectedBytesCount,
                             CountDownLatch readDoneLatch)
        {
            this.readDoneLatch = readDoneLatch;
            this.expectedBytesCount = expectedBytesCount;
            this.receivedMsgs = Unpooled.Buffer(expectedBytesCount);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ((IDuplexChannel) ctx.Channel).ShutdownOutputAsync();
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            receivedMsgs.WriteBytes(msg);
            if (receivedMsgs.ReadableBytes == expectedBytesCount)
            {
                this.actualbytes = ByteBufUtil.GetBytes(receivedMsgs);
                readDoneLatch.CountDown();
            }
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            receivedMsgs.Release();
        }

        internal byte[] ReceivedBytes()
        {
            return actualbytes;
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

        public IRecvByteBufAllocator.IHandle NewHandle()
        {
            return new ExtendedHandle(numReads);
        }

        // CLR: the source's anonymous ExtendedHandle is this nested class; UncheckedBooleanSupplier is Func<bool>.
        private sealed class ExtendedHandle(int numReads) : IRecvByteBufAllocator.IExtendedHandle
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
                return numMessagesRead < numReads;
            }

            public bool ContinueReading(Func<bool> maybeMoreDataSupplier)
            {
                return ContinueReading() && maybeMoreDataSupplier();
            }

            public void ReadComplete()
            {
                // Nothing needs to be done or adjusted after each read cycle is completed.
            }
        }
    }

    // CLR: java.util.concurrent.CountDownLatch. countDown() does nothing once the count is zero and await() waits
    // for zero, so the count is decremented with Interlocked.CompareExchange and completes a TaskCompletionSource,
    // "the producer side of a Task unbound to a delegate"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource), at zero. await()
    // awaits its Task, await(timeout, unit) maps the TimeoutException of Task.WaitAsync(TimeSpan, CancellationToken)
    // ("The timeout expired", https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync) to
    // false, and Await blocks where the source blocks an event-loop thread. Every wait also observes the test
    // cancellation, which xUnit signals at the Timeout.
    private sealed class CountDownLatch
    {
        private readonly TaskCompletionSource zero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        internal CountDownLatch(int count)
        {
            this.count = count;
        }

        internal void CountDown()
        {
            int current;
            do
            {
                current = Volatile.Read(ref count);
                if (current == 0)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref count, current - 1, current) != current);
            if (current == 1)
            {
                zero.TrySetResult();
            }
        }

        internal Task AwaitAsync(CancellationToken cancellationToken)
        {
            return zero.Task.WaitAsync(cancellationToken);
        }

        internal async Task<bool> AwaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                await zero.Task.WaitAsync(timeout, cancellationToken);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        internal void Await(CancellationToken cancellationToken)
        {
            zero.Task.Wait(cancellationToken);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
