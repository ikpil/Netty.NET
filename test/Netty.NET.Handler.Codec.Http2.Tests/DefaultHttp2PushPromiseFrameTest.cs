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

using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @BeforeEach setup() and @AfterEach shutdown() keep their names and run from IAsyncLifetime.InitializeAsync and
// DisposeAsync, because they wait for the bind, the connect and the graceful shutdown (sync()/syncUninterruptibly() are
// awaits of the Transport's Tasks, docs/codec-http-porting.md, Core test notes; Http2ConnectionRoundtripTest). The
// event loop group, bootstraps and NIO channels are the Transport's MultiThreadIoEventLoopGroup, NioIoHandler,
// ServerBootstrap/Bootstrap and NioServerSocketChannel/NioSocketChannel (src/Netty.NET.Transport); bind(NetUtil.LOCALHOST,
// 0) is ServerBootstrap.BindAsync(IPAddress, int) with Common's NetUtil.LOCALHOST, and the client connects to the
// server's bound address as in Java. SocketChannel is ISocketChannel. ConcurrentHashMap<Integer, String> is
// ConcurrentDictionary<int, string> (put is the indexer, get a TryGetValue that yields null when absent) and
// CountDownLatch is CountdownEvent (docs/codec-http-porting.md, Shared CLR adaptations). ctx.writeAndFlush(...)
// .addListener(l) is Http2CodecUtil.AddListener (the DefaultPromise listener timing on the event loop, the mapping of
// the base group); a write whose future nobody reads discards the Task. Java Strings passed as CharSequence are
// Http2TestUtil.Of (StringCharSequence); String.getBytes() is Encoding.Default.GetBytes (docs/codec-http-porting.md,
// Shared CLR adaptations: "String.getBytes() in the tests (default charset) is Encoding.Default") and
// CharsetUtil.UTF_8 is Encoding.UTF8 (src/Netty.NET.Common/CharsetUtil.cs). The handlers' assertions run on the event
// loop, where a failure goes to the pipeline's exceptionCaught, as in Java; the test checks only that the exchange runs.
// The inner classes ServerHandler and ClientHandler take the test or nothing through their constructors; the anonymous
// ChannelInitializers are named nested classes.
public class DefaultHttp2PushPromiseFrameTest : IAsyncLifetime
{

    private readonly IEventLoopGroup eventLoopGroup = new MultiThreadIoEventLoopGroup(2, NioIoHandler.NewFactory());
    private readonly ClientHandler clientHandler = new ClientHandler();
    private readonly ConcurrentDictionary<int, string> contentMap = new ConcurrentDictionary<int, string>();

    public async ValueTask InitializeAsync()
    {
        await Setup();
    }

    private async Task Setup()
    {
        ServerBootstrap serverBootstrap = new ServerBootstrap()
                .Group(eventLoopGroup)
                .Channel<NioServerSocketChannel>()
                .ChildHandler(new ServerInitializer(this));

        IChannel channel = await serverBootstrap.BindAsync(NetUtil.LOCALHOST, 0);

        Bootstrap bootstrap = new Bootstrap()
                .Group(eventLoopGroup)
                .Channel<NioSocketChannel>()
                .Handler(new ClientInitializer(clientHandler));

         await bootstrap.ConnectAsync(channel.LocalAddress);
    }

    // CLR: the anonymous ChannelInitializer<SocketChannel> of the server bootstrap.
    private sealed class ServerInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly DefaultHttp2PushPromiseFrameTest test;

