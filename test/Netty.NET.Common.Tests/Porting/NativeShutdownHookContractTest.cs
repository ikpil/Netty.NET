using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NativeShutdownHookContractTest
{
    private sealed class RecordingHook(List<string> trace, string name)
    {
        internal Thread Thread;
        internal void Invoke() { Thread = Thread.CurrentThread; trace.Add(name); }
    }

    private static async Task Stop(SingleThreadEventExecutor executor)
    {
        await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EquivalentMethodGroupsDeduplicateRemoveAndReinsertInRegistrationOrder()
    {
        var executor = new DefaultEventExecutor();
        var trace = new List<string>();
        var first = new RecordingHook(trace, "first");
        var second = new RecordingHook(trace, "second");
        Action original = new Action(first.Invoke);
        Action equivalent = new Action(first.Invoke);
        Assert.False(ReferenceEquals(original, equivalent));
        Assert.Equal(original, equivalent);
        try
        {
            executor.AddShutdownHook(original);
            executor.AddShutdownHook(equivalent);
            executor.AddShutdownHook(second.Invoke);
            executor.RemoveShutdownHook(equivalent);
            executor.AddShutdownHook(original);
            executor.AddShutdownHook(equivalent);
            await Stop(executor);
            Assert.Equal(new[] { "second", "first" }, trace);
            Assert.Same(first.Thread, second.Thread);
            Assert.NotSame(Thread.CurrentThread, first.Thread);
        }
        finally { await Stop(executor); }
    }

    [Fact]
    public async Task MutationsAffectTheNextSnapshotAndDoNotWithdrawAnAlreadySnapshottedHook()
    {
        var executor = new DefaultEventExecutor();
        var trace = new List<string>();
        Action second = () => trace.Add("second");
        Action third = () => trace.Add("third");
        Action withdrawn = () => trace.Add("withdrawn");
        try
        {
            executor.AddShutdownHook(() =>
            {
                trace.Add("first");
                executor.RemoveShutdownHook(second);
                executor.AddShutdownHook(withdrawn);
                executor.RemoveShutdownHook(withdrawn);
                executor.AddShutdownHook(third);
                executor.AddShutdownHook(third);
            });
            executor.AddShutdownHook(second);
            await Stop(executor);
            Assert.Equal(new[] { "first", "second", "third" }, trace);
        }
        finally { await Stop(executor); }
    }

    [Fact]
    public async Task HookFailureDoesNotPreventFollowingHooksAndMulticastIsOneRegistration()
    {
        var executor = new DefaultEventExecutor();
        var trace = new List<string>();
        Action first = () => { trace.Add("throw"); throw new InvalidOperationException("expected hook failure"); };
        Action skipped = () => trace.Add("skipped multicast tail");
        try
        {
            executor.AddShutdownHook(first + skipped);
            executor.AddShutdownHook(() => trace.Add("after"));
            await Stop(executor);
            Assert.Equal(new[] { "throw", "after" }, trace);
            Assert.True(executor.Termination.IsCompletedSuccessfully);
        }
        finally { await Stop(executor); }
    }

    private sealed class NativeHookLoop : SingleThreadEventExecutor
    {
        internal int Dispatches;
        internal NativeHookLoop() : base(null, new DefaultThreadFactory("native-hook", true), true) { }
        public override void Execute(Action task)
        {
            Interlocked.Increment(ref Dispatches);
            base.Execute(task);
        }
        protected override void Run()
        {
            for (;;)
            {
                var task = TakeTask();
                if (task != null) { RunTask(task); UpdateLastExecutionTime(); }
                if (ConfirmShutdown()) return;
            }
        }
    }

    [Fact]
    public async Task OffLoopEditsVisitTheNativeVirtualHookAndOnLoopEditsStayInline()
    {
        var executor = new NativeHookLoop();
        int runs = 0;
        Action hook = () => runs++;
        try
        {
            executor.AddShutdownHook(hook);
            executor.RemoveShutdownHook(hook);
            Assert.Equal(2, executor.Dispatches);
            int inlineDispatches = await executor.SubmitAsync(() =>
            {
                int before = executor.Dispatches;
                executor.AddShutdownHook(hook);
                executor.RemoveShutdownHook(hook);
                executor.AddShutdownHook(hook);
                return executor.Dispatches - before;
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, inlineDispatches);
            await Stop(executor);
            Assert.Equal(1, runs);
        }
        finally { await Stop(executor); }
    }

    [Fact]
    public async Task NullHooksFailBeforeDispatchAndTerminatedExecutorsRejectEdits()
    {
        var executor = new NativeHookLoop();
        try
        {
            Assert.Equal("task", Assert.Throws<ArgumentNullException>(() => executor.AddShutdownHook(null)).ParamName);
            Assert.Equal("task", Assert.Throws<ArgumentNullException>(() => executor.RemoveShutdownHook(null)).ParamName);
            Assert.Equal(0, executor.Dispatches);
            await executor.SubmitAsync(() =>
            {
                Assert.Equal("task", Assert.Throws<ArgumentNullException>(() => executor.AddShutdownHook(null)).ParamName);
                Assert.Equal("task", Assert.Throws<ArgumentNullException>(() => executor.RemoveShutdownHook(null)).ParamName);
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Stop(executor);
            Action hook = () => throw new InvalidOperationException("must not run");
            Assert.Throws<RejectedExecutionException>(() => executor.AddShutdownHook(hook));
            Assert.Throws<RejectedExecutionException>(() => executor.RemoveShutdownHook(hook));
        }
        finally { await Stop(executor); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterPayload(SingleThreadEventExecutor executor, bool remove)
    {
        var payload = new object();
        var weak = new WeakReference(payload);
        Action hook = () => GC.KeepAlive(payload);
        executor.AddShutdownHook(hook);
        if (remove) executor.RemoveShutdownHook(hook);
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertCollected(WeakReference weak)
    {
        for (int attempt = 0; attempt < 10 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(weak.IsAlive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedOrCompletedHooksReleaseCapturedPayloads(bool remove)
    {
        var executor = new DefaultEventExecutor();
        try
        {
            WeakReference weak = RegisterPayload(executor, remove);
            await executor.SubmitAsync(() => { }, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (!remove) await Stop(executor);
            AssertCollected(weak);
            GC.KeepAlive(executor);
        }
        finally { await Stop(executor); }
    }
}
