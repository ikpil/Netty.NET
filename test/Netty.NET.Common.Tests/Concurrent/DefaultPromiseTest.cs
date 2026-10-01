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
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class DefaultPromiseTest
{
    // CLR adaptation: a StackOverflowException terminates the process and cannot be used to discover stack depth.
    // Both original chain shapes are exercised at 20,000 promises, inside and outside the executor thread.
    private static int stackOverflowTestDepth() => 20000;
    private sealed class RejectingEventExecutor : AbstractEventExecutor
    {
        public int submissions;
        public override bool isShuttingDown() => false;
        public override Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => null;
        public override Task terminationTask() => null;
        public override void shutdown() { }
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
        public override IScheduledTask schedule(IRunnable command, TimeSpan delay) => throw new InvalidOperationException("Cannot schedule commands");
        public override IScheduledTask<V> schedule<V>(ICallable<V> callable, TimeSpan delay) => throw new InvalidOperationException("Cannot schedule commands");
        public override IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period) => throw new InvalidOperationException("Cannot schedule commands");
        public override IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay) => throw new InvalidOperationException("Cannot schedule commands");
        public override bool inEventLoop(Thread thread) => false;
        public override void execute(IRunnable command)
        {
            Interlocked.Increment(ref submissions);
            throw new InvalidOperationException("Cannot schedule commands");
        }
    }
    private sealed class Listener : IFutureListener<Void>
    {
        private readonly Action<IFuture<Void>> action;
        internal Listener(Action<IFuture<Void>> action) { this.action = action; }
        public void operationComplete(IFuture<Void> future) => action(future);
    }
    [Fact]
    public void testCancelDoesNotScheduleWhenNoListeners()
    {
        var executor = new RejectingEventExecutor();
        IPromise<Void> promise = new DefaultPromise<Void>(executor);
        Assert.True(promise.cancel(false));
        Assert.True(promise.isCancelled());
        Assert.Equal(0, executor.submissions);
    }
    [Fact]
    public void testSuccessDoesNotScheduleWhenNoListeners()
    {
        var executor = new RejectingEventExecutor();
        object value = new object();
        IPromise<object> promise = new DefaultPromise<object>(executor);
        promise.setSuccess(value);
        Assert.Same(value, promise.getNow());
        Assert.Equal(0, executor.submissions);
    }
    [Fact]
    public void testFailureDoesNotScheduleWhenNoListeners()
    {
        var executor = new RejectingEventExecutor();
        Exception cause = new Exception();
        IPromise<Void> promise = new DefaultPromise<Void>(executor);
        promise.setFailure(cause);
        Assert.Same(cause, promise.cause());
        Assert.Equal(0, executor.submissions);
    }
    [Fact]
    public void testCancellationExceptionIsThrownWhenBlockingGet()
    {
        IPromise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
        Assert.True(promise.cancel(false));
        Assert.ThrowsAny<OperationCanceledException>(() => promise.get());
    }
    [Fact]
    public void testCancellationExceptionIsThrownWhenBlockingGetWithTimeout()
    {
        IPromise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
        Assert.True(promise.cancel(false));
        Assert.ThrowsAny<OperationCanceledException>(() => promise.get(TimeSpan.FromSeconds(1)));
    }
    [Fact]
    public void testCancellationExceptionIsReturnedAsCause()
    {
        IPromise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
        Assert.True(promise.cancel(false));
        Assert.IsAssignableFrom<OperationCanceledException>(promise.cause());
    }
    [Fact]
    public void testStackOverflowWithImmediateEventExecutorA()
    {
        testStackOverFlowChainedFutures(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, true, false);
        testStackOverFlowChainedFutures(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, false, false);
    }
    [Fact]
    public void testNoStackOverflowWithDefaultEventExecutorA()
    {
        IEventExecutor executor = new DefaultEventExecutor();
        try
        {
            testStackOverFlowChainedFutures(stackOverflowTestDepth(), executor, true, false);
            testStackOverFlowChainedFutures(stackOverflowTestDepth(), executor, false, false);
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void testNoStackOverflowWithImmediateEventExecutorB()
    {
        testStackOverFlowChainedFutures(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, true, true);
        testStackOverFlowChainedFutures(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, false, true);
    }
    [Fact]
    public void testNoStackOverflowWithDefaultEventExecutorB()
    {
        IEventExecutor executor = new DefaultEventExecutor();
        try
        {
            testStackOverFlowChainedFutures(stackOverflowTestDepth(), executor, true, true);
            testStackOverFlowChainedFutures(stackOverflowTestDepth(), executor, false, true);
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void testListenerNotifyOrder()
    {
        IEventExecutor executor = new TestEventExecutor();
        try
        {
            using var listeners = new BlockingCollection<Listener>();
            int runs = 100000;
            for (int i = 0; i < runs; i++)
            {
                IPromise<Void> promise = new DefaultPromise<Void>(executor);
                Listener listener1 = null, listener2 = null, listener3 = null, listener4 = null;
                listener1 = new Listener(_ => listeners.Add(listener1));
                listener2 = new Listener(_ => listeners.Add(listener2));
                listener4 = new Listener(_ => listeners.Add(listener4));
                listener3 = new Listener(future => { listeners.Add(listener3); future.addListener(listener4); });
                GlobalEventExecutor.INSTANCE.execute(Runnables.Create(() => promise.setSuccess(null)));
                promise.addListener(listener1).addListener(listener2).addListener(listener3);
                foreach (var expected in new[] { listener1, listener2, listener3, listener4 })
                {
                    Assert.True(listeners.TryTake(out var actual, TimeSpan.FromSeconds(5)), "Listener timeout during run " + i);
                    Assert.Same(expected, actual);
                }
                Assert.Empty(listeners);
            }
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void testListenerNotifyLater()
    {
        // Testing first execution path in DefaultPromise
        runListenerNotifyLater(1);
        // Testing second execution path in DefaultPromise
        runListenerNotifyLater(2);
    }
    [Fact]
    public void testPromiseListenerAddWhenCompleteFailure() => testPromiseListenerAddWhenComplete(fakeException());
    [Fact]
    public void testPromiseListenerAddWhenCompleteSuccess() => testPromiseListenerAddWhenComplete(null);
    [Fact]
    public void testLateListenerIsOrderedCorrectlySuccess() => testLateListenerIsOrderedCorrectly(null);
    [Fact]
    public void testLateListenerIsOrderedCorrectlyFailure() => testLateListenerIsOrderedCorrectly(fakeException());
    [Fact]
    public void testSignalRace()
    {
        TimeSpan wait = TimeSpan.FromSeconds(10);
        IEventExecutor executor = new TestEventExecutor();
        var promises = new Dictionary<Thread, DefaultPromise<Void>>();
        try
        {
            const int numberOfAttempts = 4096;
            for (int i = 0; i < numberOfAttempts; i++)
            {
                var promise = new DefaultPromise<Void>(executor);
                var thread = new Thread(() => promise.setSuccess(null)) { IsBackground = true };
                promises.Add(thread, promise);
            }
            foreach (var pair in promises)
            {
                pair.Key.Start();
                long start = Stopwatch.GetTimestamp();
                Assert.True(pair.Value.awaitUninterruptibly(wait));
                Assert.True(Stopwatch.GetElapsedTime(start) < wait);
                Assert.True(pair.Key.Join(wait));
            }
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void signalUncancellableCompletionValue()
    {
        IPromise<Signal> promise = new DefaultPromise<Signal>(ImmediateEventExecutor.INSTANCE);
        promise.setSuccess(Signal.valueOf(typeof(DefaultPromise<Signal>), "UNCANCELLABLE"));
        Assert.True(promise.isDone());
        Assert.True(promise.isSuccess());
    }
    [Fact]
    public void signalSuccessCompletionValue()
    {
        IPromise<Signal> promise = new DefaultPromise<Signal>(ImmediateEventExecutor.INSTANCE);
        promise.setSuccess(Signal.valueOf(typeof(DefaultPromise<Signal>), "SUCCESS"));
        Assert.True(promise.isDone());
        Assert.True(promise.isSuccess());
    }
    [Fact]
    public void setUncancellableGetNow()
    {
        IPromise<string> promise = new DefaultPromise<string>(ImmediateEventExecutor.INSTANCE);
        Assert.Null(promise.getNow());
        Assert.True(promise.setUncancellable());
        Assert.Null(promise.getNow());
        Assert.False(promise.isDone());
        Assert.False(promise.isSuccess());
        promise.setSuccess("success");
        Assert.True(promise.isDone());
        Assert.True(promise.isSuccess());
        Assert.Equal("success", promise.getNow());
    }
    private static void testStackOverFlowChainedFutures(int promiseChainLength, IEventExecutor executor, bool runTestInExecutorThread, bool lateListener)
    {
        var promises = new IPromise<Void>[promiseChainLength];
        using var latch = new CountdownEvent(promiseChainLength);
        void initialize()
        {
            for (int i = 0; i < promises.Length; i++)
            {
                int index = i;
                promises[i] = new DefaultPromise<Void>(executor);
                var completeNext = new Listener(_ =>
                {
                    if (index + 1 < promises.Length) promises[index + 1].setSuccess(null);
                    latch.Signal();
                });
                promises[i].addListener(lateListener ? new Listener(future => future.addListener(completeNext)) : completeNext);
            }
            promises[0].setSuccess(null);
        }
        if (runTestInExecutorThread) executor.execute(Runnables.Create(initialize));
        else initialize();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(2)));
        foreach (var promise in promises) Assert.True(promise.isSuccess());
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
    private static void testLateListenerIsOrderedCorrectly(Exception cause)
    {
        IEventExecutor executor = new TestEventExecutor();
        try
        {
            int state = 0;
            using var latch1 = new CountdownEvent(1);
            using var latch2 = new CountdownEvent(2);
            IPromise<Void> promise = new DefaultPromise<Void>(executor);
            // Add a listener before completion so "lateListener" is used next time we add a listener.
            promise.addListener(new Listener(_ => Assert.Equal(0, Interlocked.CompareExchange(ref state, 1, 0))));
            // Simulate write operation completing, which will execute listeners in another thread.
            if (cause == null) promise.setSuccess(null);
            else promise.setFailure(cause);
            // Add a "late listener"
            promise.addListener(new Listener(_ => { Assert.Equal(1, Interlocked.CompareExchange(ref state, 2, 1)); latch1.Signal(); }));
            // Wait for the listeners and late listeners to be completed.
            Assert.True(latch1.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(2, Volatile.Read(ref state));
            // This is the important listener. A late listener that is added after all late listeners
            // have completed, and needs to update state before a read operation (on the same executor).
            executor.execute(Runnables.Create(() => promise.addListener(new Listener(_ =>
            {
                Assert.Equal(2, Interlocked.CompareExchange(ref state, 3, 2));
                latch2.Signal();
            }))));
            // Simulate a read operation being queued up in the executor.
            executor.execute(Runnables.Create(() =>
            {
                // This is the key, we depend upon the state being set in the next listener.
                Assert.Equal(3, Volatile.Read(ref state));
                latch2.Signal();
            }));
            Assert.True(latch2.Wait(TimeSpan.FromSeconds(2)));
        }
        finally { shutdown(executor); }
    }
    private static void testPromiseListenerAddWhenComplete(Exception cause)
    {
        using var latch = new CountdownEvent(1);
        IPromise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
        promise.addListener(new Listener(_ => promise.addListener(new Listener(_ => latch.Signal()))));
        if (cause == null) promise.setSuccess(null);
        else promise.setFailure(cause);
        Assert.True(latch.Wait(TimeSpan.FromSeconds(2)));
    }
    private static void runListenerNotifyLater(int numListenersBefore)
    {
        IEventExecutor executor = new TestEventExecutor();
        try
        {
            int expectedCount = numListenersBefore + 2;
            using var latch = new CountdownEvent(expectedCount);
            var listener = new Listener(_ => latch.Signal());
            IPromise<Void> promise = new DefaultPromise<Void>(executor);
            executor.execute(Runnables.Create(() =>
            {
                for (int i = 0; i < numListenersBefore; i++) promise.addListener(listener);
                promise.setSuccess(null);
                GlobalEventExecutor.INSTANCE.execute(Runnables.Create(() => promise.addListener(listener)));
                promise.addListener(listener);
            }));
            Assert.True(latch.Wait(TimeSpan.FromSeconds(5)), "Should have notified " + expectedCount + " listeners");
        }
        finally { shutdown(executor); }
    }
    private sealed class TestEventExecutor : SingleThreadEventExecutor
    {
        internal TestEventExecutor() : base(null, new DefaultThreadFactory(typeof(TestEventExecutor)), true) { }
        protected override void run()
        {
            for (;;)
            {
                IRunnable task = takeTask();
                if (task != null) { task.run(); updateLastExecutionTime(); }
                if (confirmShutdown()) break;
            }
        }
    }
    private static void shutdown(IEventExecutor executor)
    {
        Assert.True(executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5)));
    }
    private static Exception fakeException() => new Exception("fake exception");
}
