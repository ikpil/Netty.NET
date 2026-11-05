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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.SocketMultipleConnectSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Always close independently of the test cancellation.
public class SocketCancelWriteSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestCancelWrite(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(2);
        var sender = new TestHandler();
        var receiver = new TestHandler();
        var allocator = SelectAllocator(adaptive);
        try
        {
            var listener = await new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
                .Option(ChannelOption.ALLOCATOR, allocator).ChildOption(ChannelOption.ALLOCATOR, allocator)
                .ChildHandler(receiver).BindAsync(new IPEndPoint(Loopback(), 0), cancellation);
            var client = await new Bootstrap().Group(group).Channel<TcpSocketChannel>()
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(sender)
                .ConnectAsync(listener.LocalAddress, cancellationToken: cancellation);
            var a = Byte('a');
            var d = Byte('d');

            await CancelQueuedWrite(client, a, cancellation);
            await client.WriteAndFlushAsync(Byte('b'), cancellation);
            Task c = client.WriteAsync(Byte('c'), cancellation);
            await CancelQueuedWrite(client, d, cancellation);
            await client.WriteAndFlushAsync(Byte('e'), cancellation);
            await c.WaitAsync(cancellation);

            await receiver.Received.Task.WaitAsync(cancellation);
            await receiver.Channel.CloseAsync();
            await client.CloseAsync();
            await listener.CloseAsync();
            Assert.Null(receiver.Failure);
            Assert.Null(sender.Failure);
            Assert.Empty(sender.Bytes);
            Assert.Equal(new byte[] { (byte)'b', (byte)'c', (byte)'e' }, receiver.Bytes);
            Assert.Equal(0, a.ReferenceCount);
            Assert.Equal(0, d.ReferenceCount);
        }
        finally { await group.StopAsync(); }
    }

    private static ByteBuf Byte(char value) => Unpooled.Buffer().WriteByte((byte)value);

    private static async Task CancelQueuedWrite(IChannel channel, ByteBuf message, CancellationToken cancellation)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task write = channel.WriteAsync(message, cancel.Token);
        await channel.EventLoop.SubmitAsync(() => { }, cancellation);
        Assert.False(write.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
    }

    private sealed class TestHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal readonly List<byte> Bytes = new();
        internal readonly TaskCompletionSource Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IChannel Channel;
        internal Exception Failure;

        public override void ChannelActive(ChannelHandlerContext context) => Channel = context.Channel;

        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            while (message.IsReadable) Bytes.Add(message.ReadByte());
            if (Bytes.Count >= 3) Received.TrySetResult();
        }

        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            Failure ??= cause;
            Received.TrySetException(cause);
            _ = context.CloseAsync();
        }
    }
}
