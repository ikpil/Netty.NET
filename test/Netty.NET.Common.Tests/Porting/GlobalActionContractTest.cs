using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Global executor")]
public class GlobalActionContractTest
{
    [Fact]
    public async Task NativeCallbacksKeepQueueIdentityAndFifoAfterMulticastFailure()
    {
        var executor = GlobalEventExecutor.INSTANCE;
        if (executor._thread != null) Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var order = new List<int>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ownedThread = true;
        executor.Execute(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Action first = () =>
            {
                ownedThread &= executor.InEventLoop();
                order.Add(1);
                executor.Execute(() => { order.Add(4); finished.SetResult(); });
            };
            Action failing = () => { order.Add(2); throw new InvalidOperationException("multicast"); };
            Action skipped = () => order.Add(-1);
            executor.Execute(first);
            executor.Execute(failing + skipped);
            executor.Execute(() => { ownedThread &= executor.InEventLoop(); order.Add(3); });
            var field = typeof(GlobalEventExecutor).GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            var queue = Assert.IsType<LinkedBlockingQueue<Action>>(field.GetValue(executor));
            Assert.True(queue.TryPeek(out Action head));
            Assert.Same(first, head);
            Assert.Equal(3, executor.PendingTasks());
            Assert.Null(typeof(GlobalEventExecutor).GetMethod("TakeTask", BindingFlags.Instance | BindingFlags.Public));
            Assert.Equal("task", Assert.Throws<ArgumentNullException>(() => executor.Execute((Action)null)).ParamName);
        }
        finally { release.Set(); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 1, 2, 3, 4 }, order);
        Assert.True(ownedThread);
    }

    [Fact]
    public async Task ScheduledNativeContextAndCancellationSurviveIdleRestart()
    {
        var executor = GlobalEventExecutor.INSTANCE;
        if (executor._thread != null) Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
        var ambient = new AsyncLocal<string>();
        using var cancellation = new CancellationTokenSource();
        int canceledRuns = 0;
        Thread firstWorker = null;
        try
        {
            ambient.Value = "caller";
            string captured = await executor.ScheduleAsync(() =>
            {
                firstWorker = Thread.CurrentThread;
                string value = ambient.Value;
                ambient.Value = "scheduled";
                return value;
            }, TimeSpan.Zero, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            executor.Execute(() => raw.SetResult(ambient.Value));
            Assert.Equal("caller", captured);
            Assert.Null(await raw.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Task canceled = executor.ScheduleAsync(() => canceledRuns++, TimeSpan.FromHours(1), cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
            Assert.False(firstWorker.IsAlive);
            Thread secondWorker = await executor.SubmitAsync(() => Thread.CurrentThread, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.NotSame(firstWorker, secondWorker);
            Assert.Equal(0, canceledRuns);
            Assert.Equal("caller", ambient.Value);
            Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
        }
        finally { ambient.Value = null; }
    }
}
