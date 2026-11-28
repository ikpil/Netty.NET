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

namespace Netty.NET.Example.Telnet;

/**
 * Handles a client-side channel.
 */
// CLR: @Sharable is the [Sharable] attribute; cause.printStackTrace() writes the exception to Console.Error and
// ctx.close() is CloseAsync(), whose Task is discarded.
[Sharable]
public class TelnetClientHandler : SimpleChannelInboundHandler<string>
{

    protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
    {
        Console.Error.WriteLine(msg);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
