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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over AbstractQuicTest.NewSslTaskExecutors().
// connect().get() and closeFuture().sync() are awaits; ChannelFutureListener.CLOSE is a continuation of the write Task
// that closes the channel; BlockingQueue<Integer> is BlockingCollection<int>, whose take() waits at most the test
// timeout.
public class QuicStreamHalfClosureTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCloseHalfClosureUnidirectional(IExecutor executor)
    {
        await TestCloseHalfClosure(executor, QuicStreamType.UNIDIRECTIONAL, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCloseHalfClosureBidirectional(IExecutor executor)
    {
        await TestCloseHalfClosure(executor, QuicStreamType.BIDIRECTIONAL, TestContext.Current.CancellationToken);
    }

    private static async Task TestCloseHalfClosure(IExecutor executor, QuicStreamType type,
                                                   CancellationToken cancellationToken)
    {
        IChannel server = null;
        IChannel channel = null;
        QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
        QuicChannelValidationHandler clientHandler = new StreamCreationHandler(type);

        try
        {
            StreamHandler handler = new StreamHandler();
            server = await QuicTestUtils.NewServer(executor, serverHandler, handler);
            channel = await QuicTestUtils.NewClient(executor);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            handler.AssertSequence();
            await quicChannel.CloseFuture.WaitAsync(cancellationToken);

            serverHandler.AssertState();
            clientHandler.AssertState();
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);

            Shutdown(executor);
        }
    }

    private sealed class StreamCreationHandler : QuicChannelValidationHandler
    {
        private readonly QuicStreamType type;

        internal StreamCreationHandler(QuicStreamType type)
        {
            this.type = type;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            IQuicChannel channel = (IQuicChannel) ctx.Channel;
            _ = channel.CreateStreamAsync(type, new WriteAndCloseHandler());
        }
    }

    // The anonymous stream handler of StreamCreationHandler.
    private sealed class WriteAndCloseHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Do the write and close the channel
            IChannel streamChannel = ctx.Channel;
            _ = ctx.WriteAndFlushAsync(Unpooled.Buffer().WriteZero(8))
                    .ContinueWith(_ => streamChannel.CloseAsync(), TaskScheduler.Default);
        }
    }

    private sealed class StreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<int> queue = new BlockingCollection<int>();

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            queue.Add(0);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            queue.Add(5);
            // Close the QUIC channel as well.
            ctx.Channel.Parent.CloseAsync();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);

            if (((IQuicStreamChannel) ctx.Channel).IsInputShutdown)
            {
                queue.Add(1);
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownEvent.INSTANCE)
            {
                AddIsShutdown(ctx);
                queue.Add(3);
            }
            else if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                queue.Add(4);
                ctx.CloseAsync();
            }
        }

        private void AddIsShutdown(IChannelHandlerContext ctx)
        {
            if (((IQuicStreamChannel) ctx.Channel).IsInputShutdown)
            {
                queue.Add(2);
            }
        }

        private int Take()
        {
            Assert.True(queue.TryTake(out int value, TIMEOUT));
            return value;
        }

        internal void AssertSequence()
        {
            Assert.Equal(0, Take());
            int value = Take();
            if (value == 1)
            {
                // If we did see the value of 1 it should be followed by 2 directly.
                Assert.Equal(2, Take());
            }
            else
            {
                Assert.Equal(2, value);
            }

            Assert.Equal(3, Take());
            Assert.Equal(4, Take());
            Assert.Equal(5, Take());
            Assert.True(queue.Count == 0);
        }
    }
}
