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

namespace Netty.NET.Common.Tests.Concurrent;

public class DefaultPromiseTest {
    private static final IInternalLogger logger = InternalLoggerFactory.getInstance(DefaultPromiseTest.class);
    private static int stackOverflowDepth;

    @BeforeAll
    public static void beforeClass() {
        try {
            findStackOverflowDepth();
            throw new IllegalStateException("Expected StackOverflowError but didn't get it?!");
        } catch (StackOverflowError e) {
            logger.debug("StackOverflowError depth: {}", stackOverflowDepth);
        }
    }

    //@SuppressWarnings("InfiniteRecursion")
    private static void findStackOverflowDepth() {
        ++stackOverflowDepth;
        findStackOverflowDepth();
    }

    private static int stackOverflowTestDepth() {
        return max(stackOverflowDepth << 1, stackOverflowDepth);
    }

    private static class RejectingEventExecutor extends AbstractEventExecutor {
        @Override
        public bool isShuttingDown() {
        return false;
    }

    @Override
    public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeUnit unit) {
        return null;
    }

    @Override
    public Task terminationTask() {
        return null;
    }

    @Override
    public void shutdown() {
    }

    @Override
    public bool isShutdown() {
        return false;
    }

    @Override
    public bool isTerminated() {
        return false;
    }

    @Override
    public bool awaitTermination(long timeout, TimeUnit unit) {
        return false;
}

@Override
public ScheduledFuture<?> schedule(IRunnable command, long delay, TimeUnit unit) {
    return Assert.Fail("Cannot schedule commands");
}

@Override
public <V> ScheduledFuture<V> schedule(Func<V> callable, long delay, TimeUnit unit) {
    return Assert.Fail("Cannot schedule commands");
}

@Override
public ScheduledFuture<?> scheduleAtFixedRate(IRunnable command, long initialDelay, long period, TimeUnit unit) {
    return Assert.Fail("Cannot schedule commands");
}

@Override
public ScheduledFuture<?> scheduleWithFixedDelay(IRunnable command, long initialDelay, long delay,
    TimeUnit unit) {
    return Assert.Fail("Cannot schedule commands");
}

@Override
public bool inEventLoop(Thread thread) {
    return false;
}

@Override
public void execute(IRunnable command) {
    Assert.Fail("Cannot schedule commands");
}

}

[Fact]
public void testCancelDoesNotScheduleWhenNoListeners() {
    EventExecutor executor = new RejectingEventExecutor();

    Promise<Void> promise = new DefaultPromise<Void>(executor);
    Assert.True(promise.cancel(false));
    Assert.True(promise.isCancelled());
}

[Fact]
public void testSuccessDoesNotScheduleWhenNoListeners() {
    EventExecutor executor = new RejectingEventExecutor();

    object value = new object();
    Promise<object> promise = new DefaultPromise<object>(executor);
    promise.setSuccess(value);
    Assert.Same(value, promise.getNow());
}

[Fact]
public void testFailureDoesNotScheduleWhenNoListeners() {
    EventExecutor executor = new RejectingEventExecutor();

    Exception cause = new Exception();
    Promise<Void> promise = new DefaultPromise<Void>(executor);
    promise.setFailure(cause);
    Assert.Same(cause, promise.cause());
}

[Fact]
public void testCancellationExceptionIsThrownWhenBlockingGet() {
    final Promise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
    Assert.True(promise.cancel(false));
    Assert.Throws<TaskCanceledException>(new Executable() {
        @Override
        public void execute() throws Exception {
        promise.get();
    }
    });
}

[Fact]
public void testCancellationExceptionIsThrownWhenBlockingGetWithTimeout() {
    final Promise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
    Assert.True(promise.cancel(false));
    Assert.Throws<TaskCanceledException>(new Executable() {
        @Override
        public void execute() throws Exception {
        promise.get(1, TimeUnit.SECONDS);
    }
    });
}

[Fact]
public void testCancellationExceptionIsReturnedAsCause() {
    final Promise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
    Assert.True(promise.cancel(false));
    assertThat(promise.cause()).isInstanceOf(TaskCanceledException.class);
}

[Fact]
public void testStackOverflowWithImmediateEventExecutorA() {
    testStackOverFlowChainedFuturesA(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, true);
    testStackOverFlowChainedFuturesA(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, false);
}

