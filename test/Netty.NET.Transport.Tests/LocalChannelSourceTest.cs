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
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051
public partial class LocalChannelSourceTest
{
    private static readonly LocalAddress TestAddress = new(typeof(LocalChannelSourceTest));
    [Fact]
    public async Task TestLocalAddressReuse()
    {
        await using var groups = new Groups();
        for (int i = 0; i < 2; i++)
        {
            var clientBootstrap = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
            var serverBootstrap = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new TestHandler())));
            IChannel server = null, client = null;
            try
            {
                // Start server
                server = await serverBootstrap.BindAsync(TestAddress);
                var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Connect to the server
                client = await clientBootstrap.ConnectAsync(server.LocalAddress).WaitAsync(TimeSpan.FromSeconds(10));
                IChannel clientCopy = client;
                client.EventLoop.Execute(() =>
                {
                    // Send a message event up the pipeline.
                    clientCopy.Pipeline.FireChannelRead("Hello, World");
                    latch.TrySetResult();
                });
                await latch.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Close the channel
                await CloseChannel(client);
                await CloseChannel(server);
                await server.CloseFuture;
                Assert.Null(LocalChannelRegistry.Get(TestAddress));
            }
            finally
            {
                await CloseChannel(client);
                await CloseChannel(server);
            }
        }
    }

    [Fact]
    public async Task TestConnectionRefused()
    {
        await using var groups = new Groups();
        var bootstrap = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new TestHandler());
        var error = await Assert.ThrowsAsync<SocketException>(() => bootstrap.ConnectAsync(LocalAddress.ANY));
        Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
    }

    [Fact]
    public async Task TestConnectedAlready()
    {
        await using var groups = new Groups();
        Exception cause = null;
        var clientBootstrap = new Bootstrap().Group(groups.First).Channel<LocalChannel>().Handler(new ExceptionCatcher(error => cause = error));
        var serverBootstrap = new ServerBootstrap().Group(groups.Second).Channel<LocalServerChannel>()
            .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new TestHandler())));
        IChannel server = null, client = null;
        try
        {
            // Start server
            server = await serverBootstrap.BindAsync(TestAddress);
            // Connect to the server
            client = await clientBootstrap.ConnectAsync(server.LocalAddress).WaitAsync(TimeSpan.FromSeconds(10));
            var error = await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(server.LocalAddress));
            // CLR: SocketError.IsConnected stands for AlreadyConnectedException.
            Assert.Equal(SocketError.IsConnected, error.SocketErrorCode);
            await client.CloseAsync();
            Assert.Null(cause);
        }
        finally
        {
            await CloseChannel(client);
            await CloseChannel(server);
        }
    }

    private static Task CloseChannel(IChannel channel)
    {
        return channel?.CloseAsync() ?? Task.CompletedTask;
    }
    private class TestHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.SafeRelease(msg);
        }
    }
    private sealed class ExceptionCatcher(Action<Exception> caught) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            caught(cause);
        }
    }
    private sealed class Groups : IAsyncDisposable
    {
        internal readonly MultiThreadIoEventLoopGroup First = new(2, LocalIoHandler.NewFactory());
        internal readonly MultiThreadIoEventLoopGroup Second = new(2, LocalIoHandler.NewFactory());
        internal readonly MultiThreadIoEventLoopGroup Shared = new(1, LocalIoHandler.NewFactory());
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(First.StopAsync(), Second.StopAsync(), Shared.StopAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
