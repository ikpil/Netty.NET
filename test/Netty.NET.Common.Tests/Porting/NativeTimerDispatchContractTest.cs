using System;
using System.Collections.Concurrent;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Timer globals")]
public class NativeTimerDispatchContractTest
{
    [Fact]
    public void InvalidDispatcherIsRejectedBeforeCreatingAWorker()
    {
        int creations = 0;
        var factory = new AnonymousThreadFactory(entry => { creations++; return new Thread(entry.Invoke); });
        Assert.Equal("taskExecutor", Assert.Throws<ArgumentNullException>(() =>
            new HashedWheelTimer(factory, TimeSpan.FromMilliseconds(1), 4, false, 1, null)).ParamName);
        Assert.Equal(0, creations);
    }

    [Fact]
    public void ExpiredDispatchesHaveHostOwnedLifetimeAndDoNotConsumeTimerPendingCapacity()
    {
        using var queue = new BlockingCollection<Action>();
        using var timer = new HashedWheelTimer(new DefaultThreadFactory("timer-dispatch", true),
            TimeSpan.FromMilliseconds(1), 4, false, 1, command => queue.Add(command));
        ITimeout[] observed = new ITimeout[2];
        ITimeout first = timer.NewTimeout(TimerTask.Create(timeout => observed[0] = timeout), TimeSpan.Zero);
        Assert.True(queue.TryTake(out Action firstEntry, 5000, TestContext.Current.CancellationToken));
        Assert.True(first.IsExpired());
        Assert.Equal(0, timer.PendingTimeouts());
        ITimeout second = timer.NewTimeout(TimerTask.Create(timeout => observed[1] = timeout), TimeSpan.Zero);
        Assert.True(queue.TryTake(out Action secondEntry, 5000, TestContext.Current.CancellationToken));
        Assert.True(second.IsExpired());
        Assert.Equal(0, timer.PendingTimeouts());
        Assert.Null(observed[0]);
        Assert.Null(observed[1]);
        Assert.Empty(timer.Stop());
        Assert.False(first.Cancel());
        Assert.False(second.Cancel());
        firstEntry();
        secondEntry();
        Assert.Same(first, observed[0]);
        Assert.Same(second, observed[1]);
        Assert.Equal(0, queue.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultDispatchUsesThePhysicalWorkerStartContextRatherThanEachPublisherContext(bool suppressFlow)
    {
        var ambient = new AsyncLocal<string>();
        ambient.Value = "construction";
        using var observations = new BlockingCollection<(Thread Thread, string Context)>();
        Thread worker = null;
        var factory = new AnonymousThreadFactory(entry => worker = new Thread(entry.Invoke) { IsBackground = true });
        using var timer = new HashedWheelTimer(factory, TimeSpan.FromMilliseconds(1));
        Assert.True((worker.ThreadState & ThreadState.Unstarted) != 0);
        ITimerTask callback = TimerTask.Create(_ => observations.Add((Thread.CurrentThread, ambient.Value)));
        try
        {
            ambient.Value = "start";
            if (suppressFlow)
            {
                using (ExecutionContext.SuppressFlow()) timer.NewTimeout(callback, TimeSpan.Zero);
            }
            else timer.NewTimeout(callback, TimeSpan.Zero);
            Assert.True(observations.TryTake(out var first, 5000, TestContext.Current.CancellationToken));
            Assert.Same(worker, first.Thread);
            Assert.Equal(suppressFlow ? null : "start", first.Context);
            ambient.Value = "later publisher";
            timer.NewTimeout(callback, TimeSpan.Zero);
            Assert.True(observations.TryTake(out var second, 5000, TestContext.Current.CancellationToken));
            Assert.Same(worker, second.Thread);
            Assert.Equal(first.Context, second.Context);
            Assert.Equal("later publisher", ambient.Value);
        }
        finally
        {
            ambient.Value = null;
        }
    }

    [Fact]
    public void NativeThreadStarterCanDispatchTimerCallbacksWithoutJavaAdapters()
    {
        Thread callbackWorker = null;
        var starter = new ThreadPerTaskExecutor(new AnonymousThreadFactory(entry =>
            callbackWorker = new Thread(entry.Invoke) { IsBackground = true, Name = "native-timer-callback" }));
        Thread timerWorker = null;
        using var results = new BlockingCollection<(Thread Thread, ITimeout Timeout)>();
        using var timer = new HashedWheelTimer(new AnonymousThreadFactory(entry =>
            timerWorker = new Thread(entry.Invoke) { IsBackground = true }), TimeSpan.FromMilliseconds(1),
            4, false, 1, starter.Execute);
        ITimeout timeout = timer.NewTimeout(TimerTask.Create(handle => results.Add((Thread.CurrentThread, handle))), TimeSpan.Zero);
        Assert.True(results.TryTake(out var result, 5000, TestContext.Current.CancellationToken));
        Assert.Same(callbackWorker, result.Thread);
        Assert.Same(timeout, result.Timeout);
        Assert.NotSame(timerWorker, result.Thread);
        Assert.Equal("native-timer-callback", result.Thread.Name);
        Assert.True(timeout.IsExpired());
        Assert.True(callbackWorker.Join(TimeSpan.FromSeconds(5)));
        Assert.Empty(timer.Stop());
    }
}