[Fact]
public void testNoStackOverflowWithDefaultEventExecutorA() {
    ExecutorService executorService = Executors.newSingleThreadExecutor();
    try {
        EventExecutor executor = new DefaultEventExecutor(executorService);
        try {
            testStackOverFlowChainedFuturesA(stackOverflowTestDepth(), executor, true);
            testStackOverFlowChainedFuturesA(stackOverflowTestDepth(), executor, false);
        } finally {
            executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS);
        }
    } finally {
        executorService.shutdown();
    }
}

[Fact]
public void testNoStackOverflowWithImmediateEventExecutorB() {
    testStackOverFlowChainedFuturesB(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, true);
    testStackOverFlowChainedFuturesB(stackOverflowTestDepth(), ImmediateEventExecutor.INSTANCE, false);
}

[Fact]
public void testNoStackOverflowWithDefaultEventExecutorB() {
    ExecutorService executorService = Executors.newSingleThreadExecutor();
    try {
        EventExecutor executor = new DefaultEventExecutor(executorService);
        try {
            testStackOverFlowChainedFuturesB(stackOverflowTestDepth(), executor, true);
            testStackOverFlowChainedFuturesB(stackOverflowTestDepth(), executor, false);
        } finally {
            executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS);
        }
    } finally {
        executorService.shutdown();
    }
}

[Fact]
public void testListenerNotifyOrder() {
    EventExecutor executor = new TestEventExecutor();
    try {
        final IBlockingQueue<FutureListener<Void>> listeners = new LinkedBlockingQueue<FutureListener<Void>>();
        int runs = 100000;

        for (int i = 0; i < runs; i++) {
            final Promise<Void> promise = new DefaultPromise<Void>(executor);
            final FutureListener<Void> listener1 = new FutureListener<Void>() {
                @Override
                public void operationComplete(Future<Void> future) {
                listeners.add(this);
            }
            };
            final FutureListener<Void> listener2 = new FutureListener<Void>() {
                @Override
                public void operationComplete(Future<Void> future) {
                listeners.add(this);
            }
            };
            final FutureListener<Void> listener4 = new FutureListener<Void>() {
                @Override
                public void operationComplete(Future<Void> future) {
                listeners.add(this);
            }
            };
            final FutureListener<Void> listener3 = new FutureListener<Void>() {
                @Override
                public void operationComplete(Future<Void> future) {
                listeners.add(this);
                future.addListener(listener4);
            }
            };

            GlobalEventExecutor.INSTANCE.execute(new IRunnable() {
                @Override
                public void run() {
                promise.setSuccess(null);
            }
            });

            promise.addListener(listener1).addListener(listener2).addListener(listener3);

            Assert.Same(listener1, listeners.take(), "Fail 1 during run " + i + " / " + runs);
            Assert.Same(listener2, listeners.take(), "Fail 2 during run " + i + " / " + runs);
            Assert.Same(listener3, listeners.take(), "Fail 3 during run " + i + " / " + runs);
            Assert.Same(listener4, listeners.take(), "Fail 4 during run " + i + " / " + runs);
            Assert.True(listeners.isEmpty(), "Fail during run " + i + " / " + runs);
        }
    } finally {
        executor.shutdownGracefully(0, 0, TimeUnit.SECONDS).sync();
    }
}

[Fact]
public void testListenerNotifyLater() {
    // Testing first execution path in DefaultPromise
    testListenerNotifyLater(1);

    // Testing second execution path in DefaultPromise
    testListenerNotifyLater(2);
}

[Fact]
@Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
public void testPromiseListenerAddWhenCompleteFailure() {
    testPromiseListenerAddWhenComplete(fakeException());
}

[Fact]
@Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
public void testPromiseListenerAddWhenCompleteSuccess() {
    testPromiseListenerAddWhenComplete(null);
}

[Fact]
@Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
public void testLateListenerIsOrderedCorrectlySuccess() {
    testLateListenerIsOrderedCorrectly(null);
}

[Fact]
@Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
public void testLateListenerIsOrderedCorrectlyFailure() {
    testLateListenerIsOrderedCorrectly(fakeException());
}

