/*
 * Copyright 2016 The Netty Project
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

using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class SocketMultipleConnectSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestMultipleConnect(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(2);
        var allocator = SelectAllocator(adaptive);
        try
        {
            var listener = await new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .Option(ChannelOption.ALLOCATOR, allocator).ChildOption(ChannelOption.ALLOCATOR, allocator)
                .ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync(new IPEndPoint(Loopback(), 0), cancellation);
            var client = await new Bootstrap().Group(group).Channel<TcpSocketChannel>()
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(new ChannelInboundHandlerAdapter())
                .RegisterAsync(cancellation);

            await client.ConnectAsync(listener.LocalAddress, cancellationToken: cancellation);
            var error = await Assert.ThrowsAsync<SocketException>(() =>
                client.ConnectAsync(listener.LocalAddress, cancellationToken: cancellation));
            Assert.Equal(SocketError.IsConnected, error.SocketErrorCode);
            Assert.True(client.IsActive);
            Assert.True(client.IsOpen);
        }
        finally { await group.StopAsync(); }
    }

    internal static IByteBufAllocator SelectAllocator(bool adaptive) =>
        adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;

    internal static IPAddress Loopback() => Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
}
