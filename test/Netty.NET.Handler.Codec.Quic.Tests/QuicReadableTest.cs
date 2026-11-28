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
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over AbstractQuicTest.NewSslTaskExecutors().
// Future.get()/sync() are awaits (writeAndFlush(...).sync().channel() is the stream the awaited write was made on);
// CountDownLatch is CountdownEvent, waited with the test timeout; AtomicInteger/AtomicReference are StrongBox values
// read and written with Interlocked/Volatile; Long.BYTES is sizeof(long) and Thread.sleep(50) is Task.Delay(50).
// throwIfNotNull rethrows the recorded cause with its stack trace (ExceptionDispatchInfo). The anonymous handlers are
// the private nested classes below.
public class QuicReadableTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCorrectlyHandleReadableStreams(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int numOfStreams = 256;
        int readStreams = numOfStreams / 2;
        // We do write longs.
        int expectedDataRead = readStreams * sizeof(long);
        using CountdownEvent latch = new CountdownEvent(numOfStreams);
        StrongBox<int> bytesRead = new StrongBox<int>();
        StrongBox<Exception> serverErrorRef = new StrongBox<Exception>();
        StrongBox<Exception> clientErrorRef = new StrongBox<Exception>();

        QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
        IChannel server = await QuicTestUtils.NewServer(
                QuicTestUtils.NewQuicServerBuilder(executor).InitialMaxStreamsBidirectional(5000),
                InsecureQuicTokenHandler.INSTANCE,
                serverHandler, new ServerStreamHandler(readStreams, latch, bytesRead, serverErrorRef));
        IChannel channel = await QuicTestUtils.NewClient(executor);
        QuicChannelValidationHandler clientHandler = new QuicChannelValidationHandler();
        ByteBuf data = Unpooled.DirectBuffer().WriteLong(8);

        try
        {
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            List<IChannel> streams = new List<IChannel>();

            for (int i = 0; i < numOfStreams; i++)
            {
                IQuicStreamChannel stream = await quicChannel.CreateStreamAsync(
                        QuicStreamType.BIDIRECTIONAL, new ClientStreamHandler(clientErrorRef), cancellationToken);
                await stream.WriteAndFlushAsync(data.RetainedSlice(), cancellationToken);
                streams.Add(stream);
            }

            Assert.True(latch.Wait(TIMEOUT, cancellationToken));

            while (Volatile.Read(ref bytesRead.Value) < expectedDataRead)
            {
                await Task.Delay(50, cancellationToken);
            }

            foreach (IChannel stream in streams)
            {
                await stream.CloseAsync(cancellationToken);
            }

            await quicChannel.CloseAsync(cancellationToken);

            ThrowIfNotNull(serverErrorRef);
            ThrowIfNotNull(clientErrorRef);

            serverHandler.AssertState();
            clientHandler.AssertState();
        }
        finally
        {
            data.Release();
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    // The anonymous server stream handler of testCorrectlyHandleReadableStreams.
    private sealed class ServerStreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly int readStreams;
        private readonly CountdownEvent latch;
        private readonly StrongBox<int> bytesRead;
        private readonly StrongBox<Exception> serverErrorRef;
        private int counter;

        internal ServerStreamHandler(int readStreams, CountdownEvent latch, StrongBox<int> bytesRead,
                                     StrongBox<Exception> serverErrorRef)
        {
            this.readStreams = readStreams;
            this.latch = latch;
            this.bytesRead = bytesRead;
            this.serverErrorRef = serverErrorRef;
        }

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            // Ensure we dont read from the streams so all of these will be reported as readable
            ctx.Channel.Config.SetAutoRead(false);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            counter++;

            if (latch.CurrentCount > 0)
            {
                latch.Signal();
            }

            if (counter > readStreams)
            {
                // Now set it to readable again for some channels
                ctx.Channel.Config.SetAutoRead(true);
            }
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buffer = (ByteBuf) msg;
            Interlocked.Add(ref bytesRead.Value, buffer.ReadableBytes);
            buffer.Release();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Volatile.Write(ref serverErrorRef.Value, cause);
        }

        public override bool IsSharable => true;
    }

    // The anonymous client stream handler of testCorrectlyHandleReadableStreams.
    private sealed class ClientStreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly StrongBox<Exception> clientErrorRef;

        internal ClientStreamHandler(StrongBox<Exception> clientErrorRef)
        {
            this.clientErrorRef = clientErrorRef;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Volatile.Write(ref clientErrorRef.Value, cause);
        }
    }

    private static void ThrowIfNotNull(StrongBox<Exception> errorRef)
    {
        Exception cause = Volatile.Read(ref errorRef.Value);
        if (cause != null)
        {
            ExceptionDispatchInfo.Capture(cause).Throw();
        }
    }
}
