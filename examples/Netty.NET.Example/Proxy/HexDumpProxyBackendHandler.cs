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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Proxy;

// CLR: writeAndFlush(msg).addListener is a continuation of the write Task on the inbound channel's event loop
// (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()); future.isSuccess() is
// IsCompletedSuccessfully. ctx.close() discards its Task as Java discards the future, and cause.printStackTrace() writes
// Exception.ToString() to Console.Error.
public class HexDumpProxyBackendHandler : ChannelInboundHandlerAdapter
{

    private readonly IChannel inboundChannel;

    public HexDumpProxyBackendHandler(IChannel inboundChannel)
    {
        this.inboundChannel = inboundChannel;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (!inboundChannel.IsActive)
        {
            HexDumpProxyFrontendHandler.CloseOnFlush(ctx.Channel);
        }
        else
        {
            ctx.Read();
        }
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        _ = inboundChannel.WriteAndFlushAsync(msg).ContinueWith(future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                ctx.Read();
            }
            else
            {
                _ = ctx.CloseAsync();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            new EventExecutorTaskScheduler(() => inboundChannel.EventLoop));
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        HexDumpProxyFrontendHandler.CloseOnFlush(inboundChannel);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        HexDumpProxyFrontendHandler.CloseOnFlush(ctx.Channel);
    }
}
