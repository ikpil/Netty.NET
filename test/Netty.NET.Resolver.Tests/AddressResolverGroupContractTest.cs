using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver.Tests;

public class AddressResolverGroupContractTest
{
    private static readonly AsyncLocal<string> RegistrationContext = new();

    [Fact]
    public void GroupCreatesExactlyOneResolverForEachExecutorUnderContention()
    {
        var first = NewExecutor();
        var second = NewExecutor();
        var firstExecutor = first.Object;
        using var group = new CountingGroup();
        var results = new ConcurrentBag<IAddressResolver<EndPoint>>();
        Parallel.For(0, 100, _ => results.Add(group.GetResolver(firstExecutor)));
        var firstResolver = group.GetResolver(firstExecutor);
        Assert.All(results, resolver => Assert.Same(firstResolver, resolver));
        Assert.NotSame(firstResolver, group.GetResolver(second.Object));
        Assert.Equal(2, group.Created.Count);
    }

    [Fact]
    public async Task ExecutorTerminationClosesItsResolverAndRejectsNewLookups()
    {
        var termination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = NewExecutor(termination.Task);
        using var group = new CountingGroup();
        var resolver = (CountingResolver)group.GetResolver(executor.Object);
        executor.Setup(e => e.IsShuttingDown()).Returns(true);
        termination.SetResult();
        await resolver.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, resolver.CloseCount);
        Assert.Throws<InvalidOperationException>(() => group.GetResolver(executor.Object));
        group.Dispose();
        Assert.Equal(1, resolver.CloseCount);
    }

    [Fact]
    public async Task TerminationCleanupDoesNotCaptureRegisteringCallersExecutionContext()
    {
        var termination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = NewExecutor(termination.Task);
        using var group = new CountingGroup();
        RegistrationContext.Value = "caller";
        CountingResolver resolver;
        try
        { resolver = (CountingResolver)group.GetResolver(executor.Object); }
        finally { RegistrationContext.Value = null; }
        termination.SetResult();
        await resolver.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Null(resolver.CloseContext);
    }

    [Fact]
    public async Task GroupCloseDetachesOldTerminationRegistrationAndAllowsReuseLikeOriginal()
    {
        var termination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = NewExecutor(termination.Task);
        var group = new CountingGroup();
        var first = (CountingResolver)group.GetResolver(executor.Object);
        group.Dispose();
        group.Dispose();
        Assert.Equal(1, first.CloseCount);
        var second = (CountingResolver)group.GetResolver(executor.Object);
        Assert.NotSame(first, second);
        termination.SetResult();
        await second.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, second.CloseCount);
        group.Dispose();
        Assert.Equal(1, second.CloseCount);
    }

    [Fact]
    public async Task TerminationAndGroupCloseRaceClosesEachResolverOnce()
    {
        for (int i = 0; i < 50; i++)
        {
            var termination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = NewExecutor(termination.Task);
            var group = new CountingGroup();
            var resolver = (CountingResolver)group.GetResolver(executor.Object);
            await Task.WhenAll(Task.Run(group.Dispose, TestContext.Current.CancellationToken),
                Task.Run(() => termination.SetResult(), TestContext.Current.CancellationToken));
            await resolver.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(1, resolver.CloseCount);
        }
    }

    [Fact]
    public void FailureToCloseOneResolverDoesNotPreventOtherCleanup()
    {
        var group = new CountingGroup { FailClose = true };
        group.GetResolver(NewExecutor().Object);
        group.GetResolver(NewExecutor().Object);
        group.Dispose();
        Assert.Equal(2, group.Created.Count);
        Assert.All(group.Created, resolver => Assert.Equal(1, resolver.CloseCount));
    }

    [Fact]
    public void GroupValidatesExecutorAndPreservesFactoryFailure()
    {
        using var group = new CountingGroup { FactoryFailure = new Exception("factory") };
        Assert.Throws<ArgumentNullException>(() => group.GetResolver(null));
        var executor = NewExecutor();
        executor.Setup(e => e.IsShuttingDown()).Returns(true);
        Assert.Throws<InvalidOperationException>(() => group.GetResolver(executor.Object));
        Assert.Empty(group.Created);
        executor.Setup(e => e.IsShuttingDown()).Returns(false);
        Assert.Same(group.FactoryFailure, Assert.Throws<InvalidOperationException>(() => group.GetResolver(executor.Object)).InnerException);
        Assert.Empty(group.Created);
        group.FactoryFailure = null;
        Assert.NotNull(group.GetResolver(executor.Object));
    }

    [Fact]
    public async Task NoopGroupUsesIndependentResolversForExecutors()
    {
        var first = new DefaultEventExecutor();
        var second = new DefaultEventExecutor();
        try
        {
            var a = NoopAddressResolverGroup.INSTANCE.GetResolver(first);
            Assert.Same(a, NoopAddressResolverGroup.INSTANCE.GetResolver(first));
            Assert.NotSame(a, NoopAddressResolverGroup.INSTANCE.GetResolver(second));
            var endpoint = new DnsEndPoint("host.invalid", 80);
            Assert.Same(endpoint, await a.ResolveAsync(endpoint, TestContext.Current.CancellationToken));
        }
        finally
        {
            await Task.WhenAll(first.StopAsync(), second.StopAsync()).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static Mock<IEventExecutor> NewExecutor(Task termination = null)
    {
        var executor = new Mock<IEventExecutor>();
        executor.Setup(e => e.Termination).Returns(termination ?? new TaskCompletionSource().Task);
        return executor;
    }

    private sealed class CountingGroup : AddressResolverGroup<EndPoint>
    {
        internal readonly ConcurrentBag<CountingResolver> Created = new();
        internal Exception FactoryFailure;
        internal bool FailClose;
        protected override IAddressResolver<EndPoint> NewResolver(IEventExecutor executor)
        {
            if (FactoryFailure != null)
                throw FactoryFailure;
            var resolver = new CountingResolver(executor, FailClose);
            Created.Add(resolver);
            return resolver;
        }
    }

    private sealed class CountingResolver(IEventExecutor executor, bool failClose) : NoopAddressResolver(executor)
    {
        internal readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CloseCount;
        internal string CloseContext;
        public override void Dispose()
        {
            Interlocked.Increment(ref CloseCount);
            CloseContext = RegistrationContext.Value;
            Closed.TrySetResult();
            if (failClose)
                throw new Exception("close");
        }
    }
}
