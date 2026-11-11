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
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The original does not require the racing connection attempt to succeed.
public class SocketCloseForciblySourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestCloseForcibly(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var intercepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = await fixture.Server.Handler(new ForceClose(intercepted)).ChildHandler(new ChannelInboundHandlerAdapter())
            .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        var client = new TcpSocketChannel();
        try
        {
            Task<IChannel> connect = new Bootstrap().Group(fixture.Client.Config.Group).ChannelFactory(() => client)
                .Option(ChannelOption.ALLOCATOR, (IByteBufAllocator)fixture.Client.Config.Options[ChannelOption.ALLOCATOR])
                .Handler(new CloseOnException()).ConnectAsync(server.LocalAddress);
            // The source observes connect(...).channel().closeFuture(), including an early reset.
            // Keep channel identity available independently of the native Task<IChannel> connect result.
            await client.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
            await intercepted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await Record.ExceptionAsync(() => connect);
        }
        finally { if (client.IsRegistered) await client.CloseAsync(); else client.CloseForcibly(); await server.CloseAsync(); }
    }
    private sealed class ForceClose(TaskCompletionSource intercepted) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            var child = Assert.IsType<TcpSocketChannel>(message);
            child.Config.SetOptions(new[] { new System.Collections.Generic.KeyValuePair<ChannelOption, object>(ChannelOption.SO_LINGER, 0) });
            child.CloseForcibly();
            intercepted.TrySetResult();
        }
    }
    private sealed class CloseOnException : ChannelInboundHandlerAdapter
    { public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => _ = context.CloseAsync(); }
}
#pragma warning restore xUnit1051
