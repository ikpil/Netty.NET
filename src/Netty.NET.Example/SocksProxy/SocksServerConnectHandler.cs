/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Socksx;
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.SocksProxy;

// CLR: @ChannelHandler.Sharable is the [Sharable] attribute, and channelRead0 is protected as the abstract
// SimpleChannelInboundHandler.ChannelRead0 it overrides (an override cannot change the accessibility). The Java
// interfaces SocksMessage, Socks4CommandRequest and Socks5CommandRequest are ISocksMessage, ISocks4CommandRequest and
// ISocks5CommandRequest. ctx.executor().newPromise() is a TaskCompletionSource<IChannel> that runs its continuations
// asynchronously, and promise.addListener((FutureListener<Channel>) ...) is a continuation of its Task on the context's
// event loop (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()); future.getNow() is the
// channel when the Task succeeded and null otherwise. The listeners added to ChannelFutures are continuations of the
// operation Tasks on the event loop of the channel they belong to; future.isSuccess() is IsCompletedSuccessfully.
// SocksServerConnectHandler.this is this, NioSocketChannel.class is Channel<NioSocketChannel>(), and writeAndFlush
// discards its Task where Java discards the future.
[Sharable]
public sealed class SocksServerConnectHandler : SimpleChannelInboundHandler<ISocksMessage>
{

    private readonly Bootstrap b = new Bootstrap();

    protected override void ChannelRead0(IChannelHandlerContext ctx, ISocksMessage message)
    {
        if (message is ISocks4CommandRequest)
        {
            ISocks4CommandRequest request = (ISocks4CommandRequest) message;
            TaskCompletionSource<IChannel> promise = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = promise.Task.ContinueWith(
                    future =>
                    {
                        IChannel outboundChannel = future.IsCompletedSuccessfully ? future.Result : null;

                        if (future.IsCompletedSuccessfully)
                        {
                            Task responseFuture = ctx.Channel.WriteAndFlushAsync(
                                    new DefaultSocks4CommandResponse(Socks4CommandStatus.SUCCESS));

                            _ = responseFuture.ContinueWith(channelFuture =>
                            {
                                ctx.Pipeline.Remove(this);
                                outboundChannel.Pipeline.AddLast(new RelayHandler(ctx.Channel));
                                ctx.Pipeline.AddLast(new RelayHandler(outboundChannel));
                            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                                new EventExecutorTaskScheduler(() => ctx.Channel.EventLoop));
                        }
                        else
                        {
                            _ = ctx.Channel.WriteAndFlushAsync(
                                    new DefaultSocks4CommandResponse(Socks4CommandStatus.REJECTED_OR_FAILED));
                            SocksServerUtils.CloseOnFlush(ctx.Channel);
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ctx.Executor));

            IChannel inboundChannel = ctx.Channel;
            b.Group(inboundChannel.EventLoop)
                    .Channel<NioSocketChannel>()
                    .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 10000)
                    .Option(ChannelOption.SO_KEEPALIVE, true)
                    .Handler(new DirectClientHandler(promise));

            _ = b.ConnectAsync(request.DstAddr(), request.DstPort()).ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    // Connection established use handler provided results
                }
                else
                {
                    // Close the connection if the connection attempt has failed.
                    _ = ctx.Channel.WriteAndFlushAsync(
                            new DefaultSocks4CommandResponse(Socks4CommandStatus.REJECTED_OR_FAILED)
                    );
                    SocksServerUtils.CloseOnFlush(ctx.Channel);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => inboundChannel.EventLoop));
        }
        else if (message is ISocks5CommandRequest)
        {
            ISocks5CommandRequest request = (ISocks5CommandRequest) message;
            TaskCompletionSource<IChannel> promise = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = promise.Task.ContinueWith(
                    future =>
                    {
                        IChannel outboundChannel = future.IsCompletedSuccessfully ? future.Result : null;

                        if (future.IsCompletedSuccessfully)
                        {
                            Task responseFuture =
                                    ctx.Channel.WriteAndFlushAsync(new DefaultSocks5CommandResponse(
                                            Socks5CommandStatus.SUCCESS,
                                            request.DstAddrType(),
                                            request.DstAddr(),
                                            request.DstPort()));

                            _ = responseFuture.ContinueWith(f =>
                            {
                                ctx.Pipeline.Remove(this);
                                outboundChannel.Pipeline.AddLast(new RelayHandler(ctx.Channel));
                                ctx.Pipeline.AddLast(new RelayHandler(outboundChannel));
                            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                                new EventExecutorTaskScheduler(() => ctx.Channel.EventLoop));
                        }
                        else
                        {
                            _ = ctx.Channel.WriteAndFlushAsync(new DefaultSocks5CommandResponse(
                                    Socks5CommandStatus.FAILURE, request.DstAddrType()));
                            SocksServerUtils.CloseOnFlush(ctx.Channel);
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ctx.Executor));

            IChannel inboundChannel = ctx.Channel;
            b.Group(inboundChannel.EventLoop)
                    .Channel<NioSocketChannel>()
                    .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 10000)
                    .Option(ChannelOption.SO_KEEPALIVE, true)
                    .Handler(new DirectClientHandler(promise));

            _ = b.ConnectAsync(request.DstAddr(), request.DstPort()).ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    // Connection established use handler provided results
                }
                else
                {
                    // Close the connection if the connection attempt has failed.
                    _ = ctx.Channel.WriteAndFlushAsync(
                            new DefaultSocks5CommandResponse(Socks5CommandStatus.FAILURE, request.DstAddrType()));
                    SocksServerUtils.CloseOnFlush(ctx.Channel);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => inboundChannel.EventLoop));
        }
        else
        {
            _ = ctx.CloseAsync();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        SocksServerUtils.CloseOnFlush(ctx.Channel);
    }
}
