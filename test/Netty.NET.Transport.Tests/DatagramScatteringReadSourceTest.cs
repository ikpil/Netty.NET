/*
 * Copyright 2019 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramScatteringReadSourceTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestScatteringReadPartial(bool adaptive) => ScatteringRead(adaptive, connected: false, partial: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestScatteringRead(bool adaptive) => ScatteringRead(adaptive, connected: false, partial: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestScatteringReadConnectedPartial(bool adaptive) => ScatteringRead(adaptive, connected: true, partial: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestScatteringConnectedRead(bool adaptive) => ScatteringRead(adaptive, connected: true, partial: false);

    private static async Task ScatteringRead(bool adaptive, bool connected, bool partial)
    {
        const int packetSize = 8;
        const int numPackets = 4;

        // Set the MAX_DATAGRAM_PAYLOAD_SIZE to something bigger then the actual packet size.
        // This will allow us to check if we correctly thread the received len.
        // Budget individual CLR receives for the four datagrams that the original queues for scattering.
        var receiveAllocator = new AdaptiveRecvByteBufAllocator(
            packetSize, packetSize * (partial ? numPackets / 2 : numPackets), 64 * 1024);
        receiveAllocator.MaxMessagesPerRead(numPackets);
        using var adaptiveAllocator = adaptive ? new AdaptiveByteBufAllocator() : null;
        IByteBufAllocator allocator = adaptiveAllocator ?? (IByteBufAllocator)PooledByteBufAllocator.Default;
        var loop = new DefaultEventLoopGroup(2);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var sender = await NewBootstrap(loop, allocator, new Discard()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            var capture = new Capture(sender.LocalAddress, numPackets);
            var receiver = await NewBootstrap(loop, allocator, capture)
                .Option(ChannelOption.RECVBUF_ALLOCATOR, receiveAllocator)
                .Option(ChannelOption.AUTO_READ, false)
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            if (connected) await receiver.ConnectAsync(sender.LocalAddress, cancellationToken: token);

            var writes = new Task[numPackets];
            for (int i = 0; i < writes.Length; i++)
                writes[i] = sender.WriteAsync(new DatagramPacket(allocator.DirectBuffer().WriteLong(i), (IPEndPoint)receiver.LocalAddress), token);
            sender.Flush();
            await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(10), token);

            // Enable autoread now which also triggers a read, this should cause scattering reads (recvmmsg) to happen.
            receiver.Config.SetAutoRead(true);

            await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally { await loop.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestScatteringReadWithSmallBuffer(bool adaptive) => ScatteringReadWithSmallBuffer(adaptive, connected: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestScatteringConnectedReadWithSmallBuffer(bool adaptive) => ScatteringReadWithSmallBuffer(adaptive, connected: true);

    private static async Task ScatteringReadWithSmallBuffer(bool adaptive, bool connected)
    {
        const int packetSize = 16;
        using var adaptiveAllocator = adaptive ? new AdaptiveByteBufAllocator() : null;
        IByteBufAllocator allocator = adaptiveAllocator ?? (IByteBufAllocator)PooledByteBufAllocator.Default;
        var loop = new DefaultEventLoopGroup(2);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var sender = await NewBootstrap(loop, allocator, new Discard()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            var bytes = new byte[packetSize];
            Random.Shared.NextBytes(bytes);
            var capture = new Capture(sender.LocalAddress, 1, bytes);
            var receiver = await NewBootstrap(loop, allocator, capture)
                .Option(ChannelOption.RECVBUF_ALLOCATOR, new AdaptiveRecvByteBufAllocator(1400, 1400, 64 * 1024))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            if (connected) await receiver.ConnectAsync(sender.LocalAddress, cancellationToken: token);

            await sender.WriteAndFlushAsync(new DatagramPacket(allocator.DirectBuffer().WriteBytes(bytes),
                (IPEndPoint)receiver.LocalAddress), token);

            await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally { await loop.StopAsync(); }
    }

    private static Bootstrap NewBootstrap(DefaultEventLoopGroup loop, IByteBufAllocator allocator, IChannelHandler handler) =>
        new Bootstrap().Group(loop).ChannelFactory(static () => new DatagramChannel())
            .Option(ChannelOption.ALLOCATOR, allocator).Handler(handler);

    private sealed class Discard : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        {
            // Nothing will be sent.
        }
    }

    private sealed class Capture(EndPoint sender, int expected, byte[] bytes = null) : SimpleChannelInboundHandler<DatagramPacket>
    {
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _received;
        private int _batch;

        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet)
        {
            Assert.Equal(sender, packet.Sender);
            if (bytes == null)
            {
                // Each packet contains a long which represent the write iteration.
                Assert.Equal(8, packet.Content.ReadableBytes);
                Assert.Equal((long)_received, packet.Content.ReadLong());
            }
            else
            {
                Assert.Equal(bytes.Length, packet.Content.ReadableBytes);
                var received = new byte[bytes.Length];
                packet.Content.ReadBytes(received);
                Assert.Equal(bytes, received);
            }
            _received++;
            _batch++;
        }

        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            if (bytes == null)
            {
                Assert.True(_batch > 1);
                _batch = 0;
                context.Read();
            }
            if (_received == expected) Completed.TrySetResult();
        }

        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => Completed.TrySetException(cause);
    }
}