[Fact]
public void testSignalRace() {
    final long wait = TimeUnit.NANOSECONDS.convert(10, TimeUnit.SECONDS);
    EventExecutor executor = null;
    try {
        executor = new TestEventExecutor();

        final int numberOfAttempts = 4096;
        final Map<Thread, DefaultPromise<Void>> promises = new Dictionary<Thread, DefaultPromise<Void>>();
        for (int i = 0; i < numberOfAttempts; i++) {
            final DefaultPromise<Void> promise = new DefaultPromise<Void>(executor);
            final Thread thread = new Thread(new IRunnable() {
                @Override
                public void run() {
                promise.setSuccess(null);
            }
            });
            promises.put(thread, promise);
        }

        for (final Map.Entry<Thread, DefaultPromise<Void>> promise : promises.entrySet()) {
            promise.getKey().start();
            final long start = PreciseTimer.nanoTime();
            promise.getValue().awaitUninterruptibly(wait, TimeUnit.NANOSECONDS);
            assertThat(PreciseTimer.nanoTime() - start).isLessThan(wait);
        }
    } finally {
        if (executor != null) {
            executor.shutdownGracefully();
        }
    }
}

[Fact]
public void signalUncancellableCompletionValue() {
    final Promise<Signal> promise = new DefaultPromise<Signal>(ImmediateEventExecutor.INSTANCE);
    promise.setSuccess(Signal.valueOf(DefaultPromise.class, "UNCANCELLABLE"));
    Assert.True(promise.isDone());
    Assert.True(promise.isSuccess());
}

[Fact]
public void signalSuccessCompletionValue() {
    final Promise<Signal> promise = new DefaultPromise<Signal>(ImmediateEventExecutor.INSTANCE);
    promise.setSuccess(Signal.valueOf(DefaultPromise.class, "SUCCESS"));
    Assert.True(promise.isDone());
    Assert.True(promise.isSuccess());
}

