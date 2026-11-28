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

// Register the channel before connecting because the test needs the channel before the connect task completes. In
// listener mode, open the stream on the event loop when the preface write completes.
// Complete the test's response promise asynchronously so its continuation does not resume on the channel's event
// loop.
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
