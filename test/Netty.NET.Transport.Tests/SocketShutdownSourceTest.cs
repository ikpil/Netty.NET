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
public class SocketShutdownOutputBySelfSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestShutdownOutput(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var handler = new ShutdownTestHandler();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var channel = (IDuplexChannel)await fixture.Client.Handler(handler).ConnectAsync(listener.LocalEndpoint);
        Assert.True(channel.IsActive); Assert.False(channel.IsOutputShutdown);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await channel.WriteAndFlushAsync(channel.Allocator.Buffer(1).WriteByte(1));
        var bytes = new byte[1];
        Assert.Equal(1, await peer.GetStream().ReadAsync(bytes, TestContext.Current.CancellationToken)); Assert.Equal(1, bytes[0]);
        Assert.True(handler.Channel.IsOpen); Assert.True(handler.Channel.IsActive);
        Assert.False(handler.Channel.IsInputShutdown); Assert.False(handler.Channel.IsOutputShutdown);
        // Make the connection half-closed and ensure read() returns -1.
        await channel.ShutdownOutputAsync();
        Assert.Equal(0, await peer.GetStream().ReadAsync(bytes, TestContext.Current.CancellationToken)); // CLR streams report EOF with zero bytes.
        Assert.True(handler.Channel.IsOpen); Assert.True(handler.Channel.IsActive);
        Assert.False(handler.Channel.IsInputShutdown); Assert.True(handler.Channel.IsOutputShutdown);
        // If half-closed, the peer should be able to write something.
        await peer.GetStream().WriteAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(1, await handler.Bytes.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestShutdownOutputAfterClosed(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var channel = (IDuplexChannel)await fixture.Client.Handler(new ShutdownTestHandler()).ConnectAsync(listener.LocalEndpoint);
        Assert.True(channel.IsActive);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await channel.CloseAsync();
        CheckThrowable(await Record.ExceptionAsync(() => channel.ShutdownInputAsync()));
        CheckThrowable(await Record.ExceptionAsync(() => channel.ShutdownOutputAsync()));
    }
    private static void CheckThrowable(Exception cause)
    {
        // Depending on OIO / NIO both are ok
        Assert.NotNull(cause);
        Assert.True(cause is IOException or SocketException, cause.ToString());
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestShutdownOutputSoLingerNoAssertError(bool adaptive) => ShutdownSoLinger(adaptive, true).WaitAsync(TestContext.Current.CancellationToken);
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestShutdownSoLingerNoAssertError(bool adaptive) => ShutdownSoLinger(adaptive, false).WaitAsync(TestContext.Current.CancellationToken);
    private static async Task ShutdownSoLinger(bool adaptive, bool output)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var channel = (IDuplexChannel)await fixture.Client.Option(ChannelOption.SO_LINGER, 1)
            .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(listener.LocalEndpoint);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        if (output) await channel.ShutdownOutputAsync();
        else await channel.ShutdownAsync();
    }
}

internal sealed class ShutdownTestHandler : SimpleChannelInboundHandler<ByteBuf>
{
    internal volatile IDuplexChannel Channel;
    internal readonly System.Threading.Channels.Channel<byte> Bytes = System.Threading.Channels.Channel.CreateUnbounded<byte>();
    internal readonly TaskCompletionSource HalfClosure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Closure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int HalfClosureCount;
    internal int OutputEvents;
    public override void ChannelActive(ChannelHandlerContext ctx) => Channel = (IDuplexChannel)ctx.Channel;
    public override void ChannelInactive(ChannelHandlerContext ctx) => Closure.TrySetResult();
    protected override void ChannelRead0(ChannelHandlerContext ctx, ByteBuf msg) => Bytes.Writer.TryWrite((byte)msg.ReadByte());
    public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
    {
        if (evt is ChannelInputShutdownEvent) { Interlocked.Increment(ref HalfClosureCount); HalfClosure.TrySetResult(); }
        else if (evt is ChannelOutputShutdownEvent) Interlocked.Increment(ref OutputEvents);
    }
}

internal sealed class ShutdownFixture : IAsyncDisposable
{
    private readonly DefaultEventLoopGroup _group = new(2);
    internal Bootstrap Client { get; }
    internal ServerBootstrap Server { get; }
    internal ShutdownFixture(bool adaptive, bool domain = false)
    {
        IByteBufAllocator allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        Client = new Bootstrap().Group(_group).Option(ChannelOption.ALLOCATOR, allocator);
        Server = new ServerBootstrap().Group(_group)
            .Option(ChannelOption.ALLOCATOR, allocator).ChildOption(ChannelOption.ALLOCATOR, allocator);
        if (domain) { Client.Channel<DomainSocketChannel>(); Server.Channel<DomainServerSocketChannel>(); }
        else { Client.Channel<TcpSocketChannel>(); Server.Channel<TcpServerSocketChannel>(); }
    }
    public async ValueTask DisposeAsync() => await _group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
}
