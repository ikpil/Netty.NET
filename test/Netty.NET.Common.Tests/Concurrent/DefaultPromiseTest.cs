/*
 * Copyright 2013 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

// Native translation: the producer owns a TCS, its observer owns callback policy,
// and consumers receive only the Task. Method names retain upstream scenario identity.
public class DefaultPromiseTest
{
    // CLR adaptation: a StackOverflowException terminates the process and cannot be used to discover stack depth.
    // Both original chain shapes are exercised at 20,000 promises, inside and outside the executor thread.
    private static int StackOverflowTestDepth() => 20000;
    private static TaskCompletionSource<T> Source<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class RejectingEventExecutor : AbstractEventExecutor
    {
        public int submissions;
        public override bool IsShuttingDown() => false;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        public override Task Termination => Task.CompletedTask;
        public override void Shutdown() { }
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
        public override bool InEventLoop(Thread thread) => false;
        public override void Execute(IRunnable command)
        {
            Interlocked.Increment(ref submissions);
            throw new InvalidOperationException("Cannot schedule commands");
        }
    }
    [Fact]
    public void TestCancelDoesNotScheduleWhenNoListeners()
    {
        var executor = new RejectingEventExecutor();
        var source = Source<object>();
        using var observer = new ExecutorCompletion(executor, source.Task);
        Assert.True(source.TrySetCanceled());
        Assert.True(source.Task.IsCanceled);
        Assert.Equal(0, executor.submissions);
    }
    [Fact]
    public void TestSuccessDoesNotScheduleWhenNoListeners()
    {
        var executor = new RejectingEventExecutor();
        object value = new();
        var source = Source<object>();
        using var observer = new ExecutorCompletion(executor, source.Task);
        source.SetResult(value);
        Assert.Same(value, source.Task.GetAwaiter().GetResult());
        Assert.Equal(0, executor.submissions);
    }
    [Fact]
    public void TestFailureDoesNotScheduleWhenNoListeners()
    {
        var executor = new RejectingEventExecutor();
        Exception cause = new Exception();
        var source = Source<object>();
        using var observer = new ExecutorCompletion(executor, source.Task);
        source.SetException(cause);
        Assert.Same(cause, source.Task.Exception.InnerException);
        Assert.Equal(0, executor.submissions);
    }
    [Fact]
    public void TestCancellationExceptionIsThrownWhenBlockingGet()
    {
        var source = Source<object>();
        Assert.True(source.TrySetCanceled());
        Assert.ThrowsAny<OperationCanceledException>(() => source.Task.GetAwaiter().GetResult());
    }
    [Fact]
    public async Task TestCancellationExceptionIsThrownWhenBlockingGetWithTimeout()
    {
        var source = Source<object>();
        Assert.True(source.TrySetCanceled());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }
    [Fact]
    public async Task TestCancellationExceptionIsReturnedAsCause()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = Source<object>();
        Assert.True(source.TrySetCanceled(cancellation.Token));
        // A canceled Task has a token, not a fault Exception/cause accessor.
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.Task);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(source.Task.Exception);
    }
    [Fact]
    public void TestStackOverflowWithImmediateEventExecutorA()
    {
        TestStackOverFlowChainedFutures(StackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, true, false);
        TestStackOverFlowChainedFutures(StackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, false, false);
    }
    [Fact]
    public void TestNoStackOverflowWithDefaultEventExecutorA()
    {
        using var service = new TestSingleThreadExecutor();
        IEventExecutor executor = new DefaultEventExecutor(service);
        try
        {
            TestStackOverFlowChainedFutures(StackOverflowTestDepth(), executor, true, false);
            TestStackOverFlowChainedFutures(StackOverflowTestDepth(), executor, false, false);
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void TestNoStackOverflowWithImmediateEventExecutorB()
    {
        TestStackOverFlowChainedFutures(StackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, true, true);
        TestStackOverFlowChainedFutures(StackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, false, true);
    }
    [Fact]
    public void TestNoStackOverflowWithDefaultEventExecutorB()
    {
        using var service = new TestSingleThreadExecutor();
        IEventExecutor executor = new DefaultEventExecutor(service);
        try
        {
            TestStackOverFlowChainedFutures(StackOverflowTestDepth(), executor, true, true);
            TestStackOverFlowChainedFutures(StackOverflowTestDepth(), executor, false, true);
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void TestListenerNotifyOrder()
    {
        IEventExecutor executor = new TestEventExecutor();
        try
        {
            using var listeners = new BlockingCollection<Action<Task>>();
            int runs = 100000;
            for (int i = 0; i < runs; i++)
            {
                var source = Source<object>();
                using var observer = new ExecutorCompletion(executor, source.Task);
                Action<Task> listener1 = null, listener2 = null, listener3 = null, listener4 = null;
                CompletionRegistration fourth = null;
                listener1 = _ => listeners.Add(listener1);
                listener2 = _ => listeners.Add(listener2);
                listener4 = _ => listeners.Add(listener4);
                listener3 = _ => { listeners.Add(listener3); fourth = observer.Register(listener4); };
                GlobalEventExecutor.INSTANCE.Execute(Runnables.Create(() => source.SetResult(null)));
                using var first = observer.Register(listener1);
                using var second = observer.Register(listener2);
                using var third = observer.Register(listener3);
                foreach (var expected in new[] { listener1, listener2, listener3, listener4 })
                {
                    Assert.True(listeners.TryTake(out var actual, TimeSpan.FromSeconds(5)), "Listener timeout during run " + i);
                    Assert.Same(expected, actual);
                }
                Assert.Empty(listeners);
                // Await dispatch completion separately before disposing the queue/observer.
                Assert.True(Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted,
                    third.NotificationCompleted).Wait(TimeSpan.FromSeconds(5)));
                Assert.NotNull(fourth);
                Assert.True(fourth.NotificationCompleted.Wait(TimeSpan.FromSeconds(5)));
                fourth.Dispose();
            }
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void TestListenerNotifyLater()
    {
        // Testing first execution path in DefaultPromise
        RunListenerNotifyLater(1);
        // Testing second execution path in DefaultPromise
        RunListenerNotifyLater(2);
    }
    [Fact]
    public void TestPromiseListenerAddWhenCompleteFailure() => TestPromiseListenerAddWhenComplete(FakeException());
    [Fact]
    public void TestPromiseListenerAddWhenCompleteSuccess() => TestPromiseListenerAddWhenComplete(null);
    [Fact]
    public void TestLateListenerIsOrderedCorrectlySuccess() => TestLateListenerIsOrderedCorrectly(null);
    [Fact]
    public void TestLateListenerIsOrderedCorrectlyFailure() => TestLateListenerIsOrderedCorrectly(FakeException());
    [Fact]
    public void TestSignalRace()
    {
        TimeSpan wait = TimeSpan.FromSeconds(10);
        IEventExecutor executor = new TestEventExecutor();
        var sources = new Dictionary<Thread, TaskCompletionSource<object>>();
        try
        {
            const int numberOfAttempts = 4096;
            for (int i = 0; i < numberOfAttempts; i++)
            {
                var source = Source<object>();
                var thread = new Thread(() => source.SetResult(null)) { IsBackground = true };
                sources.Add(thread, source);
            }
            foreach (var pair in sources)
            {
                pair.Key.Start();
                long start = Stopwatch.GetTimestamp();
                pair.Value.Task.WaitAsync(wait).GetAwaiter().GetResult();
                Assert.True(Stopwatch.GetElapsedTime(start) < wait);
                Assert.True(pair.Key.Join(wait));
            }
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void SignalUncancellableCompletionValue()
    {
        var source = Source<Signal>();
        var value = Signal.ValueOf(typeof(DefaultPromiseTest), "UNCANCELLABLE");
        source.SetResult(value);
        Assert.True(source.Task.IsCompleted);
        Assert.True(source.Task.IsCompletedSuccessfully);
        Assert.Same(value, source.Task.GetAwaiter().GetResult());
    }
    [Fact]
    public void SignalSuccessCompletionValue()
    {
        var source = Source<Signal>();
        var value = Signal.ValueOf(typeof(DefaultPromiseTest), "SUCCESS");
        source.SetResult(value);
        Assert.True(source.Task.IsCompleted);
        Assert.True(source.Task.IsCompletedSuccessfully);
        Assert.Same(value, source.Task.GetAwaiter().GetResult());
    }
    [Fact]
    public async Task SetUncancellableGetNow()
    {
        IEventExecutor executor = new TestEventExecutor();
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            Task<string> operation = executor.SubmitAsync(token =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(token.IsCancellationRequested);
                return "success";
            }, cancellation.Token);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(operation.IsCompleted);
            cancellation.Cancel();
            Assert.False(operation.IsCompleted);
            Assert.False(operation.IsCompletedSuccessfully);
            release.Set();
            Assert.Equal("success", await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(operation.IsCompletedSuccessfully);
        }
        finally { release.Set(); Shutdown(executor); }
    }
    private static void TestStackOverFlowChainedFutures(int promiseChainLength, IEventExecutor executor, bool runTestInExecutorThread, bool lateListener)
    {
        var sources = new TaskCompletionSource<object>[promiseChainLength];
        var observations = new ExecutorCompletion[promiseChainLength];
        using var latch = new CountdownEvent(promiseChainLength);
        void Initialize()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                int index = i;
                // Exercise inline producer completion and the dispatcher's actual
                // recursion bound, as in the original synchronous promise chain.
                // RCAA would insert 20000 ThreadPool round trips and test pool latency
                // instead of this stack-safety scenario. Other fixtures retain RCAA.
                sources[i] = new TaskCompletionSource<object>();
                observations[i] = new ExecutorCompletion(executor, sources[i].Task);
                Action<Task> completeNext = _ =>
                {
                    if (index + 1 < sources.Length) sources[index + 1].SetResult(null);
                    latch.Signal();
                };
                observations[i].Register(lateListener
                    ? _ => observations[index].Register(completeNext)
                    : completeNext);
            }
            sources[0].SetResult(null);
        }
        try
        {
            if (runTestInExecutorThread) executor.Execute(Runnables.Create(Initialize));
            else Initialize();
            Assert.True(latch.Wait(TimeSpan.FromSeconds(2)));
            foreach (var source in sources) Assert.True(source.Task.IsCompletedSuccessfully);
        }
        finally { foreach (var observation in observations) observation?.Dispose(); }
    }
    /**
     * This test is mean to simulate the following sequence of events, which all take place on the I/O thread:
     * <ol>
     * <li>A write is done</li>
     * <li>The write operation completes, and the promise state is changed to done</li>
     * <li>A listener is added to the return from the write. The {@link FutureListener#operationComplete(Future)}
     * updates state which must be invoked before the response to the previous write is read.</li>
     * <li>The write operation</li>
     * </ol>
     */
    private static void TestLateListenerIsOrderedCorrectly(Exception cause)
    {
        IEventExecutor executor = new TestEventExecutor();
        try
        {
            int state = 0;
            using var latch1 = new CountdownEvent(1);
            using var latch2 = new CountdownEvent(2);
            var source = Source<object>();
            using var observer = new ExecutorCompletion(executor, source.Task);
            // Add a listener before completion so "lateListener" is used next time we add a listener.
            observer.Register(_ => Assert.Equal(0, Interlocked.CompareExchange(ref state, 1, 0)));
            // Simulate write operation completing, which will execute listeners in another thread.
            if (cause == null) source.SetResult(null);
            else source.SetException(cause);
            // Add a "late listener"
            observer.Register(_ => { Assert.Equal(1, Interlocked.CompareExchange(ref state, 2, 1)); latch1.Signal(); });
            // Wait for the listeners and late listeners to be completed.
            Assert.True(latch1.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(2, Volatile.Read(ref state));
            // This is the important listener. A late listener that is added after all late listeners
            // have completed, and needs to update state before a read operation (on the same executor).
            executor.Execute(Runnables.Create(() => observer.Register(_ =>
            {
                Assert.Equal(2, Interlocked.CompareExchange(ref state, 3, 2));
                latch2.Signal();
            })));
            // Simulate a read operation being queued up in the executor.
            executor.Execute(Runnables.Create(() =>
            {
                // This is the key, we depend upon the state being set in the next listener.
                Assert.Equal(3, Volatile.Read(ref state));
                latch2.Signal();
            }));
            Assert.True(latch2.Wait(TimeSpan.FromSeconds(2)));
        }
        finally { Shutdown(executor); }
    }
    private static void TestPromiseListenerAddWhenComplete(Exception cause)
    {
        using var latch = new CountdownEvent(1);
        var source = Source<object>();
        using var observer = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
        observer.Register(_ => observer.Register(_ => latch.Signal()));
        if (cause == null) source.SetResult(null);
        else source.SetException(cause);
        Assert.True(latch.Wait(TimeSpan.FromSeconds(2)));
    }
    private static void RunListenerNotifyLater(int numListenersBefore)
    {
        IEventExecutor executor = new TestEventExecutor();
        try
        {
            int expectedCount = numListenersBefore + 2;
            using var latch = new CountdownEvent(expectedCount);
            Action<Task> listener = _ => latch.Signal();
            var source = Source<object>();
            using var observer = new ExecutorCompletion(executor, source.Task);
            executor.Execute(Runnables.Create(() =>
            {
                for (int i = 0; i < numListenersBefore; i++) observer.Register(listener);
                source.SetResult(null);
                GlobalEventExecutor.INSTANCE.Execute(Runnables.Create(() => observer.Register(listener)));
                observer.Register(listener);
            }));
            Assert.True(latch.Wait(TimeSpan.FromSeconds(5)), "Should have notified " + expectedCount + " listeners");
        }
        finally { Shutdown(executor); }
    }
    private sealed class TestEventExecutor : SingleThreadEventExecutor
    {
        internal TestEventExecutor() : base(null, new AnonymousThreadFactory(task => new Thread(task.Run) { IsBackground = true }), true) { }
        protected override void Run()
        {
            for (;;)
            {
                IRunnable task = TakeTask();
                if (task != null) { task.Run(); UpdateLastExecutionTime(); }
                if (ConfirmShutdown()) break;
            }
        }
    }
    // CLR test harness for Executors.newSingleThreadExecutor(); the original executor-service lifecycle is retained.
    private sealed class TestSingleThreadExecutor : IExecutor, IDisposable
    {
        private readonly BlockingCollection<IRunnable> tasks = new();
        private readonly Thread thread;
        internal TestSingleThreadExecutor()
        {
            thread = new Thread(() => { foreach (var task in tasks.GetConsumingEnumerable()) task.Run(); }) { IsBackground = true };
            thread.Start();
        }
        public void Execute(IRunnable task) => tasks.Add(task);
        public void Dispose()
        {
            tasks.CompleteAdding();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            tasks.Dispose();
        }
    }
    private static void Shutdown(IEventExecutor executor)
    {
        Assert.True(executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5)));
    }
    private static Exception FakeException() => new Exception("fake exception");
}
