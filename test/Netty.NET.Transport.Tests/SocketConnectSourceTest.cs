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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class SocketConnectSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestCloseTwice(bool adaptive)
    {
        await using var fixture = new Fixture(adaptive, TestContext.Current.CancellationToken);
        var future = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await fixture.Server(new ChannelInboundHandlerAdapter()).BindAsync(fixture.Address, fixture.Token);
        var client = await fixture.Client(new CloseOnEvent(future)).ConnectAsync(server.LocalAddress, cancellationToken: fixture.Token);
        client.Pipeline.FireUserEventTriggered("test");
        await client.CloseAsync(fixture.Token);
        await await future.Task.WaitAsync(fixture.Token);
        await server.CloseAsync(fixture.Token);
        await server.CloseAsync(fixture.Token);
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestLocalAddressAfterConnect(bool adaptive)
    {
        await using var fixture = new Fixture(adaptive, TestContext.Current.CancellationToken);
        var local = new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await fixture.Server(new ChildAddress(local)).BindAsync(fixture.Address, fixture.Token);
        var client = await fixture.Client(new ChannelInboundHandlerAdapter()).RegisterAsync(fixture.Token);
        Assert.Null(client.LocalAddress);
        Assert.Null(client.RemoteAddress);
        await client.ConnectAsync(server.LocalAddress, cancellationToken: fixture.Token);
        AssertLocalAddress(client.LocalAddress);
        Assert.NotNull(client.RemoteAddress);
        AssertLocalAddress(await local.Task.WaitAsync(fixture.Token));
    }

    [Theory(Timeout = 3000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestChannelEventsFiredWhenClosedDirectly(bool adaptive)
    {
        await using var fixture = new Fixture(adaptive, TestContext.Current.CancellationToken);
        var events = System.Threading.Channels.Channel.CreateUnbounded<int>();
        var server = await fixture.Server(new ChannelInboundHandlerAdapter()).BindAsync(fixture.Address, fixture.Token);
        var client = fixture.Client(new ActiveEvents(events.Writer));
        // Connect and directly close again.
        Task<IChannel> connecting = client.ConnectAsync(server.LocalAddress, cancellationToken: fixture.Token);
        var close = connecting.ContinueWith(async completed =>
        {
            await (await completed).CloseAsync(fixture.Token);
        }, fixture.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
        await connecting;
        Assert.Equal(0, await events.Reader.ReadAsync(fixture.Token));
        Assert.Equal(1, await events.Reader.ReadAsync(fixture.Token));
        await close;
    }

    [Theory(Timeout = 3000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestWriteWithFastOpenBeforeConnect(bool adaptive) =>
        FastOpen(adaptive, 0, "[fastopen]", TestContext.Current.CancellationToken);

    [Theory(Timeout = 3000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestWriteWithFastOpenBeforeConnectDirectBufferReaderIndex(bool adaptive) =>
        FastOpen(adaptive, 1, "[fastopen-reader-index]", TestContext.Current.CancellationToken);

    [Theory(Timeout = 3000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestWriteWithFastOpenBeforeConnectCompositeBuffer(bool adaptive) =>
        FastOpen(adaptive, 2, "[fastopen-composite-data]", TestContext.Current.CancellationToken);

    [Theory(Timeout = 3000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestWriteWithFastOpenBeforeConnectCompositeBufferReaderIndex(bool adaptive) =>
        FastOpen(adaptive, 3, "[fastopen-composite-reader-index]", TestContext.Current.CancellationToken);

    private static async Task FastOpen(bool adaptive, int layout, string initialData, System.Threading.CancellationToken token)
    {
        await using var fixture = new Fixture(adaptive, token);
        // TFO is an almost-pure optimisation and should not change any observable behaviour in our tests.
        var server = await fixture.Server(new ActionChannelInitializer<IChannel>(child =>
            child.Pipeline.AddLast(new Echo()))).Option(ChannelOption.TCP_FASTOPEN, 5)
            .ChildOption(ChannelOption.AUTO_READ, true).BindAsync(fixture.Address, fixture.Token);
        for (int connection = 0; connection < 2; connection++)
        {
            var capture = new Capture(initialData.Length + "[normal data]".Length);
            var channel = await fixture.Client(capture).Option(ChannelOption.TCP_FASTOPEN_CONNECT, true)
                .Option(ChannelOption.AUTO_READ, true).RegisterAsync(fixture.Token);
            var initial = InitialBuffer(channel.Allocator, layout, initialData);
            var write = channel.WriteAsync(initial, fixture.Token);
            await channel.ConnectAsync(server.LocalAddress, cancellationToken: fixture.Token);
            await channel.WriteAndFlushAsync(Unpooled.CopiedBuffer("[normal data]", Encoding.ASCII), fixture.Token);
            await write;
            string result = await capture.Completed.Task.WaitAsync(fixture.Token);
            await channel.DisconnectAsync(fixture.Token);
            Assert.Equal(initialData + "[normal data]", result);
        }
    }

    private static ByteBuf InitialBuffer(IByteBufAllocator allocator, int layout, string data)
    {
        string prefix = layout is 1 or 3 ? "BAD-PREFIX-" : "";
        if (layout < 2)
        {
            var buffer = Unpooled.DirectBuffer().WriteBytes(Encoding.ASCII.GetBytes(prefix + data));
            buffer.ReaderIndex = prefix.Length;
            return buffer;
        }
        var composite = allocator.CompositeBuffer(4);
        if (prefix.Length != 0)
            composite.AddComponent(Unpooled.DirectBuffer().WriteBytes(Encoding.ASCII.GetBytes(prefix)), true);
        string[] parts = layout == 2 ? ["[fastopen-", "composite-", "data]"] :
            ["[fastopen-", "composite-reader-", "index]"];
        foreach (string part in parts)
            composite.AddComponent(Unpooled.DirectBuffer().WriteBytes(Encoding.ASCII.GetBytes(part)), true);
        composite.ReaderIndex = prefix.Length;
        return composite;
    }

    private static void AssertLocalAddress(EndPoint address)
    {
        var local = Assert.IsType<IPEndPoint>(address);
        Assert.True(local.Port > 0);
        Assert.False(local.Address.Equals(IPAddress.Any) || local.Address.Equals(IPAddress.IPv6Any));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly DefaultEventLoopGroup Group = new(2);
        internal readonly System.Threading.CancellationToken Token;
        internal readonly IPEndPoint Address = new(IPAddress.Loopback, 0);
        private readonly IByteBufAllocator _allocator;
        internal Fixture(bool adaptive, System.Threading.CancellationToken token)
        {
            Token = token;
            _allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        }
        internal ServerBootstrap Server(IChannelHandler handler) => new ServerBootstrap().Group(Group)
            .ChannelFactory(static () => new TcpServerSocketChannel()).ChildHandler(handler)
            .ChildOption(ChannelOption.ALLOCATOR, _allocator);
        internal Bootstrap Client(IChannelHandler handler) => new Bootstrap().Group(Group)
            .ChannelFactory(static () => new TcpSocketChannel()).Handler(handler).Option(ChannelOption.ALLOCATOR, _allocator);
        public async ValueTask DisposeAsync()
        {
            await Group.StopAsync();
            if (_allocator is AdaptiveByteBufAllocator adaptive) adaptive.Dispose();
        }
    }

    private sealed class CloseOnEvent(TaskCompletionSource<Task> close) : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => close.TrySetResult(context.CloseAsync());
    }

    private sealed class ChildAddress(TaskCompletionSource<EndPoint> local) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) => local.TrySetResult(context.Channel.LocalAddress);
    }

    private sealed class ActiveEvents(System.Threading.Channels.ChannelWriter<int> events) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) => events.TryWrite(0);
        public override void ChannelInactive(ChannelHandlerContext context) => events.TryWrite(1);
    }

    private sealed class Capture(int expected) : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly List<byte> _data = new();
        internal readonly TaskCompletionSource<string> Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            var bytes = new byte[message.ReadableBytes];
            message.ReadBytes(bytes);
            _data.AddRange(bytes);
            if (_data.Count >= expected)
                Completed.TrySetResult(Encoding.ASCII.GetString(_data.ToArray()));
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => Completed.TrySetException(cause);
    }

    private sealed class Echo : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            var response = context.Allocator.Buffer().WriteBytes(message, message.ReadableBytes);
            _ = context.WriteAndFlushAsync(response);
        }
    }
}
