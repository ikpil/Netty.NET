using System;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class FastThreadLocalTestRunnerContractTest
{
    private sealed class Local(Exception removalFailure = null) : FastThreadLocal<object>
    {
        internal int Removals;
        protected override void onRemoval(object value)
        {
            Interlocked.Increment(ref Removals);
            if (removalFailure != null) throw removalFailure;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerOwnsIndexedMapAndCleansUpBeforeFailureIsRethrown(bool fail)
    {
        var local = new Local();
        var cause = new InvalidOperationException("worker failure");
        Thread caller = Thread.CurrentThread;
        Thread worker = null;
        Action invocation = () =>
        {
            worker = Thread.CurrentThread;
            Assert.NotSame(caller, worker);
            Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
            local.set(new object());
            Assert.Equal(0, local.Removals);
            if (fail) throw cause;
        };
        if (fail)
        {
            Assert.Same(cause, Assert.Throws<InvalidOperationException>(() =>
                RunInFastThreadLocalThreadExtension.run(invocation)));
            Assert.Contains(nameof(WorkerOwnsIndexedMapAndCleansUpBeforeFailureIsRethrown), cause.StackTrace);
        }
        else RunInFastThreadLocalThreadExtension.run(invocation);
        Assert.Equal(1, local.Removals);
        Assert.False(worker.IsAlive);
        Assert.False(local.isSet());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupFailureReachesCallerWithoutReplacingAnEarlierInvocationFailure(bool failInvocation)
    {
        var cleanupFailure = new InvalidOperationException("cleanup failure");
        var invocationFailure = new InvalidOperationException("invocation failure");
        var local = new Local(cleanupFailure);
        var error = Assert.Throws<InvalidOperationException>(() => RunInFastThreadLocalThreadExtension.run(() =>
        {
            local.set(new object());
            if (failInvocation) throw invocationFailure;
        }));
        Assert.Same(failInvocation ? invocationFailure : cleanupFailure, error);
        Assert.Equal(1, local.Removals);
        Assert.False(local.isSet());
    }
}
