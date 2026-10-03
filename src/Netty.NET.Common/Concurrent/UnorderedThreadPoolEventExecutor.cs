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
    private readonly int configuredWorkerCount;
    private int startingWorkers;
    private int workerCount;
    private const long ZeroWorkerIdleNanos = 10_000_000;
    private readonly IThreadFactory threadFactory;
    private readonly Action<IRunnable, UnorderedThreadPoolEventExecutor> rejectedHandler;
    private bool gracefulRequested;
    private long gracefulStartNanos;
    private long gracefulActivityNanos;
    private long gracefulQuietNanos;
    private long gracefulTimeoutNanos;
    private Timer gracefulTimer;
    private bool shutdownRequested;
    private bool stopping;
    private static long nextSequence;

    /**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int, ThreadFactory)}
     * using {@link DefaultThreadFactory}.
     */
    /// <remarks>Workers start on admission. Zero uses one transient worker while reservations remain.</remarks>
    public UnorderedThreadPoolEventExecutor(int workerCount)
        : this(workerCount, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor))) { }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory)}
     */
    public UnorderedThreadPoolEventExecutor(int workerCount, IThreadFactory threadFactory)
        : this(workerCount, threadFactory, (_, _) => throw new RejectedExecutionException()) { }

    /**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int,
     * ThreadFactory, java.util.concurrent.RejectedExecutionHandler)} using {@link DefaultThreadFactory}.
     */
    public UnorderedThreadPoolEventExecutor(int workerCount, Action<IRunnable, UnorderedThreadPoolEventExecutor> handler)
        : this(workerCount, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor)), handler) { }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory, RejectedExecutionHandler)}
     */
    // CLR represents the JDK rejection handler as a delegate; it is not Netty's SingleThread rejection handler.
    public UnorderedThreadPoolEventExecutor(int workerCount, IThreadFactory threadFactory,
        Action<IRunnable, UnorderedThreadPoolEventExecutor> handler)
    {
        if (workerCount < 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        ArgumentNullException.ThrowIfNull(threadFactory);
        ArgumentNullException.ThrowIfNull(handler);
        configuredWorkerCount = workerCount;
        this.threadFactory = threadFactory;
        rejectedHandler = handler;
    }

    public IEventExecutorGroup parent() => this;
    public IEventExecutor next() => this;
    public IEnumerable<IEventExecutor> iterator() { yield return this; }
    public Ticker ticker() => Ticker.systemTicker();
    public bool inEventLoop() => inEventLoop(Thread.CurrentThread);
    public bool inEventLoop(Thread thread) => thread != null && eventLoopThreads.ContainsKey(thread);
    public bool isExecutorThread(Thread thread) => inEventLoop(thread);
    public bool isShuttingDown() { using (UninterruptibleMonitor.enter(gate)) return gracefulRequested || shutdownRequested; }
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
        AdvanceGracefulShutdown();
        Monitor.PulseAll(gate);
        if (shutdownRequested && workers.Count == 0 && startingWorkers == 0 && queue.Count == 0)
            termination.TrySetResult();
    }
    public Task Termination => termination.Task;
    public Task ShutdownGracefullyAsync() => ShutdownGracefullyAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
    /// <summary>Gets the number of pending invocation reservations, including future deadlines.</summary>
    /// <remarks>
    /// This is a momentary observation under the pool gate, not an admission guarantee.
    /// Running invocations are excluded. CancellationToken ownership removes native work;
    /// callers cannot mutate the executor's queue or transfer reservations between pools.
    /// </remarks>
    public int PendingTaskCount { get { using (UninterruptibleMonitor.enter(gate)) return queue.Count; } }
    /// <summary>Gets the number of worker threads currently owned by this pool, including retiring loops.</summary>
    public int WorkerCount { get { using (UninterruptibleMonitor.enter(gate)) return workers.Count; } }
    /// <summary>Gets the number of currently claimed invocations; yielded asynchronous bodies are caller-owned.</summary>
    public int ActiveWorkerCount { get { using (UninterruptibleMonitor.enter(gate)) return activeWorkers.Count; } }

    public List<IRunnable> shutdownNow()
    {
        List<IRunnable> tasks;
        using (UninterruptibleMonitor.enter(gate))
        {
            shutdownRequested = stopping = true;
            DisposeGracefulTimer();
            Work[] pending = queue.UnorderedItems.Select(item => item.Element).ToArray();
            tasks = pending.Select(work => work.outer).ToList();
            // Queue handles are membership, not results. Every removed reservation
            // settles cancellation or releases its raw callback before termination.
            // Canceling a submission can remove itself through its membership
            // hook. Iterate the owned snapshot rather than a live BCL enumerator.
            foreach (Work work in pending) work.cancelOuter();
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
            CloseAdmission();
        }
        // Upstream completes this promise when shutdown is requested, before the pool has terminated.
        // CLR Termination instead follows the drained queue and released worker reservations.
    }

    public Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        // TODO: At the moment this just calls shutdown but we may be able to do something more smart here which
        //       respects the quietPeriod and timeout.
        // Upstream TODO preserved above. CLR implements the EventExecutorGroup
        // contract: admission remains open until quiet or timeout, then work drains.
        if (quietPeriod < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        if (timeout < quietPeriod) throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be at least the quiet period.");
        using (UninterruptibleMonitor.enter(gate))
        {
            if (!gracefulRequested && !shutdownRequested)
            {
                gracefulStartNanos = gracefulActivityNanos = ticker().nanoTime();
                gracefulQuietNanos = AbstractScheduledEventExecutor.toNanos(quietPeriod);
                gracefulTimeoutNanos = AbstractScheduledEventExecutor.toNanos(timeout);
                // A lifecycle timer must work even with no worker (or a factory
                // returning null), without retaining the caller's ExecutionContext.
                using (ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow())
                    gracefulTimer = new Timer(static state => ((UnorderedThreadPoolEventExecutor)state).CheckGracefulShutdown(),
                        this, Timeout.Infinite, Timeout.Infinite);
                gracefulRequested = true;
                PublishPoolState();
            }
        }
        return Termination;
    }

    private void CheckGracefulShutdown()
    {
        using (UninterruptibleMonitor.enter(gate)) PublishPoolState();
    }

    // All lifecycle decisions and admission share gate. Timer lateness cannot
    // extend admission: each submission checks the monotonic deadline itself.
    private void AdvanceGracefulShutdown()
    {
        if (!gracefulRequested || shutdownRequested) return;
        long now = ticker().nanoTime();
        long timeoutRemaining = gracefulTimeoutNanos - unchecked(now - gracefulStartNanos);
        long quietRemaining = gracefulQuietNanos - unchecked(now - gracefulActivityNanos);
        if (timeoutRemaining <= 0 || gracefulQuietNanos == 0 ||
            (quietRemaining <= 0 && activeWorkers.Count == 0 && startingWorkers == 0))
        {
            CloseAdmission();
            return;
        }
        long remaining = activeWorkers.Count != 0 || startingWorkers != 0 ? timeoutRemaining :
            Math.Min(timeoutRemaining, quietRemaining);
        // Timer's millisecond range is smaller than TimeSpan's; long periods
        // are checked in chunks without overflowing or closing prematurely.
        int milliseconds = (int)Math.Min(int.MaxValue, (remaining - 1) / 1_000_000 + 1);
        gracefulTimer.Change(milliseconds, Timeout.Infinite);
    }

    private void RecordGracefulActivity()
    {
        if (gracefulRequested && !shutdownRequested) gracefulActivityNanos = ticker().nanoTime();
    }

    private void CloseAdmission()
    {
        shutdownRequested = true;
        DisposeGracefulTimer();
        // Retain accepted one-shots and cancel periodic reservations. Timeout
        // closes admission; it neither interrupts running code nor fakes drain.
        onShutdown();
    }

    private void DisposeGracefulTimer()
    {
        gracefulTimer?.Dispose();
        gracefulTimer = null;
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
            AdvanceGracefulShutdown();
            if (!shutdownRequested)
            {
                queue.Enqueue(work, (work.deadline, work.sequence));
                RecordGracefulActivity();
                ensureWorker();
                PublishPoolState();
                return;
            }
            if (work is NativeSubmissionBackend or NativeBackend)
                throw new RejectedExecutionException("Executor has been shut down.");
            handler = rejectedHandler;
        }
        handler(work.outer, this);
    }

    private void ensureWorker()
    {
        int limit = Math.Max(1, configuredWorkerCount);
        if (stopping || (shutdownRequested && queue.Count == 0) || workerCount >= limit) return;
        ++startingWorkers;
        ++workerCount;
        bool started = false;
        try
        {
            Thread thread = threadFactory.newThread(Runnables.Create(workerLoop));
            if (thread == null || stopping) return;
            workers.Add(thread);
            try { thread.Start(); }
            catch { workers.Remove(thread); throw; }
            started = true;
        }
        finally { --startingWorkers; if (!started) --workerCount; PublishPoolState(); }
    }

    private Work takeWork()
    {
        bool idleStarted = false;
        long idleDeadline = 0;
        using (UninterruptibleMonitor.enter(gate))
        {
            for (;;)
            {
                if (stopping || (shutdownRequested && queue.Count == 0))
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
                // Zero retains the constructor's transient single-worker behavior.
                // A future reservation needs that worker until its deadline or
                // cancellation; only an empty queue starts the idle retirement.
                if (configuredWorkerCount == 0 && !hasHead)
                {
                    if (!idleStarted)
                    {
                        idleDeadline = unchecked(now + ZeroWorkerIdleNanos);
                        idleStarted = true;
                    }
                    long remaining = unchecked(idleDeadline - now);
                    if (remaining <= 0)
                    {
                        --workerCount;
                        return null;
                    }
                    waitNanos = Math.Min(waitNanos, remaining);
                }
                else
                {
                    idleStarted = false;
                }
                try
                {
                    if (configuredWorkerCount != 0 && !hasHead) Monitor.Wait(gate);
                    else Monitor.Wait(gate, (int)Math.Min(int.MaxValue, (waitNanos - 1) / 1_000_000 + 1));
                }
                catch (ThreadInterruptedException) { idleStarted = false; }
            }
        }
    }

    private void workerLoop()
    {
        // Worker identity belongs to the loop, not to whichever factory created
        // its Thread. A stateful constructor factory cannot bypass native callback affinity.
        eventLoopThreads.TryAdd(Thread.CurrentThread, 0);
        bool countReleased = false;
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
                    logger.warn("Unexpected worker failure", failure);
                    return;
                }
                finally
                {
                    using (UninterruptibleMonitor.enter(gate))
                    {
                        activeWorkers.Remove(Thread.CurrentThread);
                        RecordGracefulActivity();
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
                        int minimum = configuredWorkerCount;
                        if (minimum == 0 && queue.Count != 0) minimum = 1;
                        if (workerCount < minimum) ensureWorker();
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
            return !stopping && (!shutdownRequested || work.period == 0);
    }
    private void reExecutePeriodic(Work work)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            // A detached queue handle belongs to its caller. It cannot re-create
            // pool ownership after the persistent Termination result was published.
            if (!stopping && !termination.Task.IsCompleted && !shutdownRequested)
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
            if (work.period != 0 || work.isCancelled())
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
            if (submission is ICancelableNativeSubmission cancelable)
                cancelable.SetCancellationRemoval(() => RemoveCanceledSubmission(backend));
            if (submission.IsCanceled) return;
            try { enqueue(backend); }
            catch { remove(backend); throw; }
        }
    }

    private void RemoveCanceledSubmission(NativeSubmissionBackend backend)
    {
        using (UninterruptibleMonitor.enter(gate))
        {
            remove(backend);
            backend.cancelOuter();
            PublishPoolState();
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
    private sealed class NativeBackend : Work
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

}
