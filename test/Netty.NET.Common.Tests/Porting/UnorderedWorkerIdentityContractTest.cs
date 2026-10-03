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
    public void ConstructorFactoryCreatesThreadsIndependentlyOfWorkerAccounting()
    {
        Thread created = null;
        var original = new Factory(task => created = new Thread(task.run) { IsBackground = true });
        var executor = new UnorderedThreadPoolEventExecutor(1, original);
        try
        {
            Assert.Equal(0, executor.WorkerCount);
            Thread invoked = executor.SubmitAsync(() => Thread.CurrentThread)
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.Same(created, invoked);
            Assert.True(executor.inEventLoop(created));
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
    public void NativeScheduledCallbackHasAffinityOnlyInsideTheWorkerLoop(bool forwardFactory)
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
        executor = forwardFactory
            ? new UnorderedThreadPoolEventExecutor(1, new Factory(task => factory.newThread(task)))
            : new UnorderedThreadPoolEventExecutor(1, factory);
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
    public void StatefulConstructorFactoryKeepsConcurrentWorkersRecognizedTogether()
    {
        int creations = 0;
        var factory = new Factory(task => new Thread(task.run)
        {
            IsBackground = true,
            Name = Interlocked.Increment(ref creations) == 1 ? "first" : "second"
        });
        var executor = new UnorderedThreadPoolEventExecutor(2, factory);
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
            var second = executor.ScheduleAsync(() => (Thread.CurrentThread, executor.inEventLoop()),
                TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.NotSame(oldWorker, second.Item1);
            Assert.Equal("first", oldWorker.Name);
            Assert.Equal("second", second.Item1.Name);
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
