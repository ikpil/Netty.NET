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
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(UnorderedThreadPoolEventExecutor));
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
    private readonly Action<Action, UnorderedThreadPoolEventExecutor> rejectedHandler;
    private bool gracefulRequested;
    private long gracefulStartNanos;
    private long gracefulActivityNanos;
    private long gracefulQuietNanos;
    private long gracefulTimeoutNanos;
    private Timer gracefulTimer;
    private bool shutdownRequested;
    private bool stopping;
    private Exception backendFailure;
    private readonly CancellationTokenSource stopSource = new();
    private readonly CancellationToken stopToken;
    private int stopNotifications;
    private AggregateException stopCallbackFailure;
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
    public UnorderedThreadPoolEventExecutor(int workerCount, Action<Action, UnorderedThreadPoolEventExecutor> handler)
        : this(workerCount, new DefaultThreadFactory(typeof(UnorderedThreadPoolEventExecutor)), handler) { }

    /**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory, RejectedExecutionHandler)}
     */
    // CLR represents the JDK rejection handler as a delegate; it is not Netty's SingleThread rejection handler.
    public UnorderedThreadPoolEventExecutor(int workerCount, IThreadFactory threadFactory,
        Action<Action, UnorderedThreadPoolEventExecutor> handler)
    {
        if (workerCount < 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        ArgumentNullException.ThrowIfNull(threadFactory);
        ArgumentNullException.ThrowIfNull(handler);
        configuredWorkerCount = workerCount;
        this.threadFactory = threadFactory;
        rejectedHandler = handler;
        stopToken = stopSource.Token;
    }

    public IEventExecutorGroup Parent() => this;
    public IEventExecutor Next() => this;
    public IEnumerable<IEventExecutor> Iterator() { yield return this; }
    public Ticker Ticker() => global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
    public bool InEventLoop() => InEventLoop(Thread.CurrentThread);
    public bool InEventLoop(Thread thread) => thread != null && eventLoopThreads.ContainsKey(thread);
    public bool IsExecutorThread(Thread thread) => InEventLoop(thread);
    public bool IsShuttingDown() { using (UninterruptibleMonitor.Enter(gate)) return gracefulRequested || shutdownRequested; }
    public bool IsSuspended() => false;
    public bool TrySuspend() => false;
    public bool IsShutdown() { using (UninterruptibleMonitor.Enter(gate)) return shutdownRequested; }
    public bool IsTerminated()
    {
        using (UninterruptibleMonitor.Enter(gate)) return shutdownRequested && workers.Count == 0 && startingWorkers == 0 && queue.Count == 0 && stopNotifications == 0;
    }
    // All callers hold gate. Async continuations cannot run inline while pool
    // state is being published. A start reservation counts even before its
    // factory returns a Thread; shutdown cannot finish underneath that factory.
    private void PublishPoolState()
    {
        AdvanceGracefulShutdown();
        Monitor.PulseAll(gate);
        if (shutdownRequested && workers.Count == 0 && startingWorkers == 0 && queue.Count == 0 && stopNotifications == 0 && !termination.Task.IsCompleted)
        {
            stopSource.Dispose();
            if (stopCallbackFailure != null)
                termination.TrySetException(backendFailure == null ? stopCallbackFailure.InnerExceptions :
                    new[] { backendFailure }.Concat(stopCallbackFailure.InnerExceptions));
            else if (backendFailure != null) termination.TrySetException(backendFailure);
            else termination.TrySetResult();
        }
    }
    public Task Termination => termination.Task;
    public Task ShutdownGracefullyAsync() => ShutdownGracefullyAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
    /// <summary>Gets the number of pending invocation reservations, including future deadlines.</summary>
    /// <remarks>
    /// This is a momentary observation under the pool gate, not an admission guarantee.
    /// Running invocations are excluded. CancellationToken ownership removes native work;
    /// callers cannot mutate the executor's queue or transfer reservations between pools.
    /// </remarks>
    public int PendingTaskCount { get { using (UninterruptibleMonitor.Enter(gate)) return queue.Count; } }
    /// <summary>Gets the number of worker threads currently owned by this pool, including retiring loops.</summary>
    public int WorkerCount { get { using (UninterruptibleMonitor.Enter(gate)) return workers.Count; } }
    /// <summary>Gets the number of currently claimed invocations; yielded asynchronous bodies are caller-owned.</summary>
    public int ActiveWorkerCount { get { using (UninterruptibleMonitor.Enter(gate)) return activeWorkers.Count; } }

    /// <summary>Gets the token requested by immediate stop; graceful closure does not request it.</summary>
    /// <remarks>
    /// Pass this token to native submission/scheduling or explicitly link it with an
    /// operation's owner token. Stop does not cancel unrelated caller token sources.
    /// The token remains readable after termination; its source is disposed on drain.
    /// </remarks>
    public CancellationToken StopToken => stopToken;

    /// <summary>Closes admission, cancels waiting work, requests cooperative stop, and waits for drain.</summary>
    /// <remarks>
    /// Running delegates must observe their own cancellation policy; threads are never
    /// interrupted. This is the same persistent Task as Termination. Stop callbacks
    /// present at the request execute asynchronously, are included in drain, and their failures are
    /// retained in Termination.Exception. Yielded asynchronous bodies remain caller-owned.
    /// </remarks>
    public Task StopAsync()
    {
        StopCore();
        return Termination;
    }

    private void StopCore()
    {
        Task notifications = null;
        using (UninterruptibleMonitor.Enter(gate))
        {
            shutdownRequested = stopping = true;
            DisposeGracefulTimer();
            if (!stopToken.IsCancellationRequested && !termination.Task.IsCompleted)
            {
                ++stopNotifications;
                // CancelAsync requests the token now, but never invokes arbitrary
                // registered code inline under this gate (including factory reentry).
                using (ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow())
                    notifications = stopSource.CancelAsync();
            }
            Work[] pending = queue.UnorderedItems.Select(item => item.Element).ToArray();
            // Queue handles are membership, not results. Every removed reservation
            // settles cancellation or releases its raw callback before termination.
            // Canceling a submission can remove itself through its membership
            // hook. Iterate the owned snapshot rather than a live BCL enumerator.
            foreach (Work work in pending) work.CancelOuter();
            queue.Clear();
            PublishPoolState();
        }
        if (notifications != null)
        {
            using (ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow())
                _ = ObserveStopCallbacksAsync(notifications);
        }
    }

    private async Task ObserveStopCallbacksAsync(Task notifications)
    {
        AggregateException failure = null;
        try { await notifications.ConfigureAwait(false); }
        catch (Exception error) { failure = notifications.Exception ?? new AggregateException(error); }
        using (UninterruptibleMonitor.Enter(gate))
        {
            stopCallbackFailure = failure;
            --stopNotifications;
            PublishPoolState();
        }
    }

    public void Shutdown()
    {
        using (UninterruptibleMonitor.Enter(gate))
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
        using (UninterruptibleMonitor.Enter(gate))
        {
            if (!gracefulRequested && !shutdownRequested)
            {
                gracefulStartNanos = gracefulActivityNanos = Ticker().NanoTime();
                gracefulQuietNanos = AbstractScheduledEventExecutor.ToNanos(quietPeriod);
                gracefulTimeoutNanos = AbstractScheduledEventExecutor.ToNanos(timeout);
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
        using (UninterruptibleMonitor.Enter(gate)) PublishPoolState();
    }

    // All lifecycle decisions and admission share gate. Timer lateness cannot
    // extend admission: each submission checks the monotonic deadline itself.
    private void AdvanceGracefulShutdown()
    {
        if (!gracefulRequested || shutdownRequested) return;
        long now = Ticker().NanoTime();
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
        if (gracefulRequested && !shutdownRequested) gracefulActivityNanos = Ticker().NanoTime();
    }

    private void CloseAdmission()
    {
        shutdownRequested = true;
        DisposeGracefulTimer();
        // Retain accepted one-shots and cancel periodic reservations. Timeout
        // closes admission; it neither interrupts running code nor fakes drain.
        OnShutdown();
    }

    private void DisposeGracefulTimer()
    {
        gracefulTimer?.Dispose();
        gracefulTimer = null;
    }

    public bool AwaitTermination(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return IsTerminated();
        var elapsed = Stopwatch.StartNew();
        using (UninterruptibleMonitor.Enter(gate))
        {
            while (!shutdownRequested || workers.Count != 0 || startingWorkers != 0 || queue.Count != 0 || stopNotifications != 0)
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

    public void Execute(Action command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ExecutorWork.GetNativeSubmission(command) is { } native)
        {
            ExecuteNativeSubmission(native);
            return;
        }

        var work = new RawWork(this, command);
        try
        {
            Enqueue(work);
        }
        catch
        {
            // Throwing admission must not execute the callback later after a retry.
            using (UninterruptibleMonitor.Enter(gate))
            {
                Remove(work);
                PublishPoolState();
            }

            work.CancelOuter();
            throw;
        }
    }

    private void Enqueue(Work work)
    {
        Action<Action, UnorderedThreadPoolEventExecutor> handler;
        using (UninterruptibleMonitor.Enter(gate))
        {
            AdvanceGracefulShutdown();
            if (!shutdownRequested)
            {
                queue.Enqueue(work, (work.deadline, work.sequence));
                RecordGracefulActivity();
                EnsureWorker();
                PublishPoolState();
                return;
            }
            if (work is NativeSubmissionBackend or NativeBackend)
                throw new RejectedExecutionException("Executor has been shut down.");
            handler = rejectedHandler;
        }
        // Keep callback claiming/cancellation with the existing queue membership.
        // A bound Action needs no additional result or replay-envelope object.
        handler(work.Run, this);
    }

    private bool EnsureWorker()
    {
        int limit = Math.Max(1, configuredWorkerCount);
        if (stopping || (shutdownRequested && queue.Count == 0)) return false;
        if (workerCount >= limit) return true;
        ++startingWorkers;
        ++workerCount;
        bool started = false;
        try
        {
            Thread thread = threadFactory.NewThread(WorkerLoop);
            if (thread == null || stopping) return false;
            workers.Add(thread);
            try { thread.Start(); }
            catch { workers.Remove(thread); throw; }
            started = true;
            return true;
        }
        finally { --startingWorkers; if (!started) --workerCount; PublishPoolState(); }
    }

    private Work TakeWork()
    {
        bool idleStarted = false;
        long idleDeadline = 0;
        using (UninterruptibleMonitor.Enter(gate))
        {
            for (;;)
            {
                if (stopping || (shutdownRequested && queue.Count == 0))
                {
                    --workerCount;
                    return null;
                }
                long now = Ticker().NanoTime();
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

    private void WorkerLoop()
    {
        // Worker identity belongs to the loop, not to whichever factory created
        // its Thread. A stateful constructor factory cannot bypass native callback affinity.
        eventLoopThreads.TryAdd(Thread.CurrentThread, 0);
        bool countReleased = false;
        try
        {
            for (;;)
            {
                Work work = TakeWork();
                if (work == null) { countReleased = true; return; }
                try
                {
                    work.Run();
                }
                catch (Exception failure)
                {
                    // CLR unhandled exceptions kill the process. Match JDK worker replacement instead.
                    LogWorkerFailure("Unexpected worker failure", failure);
                    return;
                }
                finally
                {
                    using (UninterruptibleMonitor.Enter(gate))
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
                using (UninterruptibleMonitor.Enter(gate))
                {
                    if (!countReleased) --workerCount;
                    try
                    {
                        if (!stopping && (!shutdownRequested || queue.Count != 0))
                        {
                            int minimum = configuredWorkerCount;
                            if (minimum == 0 && queue.Count != 0) minimum = 1;
                            if (workerCount < minimum)
                            {
                                try
                                {
                                    // Factory code may reenter shutdown; a null
                                    // return then needs no replacement or failure.
                                    if (!EnsureWorker() && !stopping && (!shutdownRequested || queue.Count != 0))
                                        FailWorkerReplacement(new InvalidOperationException("Thread factory returned no replacement worker."));
                                }
                                catch (Exception failure) { FailWorkerReplacement(failure); }
                            }
                        }
                    }
                    finally
                    {
                        // Keep the retiring loop owned until the replacement
                        // outcome is known. Its factory can close admission and
                        // discard work before throwing; publishing success from
                        // the start reservation's finally would hide that error.
                        workers.Remove(Thread.CurrentThread);
                        PublishPoolState();
                    }
                }
            }
            finally { eventLoopThreads.TryRemove(Thread.CurrentThread, out _); }
        }
    }
    // A failure on a background worker has no admission caller to receive it.
    // Never let replacement creation/start kill the CLR process or leave accepted
    // native results pending. Close admission, reject waiting work, and publish the
    // same failure only after every other worker and factory reservation drains.
    // Initial admission failures still affect only that admission and can be retried.
    private void FailWorkerReplacement(Exception failure)
    {
        backendFailure ??= failure;
        shutdownRequested = stopping = true;
        DisposeGracefulTimer();
        Work[] pending = queue.UnorderedItems.Select(item => item.Element).ToArray();
        queue.Clear();
        foreach (Work work in pending) work.Reject(backendFailure);
        LogWorkerFailure("Failed to replace executor worker", backendFailure);
        PublishPoolState();
    }

    private static void LogWorkerFailure(string message, Exception failure)
    {
        // A logging provider failure must not become an unhandled CLR thread
        // exception while reporting the original worker/backend failure.
        try { logger.Warn(message, failure); }
        catch (Exception) { }
    }

    private bool CanRun(Work work)
    {
        // Like the JDK run-state check, a saved rejection callback cannot start
        // another invocation after the pool has published its actual termination.
        using (UninterruptibleMonitor.Enter(gate))
            return !stopping && !termination.Task.IsCompleted && (!shutdownRequested || work.period == 0);
    }
    private void ReExecutePeriodic(Work work)
    {
        using (UninterruptibleMonitor.Enter(gate))
        {
            // A detached queue handle belongs to its caller. It cannot re-create
            // pool ownership after the persistent Termination result was published.
            if (!stopping && !termination.Task.IsCompleted && !shutdownRequested)
            {
                queue.Enqueue(work, (work.deadline, work.sequence));
                EnsureWorker();
                PublishPoolState();
                return;
            }
        }
        work.CancelOuter();
    }
    private void OnShutdown()
    {
        foreach (var work in queue.UnorderedItems.Select(item => item.Element).ToArray())
        {
            if (work.period != 0 || work.IsCancelled())
            {
                Remove(work);
                work.CancelOuter();
            }
        }
        PublishPoolState();
    }
    private bool Remove(Work work)
    {
        // CLR reservations have one membership each. Match reference identity and
        // retain stored priorities; net10 Remove scans then repairs the existing heap
        // without allocating a whole snapshot or reinserting every surviving entry.
        return queue.Remove(work, out _, out _, ReferenceEqualityComparer.Instance);
    }

    // JDK deadlines wrap with nanoTime. Keep the distance from an already overdue
    // queue head within Int64.MaxValue so signed-difference comparison remains valid.
    private long TriggerTime(long delay)
    {
        delay = Math.Max(0, delay);
        using (UninterruptibleMonitor.Enter(gate))
        {
            long now = Ticker().NanoTime();
            if (delay >= (long.MaxValue >> 1) && queue.TryPeek(out Work head, out _))
            {
                long headDelay = unchecked(head.deadline - now);
                if (headDelay < 0 && unchecked(delay - headDelay) < 0)
                    delay = long.MaxValue + headDelay;
            }
            return unchecked(now + delay);
        }
    }

    private interface Work
    {
        UnorderedThreadPoolEventExecutor owner { get; }
        long sequence { get; }
        long period { get; }
        long deadline { get; }
        void Run();
        bool IsCancelled();
        void CancelOuter();
        void Reject(Exception error);
    }

    internal void ExecuteNativeSubmission(INativeSubmission submission)
    {
        var backend = new NativeSubmissionBackend(this, submission);
        using (UninterruptibleMonitor.Enter(gate))
        {
            // A discard handler must not leave the producer-owned Task pending.
            if (shutdownRequested) throw new RejectedExecutionException("Executor has been shut down.");
            if (submission is ICancelableNativeSubmission cancelable)
                cancelable.SetCancellationRemoval(() => RemoveCanceledSubmission(backend));
            if (submission.IsCanceled) return;
            try { Enqueue(backend); }
            catch { Remove(backend); throw; }
        }
    }

    private void RemoveCanceledSubmission(NativeSubmissionBackend backend)
    {
        using (UninterruptibleMonitor.Enter(gate))
        {
            Remove(backend);
            backend.CancelOuter();
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
        internal NativeSubmissionBackend(UnorderedThreadPoolEventExecutor owner, INativeSubmission submission)
        {
            this.owner = owner;
            _submission = submission;
            if (submission is IQueueBoundNativeSubmission runner) runner.BindQueueOwner(owner);
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            deadline = owner.TriggerTime(0);
        }
        public bool IsCancelled() => Volatile.Read(ref _submission)?.IsCanceled ?? true;
        public void CancelOuter() => Interlocked.Exchange(ref _submission, null)?.CancelForShutdown();
        public void Reject(Exception error) => Interlocked.Exchange(ref _submission, null)?.Reject(error);
        public void Run()
        {
            INativeSubmission submission = Interlocked.Exchange(ref _submission, null);
            if (submission == null) return;
            if (!owner.CanRun(this)) submission.CancelForShutdown();
            else
            {
                try { submission.Run(); }
                catch (Exception error)
                {
                    submission.Reject(error);
                    throw;
                }
            }
        }
    }

    internal Task<T> ScheduleNative<T>(Func<CancellationToken, T> function, TimeSpan delay, long period,
        CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
        NativeBackend backend = null;
        var task = new NativeScheduledWork<T>(function, token, TriggerTime(AbstractScheduledEventExecutor.ToNanos(delay)),
            period, () => Ticker().NanoTime(), () => CanRun(backend),
            work =>
            {
                using (UninterruptibleMonitor.Enter(gate))
                {
                    if (work.Completion.IsCompleted) return;
                    try { ReExecutePeriodic(backend); }
                    catch { Remove(backend); throw; }
                }
            },
            work =>
            {
                using (UninterruptibleMonitor.Enter(gate))
                {
                    Remove(backend);
                    PublishPoolState();
                }
            });
        backend = new NativeBackend(this, task);
        try
        {
            // Native result publication must not depend on a JDK rejection handler
            // silently discarding work. It faults if shutdown prevents admission.
            using (UninterruptibleMonitor.Enter(gate))
            {
                if (shutdownRequested) throw new RejectedExecutionException("Executor has been shut down.");
                if (!task.Completion.IsCompleted) Enqueue(backend);
            }
        }
        catch (Exception error)
        {
            using (UninterruptibleMonitor.Enter(gate)) { Remove(backend); PublishPoolState(); }
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
        public long deadline => _task.DeadlineNanos();
        internal NativeBackend(UnorderedThreadPoolEventExecutor owner, ITaskScheduledWork task)
        {
            this.owner = owner;
            _task = task;
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            task.AssignId(sequence);
        }
        public bool IsCancelled() => _task.IsCanceled;
        public void CancelOuter() => _task.CancelForShutdown();
        public void Reject(Exception error) => _task.Reject(error);
        public void Run() => _task.QueueCallback();
    }

    // Raw execute owns no asynchronous result. Its queue entry claims and releases
    // the callback once, including discard during shutdown or clearing.
    private sealed class RawWork : Work
    {
        private Action command;
        public UnorderedThreadPoolEventExecutor owner { get; }
        public long sequence { get; }
        public long period => 0;
        public long deadline { get; }
        internal RawWork(UnorderedThreadPoolEventExecutor owner, Action command)
        {
            this.owner = owner;
            this.command = command;
            sequence = Interlocked.Increment(ref nextSequence) - 1;
            deadline = owner.TriggerTime(0);
        }
        public bool IsCancelled() => Volatile.Read(ref command) == null;
        public void CancelOuter() => Interlocked.Exchange(ref command, null);
        public void Reject(Exception error) => CancelOuter();
        public void Run()
        {
            Action callback = Interlocked.Exchange(ref command, null);
            if (callback == null || !owner.CanRun(this)) return;
            try { callback(); }
            catch (Exception failure) { logger.Warn("Failure during execution of task", failure); }
        }
    }

}
