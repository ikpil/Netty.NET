using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class CompletedResultConsumerContractTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ResolverFastPathsReturnCompletedResultsAndDispatchStateChangesOnTheLoop(bool failed, bool registerOnLoop)
    {
        var executor = new DefaultEventExecutor();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var address = new IPEndPoint(IPAddress.Loopback, 443);
        var error = new ArgumentException("Unsupported address type");
        // AbstractAddressResolver.resolve fast paths return a completed value or
        // failure without performing I/O. A failure is a result, not a factory throw.
        Task<IPEndPoint> Resolve() => failed ? Task.FromException<IPEndPoint>(error) : Task.FromResult(address);
        Task<IPEndPoint> operation = Resolve();
        Assert.True(operation.IsCompleted);
        using var observation = new ExecutorCompletion(executor, operation);
        CompletionRegistration registration = null;
        IPEndPoint selected = null;
        Exception observedError = null;
        bool onLoop = false;
        int calls = 0;
        Action<Task> consume = task =>
        {
            onLoop = executor.InEventLoop();
            ++calls;
            Assert.Same(operation, task);
            try { selected = ((Task<IPEndPoint>)task).GetAwaiter().GetResult(); }
            catch (Exception failure) { observedError = failure; }
        };
        try
        {
            if (registerOnLoop)
            {
                await executor.SubmitAsync(() =>
                {
                    registration = observation.Register(consume);
                    Assert.True(registration.NotificationCompleted.IsCompletedSuccessfully);
                    Assert.Equal(1, calls);
                }).WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                AwaitStartBlocker();
                registration = observation.Register(consume);
                Assert.False(registration.NotificationCompleted.IsCompleted);
                Assert.Equal(0, calls);
                release.Set();
            }
            await registration.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(onLoop);
            Assert.Equal(1, calls);
            if (failed) { Assert.Same(error, observedError); Assert.Null(selected); }
            else { Assert.Same(address, selected); Assert.Null(observedError); }
        }
        finally
        {
            release.Set();
            registration?.Dispose();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }

        void AwaitStartBlocker()
        {
            executor.SubmitAsync(() => { entered.Set(); release.Wait(); });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task DnsReservationsStayDistinctWhenCompletedMarkersShareTheSameTask()
    {
        var executor = new DefaultEventExecutor();
        var firstLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Task.CompletedTask and Task.FromResult may share instances. Force a
        // shared marker to test the permitted case without depending on caching.
        Task marker = Task.FromResult<object>(null);
        object firstReservation = new(), secondReservation = new();
        var queriesInProgress = new Dictionary<object, Task>(ReferenceEqualityComparer.Instance);
        using var firstCompletion = new ExecutorCompletion(executor, firstLookup.Task);
        using var secondCompletion = new ExecutorCompletion(executor, secondLookup.Task);
        CompletionRegistration first = null, second = null;
        try
        {
            await executor.SubmitAsync(() =>
            {
                // DnsResolveContext.queryUnresolvedNameServer needs membership
                // until each auxiliary lookup completes, independently of its marker.
                queriesInProgress.Add(firstReservation, marker);
                queriesInProgress.Add(secondReservation, marker);
                first = firstCompletion.Register(_ => Assert.True(queriesInProgress.Remove(firstReservation)));
                second = secondCompletion.Register(_ => Assert.True(queriesInProgress.Remove(secondReservation)));
                Assert.Equal(2, queriesInProgress.Count);
            }).WaitAsync(TimeSpan.FromSeconds(5));
            firstLookup.SetResult();
            await first.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            await executor.SubmitAsync(() =>
            {
                Assert.Single(queriesInProgress);
                Assert.True(queriesInProgress.ContainsKey(secondReservation));
                Assert.Same(marker, queriesInProgress[secondReservation]);
                Assert.False(secondLookup.Task.IsCompleted);
            });
            secondLookup.SetResult();
            await second.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            await executor.SubmitAsync(() => Assert.Empty(queriesInProgress));
        }
        finally
        {
            firstLookup.TrySetResult();
            secondLookup.TrySetResult();
            first?.Dispose();
            second?.Dispose();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SharedCompletedChannelResultKeepsOwnerAndExecutorInTheConsumer()
    {
        var firstLoop = new DefaultEventExecutor();
        var secondLoop = new DefaultEventExecutor();
        object firstChannel = new(), secondChannel = new();
        Task result = Task.CompletedTask;
        using var firstObservation = new ExecutorCompletion(firstLoop, result);
        using var secondObservation = new ExecutorCompletion(secondLoop, result);
        object observedFirst = null, observedSecond = null;
        Task observedFirstTask = null, observedSecondTask = null;
        bool firstAffinity = false, secondAffinity = false;
        try
        {
            using var first = firstObservation.Register(task =>
            {
                observedFirst = firstChannel;
                observedFirstTask = task;
                firstAffinity = firstLoop.InEventLoop() && !secondLoop.InEventLoop();
            });
            using var second = secondObservation.Register(task =>
            {
                observedSecond = secondChannel;
                observedSecondTask = task;
                secondAffinity = secondLoop.InEventLoop() && !firstLoop.InEventLoop();
            });
            await Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(firstChannel, observedFirst);
            Assert.Same(secondChannel, observedSecond);
            Assert.Same(result, observedFirstTask);
            Assert.Same(result, observedSecondTask);
            Assert.True(firstAffinity);
            Assert.True(secondAffinity);
        }
        finally
        {
            await Task.WhenAll(firstLoop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero),
                secondLoop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero)).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
