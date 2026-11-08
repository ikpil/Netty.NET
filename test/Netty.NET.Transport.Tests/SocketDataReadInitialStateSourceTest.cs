/*
 * Copyright 2018 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Source quiet windows and independently owned cleanup.
public class SocketDataReadInitialStateSourceTest
{
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestAutoReadOffNoDataReadUntilReadCalled(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var serverReady = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptorRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await fixture.Server.Option(ChannelOption.AUTO_READ, false).ChildOption(ChannelOption.AUTO_READ, false)
            .Handler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new Acceptor(acceptorRead))))
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => { channel.Pipeline.AddLast(new Echo(serverRead)); serverReady.SetResult(channel); }))
            .BindAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, false).Handler(new ReadSignal(clientRead)).ConnectAsync(server.LocalAddress);
            await client.WriteAndFlushAsync(client.Allocator.Buffer().WriteZero(1));
            // The acceptor shouldn't read any data until we call read() below, but give it some time to see if it will.
            await Task.Delay(100);
            Assert.False(acceptorRead.Task.IsCompleted);
            server.Read();
            IChannel child = await serverReady.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(child);
            // Allow some amount of time for the server peer to receive the message (which isn't expected to happen
            // until we call read() below).
            await Task.Delay(100);
            Assert.False(serverRead.Task.IsCompleted);
            child.Read();
            await serverRead.Task.WaitAsync(TestContext.Current.CancellationToken);
            // Allow some amount of time for the client to read the echo.
            await Task.Delay(100);
            Assert.False(clientRead.Task.IsCompleted);
            client.Read();
            await clientRead.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestAutoReadOnDataReadImmediately(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var serverRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = await fixture.Server.Option(ChannelOption.AUTO_READ, true).ChildOption(ChannelOption.AUTO_READ, true)
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new Echo(serverRead))))
            .BindAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, true).Handler(new ReadSignal(clientRead)).ConnectAsync(server.LocalAddress);
            await client.WriteAndFlushAsync(client.Allocator.Buffer().WriteZero(1));
            await serverRead.Task.WaitAsync(TestContext.Current.CancellationToken);
            await clientRead.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class Acceptor(TaskCompletionSource signal) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { signal.TrySetResult(); context.FireChannelRead(message); }
    }
    private sealed class Echo(TaskCompletionSource signal) : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        { _ = context.WriteAndFlushAsync(message.RetainedDuplicate()); signal.TrySetResult(); }
    }
    private sealed class ReadSignal(TaskCompletionSource signal) : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message) => signal.TrySetResult();
    }
}
#pragma warning restore xUnit1051
