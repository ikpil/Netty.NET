/*
 * Copyright 2019 The Netty Project
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original operation ordering and independently owned socket/group cleanup.
public class SocketShutdownOutputByPeerSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestShutdownOutput(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var handler = new ShutdownTestHandler();
        var server = await fixture.Server.ChildHandler(handler).ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true)
            .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)server.LocalAddress);
        await peer.GetStream().WriteAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(1, await handler.Bytes.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(handler.Channel.IsOpen); Assert.True(handler.Channel.IsActive);
        Assert.False(handler.Channel.IsInputShutdown); Assert.False(handler.Channel.IsOutputShutdown);
        peer.Client.Shutdown(SocketShutdown.Send);
        await handler.HalfClosure.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(handler.Channel.IsOpen); Assert.True(handler.Channel.IsActive);
        Assert.True(handler.Channel.IsInputShutdown); Assert.False(handler.Channel.IsOutputShutdown);
        while (handler.Closure.Task.IsCompleted && Volatile.Read(ref handler.HalfClosureCount) != 1) await Task.Delay(100, TestContext.Current.CancellationToken);
    }
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestShutdownOutputWithoutOption(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var handler = new ShutdownTestHandler();
        var server = await fixture.Server.ChildHandler(handler).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)server.LocalAddress);
        await peer.GetStream().WriteAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(1, await handler.Bytes.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(handler.Channel.IsOpen); Assert.True(handler.Channel.IsActive);
        Assert.False(handler.Channel.IsInputShutdown); Assert.False(handler.Channel.IsOutputShutdown);
        peer.Client.Shutdown(SocketShutdown.Send);
        await handler.Closure.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(handler.Channel.IsOpen); Assert.False(handler.Channel.IsActive);
        Assert.True(handler.Channel.IsInputShutdown); Assert.True(handler.Channel.IsOutputShutdown);
        while (handler.HalfClosure.Task.IsCompleted && Volatile.Read(ref handler.HalfClosureCount) != 0) await Task.Delay(100, TestContext.Current.CancellationToken);
    }
}
