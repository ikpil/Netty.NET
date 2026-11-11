using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.SocketConnectionAttemptSourceTest;
using static Netty.NET.Transport.Tests.SocketMultipleConnectSourceTest;

namespace Netty.NET.Transport.Tests;

// Deterministic CLR timeout/cancellation coverage. These tests hold a native connect boundary;
// they do not count as executions of the original real-network connection-attempt fixture.
public class SocketConnectTimingContractTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredTimeoutClosesAnUnfinishedNativeConnect(bool adaptive)
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
    public async Task CancellationClosesAnUnfinishedNativeConnect(bool adaptive)
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

}
