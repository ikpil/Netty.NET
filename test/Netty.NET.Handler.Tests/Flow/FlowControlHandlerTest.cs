/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Flow;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Tests.Flow;

// CLR: the static @BeforeAll init() and @AfterAll destroy() run from the class fixture BeforeAllAndAfterAll, which
// xUnit creates before the first test of the class and disposes after the last one
// (https://xunit.net/docs/shared-context, "Class Fixtures"). The Transport port's bootstrap API is asynchronous, so
// bind(...)/connect(...).syncUninterruptibly() wait for BindAsync/ConnectAsync, a test's writeAndFlush(...).sync() and
// close().syncUninterruptibly() are awaited and eventLoop().submit(callable).get() is an awaited SubmitAsync.
// CountDownLatch is CountdownEvent (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent),
// LinkedBlockingQueue is ConcurrentQueue, AtomicInteger/AtomicReference are the Common types, and
// java.util.concurrent.Exchanger, which has no BCL counterpart, is the nested Exchanger below. The anonymous handlers
// are named nested classes. readerIndex(int) is the ReaderIndex setter, refCnt() is RefCnt, EMPTY_BUFFER is
// EmptyBuffer, Arrays.asList is a List<object>, and IdleStateHandler(delayMillis, 0, 0, MILLISECONDS) takes TimeSpans.
public class FlowControlHandlerTest : IClassFixture<FlowControlHandlerTest.BeforeAllAndAfterAll>
{
    private static IEventLoopGroup GROUP;

    private static void Init()
    {
        GROUP = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
    }

    private static void Destroy()
    {
        _ = GROUP.ShutdownGracefullyAsync();
    }

    public sealed class BeforeAllAndAfterAll : IDisposable
    {
        public BeforeAllAndAfterAll()
        {
            Init();
        }

        public void Dispose()
        {
            Destroy();
        }
    }

    // CLR: CountDownLatch.countDown() does nothing once the count is zero, while CountdownEvent.Signal() throws
    // InvalidOperationException when "The current instance is already set"
    // (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent.signal); each latch is counted down by one
    // thread.
    private static void CountDown(CountdownEvent latch)
    {
        if (latch.CurrentCount > 0)
        {
            latch.Signal();
        }
    }

    /**
     * The {@link OneByteToThreeStringsDecoder} decodes this {@code byte[]} into three messages.
     */
    private static ByteBuf NewOneMessage()
    {
        return Unpooled.WrappedBuffer(new byte[]{ 1 });
    }

    private static IChannel NewServer(bool autoRead, params IChannelHandler[] handlers)
    {
        Assert.True(handlers.Length >= 1);

        ServerBootstrap serverBootstrap = new ServerBootstrap();
        serverBootstrap.Group(GROUP)
            .Channel<NioServerSocketChannel>()
            .ChildOption(ChannelOption.AUTO_READ, autoRead)
            .ChildHandler(new ServerInitializer(handlers));

        return serverBootstrap.BindAsync(0)
                .GetAwaiter().GetResult();
    }

    // CLR: the server is bound to the wildcard address. The JDK connects to the loopback address instead
    // (SocketChannelImpl.checkRemote: "if (address.isAnyLocalAddress())" returns an InetSocketAddress of
    // Net.inet4LoopbackAddress()/inet6LoopbackAddress(),
    // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/nio/ch/SocketChannelImpl.java),
    // while a CLR Socket connect to 0.0.0.0 fails on Windows with SocketException "The requested address is not valid
    // in its context" (observed in this port), so NewClient makes the same replacement.
    private static IChannel NewClient(EndPoint server)
    {
        Bootstrap bootstrap = new Bootstrap();

        bootstrap.Group(GROUP)
            .Channel<NioSocketChannel>()
            .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 1000)
            .Handler(new ClientHandler());

        IPEndPoint address = (IPEndPoint)server;
        if (address.Address.Equals(IPAddress.Any))
        {
            server = new IPEndPoint(IPAddress.Loopback, address.Port);
        }
        else if (address.Address.Equals(IPAddress.IPv6Any))
        {
            server = new IPEndPoint(IPAddress.IPv6Loopback, address.Port);
        }
        return bootstrap.ConnectAsync(server)
                .GetAwaiter().GetResult();
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly IChannelHandler[] handlers;

