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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class LinuxTcpMd5SourceTest
{
    private static readonly byte[] ServerKey = "abc"u8.ToArray();
    private static readonly byte[] BadKey = "def"u8.ToArray();
    private static readonly IReadOnlyDictionary<IPAddress, byte[]> NoKeys = new Dictionary<IPAddress, byte[]>();

    [Fact]
    public Task TestServerSocketChannelOption() => WithServer((_, server, _) =>
    {
        SetServerKeys(server, Keys(IPAddress.Loopback, ServerKey));
        SetServerKeys(server, NoKeys);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestServerOption() => WithServer(async (group, _, token) =>
    {
        var any = Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
        var server = await NewServer(group).BindAsync(new IPEndPoint(any, 0), token);
        try
        {
            SetServerKeys(server, Keys(IPAddress.Loopback, ServerKey));
            SetServerKeys(server, NoKeys);
        }
        finally { await server.CloseAsync(); }
    });

    [Fact]
    public Task TestKeyMismatch() => WithServer(async (group, server, token) =>
    {
        SetServerKeys(server, Keys(IPAddress.Loopback, ServerKey));
        await Assert.ThrowsAsync<ConnectTimeoutException>(async () =>
        {
            var client = await NewClient(group).Option(ChannelOption.TCP_MD5SIG, Keys(IPAddress.Loopback, BadKey))
                .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 1000).ConnectAsync(server.LocalAddress, cancellationToken: token);
            await client.CloseAsync(TestContext.Current.CancellationToken);
        });
    });

    [Fact]
    public Task TestKeyMatch() => WithServer(async (group, server, token) =>
    {
        SetServerKeys(server, Keys(IPAddress.Loopback, ServerKey));
        var client = await NewClient(group).Option(ChannelOption.TCP_MD5SIG, Keys(IPAddress.Loopback, ServerKey))
            .ConnectAsync(server.LocalAddress, cancellationToken: token);
        await client.CloseAsync();
    });

    internal static void SetServerKeys(IChannel server, IReadOnlyDictionary<IPAddress, byte[]> keys)
    {
        try { Assert.True(server.Config.SetOption(ChannelOption.TCP_MD5SIG, keys)); }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
        {
            // Some operating systems don't allow or support the TCP_MD5SIG option.
            // Abort the test (instead of failing) if that's the case.
            Assert.Skip("The kernel does not support TCP_MD5SIG.");
        }
    }

    internal static async Task WithServer(Func<DefaultEventLoopGroup, IChannel, CancellationToken, Task> scenario, bool ipv6 = false)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("TCP_MD5SIG requires Linux.");
        if (ipv6 && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 unavailable.");
        var token = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var server = await NewServer(group).BindAsync(
                new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0), token);
            await scenario(group, server, token);
        }
        finally { await group.StopAsync(); }
    }

    private static ServerBootstrap NewServer(DefaultEventLoopGroup group) =>
        new ServerBootstrap().Group(group).ChannelFactory(static () => new TcpServerSocketChannel())
            .ChildHandler(new ChannelInboundHandlerAdapter());

    internal static Bootstrap NewClient(DefaultEventLoopGroup group) =>
        new Bootstrap().Group(group).ChannelFactory(static () => new TcpSocketChannel()).Handler(new ChannelInboundHandlerAdapter());

    internal static IReadOnlyDictionary<IPAddress, byte[]> Keys(IPAddress address, byte[] key) =>
        new Dictionary<IPAddress, byte[]> { [address] = key };
}
