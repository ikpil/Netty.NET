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
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Proxy;

// CLR: addListener on a ChannelFuture is a continuation of the operation's Task on the event loop of the channel it
// belongs to (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()); future.isSuccess() is
// IsCompletedSuccessfully. Bootstrap.ConnectAsync yields the channel only as the result of its Task, while
// b.connect(...).channel() returns the channel at once; the channel factory is called synchronously inside ConnectAsync,
// so .channel(ctx.channel().getClass()) is a factory over ReflectiveChannelFactory (the reflective factory that
// Channel(Type) uses) that also stores the new channel in outboundChannel, as the handler-proxy tests capture
// b.connect(...).channel() (docs/handler-proxy-porting.md). inboundChannel.eventLoop() is an IEventLoop, which is an
// IEventLoopGroup. ChannelFutureListener.CLOSE is a continuation that closes the channel whatever the outcome.
// Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer, close() discards its Task as Java discards the future, and
// cause.printStackTrace() writes Exception.ToString() to Console.Error.
public class HexDumpProxyFrontendHandler : ChannelInboundHandlerAdapter
{

    private readonly string remoteHost;
    private readonly int remotePort;

    // As we use inboundChannel.eventLoop() when building the Bootstrap this does not need to be volatile as
    // the outboundChannel will use the same EventLoop (and therefore Thread) as the inboundChannel.
    private IChannel outboundChannel;

    public HexDumpProxyFrontendHandler(string remoteHost, int remotePort)
    {
        this.remoteHost = remoteHost;
        this.remotePort = remotePort;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        IChannel inboundChannel = ctx.Channel;

        // Start the connection attempt.
        Bootstrap b = new Bootstrap();
        ReflectiveChannelFactory<IChannel> channelClass = new ReflectiveChannelFactory<IChannel>(ctx.Channel.GetType());
        b.Group(inboundChannel.EventLoop)
         .ChannelFactory(() => outboundChannel = channelClass.NewChannel())
         .Handler(new HexDumpProxyBackendHandler(inboundChannel))
         .Option(ChannelOption.AUTO_READ, false);
        Task f = b.ConnectAsync(remoteHost, remotePort);
        _ = f.ContinueWith(future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                // connection complete start to read first data
                inboundChannel.Read();
            }
            else
            {
                // Close the connection if the connection attempt has failed.
                _ = inboundChannel.CloseAsync();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            new EventExecutorTaskScheduler(() => inboundChannel.EventLoop));
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (outboundChannel.IsActive)
        {
            _ = outboundChannel.WriteAndFlushAsync(msg).ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    // was able to flush out data, start to read the next chunk
                    ctx.Read();
                }
                else
                {
                    _ = ctx.CloseAsync();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => outboundChannel.EventLoop));
        }
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (outboundChannel != null)
        {
            CloseOnFlush(outboundChannel);
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        CloseOnFlush(ctx.Channel);
    }

    /**
     * Closes the specified channel after all queued write requests are flushed.
     */
    internal static void CloseOnFlush(IChannel ch)
    {
        if (ch.IsActive)
        {
            _ = ch.WriteAndFlushAsync(Unpooled.EmptyBuffer).ContinueWith(_ => ch.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => ch.EventLoop));
        }
    }
}
