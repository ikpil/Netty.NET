/*
 * Copyright 2026 The Netty Project
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
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the package-private class and enum are public (xUnit runs public test classes, and a theory parameter type must
// be as accessible as the method, CS0051); @ParameterizedTest @EnumSource(OpenMode.class) is a [Theory] with one
// InlineData row per constant. The event loop group, bootstraps and NIO channels are the Transport's
// MultiThreadIoEventLoopGroup, NioIoHandler, ServerBootstrap/Bootstrap and NioServerSocketChannel/NioSocketChannel
// (src/Netty.NET.Transport); bind(NetUtil.LOCALHOST, 0).sync().channel() is the await of
// ServerBootstrap.BindAsync(IPAddress, int), and sync()/syncUninterruptibly() are awaits (docs/codec-http-porting.md,
// Core test notes). Java's Bootstrap.connect() returns a ChannelFuture whose channel() exists before the connect
// completes; the Transport's Bootstrap.ConnectAsync() returns a Task<IChannel> that yields the channel only when the
// connect completes (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs). The test needs the channel first (the
// Http2StreamChannelBootstrap is built on it before the connect completes), so connect() is the two steps it consists
// of in Java (initAndRegister(), then channel.connect(remoteAddress, promise) in doConnect): Bootstrap.RegisterAsync()
// gives the registered channel, and cf is the Task of channel.ConnectAsync(remoteAddress), the channel's connect promise
// that Java's connect() returns; the address of remoteAddress(...) is passed to it. ChannelFuture/Future listeners
// (addListener) are continuations on Common's EventExecutorTaskScheduler over the channel's event loop, the
// DefaultPromise.notifyListeners() timing (owner decision 2026-11-24; docs/codec-http-porting.md, Listener timing):
// cf completes on the event loop (Http2ConnectionHandler.ConnectAsync completes its promise once the preface is sent),
// so the Listener mode opens the stream on the event loop in the connect notification, as Java's listener does
// (measured: InEventLoop() is true in that continuation). GenericFutureListener<Future<Http2StreamChannel>> is
// the nested listener's OperationComplete(Task<IHttp2StreamChannel>) and getNow() the result of a successful Task,
// otherwise null. CompletableFuture<Http2HeadersFrame> is a TaskCompletionSource<IHttp2HeadersFrame> created with
// RunContinuationsAsynchronously ("Forces continuations added to the current task to be executed asynchronously",
// https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcreationoptions, as cited in
// src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs), so the test does not resume on the event loop that
// completes it, as Java's get() waits on the test thread; complete/completeExceptionally are TrySetResult/
// TrySetException and get(5, SECONDS) is WaitAsync(5 s). eventLoop().submit(Runnable) is the Common SubmitAsync
// extension (src/Netty.NET.Common/Concurrent/EventExecutorExtensions.cs). throw new AssertionError() is
// UnreachableException (docs/codec-http-porting.md, Shared CLR adaptations). Java Strings passed as CharSequence are
// Http2TestUtil.Of, and copiedBuffer(s, StandardCharsets.UTF_8) is Unpooled.CopiedBuffer(s, Encoding.UTF8)
// (docs/codec-http-porting.md, Shared CLR adaptations). SocketChannel is ISocketChannel and Http2StreamChannel
// IHttp2StreamChannel. The anonymous ChannelInitializers and ChannelInboundHandlerAdapters are named nested classes.
public class H2PrefaceTest
{

    public enum OpenMode
    {
        Blocked,
        Listener,
        SubmitInListener
    }

    [Theory]
    [InlineData(OpenMode.Blocked)]
    [InlineData(OpenMode.Listener)]
    [InlineData(OpenMode.SubmitInListener)]
    public async Task OpenStreamAfterBlockingConnect(OpenMode mode)
    {
        StreamRequestResponseListener streamRequestResponseListener = new StreamRequestResponseListener();
        IEventLoopGroup eventLoopGroup = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        IChannel backend = await new ServerBootstrap()
                .Group(eventLoopGroup)
                .Channel<NioServerSocketChannel>()
                .ChildHandler(new BackendInitializer(streamRequestResponseListener))
                .BindAsync(NetUtil.LOCALHOST, 0, TestContext.Current.CancellationToken);

        Bootstrap clientBootstrap = new Bootstrap()
                .Group(eventLoopGroup)
                .Channel<NioSocketChannel>()
                .RemoteAddress(backend.LocalAddress)
                .Handler(new ClientInitializer(streamRequestResponseListener));
        IChannel channel = await clientBootstrap.RegisterAsync(TestContext.Current.CancellationToken);
        Task cf = channel.ConnectAsync(clientBootstrap.Config.RemoteAddress, null, TestContext.Current.CancellationToken);
        TaskScheduler listenerScheduler = new EventExecutorTaskScheduler(() => channel.EventLoop);
        try
        {
            Http2StreamChannelBootstrap streamChannelBootstrap = new Http2StreamChannelBootstrap(channel);

            switch (mode)
            {
                case OpenMode.Blocked:
                    await cf;
                    _ = streamChannelBootstrap.OpenAsync().ContinueWith(streamRequestResponseListener.OperationComplete,
                            TestContext.Current.CancellationToken, TaskContinuationOptions.ExecuteSynchronously,
                            listenerScheduler);
                    break;
                case OpenMode.Listener:
                    _ = cf.ContinueWith(connectFuture =>
                            streamChannelBootstrap.OpenAsync().ContinueWith(
                                    streamRequestResponseListener.OperationComplete, CancellationToken.None,
                                    TaskContinuationOptions.ExecuteSynchronously, listenerScheduler),
                            TestContext.Current.CancellationToken, TaskContinuationOptions.ExecuteSynchronously,
                            listenerScheduler);
                    break;
                case OpenMode.SubmitInListener:
                    _ = cf.ContinueWith(f => channel.EventLoop.SubmitAsync(() =>
                    {
                        _ = streamChannelBootstrap.OpenAsync().ContinueWith(
                                streamRequestResponseListener.OperationComplete, CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously, listenerScheduler);
                    }), TestContext.Current.CancellationToken, TaskContinuationOptions.ExecuteSynchronously,
                            listenerScheduler);
                    break;
                default:
                    throw new UnreachableException();
            }

            Assert.Equal("200", (await streamRequestResponseListener.responseHeaders.Task.WaitAsync(
                    TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Headers().Status().ToString());
        }
        finally
        {
            await channel.CloseAsync(TestContext.Current.CancellationToken);
            await backend.CloseAsync(TestContext.Current.CancellationToken);
            await eventLoopGroup.ShutdownGracefullyAsync();
        }
    }

    // CLR: the anonymous ChannelInitializer<SocketChannel> of the backend server.
    private sealed class BackendInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly StreamRequestResponseListener streamRequestResponseListener;

        internal BackendInitializer(StreamRequestResponseListener streamRequestResponseListener)
        {
            this.streamRequestResponseListener = streamRequestResponseListener;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            ch.Pipeline.AddLast(Http2FrameCodecBuilder.ForServer().Build());
            ch.Pipeline.AddLast(new Http2MultiplexHandler(new ServerStreamInitializer()));
            ch.Pipeline.AddLast(new ExceptionForwarder(streamRequestResponseListener));
        }
    }

    // CLR: the anonymous ChannelInitializer<Http2StreamChannel> of the backend's Http2MultiplexHandler.
    private sealed class ServerStreamInitializer : ChannelInitializer<IHttp2StreamChannel>
    {
        protected override void InitChannel(IHttp2StreamChannel ch)
        {
            ch.Pipeline.AddLast(new H2ServerHandler());
        }
    }

    // CLR: the anonymous ChannelInitializer<SocketChannel> of the client.
    private sealed class ClientInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly StreamRequestResponseListener streamRequestResponseListener;

        internal ClientInitializer(StreamRequestResponseListener streamRequestResponseListener)
        {
            this.streamRequestResponseListener = streamRequestResponseListener;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            ch.Pipeline.AddLast(Http2FrameCodecBuilder.ForClient()
                    .InitialSettings(Http2Settings.DefaultSettings())
                    .Build());
            ch.Pipeline.AddLast(new Http2MultiplexHandler(new ChannelInboundHandlerAdapter()));
            ch.Pipeline.AddLast(new ExceptionForwarder(streamRequestResponseListener));
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter that the server and the client pipelines end with.
    private sealed class ExceptionForwarder : ChannelInboundHandlerAdapter
    {
        private readonly StreamRequestResponseListener streamRequestResponseListener;

        internal ExceptionForwarder(StreamRequestResponseListener streamRequestResponseListener)
        {
            this.streamRequestResponseListener = streamRequestResponseListener;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            streamRequestResponseListener.responseHeaders.TrySetException(cause);
        }
    }

    private class H2ServerHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2HeadersFrame)
            {
                IHttp2Headers responseHeaders = new DefaultHttp2Headers().Status(Http2TestUtil.Of("200"));
                _ = ctx.WriteAsync(new DefaultHttp2HeadersFrame(responseHeaders, false));
                _ = ctx.WriteAndFlushAsync(
                        new DefaultHttp2DataFrame(Unpooled.CopiedBuffer("hello world", Encoding.UTF8), true));
            }
        }
    }

    /// Send a request and wait for a response once an Http2StreamChannel is established
    private sealed class StreamRequestResponseListener
    {
        internal readonly TaskCompletionSource<IHttp2HeadersFrame> responseHeaders =
                new TaskCompletionSource<IHttp2HeadersFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OperationComplete(Task<IHttp2StreamChannel> future)
        {
            IHttp2StreamChannel streamChannel = future.IsCompletedSuccessfully ? future.Result : null;
            streamChannel.Pipeline.AddLast(new ResponseHandler(this));
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Method(Http2TestUtil.Of("GET"))
                    .Path(Http2TestUtil.Of("/test"))
                    .Scheme(Http2TestUtil.Of("http"));
            IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(headers, true);
            _ = streamChannel.WriteAndFlushAsync(headersFrame).ContinueWith(f =>
            {
                if (!f.IsCompletedSuccessfully)
                {
                    responseHeaders.TrySetException(Http2CodecUtil.Cause(f));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => streamChannel.EventLoop));
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter that StreamRequestResponseListener adds to the stream channel.
    private sealed class ResponseHandler : ChannelInboundHandlerAdapter
    {
        private readonly StreamRequestResponseListener listener;

        internal ResponseHandler(StreamRequestResponseListener listener)
        {
            this.listener = listener;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2HeadersFrame)
            {
                listener.responseHeaders.TrySetResult((IHttp2HeadersFrame) msg);
            }
            ctx.FireChannelRead(msg);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            listener.responseHeaders.TrySetException(cause);
        }
    }
}
