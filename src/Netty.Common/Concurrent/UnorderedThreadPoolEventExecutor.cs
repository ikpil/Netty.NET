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
namespace Netty.NET.Common.Concurrent;





















/**
 * {@link IEventExecutor} implementation which makes no guarantees about the ordering of task execution that
 * are submitted because there may be multiple threads executing these tasks.
 * This implementation is most useful for protocols that do not need strict ordering.
 * <p>
 * <strong>Because it provides no ordering, care should be taken when using it!</strong>
 *
 * @deprecated The behavior of this event executor deviates from the typical Netty execution model
 * and can cause subtle issues as a result.
 * Applications that wish to process messages with greater parallelism, should instead do explicit
 * off-loading to their own thread-pools.
 */
@Deprecated
public final class UnorderedThreadPoolEventExecutor extends ScheduledThreadPoolExecutor : IEventExecutor {
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(
            typeof(UnorderedThreadPoolEventExecutor));

    private readonly Promise<?> terminationFuture = GlobalEventExecutor.INSTANCE.newPromise();
    private readonly ISet<IEventExecutor> executorSet = Collections.singleton(this);
    private readonly ISet<Thread> eventLoopThreads = ConcurrentDictionary.newKeySet();

    /**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int, ThreadFactory)}
     * using {@link DefaultThreadFactory}.
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize) {
        this(corePoolSize, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor)));
    }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory)}
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize, ThreadFactory threadFactory) {
        super(corePoolSize, threadFactory);
        setThreadFactory(new AccountingThreadFactory(threadFactory, eventLoopThreads));
    }

    /**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int,
     * ThreadFactory, java.util.concurrent.RejectedExecutionHandler)} using {@link DefaultThreadFactory}.
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize, RejectedExecutionHandler handler) {
        this(corePoolSize, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor)), handler);
    }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory, RejectedExecutionHandler)}
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize, ThreadFactory threadFactory,
                                            RejectedExecutionHandler handler) {
        super(corePoolSize, threadFactory, handler);
        setThreadFactory(new AccountingThreadFactory(threadFactory, eventLoopThreads));
    }

    @Override
    public IEventExecutor next() {
        return this;
    }

    @Override
    public IEventExecutorGroup parent() {
        return this;
    }

    @Override
    public bool inEventLoop() {
        return inEventLoop(Thread.currentThread());
    }

    @Override
    public bool inEventLoop(Thread thread) {
        return eventLoopThreads.contains(thread);
    }

    @Override
    public <V> Promise<V> newPromise() {
        return new DefaultPromise<V>(this);
    }

    @Override
    public <V> ProgressivePromise<V> newProgressivePromise() {
        return new DefaultProgressivePromise<V>(this);
    }

    @Override
    public <V> Future<V> newSucceededFuture(V result) {
        return new SucceededFuture<V>(this, result);
    }

    @Override
    public <V> Future<V> newFailedFuture(Exception cause) {
        return new FailedFuture<V>(this, cause);
    }

    @Override
    public bool isShuttingDown() {
        return isShutdown();
    }

    @Override
    public List<Runnable> shutdownNow() {
        List<Runnable> tasks = super.shutdownNow();
        terminationFuture.trySuccess(null);
        return tasks;
    }

    @Override
    public void shutdown() {
        super.shutdown();
        terminationFuture.trySuccess(null);
    }

    @Override
    public Future<?> shutdownGracefully() {
        return shutdownGracefully(2, 15, TimeSpan.SECONDS);
    }

    @Override
    public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit) {
        // TODO: At the moment this just calls shutdown but we may be able to do something more smart here which
        //       respects the quietPeriod and timeout.
        shutdown();
        return terminationFuture();
    }

    @Override
    public Future<?> terminationFuture() {
        return terminationFuture;
    }

    @Override
    public Iterator<IEventExecutor> iterator() {
        return executorSet.iterator();
    }

    @Override
    protected <V> RunnableScheduledFuture<V> decorateTask(Runnable runnable, RunnableScheduledFuture<V> task) {
        return runnable instanceof NonNotifyRunnable ?
                task : new RunnableScheduledFutureTask<V>(this, task, false);
    }

    @Override
    protected <V> RunnableScheduledFuture<V> decorateTask(Func<V> callable, RunnableScheduledFuture<V> task) {
        return new RunnableScheduledFutureTask<V>(this, task, true);
    }

    @Override
    public ScheduledFuture<?> schedule(Runnable command, long delay, TimeSpan unit) {
        return (ScheduledFuture<?>) super.schedule(command, delay, unit);
    }

    @Override
    public <V> ScheduledFuture<V> schedule(Func<V> callable, long delay, TimeSpan unit) {
        return (ScheduledFuture<V>) super.schedule(callable, delay, unit);
    }

    @Override
    public ScheduledFuture<?> scheduleAtFixedRate(Runnable command, long initialDelay, long period, TimeSpan unit) {
        return (ScheduledFuture<?>) super.scheduleAtFixedRate(command, initialDelay, period, unit);
    }

    @Override
    public ScheduledFuture<?> scheduleWithFixedDelay(Runnable command, long initialDelay, long delay, TimeSpan unit) {
        return (ScheduledFuture<?>) super.scheduleWithFixedDelay(command, initialDelay, delay, unit);
    }

    @Override
    public Future<?> submit(Runnable task) {
        return (Future<?>) super.submit(task);
    }

    @Override
    public Task<T> submit(Runnable task, T result) {
        return (Future<T>) super.submit(task, result);
    }

    @Override
    public Task<T> submit(Func<T> task) {
        return (Future<T>) super.submit(task);
    }

    @Override
    public void execute(Runnable command) {
        super.schedule(new NonNotifyRunnable(command), 0, NANOSECONDS);
    }

    private static readonly class RunnableScheduledFutureTask<V> extends PromiseTask<V>
            implements RunnableScheduledFuture<V>, ScheduledFuture<V> {
        private readonly RunnableScheduledFuture<V> future;
        private readonly bool wasCallable;

        RunnableScheduledFutureTask(IEventExecutor executor, RunnableScheduledFuture<V> future, bool wasCallable) {
            super(executor, future);
            this.future = future;
            this.wasCallable = wasCallable;
        }

        @Override
        V runTask() {
            V result =  super.runTask();
            if (result == null && wasCallable) {
                // If this RunnableScheduledFutureTask wraps a RunnableScheduledFuture that wraps a Callable we need
                // to ensure that we return the correct result by calling future.get().
                //
                // See https://github.com/netty/netty/issues/11072
                assert future.isDone();
                try {
                    return future.get();
                } catch (ExecutionException e) {
                    // unwrap exception.
                    throw e.getCause();
                }
            }
            return result;
        }

        @Override
        public void run() {
            if (!isPeriodic()) {
                super.run();
            } else if (!isDone()) {
                try {
                    // Its a periodic task so we need to ignore the return value
                    runTask();
                } catch (Exception cause) {
                    if (!tryFailureInternal(cause)) {
                        logger.warn("Failure during execution of task", cause);
                    }
                }
            }
        }

        @Override
        public bool isPeriodic() {
            return future.isPeriodic();
        }

        @Override
        public long getDelay(TimeSpan unit) {
            return future.getDelay(unit);
        }

        @Override
        public int compareTo(Delayed o) {
            return future.compareTo(o);
        }
    }

    // This is a special wrapper which we will be used in execute(...) to wrap the submitted Runnable. This is needed as
    // ScheduledThreadPoolExecutor.execute(...) will delegate to submit(...) which will then use decorateTask(...).
    // The problem with this is that decorateTask(...) needs to ensure we only do our own decoration if we not call
    // from execute(...) as otherwise we may end up creating an endless loop because DefaultPromise will call
    // IEventExecutor.execute(...) when notify the listeners of the promise.
    //
    // See https://github.com/netty/netty/issues/6507
    private static readonly class NonNotifyRunnable : Runnable {

        private readonly Runnable task;

        NonNotifyRunnable(Runnable task) {
            this.task = task;
        }

        @Override
        public void run() {
            task.run();
        }
    }

    private static readonly class AccountingThreadFactory : ThreadFactory {
        private readonly ThreadFactory delegate;
        private readonly ISet<Thread> threads;

        private AccountingThreadFactory(ThreadFactory delegate, ISet<Thread> threads) {
            this.delegate = delegate;
            this.threads = threads;
        }

        @Override
        public Thread newThread(@NotNull Runnable r) {
            return delegate.newThread(() -> {
                threads.add(Thread.currentThread());
                try {
                    r.run();
                } finally {
                    threads.remove(Thread.currentThread());
                }
            });
        }
    }
}
