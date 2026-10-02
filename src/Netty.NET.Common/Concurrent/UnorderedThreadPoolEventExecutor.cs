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
public sealed class UnorderedThreadPoolEventExecutor : IEventExecutor
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(UnorderedThreadPoolEventExecutor));
    private readonly TaskCompletionSource termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<Thread, byte> eventLoopThreads = new();

    // CLR adaptation of the inherited JDK scheduled pool: dedicated workers share
    // a deadline queue. Native reservations own their sole Task result; raw work has no result facade.
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
        this.threadFactory = threadFactory;
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
    public bool isShutdown() { using (UninterruptibleMonitor.enter(gate)) return shutdownRequested; }
    public bool isTerminated()
    {
        using (UninterruptibleMonitor.enter(gate)) return shutdownRequested && workers.Count == 0 && startingWorkers == 0 && queue.Count == 0;
    }
    // All callers hold gate. Async continuations cannot run inline while pool
    // state is being published. A start reservation counts even before its
    // factory returns a Thread; shutdown cannot finish underneath that factory.
    private void PublishPoolState()
    {
        Monitor.PulseAll(gate);
        if (shutdownRequested && workers.Count == 0 && startingWorkers == 0 && queue.Count == 0)
            termination.TrySetResult();
    }
    public Task Termination => termination.Task;
    public Task ShutdownGracefullyAsync() => ShutdownGracefullyAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
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
    public bool remove(IRunnable task) => queueView.tryRemove(task);
    private void wakeIdleWorkers()
    {
        ++configurationGeneration;
        PublishPoolState();
    }

    public List<IRunnable> shutdownNow()
    {
        List<IRunnable> tasks;
        using (UninterruptibleMonitor.enter(gate))
        {
            shutdownRequested = stopping = true;
            tasks = queue.UnorderedItems.Select(item => item.Element.outer).ToList();
            // Queue handles are membership, not results. Every removed reservation
            // settles cancellation or releases its raw callback before termination.
            foreach (var item in queue.UnorderedItems) item.Element.cancelOuter();
            queue.Clear();
            foreach (var worker in workers) worker.Interrupt();
            PublishPoolState();
        }
        return tasks;
    }

    public void shutdown()
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            shutdownRequested = true;
            onShutdown();
        }
        // Upstream completes this promise when shutdown is requested, before the pool has terminated.
        // CLR Termination instead follows the drained queue and released worker reservations.
    }

    public Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        // TODO: At the moment this just calls shutdown but we may be able to do something more smart here which
        //       respects the quietPeriod and timeout.
        shutdown();
        return Termination;
    }

    public bool awaitTermination(TimeSpan timeout)
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

    public void execute(IRunnable command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is INativeSubmission native)
        {
            ExecuteNativeSubmission(native);
            return;
        }
        var work = new RawWork(this, command);
        try { enqueue(work); }
        catch
        {
            // Throwing admission must not execute the callback later after a retry.
            using (UninterruptibleMonitor.enter(gate)) { remove(work); PublishPoolState(); }
            work.cancelOuter();
            throw;
        }
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
                PublishPoolState();
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
        finally { --startingWorkers; if (!started) --workerCount; PublishPoolState(); }
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
        // Worker identity belongs to the loop, not to whichever factory created
        // its Thread. Factory replacement must not bypass native callback affinity.
        eventLoopThreads.TryAdd(Thread.CurrentThread, 0);
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
                        PublishPoolState();
                    }
                }
            }
        }
        finally
        {
            try
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
                    PublishPoolState();
                }
            }
            finally { eventLoopThreads.TryRemove(Thread.CurrentThread, out _); }
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
                PublishPoolState();
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
        PublishPoolState();
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
        long deadline { get; }
        IRunnable outer { get; }
        bool isCancelled();
        void cancelOuter();
    }

    internal void ExecuteNativeSubmission(INativeSubmission submission)
    {
        var backend = new NativeSubmissionBackend(this, submission);
        using (UninterruptibleMonitor.enter(gate))
        {
            // A discard handler must not leave the producer-owned Task pending.
            if (shutdownRequested) throw new RejectedExecutionException("Executor has been shut down.");
            if (submission.IsCanceled) return;
            try { enqueue(backend); }
            catch { remove(backend); throw; }
        }
    }

    internal bool IsImmediateShutdownRequested => Volatile.Read(ref stopping);

    // Submission already has its sole TCS result. This queue entry carries only
    // membership and a shutdown hook without an additional result owner.
    private sealed class NativeSubmissionBackend : Work
    {
        private INativeSubmission _submission;
        public UnorderedThreadPoolEventExecutor owner { get; }
        public long sequence { get; }
        public long period => 0;
        public long deadline { get; }
        public IRunnable outer => this;
        internal NativeSubmissionBackend(UnorderedThreadPoolEventExecutor owner, INativeSubmission submission)
        {
            this.owner = owner;
            _submission = submission;
            if (submission is IQueueBoundNativeSubmission runner) runner.BindQueueOwner(owner);
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            deadline = owner.triggerTime(0);
        }
        public bool isCancelled() => Volatile.Read(ref _submission)?.IsCanceled ?? true;
        public void cancelOuter() => Interlocked.Exchange(ref _submission, null)?.CancelForShutdown();
        public void run()
        {
            INativeSubmission submission = Interlocked.Exchange(ref _submission, null);
            if (submission == null) return;
            if (!owner.canRun(this)) submission.CancelForShutdown();
            else submission.run();
        }
    }

    internal Task<T> ScheduleNative<T>(Func<CancellationToken, T> function, TimeSpan delay, long period,
        CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
        NativeBackend backend = null;
        var task = new NativeScheduledWork<T>(function, token, triggerTime(AbstractScheduledEventExecutor.toNanos(delay)),
            period, () => ticker().nanoTime(), () => canRun(backend),
            work =>
            {
                using (UninterruptibleMonitor.enter(gate))
                {
                    if (work.Completion.IsCompleted) return;
                    try { reExecutePeriodic(backend); }
                    catch { remove(backend); throw; }
                }
            },
            work =>
            {
                using (UninterruptibleMonitor.enter(gate))
                {
                    remove(backend);
                    PublishPoolState();
                }
            });
        backend = new NativeBackend(this, task);
        try
        {
            // Native result publication must not depend on a JDK rejection handler
            // silently discarding work. It faults if shutdown prevents admission.
            using (UninterruptibleMonitor.enter(gate))
            {
                if (shutdownRequested) throw new RejectedExecutionException("Executor has been shut down.");
                if (!task.Completion.IsCompleted) enqueue(backend);
            }
        }
        catch (Exception error)
        {
            using (UninterruptibleMonitor.enter(gate)) { remove(backend); PublishPoolState(); }
            task.Reject(error);
        }
        task.Publish();
        return task.ResultTask;
    }

    // One native Task result owner; this adapter supplies membership in the pool's
    // existing deadline queue. Metadata does not implement the old Future API.
    private sealed class NativeBackend : Work, IScheduledWork
    {
        private readonly ITaskScheduledWork _task;
        public UnorderedThreadPoolEventExecutor owner { get; }
        public long sequence { get; }
        public long period => _task.PeriodNanos;
        public long deadline => _task.deadlineNanos();
        public IRunnable outer => this;
        internal NativeBackend(UnorderedThreadPoolEventExecutor owner, ITaskScheduledWork task)
        {
            this.owner = owner;
            _task = task;
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            task.AssignId(sequence);
        }
        public bool isCancelled() => _task.IsCanceled;
        public void cancelOuter() => _task.CancelForShutdown();
        public void run() => _task.run();
        public bool IsCanceled => _task.IsCanceled;
        public void CancelForShutdown() => cancelOuter();
        public long deadlineNanos() => deadline;
        public long delayNanos(long now) => _task.delayNanos(now);
        public long delayNanos() => _task.delayNanos();
        public long getId() => sequence;
        public void AssignId(long id)
        {
            if (id != sequence) throw new InvalidOperationException("The pool owns the queue sequence.");
        }
        public void setConsumed() => _task.setConsumed();
    }

    // Raw execute owns no asynchronous result. Its queue entry claims and releases
    // the callback once, including discard during shutdown or clearing.
    private sealed class RawWork : Work
    {
        private IRunnable command;
        public UnorderedThreadPoolEventExecutor owner { get; }
        public long sequence { get; }
        public long period => 0;
        public long deadline { get; }
        public IRunnable outer => this;
        internal RawWork(UnorderedThreadPoolEventExecutor owner, IRunnable command)
        {
            this.owner = owner;
            this.command = command;
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            deadline = owner.triggerTime(0);
        }
        public bool isCancelled() => Volatile.Read(ref command) == null;
        public void cancelOuter() => Interlocked.Exchange(ref command, null);
        public void run()
        {
            IRunnable callback = Interlocked.Exchange(ref command, null);
            if (callback == null || !owner.canRun(this)) return;
            try { callback.run(); }
            catch (Exception failure) { logger.warn("Failure during execution of task", failure); }
        }
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
            Work work = item as Work;
            if (work == null || !ReferenceEquals(work.owner, executor))
                throw new ArgumentException("A queue reservation owned by this executor is required.", nameof(item));
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                if (executor.termination.Task.IsCompleted) return false;
                executor.queue.Enqueue(work, (work.deadline, work.sequence));
                executor.PublishPoolState();
                return true;
            }
        }
        public bool tryDequeue(out IRunnable item)
        {
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                if (executor.queue.TryPeek(out Work work, out _) && unchecked(work.deadline - executor.ticker().nanoTime()) <= 0)
                { item = executor.queue.Dequeue().outer; executor.PublishPoolState(); return true; }
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
                executor.PublishPoolState();
                return removed;
            }
        }
        public void clear()
        {
            using (UninterruptibleMonitor.enter(executor.gate))
            {
                // Clearing transfers no handles to a caller. Native reservations
                // must settle before the empty queue can complete Termination.
                foreach (var work in executor.queue.UnorderedItems.Select(item => item.Element).ToArray())
                    work.cancelOuter();
                executor.queue.Clear();
                executor.PublishPoolState();
            }
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
