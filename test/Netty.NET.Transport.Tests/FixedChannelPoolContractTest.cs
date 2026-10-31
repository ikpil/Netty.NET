using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Pool;

namespace Netty.NET.Transport.Tests;

public class FixedChannelPoolContractTest
{
    [Fact]
    public async Task CancelledPendingChainPreservesCallbacksAndDoesNotRecurseWithoutBound()
    {
        await using var t = new FixedChannelPoolTest.BootstrapTuple(1);
        await t.StartServerAsync();
        var handler = new CountingChannelPoolHandler();
        await using var pool = new FixedChannelPool(t.Client, handler, 1);
        var channel = await pool.AcquireAsync(TestContext.Current.CancellationToken);
        const int cancelledCount = 1024;
        var cancelled = new Task<IChannel>[cancelledCount];
        for (int i = 0; i < cancelledCount; i++)
        {
            using var cancellation = new CancellationTokenSource();
            cancelled[i] = pool.AcquireAsync(cancellation.Token);
            cancellation.Cancel();
        }
        var next = pool.AcquireAsync(TestContext.Current.CancellationToken);
        await pool.ReleaseAsync(channel);
        Assert.Same(channel, await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.All(cancelled, task => Assert.True(task.IsCanceled));
        Assert.Equal(1, pool.AcquiredChannelCount);
        Assert.Equal(1, handler.ChannelCount);
        Assert.Equal(cancelledCount + 2, handler.AcquiredCount);
        Assert.Equal(cancelledCount + 1, handler.ReleasedCount);
        await pool.ReleaseAsync(channel);
        Assert.Equal(0, pool.AcquiredChannelCount);
        Assert.Equal(cancelledCount + 2, handler.ReleasedCount);
    }

    [Fact]
    public async Task AdmittedRequestCancelsItsNativeTimeoutInsteadOfOpeningAnotherConnection()
    {
        await using var t = new FixedChannelPoolTest.BootstrapTuple(1);
        await t.StartServerAsync();
        var handler = new CountingChannelPoolHandler();
        await using var pool = new FixedChannelPool(t.Client, handler, ChannelHealthChecker.Active,
            FixedChannelPool.AcquireTimeoutAction.New, TimeSpan.FromMilliseconds(500), 1, int.MaxValue);
        var channel = await pool.AcquireAsync(TestContext.Current.CancellationToken);
        var next = pool.AcquireAsync(TestContext.Current.CancellationToken);
        await pool.ReleaseAsync(channel);
        Assert.Same(channel, await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        Assert.Equal(1, pool.AcquiredChannelCount);
        Assert.Equal(1, handler.ChannelCount);
        await pool.ReleaseAsync(channel);
        Assert.Equal(0, pool.AcquiredChannelCount);
    }
}
