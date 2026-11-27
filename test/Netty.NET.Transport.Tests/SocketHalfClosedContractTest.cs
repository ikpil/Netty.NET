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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent socket/group cleanup.
public class SocketHalfClosedContractTest
{
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnActualHalfCloseAlsoEmitsEachAutoReadEventOnlyOnce(bool adaptive)
    {
        // The source testHalfClosureOnlyOneEventWhenAutoRead casts the context rather than its channel, so its peer
        // closes instead of half-closing; this supplemental test checks the same counters after a real half-close.
        await using var fixture = new ShutdownFixture(adaptive);
        var handler = new SingleEventReader();
        fixture.Server.ChildHandler(new HalfClosingPeer());
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Option(ChannelOption.AUTO_READ, true).Handler(handler);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        await client.CloseFuture.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.ShutdownEvents);
        Assert.Equal(1, handler.FinalEvents);
        await server.CloseAsync();
    }
    private sealed class HalfClosingPeer : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ((NioSocketChannel)ctx.Channel).ShutdownOutputAsync();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }
    private sealed class SingleEventReader : ChannelInboundHandlerAdapter
    {
        internal int ShutdownEvents, FinalEvents;
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (ReferenceEquals(evt, ChannelInputShutdownEvent.INSTANCE))
            {
                Interlocked.Increment(ref ShutdownEvents);
            }
            else if (ReferenceEquals(evt, ChannelInputShutdownReadComplete.INSTANCE))
            {
                Interlocked.Increment(ref FinalEvents);
                _ = ctx.Executor.ScheduleAsync(() => { _ = ctx.CloseAsync(); }, TimeSpan.FromMilliseconds(100));
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task WriteFailurePreservesTheSourceAutoClosePolicyAndQueuedOwnership(bool autoClose, int errorKind)
    {
        await using var fixture = new ShutdownFixture(false);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Exception error = errorKind switch
        {
            0 => new IOException("write failure"),
            1 => new SocketException((int)SocketError.ConnectionReset),
            _ => new InvalidOperationException("file-region failure")
        };
        var handler = new ShutdownTestHandler();
        var channel = (HeldWriteChannel)await new Bootstrap().Group(fixture.Client.Config.Group)
            .ChannelFactory(() => new HeldWriteChannel()).Handler(handler).Option(ChannelOption.AUTO_CLOSE, autoClose)
            .Option(ChannelOption.ALLOW_HALF_CLOSURE, true).ConnectAsync(listener.LocalEndpoint);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var buffers = new[] { channel.Alloc().Buffer(1).WriteByte(1), channel.Alloc().Buffer(1).WriteByte(2), channel.Alloc().Buffer(1).WriteByte(3) };
        Task first = null, second = null, unflushed = null;
        await channel.EventLoop.SubmitAsync(() =>
        {
            first = channel.WriteAsync(buffers[0]);
            second = channel.WriteAsync(buffers[1]);
            channel.Flush();
            unflushed = channel.WriteAsync(buffers[2]);
        });
        await channel.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        channel.Failure.SetException(error);
        var firstError = await Record.ExceptionAsync(() => first);
        var secondError = await Record.ExceptionAsync(() => second);
        var unflushedError = await Record.ExceptionAsync(() => unflushed);
        foreach (var buffer in buffers)
            Assert.Equal(0, buffer.RefCnt);
        await channel.EventLoop.SubmitAsync(() => { });
        bool close = autoClose && error is IOException or SocketException;
        if (close)
        {
            Assert.Same(error, firstError);
            Assert.Same(error, secondError);
            Assert.Same(error, Assert.IsAssignableFrom<ClosedChannelException>(unflushedError).InnerException);
            Assert.False(channel.IsOpen);
            Assert.False(channel.IsActive);
            Assert.Equal(0, handler.OutputEvents);
        }
        else
        {
            Assert.Same(error, Assert.IsType<ChannelOutputShutdownException>(firstError).InnerException);
            Assert.Same(error, Assert.IsType<ChannelOutputShutdownException>(secondError).InnerException);
            Assert.Same(error, Assert.IsType<ChannelOutputShutdownException>(unflushedError).InnerException);
            Assert.True(channel.IsOpen);
            Assert.True(channel.IsActive);
            Assert.True(channel.IsOutputShutdown);
            Assert.False(channel.IsInputShutdown);
            Assert.Equal(1, handler.OutputEvents);
            await peer.GetStream().WriteAsync(new byte[] { 4 }, TestContext.Current.CancellationToken);
            Assert.Equal(4, await handler.Bytes.Reader.ReadAsync(TestContext.Current.CancellationToken));
        }
    }
    private sealed class HeldWriteChannel : NioSocketChannel
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // The non-blocking socket write of NioSocketChannel.doWrite reports the test's failure.
        // Until the failure is released the socket accepts nothing, so the write waits for OP_WRITE readiness.
        internal override long WriteSocket(ReadOnlyMemory<byte>[] buffers, int offset, int length)
        {
            Fail();
            return 0;
        }

        internal override int WriteSocket(ReadOnlyMemory<byte> buffer)
        {
            Fail();
            return 0;
        }

        private void Fail()
        {
            Started.TrySetResult();
            if (Failure.Task.IsCompleted)
            {
                Failure.Task.GetAwaiter().GetResult();
            }
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ZeroLingerCloseSendsAnAbortiveReset()
    {
        await using var fixture = new ShutdownFixture(false);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var channel = await fixture.Client.Option(ChannelOption.SO_LINGER, 0).Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(listener.LocalEndpoint);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await channel.CloseAsync();
        var error = await Assert.ThrowsAsync<SocketException>(async () =>
            await peer.Client.ReceiveAsync(new byte[1], SocketFlags.None, TestContext.Current.CancellationToken));
        Assert.Equal(SocketError.ConnectionReset, error.SocketErrorCode);
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualReadKeepsTheClrAdmissionPolicyAcrossTheLastByteAndFin(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var serverChild = new TaskCompletionSource<NioSocketChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Server.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Option(ChannelOption.AUTO_CLOSE, false)
            .ChildOption(ChannelOption.TCP_NODELAY, true).ChildHandler(new LastByteWriter(serverChild));
        var reader = new LastByteReader();
        // client.
        fixture.Client.Option(ChannelOption.ALLOW_HALF_CLOSURE, true).Option(ChannelOption.AUTO_CLOSE, false)
            .Option(ChannelOption.AUTO_READ, false).Handler(reader);
        var server = await fixture.Server.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var client = await fixture.Client.ConnectAsync(server.LocalAddress);
        client.Read();
        await reader.AllData.Task.WaitAsync(TestContext.Current.CancellationToken);
        // Now we need to trigger server half-close
        await (await serverChild.Task).ShutdownOutputAsync();
        await client.EventLoop.SubmitAsync(() => { });
        Assert.False(reader.Half.Task.IsCompleted);
        client.Read();
        await reader.Half.Task.WaitAsync(TestContext.Current.CancellationToken);
        await client.EventLoop.SubmitAsync(() => { });
        Assert.False(reader.Final.Task.IsCompleted);
        client.Read();
        await reader.Final.Task.WaitAsync(TestContext.Current.CancellationToken);
        await client.CloseFuture.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, reader.ReadCompletes);
        Assert.Equal(2, reader.EmptyReadCompletes);
        await server.CloseAsync();
    }
    private sealed class LastByteWriter(TaskCompletionSource<NioSocketChannel> child) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            child.TrySetResult((NioSocketChannel)ctx.Channel);
            var buffer = ctx.Alloc().Buffer(1);
            buffer.WriterIndex = buffer.Capacity;
            _ = ctx.WriteAndFlushAsync(buffer);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }
    private sealed class LastByteReader : ChannelInboundHandlerAdapter
    {
        private int _bytesRead, _sinceComplete;
        internal int ReadCompletes, EmptyReadCompletes;
        internal readonly TaskCompletionSource AllData = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Half = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Final = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            var buffer = (ByteBuf)msg;
            _bytesRead += buffer.ReadableBytes;
            _sinceComplete += buffer.ReadableBytes;
            buffer.Release();
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (_sinceComplete == 0)
            {
                Interlocked.Increment(ref EmptyReadCompletes);
            }
            else
            {
                _sinceComplete = 0;
            }
            Interlocked.Increment(ref ReadCompletes);
            if (_bytesRead == 1)
            {
                // Bounce this through the event loop to make sure it happens after we're done
                // with the read operation.
                ctx.Executor.Execute(() => AllData.TrySetResult());
            }
            else
            {
                ctx.Read();
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is ChannelInputShutdownEvent)
            {
                Half.TrySetResult();
            }
            else if (evt is ChannelInputShutdownReadComplete)
            {
                Final.TrySetResult();
                _ = ctx.CloseAsync();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            ctx.FireExceptionCaught(cause);
            _ = ctx.CloseAsync();
        }
    }
}
