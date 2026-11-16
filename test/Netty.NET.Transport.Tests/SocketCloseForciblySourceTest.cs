/*
 * Copyright 2017 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source waits for the close future without a deadline (no @Timeout).
public class SocketCloseForciblySourceTest : AbstractSocketTest
{
    [Fact]
    public async Task TestCloseForcibly()
    {
        await Run(TestContext.Current, TestCloseForcibly0);
    }

    // CLR: xUnit1024 forbids overloading the TestCloseForcibly test name.
    private async Task TestCloseForcibly0(ServerBootstrap sb, Bootstrap cb)
    {
        sb.Handler(new CloseForciblyHandler()).ChildHandler(new ChannelInboundHandlerAdapter());

        cb.Handler(new ChannelInboundHandlerAdapter());

        IChannel sc = await sb.BindAsync();

        // CLR: connect(...).channel() returns the channel of the pending ChannelFuture, whose closeFuture() is then
        // awaited even when the forcibly closed peer makes the connect fail. Bootstrap.ConnectAsync yields the channel
        // only from a successful Task<IChannel>, but it calls the channel factory synchronously before its first await
        // (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs). cb's factory cannot be replaced ("channelFactory set
        // already", as in AbstractBootstrap.channelFactory), so the connect runs on a Bootstrap with cb's group,
        // handler and allocator (the only option the harness sets) whose factory wraps cb's factory and captures the
        // channel. The source ignores the connect outcome; it is only observed after the close.
        Func<IChannel> channelFactory = cb.Config.ChannelFactory;
        IChannel channel = null;
        Bootstrap connectBootstrap = new Bootstrap().Group(cb.Config.Group)
                .ChannelFactory(() => channel = channelFactory())
                .Option(ChannelOption.ALLOCATOR, (IByteBufAllocator) cb.Config.Options[ChannelOption.ALLOCATOR])
                .Handler(cb.Config.Handler);
        Task<IChannel> connectFuture = connectBootstrap.ConnectAsync(sc.LocalAddress);
        await channel.CloseFuture;
        await Record.ExceptionAsync(() => connectFuture);
        await sc.CloseAsync();
    }

    // CLR: a named class carries the body of the source's anonymous handler; C# anonymous types "can't add methods,
    // events, or custom operators"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/anonymous-types).
    private sealed class CloseForciblyHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ISocketChannel childChannel = (ISocketChannel) msg;
            ((ISocketChannelConfig) childChannel.Config).SetSoLinger(0);
            // CLR: this port folds AbstractUnsafe into AbstractChannel, so unsafe().closeForcibly() is
            // AbstractChannel.CloseForcibly() (src/Netty.NET.Transport/Channels/AbstractChannel.cs).
            ((AbstractChannel) childChannel).CloseForcibly();
        }
    }
}
#pragma warning restore xUnit1051
