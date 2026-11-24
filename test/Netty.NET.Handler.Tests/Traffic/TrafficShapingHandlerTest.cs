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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Traffic;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Handler.Tests.Traffic;

// CLR: Executors.newSingleThreadScheduledExecutor() is a DefaultEventExecutor, the single-thread executor whose
// deadline scheduling serves the TrafficCounter (ScheduledExecutorService is IEventExecutorGroup), and shutdown() is
// ShutdownGracefullyAsync(). The static @AfterAll destroy() runs from the class fixture AfterAll, which xUnit disposes
// after the last test of the class (https://xunit.net/docs/shared-context, "Class Fixtures"). The ChannelPromise of
// writeOneOutbound(holder, promise) is the Task that WriteOneOutboundAsync returns (the Transport port has no promise
// overload); isDone() is IsCompleted and cause() the Task's exception. The Transport port's bootstrap and channel
// operations are asynchronous: bind/connect/close().sync() and await() wait for their Tasks and eventLoop().submit()
// is SubmitAsync. The anonymous ChannelInitializers and the echo handler are named nested classes.
// "foo".getBytes(CharsetUtil.UTF_8) is Encoding.UTF8.GetBytes, Attribute<Runnable> is IAttribute<Action>, and
// LocalAddress(TrafficShapingHandlerTest.class) is LocalAddress(typeof(TrafficShapingHandlerTest)).
public class TrafficShapingHandlerTest : IClassFixture<TrafficShapingHandlerTest.AfterAll>
{

    private const long READ_LIMIT_BYTES_PER_SECOND = 1;
    private const long WRITE_LIMIT_BYTES_PER_SECOND = 1;
    private static readonly IEventExecutorGroup SES = new DefaultEventExecutor();
    private static readonly IEventLoopGroup GROUP = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

    private static void Destroy()
    {
        _ = GROUP.ShutdownGracefullyAsync();
        _ = SES.ShutdownGracefullyAsync();
    }

    public sealed class AfterAll : IDisposable
    {
        public void Dispose()
        {
            Destroy();
        }
    }

    [Fact]
    public async Task TestHandlerRemove()
    {
        await TestHandlerRemove0(new ChannelTrafficShapingHandler(0, READ_LIMIT_BYTES_PER_SECOND));
        GlobalTrafficShapingHandler trafficHandler1 =
                new GlobalTrafficShapingHandler(SES, 0, READ_LIMIT_BYTES_PER_SECOND);
        try
        {
            await TestHandlerRemove0(trafficHandler1);
        }
        finally
        {
            trafficHandler1.Release();
        }
        GlobalChannelTrafficShapingHandler trafficHandler2 =
                new GlobalChannelTrafficShapingHandler(SES, 0,
                        READ_LIMIT_BYTES_PER_SECOND, 0, READ_LIMIT_BYTES_PER_SECOND);
        try
        {
            await TestHandlerRemove0(trafficHandler2);
        }
        finally
        {
            trafficHandler2.Release();
        }
    }

    [Fact]
    public void TestQueuedWritesReleasedAndFailedOnClose()
    {
        TestQueuedWritesReleasedAndFailedOnClose0(new ChannelTrafficShapingHandler(
                WRITE_LIMIT_BYTES_PER_SECOND, 0, 0));

        GlobalTrafficShapingHandler trafficHandler1 =
                new GlobalTrafficShapingHandler(SES, WRITE_LIMIT_BYTES_PER_SECOND, 0, 0);
        try
        {
            TestQueuedWritesReleasedAndFailedOnClose0(trafficHandler1);
        }
        finally
        {
            trafficHandler1.Release();
        }

        GlobalChannelTrafficShapingHandler trafficHandler2 =
                new GlobalChannelTrafficShapingHandler(SES, WRITE_LIMIT_BYTES_PER_SECOND, 0,
                        WRITE_LIMIT_BYTES_PER_SECOND, 0, 0);
        try
        {
            TestQueuedWritesReleasedAndFailedOnClose0(trafficHandler2);
        }
        finally
        {
            trafficHandler2.Release();
        }
    }

    private static void TestQueuedWritesReleasedAndFailedOnClose0(AbstractTrafficShapingHandler trafficHandler)
    {
        EmbeddedChannel ch = new EmbeddedChannel(trafficHandler);
        IByteBufHolder holder = new DefaultByteBufHolder(Unpooled.Buffer(64).WriteZero(64));
        Task promise = ch.WriteOneOutboundAsync(holder);

        Assert.False(promise.IsCompleted);
        Assert.Null(ch.ReadOutbound());
        Assert.Equal(1, holder.RefCnt);

        ch.CloseAsync().GetAwaiter().GetResult();
        Assert.Equal(0, holder.RefCnt);
        Assert.True(promise.IsCompleted);
        Assert.IsAssignableFrom<ClosedChannelException>(promise.Exception?.InnerException);
        Assert.False(ch.FinishAndReleaseAll());
    }

    private async Task TestHandlerRemove0(AbstractTrafficShapingHandler trafficHandler)
    {
        IChannel svrChannel = null;
        IChannel ch = null;
        try
        {
            ServerBootstrap serverBootstrap = new ServerBootstrap();
            serverBootstrap.Channel<LocalServerChannel>().Group(GROUP)
                    .ChildHandler(new EchoInitializer());
            LocalAddress svrAddr = new LocalAddress(typeof(TrafficShapingHandlerTest));
            svrChannel = await serverBootstrap.BindAsync(svrAddr);
            Bootstrap bootstrap = new Bootstrap();
            bootstrap.Channel<LocalChannel>().Group(GROUP).Handler(new TrafficShapingInitializer(trafficHandler));
            ch = await bootstrap.ConnectAsync(svrAddr);
            IAttribute<Action> attr = ch.Attr(AbstractTrafficShapingHandler.REOPEN_TASK);
            Assert.Null(attr.Get());
            _ = ch.WriteAndFlushAsync(Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("foo")));
            await ch.WriteAndFlushAsync(Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("bar")));
            Assert.NotNull(attr.Get());
            IChannel clientChannel = ch;
            await ch.EventLoop.SubmitAsync(() => clientChannel.Pipeline.Remove("traffic-shaping"));
            //the attribute--reopen task must be released.
            Assert.Null(attr.Get());
        }
        finally
        {
            if (ch != null)
            {
                await ch.CloseAsync();
            }
            if (svrChannel != null)
            {
                await svrChannel.CloseAsync();
            }
        }
    }

    private sealed class EchoInitializer : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(new EchoHandler());
        }
    }

    private sealed class EchoHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _ = ctx.WriteAndFlushAsync(msg);
        }
    }

    private sealed class TrafficShapingInitializer : ChannelInitializer<IChannel>
    {
        private readonly AbstractTrafficShapingHandler trafficHandler;

        internal TrafficShapingInitializer(AbstractTrafficShapingHandler trafficHandler)
        {
            this.trafficHandler = trafficHandler;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast("traffic-shaping", trafficHandler);
        }
    }

}
