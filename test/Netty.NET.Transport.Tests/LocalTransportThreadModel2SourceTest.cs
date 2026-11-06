/*
 * Copyright 2012 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent teardown.
public class LocalTransportThreadModel2SourceTest
{
    private const int MessagesPerConnection = 4;

    [Fact(Timeout = 15000)]
    public async Task TestSocketReuse()
    {
        var token = TestContext.Current.CancellationToken;
        var servers = new DefaultEventLoopGroup(4);
        var clients = new DefaultEventLoopGroup(4);
        var address = new LocalAddress(typeof(LocalTransportThreadModel2SourceTest).FullName!);
        var serverHandler = new LocalHandler("SERVER");
        var clientHandler = new LocalHandler("CLIENT");
        try
        {
            var clientBootstrap = new Bootstrap().Group(clients).Channel<LocalChannel>()
                .RemoteAddress(address).Handler(clientHandler);
            await new ServerBootstrap().Group(servers).Channel<LocalServerChannel>()
                .ChildHandler(serverHandler).BindAsync(address, token);
            const int count = 100;
            for (int i = 1; i <= count; i++)
            {
                var channel = await clientBootstrap.ConnectAsync(cancellationToken: token);
                // SPIN until we get what we are looking for.
                int target = i * MessagesPerConnection;
                while (serverHandler.Count != target || clientHandler.Count != target)
                    await Task.Delay(50, token);
                await CloseAsync(channel, clientHandler, token);
            }
            Assert.Equal(count * 2 * MessagesPerConnection, serverHandler.Count + clientHandler.Count);
        }
        finally { await Task.WhenAll(clients.StopAsync(), servers.StopAsync()); }
    }

    private static async Task CloseAsync(IChannel channel, LocalHandler handler, CancellationToken token)
    {
        // we want to make sure we actually shutdown IN the event loop
        await channel.EventLoop.SubmitAsync(async () =>
        {
            // Wait until all messages are flushed before closing the channel.
            if (handler.LastWrite is { } write) await write.WaitAsync(token);
            await channel.CloseAsync(token);
        }, token);
        // Wait until the connection is closed or the connection attempt fails.
        await channel.CloseCompletion.WaitAsync(token);
    }

    [Theory(Timeout = 5000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedConnectClosesRegisteredChannelAndReleasesItsAddress(bool alreadyBound)
    {
        var token = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        var local = new LocalAddress(Guid.NewGuid().ToString());
        var remote = new LocalAddress(Guid.NewGuid().ToString());
        try
        {
            var channel = new LocalChannel();
            await group.RegisterAsync(channel, token);
            if (alreadyBound)
            {
                await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                    .ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync(remote, token);
                await channel.BindAsync(local, token);
                await Assert.ThrowsAsync<ChannelException>(() => channel.ConnectAsync(remote, local, token));
            }
            else
            {
                var error = await Assert.ThrowsAsync<SocketException>(() => channel.ConnectAsync(remote, local, token));
                Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            }

            await channel.CloseCompletion.WaitAsync(token);
            Assert.False(channel.IsOpen);
            Assert.False(channel.IsRegistered);
            var replacement = new LocalChannel();
            await group.RegisterAsync(replacement, token);
            await replacement.BindAsync(local, token);
            Assert.Equal(local, replacement.LocalAddress);
        }
        finally { await group.StopAsync(); }
    }

    [Sharable]
    private sealed class LocalHandler(string name) : ChannelInboundHandlerAdapter
    {
        private volatile Task _lastWrite;
        private int _count;
        internal Task LastWrite => _lastWrite;
        internal int Count => Volatile.Read(ref _count);

        public override void ChannelActive(ChannelHandlerContext context)
        {
            for (int i = 0; i < MessagesPerConnection; i++)
                _lastWrite = context.Channel.WriteAsync($"{name} {i}");
            context.Channel.Flush();
        }

        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            Interlocked.Increment(ref _count);
            ReferenceCountUtil.Release(message);
        }
    }
}
