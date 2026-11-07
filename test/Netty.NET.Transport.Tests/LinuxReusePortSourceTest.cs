/*
 * Copyright 2014 The Netty Project
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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class LinuxReusePortSourceTest
{
    private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 0);

    [Fact]
    public Task TestMultipleBindSocketChannelWithoutReusePortFails() => AssertDuplicateBindFails(tcp: true);

    [Fact]
    public Task TestMultipleBindDatagramChannelWithoutReusePortFails() => AssertDuplicateBindFails(tcp: false);

    private static async Task AssertDuplicateBindFails(bool tcp)
    {
        RequireReusePort();
        var loop = new DefaultEventLoopGroup(1);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var handler = new DiscardHandler();
            var server = NewServer(loop).ChildHandler(handler);
            var datagram = NewDatagram(loop).Handler(handler);
            Task<IChannel> Bind(EndPoint address) => tcp ? server.BindAsync(address, token) : datagram.BindAsync(address, token);
            var first = await Bind(Loopback);
            var error = await Assert.ThrowsAsync<SocketException>(() => Bind(first.LocalAddress));
            Assert.Equal(SocketError.AddressAlreadyInUse, error.SocketErrorCode);
        }
        finally { await loop.StopAsync(); }
    }

    [Fact(Timeout = 10000)]
    public async Task TestMultipleBindSocketChannel()
    {
        RequireReusePort();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(9));
        var loop = new DefaultEventLoopGroup(2);
        try
        {
            var firstHandler = new AcceptHandler();
            var secondHandler = new AcceptHandler();
            var bootstrap = NewServer(loop).Option(ChannelOption.SO_REUSEPORT, true);
            var first = await bootstrap.ChildHandler(firstHandler).BindAsync(Loopback, deadline.Token);
            var address = Assert.IsType<IPEndPoint>(first.LocalAddress);
            var second = await bootstrap.ChildHandler(secondHandler).BindAsync(address, deadline.Token);
            Assert.Equal(address, second.LocalAddress);
            while (!firstHandler.Accepted || !secondHandler.Accepted)
            {
                using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await peer.ConnectAsync(address, deadline.Token);
                peer.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            Assert.True(firstHandler.Accepted);
            Assert.True(secondHandler.Accepted);
        }
        finally { await loop.StopAsync(); }
    }

    // TODO: Unignore after making it pass on centos6-1 and debian7-1
    [Fact(Timeout = 10000, Skip = "TODO: Unignore after making it pass on centos6-1 and debian7-1")]
    public async Task TestMultipleBindDatagramChannel()
    {
        RequireReusePort();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(9));
        var loop = new DefaultEventLoopGroup(2);
        try
        {
            var firstHandler = new DatagramHandler();
            var secondHandler = new DatagramHandler();
            var bootstrap = NewDatagram(loop).Option(ChannelOption.SO_REUSEPORT, true);
            var first = await bootstrap.Handler(firstHandler).BindAsync(Loopback, deadline.Token);
            var address = Assert.IsType<IPEndPoint>(first.LocalAddress);
            var second = await bootstrap.Handler(secondHandler).BindAsync(address, deadline.Token);
            Assert.Equal(address, second.LocalAddress);
            byte[] bytes = "data"u8.ToArray();

            // fire up 16 Threads and send DatagramPackets to make sure we stress it enough to see DatagramPackets received
            // on both sockets.
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                while (!firstHandler.Received || !secondHandler.Received)
                    await sender.SendToAsync(bytes.AsMemory(), SocketFlags.None, address, deadline.Token);
            }, deadline.Token)));
            Assert.True(firstHandler.Received);
            Assert.True(secondHandler.Received);
        }
        finally { await loop.StopAsync(); }
    }

    private static ServerBootstrap NewServer(DefaultEventLoopGroup loop) =>
        new ServerBootstrap().Group(loop).ChannelFactory(static () => new TcpServerSocketChannel(AddressFamily.InterNetwork));

    private static Bootstrap NewDatagram(DefaultEventLoopGroup loop) =>
        new Bootstrap().Group(loop).ChannelFactory(static () => new DatagramChannel());

    private static void RequireReusePort()
    {
        // the last part of the version may include all kind of different things. Especially when
        // someone compiles his / her own kernel.
        // Just ignore a parse error here and use 0.
        // CLR exposes numeric version components directly, including kernels with a fourth component.
        if (!OperatingSystem.IsLinux() || Environment.OSVersion.Version < new Version(3, 9))
            Assert.Skip("SO_REUSEPORT requires Linux 3.9 or later.");
    }

    [Sharable]
    private sealed class DiscardHandler : ChannelInboundHandlerAdapter { }

    [Sharable]
    private sealed class AcceptHandler : ChannelInboundHandlerAdapter
    {
        internal volatile bool Accepted;
        public override void ChannelActive(ChannelHandlerContext context)
        {
            Accepted = true;
            _ = context.CloseAsync();
        }
    }

    [Sharable]
    private sealed class DatagramHandler : SimpleChannelInboundHandler<DatagramPacket>
    {
        internal volatile bool Received;
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket message) => Received = true;
    }
}
