/*
 * Copyright 2013 The Netty Project
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

#pragma warning disable xUnit1051 // Cleanup must run independently of the test timeout.
public class SocketGatheringWriteSourceTest
{
    private static readonly byte[] Data = RandomBytes(1024 * 1024);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(60);

    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestGatheringWrite(bool adaptive) =>
        Gather(Data, adaptive, false, true, TestContext.Current.CancellationToken);

    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestGatheringWriteNotAutoRead(bool adaptive) =>
        Gather(Data, adaptive, false, false, TestContext.Current.CancellationToken);

    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestGatheringWriteWithComposite(bool adaptive) =>
        Gather(Data, adaptive, true, true, TestContext.Current.CancellationToken);

    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestGatheringWriteWithCompositeNotAutoRead(bool adaptive) =>
        Gather(Data, adaptive, true, false, TestContext.Current.CancellationToken);

    // Test for https://github.com/netty/netty/issues/2647
    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestGatheringWriteBig(bool adaptive) =>
        Gather(RandomBytes(1024 * 1024 * 50), adaptive, false, true, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestGatheringWriteSameEventLoop(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(4);
        var allocator = SelectAllocator(adaptive);
        var clients = new[] { new TestHandler(true), new TestHandler(true) };
        var receivers = new[] { new Receiver(true, Data.Length), new Receiver(true, Data.Length) };
        int clientIndex = 0, receiverIndex = 0;
        try
        {
            var listener = await Server(group, allocator, true,
                new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(receivers[Interlocked.Increment(ref receiverIndex) - 1])))
                .BindAsync(new IPEndPoint(Loopback(), 0), cancellation);
            // Ensure all clients are on the same EventLoop.
            var owner = group.Next();
            var bootstrap = Client(group, allocator, true,
                new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(clients[Interlocked.Increment(ref clientIndex) - 1])))
                .Clone(owner);
            var first = await bootstrap.ConnectAsync(listener.LocalAddress, cancellationToken: cancellation);
            var second = await bootstrap.ConnectAsync(listener.LocalAddress, cancellationToken: cancellation);
            Assert.Same(first.EventLoop, second.EventLoop);

            var writes = await owner.SubmitAsync(() =>
            {
                var queued = new List<Task>();
                for (int offset = 0; offset < Data.Length;)
                {
                    int length = Math.Min(Random.Shared.Next(8192), Data.Length - offset);
                    queued.Add(first.WriteAsync(RandomBuffer(first.Allocator, Data, offset, length), cancellation));
                    queued.Add(second.WriteAsync(RandomBuffer(second.Allocator, Data, offset, length), cancellation));
                    offset += length;
                }
                return (Queued: queued, First: first.WriteAndFlushAsync(Unpooled.EmptyBuffer, cancellation),
                    Second: second.WriteAndFlushAsync(Unpooled.EmptyBuffer, cancellation));
            }, cancellation);
            await writes.First.WaitAsync(FlushTimeout, cancellation);
            await writes.Second.WaitAsync(FlushTimeout, cancellation);
            await Task.WhenAll(writes.Queued).WaitAsync(cancellation);
            for (int i = 0; i < receivers.Length; i++)
                await AssertTransfer(receivers[i], clients[i], Data, cancellation);
            await listener.CloseAsync();
        }
        finally { await group.StopAsync(); }
    }

    private static async Task Gather(byte[] data, bool adaptive, bool composite, bool autoRead, CancellationToken cancellation)
    {
        var group = new DefaultEventLoopGroup(2);
        var allocator = SelectAllocator(adaptive);
        var receiver = new Receiver(autoRead, data.Length);
        var sender = new TestHandler(autoRead);
        try
        {
            var listener = await Server(group, allocator, autoRead, receiver)
                .BindAsync(new IPEndPoint(Loopback(), 0), cancellation);
            var client = await Client(group, allocator, autoRead, sender)
                .ConnectAsync(listener.LocalAddress, cancellationToken: cancellation);
            var writes = new List<Task>();
            for (int offset = 0; offset < data.Length;)
            {
                int length = Math.Min(Random.Shared.Next(8192), data.Length - offset);
                ByteBuf message;
                if (composite && offset % 2 == 0)
                {
                    int firstLength = length / 2;
                    message = Unpooled.CompositeBuffer()
                        .AddComponent(RandomBuffer(client.Allocator, data, offset, firstLength), true)
                        .AddComponent(RandomBuffer(client.Allocator, data, offset + firstLength, length - firstLength), true);
                }
                else
                    message = RandomBuffer(client.Allocator, data, offset, length);
                writes.Add(client.WriteAsync(message, cancellation));
                offset += length;
            }

            await client.WriteAndFlushAsync(Unpooled.EmptyBuffer, cancellation).WaitAsync(FlushTimeout, cancellation);
            await Task.WhenAll(writes).WaitAsync(cancellation);
            await AssertTransfer(receiver, sender, data, cancellation);
            await listener.CloseAsync();
        }
        finally { await group.StopAsync(); }
    }

    private static async Task AssertTransfer(Receiver receiver, TestHandler sender, byte[] data, CancellationToken cancellation)
    {
        await receiver.Done.Task.WaitAsync(cancellation);
        await receiver.Channel.CloseAsync();
        await sender.Channel.CloseAsync();
        Assert.Null(receiver.Failure);
        Assert.Null(sender.Failure);
        Assert.Equal(data, receiver.Received);
    }

    private static ServerBootstrap Server(DefaultEventLoopGroup group, IByteBufAllocator allocator, bool autoRead, IChannelHandler handler) =>
        new ServerBootstrap().Group(group).Channel<TcpServerSocketChannel>()
            .Option(ChannelOption.ALLOCATOR, allocator).ChildOption(ChannelOption.ALLOCATOR, allocator)
            .ChildOption(ChannelOption.AUTO_READ, autoRead).ChildHandler(handler);

    private static Bootstrap Client(DefaultEventLoopGroup group, IByteBufAllocator allocator, bool autoRead, IChannelHandler handler) =>
        new Bootstrap().Group(group).Channel<TcpSocketChannel>().Option(ChannelOption.ALLOCATOR, allocator)
            .Option(ChannelOption.AUTO_READ, autoRead).Handler(handler);

    private static ByteBuf RandomBuffer(IByteBufAllocator allocator, byte[] data, int offset, int length) =>
        Random.Shared.Next(2) == 0 ? allocator.DirectBuffer().WriteBytes(data.AsSpan(offset, length))
            : Unpooled.WrappedBuffer(data).Slice(offset, length);

    private static byte[] RandomBytes(int count)
    {
        var data = new byte[count];
        Random.Shared.NextBytes(data);
        return data;
    }

    private class TestHandler(bool autoRead) : SimpleChannelInboundHandler<ByteBuf>
    {
        internal IChannel Channel;
        internal Exception Failure;

        public override void ChannelActive(ChannelHandlerContext context)
        {
            Channel = context.Channel;
            if (!autoRead) context.Read();
        }

        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message) { }

        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            if (!autoRead) context.Read();
            base.ChannelReadComplete(context);
        }

        public override void ExceptionCaught(ChannelHandlerContext context, Exception error)
        {
            if (Interlocked.CompareExchange(ref Failure, error, null) == null)
                _ = context.CloseAsync();
        }
    }

    private sealed class Receiver(bool autoRead, int expected) : TestHandler(autoRead)
    {
        internal readonly byte[] Received = new byte[expected];
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            int length = message.ReadableBytes;
            message.ReadBytes(Received.AsSpan(_count, length));
            _count += length;
            if (_count == Received.Length) Done.TrySetResult();
        }

        public override void ExceptionCaught(ChannelHandlerContext context, Exception error)
        {
            Done.TrySetException(error);
            base.ExceptionCaught(context, error);
        }

        public override void ChannelInactive(ChannelHandlerContext context)
        {
            Done.TrySetException(new InvalidOperationException("server closed!"));
            base.ChannelInactive(context);
        }
    }
}
