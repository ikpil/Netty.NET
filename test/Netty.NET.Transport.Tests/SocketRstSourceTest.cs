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
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class SocketRstSourceTest
{
    // CLR: SocketException is the native socket I/O exception. Retain the reset requirement using its error code.
    protected void AssertRstOnCloseException(SocketException cause, IChannel clientChannel)
    {
        Assert.Equal(SocketError.ConnectionReset, cause.SocketErrorCode);
        if (CultureInfo.CurrentCulture.Name is "en-US" or "en-GB")
            Assert.True(cause.Message.Contains("reset") || cause.Message.Contains("closed"), "actual message: " + cause.Message);
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestSoLingerZeroCausesOnlyRstOnClose(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        ServerBootstrap sb = fixture.Server;
        Bootstrap cb = fixture.Client;
        var serverChannelRef = new StrongBox<IChannel>();
        var throwableRef = new StrongBox<Exception>();
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // SO_LINGER=0 means that we must send ONLY a RST when closing (not a FIN + RST).
        sb.ChildOption(ChannelOption.SO_LINGER, 0);
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            Interlocked.CompareExchange(ref serverChannelRef.Value, ch, null);
            latch.SetResult();
        }));
        cb.Handler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new ClientHandler(throwableRef, latch2))));
        IChannel sc = await sb.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the server to get setup.
        await latch.Task.WaitAsync(TestContext.Current.CancellationToken);

        // The server has SO_LINGER=0 and so it must send a RST when close is called.
        _ = serverChannelRef.Value.CloseAsync(TestContext.Current.CancellationToken);

        // Wait for the client to get channelInactive.
        await latch2.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Verify the client received a RST.
        Exception cause = Volatile.Read(ref throwableRef.Value);
        AssertRstOnCloseException(Assert.IsType<SocketException>(cause), cc);
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestNoRstIfSoLingerOnClose(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        ServerBootstrap sb = fixture.Server;
        Bootstrap cb = fixture.Client;
        var serverChannelRef = new StrongBox<IChannel>();
        var throwableRef = new StrongBox<Exception>();
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            Interlocked.CompareExchange(ref serverChannelRef.Value, ch, null);
            latch.SetResult();
        }));
        cb.Handler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(new ClientHandler(throwableRef, latch2))));
        IChannel sc = await sb.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        await cb.ConnectAsync(sc.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the server to get setup.
        await latch.Task.WaitAsync(TestContext.Current.CancellationToken);

        // The server has SO_LINGER=0 and so it must send a RST when close is called.
        // CLR: preserve the original default-linger setup despite this repeated source comment; do not add an option.
        _ = serverChannelRef.Value.CloseAsync(TestContext.Current.CancellationToken);

        // Wait for the client to get channelInactive.
        await latch2.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Verify the client did not received a RST.
        Assert.Null(Volatile.Read(ref throwableRef.Value));
    }

    // CLR named class replaces the two identical anonymous original handlers.
    private sealed class ClientHandler(StrongBox<Exception> throwableRef, TaskCompletionSource latch2) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) =>
            Interlocked.CompareExchange(ref throwableRef.Value, cause, null);
        public override void ChannelInactive(ChannelHandlerContext ctx) => latch2.SetResult();
    }
}
