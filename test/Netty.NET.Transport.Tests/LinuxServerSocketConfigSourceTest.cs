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
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class LinuxServerSocketConfigSourceTest
{
    [Fact]
    public Task TestTcpDeferAccept() => WithListener(config =>
    {
        Assert.True(config.SetOption(ChannelOption.TCP_DEFER_ACCEPT, 0));
        Assert.Equal(0, Get(config, ChannelOption.TCP_DEFER_ACCEPT));
        Assert.True(config.SetOption(ChannelOption.TCP_DEFER_ACCEPT, 10));
        // The returned value may be bigger then what we set.
        // See https://www.spinics.net/lists/netdev/msg117330.html
        Assert.True(10 <= Get(config, ChannelOption.TCP_DEFER_ACCEPT));
    });

    [Fact]
    public Task TestReusePort() => WithListener(config =>
    {
        Assert.True(config.SetOption(ChannelOption.SO_REUSEPORT, false));
        Assert.False(Get(config, ChannelOption.SO_REUSEPORT));
        Assert.True(config.SetOption(ChannelOption.SO_REUSEPORT, true));
        Assert.True(Get(config, ChannelOption.SO_REUSEPORT));
    });

    [Fact]
    public Task TestFreeBind() => WithListener(config =>
    {
        Assert.True(config.SetOption(ChannelOption.IP_FREEBIND, false));
        Assert.False(Get(config, ChannelOption.IP_FREEBIND));
        Assert.True(config.SetOption(ChannelOption.IP_FREEBIND, true));
        Assert.True(Get(config, ChannelOption.IP_FREEBIND));
    });

    [Fact]
    public Task GetGetOptions() => WithListener(config => Assert.NotEmpty(config.GetOptions()));

    [Fact]
    public Task TestFastOpen() => WithListener(config =>
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetOption(ChannelOption.TCP_FASTOPEN, -1));
        Assert.True(config.SetOption(ChannelOption.TCP_FASTOPEN, 10));
        Assert.Equal(10, Get(config, ChannelOption.TCP_FASTOPEN));
    });

    private static async Task WithListener(Action<IChannelConfig> assert)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux socket options require Linux.");
        var loop = new DefaultEventLoopGroup(1);
        try
        {
            var channel = await new ServerBootstrap().Group(loop).ChannelFactory(static () => new TcpServerSocketChannel())
                .ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync(new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0),
                    TestContext.Current.CancellationToken);
            assert(channel.Config);
        }
        finally { await loop.StopAsync(); }
    }

    internal static T Get<T>(IChannelConfig config, ChannelOption<T> option)
    {
        Assert.True(config.TryGetOption(option, out T value));
        return value;
    }
}