        internal ServerInitializer(DefaultHttp2PushPromiseFrameTest test)
        {
            this.test = test;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;

            Http2FrameCodec frameCodec = Http2FrameCodecBuilder.ForServer()
                    .AutoAckSettingsFrame(true)
                    .AutoAckPingFrame(true)
                    .Build();

            pipeline.AddLast(frameCodec);
            pipeline.AddLast(new ServerHandler(test));
        }
    }

    // CLR: the anonymous ChannelInitializer<SocketChannel> of the client bootstrap.
    private sealed class ClientInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly ClientHandler clientHandler;

        internal ClientInitializer(ClientHandler clientHandler)
        {
            this.clientHandler = clientHandler;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;

            Http2FrameCodec frameCodec = Http2FrameCodecBuilder.ForClient()
                    .AutoAckSettingsFrame(true)
                    .AutoAckPingFrame(true)
                    .InitialSettings(Http2Settings.DefaultSettings().PushEnabled(true))
                    .Build();

            pipeline.AddLast(frameCodec);
            pipeline.AddLast(clientHandler);
        }
    }

    [Fact]
    public void Send()
    {
        clientHandler.Write();
    }

    public async ValueTask DisposeAsync()
    {
        await Shutdown();
    }

    private async Task Shutdown()
    {
        await eventLoopGroup.ShutdownGracefullyAsync();
    }

    private sealed class ServerHandler : Http2ChannelDuplexHandler
    {
        private readonly DefaultHttp2PushPromiseFrameTest test;

        internal ServerHandler(DefaultHttp2PushPromiseFrameTest test)
        {
            this.test = test;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {

            if (msg is IHttp2HeadersFrame)
            {
                IHttp2HeadersFrame receivedFrame = (IHttp2HeadersFrame) msg;

                IHttp2Headers pushRequestHeaders = new DefaultHttp2Headers();
                pushRequestHeaders.Path(Of("/meow"))
                        .Method(Of("GET"))
                        .Scheme(Of("https"))
                        .Authority(Of("localhost:5555"));

                // Write PUSH_PROMISE request headers
                IHttp2FrameStream newPushFrameStream = NewStream();
                IHttp2PushPromiseFrame pushPromiseFrame = new DefaultHttp2PushPromiseFrame(pushRequestHeaders);
                pushPromiseFrame.Stream(receivedFrame.Stream());
                pushPromiseFrame.PushStream(newPushFrameStream);
                AddListener(ctx, ctx.WriteAndFlushAsync(pushPromiseFrame), future =>
                {
                    test.contentMap[newPushFrameStream.Id()] = "Meow, I am Pushed via HTTP/2";

                    // Write headers for actual request
                    IHttp2Headers http2Headers = new DefaultHttp2Headers();
                    http2Headers.Status(Of("200"));
                    http2Headers.Add(Of("push"), Of("false"));
                    IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(http2Headers, false);
                    headersFrame.Stream(receivedFrame.Stream());
                    Task channelFuture = ctx.WriteAndFlushAsync(headersFrame);

                    // Write Data of actual request
                    AddListener(ctx, channelFuture, f =>
                    {
                        IHttp2DataFrame dataFrame = new DefaultHttp2DataFrame(
                                Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Meow")), true);
                        dataFrame.Stream(receivedFrame.Stream());
                        _ = ctx.WriteAndFlushAsync(dataFrame);
                    });
                });
            }
            else if (msg is IHttp2PriorityFrame)
            {
                IHttp2PriorityFrame priorityFrame = (IHttp2PriorityFrame) msg;
                test.contentMap.TryGetValue(priorityFrame.Stream().Id(), out string content);

                if (content == null)
                {
                    _ = ctx.WriteAndFlushAsync(new DefaultHttp2GoAwayFrame(Http2Error.REFUSED_STREAM));
                    return;
                }

                // Write headers for Priority request
                IHttp2Headers http2Headers = new DefaultHttp2Headers();
                http2Headers.Status(Of("200"));
                http2Headers.Add(Of("push"), Of("true"));
                IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(http2Headers, false);
                headersFrame.Stream(priorityFrame.Stream());
                _ = ctx.WriteAndFlushAsync(headersFrame);

                // Write Data of Priority request
                IHttp2DataFrame dataFrame = new DefaultHttp2DataFrame(
                        Unpooled.WrappedBuffer(Encoding.Default.GetBytes(content)), true);
                dataFrame.Stream(priorityFrame.Stream());
                _ = ctx.WriteAndFlushAsync(dataFrame);
            }
        }
    }

    private sealed class ClientHandler : Http2ChannelDuplexHandler
    {

        private readonly CountdownEvent latch = new CountdownEvent(1);
        private volatile IChannelHandlerContext ctx;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            this.ctx = ctx;
            latch.Signal();
        }

        internal void Write()
        {
            latch.Wait();
            IHttp2Headers http2Headers = new DefaultHttp2Headers();
            http2Headers.Path(Of("/"))
                    .Authority(Of("localhost"))
                    .Method(Of("GET"))
                    .Scheme(Of("https"));

            IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(http2Headers, true);
            headersFrame.Stream(NewStream());
            _ = ctx.WriteAndFlushAsync(headersFrame);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {

            if (msg is IHttp2PushPromiseFrame)
            {
                IHttp2PushPromiseFrame pushPromiseFrame = (IHttp2PushPromiseFrame) msg;

                Assert.Equal("/meow", pushPromiseFrame.Http2Headers().Path().ToString());
                Assert.Equal("GET", pushPromiseFrame.Http2Headers().Method().ToString());
                Assert.Equal("https", pushPromiseFrame.Http2Headers().Scheme().ToString());
                Assert.Equal("localhost:5555", pushPromiseFrame.Http2Headers().Authority().ToString());

                IHttp2PriorityFrame priorityFrame = new DefaultHttp2PriorityFrame(pushPromiseFrame.Stream().Id(),
                        Http2CodecUtil.DEFAULT_PRIORITY_WEIGHT, true);
                priorityFrame.Stream(pushPromiseFrame.PushStream());
                _ = ctx.WriteAndFlushAsync(priorityFrame);
            }
            else if (msg is IHttp2HeadersFrame)
            {
                IHttp2HeadersFrame headersFrame = (IHttp2HeadersFrame) msg;

                if (headersFrame.Stream().Id() == 3)
                {
                    Assert.Equal("200", headersFrame.Headers().Status().ToString());
                    Assert.Equal("false", headersFrame.Headers().Get(Of("push")).ToString());
                }
                else if (headersFrame.Stream().Id() == 2)
                {
                    Assert.Equal("200", headersFrame.Headers().Status().ToString());
                    Assert.Equal("true", headersFrame.Headers().Get(Of("push")).ToString());
                }
                else
                {
                    _ = ctx.WriteAndFlushAsync(new DefaultHttp2GoAwayFrame(Http2Error.REFUSED_STREAM));
                }
            }
            else if (msg is IHttp2DataFrame)
            {
                IHttp2DataFrame dataFrame = (IHttp2DataFrame) msg;

                try
                {
                    if (dataFrame.Stream().Id() == 3)
                    {
                        Assert.Equal("Meow", dataFrame.Content.ToString(Encoding.UTF8));
                    }
                    else if (dataFrame.Stream().Id() == 2)
                    {
                        Assert.Equal("Meow, I am Pushed via HTTP/2", dataFrame.Content.ToString(Encoding.UTF8));
                    }
                    else
                    {
                        _ = ctx.WriteAndFlushAsync(new DefaultHttp2GoAwayFrame(Http2Error.REFUSED_STREAM));
                    }
                }
                finally
                {
                    ReferenceCountUtil.Release(dataFrame);
                }
            }
        }
    }
}
