using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class TaskPromiseStateContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CompletionObserversSeeTheSameTerminalState(int outcome)
    {
        // A reader observes completion while the producer is still inside its
        // completion call, rather than only comparing states after joining it.
        using var rendezvous = new Barrier(2);
        TaskCompletionSource<object> source = null;
        TaskCompletionSource<Task> notified = null;
        object value = new();
        var error = new InvalidOperationException("original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        const int iterations = 2000;
        void Meet()
        {
            Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(10)));
        }
        Task reader = System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                Meet();
                Task<object> result = source.Task;
                Assert.True(SpinWait.SpinUntil(() => result.IsCompleted, TimeSpan.FromSeconds(10)));
                Task callbackResult = notified.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                Assert.Same(result, callbackResult);
                Assert.Equal(outcome == 0, result.IsCompletedSuccessfully);
                Assert.Equal(outcome == 1, result.IsFaulted);
                Assert.Equal(outcome == 2, result.IsCanceled);
                if (outcome == 0) Assert.Same(value, result.GetAwaiter().GetResult());
                else if (outcome == 1)
                    Assert.Same(error, Assert.Throws<InvalidOperationException>(() => result.GetAwaiter().GetResult()));
                else
                    Assert.Equal(cancellation.Token,
                        Assert.ThrowsAny<OperationCanceledException>(() => result.GetAwaiter().GetResult()).CancellationToken);
                Meet();
            }
        });
        Task producer = System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                notified = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                var notification = notified;
                using var observer = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
                using var registration = observer.Register(result => notification.SetResult(result));
                Meet();
                if (outcome == 0) source.SetResult(value);
                else if (outcome == 1) source.SetException(error);
                else source.SetCanceled(cancellation.Token);
                Meet();
            }
        });
        await System.Threading.Tasks.Task.WhenAll(reader, producer).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CancellationAndFaultPublishDistinctTerminalStates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = new OperationCanceledException("original", cancellation.Token);
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(source.TrySetCanceled(cancellation.Token));
        Assert.True(source.Task.IsCanceled);
        Assert.Null(source.Task.Exception);
        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.Task);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        var failure = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        failure.SetException(error);
        Assert.True(failure.Task.IsFaulted);
        Assert.False(failure.Task.IsCanceled);
        Assert.Same(error, await Assert.ThrowsAsync<OperationCanceledException>(async () => await failure.Task));
    }

    [Fact]
    public async Task AnAggregateFailureRemainsTheOriginalSingleFailure()
    {
        var error = new AggregateException(new InvalidOperationException("one"), new ArgumentException("two"));
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetException(error);
        Assert.Same(error, source.Task.Exception.InnerException);
        AggregateException actual = await Assert.ThrowsAsync<AggregateException>(async () => await source.Task);
        Assert.Same(error, actual);
    }
}
