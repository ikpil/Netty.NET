/*
 * Copyright 2014 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

/**
 * Checks if a thread is alive periodically and runs a task when a thread dies.
 * <p>
 * This thread starts a daemon thread to check the state of the threads being watched and to invoke their
 * associated {@link Runnable}s.  When there is no thread to watch (i.e. all threads are dead), the daemon thread
 * will terminate itself, and a new daemon thread will be started again when a new watch is added.
 * </p>
 *
 * @deprecated will be removed in the next major release
 */
[Obsolete]
public static class ThreadDeathWatcher
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ThreadDeathWatcher));
    // visible for testing
    internal static readonly IThreadFactory threadFactory;
    // Use a MPMC queue as we may end up checking isEmpty() from multiple threads which may not be allowed to do
    // concurrently depending on the implementation of it in a MPSC queue.
    private static readonly ConcurrentQueue<Entry> pendingEntries = new();
    private static readonly Watcher watcher = new();
    private static int started;
    private static Thread watcherThread;

    static ThreadDeathWatcher()
    {
        string poolName = "threadDeathWatcher";
        string prefix = SystemPropertyUtil.Get("io.netty.serviceThreadPrefix");
        if (!string.IsNullOrEmpty(prefix)) poolName = prefix + poolName;
        // because the ThreadDeathWatcher is a singleton, tasks submitted to it can come from arbitrary threads and
        // this can trigger the creation of a thread from arbitrary thread groups; for this reason, the thread factory
        // must not be sticky about its thread group
        threadFactory = new DefaultThreadFactory(poolName, true, ThreadPriority.Lowest, null);
    }
    /**
     * Schedules the specified {@code task} to run when the specified {@code thread} dies.
     *
     * @param thread the {@link Thread} to watch
     * @param task the {@link Runnable} to run when the {@code thread} dies
     *
     * @throws IllegalArgumentException if the specified {@code thread} is not alive
     */
    public static void Watch(Thread thread, IRunnable task)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(task);
        if (!thread.IsAlive) throw new ArgumentException("thread must be alive.", nameof(thread));
        Schedule(thread, task, true);
    }
    /**
     * Cancels the task scheduled via {@link #watch(Thread, Runnable)}.
     */
    public static void Unwatch(Thread thread, IRunnable task)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(task);
        Schedule(thread, task, false);
    }
    private static void Schedule(Thread thread, IRunnable task, bool isWatch)
    {
        pendingEntries.Enqueue(new Entry(thread, task, isWatch));
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
        Thread worker = threadFactory.NewThread(watcher);
        // Set to null to ensure we not create classloader leaks by holds a strong reference to the inherited
        // classloader.
        // See:
        // - https://github.com/netty/netty/issues/7290
        // - https://bugs.openjdk.java.net/browse/JDK-7008595
        // CLR has no context ClassLoader. Do not retain the submitting caller's
        // ExecutionContext/AsyncLocal state in this singleton service thread.
        if (ExecutionContext.IsFlowSuppressed()) worker.Start();
        else
        {
            using var suppressed = ExecutionContext.SuppressFlow();
            worker.Start();
        }
        Volatile.Write(ref watcherThread, worker);
    }
    /**
     * Waits until the thread of this watcher has no threads to watch and terminates itself.
     * Because a new watcher thread will be started again on {@link #watch(Thread, Runnable)},
     * this operation is only useful when you want to ensure that the watcher thread is terminated
     * <strong>after</strong> your application is shut down and there's no chance of calling
     * {@link #watch(Thread, Runnable)} afterwards.
     *
     * @return {@code true} if and only if the watcher thread has been terminated
     */
    public static bool AwaitInactivity(TimeSpan timeout)
    {
        Thread worker = Volatile.Read(ref watcherThread);
        if (worker == null) return true;
        // Java join truncates to milliseconds, treats zero as unbounded, and
        // permits waits beyond the CLR Join(Int32) range. Keep those semantics.
        long milliseconds = timeout.Ticks / TimeSpan.TicksPerMillisecond;
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (milliseconds == 0) worker.Join();
        else
        {
            var elapsed = Stopwatch.StartNew();
            long remaining = milliseconds;
            while (!worker.Join((int)Math.Min(remaining, int.MaxValue)))
            {
                remaining = milliseconds - elapsed.ElapsedMilliseconds;
                if (remaining <= 0) break;
            }
        }
        return !worker.IsAlive;
    }
    private sealed class Watcher : IRunnable
    {
        private readonly List<Entry> watchees = new();
        public void Run()
        {
            for (;;)
            {
                FetchWatchees();
                NotifyWatchees();
                // Try once again just in case notifyWatchees() triggered watch() or unwatch().
                FetchWatchees();
                NotifyWatchees();
                try { Thread.Sleep(1000); }
                catch (ThreadInterruptedException)
                {
                    // Ignore the interrupt; do not terminate until all tasks are run.
                }
                if (watchees.Count == 0 && pendingEntries.IsEmpty)
                {
                    // Mark the current worker thread as stopped.
                    // The following CAS must always success and must be uncontended,
                    // because only one watcher thread should be running at the same time.
                    bool stopped = Interlocked.CompareExchange(ref started, 0, 1) == 1;
                    Debug.Assert(stopped);
                    // Check if there are pending entries added by watch() while we do CAS above.
                    if (pendingEntries.IsEmpty)
                    {
                        // A) watch() was not invoked and thus there's nothing to handle
                        //    -> safe to terminate because there's nothing left to do
                        // B) a new watcher thread started and handled them all
                        //    -> safe to terminate the new watcher thread will take care the rest
                        break;
                    }
                    // There are pending entries again, added by watch()
                    if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
                    {
                        // watch() started a new watcher thread and set 'started' to true.
                        // -> terminate this thread so that the new watcher reads from pendingEntries exclusively.
                        break;
                    }
                    // watch() added an entry, but this worker was faster to set 'started' to true.
                    // i.e. a new watcher thread was not started
                    // -> keep this thread alive to handle the newly added entries.
                }
            }
        }
        private void FetchWatchees()
        {
            while (pendingEntries.TryDequeue(out Entry entry))
                if (entry.isWatch) watchees.Add(entry);
                else watchees.Remove(entry);
        }
        private void NotifyWatchees()
        {
            for (int index = 0; index < watchees.Count;)
            {
                Entry entry = watchees[index];
                if (entry.thread.IsAlive) { index++; continue; }
                watchees.RemoveAt(index);
                try { entry.task.Run(); }
                catch (Exception failure) { logger.Warn("Thread death watcher task raised an exception:", failure); }
            }
        }
    }
    private sealed class Entry(Thread thread, IRunnable task, bool isWatch)
    {
        internal readonly Thread thread = thread;
        internal readonly IRunnable task = task;
        internal readonly bool isWatch = isWatch;
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(thread) ^ RuntimeHelpers.GetHashCode(task);
        public override bool Equals(object value) => value is Entry other &&
            ReferenceEquals(thread, other.thread) && ReferenceEquals(task, other.task);
    }
}
