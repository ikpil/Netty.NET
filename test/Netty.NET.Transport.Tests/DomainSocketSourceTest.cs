/*
 * Copyright 2024 The Netty Project
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
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent cleanup.
public class DomainSocketSourceTest
{
    [Fact]
    public async Task AccessParent()
    {
        RequireDomainSockets();
        var parent = new DomainServerSocketChannel();
        using var socket = NewSocket();
        var child = new DomainSocketChannel(parent, socket);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            Assert.Same(parent, child.Parent);
        }
        finally
        {
            await group.RegisterAsync(parent);
            await group.RegisterAsync(child);
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task TestCloseOnError()
    {
        RequireDomainSockets();
        using var socket = NewSocket();
        var server = new ProbeServer(socket);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            await group.RegisterAsync(server);
            await server.BindAsync(NewAddress());
            Assert.False(server.ShouldClose(new IOException()));
            await server.CloseAsync();
        }
        finally { await group.StopAsync(); }
    }

    [Fact]
    public async Task TestIsActiveFalseAfterClose()
    {
        RequireDomainSockets();
        var server = new DomainServerSocketChannel();
        var group = new DefaultEventLoopGroup(1);
        try
        {
            await group.RegisterAsync(server);
            await server.BindAsync(NewAddress());
            Assert.True(server.IsActive);
            Assert.True(server.IsOpen);
            await server.CloseAsync();
            Assert.False(server.IsOpen);
            Assert.False(server.IsActive);
        }
        finally { await group.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestCreateChannelFromJdkChannel(bool bindSocket)
    {
        RequireDomainSockets();
        using var socket = NewSocket();
        if (bindSocket)
        {
            socket.Bind(NewAddress());
            socket.Listen(128);
        }
        var server = new DomainServerSocketChannel(socket);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            await group.RegisterAsync(server);
            Assert.True(server.IsOpen);
            Assert.Equal(bindSocket, server.IsActive);
            await server.CloseAsync();
            Assert.False(server.IsOpen);
            Assert.False(server.IsActive);
        }
        finally { await group.StopAsync(); }
    }

    internal static Socket NewSocket() => new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    internal static UnixDomainSocketEndPoint NewAddress() => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    internal static void RequireDomainSockets()
    {
        if (!Socket.OSSupportsUnixDomainSockets) Assert.Skip("This host does not support Unix domain sockets.");
    }

    // Override just to to be able to call directly via unit tests.
    private sealed class ProbeServer(Socket socket) : DomainServerSocketChannel(socket)
    {
        internal bool ShouldClose(Exception error) => CloseOnReadError(error);
    }
}
