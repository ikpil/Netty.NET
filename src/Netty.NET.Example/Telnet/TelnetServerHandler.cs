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
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Telnet;

/**
 * Handles a server-side channel.
 */
// CLR: @Sharable is the [Sharable] attribute. InetAddress.getLocalHost().getHostName() is System.Net.Dns.GetHostName() (qualified: the
// Netty.NET.Example.Dns namespace hides it), and
// new Date() printed by String concatenation (Date.toString(), "EEE MMM dd HH:mm:ss zzz yyyy" in the default time zone)
// is the local DateTimeOffset in the same layout with the invariant culture; .NET has no time zone name specifier, so
// the zone is the UTC offset. request.toLowerCase() is ToLowerInvariant(). ctx.write(msg) is WriteAsync and
// ChannelFutureListener.CLOSE on the write future is a continuation of the write Task that closes ctx.Channel (a Task has
// no channel), as codec-base's MessageAggregator maps it. cause.printStackTrace() writes the exception to Console.Error.
[Sharable]
public class TelnetServerHandler : SimpleChannelInboundHandler<string>
{

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        // Send greeting for a new connection.
        _ = ctx.WriteAsync("Welcome to " + System.Net.Dns.GetHostName() + "!\r\n");
        _ = ctx.WriteAsync("It is " + DateTimeOffset.Now.ToString("ddd MMM dd HH:mm:ss 'UTC'zzz yyyy",
                CultureInfo.InvariantCulture) + " now.\r\n");
        ctx.Flush();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, string request)
    {
        // Generate and write a response.
        string response;
        bool close = false;

        if (request.Length == 0)
        {
            response = "Please type something.\r\n";
        }
        else if ("bye".Equals(request.ToLowerInvariant()))
        {
            response = "Have a good day!\r\n";
            close = true;
        }
        else
        {
            response = "Did you say '" + request + "'?\r\n";
        }

        // We do not need to write a ChannelBuffer here.
        // We know the encoder inserted at TelnetPipelineFactory will do the conversion.
        Task future = ctx.WriteAsync(response);

        // Close the connection after sending 'Have a good day!'
        // if the client has sent 'bye'.
        if (close)
        {
            _ = future.ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
