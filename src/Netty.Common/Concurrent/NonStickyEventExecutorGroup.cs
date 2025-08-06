/*
 * Copyright 2016 The Netty Project
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

namespace Netty.NET.Common.Concurrent;


/**
 * {@link IEventExecutorGroup} which will preserve {@link Runnable} execution order but makes no guarantees about what
 * {@link IEventExecutor} (and therefore {@link Thread}) will be used to execute the {@link Runnable}s.
 *
 * <p>The {@link IEventExecutorGroup#next()} for the wrapped {@link IEventExecutorGroup} must <strong>NOT</strong> return
 * executors of type {@link OrderedEventExecutor}.
 */
@UnstableApi
public final class NonStickyEventExecutorGroup : IEventExecutorGroup {
    private readonly IEventExecutorGroup group;
    private readonly int maxTaskExecutePerRun;

    /**
     * Creates a new instance. Be aware that the given {@link IEventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link OrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group) {
        this(group, 1024);
    }

    /**
     * Creates a new instance. Be aware that the given {@link IEventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link OrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group, int maxTaskExecutePerRun) {
        this.group = verify(group);
        this.maxTaskExecutePerRun = ObjectUtil.checkPositive(maxTaskExecutePerRun, "maxTaskExecutePerRun");
    }

    private static IEventExecutorGroup verify(IEventExecutorGroup group) {
        Iterator<IEventExecutor> executors = ObjectUtil.checkNotNull(group, "group").iterator();
        while (executors.hasNext()) {
            IEventExecutor executor = executors.next();
            if (executor instanceof OrderedEventExecutor) {
                throw new ArgumentException("IEventExecutorGroup " + group
                        + " contains OrderedEventExecutors: " + executor);
            }
        }
        return group;
    }

    private NonStickyOrderedEventExecutor newExecutor(IEventExecutor executor) {
        return new NonStickyOrderedEventExecutor(executor, maxTaskExecutePerRun);
    }

    @Override
    public bool isShuttingDown() {
        return group.isShuttingDown();
    }

    @Override
    public Future<?> shutdownGracefully() {
        return group.shutdownGracefully();
    }

    @Override
    public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit) {
        return group.shutdownGracefully(quietPeriod, timeout, unit);
    }

    @Override
    public Future<?> terminationFuture() {
        return group.terminationFuture();
    }

    @SuppressWarnings("deprecation")
    @Override
    public void shutdown() {
        group.shutdown();
    }

    @SuppressWarnings("deprecation")
    @Override
    public List<Runnable> shutdownNow() {
        return group.shutdownNow();
    }

    @Override
    public IEventExecutor next() {
        return newExecutor(group.next());
    }

    @Override
    public Iterator<IEventExecutor> iterator() {
        final Iterator<IEventExecutor> itr = group.iterator();
        return new Iterator<IEventExecutor>() {
            @Override
            public bool hasNext() {
                return itr.hasNext();
            }

            @Override
            public IEventExecutor next() {
                return newExecutor(itr.next());
            }

            @Override
            public void remove() {
                itr.remove();
            }
        };
    }

    @Override
    public Future<?> submit(Runnable task) {
        return group.submit(task);
    }

    @Override
    public Task<T> submit(Runnable task, T result) {
        return group.submit(task, result);
    }

    @Override
    public Task<T> submit(Func<T> task) {
        return group.submit(task);
    }

    @Override
    public ScheduledFuture<?> schedule(Runnable command, long delay, TimeSpan unit) {
        return group.schedule(command, delay, unit);
    }

    @Override
    public <V> ScheduledFuture<V> schedule(Func<V> callable, long delay, TimeSpan unit) {
        return group.schedule(callable, delay, unit);
    }

    @Override
    public ScheduledFuture<?> scheduleAtFixedRate(Runnable command, long initialDelay, long period, TimeSpan unit) {
        return group.scheduleAtFixedRate(command, initialDelay, period, unit);
    }

    @Override
    public ScheduledFuture<?> scheduleWithFixedDelay(Runnable command, long initialDelay, long delay, TimeSpan unit) {
        return group.scheduleWithFixedDelay(command, initialDelay, delay, unit);
    }

    @Override
    public bool isShutdown() {
        return group.isShutdown();
    }

    @Override
    public bool isTerminated() {
        return group.isTerminated();
    }

    @Override
    public bool awaitTermination(long timeout, TimeSpan unit) {
        return group.awaitTermination(timeout, unit);
    }

    @Override
    public <T> List<java.util.concurrent.Future<T>> invokeAll(
            Collection<? extends Func<T>> tasks) {
        return group.invokeAll(tasks);
    }

    @Override
    public <T> List<java.util.concurrent.Future<T>> invokeAll(
            Collection<? extends Func<T>> tasks, long timeout, TimeSpan unit) {
        return group.invokeAll(tasks, timeout, unit);
    }

    @Override
    public <T> T invokeAny(Collection<? extends Func<T>> tasks) throws InterruptedException, ExecutionException {
        return group.invokeAny(tasks);
    }

    @Override
    public <T> T invokeAny(Collection<? extends Func<T>> tasks, long timeout, TimeSpan unit)
            throws InterruptedException, ExecutionException, TimeoutException {
        return group.invokeAny(tasks, timeout, unit);
    }

    @Override
    public void execute(Runnable command) {
        group.execute(command);
    }

    private static readonly class NonStickyOrderedEventExecutor extends AbstractEventExecutor
            implements Runnable, OrderedEventExecutor {
        private readonly IEventExecutor executor;
        private readonly Queue<Runnable> tasks = PlatformDependent.newMpscQueue();

        private static readonly int NONE = 0;
        private static readonly int SUBMITTED = 1;
        private static readonly int RUNNING = 2;

        private readonly AtomicInteger state = new AtomicInteger();
        private readonly int maxTaskExecutePerRun;

        private readonly AtomicReference<Thread> executingThread = new AtomicReference<Thread>();

        NonStickyOrderedEventExecutor(IEventExecutor executor, int maxTaskExecutePerRun) {
            super(executor);
            this.executor = executor;
            this.maxTaskExecutePerRun = maxTaskExecutePerRun;
        }

        @Override
        public void run() {
            if (!state.compareAndSet(SUBMITTED, RUNNING)) {
                return;
            }
            Thread current = Thread.currentThread();
            executingThread.set(current);
            for (;;) {
                int i = 0;
                try {
                    for (; i < maxTaskExecutePerRun; i++) {
                        Runnable task = tasks.poll();
                        if (task == null) {
                            break;
                        }
                        safeExecute(task);
                    }
                } finally {
                    if (i == maxTaskExecutePerRun) {
                        try {
                            state.set(SUBMITTED);
                            // Only set executingThread to null if no other thread did update it yet.
                            executingThread.compareAndSet(current, null);
                            executor.execute(this);
                            return; // done
                        } catch (Exception ignore) {
                            // Reset the state back to running as we will keep on executing tasks.
                            state.set(RUNNING);
                            // if an error happened we should just ignore it and let the loop run again as there is not
                            // much else we can do. Most likely this was triggered by a full task queue. In this case
                            // we just will run more tasks and try again later.
                        }
                    } else {
                        state.set(NONE);
                        // After setting the state to NONE, look at the tasks queue one more time.
                        // If it is empty, then we can return from this method.
                        // Otherwise, it means the producer thread has called execute(Runnable)
                        // and enqueued a task in between the tasks.poll() above and the state.set(NONE) here.
                        // There are two possible scenarios when this happens
                        //
                        // 1. The producer thread sees state == NONE, hence the compareAndSet(NONE, SUBMITTED)
                        //    is successfully setting the state to SUBMITTED. This mean the producer
                        //    will call / has called executor.execute(this). In this case, we can just return.
                        // 2. The producer thread don't see the state change, hence the compareAndSet(NONE, SUBMITTED)
                        //    returns false. In this case, the producer thread won't call executor.execute.
                        //    In this case, we need to change the state to RUNNING and keeps running.
                        //
                        // The above cases can be distinguished by performing a
                        // compareAndSet(NONE, RUNNING). If it returns "false", it is case 1; otherwise it is case 2.
                        if (tasks.isEmpty() || !state.compareAndSet(NONE, RUNNING)) {
                            // Only set executingThread to null if no other thread did update it yet.
                            executingThread.compareAndSet(current, null);
                            return; // done
                        }
                    }
                }
            }
        }

        @Override
        public bool inEventLoop(Thread thread) {
            return executingThread.get() == thread;
        }

        @Override
        public bool isShuttingDown() {
            return executor.isShutdown();
        }

        @Override
        public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit) {
            return executor.shutdownGracefully(quietPeriod, timeout, unit);
        }

        @Override
        public Future<?> terminationFuture() {
            return executor.terminationFuture();
        }

        @Override
        public void shutdown() {
            executor.shutdown();
        }

        @Override
        public bool isShutdown() {
            return executor.isShutdown();
        }

        @Override
        public bool isTerminated() {
            return executor.isTerminated();
        }

        @Override
        public bool awaitTermination(long timeout, TimeSpan unit) {
            return executor.awaitTermination(timeout, unit);
        }

        @Override
        public void execute(Runnable command) {
            if (!tasks.offer(command)) {
                throw new RejectedExecutionException();
            }
            if (state.compareAndSet(NONE, SUBMITTED)) {
                // Actually it could happen that the runnable was picked up in between but we not care to much and just
                // execute ourself. At worst this will be a NOOP when run() is called.
                executor.execute(this);
            }
        }
    }
}
