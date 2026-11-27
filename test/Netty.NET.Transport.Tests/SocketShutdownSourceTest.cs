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
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The Runner body observes TestContext.Current.CancellationToken in its blocking waits.
#pragma warning disable xUnit1051 // Socket writes and closes run independently of the test cancellation, as in the source.
public class SocketShutdownOutputBySelfSourceTest : AbstractClientSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestShutdownOutput()
    {
        await Run(TestContext.Current, TestShutdownOutput0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownOutput test name.
    private async Task TestShutdownOutput0(Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        TestHandler h = new TestHandler();
        // CLR: java.net.ServerSocket and java.net.Socket are TcpListener and TcpClient. TcpListener receives the
        // address that ServerSocket.bind(...) receives in its constructor, and Start() binds and listens.
        TcpListener ss = new TcpListener((IPEndPoint) NewSocketAddress());
        TcpClient s = null;
        ISocketChannel ch = null;
        try
        {
            ss.Start();
            ch = (ISocketChannel) await cb.Handler(h).ConnectAsync(ss.LocalEndpoint);
            Assert.True(ch.IsActive);
            Assert.False(ch.IsOutputShutdown);

            s = await ss.AcceptTcpClientAsync(cancellation);
            await ch.WriteAndFlushAsync(TestsuitePermutation.RandomBufferType(ch.Alloc(), new byte[] { 1 }, 0, 1));
            Assert.Equal(1, await ReadAsync(s.GetStream(), cancellation));

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.False(h.ch.IsInputShutdown);
            Assert.False(h.ch.IsOutputShutdown);

            // Make the connection half-closed and ensure read() returns -1.
            await ch.ShutdownOutputAsync();
            Assert.Equal(-1, await ReadAsync(s.GetStream(), cancellation));

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.False(h.ch.IsInputShutdown);
            Assert.True(h.ch.IsOutputShutdown);

            // If half-closed, the peer should be able to write something.
            s.GetStream().Write(new byte[] { 1 });
            Assert.Equal(1, (int) await h.queue.Reader.ReadAsync(cancellation));
        }
        finally
        {
            if (s != null)
            {
                s.Close();
            }
            if (ch != null)
            {
                _ = ch.CloseAsync();
            }
            ss.Stop();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestShutdownOutputAfterClosed()
    {
        await Run(TestContext.Current, TestShutdownOutputAfterClosed0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownOutputAfterClosed test name.
    private async Task TestShutdownOutputAfterClosed0(Bootstrap cb)
    {
        TestHandler h = new TestHandler();
        TcpListener ss = new TcpListener((IPEndPoint) NewSocketAddress());
        TcpClient s = null;
        try
        {
            ss.Start();
            ISocketChannel ch = (ISocketChannel) await cb.Handler(h).ConnectAsync(ss.LocalEndpoint);
            Assert.True(ch.IsActive);
            s = await ss.AcceptTcpClientAsync(TestContext.Current.CancellationToken);

            await ch.CloseAsync();
            Exception cause = await Assert.ThrowsAnyAsync<Exception>(() => ch.ShutdownInputAsync());
            CheckThrowable(cause);
            cause = await Assert.ThrowsAnyAsync<Exception>(() => ch.ShutdownOutputAsync());
            CheckThrowable(cause);
        }
        finally
        {
            if (s != null)
            {
                s.Close();
            }
            ss.Stop();
        }
    }

    // CLR: JUnit's @Disabled marks a test that "is currently disabled and should not be executed"; its value is an
    // optional reason, which the source leaves empty (https://docs.junit.org/current/api/org.junit.jupiter.api/org/
    // junit/jupiter/api/Disabled.html). xUnit's FactAttribute.Skip is the "skip reason for the test" and the test is
    // not skipped only when it is null (xunit.v3.core 4.0.2 IFactAttribute.Skip), so it names the source annotation.
    [Fact(Timeout = 30000, Skip = "@Disabled in the source (no reason given)")]
    public async Task TestWriteAfterShutdownOutputNoWritabilityChange()
    {
        await Run(TestContext.Current, TestWriteAfterShutdownOutputNoWritabilityChange0);
    }

    // CLR: xUnit1024 forbids overloading the TestWriteAfterShutdownOutputNoWritabilityChange test name.
    private async Task TestWriteAfterShutdownOutputNoWritabilityChange0(Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        TestHandler h = new TestHandler();
        TcpListener ss = new TcpListener((IPEndPoint) NewSocketAddress());
        TcpClient s = null;
        ISocketChannel ch = null;
        try
        {
            ss.Start();
            cb.Option(ChannelOption.WRITE_BUFFER_WATER_MARK, new WriteBufferWaterMark(2, 4));
            ch = (ISocketChannel) await cb.Handler(h).ConnectAsync(ss.LocalEndpoint);
            // CLR: assumeFalse(ch instanceof OioSocketChannel) always holds: OioSocketChannel is deprecated-not-ported
            // and the harness has no OIO permutation.
            Assert.True(ch.IsActive);
            Assert.False(ch.IsOutputShutdown);

            s = await ss.AcceptTcpClientAsync(cancellation);

            byte[] expectedBytes = new byte[]{ 1, 2, 3, 4, 5, 6 };
            Task writeFuture = ch.WriteAsync(Unpooled.WrappedBuffer(expectedBytes));
            h.AssertWritability(false);
            ch.Flush();
            await writeFuture;
            h.AssertWritability(true);
            for (int i = 0; i < expectedBytes.Length; ++i)
            {
                Assert.Equal(expectedBytes[i], await ReadAsync(s.GetStream(), cancellation));
            }

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.False(h.ch.IsInputShutdown);
            Assert.False(h.ch.IsOutputShutdown);

            // Make the connection half-closed and ensure read() returns -1.
            await ch.ShutdownOutputAsync();
            Assert.Equal(-1, await ReadAsync(s.GetStream(), cancellation));

            Assert.True(h.ch.IsOpen);
            Assert.True(h.ch.IsActive);
            Assert.False(h.ch.IsInputShutdown);
            Assert.True(h.ch.IsOutputShutdown);

            // If half-closed, the local endpoint shouldn't be able to write
            ISocketChannel finalCh = ch;
            // CLR: the source requests length 2 from the one-byte array, so randomBufferType itself fails
            // (IndexOutOfBoundsException there, ArgumentOutOfRangeException here) and checkThrowable rethrows it; the
            // call is kept unchanged because the source test is disabled.
            Exception cause = await Assert.ThrowsAnyAsync<Exception>(
                    () => finalCh.WriteAndFlushAsync(
                            TestsuitePermutation.RandomBufferType(finalCh.Alloc(), new byte[]{ 2 }, 0 , 2)));
            CheckThrowable(cause);
            Assert.False(h.writabilityQueue.TryTake(out _));
        }
        finally
        {
            if (s != null)
            {
                s.Close();
            }
            if (ch != null)
            {
                _ = ch.CloseAsync();
            }
            ss.Stop();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestShutdownOutputSoLingerNoAssertError()
    {
        await Run(TestContext.Current, TestShutdownOutputSoLingerNoAssertError0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownOutputSoLingerNoAssertError test name.
    private Task TestShutdownOutputSoLingerNoAssertError0(Bootstrap cb)
    {
        return TestShutdownSoLingerNoAssertError0(cb, true);
    }

    [Fact(Timeout = 30000)]
    public async Task TestShutdownSoLingerNoAssertError()
    {
        await Run(TestContext.Current, TestShutdownSoLingerNoAssertError0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownSoLingerNoAssertError test name, and the source's private
    // two-argument testShutdownSoLingerNoAssertError0 overload keeps its own name.
    private Task TestShutdownSoLingerNoAssertError0(Bootstrap cb)
    {
        return TestShutdownSoLingerNoAssertError0(cb, false);
    }

    private async Task TestShutdownSoLingerNoAssertError0(Bootstrap cb, bool output)
    {
        TcpListener ss = new TcpListener((IPEndPoint) NewSocketAddress());
        TcpClient s = null;

        Task<IChannel> cf = null;
        try
        {
            ss.Start();
            cf = cb.Option(ChannelOption.SO_LINGER, 1).Handler(new ChannelInboundHandlerAdapter())
                    .ConnectAsync(ss.LocalEndpoint);
            await cf;
            s = await ss.AcceptTcpClientAsync(TestContext.Current.CancellationToken);

            await cf;

            if (output)
            {
                await ((ISocketChannel) await cf).ShutdownOutputAsync();
            }
            else
            {
                await ((ISocketChannel) await cf).ShutdownAsync();
            }
        }
        finally
        {
            if (s != null)
            {
                s.Close();
            }
            // CLR: the source assigns cf only after connect(...).sync() succeeded; a failed connect Task has no
            // channel to close (Bootstrap.ConnectAsync already closed it).
            if (cf != null && cf.IsCompletedSuccessfully)
            {
                _ = (await cf).CloseAsync();
            }
            ss.Stop();
        }
    }

    private static void CheckThrowable(Exception cause)
    {
        // Depending on OIO / NIO both are ok
        // CLR: java.nio.channels.ClosedChannelException is this port's Channels.ClosedChannelException and
        // java.net.SocketException is System.Net.Sockets.SocketException ("The exception that is thrown when a socket
        // error occurs", https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception).
        if (!(cause is ClosedChannelException) && !(cause is SocketException))
        {
            ExceptionDispatchInfo.Throw(cause);
        }
    }

    // CLR: InputStream.read() returns the next byte or -1 at the end of the stream. Stream.ReadAsync returns 0 "if
    // the end of the stream has been reached" (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readasync),
    // so a one-byte read that returns 0 is -1 here. The awaited read observes the test cancellation, which xUnit
    // signals at the Timeout as JUnit's @Timeout interrupts the blocked read.
    private static async Task<int> ReadAsync(Stream @in, CancellationToken cancellationToken)
    {
        byte[] b = new byte[1];
        return await @in.ReadAsync(b.AsMemory(0, 1), cancellationToken) == 0 ? -1 : b[0];
    }

    private sealed class TestHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal volatile ISocketChannel ch;
        // CLR: the LinkedBlockingQueue<Byte> is an unbounded System.Threading.Channels channel; take() is
        // ChannelReader<T>.ReadAsync, which "Asynchronously reads an item from the channel"
        // (https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
        internal readonly System.Threading.Channels.Channel<byte> queue =
                System.Threading.Channels.Channel.CreateUnbounded<byte>();
        // CLR: the LinkedBlockingDeque<Boolean> is a BlockingCollection over a ConcurrentStack, "a ConcurrentStack<T>
        // for last in-first out (LIFO) behavior" (https://learn.microsoft.com/en-us/dotnet/standard/collections/
        // thread-safe/blockingcollection-overview): Take() blocks and removes the newest item as takeLast() does,
        // TryTake(out, 100) is poll(100, MILLISECONDS) and TryTake(out) is poll().
        internal readonly BlockingCollection<bool> writabilityQueue =
                new BlockingCollection<bool>(new ConcurrentStack<bool>());

        public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            writabilityQueue.Add(ctx.Channel.IsWritable);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ch = (ISocketChannel) ctx.Channel;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            queue.Writer.TryWrite(msg.ReadByte());
        }

        private void DrainWritabilityQueue()
        {
            while (writabilityQueue.TryTake(out _, 100))
            {
                // Just drain the queue.
            }
        }

        internal void AssertWritability(bool isWritable)
        {
            try
            {
                bool writability = writabilityQueue.Take();
                Assert.Equal(isWritable, writability);
                // TODO(scott): why do we get multiple writability changes here ... race condition?
                DrainWritabilityQueue();
            }
            catch (Exception c)
            {
                // CLR: Throwable.printStackTrace() prints to the standard error stream.
                Console.Error.WriteLine(c);
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069

// CLR: supplemental helpers that are not part of SocketShutdownOutputBySelfTest. ShutdownTestHandler and
// ShutdownFixture serve the CLR contract tests (SocketShutdownContractTest, SocketHalfClosedContractTest and others),
// which create their own event-loop group and allocator per test.
internal sealed class ShutdownTestHandler : SimpleChannelInboundHandler<ByteBuf>
{
    internal volatile IDuplexChannel Channel;
    internal readonly System.Threading.Channels.Channel<byte> Bytes = System.Threading.Channels.Channel.CreateUnbounded<byte>();
    internal readonly TaskCompletionSource HalfClosure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Closure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int HalfClosureCount;
    internal int OutputEvents;
    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        Channel = (IDuplexChannel)ctx.Channel;
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Closure.TrySetResult();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
    {
        Bytes.Writer.TryWrite((byte)msg.ReadByte());
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is ChannelInputShutdownEvent)
        {
            Interlocked.Increment(ref HalfClosureCount);
            HalfClosure.TrySetResult();
        }
        else if (evt is ChannelOutputShutdownEvent)
        {
            Interlocked.Increment(ref OutputEvents);
        }
    }
}

internal sealed class ShutdownFixture : IAsyncDisposable
{
    private readonly MultiThreadIoEventLoopGroup _group = new(2, NioIoHandler.NewFactory());
    internal Bootstrap Client { get; }
    internal ServerBootstrap Server { get; }
    internal ShutdownFixture(bool adaptive, bool domain = false)
    {
        IByteBufAllocator allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        Client = new Bootstrap().Group(_group).Option(ChannelOption.ALLOCATOR, allocator);
        Server = new ServerBootstrap().Group(_group)
            .Option(ChannelOption.ALLOCATOR, allocator).ChildOption(ChannelOption.ALLOCATOR, allocator);
        if (domain)
        {
            Client.Channel<NioDomainSocketChannel>();
            Server.Channel<NioServerDomainSocketChannel>();
        }
        else
        {
            Client.Channel<NioSocketChannel>();
            Server.Channel<NioServerSocketChannel>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
