using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NativeRejectionContractTest
{
    private sealed class Loop(Action<Action, SingleThreadEventExecutor> policy, Action<Action> starter = null)
        : SingleThreadEventExecutor(null, starter ?? new ThreadPerTaskExecutor(new DefaultThreadFactory("native-rejection", true)).Execute, true, 16, policy)
    {
        internal int WakeCalls;
        internal Action OnWake;
        public override void Wakeup(bool inEventLoop)
        {
            base.Wakeup(inEventLoop);
            if (!inEventLoop && OnWake != null) { WakeCalls++; OnWake(); }
        }
        protected override void Run()
        {
            for (;;)
            {
                var task = TakeTask();
                if (task != null) { SafeExecute(task); UpdateLastExecutionTime(); }
                if (ConfirmShutdown()) return;
            }
        }
    }

    private static void Wait(ManualResetEventSlim gate)
        => Assert.True(gate.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

    private static async Task Stop(Loop executor)
    {
        executor.OnWake = null;
        await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BackoffValidatesRetryCount(int retries)
        => Assert.Contains("retries", Assert.Throws<ArgumentException>(() => RejectedExecutionHandlers.Backoff(retries, TimeSpan.Zero)).Message);

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public async Task NonPositiveBackoffDoesNotWaitAndExhaustsExactlyTheConfiguredRetries(long ticks)
    {
        var executor = new Loop(RejectedExecutionHandlers.Backoff(3, TimeSpan.FromTicks(ticks)));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            for (int index = 0; index < 16; index++) executor.Execute(static () => { });
            executor.OnWake = static () => { };
            int calls = 0;
            Assert.Throws<RejectedExecutionException>(() => executor.Execute(() => calls++));
            Assert.Equal(3, executor.WakeCalls);
            Assert.Equal(16, executor.PendingTasks());
            Assert.Equal(0, calls);
        }
        finally { release.Set(); await Stop(executor); }
    }

    [Fact]
    public async Task OffLoopBackoffReoffersTheSubmissionOnceAfterCapacityBecomesAvailable()
    {
        var executor = new Loop(RejectedExecutionHandlers.Backoff(3, TimeSpan.Zero));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var slotEntered = new ManualResetEventSlim();
        using var slotRelease = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            executor.Execute(() => { slotEntered.Set(); Wait(slotRelease); });
            for (int index = 1; index < 16; index++) executor.Execute(static () => { });
            executor.OnWake = () => { if (executor.WakeCalls == 2) { release.Set(); Wait(slotEntered); } };
            int calls = 0;
            Task<int> result = executor.SubmitAsync(() => { calls++; return 42; }, TestContext.Current.CancellationToken);
            Assert.Equal(2, executor.WakeCalls);
            Assert.Equal(16, executor.PendingTasks());
            Assert.False(result.IsCompleted);
            slotRelease.Set();
            Assert.Equal(42, await result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(1, calls);
        }
        finally { release.Set(); slotRelease.Set(); await Stop(executor); }
    }

    [Fact]
    public async Task OnLoopBackoffRejectsWithoutWaitingOrWakingTheWorker()
    {
        var executor = new Loop(RejectedExecutionHandlers.Backoff(int.MaxValue, TimeSpan.MaxValue));
        try
        {
            executor.OnWake = static () => { };
            await executor.SubmitAsync(() =>
            {
                for (int index = 0; index < 16; index++) executor.Execute(static () => { });
                Assert.Throws<RejectedExecutionException>(() => executor.Execute(static () => { }));
                Assert.Equal(0, executor.WakeCalls);
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { await Stop(executor); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomPolicyRunsOnTheSubmittingThreadAndItsFailureKeepsExceptionIdentity(bool nativeResult)
    {
        var failure = new InvalidOperationException("native policy failure");
        Thread observed = null;
        var executor = new Loop((_, _) => { observed = Thread.CurrentThread; throw failure; });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            for (int index = 0; index < 16; index++) executor.Execute(static () => { });
            Thread caller = Thread.CurrentThread;
            if (nativeResult)
            {
                Task result = executor.SubmitAsync(static () => { }, TestContext.Current.CancellationToken);
                Assert.Same(caller, observed);
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => result));
            }
            else
            {
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => executor.Execute(static () => { })));
                Assert.Same(caller, observed);
            }
        }
        finally { release.Set(); await Stop(executor); }
    }

    [Fact]
    public async Task NativeReofferPreservesQueueIdentityForShutdownRollback()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var slotEntered = new ManualResetEventSlim();
        using var slotRelease = new ManualResetEventSlim();
        Task termination = null;
        var executor = new Loop((work, owner) =>
        {
            release.Set();
            Wait(slotEntered);
            Assert.True(owner.OfferTask(work));
            termination = owner.StopAsync();
        });
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            executor.Execute(() => { slotEntered.Set(); Wait(slotRelease); });
            for (int index = 1; index < 16; index++) executor.Execute(static () => { });
            int calls = 0;
            Task result = executor.SubmitAsync(() => calls++, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<RejectedExecutionException>(() => result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(15, executor.PendingTasks());
            slotRelease.Set();
            await termination.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, calls);
        }
        finally { release.Set(); slotRelease.Set(); await Stop(executor); }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public async Task InterruptedPositiveBackoffRetriesAndRestoresThePendingInterrupt(long ticks)
    {
        var executor = new Loop(RejectedExecutionHandlers.Backoff(1, TimeSpan.FromTicks(ticks)));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var completion = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            for (int index = 0; index < 16; index++) executor.Execute(static () => { });
            executor.OnWake = static () => { };
            var caller = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.Interrupt();
                    Exception rejection = null;
                    try { executor.Execute(static () => { }); }
                    catch (Exception error) { rejection = error; }
                    // xUnit may acquire a resource lock while reading an exception
                    // message. Consume the expected pending interrupt before assertions.
                    bool interrupted = false;
                    try { Thread.Sleep(0); }
                    catch (ThreadInterruptedException) { interrupted = true; }
                    Assert.IsType<RejectedExecutionException>(rejection);
                    Assert.Equal(1, executor.WakeCalls);
                    Assert.True(interrupted);
                    completion.SetResult(null);
                }
                catch (Exception error) { completion.SetResult(error); }
            }) { IsBackground = true };
            caller.Start();
            Exception failure = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Null(failure);
        }
        finally { release.Set(); await Stop(executor); }
    }

    [Fact]
    public async Task NativeOfferHasNoWorkerBootstrapAndClosedAdmissionBypassesThePolicy()
    {
        int policies = 0;
        int starts = 0;
        var executor = new Loop((_, _) => policies++, work => { starts++; new Thread(() => work()) { IsBackground = true }.Start(); });
        try
        {
            Assert.Equal("task", Assert.Throws<ArgumentNullException>(() => executor.OfferTask((Action)null)).ParamName);
            Assert.True(executor.OfferTask(static () => { }));
            Assert.Equal(0, starts);
            await Stop(executor);
            Assert.Throws<RejectedExecutionException>(() => executor.OfferTask(static () => { }));
            Assert.Throws<RejectedExecutionException>(() => executor.Execute(static () => { }));
            Assert.Equal(0, policies);
        }
        finally { await Stop(executor); }
    }
}