[Fact]
public void setUncancellableGetNow() {
    final Promise<string> promise = new DefaultPromise<string>(ImmediateEventExecutor.INSTANCE);
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

private static void testStackOverFlowChainedFuturesA(int promiseChainLength, final EventExecutor executor,
bool runTestInExecutorThread)
{
    final Promise<Void>[] p = new DefaultPromise[promiseChainLength];
    final CountdownEvent latch = new CountdownEvent(promiseChainLength);

    if (runTestInExecutorThread) {
        executor.execute(new IRunnable() {
            @Override
            public void run() {
            testStackOverFlowChainedFuturesA(executor, p, latch);
        }
        });
    } else {
        testStackOverFlowChainedFuturesA(executor, p, latch);
    }

    Assert.True(latch.await(2, TimeUnit.SECONDS));
    for (int i = 0; i < p.length; ++i) {
        Assert.True(p[i].isSuccess(), "index " + i);
    }
}

private static void testStackOverFlowChainedFuturesA(EventExecutor executor, final Promise<Void>[] p,
final CountdownEvent latch) {
    for (int i = 0; i < p.length; i ++) {
        final int finalI = i;
        p[i] = new DefaultPromise<Void>(executor);
        p[i].addListener(new FutureListener<Void>() {
            @Override
            public void operationComplete(Future<Void> future) {
            if (finalI + 1 < p.length) {
            p[finalI + 1].setSuccess(null);
        }
        latch.countDown();
        }
        });
    }

    p[0].setSuccess(null);
}

private static void testStackOverFlowChainedFuturesB(int promiseChainLength, final EventExecutor executor,
bool runTestInExecutorThread)
{
    final Promise<Void>[] p = new DefaultPromise[promiseChainLength];
    final CountdownEvent latch = new CountdownEvent(promiseChainLength);

    if (runTestInExecutorThread) {
        executor.execute(new IRunnable() {
            @Override
            public void run() {
            testStackOverFlowChainedFuturesB(executor, p, latch);
        }
        });
    } else {
        testStackOverFlowChainedFuturesB(executor, p, latch);
    }

    Assert.True(latch.await(2, TimeUnit.SECONDS));
    for (int i = 0; i < p.length; ++i) {
        Assert.True(p[i].isSuccess(), "index " + i);
    }
}

private static void testStackOverFlowChainedFuturesB(EventExecutor executor, final Promise<Void>[] p,
final CountdownEvent latch) {
    for (int i = 0; i < p.length; i ++) {
        final int finalI = i;
        p[i] = new DefaultPromise<Void>(executor);
        p[i].addListener(new FutureListener<Void>() {
            @Override
            public void operationComplete(Future<Void> future) {
            future.addListener(new FutureListener<Void>() {
            @Override
            public void operationComplete(Future<Void> future) {
            if (finalI + 1 < p.length) {
            p[finalI + 1].setSuccess(null);
        }
        latch.countDown();
        }
        });
        }
        });
    }

    p[0].setSuccess(null);
}

/**
     * This test is mean to simulate the following sequence of events, which all take place on the I/O thread:
     * <ol>
     * <li>A write is done</li>
     * <li>The write operation completes, and the promise state is changed to done</li>
     * <li>A listener is added to the return from the write. The {@link IFutureListener#operationComplete(Future)}
     * updates state which must be invoked before the response to the previous write is read.</li>
     * <li>The write operation</li>
     * </ol>
     */
private static void testLateListenerIsOrderedCorrectly(Exception cause) {
    final EventExecutor executor = new TestEventExecutor();
    try {
        final AtomicInteger state = new AtomicInteger();
        final CountdownEvent latch1 = new CountdownEvent(1);
        final CountdownEvent latch2 = new CountdownEvent(2);
        final Promise<Void> promise = new DefaultPromise<Void>(executor);

        // Add a listener before completion so "lateListener" is used next time we add a listener.
        promise.addListener(new FutureListener<Void>() {
            @Override
            public void operationComplete(Future<Void> future) {
            Assert.True(state.compareAndSet(0, 1));
        }
        });

        // Simulate write operation completing, which will execute listeners in another thread.
        if (cause == null) {
            promise.setSuccess(null);
        } else {
            promise.setFailure(cause);
        }

        // Add a "late listener"
        promise.addListener(new FutureListener<Void>() {
            @Override
            public void operationComplete(Future<Void> future) {
            Assert.True(state.compareAndSet(1, 2));
            latch1.countDown();
        }
        });

        // Wait for the listeners and late listeners to be completed.
        latch1.await();
        Assert.Equal(2, state.get());

        // This is the important listener. A late listener that is added after all late listeners
        // have completed, and needs to update state before a read operation (on the same executor).
        executor.execute(new IRunnable() {
            @Override
            public void run() {
            promise.addListener(new FutureListener<Void>() {
            @Override
            public void operationComplete(Future<Void> future) {
            Assert.True(state.compareAndSet(2, 3));
            latch2.countDown();
        }
        });
        }
        });

        // Simulate a read operation being queued up in the executor.
        executor.execute(new IRunnable() {
            @Override
            public void run() {
            // This is the key, we depend upon the state being set in the next listener.
            Assert.Equal(3, state.get());
            latch2.countDown();
        }
        });

        latch2.await();
    } finally {
        executor.shutdownGracefully(0, 0, TimeUnit.SECONDS).sync();
    }
}

private static void testPromiseListenerAddWhenComplete(Exception cause) {
    final CountdownEvent latch = new CountdownEvent(1);
    final Promise<Void> promise = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
    promise.addListener(new FutureListener<Void>() {
        @Override
        public void operationComplete(Future<Void> future) {
        promise.addListener(new FutureListener<Void>() {
        @Override
        public void operationComplete(Future<Void> future) {
        latch.countDown();
    }
    });
    }
    });
    if (cause == null) {
        promise.setSuccess(null);
    } else {
        promise.setFailure(cause);
    }
    latch.await();
}

private static void testListenerNotifyLater(final int numListenersBefore) {
    EventExecutor executor = new TestEventExecutor();
    int expectedCount = numListenersBefore + 2;
    final CountdownEvent latch = new CountdownEvent(expectedCount);
    final FutureListener<Void> listener = new FutureListener<Void>() {
        @Override
        public void operationComplete(Future<Void> future) {
        latch.countDown();
    }
    };
    final Promise<Void> promise = new DefaultPromise<Void>(executor);
    executor.execute(new IRunnable() {
        @Override
        public void run() {
        for (int i = 0; i < numListenersBefore; i++) {
        promise.addListener(listener);
    }
    promise.setSuccess(null);

    GlobalEventExecutor.INSTANCE.execute(new IRunnable() {
        @Override
        public void run() {
        promise.addListener(listener);
    }
    });
    promise.addListener(listener);
    }
    });

    Assert.True(latch.await(5, TimeUnit.SECONDS),
        "Should have notified " + expectedCount + " listeners");
    executor.shutdownGracefully().sync();
}

private static final class TestEventExecutor extends SingleThreadEventExecutor {
    TestEventExecutor() {
        super(null, Executors.defaultThreadFactory(), true);
    }

    @Override
    protected void run() {
        for (;;) {
            IRunnable task = takeTask();
            if (task != null) {
                task.run();
                updateLastExecutionTime();
            }

            if (confirmShutdown()) {
                break;
            }
        }
    }
}

private static Exception fakeException() {
    return new Exception("fake exception");
}
}