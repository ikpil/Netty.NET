using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class AsyncMappingContractTest
{
    private sealed class DelegateMapping<TInput, TOutput>(Func<TInput, CancellationToken, Task<TOutput>> function)
        : IAsyncMapping<TInput, TOutput>
    {
        public Task<TOutput> MapAsync(TInput input, CancellationToken cancellationToken = default) =>
            function(input, cancellationToken);
    }

    private sealed class HelloLease : AbstractReferenceCounted
    {
        internal int Releases;
        internal bool ReleasedOnLoop;
        internal IEventExecutor Executor;
        protected override void deallocate()
        {
            ++Releases;
            ReleasedOnLoop = Executor.inEventLoop();
        }
        public override IReferenceCounted touch(object hint) => this;
    }

    // Consumer model of SniHandler.lookup and SslClientHelloHandler.select:
    // invoke on the loop, hold ClientHello ownership during deferred lookup,
    // apply completion on the loop and resume a pending read even on failure.
    // This models common API usage, not TLS decoding or a handler module port.
    private sealed class LookupConsumer(IEventExecutor executor)
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Events = new();
        internal bool Suppressed;
        internal bool ReadPending;
        internal int Reads;
        internal bool CompletionOnLoop;
        internal object Selected;
        internal Exception Failure;
        internal Task<object> Operation;

        internal Task RequestReadAsync() => executor.SubmitAsync(() =>
        {
            if (Suppressed) ReadPending = true;
            else ++Reads;
        });

        internal async Task<object> LookupAsync(IAsyncMapping<string, object> mapping, string hostname,
            HelloLease hello, CancellationToken cancellationToken = default)
        {
            ExecutorCompletion observation = null;
            CompletionRegistration registration = null;
            try
            {
                await executor.SubmitAsync(() =>
                {
                    try
                    {
                        Operation = mapping.MapAsync(hostname, cancellationToken)
                            ?? throw new InvalidOperationException("The mapping returned a null Task.");
                        Suppressed = !Operation.IsCompleted;
                        observation = new ExecutorCompletion(executor, Operation);
                        registration = observation.Register(task =>
                        {
                            CompletionOnLoop = executor.inEventLoop();
                            hello.release();
                            Events.Add("release");
                            Suppressed = false;
                            try { Selected = ((Task<object>)task).GetAwaiter().GetResult(); }
                            catch (Exception error) { Failure = error; }
                            Events.Add("complete");
                            if (ReadPending)
                            {
                                ReadPending = false;
                                ++Reads;
                                Events.Add("read");
                            }
                        });
                    }
                    catch
                    {
                        if (registration == null && hello.refCnt() != 0) hello.release();
                        throw;
                    }
                    finally { Started.TrySetResult(); }
                }).ConfigureAwait(false);
                await registration.NotificationCompleted.ConfigureAwait(false);
                return await Operation.ConfigureAwait(false);
            }
            finally
            {
                registration?.Dispose();
                observation?.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task CompletedAndDeferredLookupKeepLoopAffinityAndHelloOwnership(bool completed, int outcome)
    {
        var executor = new DefaultEventExecutor();
        using var cancellation = new CancellationTokenSource();
        var producer = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var value = new object();
        var failure = new InvalidOperationException("mapping failed");
        string hostname = completed ? null : "example.test";
        bool invokedOnLoop = false;
        IAsyncMapping<string, object> mapping = new DelegateMapping<object, object>((input, token) =>
        {
            Assert.Equal((object)hostname, input);
            Assert.Equal(cancellation.Token, token);
            invokedOnLoop = executor.inEventLoop();
            return producer.Task;
        });
        void Finish()
        {
            if (outcome == 0) producer.SetResult(value);
            else if (outcome == 1) producer.SetException(failure);
            else { cancellation.Cancel(); producer.SetCanceled(cancellation.Token); }
        }
        var hello = new HelloLease { Executor = executor };
        var consumer = new LookupConsumer(executor);
        try
        {
            if (completed) Finish();
            Task<object> lookup = consumer.LookupAsync(mapping, hostname, hello, cancellation.Token);
            await consumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!completed)
            {
                await executor.SubmitAsync(() =>
                {
                    Assert.True(consumer.Suppressed);
                    Assert.Equal(1, hello.refCnt());
                });
                await consumer.RequestReadAsync();
                await Task.Run(Finish);
            }
            if (outcome == 0) Assert.Same(value, await lookup.WaitAsync(TimeSpan.FromSeconds(5)));
            else if (outcome == 1)
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await lookup.WaitAsync(TimeSpan.FromSeconds(5))));
            else
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await lookup.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
            Assert.True(invokedOnLoop);
            Assert.True(consumer.CompletionOnLoop);
            Assert.True(hello.ReleasedOnLoop);
            Assert.Equal(1, hello.Releases);
            Assert.Equal(0, hello.refCnt());
            Assert.Same(producer.Task, consumer.Operation);
            Assert.False(consumer.Suppressed);
            Assert.False(consumer.ReadPending);
            Assert.Equal(completed ? 0 : 1, consumer.Reads);
            Assert.Equal(completed ? new[] { "release", "complete" } : new[] { "release", "complete", "read" },
                consumer.Events);
            if (outcome == 0) Assert.Same(value, consumer.Selected);
            else if (outcome == 1) Assert.Same(failure, consumer.Failure);
            else Assert.IsAssignableFrom<OperationCanceledException>(consumer.Failure);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvocationFailureOrInvalidTaskReleasesTheRetainedHello(bool nullTask)
    {
        var executor = new DefaultEventExecutor();
        var failure = new ArgumentException("bad mapping");
        var mapping = new DelegateMapping<string, object>((_, _) => nullTask ? null : throw failure);
        var hello = new HelloLease { Executor = executor };
        var consumer = new LookupConsumer(executor);
        try
        {
            Task<object> lookup = consumer.LookupAsync(mapping, null, hello);
            if (nullTask) await Assert.ThrowsAsync<InvalidOperationException>(async () => await lookup);
            else Assert.Same(failure, await Assert.ThrowsAsync<ArgumentException>(async () => await lookup));
            Assert.Equal(1, hello.Releases);
            Assert.True(hello.ReleasedOnLoop);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task CancellationRequestDoesNotOverrideAProvidersSuccessfulResult()
    {
        var executor = new DefaultEventExecutor();
        using var cancellation = new CancellationTokenSource();
        var producer = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var mapping = new DelegateMapping<string, object>((_, token) => { received = token; return producer.Task; });
        var hello = new HelloLease { Executor = executor };
        var consumer = new LookupConsumer(executor);
        var value = new object();
        try
        {
            Task<object> lookup = consumer.LookupAsync(mapping, null, hello, cancellation.Token);
            await consumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, received);
            Assert.True(received.IsCancellationRequested);
            Assert.False(lookup.IsCompleted);
            Assert.Equal(1, hello.refCnt());
            producer.SetResult(value);
            Assert.Same(value, await lookup.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(value, consumer.Selected);
            Assert.Null(consumer.Failure);
            Assert.Equal(1, hello.Releases);
        }
        finally
        {
            producer.TrySetResult(value);
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancelingTheObserverWaitDoesNotCancelOrReleaseThePendingLookup()
    {
        var executor = new DefaultEventExecutor();
        var producer = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new DelegateMapping<string, object>((_, _) => producer.Task);
        var hello = new HelloLease { Executor = executor };
        var consumer = new LookupConsumer(executor);
        using var waitCancellation = new CancellationTokenSource();
        var value = new object();
        try
        {
            Task<object> lookup = consumer.LookupAsync(mapping, "example.test", hello);
            await consumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            waitCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await lookup.WaitAsync(waitCancellation.Token));
            Assert.False(producer.Task.IsCompleted);
            Assert.Equal(1, hello.refCnt());
            await consumer.RequestReadAsync();
            producer.SetResult(value);
            Assert.Same(value, await lookup.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, hello.Releases);
            Assert.Equal(1, consumer.Reads);
        }
        finally
        {
            producer.TrySetResult(value);
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
