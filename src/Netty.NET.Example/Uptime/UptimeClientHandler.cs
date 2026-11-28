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
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Uptime;

/**
 * Keep reconnecting to the server while printing out the current uptime and
 * connection attempt getStatus.
 */
// CLR: @Sharable is the [Sharable] attribute, and channelRead0 is protected as the abstract
// SimpleChannelInboundHandler.ChannelRead0 it overrides (an override cannot change the accessibility). The
// package-private startTime and println are internal. System.currentTimeMillis() is
// DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), System.err.format with %s and %5d (right-aligned, width 5) and %n is
// Console.Error.WriteLine with {0} and {0,5}, and string + char appends the character as in Java.
// eventLoop().schedule(Runnable, delay, TimeUnit.SECONDS) is ScheduleAsync with a TimeSpan, whose Task is discarded as
// Java discards the ScheduledFuture; the anonymous Runnable is the scheduled Action. close() discards its Task, and
// cause.printStackTrace() writes Exception.ToString() to Console.Error.
[Sharable]
public class UptimeClientHandler : SimpleChannelInboundHandler<object>
{

    internal long startTime = -1;

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (startTime < 0)
        {
            startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        Println("Connected to: " + ctx.Channel.RemoteAddress);
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        // Discard received data
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (!(evt is IdleStateEvent))
        {
            return;
        }

        IdleStateEvent e = (IdleStateEvent) evt;
        if (e.State() == IdleState.READER_IDLE)
        {
            // The connection was OK but there was no traffic for last period.
            Println("Disconnecting due to no inbound traffic");
            _ = ctx.CloseAsync();
        }
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Println("Disconnected from: " + ctx.Channel.RemoteAddress);
    }

    public override void ChannelUnregistered(IChannelHandlerContext ctx)
    {
        Println("Sleeping for: " + UptimeClient.RECONNECT_DELAY + 's');

        _ = ctx.Channel.EventLoop.ScheduleAsync(() =>
        {
            Println("Reconnecting to: " + UptimeClient.HOST + ':' + UptimeClient.PORT);
            UptimeClient.Connect();
        }, TimeSpan.FromSeconds(UptimeClient.RECONNECT_DELAY));
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    internal void Println(string msg)
    {
        if (startTime < 0)
        {
            Console.Error.WriteLine("[SERVER IS DOWN] {0}", msg);
        }
        else
        {
            Console.Error.WriteLine("[UPTIME: {0,5}s] {1}", (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime) / 1000, msg);
        }
    }
}
