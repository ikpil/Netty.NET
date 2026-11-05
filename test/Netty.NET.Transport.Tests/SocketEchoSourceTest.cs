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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent cleanup.
public class SocketEchoSourceTest
{
    private static readonly byte[] Data = CreateData();
    private static byte[] CreateData()
    {
        var data = new byte[1048576];
        Random.Shared.NextBytes(data);
        return data;
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSimpleEcho(bool adaptive) => Echo(adaptive, false, true, false, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSimpleEchoNotAutoRead(bool adaptive) => Echo(adaptive, false, false, false, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSimpleEchoWithAdditionalExecutor(bool adaptive) => Echo(adaptive, true, true, false, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestSimpleEchoWithAdditionalExecutorNotAutoRead(bool adaptive) => Echo(adaptive, true, false, false, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task DomainEchoWithAdditionalExecutor(bool autoRead)
    {
        DomainSocketSourceTest.RequireDomainSockets();
        return Echo(false, true, autoRead, true, TestContext.Current.CancellationToken);
    }

    private static async Task Echo(bool adaptive, bool additionalExecutor, bool autoRead, bool domain, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(29));
        var loop = new DefaultEventLoopGroup(2);
        var extra = additionalExecutor ? new DefaultEventExecutorGroup(2) : null;
        IByteBufAllocator allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        var serverHandler = new EchoHandler(autoRead);
        var clientHandler = new EchoHandler(autoRead);
        try
        {
            var sb = new ServerBootstrap().Group(loop).ChildOption(ChannelOption.AUTO_READ, autoRead)
                .Option(ChannelOption.ALLOCATOR, allocator).ChildOption(ChannelOption.ALLOCATOR, allocator)
                .ChildHandler(Initialize(serverHandler, extra));
            var cb = new Bootstrap().Group(loop).Option(ChannelOption.AUTO_READ, autoRead)
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(Initialize(clientHandler, extra));
            if (domain)
            {
                sb.Channel<DomainServerSocketChannel>();
                cb.Channel<DomainSocketChannel>();
            }
            else
            {
                sb.Channel<TcpServerSocketChannel>();
                cb.Channel<TcpSocketChannel>();
            }
            EndPoint endpoint = domain ? DomainSocketSourceTest.NewAddress()
                : new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0);
            var server = await sb.BindAsync(endpoint, deadline.Token);
            var client = await cb.ConnectAsync(server.LocalAddress, cancellationToken: deadline.Token);
            var writes = new List<Task>();
            for (int i = 0; i < Data.Length;)
            {
                int length = Math.Min(Random.Shared.Next(1024 * 64), Data.Length - i);
                writes.Add(client.WriteAndFlushAsync(RandomBuffer(client.Allocator, Data, i, length), deadline.Token));
                i += length;
            }
            await Task.WhenAll(writes).WaitAsync(deadline.Token);
            await Task.WhenAll(clientHandler.Done.Task, serverHandler.Done.Task).WaitAsync(deadline.Token);
            await serverHandler.Channel.CloseAsync();
            await client.CloseAsync();
            await server.CloseAsync();
            Assert.Null(serverHandler.Failure);
            Assert.Null(clientHandler.Failure);
        }
        finally
        {
            await loop.StopAsync();
            if (extra != null) await extra.StopAsync();
        }
    }

    private static IChannelHandler Initialize(EchoHandler handler, IEventExecutorGroup executor)
        => executor == null ? handler : new ActionChannelInitializer<IChannel>(channel =>
            channel.Pipeline.AddLast(executor, "echo", handler));

    private static ByteBuf RandomBuffer(IByteBufAllocator allocator, byte[] data, int offset, int length)
        => Random.Shared.Next(2) == 0 ? allocator.DirectBuffer().WriteBytes(data.AsSpan(offset, length))
            : Unpooled.WrappedBuffer(data).Slice(offset, length);

    private sealed class EchoHandler(bool autoRead) : SimpleChannelInboundHandler<ByteBuf>
    {
        private int _counter;
        internal IChannel Channel;
        internal Exception Failure;
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            Channel = ctx.Channel;
            if (!autoRead) ctx.Read();
        }

        protected override void ChannelRead0(ChannelHandlerContext ctx, ByteBuf message)
        {
            byte[] actual = new byte[message.ReadableBytes];
            message.ReadBytes(actual);
            int last = _counter;
            for (int i = 0; i < actual.Length; i++)
                Assert.Equal(Data[i + last], actual[i]);

            // Update the counter before calling write(...) as write could in theory trigger another channelRead(...)
            // which then would use the wrong lastIdx.
            _counter += actual.Length;
            if (Channel.Parent != null)
                _ = ObserveWrite(Channel.WriteAsync(RandomBuffer(Channel.Allocator, actual, 0, actual.Length)));
            if (_counter == Data.Length) Done.TrySetResult();
        }

        private async Task ObserveWrite(Task write)
        {
            try { await write.ConfigureAwait(false); }
            catch (Exception error) { RecordFailure(error); }
        }

        public override void ChannelReadComplete(ChannelHandlerContext ctx)
        {
            try { ctx.Flush(); }
            finally { if (!autoRead) ctx.Read(); }
        }

        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause)
        {
            if (RecordFailure(cause)) _ = ctx.CloseAsync();
        }

        private bool RecordFailure(Exception cause)
        {
            if (Interlocked.CompareExchange(ref Failure, cause, null) != null) return false;
            Done.TrySetException(cause);
            return true;
        }
    }
}
