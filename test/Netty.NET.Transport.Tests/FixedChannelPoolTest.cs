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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Pool;
using AcquireTimeoutAction = Netty.NET.Transport.Channels.Pool.FixedChannelPool.AcquireTimeoutAction;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve the original explicit cancellation scenarios and finish cleanup independently.
public class FixedChannelPoolTest
{
    [Fact]
    public async Task TestAcquire()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        var handler = new CountingChannelPoolHandler();
        await using var pool = new FixedChannelPool(t.Client, handler, 1, int.MaxValue);
        var channel = await pool.AcquireAsync();
        Task<IChannel> future = pool.AcquireAsync();
        Assert.False(future.IsCompleted);
        await pool.ReleaseAsync(channel);
        var channel2 = await future.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(channel, channel2);
        Assert.Equal(1, handler.ChannelCount);
        Assert.Equal(2, handler.AcquiredCount);
        Assert.Equal(1, handler.ReleasedCount);
        await t.Server.CloseAsync();
        await channel2.CloseAsync();
    }

    [Fact]
    public async Task TestCancelledAcquireReleasesChannel()
    {
        await using var t = new BootstrapTuple(1);
        using var handler = new BlockingChannelPoolHandler();
        FixedChannelPool pool = null;
        try
        {
            await t.StartServerAsync();
            pool = new FixedChannelPool(t.Client, handler, 1);
            using var cancellation = new CancellationTokenSource();
            var cancelledAcquire = pool.AcquireAsync(cancellation.Token);
            await handler.AwaitChannelAcquiredAsync();
            Assert.Equal(1, pool.AcquiredChannelCount);
            cancellation.Cancel();
            Assert.True(cancelledAcquire.IsCanceled);
            var nextAcquire = pool.AcquireAsync();
            handler.ContinueAcquire();
            var next = await nextAcquire.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(nextAcquire.IsCompletedSuccessfully);
            Assert.True(cancelledAcquire.IsCanceled);
            Assert.Same(handler.Channel, next);
            Assert.Equal(1, pool.AcquiredChannelCount);
            await pool.ReleaseAsync(next);
            Assert.Equal(0, pool.AcquiredChannelCount);
            await pool.CloseAsync();
            Assert.False(handler.Channel.IsOpen);
        }
        finally
        {
            handler.ContinueAcquire();
            if (pool != null)
                await pool.CloseAsync();
            if (handler.Channel != null)
                await handler.Channel.CloseAsync();
        }
    }

    [Fact]
    public async Task TestCancelledPendingAcquireRunsNextTask()
    {
        await using var t = new BootstrapTuple(1);
        FixedChannelPool pool = null;
        IChannel channel = null;
        try
        {
            await t.StartServerAsync();
            pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 1);
            channel = await pool.AcquireAsync();
            using var cancellation = new CancellationTokenSource();
            var cancelledAcquire = pool.AcquireAsync(cancellation.Token);
            await t.Group.Next().SubmitAsync(() => { });
            Assert.False(cancelledAcquire.IsCompleted);
            cancellation.Cancel();
            Assert.True(cancelledAcquire.IsCanceled);
            var nextAcquire = pool.AcquireAsync();
            await t.Group.Next().SubmitAsync(() => { });
            Assert.False(nextAcquire.IsCompleted);
            await pool.ReleaseAsync(channel);
            var next = await nextAcquire.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(nextAcquire.IsCompletedSuccessfully);
            Assert.Same(channel, next);
            Assert.Equal(1, pool.AcquiredChannelCount);
            await pool.ReleaseAsync(next);
            Assert.Equal(0, pool.AcquiredChannelCount);
        }
        finally
        {
            if (pool != null)
                await pool.CloseAsync();
            if (channel != null)
                await channel.CloseAsync();
        }
    }

    [Fact]
    public async Task TestCancelledAcquireChannelClosedWhenPoolCloses()
    {
        await using var t = new BootstrapTuple(1);
        using var handler = new BlockingChannelPoolHandler();
        FixedChannelPool pool = null;
        try
        {
            await t.StartServerAsync();
            pool = new FixedChannelPool(t.Client, handler, 1);
            using var cancellation = new CancellationTokenSource();
            var cancelledAcquire = pool.AcquireAsync(cancellation.Token);
            await handler.AwaitChannelAcquiredAsync();
            cancellation.Cancel();
            Assert.True(cancelledAcquire.IsCanceled);
            Task closeFuture = pool.CloseAsync();
            handler.ContinueAcquire();
            await closeFuture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(closeFuture.IsCompletedSuccessfully);
            await handler.Channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(handler.Channel.IsOpen);
            Assert.Equal(0, pool.AcquiredChannelCount);
        }
        finally
        {
            handler.ContinueAcquire();
            if (pool != null)
                await pool.CloseAsync();
            if (handler.Channel != null)
                await handler.Channel.CloseAsync();
        }
    }

    [Fact]
    public async Task TestAcquireTimeout() => await CheckAcquireTimeout(500);

    [Fact]
    public async Task TestAcquireWithZeroTimeout() => await CheckAcquireTimeout(0);

    private static async Task CheckAcquireTimeout(long timeoutMillis)
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), ChannelHealthChecker.Active,
            AcquireTimeoutAction.Fail, TimeSpan.FromMilliseconds(timeoutMillis), 1, int.MaxValue);
        var channel = await pool.AcquireAsync();
        var future = pool.AcquireAsync();
        // JUnit assertThrows accepts the source private TimeoutException subtype.
        await Assert.ThrowsAnyAsync<TimeoutException>(() => future);
        await t.Server.CloseAsync();
        await channel.CloseAsync();
    }

    [Fact]
    public async Task TestAcquireNewConnection()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), ChannelHealthChecker.Active,
            AcquireTimeoutAction.New, TimeSpan.FromMilliseconds(500), 1, int.MaxValue);
        var channel = await pool.AcquireAsync();
        var channel2 = await pool.AcquireAsync();
        Assert.NotSame(channel, channel2);
        await t.Server.CloseAsync();
        await channel.CloseAsync();
        await channel2.CloseAsync();
    }

    [Fact]
    /**
     * Tests that the acquiredChannelCount is not added up several times for the same channel acquire request.
     * @throws Exception
     */
    public async Task TestAcquireNewConnectionWhen()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 1);
        var channel1 = await pool.AcquireAsync();
        await channel1.CloseAsync();
        _ = pool.ReleaseAsync(channel1);
        var channel2 = await pool.AcquireAsync();
        Assert.NotSame(channel1, channel2);
        await t.Server.CloseAsync();
        await channel2.CloseAsync();
    }

    [Fact]
    public async Task TestAcquireBoundQueue()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 1, 1);
        var channel = await pool.AcquireAsync();
        var future = pool.AcquireAsync();
        Assert.False(future.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.AcquireAsync());
        await t.Server.CloseAsync();
        await channel.CloseAsync();
        await pool.CloseAsync();
        // CLR: observe the queued request's expected close failure instead of leaving an unobserved Task.
        await Assert.ThrowsAsync<InvalidOperationException>(() => future);
    }

    [Fact]
    public async Task TestReleaseDifferentPool()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 1, 1);
        await using var pool2 = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 1, 1);
        var channel = await pool.AcquireAsync();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => pool2.ReleaseAsync(channel));
        // Nothing was acquired from pool2, so the failed release must not give back a slot there.
        Assert.Equal(0, pool2.AcquiredChannelCount);
        await t.Server.CloseAsync();
        await channel.CloseAsync();
    }

    [Fact]
    public async Task TestReleaseAfterClosePool()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 2);
        var acquire = pool.AcquireAsync();
        var channel = await acquire;
        await pool.CloseAsync();
        await t.Group.Next().SubmitAsync(() => { /* NOOP */ });
        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.ReleaseAsync(channel));
        // Since the pool is closed, the Channel should have been closed as well.
        await channel.CloseCompletion;
        Assert.False(channel.IsOpen);
        await t.Server.CloseAsync();
    }

    [Fact]
    public async Task TestReleaseClosed()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 2);
        var channel = await pool.AcquireAsync();
        await channel.CloseAsync();
        await pool.ReleaseAsync(channel);
        await t.Server.CloseAsync();
    }

    [Fact]
    public async Task TestReleaseFailsWithIllegalArgumentExceptionFromHandler()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        var exception = new ArgumentException();
        await using var pool = new FixedChannelPool(t.Client, new CallbackHandler(released: _ => throw exception), 1);
        var channel = await pool.AcquireAsync();
        Assert.Equal(1, pool.AcquiredChannelCount);
        var releaseFuture = pool.ReleaseAsync(channel);
        var observed = await Assert.ThrowsAsync<ArgumentException>(() => releaseFuture);
        Assert.Same(exception, observed);
        // The exception was not caused by releasing the Channel to the wrong pool, so the slot must be given back.
        Assert.Equal(0, pool.AcquiredChannelCount);
        var acquireFuture = pool.AcquireAsync();
        var acquired = await acquireFuture.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(acquireFuture.IsCompletedSuccessfully);
        Assert.Equal(1, pool.AcquiredChannelCount);
        await t.Server.CloseAsync();
        await acquired.CloseAsync();
    }

    [Fact]
    public async Task TestCloseAsync()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), 2);
        await pool.AcquireAsync();
        await pool.AcquireAsync();
        var closeFuture = pool.CloseAsync();
        async Task CloseServerAfterPoolAsync()
        {
            await closeFuture;
            Assert.Equal(0, pool.AcquiredChannelCount);
            await t.Server.CloseAsync();
        }
        Task closeServer = CloseServerAfterPoolAsync();
        await closeFuture;
        await closeServer;
    }

    [Fact]
    public async Task TestChannelAcquiredException()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        var exception = new NullReferenceException();
        await using var pool = new FixedChannelPool(t.Client, new CallbackHandler(acquired: _ => throw exception), 2);
        var observed = await Assert.ThrowsAsync<NullReferenceException>(() => pool.AcquireAsync());
        Assert.Same(exception, observed);
        await t.Server.CloseAsync();
    }

    [Fact]
    public async Task TestChannelReleaseHealthCheck()
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new InnerFixedChannelPool(t.Client, true);
        // releaseHealthCheck=true,channel=open,doHealthCheckOnRelease
        var channel = await pool.AcquireAsync();
        await pool.ReleaseAsync(channel);
        var sameChannel = await pool.AcquireAsync();
        Assert.Same(sameChannel, channel);
        // releaseHealthCheck=true,channel=close,doHealthCheckOnRelease
        await sameChannel.CloseAsync();
        await pool.ReleaseAsync(channel);
        sameChannel = await pool.AcquireAsync();
        Assert.NotSame(sameChannel, channel);
        // close all and create a new poll
        await sameChannel.CloseAsync();
        await channel.CloseAsync();
        await pool.CloseAsync();
        await using var noHealthCheck = new InnerFixedChannelPool(t.Client, false);
        // releaseHealthCheck=false,channel=open,releaseAndOffer
        channel = await noHealthCheck.AcquireAsync();
        await noHealthCheck.ReleaseAsync(channel);
        sameChannel = await noHealthCheck.AcquireAsync();
        Assert.Same(sameChannel, channel);
        // releaseHealthCheck=false,channel=close,releaseAndOffer
        await sameChannel.CloseAsync();
        await noHealthCheck.ReleaseAsync(channel);
        // the acquire will do a heath check.. this is why we call the special method
        sameChannel = noHealthCheck.PollForTest();
        Assert.Same(sameChannel, channel);
        await t.Server.CloseAsync();
        await sameChannel.CloseAsync();
        await channel.CloseAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestChannelProcessingOrder(bool lastInFirstOutOrdering)
    {
        await using var t = new BootstrapTuple();
        // Start server
        await t.StartServerAsync();
        await using var pool = new FixedChannelPool(t.Client, new TestChannelPoolHandler(), ChannelHealthChecker.Active,
            AcquireTimeoutAction.New, TimeSpan.FromMilliseconds(500), 1, int.MaxValue, false, lastInFirstOutOrdering);
        // create
        int totalChannels = 5;
        var channels = new List<IChannel>();
        for (int i = 0; i < totalChannels; i++)
            channels.Add(await pool.AcquireAsync());
        for (int i = 0; i < totalChannels; i++)
            await pool.ReleaseAsync(channels[i]);
        // test logic
        for (int i = 0; i < totalChannels; i++)
        {
            var channel = await pool.AcquireAsync();
            if (lastInFirstOutOrdering)
                Assert.Same(channel, channels[totalChannels - 1 - i]);
            else
                Assert.Same(channel, channels[i]);
        }
        // close all
        for (int i = 0; i < totalChannels; i++)
        {
            await channels[i].CloseAsync();
            await pool.ReleaseAsync(channels[i]);
        }
        await t.Server.CloseAsync();
    }

    // CLR: source bootstrap(group) and Tuple own the same addresses/initializers here. Each test
    // owns its group instead of a shared BeforeAll group, and shutdown is explicitly awaited.
    internal sealed class BootstrapTuple : IAsyncDisposable
    {
        internal readonly DefaultEventLoopGroup Group;
        internal readonly LocalAddress Address = new(ChannelPoolTestUtils.GetLocalAddrId());
        internal readonly Bootstrap Client;
        internal readonly ServerBootstrap ServerBootstrap;
        internal IChannel Server;
        internal BootstrapTuple(int threads = 2)
        {
            Group = new DefaultEventLoopGroup(threads);
            Client = new Bootstrap().RemoteAddress(Address).Group(Group).Channel<LocalChannel>();
            ServerBootstrap = new ServerBootstrap().Group(Group).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel => channel.Pipeline.AddLast(new ChannelInboundHandlerAdapter())));
        }
        internal async Task StartServerAsync() => Server = await ServerBootstrap.BindAsync(Address).WaitAsync(TimeSpan.FromSeconds(5));
        public async ValueTask DisposeAsync()
        {
            if (Server != null)
                await Server.CloseAsync();
            await Group.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockingChannelPoolHandler : AbstractChannelPoolHandler, IDisposable
    {
        private readonly TaskCompletionSource _channelAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _continueAcquire = new();
        private IChannel _channel;
        internal IChannel Channel => Volatile.Read(ref _channel);
        public override void ChannelCreated(IChannel channel) { /* NOOP */ }
        public override void ChannelAcquired(IChannel channel)
        {
            if (Interlocked.CompareExchange(ref _channel, channel, null) != null)
                return;
            _channelAcquired.TrySetResult();
            if (!_continueAcquire.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out waiting to continue acquire");
        }
        internal Task AwaitChannelAcquiredAsync() => _channelAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        internal void ContinueAcquire() => _continueAcquire.Set();
        public void Dispose() => _continueAcquire.Dispose();
    }

    private sealed class TestChannelPoolHandler : AbstractChannelPoolHandler
    {
        public override void ChannelCreated(IChannel channel) { /* NOOP */ }
    }
    private sealed class CallbackHandler(Action<IChannel> acquired = null, Action<IChannel> released = null) : AbstractChannelPoolHandler
    {
        public override void ChannelCreated(IChannel channel) { /* NOOP */ }
        public override void ChannelAcquired(IChannel channel) => acquired?.Invoke(channel);
        public override void ChannelReleased(IChannel channel) => released?.Invoke(channel);
    }
    private sealed class InnerFixedChannelPool(Bootstrap bootstrap, bool releaseHealthCheck)
        : FixedChannelPool(bootstrap, new TestChannelPoolHandler(), ChannelHealthChecker.Active, AcquireTimeoutAction.New,
            TimeSpan.FromMilliseconds(500), 1, int.MaxValue, releaseHealthCheck)
    {
        /**
         * The acquire always do a health check.
         * Only for testing purpose.
         *
         * @return channel
         */
        internal IChannel PollForTest() => base.PollChannel();
    }
}
