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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.SocksProxy;

// CLR: Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer. writeAndFlush/close return Tasks that are discarded as Java
// discards the futures, and cause.printStackTrace() writes Exception.ToString() to Console.Error.
public sealed class RelayHandler : ChannelInboundHandlerAdapter
{

    private readonly IChannel relayChannel;

    public RelayHandler(IChannel relayChannel)
    {
        this.relayChannel = relayChannel;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        _ = ctx.WriteAndFlushAsync(Unpooled.EmptyBuffer);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (relayChannel.IsActive)
        {
            _ = relayChannel.WriteAndFlushAsync(msg);
        }
        else
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (relayChannel.IsActive)
        {
            SocksServerUtils.CloseOnFlush(relayChannel);
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
