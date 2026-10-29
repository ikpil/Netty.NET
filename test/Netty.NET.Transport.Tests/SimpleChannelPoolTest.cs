/*
 * Copyright 2015 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Pool;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve original per-case cancellation and complete cleanup independently.
public class SimpleChannelPoolTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    [Fact]
    public async Task TestAcquire()
    {
        await using var setup = await Setup.StartAsync();
        var handler = new CountingChannelPoolHandler();
        await using IChannelPool pool = new SimpleChannelPool(setup.Client, handler);
        var channel = await pool.AcquireAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await pool.ReleaseAsync(channel);
        var channel2 = await pool.AcquireAsync();
        Assert.Same(channel, channel2);
        Assert.Equal(1, handler.ChannelCount);
        await pool.ReleaseAsync(channel2);
        // Should fail on multiple release calls.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => pool.ReleaseAsync(channel2));
        Assert.False(channel.IsActive);
        Assert.Equal(2, handler.AcquiredCount);
        Assert.Equal(2, handler.ReleasedCount);
    }

    [Fact]
    public async Task TestBoundedChannelPoolSegment()
    {
        await using var setup = await Setup.StartAsync();
        var handler = new CountingChannelPoolHandler();
        await using var pool = new BoundedPool(setup.Client, handler);
        var channel = await pool.AcquireAsync();
        var channel2 = await pool.AcquireAsync();
        await pool.ReleaseAsync(channel);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => pool.ReleaseAsync(channel2));
        await channel2.CloseAsync();
        Assert.Equal(2, handler.ChannelCount);
        Assert.Equal(2, handler.AcquiredCount);
        Assert.Equal(1, handler.ReleasedCount);
        await channel.CloseAsync();
        await channel2.CloseAsync();
    }

    [Fact]
    /**
     * Tests that if channel was unhealthy it is not offered back to the pool.
     *
     * @throws Exception
     */
    public async Task TestUnhealthyChannelIsNotOffered()
    {
        await using var setup = await Setup.StartAsync();
        await using var pool = new SimpleChannelPool(setup.Client, new CountingChannelPoolHandler());
        var channel1 = await pool.AcquireAsync();
        await pool.ReleaseAsync(channel1);
        var channel2 = await pool.AcquireAsync();
        //first check that when returned healthy then it actually offered back to the pool.
        Assert.Same(channel1, channel2);
        await channel1.CloseAsync();
        await pool.ReleaseAsync(channel1);
        var channel3 = await pool.AcquireAsync();
        //channel1 was not healthy anymore so it should not get acquired anymore.
        Assert.NotSame(channel1, channel3);
        await channel3.CloseAsync();
    }

    [Fact]
    public async Task TestActiveUnhealthyChannelIsClosedOnRelease()
    {
        await using var setup = await Setup.StartAsync();
        bool released = false;
        var handler = new CallbackHandler(released: channel => { Assert.True(channel.IsActive); released = true; });
        Func<IChannel, Task<bool>> checker = _ => Task.FromResult(false);
        await using var pool = new SimpleChannelPool(setup.Client, handler, checker);
        IChannel channel = null;
        try
        {
            channel = await pool.AcquireAsync();
            Assert.True(channel.IsActive);
            Task release = pool.ReleaseAsync(channel);
            await release;
            Assert.True(release.IsCompletedSuccessfully);
            Assert.True(released);
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.False(channel.IsActive);
        }
        finally { if (channel != null) await channel.CloseAsync(); }
    }

    [Fact]
    /**
     * Tests that if channel was unhealthy it is was offered back to the pool because
     * it was requested not to validate channel health on release.
     *
     * @throws Exception
     */
    public async Task TestUnhealthyChannelIsOfferedWhenNoHealthCheckRequested()
    {
        await using var setup = await Setup.StartAsync();
        await using var pool = new SimpleChannelPool(setup.Client, new CountingChannelPoolHandler(), ChannelHealthChecker.Active, false);
        var channel1 = await pool.AcquireAsync();
        await channel1.CloseAsync();
        Task release = pool.ReleaseAsync(channel1);
        await release;
        Assert.True(release.IsCompletedSuccessfully);
        var channel2 = await pool.AcquireAsync();
        //verifying that in fact the channel2 is different that means is not pulled from the pool
        Assert.NotSame(channel1, channel2);
        await channel2.CloseAsync();
    }

    [Fact]
    public void TestBootstrap()
    {
        using var pool = new SimpleChannelPool(new Bootstrap(), new CountingChannelPoolHandler());
        // Checking for the actual bootstrap object doesn't make sense here, since the pool uses a copy with a
        // modified channel handler.
        Assert.NotNull(pool.Bootstrap);
    }

    [Fact]
    public void TestHandler()
    {
        var handler = new CountingChannelPoolHandler();
        using var pool = new SimpleChannelPool(new Bootstrap(), handler);
        Assert.Same(handler, pool.Handler);
    }

    [Fact]
    public void TestHealthChecker()
    {
        var checker = ChannelHealthChecker.Active;
        using var pool = new SimpleChannelPool(new Bootstrap(), new CountingChannelPoolHandler(), checker);
        Assert.Same(checker, pool.HealthChecker);
    }

    [Fact]
    public void TestReleaseHealthCheck()
    {
        using var healthCheckOnRelease = new SimpleChannelPool(new Bootstrap(), new CountingChannelPoolHandler(), ChannelHealthChecker.Active, true);
        Assert.True(healthCheckOnRelease.ReleaseHealthCheck);
        using var noHealthCheckOnRelease = new SimpleChannelPool(new Bootstrap(), new CountingChannelPoolHandler(), ChannelHealthChecker.Active, false);
        Assert.False(noHealthCheckOnRelease.ReleaseHealthCheck);
    }

    [Fact]
    public async Task TestCloseAsync()
    {
        await using var setup = await Setup.StartAsync();
        // Create pool, acquire and return channels
        await using var pool = new SimpleChannelPool(setup.Client, new CountingChannelPoolHandler());
        var first = await pool.AcquireAsync();
        var second = await pool.AcquireAsync();
        await pool.ReleaseAsync(first).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await pool.ReleaseAsync(second).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        // Assert that returned channels are open before close
        Assert.True(first.IsOpen);
        Assert.True(second.IsOpen);
        // Close asynchronously with timeout
        await pool.CloseAsync().WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        // Assert channels were indeed closed
        Assert.False(first.IsOpen);
        Assert.False(second.IsOpen);
    }

    [Fact]
    public async Task TestChannelAcquiredException()
    {
        await using var setup = await Setup.StartAsync();
        var error = new NullReferenceException();
        // Create pool, acquire and return channels
        await using var pool = new SimpleChannelPool(setup.Client, new CallbackHandler(acquired: _ => throw error));
        var observed = await Assert.ThrowsAsync<NullReferenceException>(() => pool.AcquireAsync());
        Assert.Same(error, observed);
    }

    [Fact]
    public async Task TestAcquireCancelledDuringHealthCheckOffersChannelBack()
    {
        await using var setup = await Setup.StartAsync();
        var handler = new CountingChannelPoolHandler();
        // Hand out health checks that we complete ourselves, so the acquire Promise can be cancelled while the
        // health check of an already pooled Channel is still pending.
        using var checks = new BlockingCollection<TaskCompletionSource<bool>>();
        Func<IChannel, Task<bool>> checker = channel =>
        {
            Assert.True(channel.EventLoop.InEventLoop());
            var check = new TaskCompletionSource<bool>();
            checks.Add(check);
            return check.Task;
        };
        // Health checks on release are turned off, so releasing does not need a hand-completed health check as well.
        await using var pool = new SimpleChannelPool(setup.Client, handler, checker, false);
        IChannel channel = null;
        try
        {
            // Nothing is pooled yet, so this connects a new Channel and does not run a health check at all.
            channel = await pool.AcquireAsync();
            await pool.ReleaseAsync(channel);
            Assert.Empty(checks);
            Assert.Equal(1, handler.AcquiredCount);
            Assert.Equal(1, handler.ReleasedCount);
            // This acquire reuses the pooled Channel, so it has to pass the health check first.
            using var cancellation = new CancellationTokenSource();
            Task<IChannel> acquire = pool.AcquireAsync(cancellation.Token);
            Assert.True(checks.TryTake(out var health, Timeout));
            Assert.NotNull(health);
            cancellation.Cancel();
            Assert.True(acquire.IsCanceled);
            health.SetResult(true);
            // Let the health check listener run on the EventLoop of the Channel.
            // CLR: an executor barrier alone does not await native asynchronous continuation dispatch.
            await handler.SecondRelease.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            await channel.EventLoop.SubmitAsync(() => { }, TestContext.Current.CancellationToken);
            Assert.True(acquire.IsCanceled);
            Assert.Equal(2, handler.AcquiredCount);
            Assert.Equal(2, handler.ReleasedCount);
            Assert.True(channel.IsActive);
            // The Channel went back into the pool, so the next acquire hands out the very same Channel.
            Task<IChannel> secondAcquire = pool.AcquireAsync();
            Assert.True(checks.TryTake(out var secondHealth, Timeout));
            Assert.NotNull(secondHealth);
            secondHealth.SetResult(true);
            Assert.Same(channel, await secondAcquire.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        }
        finally { if (channel != null) await channel.CloseAsync(); }
    }

    private sealed class BoundedPool(Bootstrap bootstrap, IChannelPoolHandler handler)
        : SimpleChannelPool(bootstrap, handler, ChannelHealthChecker.Active)
    {
        private readonly BlockingCollection<IChannel> _queue = new(1);
        protected override IChannel PollChannel() => _queue.TryTake(out var channel) ? channel : null;
        protected override bool OfferChannel(IChannel channel) => _queue.TryAdd(channel);
    }

    private sealed class CallbackHandler(Action<IChannel> created = null, Action<IChannel> acquired = null, Action<IChannel> released = null)
        : IChannelPoolHandler
    {
        public void ChannelCreated(IChannel channel) { /* NOOP */ created?.Invoke(channel); }
        public void ChannelAcquired(IChannel channel) { /* NOOP */ acquired?.Invoke(channel); }
        public void ChannelReleased(IChannel channel) => released?.Invoke(channel);
    }

    // CLR: shared setup only factors identical original bootstrap/server setup. Every test owns its
    // event-loop group, server and cleanup; all original per-case operations/assertions remain above.
    internal sealed class Setup : IAsyncDisposable
    {
        internal readonly DefaultEventLoopGroup Group = new(2);
        internal readonly Bootstrap Client;
        private IChannel _server;
        private Setup(LocalAddress address) => Client = new Bootstrap().RemoteAddress(address).Group(Group).Channel<LocalChannel>();
        internal static async Task<Setup> StartAsync()
        {
            var address = new LocalAddress(ChannelPoolTestUtils.GetLocalAddrId());
            var setup = new Setup(address);
            // Start server
            setup._server = await new ServerBootstrap().Group(setup.Group).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new ChannelInboundHandlerAdapter())))
                .BindAsync(address).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            return setup;
        }
        public async ValueTask DisposeAsync()
        {
            if (_server != null)
                await _server.CloseAsync();
            await Group.StopAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
    }
}
