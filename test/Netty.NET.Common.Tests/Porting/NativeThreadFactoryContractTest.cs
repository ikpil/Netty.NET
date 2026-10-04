using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class NativeThreadFactoryContractTest
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Thread Thread, WeakReference Target) RunCapturedTarget()
    {
        var target = new object();
        var weak = new WeakReference(target);
        Thread thread = new DefaultThreadFactory("lifetime", true).NewThread(() => GC.KeepAlive(target));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        return (thread, weak);
    }

    [Fact]
    public void CompletedNativeThreadDoesNotRetainItsInvocationTarget()
    {
        var result = RunCapturedTarget();
        for (int attempt = 0; attempt < 10 && result.Target.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(result.Target.IsAlive);
        Assert.True(FastThreadLocalThread.WillCleanupFastThreadLocals(result.Thread));
        GC.KeepAlive(result.Thread);
    }

    private sealed class CapturingOwner(Action invocation) : FastThreadLocalThread(invocation)
    {
        internal Exception Error;
        public override void Run()
        {
            try { base.Run(); }
            catch (Exception error) { Error = error; }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CapturingOwner Owner, WeakReference Target) RunThrowingCapturedTarget()
    {
        var target = new object();
        var weak = new WeakReference(target);
        var owner = new CapturingOwner(() => { GC.KeepAlive(target); throw new InvalidOperationException("expected"); });
        owner.Thread.IsBackground = true;
        owner.Thread.Start();
        Assert.True(owner.Thread.Join(TimeSpan.FromSeconds(5)));
        return (owner, weak);
    }

    [Fact]
    public void ExceptionalTerminationReleasesCapturedTargetsWithRetainedOwnershipMetadata()
    {
        var result = RunThrowingCapturedTarget();
        for (int attempt = 0; attempt < 10 && result.Target.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.IsType<InvalidOperationException>(result.Owner.Error);
        Assert.False(result.Target.IsAlive);
        Assert.True(FastThreadLocalThread.WillCleanupFastThreadLocals(result.Owner.Thread));
        GC.KeepAlive(result.Owner);
    }

    private sealed class TrackingLocal : FastThreadLocal<object>
    {
        internal int Removed;
        protected override void OnRemoval(object value) => Removed++;
    }

    [Fact]
    public void NativeDelegateFactoryCreatesAnUnstartedNamedWorkerAndCleansItsPhysicalBindings()
    {
        InternalThreadLocalMap callerMap = InternalThreadLocalMap.GetIfSet();
        var factory = new DefaultThreadFactory(typeof(NativeThreadFactoryContractTest), true);
        var local = new TrackingLocal();
        Exception failure = null;
        int calls = 0;
        Thread thread = factory.NewThread(() =>
        {
            try
            {
                Assert.True(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
                Assert.True(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                Assert.Null(InternalThreadLocalMap.GetIfSet());
                local.Set(new object());
                calls++;
            }
            catch (Exception error) { failure = error; }
        });
        Assert.Equal(ThreadState.Unstarted | ThreadState.Background, thread.ThreadState);
        Assert.StartsWith("nativeThreadFactoryContractTest-", thread.Name);
        Assert.EndsWith("-1", thread.Name);
        Assert.Equal(ThreadPriority.Normal, thread.Priority);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Equal(1, calls);
        Assert.Equal(1, local.Removed);
        Assert.Same(callerMap, InternalThreadLocalMap.GetIfSet());
        Assert.Throws<ThreadStateException>(thread.Start);
    }

    private sealed class CapturingFactory : DefaultThreadFactory
    {
        internal Exception Error;
        internal CapturingFactory() : base("ordinary", true) { }
        protected override Thread NewThread(Action action, string name) => new(() =>
        {
            try { action(); }
            catch (Exception error) { Error = error; }
        }) { Name = name };
    }

    [Fact]
    public void CustomNativeFactoryKeepsCleanupWhenTheDelegateThrows()
    {
        var factory = new CapturingFactory();
        var local = new TrackingLocal();
        var expected = new InvalidOperationException("worker failed");
        Thread thread = factory.NewThread(() => { local.Set(new object()); throw expected; });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Same(expected, factory.Error);
        Assert.Equal(1, local.Removed);
        Assert.False(FastThreadLocalThread.WillCleanupFastThreadLocals(thread));
    }

    [Fact]
    public void MulticastDelegatesCleanBindingsEvenWhenAnEarlierCallbackThrows()
    {
        var local = new TrackingLocal();
        var expected = new InvalidOperationException("first callback failed");
        int tailCalls = 0;
        Action tail = FastThreadLocalRunnable.Wrap(() => tailCalls++);
        Assert.Same(tail, FastThreadLocalRunnable.Wrap(tail));
        Action chain = () => { local.Set(new object()); throw expected; };
        chain += tail;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(FastThreadLocalRunnable.Wrap(chain)));
        Assert.Equal(0, tailCalls);
        Assert.Equal(1, local.Removed);
        Assert.Null(InternalThreadLocalMap.GetIfSet());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeThreadStartOwnsExecutionContextFlow(bool suppressed)
    {
        var logical = new AsyncLocal<string>();
        logical.Value = "at creation";
        string observed = "unexecuted";
        Thread thread = new DefaultThreadFactory("context", true).NewThread(() => observed = logical.Value);
        logical.Value = "at start";
        try
        {
            if (suppressed)
            {
                using (ExecutionContext.SuppressFlow()) thread.Start();
            }
            else thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(suppressed ? null : "at start", observed);
            Assert.Equal("at start", logical.Value);
            Assert.False(ExecutionContext.IsFlowSuppressed());
        }
        finally { logical.Value = null; }
    }

    [Fact]
    public void NativeFactoryAndScopeRejectNullDelegatesWithoutInstallingPhysicalState()
    {
        InternalThreadLocalMap callerMap = InternalThreadLocalMap.GetIfSet();
        bool membership = FastThreadLocalThread.CurrentThreadHasFastThreadLocal();
        Assert.Equal("runnable", Assert.Throws<ArgumentNullException>(() =>
            new DefaultThreadFactory("null", true).NewThread(null)).ParamName);
        Assert.Equal("runnable", Assert.Throws<ArgumentNullException>(() =>
            Executors.DefaultThreadFactory().NewThread(null)).ParamName);
        Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => new AnonymousThreadFactory(null)).ParamName);
        Assert.Equal("runnable", Assert.Throws<ArgumentNullException>(() => FastThreadLocalThread.RunWithFastThreadLocal(null)).ParamName);
        Assert.Equal(membership, FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
        Assert.Same(callerMap, InternalThreadLocalMap.GetIfSet());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InvalidNativePrioritiesUseNativeRangeExceptions(int priority)
    {
        Assert.Equal("priority", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DefaultThreadFactory("priority", true, (ThreadPriority)priority)).ParamName);
    }
}
