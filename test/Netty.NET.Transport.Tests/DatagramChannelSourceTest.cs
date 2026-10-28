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
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Groups;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051
public class DatagramChannelSourceTest
{
    [Fact]
    /**
     * Test try to reproduce issue #1335
     */
    public async Task TestBindMultiple()
    {
        var channelGroup = new DefaultChannelGroup();
        var group = new DefaultEventLoopGroup(2);
        try
        {
            for (int i = 0; i < 100; i++)
            {
                var bootstrap = new Bootstrap().Group(group).Channel<DatagramChannel>()
                    .Option(ChannelOption.SO_BROADCAST, true).Handler(new DiscardHandler());
                IChannel channel = await bootstrap.BindAsync(new IPEndPoint(IPAddress.Any, 0));
                channelGroup.Add(channel);
            }
            Assert.Equal(100, channelGroup.Count);
        }
        finally
        {
            await channelGroup.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private sealed class DiscardHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            // Discard
            ReferenceCountUtil.Release(message);
        }
    }
}
