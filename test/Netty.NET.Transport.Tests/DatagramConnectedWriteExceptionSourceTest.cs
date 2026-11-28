/*
 * Copyright 2026 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using CountDownLatch = Netty.NET.Transport.Tests.DatagramUnicastSourceTest.CountDownLatch;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source's sync() calls and its close take no cancellation.
public class DatagramConnectedWriteExceptionSourceTest : AbstractClientSocketTest
{
    protected override List<TestsuitePermutation.IBootstrapFactory<Bootstrap>> NewFactories()
    {
        return SocketTestPermutation.INSTANCE.DatagramSocket();
    }

    // CLR: @DisabledOnOs(OS.WINDOWS) disables the test on Windows; xUnit v3's Assert.SkipWhen "will skip the test if
    // condition is true" (https://xunit.net/docs/getting-started/v3/whats-new).
    [Fact(Timeout = 10000)]
    public async Task TestWriteThrowsPortUnreachableException()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "@DisabledOnOs(OS.WINDOWS)");
        await Run(TestContext.Current, TestWriteExceptionAfterServerStop);
    }

    // CLR: the CountDownLatch is DatagramUnicastSourceTest.CountDownLatch and the AtomicReference<Throwable> a
    // StrongBox<Exception> (see DatagramUnicastSourceTest). ChannelFuture.addListener is Task.ContinueWith, which
    // "Creates a continuation that executes asynchronously when the target Task completes"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.continuewith); isSuccess() is
    // IsCompletedSuccessfully and cause() the Task's exception. CharsetUtil.UTF_8 is Encoding.UTF8, and
    // Thread.sleep is Task.Delay (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay).
    protected virtual async Task TestWriteExceptionAfterServerStop(Bootstrap clientBootstrap)
    {
        CountDownLatch serverReceivedLatch = new CountDownLatch(1);
        Bootstrap serverBootstrap = clientBootstrap.Clone()
                .Option(ChannelOption.SO_BROADCAST, false)
                .Handler(new ServerReceivedHandler(serverReceivedLatch));

        IChannel serverChannel = await serverBootstrap.BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));
        IPEndPoint serverAddress = (IPEndPoint) serverChannel.LocalAddress;

        clientBootstrap.Option(ChannelOption.AUTO_READ, false)
                .Handler(new NoOpHandler());

        IChannel clientChannel = await clientBootstrap.ConnectAsync(serverAddress);

        CountDownLatch clientFirstSendLatch = new CountDownLatch(1);

        try
        {
            ByteBuf firstMessage = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("First message"));
            _ = clientChannel.WriteAndFlushAsync(firstMessage)
                    .ContinueWith(future =>
                    {
                        if (future.IsCompletedSuccessfully)
                        {
                            clientFirstSendLatch.CountDown();
                        }
                    });

            Assert.True(await serverReceivedLatch.Await(TimeSpan.FromSeconds(5)), "Server should receive first message");
            Assert.True(await clientFirstSendLatch.Await(TimeSpan.FromSeconds(5)), "Client should send first message");

            await serverChannel.CloseAsync();

            StrongBox<Exception> writeException = new StrongBox<Exception>();
            CountDownLatch writesCompleteLatch = new CountDownLatch(10);

            for (int i = 0; i < 10; i++)
            {
                ByteBuf message = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("Message " + i));
                _ = clientChannel.WriteAndFlushAsync(message)
                        .ContinueWith(future =>
                        {
                            if (!future.IsCompletedSuccessfully)
                            {
                                Interlocked.CompareExchange(ref writeException.Value, future.Exception?.InnerException,
                                        null);
                            }

                            writesCompleteLatch.CountDown();
                        });
                await Task.Delay(50);
            }

            Assert.True(await writesCompleteLatch.Await(TimeSpan.FromSeconds(5)), "All writes should complete");

            // CLR: xUnit's Assert.NotNull and Assert.IsType take no message, so the source's messages are kept on
            // Assert.True. PortUnreachableException is SocketError.ConnectionRefused, as in
            // DatagramConnectNotExistsSourceTest.
            Exception cause = Volatile.Read(ref writeException.Value);
            Assert.True(cause != null, "Should have captured a write exception");

            Assert.True(cause is SocketException { SocketErrorCode: SocketError.ConnectionRefused }, "Expected " +
                    "PortUnreachableException but got: " + cause.GetType().FullName);
        }
        finally
        {
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
        }
    }

    // CLR: C# has no anonymous classes; the source's anonymous handlers are the private classes below.
    private sealed class ServerReceivedHandler(CountDownLatch serverReceivedLatch)
            : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            serverReceivedLatch.CountDown();
        }
    }

    private sealed class NoOpHandler : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            // no-op
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
