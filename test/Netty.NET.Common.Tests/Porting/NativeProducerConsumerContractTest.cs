using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NativeProducerConsumerContractTest
{
    // The producer creates its source; consumers receive only Task. This models
    // SimpleNameResolver's protected immediate/deferred provider boundary without
    // implementing the resolver module or passing an executor-owned Promise.
    private sealed class ResolverProducer
    {
        private TaskCompletionSource<object> _source;
        internal Task<object> Resolve(Action<TaskCompletionSource<object>> start)
        {
            _source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            try { start(_source); }
            catch (Exception error) { _source.TrySetException(error); }
            return _source.Task;
        }
        internal void Complete(object value) => _source.SetResult(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ProviderOwnedSourceSupportsImmediateDeferredAndThrownOutcomes(int outcome)
    {
        var executor = new DefaultEventExecutor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = outcome == 3
            ? (Exception)new OperationCanceledException("provider failure", cancellation.Token)
            : new InvalidOperationException("provider failure");
        var value = new object();
        var provider = new ResolverProducer();
        Task<object> result = provider.Resolve(source =>
        {
            if (outcome == 0) source.SetResult(value);
            else if (outcome >= 2) throw error;
        });
        try
        {
            using var observation = new ExecutorCompletion(executor, result);
            Task observed = null;
            bool onLoop = false;
            using var registration = observation.Register(task =>
            {
                observed = task;
                onLoop = executor.InEventLoop();
            });
            if (outcome == 1)
            {
                using var waiterCancellation = new CancellationTokenSource();
                Task<object> waiting = result.WaitAsync(waiterCancellation.Token);
                waiterCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
                Assert.False(result.IsCompleted);
                provider.Complete(value);
            }
            await registration.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(result, observed);
            Assert.True(onLoop);
            if (outcome < 2) Assert.Same(value, await result);
            else
            {
                Exception caught = null;
                try { await result; }
                catch (Exception failure) { caught = failure; }
                Assert.Same(error, caught);
                Assert.True(result.IsFaulted);
                Assert.False(result.IsCanceled);
            }
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }

    [Fact]
    public async Task PoolShapedReleaseAdmitsQueuedWorkBeforeCompletingTheCallerTask()
    {
        var executor = new DefaultEventExecutor();
        var backend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        int acquired = 1, pending = 1;
        using var backendObservation = new ExecutorCompletion(executor, backend.Task);
        using var callerObservation = new ExecutorCompletion(executor, caller.Task);
        bool releaseOnLoop = false, callerOnLoop = false;
        int callerAcquired = -1, callerPending = -1;
        using var released = backendObservation.Register(_ =>
        {
            releaseOnLoop = executor.InEventLoop();
            --acquired;
            events.Add("release slot");
            --pending;
            ++acquired;
            events.Add("admit pending acquire");
            caller.SetResult();
        });
        using var completed = callerObservation.Register(_ =>
        {
            callerOnLoop = executor.InEventLoop();
            callerAcquired = acquired;
            callerPending = pending;
            events.Add("caller completed");
        });
        try
        {
            backend.SetResult();
            await Task.WhenAll(released.NotificationCompleted, completed.NotificationCompleted)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await caller.Task;
            Assert.True(releaseOnLoop);
            Assert.True(callerOnLoop);
            Assert.Equal(1, callerAcquired);
            Assert.Equal(0, callerPending);
            Assert.Equal(new[] { "release slot", "admit pending acquire", "caller completed" }, events);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }
}
