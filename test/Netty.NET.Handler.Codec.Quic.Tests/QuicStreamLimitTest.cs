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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over AbstractQuicTest.NewSslTaskExecutors().
// Future.get()/sync() are awaits and Future.await().cause() is the exception of the awaited Task (Cause). CountDownLatch
// is CountdownEvent, waited with the test timeout and counted down at most to zero as Java's latch is;
// assertInstanceOf(QuicException.class, ...) is Assert.IsAssignableFrom<QuicException>. The anonymous handlers are the
// private nested classes below. QuicStreamLimitChangedEvent follows the peer's stream limits that msquic reports
// (QuicConnectionOptions.StreamCapacityCallback, docs/codec-classes-quic-porting.md): once when the connection is
// established and again when the peer allows more streams.
//
// CLR-inapplicable (docs/codec-native-quic-porting-manifest.json):
// testStreamLimitEnforcedWhenCreatingViaServerBidirectional and testStreamLimitEnforcedWhenCreatingViaServerUnidirectional
// expect the server's second createStream to fail with QuicException when the client allows one stream of the type.
// System.Net.Quic does not report the client's initial stream limits to a server connection
// (QuicServerConnectionOptions.StreamCapacityCallback is not called for them, measured with .NET 10.0.11), so the
// server cannot fail the stream early: its second OpenOutboundStreamAsync waits for stream capacity, and the test only
// ends when the idle timeout closes the connection (ClosedChannelException).
public class QuicStreamLimitTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestStreamLimitEnforcedWhenCreatingViaClientBidirectional(IExecutor executor)
    {
        await TestStreamLimitEnforcedWhenCreatingViaClient(executor, QuicStreamType.BIDIRECTIONAL,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestStreamLimitEnforcedWhenCreatingViaClientUnidirectional(IExecutor executor)
    {
        await TestStreamLimitEnforcedWhenCreatingViaClient(executor, QuicStreamType.UNIDIRECTIONAL,
                TestContext.Current.CancellationToken);
    }

    private static async Task TestStreamLimitEnforcedWhenCreatingViaClient(IExecutor executor, QuicStreamType type,
            CancellationToken cancellationToken)
    {
        QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
        IChannel server = await QuicTestUtils.NewServer(
                QuicTestUtils.NewQuicServerBuilder(executor).InitialMaxStreamsBidirectional(1)
                        .InitialMaxStreamsUnidirectional(1),
                InsecureQuicTokenHandler.INSTANCE,
                serverHandler, new CloseOnInputShutdownReadCompleteHandler());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(executor);

        using CountdownEvent latch = new CountdownEvent(1);
        using CountdownEvent latch2 = new CountdownEvent(1);
        QuicChannelValidationHandler clientHandler = new StreamLimitChangedHandler(latch, latch2);

        try
        {
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken);
            Assert.True(latch.Wait(TIMEOUT, cancellationToken));
            Assert.Equal(1, quicChannel.PeerAllowedStreams(QuicStreamType.UNIDIRECTIONAL));
            Assert.Equal(1, quicChannel.PeerAllowedStreams(QuicStreamType.BIDIRECTIONAL));
            IQuicStreamChannel stream = await quicChannel.CreateStreamAsync(
                    type, new ChannelInboundHandlerAdapter(), cancellationToken);

            Assert.Equal(0, quicChannel.PeerAllowedStreams(type));

            // Second stream creation should fail.
            Exception cause = await Cause(quicChannel.CreateStreamAsync(
                    type, new ChannelInboundHandlerAdapter(), cancellationToken));
            Assert.IsAssignableFrom<QuicException>(cause);
            await stream.CloseAsync(cancellationToken);
            Assert.True(latch2.Wait(TIMEOUT, cancellationToken));

            Assert.Equal(1, quicChannel.PeerAllowedStreams(type));
            await quicChannel.CloseAsync(cancellationToken);

            serverHandler.AssertState();
            clientHandler.AssertState();
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    // The anonymous server stream handler of testStreamLimitEnforcedWhenCreatingViaClient.
    private sealed class CloseOnInputShutdownReadCompleteHandler : ChannelInboundHandlerAdapter
    {
        public override bool IsSharable => true;

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                ctx.CloseAsync();
            }
        }
    }

    // The anonymous client QuicChannelValidationHandler of testStreamLimitEnforcedWhenCreatingViaClient.
    private sealed class StreamLimitChangedHandler : QuicChannelValidationHandler
    {
        private readonly CountdownEvent latch;
        private readonly CountdownEvent latch2;

        internal StreamLimitChangedHandler(CountdownEvent latch, CountdownEvent latch2)
        {
            this.latch = latch;
            this.latch2 = latch2;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is QuicStreamLimitChangedEvent)
            {
                if (latch.CurrentCount == 0)
                {
                    CountDown(latch2);
                }
                else
                {
                    CountDown(latch);
                }
            }

            base.UserEventTriggered(ctx, evt);
        }
    }

    // CLR-only: CountDownLatch.countDown(), which stops at zero.
    private static void CountDown(CountdownEvent latch)
    {
        if (latch.CurrentCount > 0)
        {
            latch.Signal();
        }
    }

    // CLR-only: Future.await().cause() of a Task.
    private static async Task<Exception> Cause(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
