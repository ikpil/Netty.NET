using System;
using System.Collections.Generic;
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
    [Fact]
    public void ImmediateNativeMulticastFailureKeepsReentrantFifoAndLaterExecution()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var order = new List<int>();
        Action first = () =>
        {
            order.Add(3);
            executor.Execute(() => order.Add(5));
            throw new InvalidOperationException("multicast");
        };
        Action skipped = () => order.Add(-1);
        executor.Execute(() =>
        {
            order.Add(1);
            executor.Execute(first + skipped);
            executor.Execute(() => order.Add(4));
            order.Add(2);
            throw new InvalidOperationException("outer");
        });
        executor.Execute(() => order.Add(6));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, order);
        Assert.Equal("command", Assert.Throws<ArgumentNullException>(() => executor.Execute((Action)null)).ParamName);
    }

    [Fact]
    public void ImmediateNativeReentryBoundsTheStackAndUsesTheCallersThread()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        Thread caller = Thread.CurrentThread;
        int runs = 0, depth = 0, maximumDepth = 0;
        bool callerOnly = true;
        Action next = null;
        next = () =>
        {
            depth++;
            maximumDepth = Math.Max(maximumDepth, depth);
            callerOnly &= ReferenceEquals(caller, Thread.CurrentThread);
            if (++runs < 100000) executor.Execute(next);
            depth--;
        };
        executor.Execute(next);
        Assert.Equal(100000, runs);
        Assert.Equal(1, maximumDepth);
        Assert.Equal(0, depth);
        Assert.True(callerOnly);
    }

    [Fact]
    public async Task ImmediateRawActionsUseLiveContextWhileNativeSubmissionsCaptureAndCancel()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var ambient = new AsyncLocal<string>();
        using var cancellation = new CancellationTokenSource();
        string rawValue = null, restoredValue = null;
        int canceledRuns = 0;
        Task<string> captured = null;
        Task canceled = null;
        try
        {
            executor.Execute(() =>
            {
                ambient.Value = "enqueue";
                executor.Execute(() => { rawValue = ambient.Value; ambient.Value = "raw"; });
                captured = executor.SubmitAsync(() =>
                {
                    string value = ambient.Value;
                    ambient.Value = "submission";
                    return value;
                }, TestContext.Current.CancellationToken);
                canceled = executor.SubmitAsync(() => canceledRuns++, cancellation.Token);
                cancellation.Cancel();
                executor.Execute(() => restoredValue = ambient.Value);
                ambient.Value = "drain";
            });
            Assert.Equal("drain", rawValue);
            Assert.True(captured.IsCompletedSuccessfully);
            Assert.Equal("enqueue", await captured);
            Assert.True(canceled.IsCanceled);
            Assert.Equal(0, canceledRuns);
            Assert.Equal("raw", restoredValue);
            Assert.Equal("raw", ambient.Value);
        }
        finally { ambient.Value = null; }
    }

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
        protected override bool WakesUpForTask(Action task)
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
        internal Func<Action, Action> Transform = static task => task;
        public override void Execute(Action task) { Executions++; owner.Execute(Transform(task)); }
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UnorderedCancellationOwnsOnlyTheExactIssuedSubmissionCallback(int transform)
    {
        var owner = new UnorderedThreadPoolEventExecutor(1);
        var executor = new ForwardingExecutor(owner);
        int prefixes = 0, calls = 0;
        Action prefix = () => prefixes++;
        executor.Transform = transform switch
        {
            0 => static task => task,
            1 => static task => (Action)task.Clone(),
            _ => task => prefix + task
        };
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        try
        {
            owner.Execute(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Task result = executor.SubmitAsync(() => calls++, cancellation.Token);
            Assert.Equal(1, owner.PendingTaskCount);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
            Assert.Equal(transform == 0 ? 0 : 1, owner.PendingTaskCount);
            release.Set();
            await owner.SubmitAsync(static () => { }, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, calls);
            Assert.Equal(transform == 2 ? 1 : 0, prefixes);
        }
        finally
        {
            release.Set();
            await owner.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnorderedRawRejectionClaimsOneInvocationAndPreservesPolicyFailure(bool throwPolicy)
    {
        Action rejected = null;
        var policyFailure = new InvalidOperationException("policy failure");
        Thread policyThread = null;
        var owner = new UnorderedThreadPoolEventExecutor(1, (task, _) =>
        {
            rejected = task;
            policyThread = Thread.CurrentThread;
            task();
            task();
            if (throwPolicy) throw policyFailure;
        });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0, tailCalls = 0, prefixes = 0;
        try
        {
            owner.Execute(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            owner.Shutdown();
            Action callback = () => calls++;
            callback += () => throw new InvalidOperationException("raw multicast failure");
            callback += () => tailCalls++;
            if (throwPolicy) Assert.Same(policyFailure, Assert.Throws<InvalidOperationException>(() => owner.Execute(callback)));
            else owner.Execute(callback);
            Assert.Same(Thread.CurrentThread, policyThread);
            Assert.NotNull(rejected);
            Action replay = (Action)rejected.Clone();
            Action composed = (() => prefixes++) + replay;
            composed();
            Assert.Equal(1, calls);
            Assert.Equal(0, tailCalls);
            Assert.Equal(1, prefixes);
            Assert.Equal(0, owner.PendingTaskCount);
            Task stopping = owner.StopAsync();
            rejected();
            release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            replay();
            Assert.Equal(1, calls);
        }
        finally
        {
            release.Set();
            await owner.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
