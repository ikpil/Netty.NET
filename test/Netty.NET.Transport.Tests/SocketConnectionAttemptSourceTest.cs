/*
 * Copyright 2013 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.SocketMultipleConnectSourceTest;

namespace Netty.NET.Transport.Tests;

public class SocketConnectionAttemptSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectTimeout(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var address = Loopback();
        var group = new DefaultEventLoopGroup(1);
        var channel = new PendingSocketConnect(address.AddressFamily);
        try
        {
            var bootstrap = PendingBootstrap(group, channel, adaptive, 2000);
            await Assert.ThrowsAsync<ConnectTimeoutException>(() =>
                bootstrap.ConnectAsync(new IPEndPoint(address, 65535), cancellationToken: cancellation)
                    .WaitAsync(TimeSpan.FromMilliseconds(3000), cancellation));
            Assert.False(channel.IsOpen);
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectCancellation(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var address = Loopback();
        var group = new DefaultEventLoopGroup(1);
        var channel = new PendingSocketConnect(address.AddressFamily);
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            var connect = PendingBootstrap(group, channel, adaptive, 4000)
                .ConnectAsync(new IPEndPoint(address, 65535), cancellationToken: connectCancellation.Token);
            await channel.Started.Task.WaitAsync(cancellation);
            await Assert.ThrowsAsync<TimeoutException>(() => connect.WaitAsync(TimeSpan.FromMilliseconds(1000), cancellation));
            connectCancellation.Cancel();
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromMilliseconds(500), cancellation);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
            Assert.True(connect.IsCanceled);
        }
        finally
        {
            connectCancellation.Cancel();
            await group.StopAsync();
        }
    }

    private static Bootstrap PendingBootstrap(DefaultEventLoopGroup group, PendingSocketConnect channel,
        bool adaptive, int timeout) => new Bootstrap().Group(group).ChannelFactory(() => channel)
        .Handler(new ChannelInboundHandlerAdapter()).Option(ChannelOption.ALLOCATOR, SelectAllocator(adaptive))
        .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, timeout);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestConnectRefused(bool adaptive) =>
        ConnectRefused(adaptive, false, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestConnectRefusedHalfClosure(bool adaptive) =>
        ConnectRefused(adaptive, true, TestContext.Current.CancellationToken);

    private static async Task ConnectRefused(bool adaptive, bool halfClosure, CancellationToken cancellation)
    {
        var address = Loopback();
        using var reserved = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(address, 0));
        var group = new DefaultEventLoopGroup(1);
        var channel = new TcpSocketChannel(address.AddressFamily);
        var handler = new ActiveProbe();
        try
        {
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(() => channel).Handler(handler)
                .Option(ChannelOption.ALLOCATOR, SelectAllocator(adaptive))
                .Option(ChannelOption.ALLOW_HALF_CLOSURE, halfClosure);
            var error = await Assert.ThrowsAsync<SocketException>(() =>
                bootstrap.ConnectAsync(reserved.LocalEndPoint, cancellationToken: cancellation));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            Assert.False(handler.WasActive, "should have never been called");
            Assert.False(channel.IsOpen);
        }
        finally { await group.StopAsync(); }
    }

    private sealed class ActiveProbe : ChannelInboundHandlerAdapter
    {
        internal bool WasActive;
        public override void ChannelActive(ChannelHandlerContext context) => WasActive = true;
    }

    internal sealed class PendingSocketConnect(AddressFamily family) : TcpSocketChannel(family)
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async ValueTask ConnectSocketAsync(EndPoint remote, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await base.ConnectSocketAsync(remote, cancellationToken).ConfigureAwait(false);
        }
    }
}
