/*
 * Copyright 2023 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over AbstractQuicTest.NewSslTaskExecutors().
// A ChannelFutureListener is a continuation of the write Task (Task.Exception is its cause), shutdownInput().sync() is
// await ShutdownInputAsync(); CountDownLatch is CountdownEvent, counted down at most to zero as Java's latch is
// (CountDown); AtomicReference/AtomicLong are Interlocked fields; fail(message, cause) is Assert.Fail with the cause's
// text. The decoder's anonymous ByteToMessageDecoder is the private FourBytesDecoder.
public class QuicStreamShutdownTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestShutdownInputClosureCausesStreamStopped(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = null;
        IChannel channel = null;
        CountdownEvent latch = new CountdownEvent(2);

        try
        {
            server = await QuicTestUtils.NewServer(executor, new ChannelInboundHandlerAdapter(),
                    new EchoTwiceHandler(latch));
            channel = await QuicTestUtils.NewClient(executor);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(new ChannelInboundHandlerAdapter())
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            IQuicStreamChannel streamChannel = await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                    new ChannelInboundHandlerAdapter(), cancellationToken);
            await streamChannel.ShutdownInputAsync();
            Assert.True(streamChannel.IsInputShutdown);
            await streamChannel.WriteAndFlushAsync(Unpooled.Buffer().WriteLong(8), cancellationToken);

            Assert.True(latch.Wait(TIMEOUT, cancellationToken));
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);

            Shutdown(executor);
        }
    }

    // The anonymous stream handler of testShutdownInputClosureCausesStreamStopped.
    private sealed class EchoTwiceHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;

        internal EchoTwiceHandler(CountdownEvent latch)
        {
            this.latch = latch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Action<Task> futureListener = channelFuture =>
            {
                Exception cause = channelFuture.Exception?.InnerException;
                if (cause is ChannelOutputShutdownException)
                {
                    CountDown(latch);
                }
            };
            ByteBuf buffer = (ByteBuf) msg;
            _ = ctx.WriteAsync(buffer.RetainedDuplicate()).ContinueWith(futureListener, TaskScheduler.Default);
            _ = ctx.WriteAndFlushAsync(buffer).ContinueWith(futureListener, TaskScheduler.Default);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestShutdownInputClosureByServerCausesStreamStopped(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = null;
        IChannel channel = null;
        CountdownEvent latch = new CountdownEvent(2);
        StrongBox<Exception> errorRef = new StrongBox<Exception>();

        try
        {
            server = await QuicTestUtils.NewServer(executor, new ChannelInboundHandlerAdapter(),
                    new ShutdownInputThenEchoHandler(latch, errorRef));

            channel = await QuicTestUtils.NewClient(executor);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(new ChannelInboundHandlerAdapter())
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            IQuicStreamChannel streamChannel = await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                    new FourBytesDecoder(latch), cancellationToken);

            await streamChannel.WriteAndFlushAsync(Unpooled.Buffer().WriteInt(4), cancellationToken);

            if (!latch.Wait(TimeSpan.FromSeconds(5), cancellationToken))
            {
                Assert.Fail("Timeout while waiting for completion: " + Volatile.Read(ref errorRef.Value));
            }

            Exception error = Volatile.Read(ref errorRef.Value);
            if (error != null)
            {
                Assert.Fail("Failure during execution: " + error);
            }
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);

            Shutdown(executor);
        }
    }

    // The anonymous stream handler of testShutdownInputClosureByServerCausesStreamStopped.
    private sealed class ShutdownInputThenEchoHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;
        private readonly StrongBox<Exception> errorRef;

        internal ShutdownInputThenEchoHandler(CountdownEvent latch, StrongBox<Exception> errorRef)
        {
            this.latch = latch;
            this.errorRef = errorRef;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            IQuicStreamChannel streamChannel = (IQuicStreamChannel) ctx.Channel;
            _ = streamChannel.ShutdownInputAsync()
                    .ContinueWith(f =>
                    {
                        ByteBuf buffer = (ByteBuf) msg;

                        if (!f.IsCompletedSuccessfully)
                        {
                            Interlocked.CompareExchange(ref errorRef.Value, f.Exception?.InnerException, null);
                            CountDown(latch);
                            buffer.Release();
                        }
                        else
                        {
                            _ = ctx.WriteAndFlushAsync(buffer).ContinueWith(channelFuture =>
                            {
                                if (!channelFuture.IsCompletedSuccessfully)
                                {
                                    Interlocked.CompareExchange(ref errorRef.Value,
                                            channelFuture.Exception?.InnerException, null);
                                }

                                CountDown(latch);
                            }, TaskScheduler.Default);
                        }
                    }, TaskScheduler.Default);
        }
    }

    // The anonymous ByteToMessageDecoder of testShutdownInputClosureByServerCausesStreamStopped.
    private sealed class FourBytesDecoder : ByteToMessageDecoder
    {
        private readonly CountdownEvent latch;

        internal FourBytesDecoder(CountdownEvent latch)
        {
            this.latch = latch;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> list)
        {
            if (@in.ReadableBytes == 4)
            {
                CountDown(latch);
                ctx.CloseAsync();
            }
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestShutdownOutputClosureByServerCausesStreamReset(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = null;
        IChannel channel = null;
        CountdownEvent latch = new CountdownEvent(2);
        StrongBox<Exception> errorRef = new StrongBox<Exception>();
        StrongBox<long> applicationErrorCode = new StrongBox<long>(-1);

        try
        {
            server = await QuicTestUtils.NewServer(executor, new ChannelInboundHandlerAdapter(),
                    new ShutdownOutputHandler(latch, errorRef));

            channel = await QuicTestUtils.NewClient(executor);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(new ChannelInboundHandlerAdapter())
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            IQuicStreamChannel streamChannel = await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                    new ResetRecordingHandler(latch, errorRef, applicationErrorCode), cancellationToken);

            await streamChannel.WriteAndFlushAsync(Unpooled.Buffer().WriteInt(4), cancellationToken);

            if (!latch.Wait(TimeSpan.FromSeconds(5), cancellationToken))
            {
                Assert.Fail("Timeout while waiting for completion: " + Volatile.Read(ref errorRef.Value));
            }

            Exception error = Volatile.Read(ref errorRef.Value);
            if (error != null)
            {
                Assert.Fail("Failure during execution: " + error);
            }

            Assert.Equal(100, Interlocked.Read(ref applicationErrorCode.Value));
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);

            Shutdown(executor);
        }
    }

    // The anonymous stream handler of testShutdownOutputClosureByServerCausesStreamReset (server side).
    private sealed class ShutdownOutputHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;
        private readonly StrongBox<Exception> errorRef;

        internal ShutdownOutputHandler(CountdownEvent latch, StrongBox<Exception> errorRef)
        {
            this.latch = latch;
            this.errorRef = errorRef;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            IQuicStreamChannel streamChannel = (IQuicStreamChannel) ctx.Channel;
            ByteBuf buffer = (ByteBuf) msg;
            buffer.Release();
            _ = streamChannel.ShutdownOutputAsync(100)
                    .ContinueWith(f =>
                    {
                        if (!f.IsCompletedSuccessfully)
                        {
                            Interlocked.CompareExchange(ref errorRef.Value, f.Exception?.InnerException, null);
                        }

                        CountDown(latch);
                    }, TaskScheduler.Default);
        }
    }

    // The anonymous stream handler of testShutdownOutputClosureByServerCausesStreamReset (client side).
    private sealed class ResetRecordingHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;
        private readonly StrongBox<Exception> errorRef;
        private readonly StrongBox<long> applicationErrorCode;

        internal ResetRecordingHandler(CountdownEvent latch, StrongBox<Exception> errorRef,
                                       StrongBox<long> applicationErrorCode)
        {
            this.latch = latch;
            this.errorRef = errorRef;
            this.applicationErrorCode = applicationErrorCode;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause is QuicStreamResetException reset)
            {
                Interlocked.Exchange(ref applicationErrorCode.Value, reset.ApplicationProtocolCode());
            }
            else
            {
                Volatile.Write(ref errorRef.Value, cause);
            }

            CountDown(latch);
        }
    }

    // CLR-only helper: CountDownLatch.countDown() does nothing once the count is zero, where CountdownEvent.Signal throws.
    private static void CountDown(CountdownEvent latch)
    {
        lock (latch)
        {
            if (latch.CurrentCount > 0)
            {
                latch.Signal();
            }
        }
    }

    // CLR-only helper: a mutable box shared with the handlers (AtomicReference/AtomicLong).
    private sealed class StrongBox<T>
    {
        internal T Value;

        internal StrongBox()
        {
        }

        internal StrongBox(T value)
        {
            Value = value;
        }
    }
}
