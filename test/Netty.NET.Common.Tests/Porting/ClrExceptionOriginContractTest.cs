using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class ClrExceptionOriginContractTest
{
    [Fact]
    public void FailedThreadLocalInitializationPreservesOriginAndRemainsRetryable()
    {
        var original = new InvalidOperationException("initializer failure");
        var local = new FailingLocal(original, failRemoval: false);
        try
        {
            Assert.Same(original, Assert.Throws<InvalidOperationException>(() => local.Get()));
            Assert.Contains(nameof(ThrowAtOrigin), original.StackTrace);
            Assert.False(local.IsSet());
            Assert.Equal("ready", local.Get());
            Assert.True(local.IsSet());
            Assert.Equal(2, local.Initializations);
        }
        finally { local.Remove(); }
    }

    [Fact]
    public void FailedThreadLocalRemovalPreservesOriginAfterClearingItsBinding()
    {
        var original = new InvalidOperationException("removal failure");
        var local = new FailingLocal(original, failRemoval: true);
        Assert.Equal("ready", local.Get());
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => local.Remove()));
        Assert.Contains(nameof(ThrowAtOrigin), original.StackTrace);
        Assert.False(local.IsSet());
        local.Remove();
        Assert.Equal(1, local.Removals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FatalExecutorStartupRetainsTheOriginalFrameAndFaultsTermination(bool stackOverflow)
    {
        // Synthetic exceptions exercise propagation without exhausting the host.
        Exception original = stackOverflow ? new StackOverflowException("startup") : new OutOfMemoryException("startup");
        var executor = new DefaultEventExecutor(new FailingExecutor(original).Execute);
        Exception observed = Record.Exception(() =>
        {
            _ = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        });
        Assert.Same(original, observed);
        Assert.Contains(nameof(ThrowAtOrigin), observed.StackTrace);
        Assert.True(executor.IsTerminated());
        Exception termination = await Record.ExceptionAsync(async () => await executor.Termination);
        Assert.Same(original, termination);
        Assert.Contains(nameof(ThrowAtOrigin), termination.StackTrace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ThrowAtOrigin(Exception original) => throw original;

    private sealed class FailingLocal(Exception original, bool failRemoval) : FastThreadLocal<string>
    {
        internal int Initializations;
        internal int Removals;
        protected override string InitialValue()
        {
            Initializations++;
            return !failRemoval && Initializations == 1 ? ThrowAtOrigin(original) : "ready";
        }
        protected override void OnRemoval(string value)
        {
            Removals++;
            if (failRemoval) ThrowAtOrigin(original);
        }
    }

    private sealed class FailingExecutor(Exception original)
    {
        public void Execute(Action command) => ThrowAtOrigin(original);
    }
}
