/*
 * Copyright 2022 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// CLR: supplemental network I/O contracts shared by xUnit and the NativeAOT consumer executable
// (test/Netty.NET.Transport.NativeAot.Tests). The original GraalVM native-image programs
// (testsuite-native-image*) are recorded as jvm-only-replaced; these scenarios check that real NIO socket I/O
// (bootstrap, event loops, TCP accept/connect/read/write and UDP send/receive) survives native compilation.
internal static class NativeAotNetworkScenarios
{
    internal const int Count = 2;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    internal static void Run(int scenario)
    {
        switch (scenario)
        {
            case 0: TcpEchoOverLoopback(); break;
            case 1: UdpDatagramOverLoopback(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static byte[] Payload(int length)
    {
        var payload = new byte[length];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251);
        return payload;
    }

    private static void TcpEchoOverLoopback()
    {
        var group = new MultiThreadIoEventLoopGroup(2, NioIoHandler.NewFactory());
        IChannel server = null, client = null;
        try
        {
            // Larger than one read so that the echo crosses several reads, writes and flushes.
            var payload = Payload(256 * 1024);
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = new ServerBootstrap().Group(group).Channel<NioServerSocketChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new Echo())))
                .BindAsync(IPAddress.Loopback, 0).WaitAsync(Timeout).GetAwaiter().GetResult();
            client = new Bootstrap().Group(group).Channel<NioSocketChannel>()
                .Handler(new Collector(payload.Length, received))
                .ConnectAsync(server.LocalAddress).WaitAsync(Timeout).GetAwaiter().GetResult();
            client.WriteAndFlushAsync(Unpooled.WrappedBuffer(payload)).WaitAsync(Timeout).GetAwaiter().GetResult();
            var echoed = received.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            Check(echoed.AsSpan().SequenceEqual(payload), "The TCP echo changed the payload.");
        }
        finally
        {
            client?.CloseAsync().Wait(Timeout);
            server?.CloseAsync().Wait(Timeout);
            group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)).Wait(Timeout);
        }
    }

    private static void UdpDatagramOverLoopback()
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannel receiver = null, sender = null;
        try
        {
            var payload = Payload(1024);
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiver = new Bootstrap().Group(group).ChannelFactory(() => new NioDatagramChannel(AddressFamily.InterNetwork))
                .Handler(new DatagramCollector(received))
                .BindAsync(IPAddress.Loopback, 0).WaitAsync(Timeout).GetAwaiter().GetResult();
            sender = new Bootstrap().Group(group).ChannelFactory(() => new NioDatagramChannel(AddressFamily.InterNetwork))
                .Handler(new ChannelInboundHandlerAdapter())
                .BindAsync(IPAddress.Loopback, 0).WaitAsync(Timeout).GetAwaiter().GetResult();
            sender.WriteAndFlushAsync(new DatagramPacket(Unpooled.WrappedBuffer(payload), (IPEndPoint)receiver.LocalAddress))
                .WaitAsync(Timeout).GetAwaiter().GetResult();
            var datagram = received.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            Check(datagram.AsSpan().SequenceEqual(payload), "The UDP datagram changed.");
        }
        finally
        {
            sender?.CloseAsync().Wait(Timeout);
            receiver?.CloseAsync().Wait(Timeout);
            group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)).Wait(Timeout);
        }
    }

    private sealed class Echo : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg) => _ = ctx.WriteAsync(msg);
        public override void ChannelReadComplete(IChannelHandlerContext ctx) => ctx.Flush();
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause) => ctx.CloseAsync();
    }

    private sealed class Collector(int expected, TaskCompletionSource<byte[]> received) : ChannelInboundHandlerAdapter
    {
        private readonly MemoryStream _bytes = new();

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            var buffer = (ByteBuf)msg;
            try
            {
                var chunk = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(chunk);
                _bytes.Write(chunk);
                if (_bytes.Length >= expected) received.TrySetResult(_bytes.ToArray());
            }
            finally { buffer.Release(); }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause) => received.TrySetException(cause);
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            received.TrySetException(new IOException("The connection closed before the echo completed."));
            ctx.FireChannelInactive();
        }
    }

    private sealed class DatagramCollector(TaskCompletionSource<byte[]> received) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            var packet = (DatagramPacket)msg;
            try
            {
                var bytes = new byte[packet.Content.ReadableBytes];
                packet.Content.ReadBytes(bytes);
                received.TrySetResult(bytes);
            }
            finally { ReferenceCountUtil.Release(packet); }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause) => received.TrySetException(cause);
    }
}
