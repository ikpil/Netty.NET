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
    private static readonly string BadHost = Netty.NET.Common.Internal.SystemPropertyUtil.Get("io.netty.testsuite.badHost", "198.51.100.254");
    private static readonly int BadPort = Netty.NET.Common.Internal.SystemPropertyUtil.GetInt("io.netty.testsuite.badPort", 65535);
    private static readonly EndPoint BadAddress = IPAddress.TryParse(BadHost, out var address)
        ? new IPEndPoint(address, BadPort) : new DnsEndPoint(BadHost, BadPort);
    private static AddressFamily BadAddressFamily => BadAddress.AddressFamily == AddressFamily.InterNetworkV6
        ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectTimeout(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        var channel = new TcpSocketChannel(BadAddressFamily);
        try
        {
            Task<IChannel> future = NetworkBootstrap(group, channel, adaptive, 2000).ConnectAsync(BadAddress, cancellationToken: cancellation);
            try
            {
                // The source asserts completion within 3000 ms, irrespective of timeout versus immediate refusal.
                await Observe(future).WaitAsync(TimeSpan.FromMilliseconds(3000), cancellation);
            }
            finally { await Bootstrap.CleanupAsync(channel); }
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectCancellation(bool adaptive)
    {
        var cancellation = TestContext.Current.CancellationToken;
        // Check if the test can be executed or should be skipped because of no network/internet connection
        // See https://github.com/netty/netty/issues/1474
        bool badHostTimedOut = true;
        using (var socket = new Socket(BadAddressFamily, SocketType.Stream, ProtocolType.Tcp))
        using (var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            probeTimeout.CancelAfter(10);
            try { await socket.ConnectAsync(BadAddress, probeTimeout.Token); }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.ConnectionRefused or SocketError.NetworkUnreachable or SocketError.HostUnreachable)
            {
                badHostTimedOut = false;
                // is thrown for no route to host when using Socket connect
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // ignore
            }
        }
        if (!badHostTimedOut) Assert.Skip("The connection attempt to " + BadHost + " does not time out.");

        var group = new DefaultEventLoopGroup(1);
        var channel = new TcpSocketChannel(BadAddressFamily);
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task<IChannel> future = null;
        try
        {
            future = NetworkBootstrap(group, channel, adaptive, 4000).ConnectAsync(BadAddress, cancellationToken: connectCancellation.Token);
            if (await Task.WhenAny(future, Task.Delay(1000, cancellation)) == future)
            {
                if (future.IsCompletedSuccessfully) Assert.Fail("A connection attempt to " + BadHost + " must not succeed.");
                await future;
            }
            // Check if cancellation is supported or not.
            // CLR asynchronous Socket connects support native token cancellation; there is no OIO permutation.
            connectCancellation.Cancel();
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromMilliseconds(500), cancellation);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => future);
            Assert.True(future.IsCanceled);
        }
        finally
        {
            connectCancellation.Cancel();
            await Bootstrap.CleanupAsync(channel);
            if (future != null) await Observe(future);
            await group.StopAsync();
        }
    }

    private static Bootstrap NetworkBootstrap(DefaultEventLoopGroup group, TcpSocketChannel channel, bool adaptive, int timeout) =>
        new Bootstrap().Group(group).ChannelFactory(() => channel).Handler(new TestHandler())
            .Option(ChannelOption.ALLOCATOR, SelectAllocator(adaptive)).Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, timeout);
    private static async Task Observe(Task operation) { try { await operation; } catch { } }
    private sealed class TestHandler : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) =>
            Netty.NET.Common.Internal.Logging.InternalLoggerFactory.GetInstance(typeof(TestHandler)).Warn("Unexpected exception:", error);
    }

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
