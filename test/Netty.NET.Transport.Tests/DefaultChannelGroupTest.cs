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
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Groups;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DefaultChannelGroupTest
{
    // Test for #1183
    [Fact]
    public async Task TestNotThrowBlockingOperationException()
    {
        var group = new DefaultEventLoopGroup();
        // CLR aggregation uses Task.WhenAll; the original GlobalEventExecutor is not a Task owner.
        var allChannels = new DefaultChannelGroup();
        try
        {
            var bootstrap = new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .ChildHandler(new ActiveHandler(allChannels));
            IChannel server = await bootstrap.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            allChannels.Add(server);
            await allChannels.CloseAsync().WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)); }
    }
    private sealed class ActiveHandler(DefaultChannelGroup channels) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) => channels.Add(context.Channel);
    }
}
