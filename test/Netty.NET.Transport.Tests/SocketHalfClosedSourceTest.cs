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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

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
public class SocketHalfClosedSourceTest : AbstractSocketTest
{
    // CLR: the first half of SocketHalfClosedTest, through AutoCloseFalseLeader.
#pragma warning disable xUnit1069 // The Runner bodies observe TestContext.Current.CancellationToken in their waits.
#pragma warning disable xUnit1051 // Channel operations and closes run independently of the test cancellation, as in the source.
    protected virtual int MaxReadCompleteWithNoDataAfterInputShutdown()
    {
        return 2; // nio needs read flag to detect full closure.
    }

    [Fact(Timeout = 20000)]
    public async Task TestAllDataReadEventTriggeredAfterHalfClosure()
    {
        await Run(TestContext.Current, async (serverBootstrap, bootstrap) =>
        {
            // CLR: the source first returns for an OioEventLoopGroup ("Ignoring test for incompatible OIO event
            // system"); OioEventLoopGroup is deprecated-not-ported, so no harness group takes that branch.
            if (bootstrap.Config.Group is IIoEventLoopGroup)
            {
                IIoEventLoopGroup group = (IIoEventLoopGroup) bootstrap.Config.Group;
                if (group.IsIoType(typeof(NioIoHandler)))
                {
                    logger.Debug("Ignoring test for incompatible NioHandler");
                    return;
                }
            }

            await AllDataReadEventTriggeredAfterHalfClosure(serverBootstrap, bootstrap);
        });
    }

