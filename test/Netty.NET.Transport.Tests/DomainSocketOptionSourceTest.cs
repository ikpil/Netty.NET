/*
 * Copyright 2018 The Netty Project
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

using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.DomainSocketSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent cleanup.
public class DomainSocketOptionSourceTest
{
    [Fact]
    public async Task TestNioChannelOption()
    {
        RequireDomainSockets();
        using var socket = NewSocket();
        var server = new DomainServerSocketChannel(socket);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            int before = socket.ReceiveBufferSize;
            Assert.True(server.Config.TryGetOption(ChannelOption.SO_RCVBUF, out int configured));
            Assert.Equal(before, configured);
            server.Config.SetOption(ChannelOption.SO_RCVBUF, 1 + configured);
            Assert.True(server.Config.TryGetOption(ChannelOption.SO_RCVBUF, out int after));
            Assert.Equal(socket.ReceiveBufferSize, after);
            Assert.NotEqual(before, after);
        }
        finally
        {
            await group.RegisterAsync(server);
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task TestGetOptions()
    {
        RequireDomainSockets();
        var server = new DomainServerSocketChannel();
        var group = new DefaultEventLoopGroup(1);
        try { server.Config.GetOptions(); }
        finally
        {
            await group.RegisterAsync(server);
            await group.StopAsync();
        }
    }
}
