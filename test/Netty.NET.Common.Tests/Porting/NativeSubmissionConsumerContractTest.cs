using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class NativeSubmissionConsumerContractTest
{
    private sealed class QueuedExecutor : AbstractEventExecutor
    {
        private readonly Queue<IRunnable> _queue = new();
        private bool _running;
        internal void RunOwned(Action action)
        {
            _running = true;
            try { action(); }
            finally { _running = false; }
        }
        internal void RunAll() => RunOwned(() => { while (_queue.TryDequeue(out var task)) task.run(); });
        public override void execute(IRunnable task) => _queue.Enqueue(task);
        public override bool inEventLoop(Thread thread) => _running && thread == Thread.CurrentThread;
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        public override void shutdown() { }
        public override bool isShutdown() => false;
        public override bool isShuttingDown() => false;
        public override bool isTerminated() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlushShapedSubmissionYieldsAndExplicitFlushCancelsUnstartedWork(bool flushImmediately)
    {
        var executor = new QueuedExecutor();
        using var cancellation = new CancellationTokenSource();
        int pendingWrites = 0, flushes = 0;
        Task scheduled = null;
        var events = new List<string>();
        executor.RunOwned(() =>
        {
            ++pendingWrites;
            scheduled = executor.SubmitAsync(() =>
            {
                events.Add("scheduled flush:" + pendingWrites);
                pendingWrites = 0;
                ++flushes;
            }, cancellation.Token);
            events.Add("write one");
            ++pendingWrites;
            events.Add("write two");
            if (flushImmediately)
            {
                cancellation.Cancel();
                events.Add("explicit flush:" + pendingWrites);
                pendingWrites = 0;
                ++flushes;
            }
        });
        Assert.Equal(flushImmediately ? 1 : 0, flushes);
        executor.RunAll();
        if (flushImmediately)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await scheduled);
            Assert.Equal(new[] { "write one", "write two", "explicit flush:2" }, events);
        }
        else
        {
            await scheduled;
            Assert.Equal(new[] { "write one", "write two", "scheduled flush:2" }, events);
        }
        Assert.Equal(1, flushes);
        Assert.Equal(0, pendingWrites);
    }

    private sealed class GatedFactory(ManualResetEventSlim entered, ManualResetEventSlim release) : IThreadFactory
    {
        public Thread newThread(IRunnable task) => new(() =>
        {
            entered.Set();
            release.Wait();
            task.run();
        }) { IsBackground = true };
    }

    [Fact]
    public async Task ThreadPropertiesBootstrapPreservesAnInterruptConsumedDuringItsNativeWait()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var executor = new DefaultEventExecutor(new GatedFactory(entered, release));
        Exception failure = null;
        IThreadProperties observed = null;
        var caller = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                observed = executor.threadProperties();
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(0));
                Thread.Sleep(0);
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        try
        {
            caller.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => (caller.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));
            release.Set();
            Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.NotNull(observed);
            Assert.Same(observed, executor.threadProperties());
            Assert.True(observed.isAlive());
        }
        finally
        {
            release.Set();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class FailingFactory(Exception failure) : IThreadFactory
    {
        private int _attempts;
        public Thread newThread(IRunnable task)
        {
            if (Interlocked.Increment(ref _attempts) == 1) throw failure;
            return new Thread(task.run) { IsBackground = true };
        }
    }

    [Fact]
    public async Task AThreadInterruptedProducerFailureIsNotMistakenForAnInterruptedBootstrapWait()
    {
        var failure = new ThreadInterruptedException("worker creation failed");
        var executor = new DefaultEventExecutor(new FailingFactory(failure));
        try { Assert.Same(failure, Assert.Throws<ThreadInterruptedException>(() => executor.threadProperties())); }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
