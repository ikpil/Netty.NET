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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * {@link EventExecutor} implementation which makes no guarantees about the ordering of task execution that
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
[Obsolete]
public sealed class UnorderedThreadPoolEventExecutor : AbstractExecutorService, IEventExecutor
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(UnorderedThreadPoolEventExecutor));
    private readonly IPromise<Void> termination = GlobalEventExecutor.INSTANCE.newPromise<Void>();
    private readonly ConcurrentDictionary<Thread, byte> eventLoopThreads = new();

    // CLR adaptation of the inherited JDK scheduled pool: dedicated workers share
    // a deadline queue. The Netty decoration remains separate from backend completion.
    private readonly object gate = new();
    private readonly PriorityQueue<Work, (long deadline, long sequence)> queue = new(
        Comparer<(long deadline, long sequence)>.Create((left, right) =>
        {
            long delta = unchecked(left.deadline - right.deadline);
            return delta == 0 ? left.sequence.CompareTo(right.sequence) : delta < 0 ? -1 : 1;
        }));
    private readonly HashSet<Thread> workers = new();
    private readonly HashSet<Thread> activeWorkers = new();
    private int corePoolSize;
    private int maximumPoolSize = int.MaxValue;
    private int startingWorkers;
    private int workerCount;
    private int largestPoolSize;
    private long completedTaskCount;
    private long keepAliveNanos = 10_000_000;
    private bool coreThreadTimeout;
    private long configurationGeneration;
    private bool continuePeriodicAfterShutdown;
    private bool executeDelayedAfterShutdown = true;
    private bool removeOnCancel;
    private IThreadFactory threadFactory;
    private Action<IRunnable, UnorderedThreadPoolEventExecutor> rejectedHandler;
    private bool shutdownRequested;
    private bool stopping;
    private static long nextSequence;
    private readonly QueueView queueView;

    /**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int, ThreadFactory)}
     * using {@link DefaultThreadFactory}.
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize)
        : this(corePoolSize, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor))) { }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory)}
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize, IThreadFactory threadFactory)
        : this(corePoolSize, threadFactory, (_, _) => throw new RejectedExecutionException()) { }

    /**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int,
     * ThreadFactory, java.util.concurrent.RejectedExecutionHandler)} using {@link DefaultThreadFactory}.
     */
    public UnorderedThreadPoolEventExecutor(int corePoolSize, Action<IRunnable, UnorderedThreadPoolEventExecutor> handler)
        : this(corePoolSize, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor)), handler) { }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory, RejectedExecutionHandler)}
     */
    // CLR represents the JDK rejection handler as a delegate; it is not Netty's SingleThread rejection handler.
    public UnorderedThreadPoolEventExecutor(int corePoolSize, IThreadFactory threadFactory,
        Action<IRunnable, UnorderedThreadPoolEventExecutor> handler)
    {
        if (corePoolSize < 0) throw new ArgumentOutOfRangeException(nameof(corePoolSize));
        ArgumentNullException.ThrowIfNull(threadFactory);
        ArgumentNullException.ThrowIfNull(handler);
        this.corePoolSize = corePoolSize;
        this.threadFactory = new AccountingThreadFactory(threadFactory, eventLoopThreads);
        rejectedHandler = handler;
        queueView = new QueueView(this);
    }

    public IEventExecutorGroup parent() => this;
    public IEventExecutor next() => this;
    public IEnumerable<IEventExecutor> iterator() { yield return this; }
    public Ticker ticker() => Ticker.systemTicker();
    public bool inEventLoop() => inEventLoop(Thread.CurrentThread);
    public bool inEventLoop(Thread thread) => thread != null && eventLoopThreads.ContainsKey(thread);
    public bool isExecutorThread(Thread thread) => inEventLoop(thread);
    public bool isShuttingDown() => isShutdown();
    public bool isSuspended() => false;
    public bool trySuspend() => false;
    public IPromise<T> newPromise<T>() => new DefaultPromise<T>(this);
    public IProgressivePromise<T> newProgressivePromise<T>() => new DefaultProgressivePromise<T>(this);
    public IFuture<T> newSucceededFuture<T>(T result) => new SucceededFuture<T>(this, result);
    public IFuture<T> newFailedFuture<T>(Exception cause) => new FailedFuture<T>(this, cause);
    protected override IRunnableFuture<T> newTaskFor<T>(IRunnable runnable, T value) => new JdkFutureTask<T>(this, runnable, value);
    protected override IRunnableFuture<T> newTaskFor<T>(ICallable<T> callable) => new JdkFutureTask<T>(this, callable);
    public override bool isShutdown() { using (UninterruptibleMonitor.enter(gate)) return shutdownRequested; }
    public override bool isTerminated()
    {
        using (UninterruptibleMonitor.enter(gate)) return shutdownRequested && workers.Count == 0 && startingWorkers == 0 && queue.Count == 0;
    }
    public IFuture<Void> terminationFuture() => termination;
    public Task terminationTask() => terminationFuture().Task;
    public Task shutdownGracefullyAsync() => shutdownGracefully().Task;
    public Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => shutdownGracefully(quietPeriod, timeout).Task;
    public IFuture<Void> shutdownGracefully() => shutdownGracefully(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
    public IQueue<IRunnable> getQueue() => queueView;
    public int getCorePoolSize() { using (UninterruptibleMonitor.enter(gate)) return corePoolSize; }

    // CLR TimeSpan/delegate equivalents of inherited JDK pool configuration.
    public void setCorePoolSize(int size)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            if (size < 0 || size > maximumPoolSize) throw new ArgumentOutOfRangeException(nameof(size));
            int delta = size - corePoolSize;
            corePoolSize = size;
            if (workerCount > size) wakeIdleWorkers();
            else if (delta > 0)
            {
                int count = Math.Min(delta, queue.Count);
                while (count-- > 0 && startWorker(corePoolSize))
                    if (queue.Count == 0) break;
            }
        }
    }
    public int getMaximumPoolSize() { using (UninterruptibleMonitor.enter(gate)) return maximumPoolSize; }
    public void setMaximumPoolSize(int size)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            if (size <= 0 || size < corePoolSize) throw new ArgumentOutOfRangeException(nameof(size));
            maximumPoolSize = size;
            if (workerCount > size) wakeIdleWorkers();
        }
    }
    public TimeSpan getKeepAliveTime() { using (UninterruptibleMonitor.enter(gate)) return TimeSpan.FromTicks(keepAliveNanos / 100); }
    public void setKeepAliveTime(TimeSpan time)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            if (time < TimeSpan.Zero || (time == TimeSpan.Zero && coreThreadTimeout)) throw new ArgumentException("invalid keep-alive time", nameof(time));
            long nanos = AbstractScheduledEventExecutor.toNanos(time);
            bool shorter = nanos < keepAliveNanos;
            keepAliveNanos = nanos;
            if (shorter) wakeIdleWorkers();
        }
    }
    public bool allowsCoreThreadTimeOut() { using (UninterruptibleMonitor.enter(gate)) return coreThreadTimeout; }
    public void allowCoreThreadTimeOut(bool value)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            if (value && keepAliveNanos <= 0) throw new ArgumentException("core threads must have a positive keep-alive time");
            if (value != coreThreadTimeout)
            {
                coreThreadTimeout = value;
                if (value) wakeIdleWorkers();
            }
        }
    }
    public bool prestartCoreThread() { using (UninterruptibleMonitor.enter(gate)) return startWorker(corePoolSize); }
    public int prestartAllCoreThreads()
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            int count = 0;
            while (startWorker(corePoolSize)) ++count;
            return count;
        }
    }
    public int getPoolSize() { using (UninterruptibleMonitor.enter(gate)) return workers.Count; }
    public int getActiveCount() { using (UninterruptibleMonitor.enter(gate)) return activeWorkers.Count; }
    public int getLargestPoolSize() { using (UninterruptibleMonitor.enter(gate)) return largestPoolSize; }
    public long getCompletedTaskCount() { using (UninterruptibleMonitor.enter(gate)) return completedTaskCount; }
    public long getTaskCount() { using (UninterruptibleMonitor.enter(gate)) return unchecked(completedTaskCount + activeWorkers.Count + queue.Count); }
    public bool isTerminating() { using (UninterruptibleMonitor.enter(gate)) return shutdownRequested && !isTerminated(); }
    public IThreadFactory getThreadFactory() { using (UninterruptibleMonitor.enter(gate)) return threadFactory; }
    public void setThreadFactory(IThreadFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using (UninterruptibleMonitor.enter(gate)) threadFactory = factory;
    }
    public Action<IRunnable, UnorderedThreadPoolEventExecutor> getRejectedExecutionHandler()
    { using (UninterruptibleMonitor.enter(gate)) return rejectedHandler; }
    public void setRejectedExecutionHandler(Action<IRunnable, UnorderedThreadPoolEventExecutor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        using (UninterruptibleMonitor.enter(gate)) rejectedHandler = handler;
    }
    public bool getContinueExistingPeriodicTasksAfterShutdownPolicy()
    { using (UninterruptibleMonitor.enter(gate)) return continuePeriodicAfterShutdown; }
    public void setContinueExistingPeriodicTasksAfterShutdownPolicy(bool value)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            continuePeriodicAfterShutdown = value;
            if (!value && shutdownRequested) onShutdown();
        }
    }
    public bool getExecuteExistingDelayedTasksAfterShutdownPolicy()
    { using (UninterruptibleMonitor.enter(gate)) return executeDelayedAfterShutdown; }
    public void setExecuteExistingDelayedTasksAfterShutdownPolicy(bool value)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            executeDelayedAfterShutdown = value;
            if (!value && shutdownRequested) onShutdown();
        }
    }
    public bool getRemoveOnCancelPolicy() { using (UninterruptibleMonitor.enter(gate)) return removeOnCancel; }
    public void setRemoveOnCancelPolicy(bool value) { using (UninterruptibleMonitor.enter(gate)) removeOnCancel = value; }
    public bool remove(IRunnable task) => queueView.tryRemove(task);
    public void purge()
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            foreach (var work in queue.UnorderedItems.Select(item => item.Element).ToArray())
                if (work.isCancelled()) remove(work);
            Monitor.PulseAll(gate);
        }
    }
    private void wakeIdleWorkers()
    {
        ++configurationGeneration;
        Monitor.PulseAll(gate);
    }

    public override List<IRunnable> shutdownNow()
    {
        List<IRunnable> tasks;
        using (UninterruptibleMonitor.enter(gate))
        {
            shutdownRequested = stopping = true;
            tasks = queue.UnorderedItems.Select(item => item.Element.outer).ToList();
            queue.Clear();
            foreach (var worker in workers) worker.Interrupt();
            Monitor.PulseAll(gate);
        }
        termination.trySuccess(null);
        return tasks;
    }

    public override void shutdown()
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            shutdownRequested = true;
            onShutdown();
        }
        // Upstream completes this promise when shutdown is requested, before the pool has terminated.
        termination.trySuccess(null);
    }

    public IFuture<Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout)
    {
        // TODO: At the moment this just calls shutdown but we may be able to do something more smart here which
        //       respects the quietPeriod and timeout.
        shutdown();
        return terminationFuture();
    }

    public override bool awaitTermination(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return isTerminated();
        var elapsed = Stopwatch.StartNew();
        using (UninterruptibleMonitor.enter(gate))
        {
            while (!shutdownRequested || workers.Count != 0 || startingWorkers != 0 || queue.Count != 0)
            {
                TimeSpan remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero) return false;
                int milliseconds = (int)Math.Min(int.MaxValue,
                    (remaining.Ticks - 1) / TimeSpan.TicksPerMillisecond + 1);
                Monitor.Wait(gate, milliseconds);
            }
            return true;
        }
    }

    public override IFuture<Void> submit(IRunnable task) => (IFuture<Void>)schedule(task, TimeSpan.Zero);
    public override IFuture<T> submit<T>(IRunnable task, T result)
    {
        ArgumentNullException.ThrowIfNull(task);
        // JDK submit(runnable, result) adapts to a Callable, so decoration must retain its result.
        return schedule(new AnonymousCallable<T>(() => { task.run(); return result; }), TimeSpan.Zero);
    }
    public override IFuture<T> submit<T>(ICallable<T> task) => schedule(task, TimeSpan.Zero);

    public IScheduledTask schedule(IRunnable command, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(command);
        return schedule(new Backend<Void>(this, command, delay, 0), false);
    }
    public IScheduledTask<T> schedule<T>(ICallable<T> callable, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(callable);
        return schedule(new Backend<T>(this, callable, delay), true);
    }
    public IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (period <= TimeSpan.Zero) throw new ArgumentException("period must be positive", nameof(period));
        return schedule(new Backend<Void>(this, command, initialDelay, AbstractScheduledEventExecutor.toNanos(period)), false);
    }
    public IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (delay <= TimeSpan.Zero) throw new ArgumentException("delay must be positive", nameof(delay));
        return schedule(new Backend<Void>(this, command, initialDelay, -AbstractScheduledEventExecutor.toNanos(delay)), false);
    }
    private ScheduledFutureTask<T> schedule<T>(Backend<T> backend, bool wasCallable)
    {
        var decorated = new ScheduledFutureTask<T>(this, backend, wasCallable);
        backend.outer = decorated;
        enqueue(backend);
        return decorated;
    }

    public override void execute(IRunnable command)
    {
        ArgumentNullException.ThrowIfNull(command);
        enqueue(new Backend<Void>(this, new NonNotifyRunnable(command), TimeSpan.Zero, 0));
    }

    private void enqueue(Work work)
    {
        Action<IRunnable, UnorderedThreadPoolEventExecutor> handler;
        using (UninterruptibleMonitor.enter(gate))
        {
            if (!shutdownRequested)
            {
                queue.Enqueue(work, (work.deadline, work.sequence));
                ensureWorker();
                Monitor.PulseAll(gate);
                return;
            }
            handler = rejectedHandler;
        }
        handler(work.outer, this);
    }

    private void ensureWorker()
    {
        startWorker(Math.Max(1, corePoolSize));
    }
    private bool startWorker(int limit)
    {
        if (stopping || (shutdownRequested && queue.Count == 0) || workerCount >= limit) return false;
        ++startingWorkers;
        ++workerCount;
        bool started = false;
        try
        {
            Thread thread = threadFactory.newThread(Runnables.Create(workerLoop));
            if (thread == null || stopping) return false;
            workers.Add(thread);
            largestPoolSize = Math.Max(largestPoolSize, workers.Count);
            try { thread.Start(); }
            catch { workers.Remove(thread); throw; }
            started = true;
            return true;
        }
        finally { --startingWorkers; if (!started) --workerCount; Monitor.PulseAll(gate); }
    }

    private Work takeWork()
    {
        bool timedOut = false;
        bool idleStarted = false;
        long idleDeadline = 0;
        long generation;
        using (UninterruptibleMonitor.enter(gate))
        {
            generation = configurationGeneration;
            for (;;)
            {
                if (generation != configurationGeneration)
                {
                    generation = configurationGeneration;
                    timedOut = idleStarted = false;
                }
                if (stopping || (shutdownRequested && queue.Count == 0))
                {
                    --workerCount;
                    return null;
                }
                bool timed = coreThreadTimeout || workerCount > corePoolSize;
                if ((workerCount > maximumPoolSize || (timed && timedOut)) && (workerCount > 1 || queue.Count == 0))
                {
                    --workerCount;
                    return null;
                }
                long now = ticker().nanoTime();
                long waitNanos = long.MaxValue;
                bool hasHead = queue.TryPeek(out Work work, out _);
                if (hasHead)
                {
                    waitNanos = unchecked(work.deadline - now);
                    if (waitNanos <= 0)
                    {
                        queue.Dequeue();
                        activeWorkers.Add(Thread.CurrentThread);
                        return work;
                    }
                }
                if (timed)
                {
                    if (!idleStarted)
                    {
                        idleDeadline = unchecked(now + keepAliveNanos);
                        idleStarted = true;
                    }
                    long remaining = unchecked(idleDeadline - now);
                    if (remaining <= 0)
                    {
                        timedOut = true;
                        idleStarted = false;
                        try { Monitor.Wait(gate, 0); }
                        catch (ThreadInterruptedException) { timedOut = false; }
                        continue;
                    }
                    waitNanos = Math.Min(waitNanos, remaining);
                }
                else
                {
                    timedOut = idleStarted = false;
                }
                try
                {
                    if (!timed && !hasHead) Monitor.Wait(gate);
                    else Monitor.Wait(gate, (int)Math.Min(int.MaxValue, (waitNanos - 1) / 1_000_000 + 1));
                    if (timed && unchecked(idleDeadline - ticker().nanoTime()) <= 0)
                    {
                        timedOut = true;
                        idleStarted = false;
                    }
                }
                catch (ThreadInterruptedException) { timedOut = idleStarted = false; }
            }
        }
    }

    private void workerLoop()
    {
        bool countReleased = false;
        bool abrupt = false;
        try
        {
            for (;;)
            {
                Work work = takeWork();
                if (work == null) { countReleased = true; return; }
                try
                {
                    // JDK workers clear an old interrupt before ordinary work and retain it for shutdownNow.
                    try { Thread.Sleep(0); }
                    catch (ThreadInterruptedException) { }
                    if (Volatile.Read(ref stopping)) Thread.CurrentThread.Interrupt();
                    work.outer.run();
                }
                catch (Exception failure)
                {
                    // CLR unhandled exceptions kill the process. Match JDK worker replacement instead.
                    abrupt = true;
                    logger.warn("Unexpected worker failure", failure);
                    return;
                }
                finally
                {
                    using (UninterruptibleMonitor.enter(gate))
                    {
                        activeWorkers.Remove(Thread.CurrentThread);
                        ++completedTaskCount;
                        Monitor.PulseAll(gate);
                    }
                }
            }
        }
        finally
        {
            using (UninterruptibleMonitor.enter(gate))
            {
                if (!countReleased) --workerCount;
                workers.Remove(Thread.CurrentThread);
                if (!stopping)
                {
                    int minimum = coreThreadTimeout ? 0 : corePoolSize;
                    if (minimum == 0 && queue.Count != 0) minimum = 1;
                    if (abrupt) startWorker(maximumPoolSize);
                    else if (workerCount < minimum) startWorker(Math.Max(1, corePoolSize));
                }
                Monitor.PulseAll(gate);
            }
        }
    }
    private bool canRun(Work work)
    {
        using (UninterruptibleMonitor.enter(gate))
            return !stopping && (!shutdownRequested || (work.period != 0 ? continuePeriodicAfterShutdown :
                executeDelayedAfterShutdown || unchecked(work.deadline - ticker().nanoTime()) <= 0));
    }
    private void reExecutePeriodic(Work work)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            if (!stopping && (!shutdownRequested || continuePeriodicAfterShutdown))
            {
                queue.Enqueue(work, (work.deadline, work.sequence));
                ensureWorker();
                Monitor.PulseAll(gate);
                return;
            }
        }
        work.cancelOuter();
    }
    private void onShutdown()
    {
        foreach (var work in queue.UnorderedItems.Select(item => item.Element).ToArray())
        {
            if ((work.period != 0 ? !continuePeriodicAfterShutdown :
                !executeDelayedAfterShutdown && unchecked(work.deadline - ticker().nanoTime()) > 0) || work.isCancelled())
            {
                remove(work);
                work.cancelOuter();
            }
        }
        Monitor.PulseAll(gate);
    }
    private bool remove(Work work)
    {
        var items = queue.UnorderedItems.ToArray();
        if (!items.Any(item => ReferenceEquals(item.Element, work))) return false;
        queue.Clear();
        foreach (var item in items)
            if (!ReferenceEquals(item.Element, work)) queue.Enqueue(item.Element, item.Priority);
        return true;
    }

    // JDK deadlines wrap with nanoTime. Keep the distance from an already overdue
    // queue head within Int64.MaxValue so signed-difference comparison remains valid.
    private long triggerTime(long delay)
    {
        delay = Math.Max(0, delay);
        using (UninterruptibleMonitor.enter(gate))
        {
            long now = ticker().nanoTime();
            if (delay >= (long.MaxValue >> 1) && queue.TryPeek(out Work head, out _))
            {
                long headDelay = unchecked(head.deadline - now);
                if (headDelay < 0 && unchecked(delay - headDelay) < 0)
                    delay = long.MaxValue + headDelay;
            }
            return unchecked(now + delay);
        }
    }

    private interface Work : IRunnable
    {
        UnorderedThreadPoolEventExecutor owner { get; }
        long sequence { get; }
        long period { get; }
        long deadline { get; set; }
        IRunnable outer { get; set; }
        bool isCancelled();
        void cancelOuter();
    }

    private interface WorkProvider
    {
        Work backend();
    }

    // The inherited native FutureTask captures failures separately from the Netty
    // outer promise. Raw execute() queue entries also expose cancellable Future status.
    private sealed class Backend<T> : JdkFutureTask<T>, Work
    {
        public UnorderedThreadPoolEventExecutor owner { get; }
        public long sequence { get; }
        public long period { get; }
        public long deadline { get; set; }
        public IRunnable outer { get; set; }

        internal Backend(UnorderedThreadPoolEventExecutor executor, IRunnable task, TimeSpan delay, long period)
            : base(executor, task, default)
        {
            owner = executor;
            this.period = period;
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            deadline = owner.triggerTime(AbstractScheduledEventExecutor.toNanos(delay));
            outer = this;
        }
        internal Backend(UnorderedThreadPoolEventExecutor executor, ICallable<T> task, TimeSpan delay)
            : base(executor, task)
        {
            owner = executor;
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            deadline = owner.triggerTime(AbstractScheduledEventExecutor.toNanos(delay));
            outer = this;
        }
        bool Work.isCancelled() => outer is IFuture future && future.isCancelled();
        public void cancelOuter() => ((IFuture)outer).cancel(false);
        public override bool cancel(bool mayInterruptIfRunning)
        {
            bool cancelled = base.cancel(mayInterruptIfRunning);
            if (cancelled)
            {
                using (UninterruptibleMonitor.enter(owner.gate))
                {
                    // JDK remove-on-cancel operates on the inner task. A decorated Netty
                    // promise is a different queue element, so it is not removed here.
                    if (owner.removeOnCancel && ReferenceEquals(outer, this))
                    {
                        owner.remove(this);
                        Monitor.PulseAll(owner.gate);
                    }
                }
            }
            return cancelled;
        }
        public override void run()
        {
            if (!owner.canRun(this)) { cancel(false); return; }
            if (period == 0) base.run();
            else if (runAndReset())
            {
                deadline = period > 0 ? unchecked(deadline + period) : owner.triggerTime(-period);
                owner.reExecutePeriodic(this);
            }
        }
    }
    private sealed class ScheduledFutureTask<T> : PromiseTask<T>, IScheduledTask<T>, WorkProvider
    {
        private readonly Backend<T> future;
        private readonly bool wasCallable;
        internal ScheduledFutureTask(UnorderedThreadPoolEventExecutor executor, Backend<T> future, bool wasCallable)
            : base(executor, future) { this.future = future; this.wasCallable = wasCallable; }
        internal override T runTask()
        {
            T result = base.runTask();
            if (wasCallable)
            {
                // If this RunnableScheduledFutureTask wraps a RunnableScheduledFuture that wraps a Callable we need
                // to ensure that we return the correct result by calling future.get().
                //
                // See https://github.com/netty/netty/issues/11072
                // CLR: base.runTask() yields default(T), including non-nullable value types.
                try { return future.get(); }
                catch (AggregateException e)
                {
                    // unwrap exception.
                    PlatformDependent.throwException(e.InnerException);
                    throw;
                }
            }
            return result;
        }
        public override void run()
        {
            if (!isPeriodic()) base.run();
            else if (!isDone())
            {
                try
                {
                    // Its a periodic task so we need to ignore the return value
                    runTask();
                }
                catch (Exception cause)
                {
                    if (!tryFailureInternal(cause)) logger.warn("Failure during execution of task", cause);
                }
            }
        }
        Work WorkProvider.backend() => future;
        public bool isPeriodic() => future.period != 0;
        public long getDelay() => unchecked(future.deadline - future.owner.ticker().nanoTime());
        public long deadlineNanos() => future.deadline;
        public long delayNanos() => Math.Max(0, getDelay());
        public long delayNanos(long nanos) => Math.Max(0, unchecked(future.deadline - nanos));
        public long getId() => future.sequence;
        public IScheduledTask setId(long id) => this;
        public void setConsumed() { }
        public bool cancel() => cancel(false);
        public bool cancelWithoutRemove(bool mayInterruptIfRunning) => cancel(mayInterruptIfRunning);
        public int CompareTo(IScheduledTask other)
        {
            if (ReferenceEquals(this, other)) return 0;
            long delta = unchecked(deadlineNanos() - other.deadlineNanos());
            int order = delta == 0 ? 0 : delta < 0 ? -1 : 1;
            return order != 0 ? order : getId().CompareTo(other.getId());
        }
        public T Result => get();
        public Task<T> Completion => Task;
        public TaskAwaiter<T> GetAwaiter() => Task.GetAwaiter();
    }

    // This is a special wrapper which we will be used in execute(...) to wrap the submitted Runnable. This is needed as
    // ScheduledThreadPoolExecutor.execute(...) will delegate to submit(...) which will then use decorateTask(...).
    // The problem with this is that decorateTask(...) needs to ensure we only do our own decoration if we not call
    // from execute(...) as otherwise we may end up creating an endless loop because DefaultPromise will call
    // EventExecutor.execute(...) when notify the listeners of the promise.
    //
    // See https://github.com/netty/netty/issues/6507
    private sealed class NonNotifyRunnable : IRunnable
    {
        private readonly IRunnable task;
        internal NonNotifyRunnable(IRunnable task) => this.task = task;
        public void run() => task.run();
    }

    private sealed class AccountingThreadFactory : IThreadFactory
    {
        private readonly IThreadFactory factory;
        private readonly ConcurrentDictionary<Thread, byte> threads;
        internal AccountingThreadFactory(IThreadFactory factory, ConcurrentDictionary<Thread, byte> threads)
        { this.factory = factory; this.threads = threads; }
        public Thread newThread(IRunnable task) => factory.newThread(Runnables.Create(() =>
        {
            threads.TryAdd(Thread.CurrentThread, 0);
            try { task.run(); }
            finally { threads.TryRemove(Thread.CurrentThread, out _); }
        }));
    }

    private sealed class QueueView : IQueue<IRunnable>
    {
        private readonly UnorderedThreadPoolEventExecutor executor;
        internal QueueView(UnorderedThreadPoolEventExecutor executor) => this.executor = executor;
        public int Count { get { using (UninterruptibleMonitor.enter(executor.gate)) return executor.queue.Count; } }
        public bool isEmpty() => Count == 0;
        public bool tryEnqueue(IRunnable item)
        {
            ArgumentNullException.ThrowIfNull(item);
            Work work = item as Work ?? (item as WorkProvider)?.backend();
            if (work == null) throw new ArgumentException("A scheduled future is required.", nameof(item));
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                executor.queue.Enqueue(work, (work.deadline, work.sequence));
                Monitor.PulseAll(executor.gate);
                return true;
            }
        }
        public bool tryDequeue(out IRunnable item)
        {
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                if (executor.queue.TryPeek(out Work work, out _) && unchecked(work.deadline - executor.ticker().nanoTime()) <= 0)
                { item = executor.queue.Dequeue().outer; return true; }
                item = null; return false;
            }
        }
        public bool tryPeek(out IRunnable item)
        {
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                if (executor.queue.TryPeek(out Work work, out _)) { item = work.outer; return true; }
                item = null; return false;
            }
        }
        public bool tryRemove(IRunnable item)
        {
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                var work = executor.queue.UnorderedItems.Select(entry => entry.Element).FirstOrDefault(w => ReferenceEquals(w.outer, item));
                if (work == null) return false;
                bool removed = executor.remove(work);
                Monitor.PulseAll(executor.gate);
                return removed;
            }
        }
        public void clear()
        {
            using (UninterruptibleMonitor.enter(executor.gate)) { executor.queue.Clear(); Monitor.PulseAll(executor.gate); }
        }
        public int drain(IConsumer<IRunnable> consumer, int limit)
        {
            ArgumentNullException.ThrowIfNull(consumer);
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            int drained = 0;
            while (drained < limit && tryDequeue(out var item)) { consumer.accept(item); ++drained; }
            return drained;
        }
    }
}
