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
using Moq;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using static Netty.NET.Handler.Codec.Http2.Http2FrameCodecBuilder;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the Local transport is the Transport port (MultiThreadIoEventLoopGroup with LocalIoHandler.NewFactory(),
// LocalServerChannel, LocalChannel, docs/transport-porting.md, Tests); sync()/syncUninterruptibly() on a ChannelFuture
// is awaiting its Task, and Promise<Http2StreamChannel> is TaskCompletionSource<IHttp2StreamChannel>
// (src/Netty.NET.Handler.Codec.Http2/Http2StreamChannelBootstrap.cs, docs/codec-http-porting.md, Shared CLR
// adaptations). getClass().getName() is GetType().FullName (docs/common-clr-design.md, Source class names in text). The
// anonymous ChannelInitializer bodies are ActionChannelInitializer delegates (docs/transport-porting.md: "the anonymous
// ChannelInitializer is an ActionChannelInitializer<IChannel>"), CountDownLatch is CountdownEvent and await(timeout)
// Wait(timeout, TestContext.Current.CancellationToken) (docs/handler-porting.md, SSL tests: Concurrency helpers), and
// Mockito's mock/when(...).thenReturn are Moq Mock<T>/Setup(...).Returns (docs/codec-http-porting.md, Shared CLR
// adaptations). Future.get() throws ExecutionException with the cause, awaiting the Task throws the cause itself, so
// assertThrows(ExecutionException) with assertInstanceOf(T, e.getCause()) is Assert.ThrowsAnyAsync<T>
// (docs/codec-http-porting.md, Core test notes), and assertInstanceOf is Assert.IsAssignableFrom (same section).
// InterruptedException is ThreadInterruptedException, which Thread.Interrupt() raises in a blocked thread
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.interrupt?view=net-10.0).
public class Http2StreamChannelBootstrapTest
{

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(Http2StreamChannelBootstrapTest));

    private volatile IChannel serverConnectedChannel;

    [Fact]
    public async Task TestStreamIsNotCreatedIfParentConnectionIsClosedConcurrently()
    {
        IEventLoopGroup group = null;
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            CountdownEvent serverChannelLatch = new CountdownEvent(1);
            group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
            LocalAddress serverAddress = new LocalAddress(GetType().FullName);
            ServerBootstrap sb = new ServerBootstrap()
                    .Channel<LocalServerChannel>()
                    .Group(group)
                    .ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        serverConnectedChannel = ch;
                        ch.Pipeline.AddLast(ForServer().Build(), NewMultiplexedHandler());
                        serverChannelLatch.Signal();
                    }));
            serverChannel = await sb.BindAsync(serverAddress, TestContext.Current.CancellationToken);

            Bootstrap cb = new Bootstrap()
                    .Channel<LocalChannel>()
                    .Group(group)
                    .Handler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        ch.Pipeline.AddLast(ForClient().Build(), NewMultiplexedHandler());
                    }));
            clientChannel = await cb.ConnectAsync(serverAddress, TestContext.Current.CancellationToken);
            Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));

            CountdownEvent closeLatch = new CountdownEvent(1);
            IChannel clientChannelToClose = clientChannel;
            // CLR: the anonymous Runnable is a lambda; closeLatch.await() is Wait() and
            // close().syncUninterruptibly() blocks on the close Task, which the local channel completes inside the
            // close it runs on its own event loop (Wait returns at once, as Java's sync finds the future done).
            group.Execute(() =>
            {
                try
                {
                    closeLatch.Wait();
                    clientChannelToClose.CloseAsync().GetAwaiter().GetResult();
                }
                catch (ThreadInterruptedException e)
                {
                    logger.Error(e);
                }
            });

            Http2StreamChannelBootstrap bootstrap = new Http2StreamChannelBootstrap(clientChannel);
            TaskCompletionSource<IHttp2StreamChannel> promise = new TaskCompletionSource<IHttp2StreamChannel>();
            _ = bootstrap.OpenAsync(promise);
            Assert.False(promise.Task.IsCompleted);
            closeLatch.Signal();

            // CLR: promise.get(3, SECONDS) is the promise's Task awaited with a 3 s WaitAsync.
            await Assert.ThrowsAnyAsync<ClosedChannelException>(
                    () => promise.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        }
        finally
        {
            await SafeClose(clientChannel);
            await SafeClose(serverConnectedChannel);
            await SafeClose(serverChannel);

            if (group != null)
            {
                _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(3));
            }
        }
    }

    // CLR: the anonymous ChannelInitializer<Http2StreamChannel> is an ActionChannelInitializer<IHttp2StreamChannel>.
    private static Http2MultiplexHandler NewMultiplexedHandler()
    {
        return new Http2MultiplexHandler(new ActionChannelInitializer<IHttp2StreamChannel>(ch =>
        {
            // noop
        }));
    }

    // CLR: close().syncUninterruptibly() is awaiting the close Task, so the helper returns a Task.
    private static async Task SafeClose(IChannel channel)
    {
        if (channel != null)
        {
            try
            {
                await channel.CloseAsync();
            }
            catch (Exception e)
            {
                logger.Error(e);
            }
        }
    }

    // CLR: the @Deprecated open0(ChannelHandlerContext, Promise), whose @deprecated text names open() and
    // open(Promise), is not ported as a public member
    // (src/Netty.NET.Handler.Codec.Http2/Http2StreamChannelBootstrap.cs: its body is the private Open0 that OpenAsync
    // runs), so the test reaches the same body through the named replacement OpenAsync(promise): the mocked channel's
    // pipeline returns the mocked context for Context(typeof(Http2MultiplexHandler)), which is how open(Promise) finds
    // the context that the source passes to open0 directly, and the mocked executor reports InEventLoop(), so OpenAsync
    // runs Open0 at once, as the direct call does. new DefaultPromise<Http2StreamChannel>(mock(EventExecutor.class)) is
    // a TaskCompletionSource; promise.isDone() is Task.IsCompleted and promise.cause() Http2CodecUtil.Cause(task);
    // IllegalStateException is InvalidOperationException (Http2ChannelDuplexHandler.NewStream throws it when no
    // Http2FrameCodec was found).
    [Fact]
    public void Open0FailsPromiseOnHttp2MultiplexHandlerError()
    {
        Mock<IChannel> channel = new Mock<IChannel>();
        Http2StreamChannelBootstrap bootstrap = new Http2StreamChannelBootstrap(channel.Object);

        Http2MultiplexHandler handler = new Http2MultiplexHandler(new Mock<IChannelHandler>().Object);
        Mock<IEventExecutor> executor = new Mock<IEventExecutor>();
        executor.Setup(e => e.InEventLoop()).Returns(true);
        Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();
        ctx.Setup(c => c.Executor).Returns(executor.Object);
        ctx.Setup(c => c.Handler).Returns(handler);
        Mock<IChannelPipeline> pipeline = new Mock<IChannelPipeline>();
        pipeline.Setup(p => p.Context(typeof(Http2MultiplexHandler))).Returns(ctx.Object);
        channel.Setup(c => c.Pipeline).Returns(pipeline.Object);

        TaskCompletionSource<IHttp2StreamChannel> promise = new TaskCompletionSource<IHttp2StreamChannel>();
        _ = bootstrap.OpenAsync(promise);
        Assert.True(promise.Task.IsCompleted);
        Assert.IsAssignableFrom<InvalidOperationException>(Http2CodecUtil.Cause(promise.Task));
    }
}
