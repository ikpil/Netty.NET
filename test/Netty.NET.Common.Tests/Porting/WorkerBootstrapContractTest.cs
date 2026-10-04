using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class WorkerBootstrapContractTest
{
    [Fact]
    public void NullEntryIsRejectedBeforeCallingTheConfiguredFactory()
    {
        int creations = 0;
        var executor = new ThreadPerTaskExecutor(new AnonymousThreadFactory(_ => { creations++; return null; }));
        Assert.Equal("command", Assert.Throws<ArgumentNullException>(() => executor.Execute(null)).ParamName);
        Assert.Equal(0, creations);
    }

    [Fact]
    public void AFactoryReturningNoNativeThreadFailsSynchronouslyAndClearly()
    {
        var executor = new ThreadPerTaskExecutor(new AnonymousThreadFactory(_ => null));
        Assert.Contains("thread", Assert.Throws<InvalidOperationException>(() => executor.Execute(() => { })).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullFactoryUsesTheNativePublicParameterName()
    {
        Assert.Equal("threadFactory", Assert.Throws<ArgumentNullException>(() => new ThreadPerTaskExecutor(null)).ParamName);
    }

    [Fact]
    public void EachEntryStartsAFreshPhysicalWorkerWithoutRunningOnTheCaller()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        Thread[] threads = new Thread[2];
        int[] ids = new int[2];
        int creations = 0;
        var executor = new ThreadPerTaskExecutor(new AnonymousThreadFactory(command =>
        {
            var thread = new Thread(() => command()) { IsBackground = true };
            threads[creations++] = thread;
            return thread;
        }));
        int caller = Environment.CurrentManagedThreadId;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                executor.Execute(() =>
                {
                    ids[index] = Environment.CurrentManagedThreadId;
                    entered.Signal();
                    release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                });
            }
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(2, creations);
            Assert.NotEqual(caller, ids[0]);
            Assert.NotEqual(caller, ids[1]);
            Assert.NotEqual(ids[0], ids[1]);
            Assert.NotSame(threads[0], threads[1]);
        }
        finally
        {
            release.Set();
            foreach (Thread thread in threads)
                if (thread != null) Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void FactoryFailureReachesTheCallerWithoutReplacingTheException()
    {
        var failure = new InvalidOperationException("creation failed");
        var executor = new ThreadPerTaskExecutor(new AnonymousThreadFactory(_ => throw failure));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => executor.Execute(() => { })));
    }

    [Fact]
    public void APreviouslyStartedThreadCannotPretendToAcceptAnotherEntry()
    {
        var thread = new Thread(() => { }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        int calls = 0;
        var executor = new ThreadPerTaskExecutor(new AnonymousThreadFactory(_ => thread));
        Assert.Throws<ThreadStateException>(() => executor.Execute(() => calls++));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredWorkerMappingRestoresThePreviousBindingEvenWhenEntryFails(bool fail)
    {
        IEventExecutor previous = ImmediateEventExecutor.INSTANCE;
        IEventExecutor mapped = GlobalEventExecutor.INSTANCE;
        IEventExecutor callerBinding = ThreadExecutorMap.CurrentExecutor();
        Action entry = null;
        var failure = new InvalidOperationException("entry failed");
        Action<Action> dispatch = ThreadExecutorMap.Apply(command => entry = command, mapped);
        dispatch(() =>
        {
            Assert.Same(mapped, ThreadExecutorMap.CurrentExecutor());
            if (fail) throw failure;
        });
        Assert.Same(callerBinding, ThreadExecutorMap.CurrentExecutor());
        Exception workerFailure = null;
        var worker = new FastThreadLocalThread(() =>
        {
            try
            {
                ThreadExecutorMap.SetCurrentExecutor(previous);
                if (fail) Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => entry()));
                else entry();
                Assert.Same(previous, ThreadExecutorMap.CurrentExecutor());
            }
            catch (Exception error) { workerFailure = error; }
        });
        worker.Thread.IsBackground = true;
        worker.Thread.Start();
        Assert.True(worker.Thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(workerFailure);
        Assert.Same(callerBinding, ThreadExecutorMap.CurrentExecutor());
    }

    [Fact]
    public void NativeMappingRejectsMissingDependenciesBeforeDispatch()
    {
        Assert.Equal("executor", Assert.Throws<ArgumentNullException>(() =>
            ThreadExecutorMap.Apply((Action<Action>)null, ImmediateEventExecutor.INSTANCE)).ParamName);
        Assert.Equal("eventExecutor", Assert.Throws<ArgumentNullException>(() =>
            ThreadExecutorMap.Apply((Action<Action>)(_ => { }), null)).ParamName);
        int calls = 0;
        var dispatch = ThreadExecutorMap.Apply(_ => calls++, ImmediateEventExecutor.INSTANCE);
        Assert.Equal("command", Assert.Throws<ArgumentNullException>(() => dispatch(null)).ParamName);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NativeBackendStartsOneMappedWorkerForSerialSubmissionsAndTermination()
    {
        int starts = 0;
        var starter = new ThreadPerTaskExecutor(new DefaultThreadFactory("native-bootstrap", true));
        var executor = new DefaultEventExecutor(command => { Interlocked.Increment(ref starts); starter.Execute(command); });
        Thread worker = null;
        int counter = 0;
        try
        {
            Task<int>[] work = new Task<int>[12];
            for (int i = 0; i < work.Length; i++)
                work[i] = executor.SubmitAsync(() =>
                {
                    Assert.Same(executor, ThreadExecutorMap.CurrentExecutor());
                    Assert.True(executor.InEventLoop());
                    if (worker == null) worker = Thread.CurrentThread;
                    Assert.Same(worker, Thread.CurrentThread);
                    return ++counter;
                }, TestContext.Current.CancellationToken);
            int[] results = await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            for (int i = 0; i < results.Length; i++) Assert.Equal(i + 1, results[i]);
            Assert.Equal(1, starts);
            Assert.NotSame(Thread.CurrentThread, worker);
            Assert.False(executor.Termination.IsCompleted);
            Assert.Same(executor.Termination, executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
    }

    private sealed class NativeGroup(Action<Action> start) : MultithreadEventExecutorGroup(2, start)
    {
        protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
            => new DefaultEventExecutor(this, executor);
    }

    [Fact]
    public async Task NativeGroupChildHookDispatchesEachDedicatedWorkerAndAggregatesTermination()
    {
        int starts = 0;
        var starter = new ThreadPerTaskExecutor(new DefaultThreadFactory("native-group-bootstrap", true));
        var group = new NativeGroup(command => { Interlocked.Increment(ref starts); starter.Execute(command); });
        try
        {
            IEventExecutor first = group.Next();
            IEventExecutor second = group.Next();
            Thread a = await first.SubmitAsync(() =>
            {
                Assert.Same(first, ThreadExecutorMap.CurrentExecutor());
                return Thread.CurrentThread;
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Thread b = await second.SubmitAsync(() =>
            {
                Assert.Same(second, ThreadExecutorMap.CurrentExecutor());
                return Thread.CurrentThread;
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.NotSame(a, b);
            Assert.Equal(2, starts);
            Assert.Same(group.Termination, group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
        }
        finally
        {
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }
}
