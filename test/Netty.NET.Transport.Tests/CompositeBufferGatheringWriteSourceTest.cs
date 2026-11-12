/*
 * Copyright 2017 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class CompositeBufferGatheringWriteSourceTest
{
    private const int EXPECTED_BYTES = 20;
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(CompositeBufferGatheringWriteSourceTest));

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestSingleCompositeBufferWrite(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        ServerBootstrap sb = fixture.Server;
        Bootstrap cb = fixture.Client;
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientReceived = new StrongBox<object>();
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new SingleServerHandler())));
            cb.Handler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new ClientHandler(clientReceived, latch))));
            serverChannel = await sb.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            ByteBuf expected = NewCompositeBuffer(clientChannel.Allocator);
            try
            {
                await latch.Task.WaitAsync(TestContext.Current.CancellationToken);
                object received = Volatile.Read(ref clientReceived.Value);
                if (received is ByteBuf actual)
                {
                    try { Assert.True(ByteBufUtil.Equals(expected, actual)); }
                    finally { actual.Release(); }
                }
                else throw (Exception)received;
            }
            finally { expected.Release(); }
        }
        finally
        {
            if (clientChannel != null) await clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            if (serverChannel != null) await serverChannel.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    protected virtual void CompositeBufferPartialWriteDoesNotCorruptDataInitServerConfig(IChannelConfig config, int soSndBuf) { }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestCompositeBufferPartialWriteDoesNotCorruptData(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        ServerBootstrap sb = fixture.Server;
        Bootstrap cb = fixture.Client;
        // The scenario is the following:
        // Limit SO_SNDBUF so that a single buffer can be written, and part of a CompositeByteBuf at the same time.
        // We then write the single buffer, the CompositeByteBuf, and another single buffer and verify the data is not
        // corrupted when we read it on the other side.
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        const int soSndBuf = 1024;
        IByteBufAllocator alloc = IByteBufAllocator.Default;
        ByteBuf expectedContent = alloc.Buffer(soSndBuf * 2);
        expectedContent.WriteBytes(NewRandomBytes(expectedContent.WritableBytes));
        try
        {
            var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientReceived = new StrongBox<object>();
            sb.ChildOption(ChannelOption.SO_SNDBUF, soSndBuf).ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                ch.Pipeline.AddLast(new PartialServerHandler(this, expectedContent, clientReceived, latch))));
            cb.Handler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new ClientHandler(clientReceived, latch, expectedContent))));
            serverChannel = await sb.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            await latch.Task.WaitAsync(TestContext.Current.CancellationToken);
            object received = Volatile.Read(ref clientReceived.Value);
            if (received is ByteBuf actual)
            {
                try { Assert.True(ByteBufUtil.Equals(expectedContent, actual)); }
                finally { actual.Release(); }
            }
            else throw (Exception)received;
        }
        finally
        {
            expectedContent.Release();
            if (clientChannel != null) await clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            if (serverChannel != null) await serverChannel.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class SingleServerHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext ctx) =>
            _ = OperationComplete(ctx.WriteAndFlushAsync(NewCompositeBuffer(ctx.Allocator), TestContext.Current.CancellationToken), ctx);
        public override void ChannelRead(ChannelHandlerContext ctx, object msg)
        {
            // Any inbound data from the client is the ack that it received everything.
            ReferenceCountUtil.Release(msg);
            _ = ctx.CloseAsync();
        }
    }

    private sealed class PartialServerHandler(CompositeBufferGatheringWriteSourceTest owner, ByteBuf expectedContent,
        StrongBox<object> clientReceived, TaskCompletionSource latch) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            const int soSndBuf = 1024;
            owner.CompositeBufferPartialWriteDoesNotCorruptDataInitServerConfig(ctx.Channel.Config, soSndBuf);
            // First single write
            int offset = soSndBuf - 100;
            _ = ctx.WriteAsync(expectedContent.RetainedSlice(expectedContent.ReaderIndex, offset), TestContext.Current.CancellationToken);

            // Build and write CompositeByteBuf
            CompositeByteBuf compositeByteBuf = ctx.Allocator.CompositeBuffer();
            compositeByteBuf.AddComponent(expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset, 50), true);
            offset += 50;
            compositeByteBuf.AddComponent(expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset, 200), true);
            offset += 200;
            _ = ctx.WriteAsync(compositeByteBuf, TestContext.Current.CancellationToken);

            // Write a single buffer that is smaller than the second component of the CompositeByteBuf
            // above but small enough to fit in the remaining space allowed by the soSndBuf amount.
            _ = ctx.WriteAsync(expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset, 50), TestContext.Current.CancellationToken);
            offset += 50;

            // Write the remainder of the content
            _ = OperationComplete(ctx.WriteAndFlushAsync(expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset,
                expectedContent.ReadableBytes - expectedContent.ReaderIndex - offset), TestContext.Current.CancellationToken), ctx);
        }
        public override void ChannelRead(ChannelHandlerContext ctx, object msg)
        {
            // Any inbound data from the client is the ack that it received everything.
            ReferenceCountUtil.Release(msg);
            _ = ctx.CloseAsync();
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause)
        {
            // IOException is fine as it will also close the channel and may just be a connection reset.
            if (cause is not (IOException or SocketException))
            {
                Interlocked.Exchange(ref clientReceived.Value, cause);
                latch.TrySetResult();
            }
            else if (!cause.Message.Contains("reset")) Logger.Warn("{} server got weird exception", owner.GetType(), cause);
        }
    }

    // CLR Task observation replaces the anonymous ChannelFutureListener while preserving its failure/ack policy.
    private static async Task OperationComplete(Task future, ChannelHandlerContext ctx)
    {
        try { await future.ConfigureAwait(false); }
        catch { await ctx.CloseAsync().ConfigureAwait(false); }
        // Otherwise wait for the client to ack that it received everything
        // (see channelRead below) before closing. Closing right after the write
        // completes races with the client actually having read the data.
    }

    private sealed class ClientHandler(StrongBox<object> clientReceived, TaskCompletionSource latch,
        ByteBuf expectedContent = null) : ChannelInboundHandlerAdapter
    {
        private ByteBuf aggregator;
        private bool acked;
        private bool published;
        public override void HandlerAdded(ChannelHandlerContext ctx) =>
            aggregator = ctx.Allocator.Buffer(expectedContent?.ReadableBytes ?? EXPECTED_BYTES);
        public override void ChannelRead(ChannelHandlerContext ctx, object msg)
        {
            try
            {
                if (msg is ByteBuf)
                {
                    aggregator.WriteBytes((ByteBuf)msg);
                    if (!acked && aggregator.ReadableBytes >= (expectedContent?.ReadableBytes ?? EXPECTED_BYTES))
                    {
                        acked = true;
                        if (Interlocked.CompareExchange(ref clientReceived.Value, aggregator, null) == null)
                        {
                            published = true;
                            latch.TrySetResult();
                        }
                        // Ack so the server knows it is safe to close.
                        _ = ctx.WriteAndFlushAsync(ctx.Allocator.Buffer(1).WriteByte(0), TestContext.Current.CancellationToken);
                    }
                }
            }
            finally { ReferenceCountUtil.Release(msg); }
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause)
        {
            // IOException is fine as it will also close the channel and may just be a connection reset.
            if (cause is not (IOException or SocketException))
            {
                Interlocked.Exchange(ref clientReceived.Value, cause);
                latch.TrySetResult();
            }
            else if (!cause.Message.Contains("reset")) Logger.Warn("{} client got weird exception", typeof(CompositeBufferGatheringWriteSourceTest), cause);
        }
        public override void ChannelInactive(ChannelHandlerContext ctx)
        {
            if (Interlocked.CompareExchange(ref clientReceived.Value, aggregator, null) == null)
            {
                published = true;
                try { Assert.Equal(expectedContent?.ReadableBytes ?? EXPECTED_BYTES, aggregator.ReadableBytes); }
                catch (Exception cause)
                {
                    aggregator.Release();
                    aggregator = null;
                    Interlocked.Exchange(ref clientReceived.Value, cause);
                }
                finally { latch.TrySetResult(); }
            }
        }
        public override void HandlerRemoved(ChannelHandlerContext ctx)
        {
            // CLR cancellation/failure cleanup only; a published aggregator belongs to the original assertion body.
            if (!published) ReferenceCountUtil.SafeRelease(aggregator);
        }
    }

    private static ByteBuf NewCompositeBuffer(IByteBufAllocator alloc)
    {
        CompositeByteBuf compositeByteBuf = alloc.CompositeBuffer();
        compositeByteBuf.AddComponent(alloc.DirectBuffer(4).WriteInt(100), true);
        compositeByteBuf.AddComponent(alloc.DirectBuffer(8).WriteLong(123), true);
        compositeByteBuf.AddComponent(alloc.DirectBuffer(8).WriteLong(456), true);
        Assert.Equal(EXPECTED_BYTES, compositeByteBuf.ReadableBytes);
        return compositeByteBuf;
    }
    private static byte[] NewRandomBytes(int size)
    {
        byte[] bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
