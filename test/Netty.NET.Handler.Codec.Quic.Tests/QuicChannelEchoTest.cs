/*
 * Copyright 2020 The Netty Project
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
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest(name = ...) @MethodSource("data") is a theory with the eight (autoRead, directBuffer,
// composite) rows of data(); PlatformDependent.splittableRandomNextBytes(new SplittableRandom(seed), data) fills the data
// with System.Random. The heap-only allocator is the private HeapOnlyByteBufAllocator (the anonymous
// AbstractByteBufAllocator subclass); "memoryAddress() is not supported" has no counterpart, as ByteBufs are managed
// memory; writeBytes(byte[], int, int) is WriteBytes(ReadOnlySpan<byte>). ChannelFuture.sync() is await, Thread.sleep(50) is Task.Delay, Future.addListener a continuation of the
// CreateStreamAsync Task (getNow() its result), AtomicReference a volatile/Interlocked field. Assertions inside the
// handlers record their failures through exceptionCaught, as in Java. java.io.IOException is System.IO.IOException.
public class QuicChannelEchoTest : AbstractQuicTest
{

    private static readonly Random random = new Random();
    internal static readonly byte[] data = new byte[1048576];

    static QuicChannelEchoTest()
    {
        new Random(random.Next()).NextBytes(data);
    }

    public static IEnumerable<object[]> Data()
    {
        List<object[]> config = new List<object[]>();
        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                for (int c = 0; c < 2; c++)
                {
                    config.Add(new object[] { a == 0, b == 0, c == 0 });
                }
            }
        }
        return config;
    }

    private static void SetAllocator(IChannel channel, IByteBufAllocator allocator)
    {
        channel.Config.SetAllocator(allocator);
    }

    private static IByteBufAllocator GetAllocator(bool directBuffer)
    {
        if (directBuffer)
        {
            return new UnpooledByteBufAllocator(true);
        }
        else
        {
            // Force usage of heap buffers and also ensure memoryAddress() is not not supported.
            return new HeapOnlyByteBufAllocator();
        }
    }

    // The anonymous AbstractByteBufAllocator of getAllocator(false).
    private sealed class HeapOnlyByteBufAllocator : AbstractByteBufAllocator
    {
        internal HeapOnlyByteBufAllocator() : base(false)
        {
        }

        public override ByteBuf IoBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        {
            return HeapBuffer(initialCapacity, maxCapacity);
        }

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            return new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            return new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity);
        }

        public override bool IsDirectBufferPooled => false;
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(Data))]
    public async Task TestEchoStartedFromServer(bool autoRead, bool directBuffer, bool composite)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IByteBufAllocator allocator = GetAllocator(directBuffer);
        EchoHandler sh = new EchoHandler(true, autoRead, allocator);
        EchoHandler ch = new EchoHandler(false, autoRead, allocator);
        WriteFutures writeFutures = new WriteFutures();
        IChannel server = await QuicTestUtils.NewServer(ImmediateExecutor.INSTANCE,
                new ServerStartsStreamHandler(this, sh, writeFutures, autoRead, composite, allocator), sh);
        SetAllocator(server, allocator);
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(ImmediateExecutor.INSTANCE);
        IQuicChannel quicChannel = null;
        try
        {
            quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(new ReadOnDemandHandler(autoRead))
                    .StreamHandler(ch)
                    // Use the same allocator for the streams.
                    .StreamOption(ChannelOption.ALLOCATOR, allocator)
                    .RemoteAddress(address)
                    .Option(ChannelOption.AUTO_READ, autoRead)
                    .Option(ChannelOption.ALLOCATOR, allocator)
                    .ConnectAsync(cancellationToken);

            await WaitForData(ch, sh, cancellationToken);

            for (;;)
            {
                List<Task> futures = writeFutures.Value;
                if (futures != null)
                {
                    foreach (Task f in futures)
                    {
                        await f.WaitAsync(cancellationToken);
                    }
                    break;
                }

                await Task.Delay(50, cancellationToken);
            }
            await WaitForData(sh, ch, cancellationToken);

            // Close underlying streams.
            await sh.channel.CloseAsync(cancellationToken);
            await ch.channel.CloseAsync(cancellationToken);

            // Close underlying quic channels
            await sh.channel.Parent.CloseAsync(cancellationToken);
            await ch.channel.Parent.CloseAsync(cancellationToken);

            CheckForException(ch, sh);
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            await QuicTestUtils.CloseIfNotNull(quicChannel);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);
        }
    }

    // A Box for the AtomicReference<List<ChannelFuture>> of testEchoStartedFromServer.
    private sealed class WriteFutures
    {
        private volatile List<Task> value;

        internal List<Task> Value
        {
            get => value;
            set => this.value = value;
        }
    }

    // The anonymous server handler of testEchoStartedFromServer.
    private sealed class ServerStartsStreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly QuicChannelEchoTest test;
        private readonly EchoHandler sh;
        private readonly WriteFutures writeFutures;
        private readonly bool autoRead;
        private readonly bool composite;
        private readonly IByteBufAllocator allocator;

        internal ServerStartsStreamHandler(QuicChannelEchoTest test, EchoHandler sh, WriteFutures writeFutures,
                                           bool autoRead, bool composite, IByteBufAllocator allocator)
        {
            this.test = test;
            this.sh = sh;
            this.writeFutures = writeFutures;
            this.autoRead = autoRead;
            this.composite = composite;
            this.allocator = allocator;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            SetAllocator(ctx.Channel, allocator);
            _ = ((IQuicChannel) ctx.Channel).CreateStreamAsync(QuicStreamType.BIDIRECTIONAL, sh)
                    .ContinueWith(future =>
                    {
                        IQuicStreamChannel stream = future.Result;
                        SetAllocator(stream, allocator);
                        List<Task> futures = test.WriteAllData(stream, composite, allocator);
                        writeFutures.Value = futures;
                    }, TaskScheduler.Default);

            ctx.Channel.Config.SetAutoRead(autoRead);
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }
    }

    // The anonymous client handler of testEchoStartedFromServer.
    private sealed class ReadOnDemandHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool autoRead;

        internal ReadOnDemandHandler(bool autoRead)
        {
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(Data))]
    public Task TestEchoStartedFromClient(bool autoRead, bool directBuffer, bool composite)
    {
        return EchoStartedFromClient(autoRead, composite, GetAllocator(directBuffer), TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdaptiveMemorySupportsTheRuntimeQuicPin(bool composite)
    {
        using var allocator = new AdaptiveByteBufAllocator(preferDirect: true);
        await EchoStartedFromClient(autoRead: true, composite, allocator, TestContext.Current.CancellationToken);
    }

    private async Task EchoStartedFromClient(bool autoRead, bool composite, IByteBufAllocator allocator,
        CancellationToken cancellationToken)
    {
        EchoHandler sh = new EchoHandler(true, autoRead, allocator);
        EchoHandler ch = new EchoHandler(false, autoRead, allocator);
        QuicChannelValidationHandler serverHandler = new ServerValidationHandler(autoRead, allocator);

        IChannel server = await QuicTestUtils.NewServer(ImmediateExecutor.INSTANCE, serverHandler, sh);
        SetAllocator(server, allocator);
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(ImmediateExecutor.INSTANCE);
        IQuicChannel quicChannel = null;
        try
        {
            QuicChannelValidationHandler clientHandler = new ClientValidationHandler(autoRead);
            quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(ch)
                    // Use the same allocator for the streams.
                    .StreamOption(ChannelOption.ALLOCATOR, allocator)
                    .RemoteAddress(address)
                    .Option(ChannelOption.AUTO_READ, autoRead)
                    .Option(ChannelOption.ALLOCATOR, allocator)
                    .ConnectAsync(cancellationToken);

            IQuicStreamChannel stream = await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL, ch,
                    cancellationToken);
            SetAllocator(stream, allocator);

            Assert.Equal(QuicStreamType.BIDIRECTIONAL, stream.Type());
            Assert.Equal(0, stream.StreamId());
            Assert.True(stream.IsLocalCreated());

            for (int i = 0; i < 5; i++)
            {
                ch.counter = 0;
                sh.counter = 0;
                List<Task> futures = WriteAllData(stream, composite, allocator);

                foreach (Task f in futures)
                {
                    await f.WaitAsync(cancellationToken);
                }
                await WaitForData(ch, sh, cancellationToken);
                await WaitForData(sh, ch, cancellationToken);
                await Task.Delay(100, cancellationToken);
            }

            // Close underlying streams.
            await sh.channel.CloseAsync(cancellationToken);
            await ch.channel.CloseAsync(cancellationToken);

            // Close underlying quic channels
            await sh.channel.Parent.CloseAsync(cancellationToken);
            await ch.channel.Parent.CloseAsync(cancellationToken);
            CheckForException(ch, sh);

            serverHandler.AssertState();
            clientHandler.AssertState();
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            await QuicTestUtils.CloseIfNotNull(quicChannel);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);
        }
    }

    // The anonymous server QuicChannelValidationHandler of testEchoStartedFromClient.
    private sealed class ServerValidationHandler : QuicChannelValidationHandler
    {
        private readonly bool autoRead;
        private readonly IByteBufAllocator allocator;

        internal ServerValidationHandler(bool autoRead, IByteBufAllocator allocator)
        {
            this.autoRead = autoRead;
            this.allocator = allocator;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            SetAllocator(ctx.Channel, allocator);
            ctx.Channel.Config.SetAutoRead(autoRead);
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }
    }

    // The anonymous client QuicChannelValidationHandler of testEchoStartedFromClient.
    private sealed class ClientValidationHandler : QuicChannelValidationHandler
    {
        private readonly bool autoRead;

        internal ClientValidationHandler(bool autoRead)
        {
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }
    }

    private List<Task> WriteAllData(IChannel channel, bool composite, IByteBufAllocator allocator)
    {
        if (composite)
        {
            CompositeByteBuf compositeByteBuf = allocator.CompositeBuffer();
            for (int i = 0; i < data.Length;)
            {
                int length = Math.Min(random.Next(1024 * 64), data.Length - i);
                ByteBuf buf = allocator.Buffer().WriteBytes(data.AsSpan(i, length));
                compositeByteBuf.AddComponent(buf, true);
                i += length;
            }
            return new List<Task> { channel.WriteAndFlushAsync(compositeByteBuf) };
        }
        else
        {
            List<Task> futures = new List<Task>();
            for (int i = 0; i < data.Length;)
            {
                int length = Math.Min(random.Next(1024 * 64), data.Length - i);
                ByteBuf buf = allocator.Buffer().WriteBytes(data.AsSpan(i, length));
                futures.Add(channel.WriteAndFlushAsync(buf));
                i += length;
            }
            return futures;
        }
    }

    private static async Task WaitForData(EchoHandler h1, EchoHandler h2, CancellationToken cancellationToken)
    {
        while (h1.counter < data.Length)
        {
            if (h2.exception != null)
            {
                break;
            }
            if (h1.exception != null)
            {
                break;
            }

            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (ThreadInterruptedException)
            {
                // Ignore.
            }
        }
    }

    private static void CheckForException(EchoHandler h1, EchoHandler h2)
    {
        if (h1.exception != null && !(h1.exception is IOException))
        {
            ExceptionDispatchInfo.Capture(h1.exception).Throw();
        }
        if (h2.exception != null && !(h2.exception is IOException))
        {
            ExceptionDispatchInfo.Capture(h2.exception).Throw();
        }
        if (h1.exception != null)
        {
            ExceptionDispatchInfo.Capture(h1.exception).Throw();
        }
        if (h2.exception != null)
        {
            ExceptionDispatchInfo.Capture(h2.exception).Throw();
        }
    }

    private sealed class EchoHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly bool server;
        private readonly bool autoRead;
        private readonly IByteBufAllocator allocator;
        internal volatile IChannel channel;
        private Exception exceptionValue;
        internal volatile int counter;

        // AtomicReference<Throwable> exception.
        internal Exception exception => Volatile.Read(ref exceptionValue);

        internal EchoHandler(bool server, bool autoRead, IByteBufAllocator allocator)
        {
            this.server = server;
            this.autoRead = autoRead;
            this.allocator = allocator;
        }

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            ctx.Channel.Config.SetAutoRead(autoRead);
            SetAllocator(ctx.Channel, allocator);
            ctx.FireChannelRegistered();
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
            IQuicStreamChannel channel0 = (IQuicStreamChannel) ctx.Channel;
            Assert.Equal(QuicStreamType.BIDIRECTIONAL, channel0.Type());
            if (channel0.IsLocalCreated())
            {
                // Server starts with 1, client with 0
                Assert.Equal(server ? 1 : 0, channel0.StreamId());
            }
            else
            {
                // Server starts with 1, client with 0
                Assert.Equal(server ? 0 : 1, channel0.StreamId());
            }
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            byte[] actual = new byte[@in.ReadableBytes];
            @in.ReadBytes(actual);

            int lastIdx = counter;
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.Equal(data[i + lastIdx], actual[i]);
            }

            if (!((IQuicStreamChannel) ctx.Channel).IsLocalCreated())
            {
                channel.WriteAsync(Unpooled.WrappedBuffer(actual));
            }

            counter += actual.Length;
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            try
            {
                ctx.Flush();
            }
            finally
            {
                if (!autoRead)
                {
                    ctx.Read();
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
                                             Exception cause)
        {
            if (Interlocked.CompareExchange(ref exceptionValue, cause, null) == null)
            {
                Console.Error.WriteLine(cause);
                ctx.CloseAsync();
            }
        }
    }
}
