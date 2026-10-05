using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

// This legacy unordered backend remains in the ownership/lifecycle contract matrix.
#pragma warning disable CS0612, CS0618

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedActionBoundaryContractTest
{
    private sealed class NoWorkerFactory : IThreadFactory
    {
        public Thread NewThread(Action work) => null;
    }

    private static void Wait(ManualResetEventSlim gate)
        => Assert.True(gate.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

    [Fact]
    public void PublicStopUsesThePersistentTaskWithoutAJdkServiceOrMembershipList()
    {
        Type[] types = [typeof(IEventExecutorGroup), typeof(AbstractEventExecutor),
            typeof(AbstractEventExecutorGroup), typeof(NonStickyEventExecutorGroup), typeof(UnorderedThreadPoolEventExecutor)];
        foreach (Type type in types)
        {
            Assert.Null(type.GetMethod("ShutdownNow"));
            Assert.Equal(typeof(Task), type.GetMethod("StopAsync").ReturnType);
        }
        Assert.Null(typeof(IEventExecutorGroup).Assembly.GetType("Netty.NET.Common.Concurrent.IExecutorService"));
        Assert.Equal(typeof(IExecutor), Assert.Single(typeof(IEventExecutorGroup).GetInterfaces()));
        var callbacks = typeof(UnorderedThreadPoolEventExecutor).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()).Where(parameter => parameter.Name == "handler").ToArray();
        Assert.Equal(2, callbacks.Length);
        Assert.All(callbacks, parameter => Assert.Equal(typeof(Action<Action, UnorderedThreadPoolEventExecutor>), parameter.ParameterType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedRawReplayClaimsOnceAndImmediateStopPreventsInvocation(bool immediate)
    {
        Action replay = null;
        Thread policyThread = null;
        UnorderedThreadPoolEventExecutor observed = null;
        var executor = new UnorderedThreadPoolEventExecutor(1, (work, owner) =>
        {
            replay = work; observed = owner; policyThread = Thread.CurrentThread;
        });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            Task termination = immediate ? executor.StopAsync() : executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            int calls = 0;
            Thread caller = Thread.CurrentThread;
            executor.Execute(() => Interlocked.Increment(ref calls));
            Assert.NotNull(replay);
            Assert.Same(executor, observed);
            Assert.Same(caller, policyThread);
            Assert.False(termination.IsCompleted);
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(replay, TestContext.Current.CancellationToken)))
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(immediate ? 0 : 1, calls);
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            release.Set();
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task GracefulTerminationPreventsSavedAndNewlyRejectedRawCallbacksFromStarting()
    {
        Action replay = null;
        var executor = new UnorderedThreadPoolEventExecutor(1, (work, _) => replay = work);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            int calls = 0;
            executor.Execute(() => calls++);
            Action beforeDrain = replay;
            Assert.NotNull(beforeDrain);
            release.Set();
            await termination.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            beforeDrain(); beforeDrain();
            executor.Execute(() => calls++);
            Assert.NotSame(beforeDrain, replay);
            replay(); replay();
            Assert.Equal(0, calls);
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            release.Set();
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ThrowingNativePolicyDisablesItsSavedReplayAndPreservesExceptionIdentity()
    {
        Action replay = null;
        var error = new InvalidOperationException("native unordered policy");
        var executor = new UnorderedThreadPoolEventExecutor(1, (work, _) => { replay = work; throw error; });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            _ = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            int calls = 0;
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => executor.Execute(() => calls++)));
            Assert.NotNull(replay);
            replay(); replay();
            Assert.Equal(0, calls);
            Assert.Equal(0, executor.PendingTaskCount);
        }
        finally
        {
            release.Set();
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task NativeRejectionPolicyCanRequestStopFromAnotherThreadWithoutHoldingTheQueueGate()
    {
        using var requested = new ManualResetEventSlim();
        Task stop = null;
        var executor = new UnorderedThreadPoolEventExecutor(1, (_, owner) =>
        {
            new Thread(() => { stop = owner.StopAsync(); requested.Set(); }) { IsBackground = true }.Start();
            Wait(requested);
        });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            executor.Execute(() => { entered.Set(); Wait(release); });
            Wait(entered);
            _ = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            executor.Execute(static () => { });
            Assert.NotNull(stop);
            Assert.Same(executor.Termination, stop);
            release.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            release.Set();
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DiscardingPolicyCannotHideNativeSubmissionOrScheduleRejection()
    {
        int policies = 0;
        var executor = new UnorderedThreadPoolEventExecutor(1, (_, _) => policies++);
        await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task submit = executor.SubmitAsync(static () => { }, TestContext.Current.CancellationToken);
        Task<int> schedule = executor.ScheduleAsync(static () => 7, TimeSpan.Zero, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<RejectedExecutionException>(() => submit);
        await Assert.ThrowsAsync<RejectedExecutionException>(() => schedule);
        Assert.Equal(0, policies);
        executor.Execute(static () => { });
        Assert.Equal(1, policies);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UnorderedThreadPoolEventExecutor Owner, Task Termination, Task Result, WeakReference Payload)
        StopCapturedPayload(string kind)
    {
        var owner = new UnorderedThreadPoolEventExecutor(1, new NoWorkerFactory());
        var payload = new object();
        var weak = new WeakReference(payload);
        Task result = Task.CompletedTask;
        switch (kind)
        {
            case "raw": owner.Execute(() => GC.KeepAlive(payload)); break;
            case "submit": result = owner.SubmitAsync(() => GC.KeepAlive(payload)); break;
            case "schedule": result = owner.ScheduleAsync(() => GC.KeepAlive(payload), TimeSpan.FromDays(1)); break;
            default: throw new ArgumentException(nameof(kind));
        }
        return (owner, owner.StopAsync(), result, weak);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("submit")]
    [InlineData("schedule")]
    public async Task RetainedNativeStopResultsReleasePayloadsAndConcurrentRequestsCannotReviveWork(string kind)
    {
        var stopped = StopCapturedPayload(kind);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
                () => Assert.Same(stopped.Termination, stopped.Owner.StopAsync()), TestContext.Current.CancellationToken)))
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await stopped.Owner.Termination.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (kind != "raw") Assert.True(stopped.Result.IsCanceled);
        Assert.Equal(0, stopped.Owner.PendingTaskCount);
        for (int attempt = 0; attempt < 10 && stopped.Payload.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(stopped.Payload.IsAlive);
        GC.KeepAlive(stopped.Owner); GC.KeepAlive(stopped.Termination); GC.KeepAlive(stopped.Result);
    }

    [Fact]
    public async Task NonStickyWrapperForwardsTheSameNativeStopAndCancellationPolicy()
    {
        var pool = new UnorderedThreadPoolEventExecutor(1, new NoWorkerFactory());
        IEventExecutorGroup group = new NonStickyEventExecutorGroup(pool);
        int calls = 0;
        group.Execute(() => calls++);
        Assert.Equal(1, pool.PendingTaskCount);
        Task stopping = group.StopAsync();
        Assert.Same(pool.Termination, stopping);
        Assert.Same(stopping, group.StopAsync());
        Assert.Equal(0, calls);
        await pool.Termination.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Same(stopping, group.StopAsync());
    }
}
