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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The Runner bodies observe TestContext.Current.CancellationToken in their latch waits.
#pragma warning disable xUnit1051 // Channel and group closes run independently of the test cancellation, as in the source.
public class SocketChannelNotYetConnectedSourceTest : AbstractClientSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestShutdownNotYetConnected()
    {
        await Run(TestContext.Current, TestShutdownNotYetConnected0);
    }

    // CLR: xUnit1024 forbids overloading the TestShutdownNotYetConnected test name.
    private async Task TestShutdownNotYetConnected0(Bootstrap cb)
    {
        ISocketChannel ch = (ISocketChannel) await cb.Handler(new ChannelInboundHandlerAdapter())
                .BindAsync(NewSocketAddress());
        try
        {
            Exception cause = await Assert.ThrowsAnyAsync<Exception>(() => ch.ShutdownInputAsync());
            CheckThrowable(cause);

            cause = await Assert.ThrowsAnyAsync<Exception>(() => ch.ShutdownOutputAsync());
            CheckThrowable(cause);
        }
        finally
        {
            await ch.CloseAsync();
        }
    }

    private static void CheckThrowable(Exception cause)
    {
        // Depending on OIO / NIO both are ok
        // CLR: java.nio.channels.NotYetConnectedException has no BCL type; the CLR socket reports the condition as
        // SocketException with SocketError.NotConnected, "The application tried to send or receive data, and the
        // Socket is not connected" (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror), so
        // the source's SocketException branch accepts it.
        if (!(cause is SocketException))
        {
            ExceptionDispatchInfo.Throw(cause);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task ReadMustBePendingUntilChannelIsActive()
    {
        await Run(TestContext.Current, async bootstrap =>
        {
            IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
            ServerBootstrap sb = new ServerBootstrap().Group(group);
            // CLR: bind(0) binds new InetSocketAddress(0), the wildcard address, and the connect below reaches it
            // because OpenJDK's SocketChannelImpl.checkRemote replaces a wildcard remote address with the loopback
            // address ("if (address.isAnyLocalAddress()) ... Net.inet4LoopbackAddress()",
            // https://github.com/openjdk/jdk21u/blob/master/src/java.base/share/classes/sun/nio/ch/SocketChannelImpl.java).
            // A CLR socket does not make that replacement, so the server binds the loopback address
            // (NetUtil.LOCALHOST) that the source's connect ends up using, port 0 as in bind(0).
            IChannel serverChannel = await sb.ChildHandler(new SendFortyTwo())
                    .Channel<NioServerSocketChannel>().BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));

            TaskCompletionSource readLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bootstrap.Handler(new ReadFortyTwo(readLatch));
            await bootstrap.ConnectAsync(serverChannel.LocalAddress);

            await readLatch.Task.WaitAsync(TestContext.Current.CancellationToken);
            await group.ShutdownGracefullyAsync();
        });
    }

    // CLR: supplemental helper, not part of the source: ChannelOutputShutdownContractTest runs the same read-before-
    // connect scenario with AUTO_READ disabled on its own event-loop group and allocator.
    internal static async Task ReadBeforeConnect(bool adaptive, bool autoRead)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        IChannel server = await fixture.Server.ChildHandler(new SendFortyTwo()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, autoRead).Handler(new ReadFortyTwo(read)).ConnectAsync(server.LocalAddress);
            await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (client != null)
            {
                await client.CloseAsync();
            }
            await server.CloseAsync();
        }
    }

    // CLR: the source's anonymous server handler as a named class; C# anonymous types "can't add methods, events, or
    // custom operators" (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/
    // anonymous-types).
    private sealed class SendFortyTwo : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(Unpooled.CopyInt(42));
        }
    }

    // CLR: the source's anonymous ByteToMessageDecoder as a named class (codec-base is ported since 2026-11-18): handlerAdded
    // asserts the inactive channel and requests a read, and decode asserts at most Integer.BYTES (sizeof(int)) readable
    // bytes and the integer 42. A failed assertion reaches ExceptionCaught (wrapped in a DecoderException when it comes
    // from decode), which faults the latch so the test fails without waiting for its Timeout; the source has no
    // exceptionCaught override and waits for its @Timeout instead.
    private sealed class ReadFortyTwo(TaskCompletionSource readLatch) : ByteToMessageDecoder
    {
        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            Assert.False(ctx.Channel.IsActive);
            ctx.Read();
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            Assert.True(@in.ReadableBytes <= sizeof(int));
            if (@in.ReadableBytes == sizeof(int))
            {
                Assert.Equal(42, @in.ReadInt());
                readLatch.TrySetResult();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            readLatch.TrySetException(cause);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
