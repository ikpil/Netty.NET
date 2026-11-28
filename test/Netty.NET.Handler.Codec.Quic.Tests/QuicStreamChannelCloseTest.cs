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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: The ImmediateEventExecutor Promise<Channel> is a TaskCompletionSource<IChannel> (trySuccess is
// TrySetResult, setFailure TrySetException, await().cause() the exception of the awaited Task),
// Future.get()/sync() are awaits and closeFuture().get(3000, MILLISECONDS) waits for the CloseFuture at most 3000
// ms (TimeoutException as Java's TimeoutException). The ChannelFutureListeners are continuations of the Tasks:
// ChannelFutureListener.CLOSE closes the stream and QuicStreamChannel.SHUTDOWN_OUTPUT, which has no counterpart,
// calls ShutdownOutputAsync(). java.nio.channels.ClosedChannelException is the Transport's ClosedChannelException
// and assertInstanceOf(C.class, x) is Assert.IsAssignableFrom<C>.
public class QuicStreamChannelCloseTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCloseFromServerWhileInActiveUnidirectional(IExecutor executor)
    {
        await TestCloseFromServerWhileInActive(executor, QuicStreamType.UNIDIRECTIONAL, false,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCloseFromServerWhileInActiveBidirectional(IExecutor executor)
    {
        await TestCloseFromServerWhileInActive(executor, QuicStreamType.BIDIRECTIONAL, false,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestHalfCloseFromServerWhileInActiveUnidirectional(IExecutor executor)
    {
        await TestCloseFromServerWhileInActive(executor, QuicStreamType.UNIDIRECTIONAL, true,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestHalfCloseFromServerWhileInActiveBidirectional(IExecutor executor)
    {
        await TestCloseFromServerWhileInActive(executor, QuicStreamType.BIDIRECTIONAL, true,
                TestContext.Current.CancellationToken);
    }

    private static async Task TestCloseFromServerWhileInActive(IExecutor executor, QuicStreamType type,
                                                               bool halfClose, CancellationToken cancellationToken)
    {
        IChannel server = null;
        IChannel channel = null;

        try
        {
            TaskCompletionSource<IChannel> streamPromise =
                    new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            QuicChannelValidationHandler serverHandler = new StreamCreationHandler(type, halfClose, streamPromise);
            server = await QuicTestUtils.NewServer(executor, serverHandler,
                    new ChannelInboundHandlerAdapter());
            channel = await QuicTestUtils.NewClient(executor);

            QuicChannelValidationHandler clientHandler = new QuicChannelValidationHandler();

            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new StreamHandler())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            IChannel streamChannel = await streamPromise.Task.WaitAsync(cancellationToken);

            // Wait for the steam to close. It needs to happen before the 5-second connection idle timeout.
            await streamChannel.CloseFuture.WaitAsync(TimeSpan.FromMilliseconds(3000), cancellationToken);

            _ = streamChannel.Parent.CloseAsync();

            // Wait till the client was closed
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

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCloseFromClientWhileInActiveUnidirectional(IExecutor executor)
    {
        await TestCloseFromClientWhileInActive(executor, QuicStreamType.UNIDIRECTIONAL, false,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestCloseFromClientWhileInActiveBidirectional(IExecutor executor)
    {
        await TestCloseFromClientWhileInActive(executor, QuicStreamType.BIDIRECTIONAL, false,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestHalfCloseFromClientWhileInActiveUnidirectional(IExecutor executor)
    {
        await TestCloseFromClientWhileInActive(executor, QuicStreamType.UNIDIRECTIONAL, true,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestHalfCloseFromClientWhileInActiveBidirectional(IExecutor executor)
    {
        await TestCloseFromClientWhileInActive(executor, QuicStreamType.BIDIRECTIONAL, true,
                TestContext.Current.CancellationToken);
    }

    private static async Task TestCloseFromClientWhileInActive(IExecutor executor, QuicStreamType type,
                                                               bool halfClose, CancellationToken cancellationToken)
    {
        IChannel server = null;
        IChannel channel = null;

        try
        {
            TaskCompletionSource<IChannel> streamPromise =
                    new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
            server = await QuicTestUtils.NewServer(executor, serverHandler, new StreamHandler());
            channel = await QuicTestUtils.NewClient(executor);

            StreamCreationHandler creationHandler = new StreamCreationHandler(type, halfClose, streamPromise);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(creationHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            IChannel streamChannel = await streamPromise.Task.WaitAsync(cancellationToken);

            // Wait for the steam to close. It needs to happen before the 5-second connection idle timeout.
            await streamChannel.CloseFuture.WaitAsync(TimeSpan.FromMilliseconds(3000), cancellationToken);

            _ = streamChannel.Parent.CloseAsync();

            // Wait till the client was closed
            await quicChannel.CloseFuture.WaitAsync(cancellationToken);

            serverHandler.AssertState();
            creationHandler.AssertState();
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);

            Shutdown(executor);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestWriteToUnidirectionalAfterShutdownOutput(IExecutor executor)
    {
        await TestWriteAfterClosedOrShutdown(executor, QuicStreamType.UNIDIRECTIONAL, true,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestWriteToBidirectionalAfterShutdownOutput(IExecutor executor)
    {
        await TestWriteAfterClosedOrShutdown(executor, QuicStreamType.BIDIRECTIONAL, true,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestWriteToUnidirectionalAfterClose(IExecutor executor)
    {
        await TestWriteAfterClosedOrShutdown(executor, QuicStreamType.UNIDIRECTIONAL, false,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestWriteToBidirectionalAfterClose(IExecutor executor)
    {
        await TestWriteAfterClosedOrShutdown(executor, QuicStreamType.BIDIRECTIONAL, false,
                TestContext.Current.CancellationToken);
    }

    private static async Task TestWriteAfterClosedOrShutdown(IExecutor executor, QuicStreamType type,
                                                             bool halfClose, CancellationToken cancellationToken)
    {
        IChannel server = null;
        IChannel channel = null;

        try
        {
            TaskCompletionSource<IChannel> streamPromise =
                    new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = await QuicTestUtils.NewServer(executor, new ChannelInboundHandlerAdapter(), new StreamHandler());
            channel = await QuicTestUtils.NewClient(executor);

            StreamCreationAndTearDownHandler creationHandler =
                    new StreamCreationAndTearDownHandler(type, halfClose, streamPromise);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(creationHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            // ChannelOutputShutdownException should only be used when its a BIDIRECTIONAL channel and half-closure
            // is used.
            Exception cause = await Cause(streamPromise.Task.WaitAsync(cancellationToken));

            if (halfClose && type != QuicStreamType.UNIDIRECTIONAL)
            {
                Assert.IsAssignableFrom<ChannelOutputShutdownException>(cause);
            }
            else
            {
                Assert.IsAssignableFrom<ClosedChannelException>(cause);
            }

            await quicChannel.CloseAsync(cancellationToken);

            // Wait till the client was closed
            await quicChannel.CloseFuture.WaitAsync(cancellationToken);
            creationHandler.AssertState();
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);

            Shutdown(executor);
        }
    }

    private sealed class StreamCreationAndTearDownHandler : QuicChannelValidationHandler
    {
        private readonly QuicStreamType type;
        private readonly bool halfClose;
        private readonly TaskCompletionSource<IChannel> streamPromise;

        internal StreamCreationAndTearDownHandler(QuicStreamType type, bool halfClose,
                                                  TaskCompletionSource<IChannel> streamPromise)
        {
            this.type = type;
            this.halfClose = halfClose;
            this.streamPromise = streamPromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            IQuicChannel channel = (IQuicChannel) ctx.Channel;
            _ = channel.CreateStreamAsync(type, new TearDownHandler(halfClose, streamPromise));
        }
    }

    // The anonymous stream handler of StreamCreationAndTearDownHandler.
    private sealed class TearDownHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool halfClose;
        private readonly TaskCompletionSource<IChannel> streamPromise;

        internal TearDownHandler(bool halfClose, TaskCompletionSource<IChannel> streamPromise)
        {
            this.halfClose = halfClose;
            this.streamPromise = streamPromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            Task future;

            if (halfClose)
            {
                future = ((IQuicStreamChannel) ctx.Channel).ShutdownOutputAsync();
            }
            else
            {
                future = ctx.Channel.CloseAsync();
            }

            _ = future.ContinueWith(f =>
            {
                _ = ctx.Channel.WriteAndFlushAsync("Unsupported message").ContinueWith(wf =>
                {
                    streamPromise.TrySetException(wf.Exception?.InnerException ??
                            new InvalidOperationException("The write did not fail"));
                }, TaskContinuationOptions.ExecuteSynchronously);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    private sealed class StreamCreationHandler : QuicChannelValidationHandler
    {
        private readonly QuicStreamType type;
        private readonly bool halfClose;
        private readonly TaskCompletionSource<IChannel> streamPromise;

        internal StreamCreationHandler(QuicStreamType type, bool halfClose, TaskCompletionSource<IChannel> streamPromise)
        {
            this.type = type;
            this.halfClose = halfClose;
            this.streamPromise = streamPromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            IQuicChannel channel = (IQuicChannel) ctx.Channel;
            _ = channel.CreateStreamAsync(type, new WriteAndCloseHandler(halfClose, streamPromise));
        }
    }

    // The anonymous stream handler of StreamCreationHandler.
    private sealed class WriteAndCloseHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool halfClose;
        private readonly TaskCompletionSource<IChannel> streamPromise;

        internal WriteAndCloseHandler(bool halfClose, TaskCompletionSource<IChannel> streamPromise)
        {
            this.halfClose = halfClose;
            this.streamPromise = streamPromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            streamPromise.TrySetResult(ctx.Channel);
            // Do the write and close the channel
            IQuicStreamChannel stream = (IQuicStreamChannel) ctx.Channel;
            _ = ctx.WriteAndFlushAsync(Unpooled.Buffer().WriteZero(8))
                    .ContinueWith(_ => halfClose
                            ? stream.ShutdownOutputAsync()
                            : stream.CloseAsync(), TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    private sealed class StreamHandler : ChannelInboundHandlerAdapter
    {

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                // Received a FIN
                ctx.CloseAsync();
            }
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
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
