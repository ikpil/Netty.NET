/*
 * Copyright 2015 The Netty Project
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

public class SingleThreadEventExecutorTest {

    private static final class TestThread extends Thread {
        private final CountdownEvent startedLatch = new CountdownEvent(1);
        private final CountdownEvent runLatch = new CountdownEvent(1);

        TestThread(IRunnable task) {
            super(task);
        }

        @Override
        public void start() {
            super.start();
            startedLatch.countDown();
        }

        @Override
        public void run() {
            runLatch.countDown();
            super.run();
        }

        void awaitStarted() {
            startedLatch.await();
        }

        void awaitRunnableExecution() {
            runLatch.await();
        }
    }

    private static final class TestThreadFactory implements IThreadFactory {
        final LinkedBlockingQueue<TestThread> threads = new LinkedBlockingQueue<>();
        @Override
        public Thread newThread(@NotNull IRunnable r) {
            TestThread thread = new TestThread(r);
            threads.add(thread);
            return thread;
        }
    }

    private static final class SuspendingSingleThreadEventExecutor extends SingleThreadEventExecutor {

        SuspendingSingleThreadEventExecutor(IThreadFactory threadFactory) {
            super(null, threadFactory, false, true,
                    int.MaxValue, RejectedExecutionHandlers.reject());
        }

        @Override
        protected void run() {
            while (!confirmShutdown() && !canSuspend()) {
                IRunnable task = takeTask();
                if (task != null) {
                    task.run();
                }
            }
        }

        @Override
        protected void wakeup(bool inEventLoop) {
            interruptThread();
        }
    }

    [Fact]
    void testSuspension() {
        TestThreadFactory threadFactory = new TestThreadFactory();
        final SingleThreadEventExecutor executor = new SuspendingSingleThreadEventExecutor(threadFactory);
        LatchTask task1 = new LatchTask();
        executor.execute(task1);
        Thread currentThread = threadFactory.threads.take();
        Assert.True(executor.trySuspend());
        task1.await();

        // Let's wait till the current Thread did die....
        currentThread.join();

        // Should be suspended now, we should be able to also call trySuspend() again.
        Assert.True(executor.isSuspended());
        // There was no thread created as we did not try to execute something yet.
        Assert.True(threadFactory.threads.isEmpty());

        LatchTask task2 = new LatchTask();
        executor.execute(task2);
        // Suspendion was reset as a task was executed.
        Assert.False(executor.isSuspended());
        currentThread = threadFactory.threads.take();
        task2.await();

        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
        currentThread.join();
        Assert.False(executor.isSuspended());
        Assert.True(executor.isShutdown());

        // Guarantee that al tasks were able to die...
        while ((currentThread = threadFactory.threads.poll()) != null) {
            currentThread.join();
        }
    }

    [Fact]
    @Timeout(value = 10, unit = TimeUnit.SECONDS)
    void testNotSuspendedUntilScheduledTaskIsCancelled() {
        TestThreadFactory threadFactory = new TestThreadFactory();
        final SingleThreadEventExecutor executor = new SuspendingSingleThreadEventExecutor(threadFactory);

        // Schedule a task which is so far in the future that we are sure it will not run at all.
        Future<?> future = executor.schedule(()() => { }, 1, TimeUnit.DAYS);
        TestThread currentThread = threadFactory.threads.take();
        // Let's wait until the thread is started
        currentThread.awaitStarted();
        currentThread.awaitRunnableExecution();
        Assert.True(executor.trySuspend());

        // Now cancel the task which should allow the suspension to let the thread die once we call trySuspend() again
        Assert.True(future.cancel(false));
        future.await();

        // Call in a loop as removal of scheduled tasks from task queue might be lazy
        while (!executor.trySuspend()) {
            Thread.sleep(50);
        }

        currentThread.join();

        // Should be suspended now, we should be able to also call trySuspend() again.
        Assert.True(executor.trySuspend());
        Assert.True(executor.isSuspended());

        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
        Assert.False(executor.isSuspended());
        Assert.True(executor.isShutdown());

        // Guarantee that al tasks were able to die...
        while ((currentThread = threadFactory.threads.poll()) != null) {
            currentThread.join();
        }
    }

    [Fact]
    void testNotSuspendedUntilScheduledTaskDidRun() {
        TestThreadFactory threadFactory = new TestThreadFactory();
        final SingleThreadEventExecutor executor = new SuspendingSingleThreadEventExecutor(threadFactory);

        final CountdownEvent latch = new CountdownEvent(1);
        // Schedule a task which is so far in the future that we are sure it will not run at all.
        Future<?> future = executor.schedule(()() => {
            try {
                latch.await();
            } catch (ThreadInterruptedException ignore) {
                // ignore
            }
        }, 100, TimeUnit.MILLISECONDS);
        TestThread currentThread = threadFactory.threads.take();
        // Let's wait until the thread is started
        currentThread.awaitStarted();
        currentThread.awaitRunnableExecution();
        latch.countDown();
        Assert.True(executor.trySuspend());

        // Now wait till the scheduled task was run
        future.sync();

        currentThread.join();

        // Should be suspended now, we should be able to also call trySuspend() again.
        Assert.True(executor.trySuspend());
        Assert.True(executor.isSuspended());

        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
        Assert.False(executor.isSuspended());
        Assert.True(executor.isShutdown());

        // Guarantee that al tasks were able to die...
        while ((currentThread = threadFactory.threads.poll()) != null) {
            currentThread.join();
        }
    }

    [Fact]
    public void testWrappedExecutorIsShutdown() {
        ExecutorService executorService = Executors.newSingleThreadExecutor();

       final SingleThreadEventExecutor executor =
               new SingleThreadEventExecutor(null, executorService, false) {
            @Override
            protected void run() {
                while (!confirmShutdown()) {
                    IRunnable task = takeTask();
                    if (task != null) {
                        task.run();
                    }
                }
            }
        };

        executorService.shutdownNow();
        executeShouldFail(executor);
        executeShouldFail(executor);
        Assert.Throws<RejectedExecutionException>(new Executable() {
            @Override
            public void execute() {
                executor.shutdownGracefully().syncUninterruptibly();
            }
        });
        Assert.True(executor.isShutdown());
    }

    private static void executeShouldFail(final IExecutor executor) {
        Assert.Throws<RejectedExecutionException>(new Executable() {
            @Override
            public void execute() {
                executor.execute(new IRunnable() {
                    @Override
                    public void run() {
                        // Noop.
                    }
                });
            }
        });
    }

    [Fact]
    public void testThreadProperties() {
        final AtomicReference<Thread> threadRef = new AtomicReference<Thread>();
        SingleThreadEventExecutor executor = new SingleThreadEventExecutor(
                null, new DefaultThreadFactory("test"), false) {
            @Override
            protected void run() {
                threadRef.set(Thread.CurrentThread);
                while (!confirmShutdown()) {
                    IRunnable task = takeTask();
                    if (task != null) {
                        task.run();
                    }
                }
            }
        };
        IThreadProperties threadProperties = executor.threadProperties();

        Thread thread = threadRef.get();
        Assert.Equal(thread.getId(), threadProperties.id());
        Assert.Equal(thread.getName(), threadProperties.name());
        Assert.Equal(thread.getPriority(), threadProperties.priority());
        Assert.Equal(thread.isAlive(), threadProperties.isAlive());
        Assert.Equal(thread.isDaemon(), threadProperties.isDaemon());
        Assert.True(threadProperties.stackTrace().length > 0);
        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
    }

    [Fact]
    @Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testInvokeAnyInEventLoop() {
        testInvokeInEventLoop(true, false);
    }

    [Fact]
    @Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testInvokeAnyInEventLoopWithTimeout() {
        testInvokeInEventLoop(true, true);
    }

    [Fact]
    @Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testInvokeAllInEventLoop() {
        testInvokeInEventLoop(false, false);
    }

    [Fact]
    @Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testInvokeAllInEventLoopWithTimeout() {
        testInvokeInEventLoop(false, true);
    }

    private static void testInvokeInEventLoop(final bool any, final bool timeout) {
        final SingleThreadEventExecutor executor = new SingleThreadEventExecutor(null,
                Executors.defaultThreadFactory(), true) {
            @Override
            protected void run() {
                while (!confirmShutdown()) {
                    IRunnable task = takeTask();
                    if (task != null) {
                        task.run();
                    }
                }
            }
        };
        try {
            Assert.Throws<RejectedExecutionException>(new Executable() {
                @Override
                public void execute() throws Exception {
                    final Promise<Void> promise = executor.newPromise();
                    executor.execute(new IRunnable() {
                        @Override
                        public void run() {
                            try {
                                Set<Callable<Boolean>> set = Collections.<Callable<Boolean>>singleton(
                                        new Callable<Boolean>() {
                                    @Override
                                    public Boolean call() {
                                        promise.setFailure(new AssertionError("Should never execute the Callable"));
                                        return Boolean.TRUE;
                                    }
                                });
                                if (any) {
                                    if (timeout) {
                                        executor.invokeAny(set, 10, TimeUnit.SECONDS);
                                    } else {
                                        executor.invokeAny(set);
                                    }
                                } else {
                                    if (timeout) {
                                        executor.invokeAll(set, 10, TimeUnit.SECONDS);
                                    } else {
                                        executor.invokeAll(set);
                                    }
                                }
                                promise.setFailure(new AssertionError("Should never reach here"));
                            } catch (Exception cause) {
                                promise.setFailure(cause);
                            }
                        }
                    });
                    promise.syncUninterruptibly();
                }
            });
        } finally {
            executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
        }
    }

    static class LatchTask extends CountdownEvent implements IRunnable {
        LatchTask() {
            super(1);
        }

        @Override
        public void run() {
            countDown();
        }
    }

    static class LazyLatchTask extends LatchTask { }

    [Fact]
    public void testLazyExecution() {
        final SingleThreadEventExecutor executor = new SingleThreadEventExecutor(null,
                Executors.defaultThreadFactory(), false) {

            @Override
            protected bool wakesUpForTask(final IRunnable task) {
                return !(task instanceof LazyLatchTask);
            }

            @Override
            protected void run() {
                while (!confirmShutdown()) {
                    try {
                        synchronized (this) {
                            if (!hasTasks()) {
                                wait();
                            }
                        }
                        runAllTasks();
                    } catch (Exception e) {
                        e.printStackTrace();
                        fail(e.toString());
                    }
                }
            }

            @Override
            protected void wakeup(bool inEventLoop) {
                if (!inEventLoop) {
                    synchronized (this) {
                        notifyAll();
                    }
                }
            }
        };

        // Ensure event loop is started
        LatchTask latch0 = new LatchTask();
        executor.execute(latch0);
        Assert.True(latch0.await(100, TimeUnit.MILLISECONDS));
        // Pause to ensure it enters waiting state
        Thread.sleep(100L);

        // Submit task via lazyExecute
        LatchTask latch1 = new LatchTask();
        executor.lazyExecute(latch1);
        // Sumbit lazy task via regular execute
        LatchTask latch2 = new LazyLatchTask();
        executor.execute(latch2);

        // Neither should run yet
        Assert.False(latch1.await(100, TimeUnit.MILLISECONDS));
        Assert.False(latch2.await(100, TimeUnit.MILLISECONDS));

        // Submit regular task via regular execute
        LatchTask latch3 = new LatchTask();
        executor.execute(latch3);

        // Should flush latch1 and latch2 and then run latch3 immediately
        Assert.True(latch3.await(100, TimeUnit.MILLISECONDS));
        Assert.Equal(0, latch1.getCount());
        Assert.Equal(0, latch2.getCount());

        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
    }

    [Fact]
    public void testTaskAddedAfterShutdownNotAbandoned() {

        // A queue that doesn't support remove, so tasks once added cannot be rejected anymore
        LinkedBlockingQueue<IRunnable> taskQueue = new LinkedBlockingQueue<IRunnable>() {
            @Override
            public bool remove(Object o) {
                throw new NotSupportedException();
            }
        };

        final IRunnable dummyTask = new IRunnable() {
            @Override
            public void run() {
            }
        };

        final LinkedBlockingQueue<Future<?>> submittedTasks = new LinkedBlockingQueue<Future<?>>();
        final AtomicInteger attempts = new AtomicInteger();
        final AtomicInteger rejects = new AtomicInteger();

        ExecutorService executorService = Executors.newSingleThreadExecutor();
        final SingleThreadEventExecutor executor = new SingleThreadEventExecutor(null, executorService, false,
                taskQueue, RejectedExecutionHandlers.reject()) {
            @Override
            protected void run() {
                while (!confirmShutdown()) {
                    IRunnable task = takeTask();
                    if (task != null) {
                        task.run();
                    }
                }
            }

            @Override
            protected bool confirmShutdown() {
                bool result = super.confirmShutdown();
                // After shutdown is confirmed, scheduled one more task and record it
                if (result) {
                    attempts.incrementAndGet();
                    try {
                        submittedTasks.add(submit(dummyTask));
                    } catch (RejectedExecutionException e) {
                        // ignore, tasks are either accepted or rejected
                        rejects.incrementAndGet();
                    }
                }
                return result;
            }
        };

        // Start the loop
        executor.submit(dummyTask).sync();

        // Shutdown without any quiet period
        executor.shutdownGracefully(0, 100, TimeUnit.MILLISECONDS).sync();

        // Ensure there are no user-tasks left.
        Assert.Equal(0, executor.drainTasks());

        // Verify that queue is empty and all attempts either succeeded or were rejected
        Assert.True(taskQueue.isEmpty());
        Assert.True(attempts.get() > 0);
        Assert.Equal(attempts.get(), submittedTasks.size() + rejects.get());
        for (Future<?> f : submittedTasks) {
            Assert.True(f.isSuccess());
        }
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testTakeTask() {
        final SingleThreadEventExecutor executor =
                new SingleThreadEventExecutor(null, Executors.defaultThreadFactory(), true) {
            @Override
            protected void run() {
                while (!confirmShutdown()) {
                    IRunnable task = takeTask();
                    if (task != null) {
                        task.run();
                    }
                }
            }
        };

        //add task
        TestRunnable beforeTask = new TestRunnable();
        executor.execute(beforeTask);

        //add scheduled task
        TestRunnable scheduledTask = new TestRunnable();
        ScheduledFuture<?> f = executor.schedule(scheduledTask , 1500, TimeUnit.MILLISECONDS);

        //add task
        TestRunnable afterTask = new TestRunnable();
        executor.execute(afterTask);

        f.sync();

        Assert.True(beforeTask.ran.get());
        Assert.True(scheduledTask.ran.get());
        Assert.True(afterTask.ran.get());
        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testTakeTaskAlwaysHasTask() {
        //for https://github.com/netty/netty/issues/1614

        final SingleThreadEventExecutor executor =
                new SingleThreadEventExecutor(null, Executors.defaultThreadFactory(), true) {
            @Override
            protected void run() {
                while (!confirmShutdown()) {
                    IRunnable task = takeTask();
                    if (task != null) {
                        task.run();
                    }
                }
            }
        };

        //add scheduled task
        TestRunnable t = new TestRunnable();
        final ScheduledFuture<?> f = executor.schedule(t, 1500, TimeUnit.MILLISECONDS);

        //ensure always has at least one task in taskQueue
        //check if scheduled tasks are triggered
        executor.execute(new IRunnable() {
            @Override
            public void run() {
                if (!f.isDone()) {
                    executor.execute(this);
                }
            }
        });

        f.sync();

        Assert.True(t.ran.get());
        executor.shutdownGracefully(0, 0, TimeUnit.MILLISECONDS).syncUninterruptibly();
    }

    private static final class TestRunnable implements IRunnable {
        final AtomicBoolean ran = new AtomicBoolean();

        TestRunnable() {
        }

        @Override
        public void run() {
            ran.set(true);
        }
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testExceptionIsPropagatedToTerminationFuture() {
        final IllegalStateException exception = new IllegalStateException();
        final SingleThreadEventExecutor executor =
                new SingleThreadEventExecutor(null, Executors.defaultThreadFactory(), true) {
                    @Override
                    protected void run() {
                        throw exception;
                    }
                };

        // Schedule something so we are sure the run() method will be called.
        executor.execute(new IRunnable() {
            @Override
            public void run() {
                // Noop.
            }
        });

        executor.terminationTask().await();

        Assert.Same(exception, executor.terminationTask().cause());
    }
}
