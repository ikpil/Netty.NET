using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class ExecutorSubmissionContractTest
{
    private sealed class ManualExecutor : AbstractEventExecutor
    {
        internal readonly BlockingCollection<IRunnable> tasks = new();
        private bool running;
        public override bool InEventLoop(Thread thread) => running && thread == Thread.CurrentThread;
        public override void Execute(IRunnable task) => tasks.Add(task);
        internal IRunnable Take()
        {
            Assert.True(tasks.TryTake(out var task, TimeSpan.FromSeconds(5)));
            return task;
        }
        internal void Run(IRunnable task)
        {
            running = true;
            try { task.Run(); }
            finally { running = false; }
        }
        public override void Shutdown() { }
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        public override bool IsShuttingDown() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
    }
    [Fact]
    public void NativeSubmissionReturnsItsResultAndNotifiesOnTheSelectedExecutor()
    {
        var executor = new ManualExecutor();
        var result = new object();
        Task<object> operation = executor.SubmitAsync(() => result);
        using var observation = new ExecutorCompletion(executor, operation);
        bool onLoop = false;
        Task observed = null;
        using var registration = observation.Register(task => { onLoop = executor.InEventLoop(); observed = task; });
        Assert.False(operation.IsCompleted);
        Assert.IsAssignableFrom<INativeSubmission>(executor.tasks.First());
        executor.Run(executor.Take());
        Assert.Same(result, operation.GetAwaiter().GetResult());
        while (!registration.NotificationCompleted.IsCompleted) executor.Run(executor.Take());
        registration.NotificationCompleted.GetAwaiter().GetResult();
        Assert.True(onLoop);
        Assert.Same(operation, observed);
    }

    [Fact]
    public void NativeFunctionsRetainExplicitResultAndActionsHaveNoInventedVoidValue()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        var result = new object();
        var supplied = executor.SubmitAsync(() => { ++calls; return result; });
        Task empty = executor.SubmitAsync(() => { ++calls; });
        executor.Run(executor.Take());
        executor.Run(executor.Take());
        Assert.Same(result, supplied.GetAwaiter().GetResult());
        empty.GetAwaiter().GetResult();
        Assert.True(empty.IsCompletedSuccessfully);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CancellationBeforeExecutionPreventsUserCodeAndCompletesImmediately()
    {
        var executor = new ManualExecutor();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        var operation = executor.SubmitAsync(() => ++calls, cancellation.Token);
        cancellation.Cancel();
        Assert.True(operation.IsCanceled);
        executor.Run(executor.Take());
        Assert.Equal(0, calls);
        var error = Assert.ThrowsAny<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void ClaimedNativeSubmissionRetainsFailureAfterItsTokenIsCanceled()
    {
        var executor = new ManualExecutor();
        using var cancellation = new CancellationTokenSource();
        var cause = new InvalidOperationException("original");
        Task<int> operation = executor.SubmitAsync(int () =>
        {
            cancellation.Cancel();
            throw cause;
        }, cancellation.Token);
        executor.Run(executor.Take());
        Assert.True(operation.IsFaulted);
        Assert.False(operation.IsCanceled);
        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => operation.GetAwaiter().GetResult()));
        Assert.Same(cause, Assert.Throws<AggregateException>(() => operation.Result).InnerException);
    }

    [Fact]
    public void SubmissionRejectsNullBeforeQueuing()
    {
        var executor = new ManualExecutor();
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Action)null));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<int>)null));
        Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<CancellationToken, int>)null));
        Assert.Equal(0, executor.tasks.Count);
    }

    [Fact]
    public async Task WhenAllRetainsInputOrderAndEachNativeFailure()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var cause = new InvalidOperationException("second");
        Task<int>[] operations = { executor.SubmitAsync(() => 1),
            executor.SubmitAsync(int () => throw cause), executor.SubmitAsync(() => 3) };
        Task<int[]> all = Task.WhenAll(operations);
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => all));
        Assert.All(operations, task => Assert.True(task.IsCompleted));
        Assert.Equal(1, await operations[0]);
        Assert.Same(cause, operations[1].Exception.InnerException);
        Assert.Equal(3, await operations[2]);
        Assert.Equal(new[] { 1, 3 }, await Task.WhenAll(operations[0], operations[2]));
    }

    [Fact]
    public void PreCanceledBatchDoesNotQueueAnyFunction()
    {
        var executor = new ManualExecutor();
        using var owner = new CancellationTokenSource();
        owner.Cancel();
        Task<int>[] operations = { executor.SubmitAsync(() => 1, owner.Token),
            executor.SubmitAsync(() => 2, owner.Token) };
        Assert.All(operations, task => Assert.True(task.IsCanceled));
        Assert.Equal(0, executor.tasks.Count);
    }

    [Fact]
    public void InvalidNegativeWaitTimeoutDoesNotCancelAcceptedSubmissions()
    {
        var executor = new ManualExecutor();
        Task<int> operation = executor.SubmitAsync(() => 42);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = Task.WhenAll(operation).WaitAsync(TimeSpan.FromMilliseconds(-2));
        });
        Assert.False(operation.IsCompleted);
        executor.Run(executor.Take());
        Assert.Equal(42, operation.GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task SubmillisecondWaitTimeoutDoesNotCancelAcceptedWork(int ticks)
    {
        var executor = new ManualExecutor();
        Task<int> operation = executor.SubmitAsync(() => 42);
        await Assert.ThrowsAsync<TimeoutException>(() => Task.WhenAll(operation).WaitAsync(TimeSpan.FromTicks(ticks)));
        Assert.False(operation.IsCompleted);
        executor.Run(executor.Take());
        Assert.Equal(42, await operation);
    }

    [Fact]
    public async Task BatchTimeoutOwnerCancelsQueuedTasksBeforeTheyExecute()
    {
        var executor = new ManualExecutor();
        using var owner = new CancellationTokenSource();
        int calls = 0;
        Task<int>[] operations = { executor.SubmitAsync(() => ++calls, owner.Token),
            executor.SubmitAsync(() => ++calls, owner.Token) };
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Task.WhenAll(operations).WaitAsync(TimeSpan.FromMilliseconds(10)));
            Assert.All(operations, task => Assert.False(task.IsCompleted));
        }
        finally { owner.Cancel(); }
        Assert.All(operations, task => Assert.True(task.IsCanceled));
        while (executor.tasks.TryTake(out var task)) executor.Run(task);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CallerCancelsPartialAdmissionAfterAnInvalidNativeFunction()
    {
        var executor = new ManualExecutor();
        using var owner = new CancellationTokenSource();
        Task<int> first = executor.SubmitAsync(int () => throw new InvalidOperationException("Canceled invocation ran"), owner.Token);
        try { Assert.Throws<ArgumentNullException>(() => executor.SubmitAsync((Func<int>)null, owner.Token)); }
        finally { owner.Cancel(); }
        Assert.True(first.IsCanceled);
        executor.Run(executor.Take());
    }

    [Fact]
    public async Task FirstSuccessConsumerObservesFailuresAndCancelsQueuedLosers()
    {
        var executor = new ManualExecutor();
        using var owner = new CancellationTokenSource();
        var cause = new InvalidOperationException("first failure");
        int lateCalls = 0;
        Task<int>[] operations = { executor.SubmitAsync(int () => throw cause, owner.Token),
            executor.SubmitAsync(() => 42, owner.Token), executor.SubmitAsync(() => ++lateCalls, owner.Token) };
        async Task<int> FindSuccessAsync()
        {
            var remaining = operations.ToList();
            var failures = new List<Exception>();
            try
            {
                while (remaining.Count != 0)
                {
                    Task<int> candidate = await Task.WhenAny(remaining).ConfigureAwait(false);
                    remaining.Remove(candidate);
                    try { return await candidate.ConfigureAwait(false); }
                    catch (Exception failure) { failures.Add(failure); }
                }
                throw new AggregateException(failures);
            }
            finally { owner.Cancel(); }
        }
        Task<int> result = FindSuccessAsync();
        executor.Run(executor.Take());
        executor.Run(executor.Take());
        Assert.Equal(42, await result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(cause, operations[0].Exception.InnerException);
        Assert.True(operations[2].IsCanceled);
        executor.Run(executor.Take());
        Assert.Equal(0, lateCalls);
    }

    [Fact]
    public async Task WhenAnyReturnsFirstCompletionIncludingItsNativeFailure()
    {
        var executor = new ManualExecutor();
        var cause = new InvalidOperationException("first");
        Task<int>[] operations = { executor.SubmitAsync(int () => throw cause), executor.SubmitAsync(() => 42) };
        Task<Task<int>> winner = Task.WhenAny(operations);
        executor.Run(executor.Take());
        Assert.Same(operations[0], await winner);
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => operations[0]));
        Assert.False(operations[1].IsCompleted);
        executor.Run(executor.Take());
        Assert.Equal(42, await operations[1]);
    }

    [Fact]
    public async Task WhenAnyTimeoutOwnerExplicitlyCancelsUnstartedWork()
    {
        var executor = new ManualExecutor();
        using var owner = new CancellationTokenSource();
        int calls = 0;
        Task<int> operation = executor.SubmitAsync(() => ++calls, owner.Token);
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Task.WhenAny(operation).WaitAsync(TimeSpan.FromMilliseconds(10)));
            Assert.False(operation.IsCompleted);
        }
        finally { owner.Cancel(); }
        executor.Run(executor.Take());
        Assert.True(operation.IsCanceled);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task EmptyNativeBatchQueuesNothingAndCannotSelectAFirstCompletion()
    {
        var executor = new ManualExecutor();
        Task<int>[] operations = Array.Empty<Func<int>>().Select(function => executor.SubmitAsync(function)).ToArray();
        Assert.Empty(await Task.WhenAll(operations));
        Assert.Throws<ArgumentException>(() => Task.WhenAny(operations));
        Assert.Equal(0, executor.tasks.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelingOnlyTheBatchWaitLeavesAcceptedWorkRunnable(bool any)
    {
        var executor = new ManualExecutor();
        using var observer = new CancellationTokenSource();
        int calls = 0;
        Task<int>[] operations = { executor.SubmitAsync(() => ++calls), executor.SubmitAsync(() => ++calls) };
        Task aggregate = any ? Task.WhenAny(operations) : Task.WhenAll(operations);
        Task wait = aggregate.WaitAsync(observer.Token);
        observer.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.All(operations, task => Assert.False(task.IsCompleted));
        executor.Run(executor.Take());
        executor.Run(executor.Take());
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(operations));
        Assert.Equal(2, calls);
    }
}
