/*
 * Copyright 2015 The Netty Project
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
using static Netty.NET.Transport.Tests.LinuxServerSocketConfigSourceTest;

namespace Netty.NET.Transport.Tests;

public class LinuxStreamSocketConfigSourceTest
{
    [Fact]
    public Task TestRandomTcpNotSentLowAt() => WithChannel(channel =>
    {
        long expected = Random.Shared.NextInt64() & uint.MaxValue;
        try
        {
            Assert.True(channel.Config.SetOption(ChannelOption.TCP_NOTSENT_LOWAT, expected));
            Assert.Equal(expected, Get(channel.Config, ChannelOption.TCP_NOTSENT_LOWAT));
        }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
        { Assert.Skip("The kernel does not support TCP_NOTSENT_LOWAT."); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestInvalidHighTcpNotSentLowAt() => WithChannel(channel =>
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => channel.Config.SetOption(ChannelOption.TCP_NOTSENT_LOWAT, (long)uint.MaxValue + 1));
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestInvalidLowTcpNotSentLowAt() => WithChannel(channel =>
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => channel.Config.SetOption(ChannelOption.TCP_NOTSENT_LOWAT, -1L));
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestTcpCork() => WithChannel(channel =>
    {
        Assert.True(channel.Config.SetOption(ChannelOption.TCP_CORK, false));
        Assert.False(Get(channel.Config, ChannelOption.TCP_CORK));
        Assert.True(channel.Config.SetOption(ChannelOption.TCP_CORK, true));
        Assert.True(Get(channel.Config, ChannelOption.TCP_CORK));
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestTcpQuickAck() => WithChannel(channel =>
    {
        Assert.True(channel.Config.SetOption(ChannelOption.TCP_QUICKACK, false));
        Assert.False(Get(channel.Config, ChannelOption.TCP_QUICKACK));
        Assert.True(channel.Config.SetOption(ChannelOption.TCP_QUICKACK, true));
        Assert.True(Get(channel.Config, ChannelOption.TCP_QUICKACK));
        return Task.CompletedTask;
    });

    // For this test to pass, we are relying on the sockets file descriptor not being reused after the socket is closed.
    // This is inherently racy, so we allow getSoLinger to throw ChannelException a few of times, but eventually we do
    // want to see a ClosedChannelException for the test to pass.
    // CLR disposes the Socket object and uses a SafeHandle for descriptor operations.
    [Fact]
    public Task TestSetOptionWhenClosed() => WithChannel(async channel =>
    {
        await channel.CloseAsync();
        Assert.Throws<ObjectDisposedException>(() => channel.Config.SetOption(ChannelOption.SO_LINGER, 0));
    });

    [Fact]
    public Task TestGetOptionWhenClosed() => WithChannel(async channel =>
    {
        await channel.CloseAsync();
        Assert.Throws<ObjectDisposedException>(() => channel.Config.TryGetOption(ChannelOption.SO_LINGER, out _));
    });

    [Fact]
    public Task GetGetOptions() => WithChannel(channel =>
    {
        Assert.NotEmpty(channel.Config.GetOptions());
        return Task.CompletedTask;
    });

    private static async Task WithChannel(Func<IChannel, Task> assert)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux socket options require Linux.");
        var loop = new DefaultEventLoopGroup(1);
        try
        {
            var channel = await new Bootstrap().Group(loop).ChannelFactory(static () => new TcpSocketChannel())
                .Handler(new ChannelInboundHandlerAdapter()).BindAsync(new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0),
                    TestContext.Current.CancellationToken);
            await assert(channel);
        }
        finally { await loop.StopAsync(); }
    }
}
