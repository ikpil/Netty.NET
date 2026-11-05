/*
 * Copyright 2021 The Netty Project
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
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original independent channel/group cleanup.
public class DatagramUnicastInetSourceTest
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task TestBindWithPortOnly(bool ipv6, bool adaptive, bool mapped)
    {
        if (ipv6 && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 not supported");
        var group = new DefaultEventLoopGroup(1);
        IByteBufAllocator allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        IChannel channel = null;
        try
        {
            var cb = new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, mapped))
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(new ChannelInboundHandlerAdapter());
            channel = await cb.BindAsync(0);
        }
        finally
        {
            if (channel != null) await channel.CloseAsync();
            await group.StopAsync();
            if (allocator is AdaptiveByteBufAllocator a) a.Dispose();
        }
    }
}
