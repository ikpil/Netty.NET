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

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over AbstractQuicTest.NewSslTaskExecutors().
// The ImmediateEventExecutor promises are TaskCompletionSources (Promise<Throwable>.setSuccess(cause) is
// TrySetResult(cause), PromiseNotifier a continuation that completes the promise like the write Task, await().isSuccess()
// an awaited Task that completed successfully); assertInstanceOf(UnsupportedOperationException.class, ...) is
// Assert.IsAssignableFrom<NotSupportedException> (UnsupportedOperationException is NotSupportedException).
public class QuicStreamTypeTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestUnidirectionalCreatedByClient(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = null;
        IChannel channel = null;
        QuicChannelValidationHandler serverHandler = new QuicChannelValidationHandler();
        QuicChannelValidationHandler clientHandler = new QuicChannelValidationHandler();

        try
        {
            TaskCompletionSource<Exception> serverWritePromise =
                    new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = await QuicTestUtils.NewServer(executor, serverHandler,
                    new ServerUnidirectionalStreamHandler(serverWritePromise));

            channel = await QuicTestUtils.NewClient(executor);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);
            IQuicStreamChannel streamChannel = await quicChannel.CreateStreamAsync(
                    QuicStreamType.UNIDIRECTIONAL, new ChannelInboundHandlerAdapter(), cancellationToken);
            // Do the write which should succeed
            await streamChannel.WriteAndFlushAsync(Unpooled.Buffer().WriteZero(8), cancellationToken);

            // Close stream and quic channel
            await streamChannel.CloseAsync(cancellationToken);
            await quicChannel.CloseAsync(cancellationToken);
            Assert.IsAssignableFrom<NotSupportedException>(await serverWritePromise.Task.WaitAsync(cancellationToken));

            serverHandler.AssertState();
            clientHandler.AssertState();
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);
        }
    }

    // The anonymous server stream handler of testUnidirectionalCreatedByClient.
    private sealed class ServerUnidirectionalStreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource<Exception> serverWritePromise;

        internal ServerUnidirectionalStreamHandler(TaskCompletionSource<Exception> serverWritePromise)
        {
            this.serverWritePromise = serverWritePromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            IQuicStreamChannel channel = (IQuicStreamChannel) ctx.Channel;
            Assert.Equal(QuicStreamType.UNIDIRECTIONAL, channel.Type());
            Assert.False(channel.IsLocalCreated());
            _ = ctx.WriteAndFlushAsync(Unpooled.Buffer().WriteZero(8))
                    .ContinueWith(future => serverWritePromise.TrySetResult(future.Exception?.InnerException),
                            TaskScheduler.Default);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestUnidirectionalCreatedByServer(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = null;
        IChannel channel = null;
        TaskCompletionSource serverWritePromise =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Exception> clientWritePromise =
                new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        QuicChannelValidationHandler serverHandler = new ServerStreamCreationHandler(serverWritePromise);
        QuicChannelValidationHandler clientHandler = new QuicChannelValidationHandler();
        try
        {
            server = await QuicTestUtils.NewServer(executor, serverHandler, new ChannelInboundHandlerAdapter());

            channel = await QuicTestUtils.NewClient(executor);
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientHandler)
                    .StreamHandler(new ClientStreamHandler(clientWritePromise))
                    .RemoteAddress(server.LocalAddress)
                    .ConnectAsync(cancellationToken);

            await quicChannel.CloseFuture.WaitAsync(cancellationToken);
            await serverWritePromise.Task.WaitAsync(cancellationToken);
            Assert.True(serverWritePromise.Task.IsCompletedSuccessfully);
            Assert.IsAssignableFrom<NotSupportedException>(await clientWritePromise.Task.WaitAsync(cancellationToken));

            serverHandler.AssertState();
            clientHandler.AssertState();
        }
        finally
        {
            await QuicTestUtils.CloseIfNotNull(channel);
            await QuicTestUtils.CloseIfNotNull(server);
        }
    }

    // The anonymous QuicChannelValidationHandler of testUnidirectionalCreatedByServer.
    private sealed class ServerStreamCreationHandler : QuicChannelValidationHandler
    {
        private readonly TaskCompletionSource serverWritePromise;

        internal ServerStreamCreationHandler(TaskCompletionSource serverWritePromise)
        {
            this.serverWritePromise = serverWritePromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            IQuicChannel channel = (IQuicChannel) ctx.Channel;
            _ = channel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL, new ServerStreamWriteHandler(serverWritePromise));
        }
    }

    // The anonymous stream handler of ServerStreamCreationHandler.
    private sealed class ServerStreamWriteHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource serverWritePromise;

        internal ServerStreamWriteHandler(TaskCompletionSource serverWritePromise)
        {
            this.serverWritePromise = serverWritePromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Do the write which should succeed
            _ = ctx.WriteAndFlushAsync(Unpooled.Buffer().WriteZero(8)).ContinueWith(f =>
            {
                if (f.IsCompletedSuccessfully)
                {
                    serverWritePromise.TrySetResult();
                }
                else
                {
                    serverWritePromise.TrySetException(f.Exception?.InnerException ?? new OperationCanceledException());
                }
            }, TaskScheduler.Default);
        }
    }

    // The anonymous client stream handler of testUnidirectionalCreatedByServer.
    private sealed class ClientStreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource<Exception> clientWritePromise;

        internal ClientStreamHandler(TaskCompletionSource<Exception> clientWritePromise)
        {
            this.clientWritePromise = clientWritePromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Do the write should fail
            _ = ctx.WriteAndFlushAsync(Unpooled.Buffer().WriteZero(8))
                    .ContinueWith(future => clientWritePromise.TrySetResult(future.Exception?.InnerException),
                            TaskScheduler.Default);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            // Close the QUIC channel as well.
            ctx.Channel.Parent.CloseAsync();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            // Let's close the stream
            ctx.CloseAsync();
        }
    }
}
