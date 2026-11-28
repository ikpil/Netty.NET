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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Qotm;

// CLR: channelRead0 is protected as the abstract SimpleChannelInboundHandler.ChannelRead0 it overrides (an override
// cannot change the accessibility). synchronized (random) is lock (random), random.nextInt(bound) is
// random.Next(maxValue) with the same exclusive bound, System.err.println(Object) is Console.Error.WriteLine(object),
// packet.content() and packet.sender() are the Content and Sender properties, and CharsetUtil.UTF_8 is Encoding.UTF8.
// ctx.write discards its Task as Java discards the future, and cause.printStackTrace() writes Exception.ToString() to
// Console.Error.
public class QuoteOfTheMomentServerHandler : SimpleChannelInboundHandler<DatagramPacket>
{

    private static readonly Random random = new Random();

    // Quotes from Mohandas K. Gandhi:
    private static readonly string[] quotes = {
        "Where there is love there is life.",
        "First they ignore you, then they laugh at you, then they fight you, then you win.",
        "Be the change you want to see in the world.",
        "The weak can never forgive. Forgiveness is the attribute of the strong.",
    };

    private static string NextQuote()
    {
        int quoteId;

        lock (random)
        {
            quoteId = random.Next(quotes.Length);
        }

        return quotes[quoteId];
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket packet)
    {
        Console.Error.WriteLine(packet);

        if ("QOTM?".Equals(packet.Content.ToString(Encoding.UTF8)))
        {
            _ = ctx.WriteAsync(new DatagramPacket(
                    Unpooled.CopiedBuffer("QOTM: " + NextQuote(), Encoding.UTF8), packet.Sender));
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        // We don't close the channel because we can keep serving requests.
    }
}
