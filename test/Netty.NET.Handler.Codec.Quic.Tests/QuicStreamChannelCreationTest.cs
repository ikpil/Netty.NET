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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over AbstractQuicTest.NewSslTaskExecutors();
// Future.sync().get()/get() is await; CountDownLatch is CountdownEvent (waited with the test timeout);
// config().getOption(AUTO_READ) is Config.TryGetOption. Boolean.TRUE/FALSE are bool values; a null expected attribute
// is null.
public class QuicStreamChannelCreationTest : AbstractQuicTest
{

    private static readonly AttributeKey<string> ATTRIBUTE_KEY = AttributeKey<string>.NewInstance("testKey");
    private const string ATTRIBUTE_VALUE = "Test";

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCreateStream(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
        IChannel server = await QuicTestUtils.NewServer(executor, serverHandler,
                new ChannelInboundHandlerAdapter());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(executor);
        QuicChannelValidationHandler clientHandler = new QuicChannelValidationHandler();

        try
        {
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken);
            using CountdownEvent latch = new CountdownEvent(1);
            IQuicStreamChannel stream = await quicChannel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                    new RegisteredHandler(QuicStreamType.UNIDIRECTIONAL, true, null, latch), cancellationToken);
            AssertQuicStreamChannel(stream, QuicStreamType.UNIDIRECTIONAL, true, null);
            Assert.True(latch.Wait(TIMEOUT, cancellationToken));
            await stream.CloseAsync(cancellationToken);
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

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCreateStreamViaBootstrap(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
        IChannel server = await QuicTestUtils.NewServer(executor, serverHandler,
                new ChannelInboundHandlerAdapter());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(executor);
        QuicChannelValidationHandler clientHandler = new QuicChannelValidationHandler();

        try
        {
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken);
            using CountdownEvent latch = new CountdownEvent(1);
            IQuicStreamChannel stream = await quicChannel.NewStreamBootstrap()
                    .Type(QuicStreamType.UNIDIRECTIONAL)
                    .Attr(ATTRIBUTE_KEY, ATTRIBUTE_VALUE)
                    .Option(ChannelOption.AUTO_READ, false)
                    .Handler(new RegisteredHandler(QuicStreamType.UNIDIRECTIONAL, false, ATTRIBUTE_VALUE, latch))
                    .CreateAsync(cancellationToken);
            AssertQuicStreamChannel(stream, QuicStreamType.UNIDIRECTIONAL, false, ATTRIBUTE_VALUE);
            Assert.True(latch.Wait(TIMEOUT, cancellationToken));
            await stream.CloseAsync(cancellationToken);
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

    // The anonymous ChannelInboundHandlerAdapter whose channelRegistered asserts the stream channel.
    private sealed class RegisteredHandler : ChannelInboundHandlerAdapter
    {
        private readonly QuicStreamType expectedType;
        private readonly bool expectedAutoRead;
        private readonly string expectedAttribute;
        private readonly CountdownEvent latch;

        internal RegisteredHandler(QuicStreamType expectedType, bool expectedAutoRead, string expectedAttribute,
                                   CountdownEvent latch)
        {
            this.expectedType = expectedType;
            this.expectedAutoRead = expectedAutoRead;
            this.expectedAttribute = expectedAttribute;
            this.latch = latch;
        }

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            AssertQuicStreamChannel((IQuicStreamChannel) ctx.Channel, expectedType, expectedAutoRead,
                    expectedAttribute);
            latch.Signal();
        }
    }

    private static void AssertQuicStreamChannel(IQuicStreamChannel channel, QuicStreamType expectedType,
                                                bool expectedAutoRead, string expectedAttribute)
    {
        Assert.Equal(expectedType, channel.Type());
        Assert.True(channel.Config.TryGetOption(ChannelOption.AUTO_READ, out bool autoRead));
        Assert.Equal(expectedAutoRead, autoRead);
        Assert.Equal(expectedAttribute, channel.Attr(ATTRIBUTE_KEY).Get());
    }
}