        internal ServerInitializer(IChannelHandler[] handlers)
        {
            this.handlers = handlers;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;
            pipeline.AddLast(new OneByteToThreeStringsDecoder());
            pipeline.AddLast(handlers);
        }
    }

    private sealed class ClientHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Assert.Fail("In this test the client is never receiving a message from the server.");
        }
    }

    /**
     * This test demonstrates the default behavior if auto reading
     * is turned on from the get-go and you're trying to turn it off
     * once you've received your first message.
     *
     * NOTE: This test waits for the client to disconnect which is
     * interpreted as the signal that all {@code byte}s have been
     * transferred to the server.
     */
    [Fact]
    public async Task TestAutoReadingOn()
    {
        CountdownEvent latch = new CountdownEvent(3);

        ChannelInboundHandlerAdapter handler = new AutoReadingOnHandler(latch);

        IChannel server = NewServer(true, handler);
        IChannel client = NewClient(server.LocalAddress);

        try
        {
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // We received three messages even through auto reading
            // was turned off after we received the first message.
            Assert.True(latch.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class AutoReadingOnHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;

        internal AutoReadingOnHandler(CountdownEvent latch)
        {
            this.latch = latch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            // We're turning off auto reading in the hope that no
            // new messages are being sent but that is not true.
            ctx.Channel.Config.SetAutoRead(false);

            CountDown(latch);
        }
    }

    /**
     * This test demonstrates the default behavior if auto reading
     * is turned off from the get-go and you're calling read() in
     * the hope that only one message will be returned.
     *
     * NOTE: This test waits for the client to disconnect which is
     * interpreted as the signal that all {@code byte}s have been
     * transferred to the server.
     */
    [Fact]
    public async Task TestAutoReadingOff()
    {
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();
        CountdownEvent latch = new CountdownEvent(3);

        ChannelInboundHandlerAdapter handler = new AutoReadingOffHandler(peerRef, latch);

        IChannel server = NewServer(false, handler);
        IChannel client = NewClient(server.LocalAddress);

        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            // Write the message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // Read the message
            peer.Read();

            // We received all three messages but hoped that only one
            // message was read because auto reading was off and we
            // invoked the read() method only once.
            Assert.True(latch.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class AutoReadingOffHandler : ChannelInboundHandlerAdapter
    {
        private readonly Exchanger<IChannel> peerRef;
        private readonly CountdownEvent latch;

        internal AutoReadingOffHandler(Exchanger<IChannel> peerRef, CountdownEvent latch)
        {
            this.peerRef = peerRef;
            this.latch = latch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            peerRef.Exchange(ctx.Channel, TimeSpan.FromSeconds(1L));
            ctx.FireChannelActive();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            CountDown(latch);
        }
    }

    /**
     * The {@link FlowControlHandler} will simply pass-through all messages
     * if auto reading is on and remains on.
     */
    [Fact]
    public async Task TestFlowAutoReadOn()
    {
        CountdownEvent latch = new CountdownEvent(3);
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();

        ChannelInboundHandlerAdapter handler = new FlowAutoReadOnHandler(peerRef, latch);

        FlowControlHandler flow = new FlowControlHandler();
        IChannel server = NewServer(true, flow, handler);
        IChannel client = NewClient(server.LocalAddress);
        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            // Write the message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // We should receive 3 messages
            Assert.True(latch.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            Assert.True(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class FlowAutoReadOnHandler : ChannelDuplexHandler
    {
        private readonly Exchanger<IChannel> peerRef;
        private readonly CountdownEvent latch;

        internal FlowAutoReadOnHandler(Exchanger<IChannel> peerRef, CountdownEvent latch)
        {
            this.peerRef = peerRef;
            this.latch = latch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            peerRef.Exchange(ctx.Channel, TimeSpan.FromSeconds(1L));
            base.ChannelActive(ctx);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            CountDown(latch);
        }
    }

    /**
     * The {@link FlowControlHandler} will pass down messages one by one
     * if {@link ChannelConfig#setAutoRead(boolean)} is being toggled.
     */
    [Fact]
    public async Task TestFlowToggleAutoRead()
    {
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();
        CountdownEvent msgRcvLatch1 = new CountdownEvent(1);
        CountdownEvent msgRcvLatch2 = new CountdownEvent(1);
        CountdownEvent msgRcvLatch3 = new CountdownEvent(1);
        CountdownEvent setAutoReadLatch1 = new CountdownEvent(1);
        CountdownEvent setAutoReadLatch2 = new CountdownEvent(1);

        ChannelInboundHandlerAdapter handler = new FlowToggleAutoReadHandler(peerRef, msgRcvLatch1, msgRcvLatch2,
                msgRcvLatch3, setAutoReadLatch1, setAutoReadLatch2);

        FlowControlHandler flow = new FlowControlHandler();
        IChannel server = NewServer(true, flow, handler);
        IChannel client = NewClient(server.LocalAddress);
        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // channelRead(1)
            Assert.True(msgRcvLatch1.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            // channelRead(2)
            peer.Config.SetAutoRead(true);
            CountDown(setAutoReadLatch1);
            Assert.True(msgRcvLatch1.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            // channelRead(3)
            peer.Config.SetAutoRead(true);
            CountDown(setAutoReadLatch2);
            Assert.True(msgRcvLatch3.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            Assert.True(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class FlowToggleAutoReadHandler : ChannelInboundHandlerAdapter
    {
        private readonly Exchanger<IChannel> peerRef;
        private readonly CountdownEvent msgRcvLatch1;
        private readonly CountdownEvent msgRcvLatch2;
        private readonly CountdownEvent msgRcvLatch3;
        private readonly CountdownEvent setAutoReadLatch1;
        private readonly CountdownEvent setAutoReadLatch2;
        private int msgRcvCount;
        private int expectedMsgCount;

        internal FlowToggleAutoReadHandler(Exchanger<IChannel> peerRef, CountdownEvent msgRcvLatch1,
                CountdownEvent msgRcvLatch2, CountdownEvent msgRcvLatch3, CountdownEvent setAutoReadLatch1,
                CountdownEvent setAutoReadLatch2)
        {
            this.peerRef = peerRef;
            this.msgRcvLatch1 = msgRcvLatch1;
            this.msgRcvLatch2 = msgRcvLatch2;
            this.msgRcvLatch3 = msgRcvLatch3;
            this.setAutoReadLatch1 = setAutoReadLatch1;
            this.setAutoReadLatch2 = setAutoReadLatch2;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            peerRef.Exchange(ctx.Channel, TimeSpan.FromSeconds(1L));
            ctx.FireChannelActive();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);

            // Disable auto reading after each message
            ctx.Channel.Config.SetAutoRead(false);

            if (msgRcvCount++ != expectedMsgCount)
            {
                return;
            }
            switch (msgRcvCount)
            {
                case 1:
                    CountDown(msgRcvLatch1);
                    if (setAutoReadLatch1.Wait(TimeSpan.FromSeconds(1L)))
                    {
                        ++expectedMsgCount;
                    }
                    break;
                case 2:
                    CountDown(msgRcvLatch2);
                    if (setAutoReadLatch2.Wait(TimeSpan.FromSeconds(1L)))
                    {
                        ++expectedMsgCount;
                    }
                    break;
                default:
                    CountDown(msgRcvLatch3);
                    break;
            }
        }
    }

    /**
     * The {@link FlowControlHandler} will pass down messages one by one
     * if auto reading is off and the user is calling {@code read()} on
     * their own.
     */
    [Fact]
    public async Task TestFlowAutoReadOff()
    {
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();
        CountdownEvent msgRcvLatch1 = new CountdownEvent(1);
        CountdownEvent msgRcvLatch2 = new CountdownEvent(2);
        CountdownEvent msgRcvLatch3 = new CountdownEvent(3);

        ChannelInboundHandlerAdapter handler = new CountingPeerHandler(peerRef, msgRcvLatch1, msgRcvLatch2,
                msgRcvLatch3);

        FlowControlHandler flow = new FlowControlHandler();
        IChannel server = NewServer(false, flow, handler);
        IChannel client = NewClient(server.LocalAddress);
        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            // Write the message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // channelRead(1)
            peer.Read();
            Assert.True(msgRcvLatch1.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            // channelRead(2)
            peer.Read();
            Assert.True(msgRcvLatch2.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            // channelRead(3)
            peer.Read();
            Assert.True(msgRcvLatch3.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            Assert.True(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    // CLR: the anonymous ChannelDuplexHandler shared by testFlowAutoReadOff and testFlowAutoReadOffAndQueueNonEmpty.
    private sealed class CountingPeerHandler : ChannelDuplexHandler
    {
        private readonly Exchanger<IChannel> peerRef;
        private readonly CountdownEvent msgRcvLatch1;
        private readonly CountdownEvent msgRcvLatch2;
        private readonly CountdownEvent msgRcvLatch3;

        internal CountingPeerHandler(Exchanger<IChannel> peerRef, CountdownEvent msgRcvLatch1,
                CountdownEvent msgRcvLatch2, CountdownEvent msgRcvLatch3)
        {
            this.peerRef = peerRef;
            this.msgRcvLatch1 = msgRcvLatch1;
            this.msgRcvLatch2 = msgRcvLatch2;
            this.msgRcvLatch3 = msgRcvLatch3;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelActive();
            peerRef.Exchange(ctx.Channel, TimeSpan.FromSeconds(1L));
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            CountDown(msgRcvLatch1);
            CountDown(msgRcvLatch2);
            CountDown(msgRcvLatch3);
        }
    }

    /**
     * The {@link FlowControlHandler} will not pass read events onto the
     * pipeline when the user is calling {@code read()} on their own if the
     * queue is not empty and auto-reading is turned off for the channel.
     */
    [Fact]
    public async Task TestFlowAutoReadOffAndQueueNonEmpty()
    {
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();
        CountdownEvent msgRcvLatch1 = new CountdownEvent(1);
        CountdownEvent msgRcvLatch2 = new CountdownEvent(2);
        CountdownEvent msgRcvLatch3 = new CountdownEvent(3);

        ChannelInboundHandlerAdapter handler = new CountingPeerHandler(peerRef, msgRcvLatch1, msgRcvLatch2,
                msgRcvLatch3);

        FlowControlHandler flow = new FlowControlHandler();
        IChannel server = NewServer(false, flow, handler);
        IChannel client = NewClient(server.LocalAddress);
        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            // Write the first message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // channelRead(1)
            peer.Read();
            Assert.True(msgRcvLatch1.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));
            Assert.False(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));

            // Write the second message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // channelRead(2)
            peer.Read();
            Assert.True(msgRcvLatch2.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            // channelRead(3)
            peer.Read();
            Assert.True(msgRcvLatch3.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            Assert.True(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TestReentranceNotCausesNPE()
    {
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();
        CountdownEvent latch = new CountdownEvent(3);
        AtomicReference<Exception> causeRef = new AtomicReference<Exception>();
        ChannelInboundHandlerAdapter handler = new ReentrantReadHandler(peerRef, latch, causeRef);

        FlowControlHandler flow = new FlowControlHandler();
        IChannel server = NewServer(false, flow, handler);
        IChannel client = NewClient(server.LocalAddress);
        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            // Write the message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // channelRead(1)
            peer.Read();
            Assert.True(latch.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));

            Assert.True(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));

            Exception cause = causeRef.Get();
            if (cause != null)
            {
                throw cause;
            }
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class ReentrantReadHandler : ChannelDuplexHandler
    {
        private readonly Exchanger<IChannel> peerRef;
        private readonly CountdownEvent latch;
        private readonly AtomicReference<Exception> causeRef;

        internal ReentrantReadHandler(Exchanger<IChannel> peerRef, CountdownEvent latch,
                AtomicReference<Exception> causeRef)
        {
            this.peerRef = peerRef;
            this.latch = latch;
            this.causeRef = causeRef;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelActive();
            peerRef.Exchange(ctx.Channel, TimeSpan.FromSeconds(1L));
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            CountDown(latch);
            ctx.Read();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            causeRef.Set(cause);
        }
    }

    [Fact]
    public void TestSwallowedReadComplete()
    {
        long delayMillis = 100;
        ConcurrentQueue<IdleStateEvent> userEvents = new ConcurrentQueue<IdleStateEvent>();
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
            new FlowControlHandler(),
            new IdleStateHandler(TimeSpan.FromMilliseconds(delayMillis), TimeSpan.Zero, TimeSpan.Zero),
            new SwallowedReadCompleteHandler(userEvents)
        );

        channel.Config.SetAutoRead(false);
        Assert.False(channel.Config.IsAutoRead());

        channel.Register();

        // Reset read timeout by some message
        Assert.True(channel.WriteInbound(Unpooled.EmptyBuffer));
        channel.FlushInbound();
        Assert.Equal(Unpooled.EmptyBuffer, channel.ReadInbound());

        // Emulate 'no more messages in NIO channel' on the next read attempt.
        channel.FlushInbound();
        Assert.Null(channel.ReadInbound());

        Thread.Sleep(TimeSpan.FromMilliseconds(delayMillis + 20L));
        channel.RunPendingTasks();
        Assert.Equal(IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT, Poll(userEvents));
        Assert.False(channel.Finish());
    }

    // CLR: Queue.poll() returns null when the queue is empty.
    private static IdleStateEvent Poll(ConcurrentQueue<IdleStateEvent> queue)
    {
        return queue.TryDequeue(out IdleStateEvent evt) ? evt : null;
    }

    private sealed class SwallowedReadCompleteHandler : ChannelInboundHandlerAdapter
    {
        private readonly ConcurrentQueue<IdleStateEvent> userEvents;

        internal SwallowedReadCompleteHandler(ConcurrentQueue<IdleStateEvent> userEvents)
        {
            this.userEvents = userEvents;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelActive();
            ctx.Read();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);
            ctx.Read();
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            ctx.FireChannelReadComplete();
            ctx.Read();
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is IdleStateEvent)
            {
                userEvents.Enqueue((IdleStateEvent)evt);
            }
            ctx.FireUserEventTriggered(evt);
        }
    }

    [Fact]
    public async Task TestRemoveFlowControl()
    {
        Exchanger<IChannel> peerRef = new Exchanger<IChannel>();

        CountdownEvent latch = new CountdownEvent(3);

        ChannelInboundHandlerAdapter handler = new FirstReadHandler(peerRef, latch);

        FlowControlHandler flow = new RemovingFlowControlHandler();
        ChannelInboundHandlerAdapter tail = new ConsumingHandler();

        IChannel server = NewServer(false /* no auto read */, flow, handler, tail);
        IChannel client = NewClient(server.LocalAddress);
        try
        {
            // The client connection on the server side
            IChannel peer = peerRef.Exchange(null, TimeSpan.FromSeconds(1L));

            // Write one message
            await client.WriteAndFlushAsync(NewOneMessage(), TestContext.Current.CancellationToken);

            // We should receive 3 messages
            Assert.True(latch.Wait(TimeSpan.FromSeconds(1L), TestContext.Current.CancellationToken));
            Assert.True(await peer.EventLoop.SubmitAsync(() => flow.IsQueueEmpty(), TestContext.Current.CancellationToken));
        }
        finally
        {
            _ = client.CloseAsync(TestContext.Current.CancellationToken);
            _ = server.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class FirstReadHandler : ChannelDuplexHandler
    {
        private readonly Exchanger<IChannel> peerRef;
        private readonly CountdownEvent latch;

        internal FirstReadHandler(Exchanger<IChannel> peerRef, CountdownEvent latch)
        {
            this.peerRef = peerRef;
            this.latch = latch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            peerRef.Exchange(ctx.Channel, TimeSpan.FromSeconds(1L));
            //do the first read
            ctx.Read();
            base.ChannelActive(ctx);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            CountDown(latch);
            base.ChannelRead(ctx, msg);
        }
    }

    private sealed class RemovingFlowControlHandler : FlowControlHandler
    {
        private int num;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            base.ChannelRead(ctx, msg);
            ++num;
            if (num >= 3)
            {
                //We have received 3 messages. Remove myself later
                IChannelHandler handler = this;
                ctx.Channel.EventLoop.Execute(() => ctx.Pipeline.Remove(handler));
            }
        }
    }

    private sealed class ConsumingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            //consume this msg
            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void TestCompletingReadWithNonEmptyQueue()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                false, false,
                upstream,
                new FlowControlHandler(),
                new ReadCountingHandler(reads, readCompletes));

        channel.Config.SetAutoRead(false);
        channel.Register();

        Assert.False(channel.WriteInbound("msg1", "msg2"));
        Assert.Equal(0, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        channel.Read();
        Assert.Equal(1, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.Read();
        Assert.Equal(2, reads.Get());
        Assert.Equal(2, readCompletes.Get());

        // Every read() was satisfied straight from the queue, so none was forwarded upstream.
        Assert.Equal(0, upstream.reads.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter that counts reads and read completions, shared by several tests.
    private sealed class ReadCountingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger reads;
        private readonly AtomicInteger readCompletes;

        internal ReadCountingHandler(AtomicInteger reads, AtomicInteger readCompletes)
        {
            this.reads = reads;
            this.readCompletes = readCompletes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            reads.IncrementAndGet();
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            readCompletes.IncrementAndGet();
        }
    }

    [Fact]
    public async Task TestSuppressingUpstreamReadCompletes()
    {
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                false, false,
                new FlowControlHandler(),
                new ReadCountingHandler(reads, readCompletes));

        channel.Config.SetAutoRead(false);
        channel.Register();

        Assert.Equal(0, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        channel.FlushInbound();
        channel.FlushInbound();
        channel.FlushInbound();

        Assert.Equal(0, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        channel.Read();
        await channel.WriteOneInboundAsync("msg");
        Assert.Equal(1, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.FlushInbound();
        channel.FlushInbound();
        Assert.Equal(1, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.Read();
        channel.FlushInbound();

        Assert.Equal(1, reads.Get());
        Assert.Equal(2, readCompletes.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestEmptyRead()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                false, false,
                upstream,
                new FlowControlHandler(),
                new ReadCountingHandler(reads, readCompletes));

        channel.Config.SetAutoRead(false);
        channel.Register();

        // Downstream issues a read() but upstream has no data and only fires channelReadComplete.
        // FlowControlHandler must forward that channelReadComplete to satisfy the outstanding read.
        channel.Read();
        channel.FlushInbound();

        Assert.Equal(0, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        // The empty read() could not be satisfied from the queue, so it was forwarded upstream exactly once.
        Assert.Equal(1, upstream.reads.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestMultipleReadsOnEmptyQueue()
    {
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                false, false,
                new FlowControlHandler(),
                new ReadCountingHandler(reads, readCompletes));
        channel.Config.SetAutoRead(false);
        channel.Register();

        channel.Read();
        channel.Read();
        channel.Read();

        await channel.WriteOneInboundAsync("msg1");

        Assert.Equal(1, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        channel.FlushInbound();

        Assert.Equal(1, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.Read();
        channel.Read();
        channel.Read();

        // empty read
        channel.FlushInbound();

        Assert.Equal(1, reads.Get());
        Assert.Equal(2, readCompletes.Get());

        // quick check that internal state is not broken
        await channel.WriteOneInboundAsync("msg2");
        channel.FlushInbound();

        Assert.Equal(1, reads.Get());
        Assert.Equal(2, readCompletes.Get());

        channel.Read();

        Assert.Equal(2, reads.Get());
        Assert.Equal(3, readCompletes.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestCompleteReadOnUpstreamCompleteWhenAutoReadOn()
    {
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                false, false,
                new FlowControlHandler(),
                new ReadCountingHandler(reads, readCompletes));

        Assert.True(channel.Config.IsAutoRead());
        channel.Register();

        await channel.WriteOneInboundAsync("msg1");
        await channel.WriteOneInboundAsync("msg2");
        await channel.WriteOneInboundAsync("msg3");

        // All three messages must arrive before channelReadComplete signals end-of-batch.
        Assert.Equal(3, reads.Get());
        // As auto-read is on, FlowControlHandler should not fire a channelReadComplete on its own but should wait
        // for upstream to fire it.
        Assert.Equal(0, readCompletes.Get());

        // Upstream now fires channelReadComplete and FlowControlHandler should pass it through.
        channel.FlushInbound();

        Assert.Equal(3, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestSatisfyPendingReadsAfterDisablingAutoRead()
    {
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(
                false, false,
                new FlowControlHandler(),
                new ReadCountingHandler(reads, readCompletes));

        channel.Config.SetAutoRead(false);
        channel.Register();

        // We issue two reads with auto-read off. We expect at least two messages to be delivered, even when we are
        // going to turn off auto-read in a moment.
        channel.Read();
        channel.Read();
        channel.Config.SetAutoRead(true);

        // We got the first message with auto-read on. It immediately satisfies the first read.
        await channel.WriteOneInboundAsync("msg1");

        Assert.Equal(1, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        channel.Config.SetAutoRead(false);
        channel.Config.SetAutoRead(true);
        // In the end auto-read is off, and we have one remaining unsatisfied read.
        channel.Config.SetAutoRead(false);

        // sanity check: nothing should happen.
        Assert.Equal(1, reads.Get());
        Assert.Equal(0, readCompletes.Get());

        // The second message is delivered right away, satisfying the second read, and completing the batch.
        await channel.WriteOneInboundAsync("msg2");

        Assert.Equal(2, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        // The third message is queued but not delivered as autoRead is off, and we have no unsatisfied reads anymore.
        await channel.WriteOneInboundAsync("msg3");

        Assert.Equal(2, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        // Upstream fires channelReadComplete.
        channel.FlushInbound();

        // As autoRead is off, FlowControlHandler is the one determining the end of the read cycle, not upstream.
        // It ignores the channelReadComplete.
        Assert.Equal(2, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.Config.SetAutoRead(true);

        // The third message is dequeued and delivered.
        Assert.Equal(3, reads.Get());
        Assert.Equal(1, readCompletes.Get());

        channel.FlushInbound();

        Assert.Equal(3, reads.Get());
        Assert.Equal(2, readCompletes.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestAutoReadDisabledDuringDequeueStopsDelivery()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
                upstream,
                new FlowControlHandler(),
                new AutoReadDisablingHandler(reads, readCompletes));
        channel.Config.SetAutoRead(false);
        channel.Register();

        // Begin with auto-read on while the queue is empty: that forwards a single read upstream and delivers
        // nothing.
        channel.Config.SetAutoRead(true);
        Assert.Equal(0, reads.Get());
        Assert.Equal(1, upstream.reads.Get());

        // Messages now arrive with auto-read on. The handler disables auto-read while processing the first, so
        // messages 2..5 stay queued: delivery stops after one and the cycle completes once (from channelRead).
        channel.WriteInbound("1", "2", "3", "4", "5");
        Assert.Equal(1, reads.Get());
        Assert.Equal(1, readCompletes.Get());
        Assert.Equal(1, upstream.reads.Get());

        // A trailing upstream channelReadComplete (auto-read off, no read outstanding) is dropped.
        channel.FlushInbound();
        Assert.Equal(1, readCompletes.Get());

        // Resume: re-enabling auto-read starts draining the four queued messages, but the handler disables it
        // again while processing the first. The dequeue must observe the renewed setAutoRead(false), release
        // exactly one more, and complete once (from read()). The message was served from the queue, so nothing
        // is read further upstream.
        channel.Config.SetAutoRead(true);
        Assert.Equal(2, reads.Get());
        Assert.Equal(2, readCompletes.Get());
        Assert.Equal(1, upstream.reads.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class AutoReadDisablingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger reads;
        private readonly AtomicInteger readCompletes;

        internal AutoReadDisablingHandler(AtomicInteger reads, AtomicInteger readCompletes)
        {
            this.reads = reads;
            this.readCompletes = readCompletes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            reads.IncrementAndGet();
            ctx.Channel.Config.SetAutoRead(false);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            readCompletes.IncrementAndGet();
        }
    }

    [Fact]
    public void TestReentrantReadIsSatisfiedFromQueue()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        AtomicInteger reads = new AtomicInteger();
        AtomicInteger readCompletes = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
                upstream,
                new FlowControlHandler(),
                new FirstMessageReadingHandler(reads, readCompletes));
        channel.Config.SetAutoRead(false);
        channel.Register();
        channel.WriteInbound("1", "2");

        channel.Read();

        Assert.Equal(2, reads.Get());
        Assert.Equal(0, upstream.reads.Get());
        Assert.Equal(1, readCompletes.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class FirstMessageReadingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger reads;
        private readonly AtomicInteger readCompletes;

        internal FirstMessageReadingHandler(AtomicInteger reads, AtomicInteger readCompletes)
        {
            this.reads = reads;
            this.readCompletes = readCompletes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (reads.IncrementAndGet() == 1)
            {
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            readCompletes.IncrementAndGet();
        }
    }

    [Fact]
    public void TestAutoReadEnabledDuringDequeueDrainsRemaining()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        AtomicInteger reads = new AtomicInteger();
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
                upstream,
                new FlowControlHandler(),
                new AutoReadEnablingHandler(reads));
        channel.Config.SetAutoRead(false);
        channel.Register();

        // Auto-read off: all five messages are held in the queue, nothing read upstream.
        channel.WriteInbound("1", "2", "3", "4", "5");
        Assert.Equal(0, reads.Get());
        Assert.Equal(0, upstream.reads.Get());

        // A single read() delivers the first message; the handler re-enables auto-read from inside channelRead,
        // which drains the whole remaining queue. With the queue empty and auto-read on, FlowControlHandler
        // then reads further upstream twice: once for the transparent resume, once for the read() that
        // consumed the queued message.
        channel.Read();
        Assert.Equal(5, reads.Get());
        Assert.Equal(2, upstream.reads.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class AutoReadEnablingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger reads;

        internal AutoReadEnablingHandler(AtomicInteger reads)
        {
            this.reads = reads;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            reads.IncrementAndGet();
            // Resume the connection while "processing" each released message.
            ctx.Channel.Config.SetAutoRead(true);
        }
    }

    [Fact]
    public void TestHandlerRemovedFlushesQueuedMessages()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        List<object> received = new List<object>();
        AtomicInteger readCompletes = new AtomicInteger();
        FlowControlHandler flow = new FlowControlHandler();
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
                upstream,
                flow,
                new ReceivingHandler(received, readCompletes));
        channel.Config.SetAutoRead(false);
        channel.Register();

        // With auto-read off and no read(), all five messages stay queued in the handler.
        channel.WriteInbound("1", "2", "3", "4", "5");
        Assert.Empty(received);
        Assert.Equal(0, readCompletes.Get());

        // Removing the handler flushes the whole queue downstream, in order, then completes the batch once.
        channel.Pipeline.Remove(flow);
        Assert.Equal(new List<object> { "1", "2", "3", "4", "5" }, received);
        Assert.Equal(1, readCompletes.Get());
        Assert.True(flow.IsQueueEmpty());

        // The flush happens locally on removal; nothing is read upstream.
        Assert.Equal(0, upstream.reads.Get());

        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class ReceivingHandler : ChannelInboundHandlerAdapter
    {
        private readonly List<object> received;
        private readonly AtomicInteger readCompletes;

        internal ReceivingHandler(List<object> received, AtomicInteger readCompletes)
        {
            this.received = received;
            this.readCompletes = readCompletes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            received.Add(msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            readCompletes.IncrementAndGet();
        }
    }

    [Fact]
    public async Task TestChannelInactiveReleasesQueuedMessages()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        FlowControlHandler flow = new FlowControlHandler();
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
                upstream, flow, new ChannelInboundHandlerAdapter());
        channel.Config.SetAutoRead(false);
        channel.Register();

        ByteBuf msg1 = Unpooled.Buffer().WriteByte(1);
        ByteBuf msg2 = Unpooled.Buffer().WriteByte(2);

        // Auto-read off: the buffers are held in the handler's queue, not delivered downstream.
        channel.WriteInbound(msg1, msg2);
        Assert.False(flow.IsQueueEmpty());
        Assert.Equal(1, msg1.RefCnt);
        Assert.Equal(1, msg2.RefCnt);

        // Closing fires channelInactive, which destroys the queue and releases the held buffers.
        await channel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, msg1.RefCnt);
        Assert.Equal(0, msg2.RefCnt);
        Assert.True(flow.IsQueueEmpty());

        // Nothing was ever read upstream.
        Assert.Equal(0, upstream.reads.Get());
    }

    [Fact]
    public async Task TestReleaseMessagesFalseDoesNotReleaseQueuedMessages()
    {
        UpstreamReadCounter upstream = new UpstreamReadCounter();
        FlowControlHandler flow = new FlowControlHandler(false);
        EmbeddedChannel channel = new EmbeddedChannel(false, false,
                upstream, flow, new ChannelInboundHandlerAdapter());
        channel.Config.SetAutoRead(false);
        channel.Register();

        ByteBuf msg1 = Unpooled.Buffer().WriteByte(1);
        ByteBuf msg2 = Unpooled.Buffer().WriteByte(2);

        channel.WriteInbound(msg1, msg2);
        Assert.False(flow.IsQueueEmpty());

        // releaseMessages == false: destroy() discards the queue but must not release the buffers.
        await channel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, msg1.RefCnt);
        Assert.Equal(1, msg2.RefCnt);
        Assert.True(flow.IsQueueEmpty());

        // Nothing was ever read upstream.
        Assert.Equal(0, upstream.reads.Get());

        msg1.Release();
        msg2.Release();
    }

    /**
     * This is a fictional message decoder. It decodes each {@code byte}
     * into three strings.
     */
    private sealed class OneByteToThreeStringsDecoder : ByteToMessageDecoder
    {
        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            for (int i = 0; i < @in.ReadableBytes; i++)
            {
                @out.Add("1");
                @out.Add("2");
                @out.Add("3");
            }
            @in.ReaderIndex = @in.ReadableBytes;
        }
    }

    /**
     * Counts the {@code read()} events {@link FlowControlHandler} forwards upstream. Placed at the head side
     * of the handler.
     */
    private sealed class UpstreamReadCounter : ChannelDuplexHandler
    {
        internal readonly AtomicInteger reads = new AtomicInteger();

        public override void Read(IChannelHandlerContext ctx)
        {
            reads.IncrementAndGet();
            base.Read(ctx);
        }
    }

    // CLR: java.util.concurrent.Exchanger, "A synchronization point at which threads can pair and swap elements within
    // pairs" whose timed exchange throws TimeoutException "if the specified waiting time elapses before another thread
    // enters the exchange" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Exchanger.html),
    // has no BCL counterpart; this two-party version is a Monitor wait over a single slot. The tests exchange once per
    // instance, and the interrupt case has no counterpart.
    private sealed class Exchanger<V> where V : class
    {
        private readonly object gate = new object();
        private V offered;
        private bool waiting;
        private V answer;
        private bool answered;

        internal V Exchange(V x, TimeSpan timeout)
        {
            lock (gate)
            {
                if (waiting)
                {
                    V other = offered;
                    offered = null;
                    waiting = false;
                    answer = x;
                    answered = true;
                    Monitor.PulseAll(gate);
                    return other;
                }
                offered = x;
                waiting = true;
                long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
                while (!answered)
                {
                    long remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0 || !Monitor.Wait(gate, TimeSpan.FromMilliseconds(remaining)))
                    {
                        if (!answered)
                        {
                            offered = null;
                            waiting = false;
                            throw new System.TimeoutException();
                        }
                    }
                }
                V result = answer;
                answer = null;
                answered = false;
                return result;
            }
        }
    }
}
