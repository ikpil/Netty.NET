/*
 * Copyright 2013 The Netty Project
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

#pragma warning disable xUnit1069 // The source starts connect/write/close without waiting for any of them.
#pragma warning disable xUnit1051 // The source starts connect/write/close without waiting for any of them.
public class WriteBeforeRegisteredSourceTest : AbstractClientSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestWriteBeforeConnect()
    {
        await Run(TestContext.Current, TestWriteBeforeConnect0);
    }

    // CLR: xUnit1024 forbids overloading the TestWriteBeforeConnect test name.
    private Task TestWriteBeforeConnect0(Bootstrap cb)
    {
        TestHandler h = new TestHandler();
        ISocketChannel ch = null;
        // CLR: connect(...).channel() returns the channel of the pending ChannelFuture at once, before its
        // registration has run. Bootstrap.ConnectAsync yields the channel only from a successful Task<IChannel>, but
        // it calls the channel factory synchronously before its first await
        // (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs). cb's factory cannot be replaced ("channelFactory set
        // already", as in AbstractBootstrap.channelFactory), so the connect runs on a Bootstrap with cb's group and
        // allocator (the only option the harness sets) whose factory wraps cb's factory and captures the channel. As
        // in the source, neither the connect, the write nor the close is awaited.
        Func<IChannel> channelFactory = cb.Config.ChannelFactory;
        IChannel channel = null;
        Bootstrap connectBootstrap = new Bootstrap().Group(cb.Config.Group)
                .ChannelFactory(() => channel = channelFactory())
                .Option(ChannelOption.ALLOCATOR, (IByteBufAllocator) cb.Config.Options[ChannelOption.ALLOCATOR]);

        try
        {
            _ = connectBootstrap.Handler(h).ConnectAsync(NewSocketAddress());
            ch = (ISocketChannel) channel;
            _ = ch.WriteAndFlushAsync(TestsuitePermutation.RandomBufferType(ch.Alloc(), new byte[] { 1 }, 0, 1));
        }
        finally
        {
            if (ch != null)
            {
                _ = ch.CloseAsync();
            }
        }

        return Task.CompletedTask;
    }

    private class TestHandler : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            // CLR: Throwable.printStackTrace() prints to the standard error stream.
            Console.Error.WriteLine(cause);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
