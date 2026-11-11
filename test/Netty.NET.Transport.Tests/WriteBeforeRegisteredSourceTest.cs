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
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The original starts connect/write/close without waiting between them.
public class WriteBeforeRegisteredSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestWriteBeforeConnect(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var channel = new TcpSocketChannel();
        // Keep channel access independent of the connect Task, as in connect(...).channel().
        var bootstrap = new Bootstrap().Group(fixture.Client.Config.Group).ChannelFactory(() => channel)
            .Option(ChannelOption.ALLOCATOR, (IByteBufAllocator)fixture.Client.Config.Options[ChannelOption.ALLOCATOR]).Handler(new TestHandler());
        Task<IChannel> connect = null;
        try
        {
            connect = bootstrap.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken: TestContext.Current.CancellationToken);
            var buffer = (Random.Shared.Next(2) == 0 ? channel.Allocator.DirectBuffer(1) : channel.Allocator.HeapBuffer(1)).WriteByte(1);
            _ = Observe(channel.WriteAndFlushAsync(buffer));
        }
        finally
        {
            await Bootstrap.CleanupAsync(channel);
            if (connect != null) await Observe(connect);
        }
    }
    private static async Task Observe(Task operation) { try { await operation; } catch { } }
    private sealed class TestHandler : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => System.Diagnostics.Trace.WriteLine(error);
    }
}
#pragma warning restore xUnit1051
