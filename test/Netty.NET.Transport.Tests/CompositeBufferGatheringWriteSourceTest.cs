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
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source waits for and closes its channels independently of the test cancellation.
public class CompositeBufferGatheringWriteSourceTest : AbstractSocketTest
{
    private const int EXPECTED_BYTES = 20;

    [Fact(Timeout = 10000)]
    public async Task TestSingleCompositeBufferWrite()
    {
        await Run(TestContext.Current, TestSingleCompositeBufferWrite0);
    }

    // CLR: xUnit1024 forbids overloading the TestSingleCompositeBufferWrite test name.
    private async Task TestSingleCompositeBufferWrite0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            // CLR: CountDownLatch(1) is a TaskCompletionSource, and AtomicReference<Object> is a StrongBox<object>
            // updated with Interlocked.
            TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            StrongBox<object> clientReceived = new StrongBox<object>();
            // CLR: ActionChannelInitializer carries the initChannel body of each anonymous ChannelInitializer, and the
            // anonymous handlers they add are the nested SingleCompositeBufferServerHandler and ClientHandler classes.
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                    ch.Pipeline.AddLast(new SingleCompositeBufferServerHandler())));
            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
                    ch.Pipeline.AddLast(new ClientHandler(this, EXPECTED_BYTES, clientReceived, latch))));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);

            ByteBuf expected = NewCompositeBuffer(clientChannel.Alloc());
            await latch.Task;
            object received = Volatile.Read(ref clientReceived.Value);
            if (received is ByteBuf)
            {
                ByteBuf actual = (ByteBuf) received;
                Assert.Equal(expected, actual);
                expected.Release();
                actual.Release();
            }
            else
            {
                expected.Release();
                throw (Exception) received;
            }
        }
        finally
        {
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
        }
    }

    [Fact(Timeout = 10000)]
    public async Task TestCompositeBufferPartialWriteDoesNotCorruptData()
    {
        await Run(TestContext.Current, TestCompositeBufferPartialWriteDoesNotCorruptData0);
    }

    protected virtual void CompositeBufferPartialWriteDoesNotCorruptDataInitServerConfig(IChannelConfig config,
                                                                                         int soSndBuf)
    {
    }

    // CLR: xUnit1024 forbids overloading the TestCompositeBufferPartialWriteDoesNotCorruptData test name.
    private async Task TestCompositeBufferPartialWriteDoesNotCorruptData0(ServerBootstrap sb, Bootstrap cb)
    {
        // The scenario is the following:
        // Limit SO_SNDBUF so that a single buffer can be written, and part of a CompositeByteBuf at the same time.
        // We then write the single buffer, the CompositeByteBuf, and another single buffer and verify the data is not
        // corrupted when we read it on the other side.
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            const int soSndBuf = 1024;
            IByteBufAllocator alloc = IByteBufAllocator.Default;
            ByteBuf expectedContent = alloc.Buffer(soSndBuf * 2);
            expectedContent.WriteBytes(NewRandomBytes(expectedContent.WritableBytes));
            TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            StrongBox<object> clientReceived = new StrongBox<object>();
            sb.ChildOption(ChannelOption.SO_SNDBUF, soSndBuf)
              .ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                    ch.Pipeline.AddLast(new PartialWriteServerHandler(
                            this, soSndBuf, expectedContent, clientReceived, latch))));
            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
                    ch.Pipeline.AddLast(new ClientHandler(
                            this, expectedContent.ReadableBytes, clientReceived, latch))));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);

            await latch.Task;
            object received = Volatile.Read(ref clientReceived.Value);
            if (received is ByteBuf)
            {
                ByteBuf actual = (ByteBuf) received;
                Assert.Equal(expectedContent, actual);
                expectedContent.Release();
                actual.Release();
            }
            else
            {
                expectedContent.Release();
                throw (Exception) received;
            }
        }
        finally
        {
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
        }
    }

    // CLR: the anonymous ChannelFutureListener becomes a continuation of the write Task (this port's outbound
    // operations return Tasks, IChannel.cs); IsCompletedSuccessfully is ChannelFuture.isSuccess().
    private static void CloseOnFailure(Task future, IChannelHandlerContext ctx)
    {
        _ = future.ContinueWith(f =>
        {
            if (!f.IsCompletedSuccessfully)
            {
                _ = ctx.CloseAsync();
            }
            // Otherwise wait for the client to ack that it received everything
            // (see channelRead below) before closing. Closing right after the write
            // completes races with the client actually having read the data.
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CLR: java.io.IOException covers the JDK's socket I/O failures, but a CLR SocketException derives from
    // Win32Exception, not IOException (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception),
    // so both types take the source's IOException branch, as AbstractNioByteChannel's read-error handling does.
    private static bool IsIOException(Exception cause) => cause is IOException || cause is SocketException;

    private sealed class SingleCompositeBufferServerHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            CloseOnFailure(ctx.WriteAndFlushAsync(NewCompositeBuffer(ctx.Alloc())), ctx);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            // Any inbound data from the client is the ack that it received everything.
            ReferenceCountUtil.Release(msg);
            _ = ctx.CloseAsync();
        }
    }

    private sealed class PartialWriteServerHandler(
            CompositeBufferGatheringWriteSourceTest owner, int soSndBuf, ByteBuf expectedContent,
            StrongBox<object> clientReceived, TaskCompletionSource latch) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            owner.CompositeBufferPartialWriteDoesNotCorruptDataInitServerConfig(ctx.Channel.Config,
                    soSndBuf);
            // First single write
            int offset = soSndBuf - 100;
            _ = ctx.WriteAsync(expectedContent.RetainedSlice(expectedContent.ReaderIndex, offset));

            // Build and write CompositeByteBuf
            CompositeByteBuf compositeByteBuf = ctx.Alloc().CompositeBuffer();
            compositeByteBuf.AddComponent(
                    expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset, 50), true);
            offset += 50;
            compositeByteBuf.AddComponent(
                    expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset, 200), true);
            offset += 200;
            _ = ctx.WriteAsync(compositeByteBuf);

            // Write a single buffer that is smaller than the second component of the CompositeByteBuf
            // above but small enough to fit in the remaining space allowed by the soSndBuf amount.
            _ = ctx.WriteAsync(expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset, 50));
            offset += 50;

            // Write the remainder of the content
            CloseOnFailure(ctx.WriteAndFlushAsync(expectedContent.RetainedSlice(expectedContent.ReaderIndex + offset,
                    expectedContent.ReadableBytes - expectedContent.ReaderIndex - offset)), ctx);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            // Any inbound data from the client is the ack that it received everything.
            ReferenceCountUtil.Release(msg);
            _ = ctx.CloseAsync();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            // IOException is fine as it will also close the channel and may just be a connection reset.
            if (!IsIOException(cause))
            {
                Volatile.Write(ref clientReceived.Value, cause);
                latch.TrySetResult();
            }
            else if (!cause.Message.Contains("reset"))
            {
                owner.logger.Warn("{} server got weird exception",
                        owner.GetType(), cause);
            }
        }
    }

    // CLR: one named class replaces the two anonymous client handlers, which differ only in the number of bytes they
    // expect (EXPECTED_BYTES or expectedContent.readableBytes()).
    private sealed class ClientHandler(
            CompositeBufferGatheringWriteSourceTest owner, int expectedBytes,
            StrongBox<object> clientReceived, TaskCompletionSource latch) : ChannelInboundHandlerAdapter
    {
        private ByteBuf aggregator;
        private bool acked;

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            aggregator = ctx.Alloc().Buffer(expectedBytes);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            try
            {
                if (msg is ByteBuf)
                {
                    aggregator.WriteBytes((ByteBuf) msg);
                    if (!acked && aggregator.ReadableBytes >= expectedBytes)
                    {
                        acked = true;
                        if (Interlocked.CompareExchange(ref clientReceived.Value, aggregator, null) == null)
                        {
                            latch.TrySetResult();
                        }
                        // Ack so the server knows it is safe to close.
                        _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer(1).WriteByte(0));
                    }
                }
            }
            finally
            {
                ReferenceCountUtil.Release(msg);
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            // IOException is fine as it will also close the channel and may just be a connection reset.
            if (!IsIOException(cause))
            {
                Volatile.Write(ref clientReceived.Value, cause);
                latch.TrySetResult();
            }
            else if (!cause.Message.Contains("reset"))
            {
                owner.logger.Warn("{} client got weird exception",
                        owner.GetType(), cause);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            if (Interlocked.CompareExchange(ref clientReceived.Value, aggregator, null) == null)
            {
                try
                {
                    Assert.Equal(expectedBytes, aggregator.ReadableBytes);
                }
                catch (Exception cause)
                {
                    aggregator.Release();
                    aggregator = null;
                    Volatile.Write(ref clientReceived.Value, cause);
                }
                finally
                {
                    latch.TrySetResult();
                }
            }
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
        // CLR: Random.Shared ("a thread-safe Random instance that may be used concurrently from any thread",
        // https://learn.microsoft.com/en-us/dotnet/api/system.random.shared) replaces ThreadLocalRandom.current().
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
