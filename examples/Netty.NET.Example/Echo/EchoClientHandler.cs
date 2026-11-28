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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Echo;

/**
 * Handler implementation for the echo client.  It initiates the ping-pong
 * traffic between the echo client and server by sending the first message to
 * the server.
 */
// CLR: ctx.writeAndFlush/write/close return Tasks that are discarded as Java discards the futures. capacity() is the
// Capacity property. cause.printStackTrace() writes Exception.ToString() to Console.Error.
public class EchoClientHandler : ChannelInboundHandlerAdapter
{

    private readonly ByteBuf firstMessage;

    /**
     * Creates a client-side handler.
     */
    public EchoClientHandler()
    {
        firstMessage = Unpooled.Buffer(EchoClient.SIZE);

        for (int i = 0; i < firstMessage.Capacity; i ++)
        {
            firstMessage.WriteByte((byte) i);
        }
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        _ = ctx.WriteAndFlushAsync(firstMessage);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        _ = ctx.WriteAsync(msg);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
       ctx.Flush();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        // Close the connection when an exception is raised.
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
