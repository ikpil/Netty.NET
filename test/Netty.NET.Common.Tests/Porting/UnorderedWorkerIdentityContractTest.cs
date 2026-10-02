using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedWorkerIdentityContractTest
{
    private sealed class Factory(Func<IRunnable, Thread> create) : IThreadFactory
    {
        public Thread newThread(IRunnable task) => create(task);
    }

    [Fact]
    public void ConfiguredFactoryIdentityIsIndependentOfWorkerAccounting()
    {
        var original = new Factory(task => new Thread(task.run) { IsBackground = true });
        var replacement = new Factory(task => new Thread(task.run) { IsBackground = true });
        var executor = new UnorderedThreadPoolEventExecutor(1, original);
        try
        {
            Assert.Same(original, executor.getThreadFactory());
            executor.setThreadFactory(replacement);
            Assert.Same(replacement, executor.getThreadFactory());
        }
        finally
        {
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScheduledCallbackHasAffinityOnlyInsideTheWorkerLoop(bool replaceFactory)
    {
        UnorderedThreadPoolEventExecutor executor = null;
        Thread worker = null;
        bool prefixAffinity = true, suffixAffinity = true;
        using var suffixDone = new ManualResetEventSlim();
        var factory = new Factory(task => worker = new Thread(() =>
        {
            prefixAffinity = executor.inEventLoop();
            try { task.run(); }
            finally
            {
                suffixAffinity = executor.inEventLoop();
                suffixDone.Set();
            }
        }) { IsBackground = true });
        executor = replaceFactory
            ? new UnorderedThreadPoolEventExecutor(1)
            : new UnorderedThreadPoolEventExecutor(1, factory);
        if (replaceFactory) executor.setThreadFactory(factory);
        try
        {
            var invocation = executor.ScheduleAsync(() =>
                (Thread.CurrentThread, executor.inEventLoop(), executor.isExecutorThread(Thread.CurrentThread)),
                TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.Same(worker, invocation.Item1);
            Assert.True(invocation.Item2);
            Assert.True(invocation.Item3);
            executor.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(suffixDone.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.False(prefixAffinity);
            Assert.False(suffixAffinity);
            Assert.False(executor.inEventLoop(worker));
        }
        finally
        {
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
            if (worker != null) Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void ReplacementFactoryKeepsOldAndNewWorkersRecognizedTogether()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Thread oldWorker = null;
        Task<bool> first = executor.SubmitAsync(() =>
        {
            oldWorker = Thread.CurrentThread;
            entered.Set();
            release.Wait();
            return executor.inEventLoop();
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            executor.setThreadFactory(new Factory(task => new Thread(task.run) { IsBackground = true }));
            executor.setCorePoolSize(2);
            var second = executor.ScheduleAsync(() => (Thread.CurrentThread, executor.inEventLoop()),
                TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.NotSame(oldWorker, second.Item1);
            Assert.True(second.Item2);
            Assert.True(executor.inEventLoop(oldWorker));
            Assert.True(executor.inEventLoop(second.Item1));
            release.Set();
            Assert.True(first.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally
        {
            release.Set();
            executor.shutdownNow();
            Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
        }
    }
}
