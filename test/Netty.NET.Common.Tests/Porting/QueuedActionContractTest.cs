using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

// The deprecated original pool backend remains part of the execution contract matrix.
#pragma warning disable CS0612

namespace Netty.NET.Common.Tests.Porting;

public class QueuedActionContractTest
{
    private sealed class HookLoop : SingleThreadEventExecutor
    {
        internal int Executions;
        internal int LazyExecutions;
        internal int WakeChecks;
        internal Action Prefix;
        internal HookLoop() : base(null, new DefaultThreadFactory("native-action-hook", true), true) { }
        public override void Execute(Action task)
        {
            Interlocked.Increment(ref Executions);
            base.Execute(Prefix == null ? task : Prefix + task);
        }
        public override void LazyExecute(Action task)
        {
            Interlocked.Increment(ref LazyExecutions);
            base.LazyExecute(task);
        }
        protected override bool WakesUpForTask(IRunnable task)
        {
            Interlocked.Increment(ref WakeChecks);
            return base.WakesUpForTask(task);
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
    public void PublicExecutionContractsAcceptOnlyNativeActions()
    {
        Type[] types = [typeof(IExecutor), typeof(AbstractEventExecutor), typeof(AbstractEventExecutorGroup),
            typeof(SingleThreadEventExecutor), typeof(UnorderedThreadPoolEventExecutor), typeof(ImmediateExecutor),
            typeof(ImmediateEventExecutor), typeof(GlobalEventExecutor), typeof(NonStickyEventExecutorGroup)];
        foreach (Type type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name is "Execute" or "LazyExecute").ToArray();
            Assert.NotEmpty(methods);
            Assert.All(methods, method => Assert.Equal(typeof(Action), Assert.Single(method.GetParameters()).ParameterType));
        }
    }

    private sealed class ForwardingExecutor(UnorderedThreadPoolEventExecutor owner) : AbstractEventExecutor
    {
        internal int Executions;
        public override void Execute(Action task) { Executions++; owner.Execute(task); }
        public override bool InEventLoop(Thread thread) => owner.InEventLoop(thread);
        public override bool IsShuttingDown() => owner.IsShuttingDown();
        public override bool IsShutdown() => owner.IsShutdown();
        public override bool IsTerminated() => owner.IsTerminated();
        [Obsolete]
        public override void Shutdown() => owner.Shutdown();
        public override Task Termination => owner.Termination;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => owner.ShutdownGracefullyAsync(quietPeriod, timeout);
        public override bool AwaitTermination(TimeSpan timeout) => owner.AwaitTermination(timeout);
    }

    [Fact]
    public void NativeAdapterRejectsMissingConfigurationAndCommandsBeforeDispatch()
    {
        Assert.Equal("action", Assert.Throws<ArgumentNullException>(() => new AnonymousExecutor(null)).ParamName);
        int calls = 0;
        IExecutor executor = new AnonymousExecutor(_ => calls++);
        Assert.Equal("command", Assert.Throws<ArgumentNullException>(() => executor.Execute(null)).ParamName);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NativeVirtualHookPreservesCancelableSubmissionQueueIdentity()
    {
        var owner = new UnorderedThreadPoolEventExecutor(1);
        var executor = new ForwardingExecutor(owner);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        try
        {
            executor.Execute(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            int runs = 0;
            Task task = executor.SubmitAsync(() => runs++, cancellation.Token);
            Assert.Equal(2, executor.Executions);
            Assert.Equal(1, owner.PendingTaskCount);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(0, owner.PendingTaskCount);
            Assert.Equal(0, runs);
        }
        finally
        {
            release.Set();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ASubclassComposedMulticastActionRunsItsPrefixAndTheSubmittedInvocation()
    {
        int prefixes = 0;
        var executor = new HookLoop { Prefix = () => prefixes++ };
        try
        {
            int result = await executor.SubmitAsync(() => 42, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(42, result);
            Assert.Equal(1, prefixes);
            Assert.Equal(1, executor.Executions);
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeLazyAndOrdinaryActionsVisitTheirActualSubclassHook(bool lazy)
    {
        var executor = new HookLoop();
        var completion = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Action command = () => completion.SetResult(Thread.CurrentThread);
            if (lazy) executor.LazyExecute(command);
            else executor.Execute(command);
            Thread worker = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(executor.InEventLoop(worker));
            Assert.Equal(lazy ? 0 : 1, executor.Executions);
            Assert.Equal(lazy ? 1 : 0, executor.LazyExecutions);
            Assert.Equal(lazy ? 0 : 1, executor.WakeChecks);
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }
}
