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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve source operation ordering and independently owned cleanup.
public partial class SocketHalfClosedSourceTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestShutdownInputAllowsOutboundWrites(bool adaptive, bool clientInitiates)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var expected = new byte[100]; Random.Shared.NextBytes(expected);
        var reader = new ShutdownReader(expected.Length);
        var writer = new ShutdownInputWriter(expected);
        fixture.Server.ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true).ChildHandler(clientInitiates ? reader : writer);
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Handler(clientInitiates ? writer : reader);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        Assert.Equal(expected, await reader.Done.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await server.CloseAsync(); await client.CloseAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestShutdownOutputAllowsInboundReads(bool adaptive, bool clientInitiates)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var expected = new byte[100]; Random.Shared.NextBytes(expected);
        var reader = new ShutdownOutputReader(expected.Length);
        var writer = new ShutdownOutputWriter(expected);
        fixture.Server.ChildOption(ChannelOption.ALLOW_HALF_CLOSURE, true).ChildHandler(clientInitiates ? writer : reader);
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Handler(clientInitiates ? reader : writer);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        Assert.Equal(expected, await reader.Done.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await server.CloseAsync(); await client.CloseAsync();
    }
    private class ShutdownReader(int expected) : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly ByteBuf _received = Unpooled.Buffer(expected);
        internal readonly TaskCompletionSource<byte[]> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void ChannelRead0(ChannelHandlerContext ctx, ByteBuf msg)
        {
            _received.WriteBytes(msg);
            if (_received.ReadableBytes == expected)
            {
                var bytes = new byte[expected]; _received.GetBytes(_received.ReaderIndex, bytes); Done.TrySetResult(bytes);
            }
        }
        public override void HandlerRemoved(ChannelHandlerContext ctx) => _received.Release();
    }
    private sealed class ShutdownInputWriter(byte[] expected) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext ctx) => _ = WriteAfterShutdown(ctx);
        private async Task WriteAfterShutdown(ChannelHandlerContext ctx)
        {
            await ((TcpSocketChannel)ctx.Channel).ShutdownInputAsync();
            await ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(expected));
        }
    }
    private sealed class ShutdownOutputReader(int expected) : ShutdownReader(expected)
    {
        public override void ChannelActive(ChannelHandlerContext ctx) => _ = ((TcpSocketChannel)ctx.Channel).ShutdownOutputAsync();
    }
    private sealed class ShutdownOutputWriter(byte[] expected) : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE)) _ = ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(expected));
            ctx.FireUserEventTriggered(evt);
        }
    }

    [Theory(Timeout = 30000)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task TestAllDataReadClosure(bool adaptive, bool autoRead, bool allowHalfClosed)
    {
        const int totalServerBytesWritten = 1024 * 16, numReadsPerReadLoop = 2;
        await using var fixture = new ShutdownFixture(adaptive);
        var writer = new ClosingWriter(totalServerBytesWritten);
        var reader = new ClosureReader(totalServerBytesWritten, autoRead, allowHalfClosed);
        fixture.Server.ChildHandler(writer);
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, allowHalfClosed).Option(ChannelOption.AUTO_READ, autoRead)
            .Option(ChannelOption.RECVBUF_ALLOCATOR, new TestNumReadsRecvByteBufAllocator(numReadsPerReadLoop)).Handler(reader);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        client.Read();
        await writer.Initialized.Task.WaitAsync(TestContext.Current.CancellationToken);
        await reader.AllData.Task.WaitAsync(TestContext.Current.CancellationToken);
        await reader.HalfClosed.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(totalServerBytesWritten / numReadsPerReadLoop + 10 > reader.ReadCompletes, "too many read complete events: " + reader.ReadCompletes);
        await client.CloseAsync(); await server.CloseAsync();
    }
    private sealed class ClosingWriter(int count) : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource Initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            var buffer = ctx.Allocator.Buffer(count); buffer.WriterIndex = buffer.Capacity;
            _ = WriteAndClose(ctx, buffer); Initialized.TrySetResult();
        }
        private static async Task WriteAndClose(ChannelHandlerContext ctx, ByteBuf buffer)
        { try { await ctx.WriteAndFlushAsync(buffer); } finally { await ctx.CloseAsync(); } }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => _ = ctx.CloseAsync();
    }
    private sealed class ClosureReader(int total, bool autoRead, bool allowHalfClosed) : ChannelInboundHandlerAdapter
    {
        private int _bytesRead;
        internal int ReadCompletes;
        internal readonly TaskCompletionSource AllData = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource HalfClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelRead(ChannelHandlerContext ctx, object message)
        { var buffer = (ByteBuf)message; _bytesRead += buffer.ReadableBytes; buffer.Release(); }
        public override void UserEventTriggered(ChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE) && allowHalfClosed) HalfClosed.TrySetResult();
            else if (ReferenceEquals(evt, ChannelInputShutdownReadComplete.INSTANCE)) _ = ctx.CloseAsync();
        }
        public override void ChannelInactive(ChannelHandlerContext ctx) { if (!allowHalfClosed) HalfClosed.TrySetResult(); }
        public override void ChannelReadComplete(ChannelHandlerContext ctx)
        {
            Interlocked.Increment(ref ReadCompletes);
            if (_bytesRead == total) AllData.TrySetResult();
            if (!autoRead) ctx.Read();
        }
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => _ = ctx.CloseAsync();
    }
    /**
     * Designed to read a single byte at a time to control the number of reads done at a fine granularity.
     */
    private sealed class TestNumReadsRecvByteBufAllocator(int numReads) : IRecvByteBufAllocator
    {
        public IRecvByteBufAllocator.IHandle NewHandle() => new Handle(numReads);
        private sealed class Handle(int numReads) : IRecvByteBufAllocator.IExtendedHandle
        {
            private int _attempted, _last, _messages;
            public ByteBuf Allocate(IByteBufAllocator alloc) => alloc.IoBuffer(Guess(), Guess());
            public int Guess() => 1; // only ever allocate buffers of size 1 to ensure the number of reads is controlled.
            public void Reset(IChannelConfig config) => _messages = 0;
            public void IncMessagesRead(int messages) => _messages += messages;
            public void LastBytesRead(int bytes) => _last = bytes;
            public int LastBytesRead() => _last;
            public void AttemptedBytesRead(int bytes) => _attempted = bytes;
            public int AttemptedBytesRead() => _attempted;
            public bool ContinueReading() => _messages < numReads;
            public bool ContinueReading(Func<bool> supplier) => ContinueReading() && supplier();
            public void ReadComplete()
            {
                // Nothing needs to be done or adjusted after each read cycle is completed.
            }
        }
    }
}
