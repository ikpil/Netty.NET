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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Discard;

/**
 * Handles a server-side channel.
 */
// CLR: channelRead0 is protected as the abstract SimpleChannelInboundHandler.ChannelRead0 it overrides (an override
// cannot change the accessibility). cause.printStackTrace() writes Exception.ToString() to Console.Error.
public class DiscardServerHandler : SimpleChannelInboundHandler<object>
{

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        // discard
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        // Close the connection when an exception is raised.
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