    private static async Task AllDataReadEventTriggeredAfterHalfClosure(ServerBootstrap sb, Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        const int totalServerBytesWritten = 1;
        CountDownLatch clientReadAllDataLatch = new CountDownLatch(1);
        CountDownLatch clientHalfClosedLatch = new CountDownLatch(1);
        CountDownLatch clientHalfClosedAllBytesRead = new CountDownLatch(1);
        StrongBox<int> clientReadCompletes = new StrongBox<int>();
        StrongBox<int> clientZeroDataReadCompletes = new StrongBox<int>();
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        StrongBox<IChannel> serverChildChannel = new StrongBox<IChannel>();

        try
        {
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true)
                    .Option(ChannelOption.AUTO_CLOSE, false)
                    .Option(ChannelOption.AUTO_READ, false);

            sb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true)
                    .Option(ChannelOption.AUTO_CLOSE, false)
                    .ChildOption(ChannelOption.TCP_NODELAY, true);

            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                Volatile.Write(ref serverChildChannel.Value, ch);
                ch.Pipeline.AddLast(new AllDataReadEventTriggeredServerHandler(totalServerBytesWritten));
            }));

            // client.
            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new AllDataReadEventTriggeredClientHandler(ch, totalServerBytesWritten,
                        clientReadAllDataLatch, clientHalfClosedLatch, clientHalfClosedAllBytesRead,
                        clientReadCompletes, clientZeroDataReadCompletes));
                ch.Read();
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            clientChannel.Read();

            await clientReadAllDataLatch.AwaitAsync(cancellation);

            // Now we need to trigger server half-close
            _ = ((IDuplexChannel) Volatile.Read(ref serverChildChannel.Value)).ShutdownOutputAsync();

            await clientHalfClosedLatch.AwaitAsync(cancellation);
            await clientHalfClosedAllBytesRead.AwaitAsync(cancellation);
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

    private sealed class AllDataReadEventTriggeredServerHandler(int totalServerBytesWritten)
            : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ByteBuf buf = ctx.Alloc().Buffer(totalServerBytesWritten);
            buf.WriterIndex = buf.Capacity;
            _ = ctx.WriteAndFlushAsync(buf);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    private sealed class AllDataReadEventTriggeredClientHandler(
            IChannel ch, int totalServerBytesWritten, CountDownLatch clientReadAllDataLatch,
            CountDownLatch clientHalfClosedLatch, CountDownLatch clientHalfClosedAllBytesRead,
            StrongBox<int> clientReadCompletes, StrongBox<int> clientZeroDataReadCompletes)
            : ChannelInboundHandlerAdapter
    {
        private int bytesRead;
        private int bytesSinceReadComplete;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            bytesRead += buf.ReadableBytes;
            bytesSinceReadComplete += buf.ReadableBytes;
            buf.Release();
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownEvent.INSTANCE)
            {
                clientHalfClosedLatch.CountDown();
            }
            else if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                clientHalfClosedAllBytesRead.CountDown();
                _ = ctx.CloseAsync();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (bytesSinceReadComplete == 0)
            {
                Interlocked.Increment(ref clientZeroDataReadCompletes.Value);
            }
            else
            {
                bytesSinceReadComplete = 0;
            }

            Interlocked.Increment(ref clientReadCompletes.Value);

            if (bytesRead == totalServerBytesWritten)
            {
                // Bounce this through the event loop to make sure it happens after we're done
                // with the read operation.
                ch.EventLoop.Execute(() =>
                {
                    clientReadAllDataLatch.CountDown();
                });
            }
            else
            {
                ctx.Read();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            ctx.FireExceptionCaught(cause);
            _ = ctx.CloseAsync();
        }
    }

    [Fact(Timeout = 20000)]
    public async Task TestHalfClosureReceiveDataOnFinalWait2StateWhenSoLingerSet()
    {
        await Run(TestContext.Current, TestHalfClosureReceiveDataOnFinalWait2StateWhenSoLingerSet0);
    }

    // CLR: xUnit1024 forbids overloading the TestHalfClosureReceiveDataOnFinalWait2StateWhenSoLingerSet test name.
    private static async Task TestHalfClosureReceiveDataOnFinalWait2StateWhenSoLingerSet0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        CountDownLatch waitHalfClosureDone = new CountDownLatch(1);

        try
        {
            sb.ChildOption(ChannelOption.SO_LINGER, 1)
              .ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
              {
                  ch.Pipeline.AddLast(new FinalWait2ServerHandler(waitHalfClosureDone));
              }));

            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true)
              .Handler(new ActionChannelInitializer<IChannel>(ch =>
              {
                  ch.Pipeline.AddLast(new FinalWait2ClientHandler());
              }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            await waitHalfClosureDone.AwaitAsync(TestContext.Current.CancellationToken);
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

    private sealed class FinalWait2ServerHandler(CountDownLatch waitHalfClosureDone) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ISocketChannel channel = (ISocketChannel) ctx.Channel;
            _ = channel.ShutdownOutputAsync();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            waitHalfClosureDone.CountDown();
        }
    }

    private sealed class FinalWait2ClientHandler : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (ChannelInputShutdownEvent.INSTANCE == evt)
            {
                _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer().WriteZero(16));
            }

            if (ChannelInputShutdownReadComplete.INSTANCE == evt)
            {
                _ = ctx.CloseAsync();
            }
        }
    }

    [Fact(Timeout = 20000)]
    public async Task TestHalfClosureOnlyOneEventWhenAutoRead()
    {
        await Run(TestContext.Current, TestHalfClosureOnlyOneEventWhenAutoRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestHalfClosureOnlyOneEventWhenAutoRead test name.
    private static async Task TestHalfClosureOnlyOneEventWhenAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;

        try
        {
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true)
                    .Option(ChannelOption.AUTO_READ, true);
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new HalfClosureOnlyOneEventServerHandler());
            }));

            StrongBox<int> shutdownEventReceivedCounter = new StrongBox<int>();
            StrongBox<int> shutdownReadCompleteEventReceivedCounter = new StrongBox<int>();

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new HalfClosureOnlyOneEventClientHandler(
                        shutdownEventReceivedCounter, shutdownReadCompleteEventReceivedCounter));
            }));

            serverChannel = await sb.BindAsync();
            IChannel clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            await clientChannel.CloseFuture.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, Volatile.Read(ref shutdownEventReceivedCounter.Value));
            Assert.Equal(1, Volatile.Read(ref shutdownReadCompleteEventReceivedCounter.Value));
        }
        finally
        {
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
        }
    }

    private sealed class HalfClosureOnlyOneEventServerHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // CLR: as in the source, the context itself rather than ctx.Channel is cast. The cast fails with
            // InvalidCastException, "thrown for invalid casting or explicit conversion"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.invalidcastexception), where the JVM throws
            // ClassCastException; the pipeline passes it to exceptionCaught, which closes the accepted channel.
            _ = ((IDuplexChannel) ctx).ShutdownOutputAsync();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    private sealed class HalfClosureOnlyOneEventClientHandler(
            StrongBox<int> shutdownEventReceivedCounter, StrongBox<int> shutdownReadCompleteEventReceivedCounter)
            : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownEvent.INSTANCE)
            {
                Interlocked.Increment(ref shutdownEventReceivedCounter.Value);
            }
            else if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                Interlocked.Increment(ref shutdownReadCompleteEventReceivedCounter.Value);
                _ = ctx.Executor.ScheduleAsync(() =>
                {
                    _ = ctx.CloseAsync();
                }, TimeSpan.FromMilliseconds(100));
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    [Fact(Timeout = 20000)]
    public async Task TestAllDataReadAfterHalfClosure()
    {
        await Run(TestContext.Current, TestAllDataReadAfterHalfClosure0);
    }

    // CLR: xUnit1024 forbids overloading the TestAllDataReadAfterHalfClosure test name; both source overloads keep the
    // 0 suffix.
    private async Task TestAllDataReadAfterHalfClosure0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestAllDataReadAfterHalfClosure0(true, sb, cb);
        await TestAllDataReadAfterHalfClosure0(false, sb, cb);
    }

    private async Task TestAllDataReadAfterHalfClosure0(bool autoRead,
                                                 ServerBootstrap sb, Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        const int totalServerBytesWritten = 1024 * 16;
        const int numReadsPerReadLoop = 2;
        CountDownLatch serverInitializedLatch = new CountDownLatch(1);
        CountDownLatch clientReadAllDataLatch = new CountDownLatch(1);
        CountDownLatch clientHalfClosedLatch = new CountDownLatch(1);
        StrongBox<int> clientReadCompletes = new StrongBox<int>();
        StrongBox<int> clientZeroDataReadCompletes = new StrongBox<int>();
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true)
              .Option(ChannelOption.AUTO_READ, autoRead)
              .Option(ChannelOption.RECVBUF_ALLOCATOR, new TestNumReadsRecvByteBufAllocator(numReadsPerReadLoop));

            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new AllDataReadAfterHalfClosureServerHandler(
                        totalServerBytesWritten, serverInitializedLatch));
            }));

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new AllDataReadAfterHalfClosureClientHandler(autoRead, totalServerBytesWritten,
                        clientReadAllDataLatch, clientHalfClosedLatch, clientReadCompletes,
                        clientZeroDataReadCompletes));
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            clientChannel.Read();

            await serverInitializedLatch.AwaitAsync(cancellation);
            await clientReadAllDataLatch.AwaitAsync(cancellation);
            await clientHalfClosedLatch.AwaitAsync(cancellation);
            // In practice this should be much less, as we allow numReadsPerReadLoop per wakeup, but we limit the
            // number of bytes to 1 per read so in theory we may need more. We check below that readComplete is called
            // when data is actually read.
            Assert.True(totalServerBytesWritten > Volatile.Read(ref clientReadCompletes.Value),
                    "too many read complete events: " + Volatile.Read(ref clientReadCompletes.Value));
            Assert.True(Volatile.Read(ref clientZeroDataReadCompletes.Value) <=
                            MaxReadCompleteWithNoDataAfterInputShutdown(),
                    "too many readComplete with no data: " + Volatile.Read(ref clientZeroDataReadCompletes.Value) +
                            " readComplete: " + Volatile.Read(ref clientReadCompletes.Value));
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

    private sealed class AllDataReadAfterHalfClosureServerHandler(
            int totalServerBytesWritten, CountDownLatch serverInitializedLatch) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ByteBuf buf = ctx.Alloc().Buffer(totalServerBytesWritten);
            buf.WriterIndex = buf.Capacity;
            // CLR: future.channel() of the write is ctx.Channel.
            _ = ctx.WriteAndFlushAsync(buf).ContinueWith(future =>
                    ((IDuplexChannel) ctx.Channel).ShutdownOutputAsync(), TaskScheduler.Default);

            serverInitializedLatch.CountDown();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    private sealed class AllDataReadAfterHalfClosureClientHandler(
            bool autoRead, int totalServerBytesWritten, CountDownLatch clientReadAllDataLatch,
            CountDownLatch clientHalfClosedLatch, StrongBox<int> clientReadCompletes,
            StrongBox<int> clientZeroDataReadCompletes) : ChannelInboundHandlerAdapter
    {
        private int bytesRead;
        private int bytesSinceReadComplete;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            bytesRead += buf.ReadableBytes;
            bytesSinceReadComplete += buf.ReadableBytes;
            buf.Release();
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownEvent.INSTANCE)
            {
                clientHalfClosedLatch.CountDown();
            }
            else if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                _ = ctx.CloseAsync();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (bytesSinceReadComplete == 0)
            {
                Interlocked.Increment(ref clientZeroDataReadCompletes.Value);
            }
            else
            {
                bytesSinceReadComplete = 0;
            }

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
    public async Task TestAutoCloseFalseDoesShutdownOutput()
    {
        // This test only works on Linux / BSD / MacOS as we assume some semantics that are not true for Windows.
        // CLR: assumeFalse(condition) aborts the test as skipped when the condition holds; xUnit v3 Assert.SkipWhen
        // "skips when it is true" (https://xunit.net/docs/getting-started/v3/whats-new).
        Assert.SkipWhen(PlatformDependent.IsWindows(), "assumeFalse(PlatformDependent.isWindows())");
        await Run(TestContext.Current, TestAutoCloseFalseDoesShutdownOutput0);
    }

    // CLR: xUnit1024 forbids overloading the TestAutoCloseFalseDoesShutdownOutput test name; both source overloads
    // keep the 0 suffix.
    private async Task TestAutoCloseFalseDoesShutdownOutput0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestAutoCloseFalseDoesShutdownOutput0(false, false, sb, cb);
        await TestAutoCloseFalseDoesShutdownOutput0(false, true, sb, cb);
        await TestAutoCloseFalseDoesShutdownOutput0(true, false, sb, cb);
        await TestAutoCloseFalseDoesShutdownOutput0(true, true, sb, cb);
    }

    private static async Task TestAutoCloseFalseDoesShutdownOutput0(bool allowHalfClosed,
                                                             bool clientIsLeader,
                                                             ServerBootstrap sb,
                                                             Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        const int expectedBytes = 100;
        CountDownLatch serverReadExpectedLatch = new CountDownLatch(1);
        CountDownLatch doneLatch = new CountDownLatch(2);
        StrongBox<Exception> causeRef = new StrongBox<Exception>();
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, allowHalfClosed)
                    .Option(ChannelOption.AUTO_CLOSE, false)
                    .Option(ChannelOption.SO_LINGER, 0);
            sb.ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, allowHalfClosed)
                    .ChildOption(ChannelOption.AUTO_CLOSE, false)
                    .ChildOption(ChannelOption.SO_LINGER, 0);

            AutoCloseFalseLeader leaderHandler = new AutoCloseFalseLeader(expectedBytes,
                    serverReadExpectedLatch, doneLatch, causeRef, cancellation);
            AutoCloseFalseFollower followerHandler = new AutoCloseFalseFollower(expectedBytes,
                    serverReadExpectedLatch, doneLatch, causeRef);
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientIsLeader ? followerHandler : (IChannelHandler) leaderHandler);
            }));

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientIsLeader ? leaderHandler : (IChannelHandler) followerHandler);
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);

            await doneLatch.AwaitAsync(cancellation);
            Assert.Null(Volatile.Read(ref causeRef.Value));
            Assert.True(leaderHandler.seenOutputShutdown);
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

    private sealed class AutoCloseFalseFollower : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly int expectedBytes;
        private readonly CountDownLatch followerCloseLatch;
        private readonly CountDownLatch doneLatch;
        private readonly StrongBox<Exception> causeRef;
        private int bytesRead;

        internal AutoCloseFalseFollower(int expectedBytes, CountDownLatch followerCloseLatch, CountDownLatch doneLatch,
                               StrongBox<Exception> causeRef)
        {
            this.expectedBytes = expectedBytes;
            this.followerCloseLatch = followerCloseLatch;
            this.doneLatch = doneLatch;
            this.causeRef = causeRef;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            CheckPrematureClose();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
            CheckPrematureClose();
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            bytesRead += msg.ReadableBytes;
            if (bytesRead >= expectedBytes)
            {
                // We write a reply and immediately close our end of the socket.
                ByteBuf buf = ctx.Alloc().Buffer(expectedBytes);
                buf.WriterIndex = buf.WriterIndex + expectedBytes;
                _ = ctx.WriteAndFlushAsync(buf).ContinueWith(future =>
                        ctx.CloseAsync().ContinueWith(f =>
                        {
                            // This is a bit racy but there is no better way how to handle this in Java11.
                            // The problem is that on close() the underlying FD will not actually be closed directly
                            // but the close will be done after the Selector did process all events. Because of
                            // this we will need to give it a bit time to ensure the FD is actual closed before we
                            // count down the latch and try to write.
                            _ = ctx.Executor.ScheduleAsync(() =>
                            {
                                followerCloseLatch.CountDown();
                            }, TimeSpan.FromMilliseconds(200));
                        }, TaskScheduler.Default), TaskScheduler.Default);
            }
        }

        private void CheckPrematureClose()
        {
            if (bytesRead < expectedBytes)
            {
                // CLR: IllegalStateException is InvalidOperationException, "thrown when a method call is invalid for
                // the object's current state" (https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception).
                Volatile.Write(ref causeRef.Value, new InvalidOperationException("follower premature close"));
                doneLatch.CountDown();
            }
        }
    }

    private sealed class AutoCloseFalseLeader : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly int expectedBytes;
        private readonly CountDownLatch followerCloseLatch;
        private readonly CountDownLatch doneLatch;
        private readonly StrongBox<Exception> causeRef;
        private readonly CancellationToken cancellation;
        private int bytesRead;
        internal bool seenOutputShutdown;

        // CLR: the extra cancellation argument lets the blocking followerCloseLatch wait on the event loop end when
        // xUnit signals the Timeout, so a timed-out run does not keep a thread of the shared group blocked.
        internal AutoCloseFalseLeader(int expectedBytes, CountDownLatch followerCloseLatch, CountDownLatch doneLatch,
                             StrongBox<Exception> causeRef, CancellationToken cancellation)
        {
            this.expectedBytes = expectedBytes;
            this.followerCloseLatch = followerCloseLatch;
            this.doneLatch = doneLatch;
            this.causeRef = causeRef;
            this.cancellation = cancellation;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ByteBuf buf = ctx.Alloc().Buffer(expectedBytes);
            buf.WriterIndex = buf.WriterIndex + expectedBytes;
            _ = ctx.WriteAndFlushAsync(buf.RetainedDuplicate());

            // We wait here to ensure that we write before we have a chance to process the outbound
            // shutdown event.
            followerCloseLatch.Await(cancellation);

            // This write should fail, but we should still be allowed to read the peer's data
            _ = ctx.WriteAndFlushAsync(buf).ContinueWith(future =>
            {
                // CLR: future.cause() == null on a completed future is Task.IsCompletedSuccessfully.
                if (future.IsCompletedSuccessfully)
                {
                    Volatile.Write(ref causeRef.Value,
                            new InvalidOperationException("second write should have failed!"));
                    doneLatch.CountDown();
                }
            }, TaskScheduler.Default);
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            bytesRead += msg.ReadableBytes;
            if (bytesRead >= expectedBytes)
            {
                doneLatch.CountDown();
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is ChannelOutputShutdownEvent)
            {
                seenOutputShutdown = true;
                doneLatch.CountDown();
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            CheckPrematureClose();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
            CheckPrematureClose();
        }

        private void CheckPrematureClose()
        {
            if (bytesRead < expectedBytes || !seenOutputShutdown)
            {
                Volatile.Write(ref causeRef.Value, new InvalidOperationException("leader premature close"));
                doneLatch.CountDown();
            }
        }
    }
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069

#pragma warning disable xUnit1069 // The Runner bodies observe TestContext.Current.CancellationToken in their waits.
#pragma warning disable xUnit1051 // Channel operations and closes run independently of the test cancellation, as in the source.
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

        // CLR: UncheckedBooleanSupplier is Func<bool>.
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
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
}
