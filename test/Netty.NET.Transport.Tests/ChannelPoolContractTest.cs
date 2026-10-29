using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Pool;

namespace Netty.NET.Transport.Tests;

public class ChannelPoolContractTest
{
    [Fact]
    public async Task CancelledConnectionIsReturnedAfterTheConnectCompletes()
    {
        await using var setup = await SimpleChannelPoolTest.Setup.StartAsync();
        var handler = new ObservedHandler();
        await using var pool = new DelayedConnectPool(setup.Client, handler);
        using var cancellation = new CancellationTokenSource();
        Task<IChannel> acquire = pool.AcquireAsync(cancellation.Token);
        var channel = await pool.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.True(acquire.IsCanceled);
        pool.CompleteConnect.TrySetResult();
        await handler.Released.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Acquired);
        Assert.True(channel.IsActive);
        Assert.Same(channel, await pool.AcquireAsync(TestContext.Current.CancellationToken));
        await pool.ReleaseAsync(channel);
    }

    [Fact]
    public async Task AsyncHealthCompletionMaintainsChannelEventLoopOwnership()
    {
        await using var setup = await SimpleChannelPoolTest.Setup.StartAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ObservedHandler();
        await using var pool = new SimpleChannelPool(setup.Client, handler, channel =>
        {
            Assert.True(channel.EventLoop.InEventLoop());
            started.TrySetResult();
            return check.Task;
        }, false);
        var first = await pool.AcquireAsync(TestContext.Current.CancellationToken);
        await pool.ReleaseAsync(first);
        var acquire = pool.AcquireAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Run(() => check.SetResult(true), TestContext.Current.CancellationToken);
        Assert.Same(first, await acquire.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Acquired);
        await pool.ReleaseAsync(first);
    }

    [Fact]
    public async Task FailedAsyncHealthCheckClosesTheOldChannelAndConnectsANewOne()
    {
        await using var setup = await SimpleChannelPoolTest.Setup.StartAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = new SimpleChannelPool(setup.Client, new ObservedHandler(), _ =>
        {
            started.TrySetResult();
            return check.Task;
        }, false);
        var first = await pool.AcquireAsync(TestContext.Current.CancellationToken);
        await pool.ReleaseAsync(first);
        var acquire = pool.AcquireAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        check.SetException(new IOException("Health check failed."));
        var second = await acquire.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotSame(first, second);
        Assert.False(first.IsOpen);
        await pool.ReleaseAsync(second);
    }

    [Fact]
    public async Task LifoAndFifoSelectionPreserveReleasedChannelIdentity()
    {
        foreach (bool lifo in new[] { true, false })
        {
            await using var setup = await SimpleChannelPoolTest.Setup.StartAsync();
            await using var pool = new SimpleChannelPool(setup.Client, new ObservedHandler(), ChannelHealthChecker.Active, true, lifo);
            var first = await pool.AcquireAsync(TestContext.Current.CancellationToken);
            var second = await pool.AcquireAsync(TestContext.Current.CancellationToken);
            await pool.ReleaseAsync(first);
            await pool.ReleaseAsync(second);
            var next = await pool.AcquireAsync(TestContext.Current.CancellationToken);
            var last = await pool.AcquireAsync(TestContext.Current.CancellationToken);
            Assert.Same(lifo ? second : first, next);
            Assert.Same(lifo ? first : second, last);
            await pool.ReleaseAsync(next);
            await pool.ReleaseAsync(last);
        }
    }

    [Fact]
    public async Task ClonedPoolBootstrapRegistersOnTheExplicitEventLoop()
    {
        await using var setup = await SimpleChannelPoolTest.Setup.StartAsync();
        var owner = setup.Group.Next();
        await using var pool = new SimpleChannelPool(setup.Client.Clone(owner), new ObservedHandler());
        var channel = await pool.AcquireAsync(TestContext.Current.CancellationToken);
        Assert.Same(owner, channel.EventLoop);
        await pool.ReleaseAsync(channel);
    }

    [Fact]
    public async Task ConcurrentMapCreationClosesTheDiscardedPool()
    {
        using var barrier = new Barrier(2);
        var created = new ConcurrentQueue<TrackingPool>();
        await using var map = new RacingMap(() =>
        {
            var pool = new TrackingPool();
            created.Enqueue(pool);
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            return pool;
        });
        var results = await Task.WhenAll(Task.Run(() => map.Get("key"), TestContext.Current.CancellationToken),
            Task.Run(() => map.Get("key"), TestContext.Current.CancellationToken));
        Assert.Same(results[0], results[1]);
        Assert.Equal(1, map.Size);
        Assert.Equal(2, created.Count);
        Assert.Single(created, pool => pool.Closed);
        Assert.False(results[0].Closed);
        await map.CloseAsync();
        Assert.All(created, pool => Assert.True(pool.Closed));
        Assert.True(map.IsEmpty);
    }

    private sealed class ObservedHandler : AbstractChannelPoolHandler
    {
        private int _acquired;
        internal int Acquired => Volatile.Read(ref _acquired);
        internal readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelCreated(IChannel channel) => Assert.True(channel.EventLoop.InEventLoop());
        public override void ChannelAcquired(IChannel channel)
        {
            Assert.True(channel.EventLoop.InEventLoop());
            Interlocked.Increment(ref _acquired);
        }
        public override void ChannelReleased(IChannel channel)
        {
            Assert.True(channel.EventLoop.InEventLoop());
            Released.TrySetResult();
        }
    }
    private sealed class DelayedConnectPool(Bootstrap bootstrap, IChannelPoolHandler handler) : SimpleChannelPool(bootstrap, handler)
    {
        internal readonly TaskCompletionSource<IChannel> Connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CompleteConnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<IChannel> ConnectChannelAsync(Bootstrap source)
        {
            var channel = await base.ConnectChannelAsync(source);
            Connected.TrySetResult(channel);
            await CompleteConnect.Task;
            return channel;
        }
    }
    private sealed class RacingMap(Func<TrackingPool> create) : AbstractChannelPoolMap<string, TrackingPool>
    {
        protected override TrackingPool NewPool(string key) => create();
    }
    private sealed class TrackingPool : IChannelPool
    {
        internal bool Closed;
        public Task<IChannel> AcquireAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(IChannel channel) => throw new NotSupportedException();
        public Task CloseAsync() { Closed = true; return Task.CompletedTask; }
        public void Close() => Closed = true;
        public void Dispose() => Close();
        public ValueTask DisposeAsync() => new(CloseAsync());
    }
}
