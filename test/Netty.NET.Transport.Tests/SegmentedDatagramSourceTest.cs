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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class SegmentedDatagramSourceTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSendSegmentedDatagramPacket(bool adaptive) => Run(adaptive, composite: false, gro: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSendSegmentedDatagramPacketComposite(bool adaptive) => Run(adaptive, composite: true, gro: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSendAndReceiveSegmentedDatagramPacket(bool adaptive) => Run(adaptive, composite: false, gro: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSendAndReceiveSegmentedDatagramPacketComposite(bool adaptive) => Run(adaptive, composite: true, gro: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSendAndReceiveSegmentedDatagramPacketWithScatteringRead(bool adaptive) =>
        Run(adaptive, composite: false, gro: true, scatteringRead: true);

    private static async Task Run(bool adaptive, bool composite, bool gro, bool scatteringRead = false)
    {
        if (gro) RequireGro(AddressFamily.InterNetwork);
        var token = TestContext.Current.CancellationToken;
        using var adaptiveAllocator = adaptive ? new AdaptiveByteBufAllocator() : null;
        IByteBufAllocator allocator = adaptiveAllocator ?? (IByteBufAllocator)PooledByteBufAllocator.Default;
        var loop = new DefaultEventLoopGroup(2);
        try
        {
            var sender = await NewBootstrap(loop, allocator, new Discard()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);

            const int numBuffers = 16;
            const int segmentSize = 512;
            const int bufferCapacity = numBuffers * segmentSize;
            var capture = new Capture(numBuffers, segmentSize);
            var server = NewBootstrap(loop, allocator, capture);
            if (gro)
            {
                // Enable GRO and also ensure we can read everything with one read as otherwise
                // we will drop things on the floor.
                server.Option(ChannelOption.UDP_GRO, true);
                if (scatteringRead)
                {
                    // Room for two batches, so the batch is read with recvmmsg instead of recvmsg.
                    server.Option(ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, bufferCapacity)
                        .Option(ChannelOption.RECVBUF_ALLOCATOR, new FixedRecvByteBufAllocator(2 * bufferCapacity));
                }
                else
                    server.Option(ChannelOption.RECVBUF_ALLOCATOR, new FixedRecvByteBufAllocator(bufferCapacity));
            }
            var receiver = await server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            Assert.True(receiver.Config.TryGetOption(ChannelOption.UDP_GRO, out bool configured));
            Assert.Equal(gro, configured);

            ByteBuf buffer;
            if (composite)
            {
                var parts = Unpooled.CompositeBuffer(numBuffers);
                for (int i = 0; i < numBuffers; i++)
                    parts.AddComponent(Unpooled.DirectBuffer(segmentSize).WriteZero(segmentSize), true);
                buffer = parts;
            }
            else
                buffer = Unpooled.DirectBuffer(bufferCapacity).WriteZero(bufferCapacity);

            await sender.WriteAndFlushAsync(new SegmentedDatagramPacket(buffer, segmentSize, (IPEndPoint)receiver.LocalAddress), token);
            await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally { await loop.StopAsync(); }
    }

    private static Bootstrap NewBootstrap(DefaultEventLoopGroup loop, IByteBufAllocator allocator, IChannelHandler handler) =>
        new Bootstrap().Group(loop).ChannelFactory(static () => new DatagramChannel())
            .Option(ChannelOption.ALLOCATOR, allocator).Handler(handler);

    internal static void RequireGro(AddressFamily family)
    {
        // Only supported for the native epoll transport.
        if (!OperatingSystem.IsLinux()) Assert.Skip("Kernel UDP_GRO is available only on Linux.");
        if (family == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 unavailable.");
        using var receiver = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            LinuxUdpOffload.SetGro(receiver, true);
            // Match the source capability probe with an integer socket option, independent of the
            // channel's per-message sendmsg path.
            receiver.SetRawSocketOption(17, 103, BitConverter.GetBytes(512));
        }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
        {
            Assert.Skip("Kernel does not support UDP_GRO.");
        }
    }

    private sealed class Discard : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        {
            // Nothing will be sent.
        }
    }

    private sealed class Capture(int expected, int segmentSize) : SimpleChannelInboundHandler<DatagramPacket>
    {
        private int _received;
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet)
        {
            Assert.Equal(segmentSize, packet.Content.ReadableBytes);
            if (++_received == expected) Completed.TrySetResult();
        }

        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => Completed.TrySetException(cause);
    }
}
