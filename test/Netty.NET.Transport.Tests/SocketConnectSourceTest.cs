/*
 * Copyright 2016 The Netty Project
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source waits for and closes its channels independently of the test cancellation.
public class SocketConnectSourceTest : AbstractSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestCloseTwice()
    {
        await Run(TestContext.Current, TestCloseTwice0);
    }

    // CLR: xUnit1024 forbids overloading the TestCloseTwice test name.
    private async Task TestCloseTwice0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            serverChannel = await sb.ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync();
            // CLR: the LinkedBlockingQueue is an unbounded System.Threading.Channels channel, a "synchronization data
            // structure for passing data between producers and consumers asynchronously"
            // (https://learn.microsoft.com/en-us/dotnet/core/extensions/channels); ReadAsync is take().
            System.Threading.Channels.Channel<Task> futures = System.Threading.Channels.Channel.CreateUnbounded<Task>();
            clientChannel = await cb.Handler(new CloseOnUserEventHandler(futures.Writer))
                    .ConnectAsync(serverChannel.LocalAddress);
            clientChannel.Pipeline.FireUserEventTriggered("test");
            await clientChannel.CloseAsync();
            await await futures.Reader.ReadAsync();
            clientChannel = null;

            await serverChannel.CloseAsync();
            await serverChannel.CloseAsync();
            serverChannel = null;
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

    [Fact(Timeout = 30000)]
    public async Task TestLocalAddressAfterConnect()
    {
        await Run(TestContext.Current, TestLocalAddressAfterConnect0);
    }

    // CLR: xUnit1024 forbids overloading the TestLocalAddressAfterConnect test name.
    private async Task TestLocalAddressAfterConnect0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            // CLR: IPEndPoint is the java.net.InetSocketAddress counterpart.
            TaskCompletionSource<IPEndPoint> localAddressPromise =
                    new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            serverChannel = await sb.ChildHandler(new LocalAddressHandler(localAddressPromise)).BindAsync();

            clientChannel = await cb.Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();

            Assert.Null(clientChannel.LocalAddress);
            Assert.Null(clientChannel.RemoteAddress);

            await clientChannel.ConnectAsync(serverChannel.LocalAddress);
            AssertLocalAddress((IPEndPoint) clientChannel.LocalAddress);
            Assert.NotNull(clientChannel.RemoteAddress);

            AssertLocalAddress(await localAddressPromise.Task);
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

    [Fact(Timeout = 3000)]
    public async Task TestChannelEventsFiredWhenClosedDirectly()
    {
        await Run(TestContext.Current, TestChannelEventsFiredWhenClosedDirectly0);
    }

    // CLR: xUnit1024 forbids overloading the TestChannelEventsFiredWhenClosedDirectly test name.
    private async Task TestChannelEventsFiredWhenClosedDirectly0(ServerBootstrap sb, Bootstrap cb)
    {
        // CLR: the LinkedBlockingQueue<Integer> is an unbounded channel, as in testCloseTwice.
        System.Threading.Channels.Channel<int> events = System.Threading.Channels.Channel.CreateUnbounded<int>();

        IChannel sc = null;
        IChannel cc = null;

        try
        {
            sb.ChildHandler(new ChannelInboundHandlerAdapter());
            sc = await sb.BindAsync();

            cb.Handler(new ActiveInactiveEventsHandler(events.Writer));
            // Connect and directly close again.
            // CLR: ChannelFutureListener.CLOSE closes the future's channel once the connect completes; a synchronous
            // continuation of the connect Task does the same. A failed Bootstrap connect Task carries no channel, and
            // Bootstrap already closes the channel of a failed connect (Bootstrap.cs).
            Task<IChannel> connectFuture = cb.ConnectAsync(sc.LocalAddress);
            _ = connectFuture.ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    _ = future.Result.CloseAsync();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            cc = await connectFuture;
            Assert.Equal(0, await events.Reader.ReadAsync());
            Assert.Equal(1, await events.Reader.ReadAsync());
        }
        finally
        {
            if (cc != null)
            {
                _ = cc.CloseAsync();
            }

            if (sc != null)
            {
                _ = sc.CloseAsync();
            }
        }
    }

    [Fact(Timeout = 3000)]
    public async Task TestWriteWithFastOpenBeforeConnect()
    {
        await Run(TestContext.Current, TestWriteWithFastOpenBeforeConnect0);
    }

    [Fact(Timeout = 3000)]
    public async Task TestWriteWithFastOpenBeforeConnectDirectBufferReaderIndex()
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) =>
                TestWriteWithFastOpenBeforeConnect0(serverBootstrap, bootstrap,
                        channel => DirectBufferWithReaderIndex(
                                channel, "BAD-PREFIX-", "[fastopen-reader-index]"),
                        "[fastopen-reader-index]"));
    }

    [Fact(Timeout = 3000)]
    public async Task TestWriteWithFastOpenBeforeConnectCompositeBuffer()
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) =>
                TestWriteWithFastOpenBeforeConnect0(serverBootstrap, bootstrap,
                        channel => CompositeBuffer(channel, "[fastopen-", "composite-", "data]"),
                        "[fastopen-composite-data]"));
    }

    [Fact(Timeout = 3000)]
    public async Task TestWriteWithFastOpenBeforeConnectCompositeBufferReaderIndex()
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) =>
                TestWriteWithFastOpenBeforeConnect0(serverBootstrap, bootstrap,
                        channel => CompositeBufferWithReaderIndex(
                                channel, "BAD-PREFIX-", "[fastopen-", "composite-reader-", "index]"),
                        "[fastopen-composite-reader-index]"));
    }

    // CLR: xUnit1024 forbids overloading the TestWriteWithFastOpenBeforeConnect test name, so both the body and the
    // protected overload that takes the initial data factory are named TestWriteWithFastOpenBeforeConnect0.
    private async Task TestWriteWithFastOpenBeforeConnect0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestWriteWithFastOpenBeforeConnect0(sb, cb,
                channel => ByteBufUtil.WriteAscii(UnpooledByteBufAllocator.Default, "[fastopen]"), "[fastopen]");
    }

    // CLR: java.util.function.Function<Channel, ByteBuf> is Func<IChannel, ByteBuf>.
    protected virtual async Task TestWriteWithFastOpenBeforeConnect0(ServerBootstrap sb, Bootstrap cb,
                                                                    Func<IChannel, ByteBuf> initialDataFactory,
                                                                    string expectedInitialData)
    {
        EnableTcpFastOpen(sb, cb);
        sb.ChildOption(ChannelOption.AUTO_READ, true);
        cb.Option(ChannelOption.AUTO_READ, true);

        // CLR: ActionChannelInitializer carries the initChannel body of the source's anonymous
        // ChannelInitializer<SocketChannel>.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                ch.Pipeline.AddLast(new EchoServerHandler())));

        IChannel sc = await sb.BindAsync();
        await ConnectAndVerifyDataTransfer(cb, sc, initialDataFactory, expectedInitialData);
        await ConnectAndVerifyDataTransfer(cb, sc, initialDataFactory, expectedInitialData);
    }

    private static async Task ConnectAndVerifyDataTransfer(Bootstrap cb, IChannel sc,
                                                           Func<IChannel, ByteBuf> initialDataFactory,
                                                           string expectedInitialData)
    {
        BufferingClientHandler handler = new BufferingClientHandler();
        cb.Handler(handler);
        Task<IChannel> register = cb.RegisterAsync();
        IChannel channel = await register;
        Task write = channel.WriteAsync(initialDataFactory(channel));
        EndPoint remoteAddress = sc.LocalAddress;
        // CLR: Channel.connect(...) returns a Task without the channel, so connectFuture.sync().channel() is the
        // registered channel itself.
        Task connectFuture = channel.ConnectAsync(remoteAddress);
        await connectFuture;
        IChannel cc = channel;
        await cc.WriteAndFlushAsync(ByteBufUtil.WriteAscii(UnpooledByteBufAllocator.Default, "[normal data]"));
        await write;
        string expectedString = expectedInitialData + "[normal data]";
        string result = await handler.CollectBuffer(Encoding.ASCII.GetBytes(expectedString).Length);
        await cc.DisconnectAsync();
        Assert.Equal(expectedString, result);
    }

    private static ByteBuf DirectBufferWithReaderIndex(IChannel channel, string prefix, string data)
    {
        ByteBuf buffer = DirectBuffer(channel, prefix + data);
        buffer.ReaderIndex = Encoding.ASCII.GetBytes(prefix).Length;
        return buffer;
    }

    private static ByteBuf CompositeBufferWithReaderIndex(IChannel channel, string prefix, params string[] parts)
    {
        CompositeByteBuf buffer = channel.Alloc().CompositeDirectBuffer(parts.Length + 1);
        buffer.AddComponent(DirectBuffer(channel, prefix), true);

        foreach (string part in parts)
        {
            buffer.AddComponent(DirectBuffer(channel, part), true);
        }

        buffer.ReaderIndex = Encoding.ASCII.GetBytes(prefix).Length;
        return buffer;
    }

    private static ByteBuf CompositeBuffer(IChannel channel, params string[] parts)
    {
        CompositeByteBuf buffer = channel.Alloc().CompositeDirectBuffer(parts.Length);

        foreach (string part in parts)
        {
            buffer.AddComponent(DirectBuffer(channel, part), true);
        }

        return buffer;
    }

    private static ByteBuf DirectBuffer(IChannel channel, string data)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(data);
        ByteBuf buffer = channel.Alloc().DirectBuffer(bytes.Length);
        buffer.WriteBytes(bytes);
        return buffer;
    }

    protected virtual void EnableTcpFastOpen(ServerBootstrap sb, Bootstrap cb)
    {
        // TFO is an almost-pure optimisation and should not change any observable behaviour in our tests.
        sb.Option(ChannelOption.TCP_FASTOPEN, 5);
        cb.Option(ChannelOption.TCP_FASTOPEN_CONNECT, true);
    }

    // CLR: InetAddress.isAnyLocalAddress() is a comparison with the IPv4 and IPv6 wildcard addresses.
    private static void AssertLocalAddress(IPEndPoint address)
    {
        Assert.True(address.Port > 0);
        Assert.False(address.Address.Equals(IPAddress.Any) || address.Address.Equals(IPAddress.IPv6Any));
    }

    private sealed class CloseOnUserEventHandler(System.Threading.Channels.ChannelWriter<Task> futures)
            : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            futures.TryWrite(ctx.CloseAsync());
        }
    }

    private sealed class LocalAddressHandler(TaskCompletionSource<IPEndPoint> localAddressPromise)
            : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            localAddressPromise.SetResult((IPEndPoint) ctx.Channel.LocalAddress);
        }
    }

    private sealed class ActiveInactiveEventsHandler(System.Threading.Channels.ChannelWriter<int> events)
            : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            events.TryWrite(0);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            events.TryWrite(1);
        }
    }

    private class BufferingClientHandler : ChannelInboundHandlerAdapter
    {
        // CLR: SemaphoreSlim "limits the number of threads that can access a resource or pool of resources
        // concurrently" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim) and replaces
        // java.util.concurrent.Semaphore; Release(int) is release(int).
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
        // CLR: ByteArrayOutputStream's write/reset/toByteArray are synchronized methods (OpenJDK
        // https://github.com/openjdk/jdk21u/blob/master/src/java.base/share/classes/java/io/ByteArrayOutputStream.java);
        // MemoryStream is not, so a lock on it takes their place.
        private readonly MemoryStream streamBuffer = new MemoryStream();

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                ByteBuf buf = (ByteBuf) msg;
                int readableBytes = buf.ReadableBytes;

                lock (streamBuffer)
                {
                    buf.ReadBytes(streamBuffer, readableBytes);
                }

                semaphore.Release(readableBytes);
                buf.Release();
            }
            else
            {
                // CLR: ArgumentException is the IllegalArgumentException counterpart.
                throw new ArgumentException("Unexpected message type: " + msg);
            }
        }

        // CLR: SemaphoreSlim has no multi-count acquire, so acquire(expectedBytes) waits expectedBytes times; with this
        // single consumer it takes the same permits. The method is asynchronous so the wait does not block a thread.
        internal async Task<string> CollectBuffer(int expectedBytes)
        {
            for (int i = 0; i < expectedBytes; i++)
            {
                await semaphore.WaitAsync();
            }

            byte[] bytes;

            lock (streamBuffer)
            {
                bytes = streamBuffer.ToArray();
                streamBuffer.SetLength(0);
            }

            return Encoding.ASCII.GetString(bytes);
        }
    }

    private sealed class EchoServerHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                ByteBuf buffer = ctx.Alloc().Buffer();
                ByteBuf buf = (ByteBuf) msg;
                buffer.WriteBytes(buf);
                buf.Release();
                _ = ctx.Channel.WriteAndFlushAsync(buffer);
            }
            else
            {
                throw new ArgumentException("Unexpected message type: " + msg);
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
