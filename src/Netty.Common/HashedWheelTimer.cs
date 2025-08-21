/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A {@link ITimer} optimized for approximated I/O timeout scheduling.
 *
 * <h3>Tick Duration</h3>
 *
 * As described with 'approximated', this timer does not execute the scheduled
 * {@link ITimerTask} on time.  {@link HashedWheelTimer}, on every tick, will
 * check if there are any {@link ITimerTask}s behind the schedule and execute
 * them.
 * <p>
 * You can increase or decrease the accuracy of the execution timing by
 * specifying smaller or larger tick duration in the constructor.  In most
 * network applications, I/O timeout does not need to be accurate.  Therefore,
 * the default tick duration is 100 milliseconds and you will not need to try
 * different configurations in most cases.
 *
 * <h3>Ticks per Wheel (Wheel Size)</h3>
 *
 * {@link HashedWheelTimer} maintains a data structure called 'wheel'.
 * To put simply, a wheel is a hash table of {@link ITimerTask}s whose hash
 * function is 'dead line of the task'.  The default number of ticks per wheel
 * (i.e. the size of the wheel) is 512.  You could specify a larger value
 * if you are going to schedule a lot of timeouts.
 *
 * <h3>Do not create many instances.</h3>
 *
 * {@link HashedWheelTimer} creates a new thread whenever it is instantiated and
 * started.  Therefore, you should make sure to create only one instance and
 * share it across your application.  One of the common mistakes, that makes
 * your application unresponsive, is to create a new instance for every connection.
 *
 * <h3>Implementation Details</h3>
 *
 * {@link HashedWheelTimer} is based on
 * <a href="https://cseweb.ucsd.edu/users/varghese/">George Varghese</a> and
 * Tony Lauck's paper,
 * <a href="https://cseweb.ucsd.edu/users/varghese/PAPERS/twheel.ps.Z">'Hashed
 * and Hierarchical Timing Wheels: data structures to efficiently implement a
 * timer facility'</a>.  More comprehensive slides are located
 * <a href="https://www.cse.wustl.edu/~cdgill/courses/cs6874/TimingWheels.ppt">here</a>.
 */
public class HashedWheelTimer : ITimer 
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(HashedWheelTimer));

    private static readonly AtomicInteger INSTANCE_COUNTER = new AtomicInteger();
    private static readonly AtomicBoolean WARNED_TOO_MANY_INSTANCES = new AtomicBoolean();
    private static readonly int INSTANCE_COUNT_LIMIT = 64;
    private static readonly long MILLISECOND_NANOS = TimeSpan.MILLISECONDS.toNanos(1);
    private static readonly ResourceLeakDetector<HashedWheelTimer> leakDetector = ResourceLeakDetectorFactory.instance()
            .newResourceLeakDetector(typeof(HashedWheelTimer), 1);

    private static readonly AtomicIntegerFieldUpdater<HashedWheelTimer> WORKER_STATE_UPDATER =
            AtomicIntegerFieldUpdater.newUpdater(typeof(HashedWheelTimer), "workerState");

    private readonly IResourceLeakTracker<HashedWheelTimer> leak;
    private readonly HashedWheelWorker worker = new HashedWheelWorker();
    private readonly Thread workerThread;

    public const int WORKER_STATE_INIT = 0;
    public const int WORKER_STATE_STARTED = 1;
    public const int WORKER_STATE_SHUTDOWN = 2;
    
    @SuppressWarnings({"unused", "FieldMayBeFinal"})
    private volatile int workerState; // 0 - init, 1 - started, 2 - shut down

    private readonly long _tickDuration;
    private readonly HashedWheelBucket[] _wheel;
    private readonly int _mask;
    private readonly CountDownLatch _startTimeInitialized = new CountDownLatch(1);
    private readonly Queue<HashedWheelTimeout> _timeouts = PlatformDependent.newMpscQueue();
    private readonly Queue<HashedWheelTimeout> _cancelledTimeouts = PlatformDependent.newMpscQueue();
    private readonly AtomicLong _pendingTimeouts = new AtomicLong(0);
    private readonly long _maxPendingTimeouts;
    private readonly IExecutor _taskExecutor;

    private volatile long startTime;

    /**
     * Creates a new timer with the default thread factory
     * ({@link Executors#defaultThreadFactory()}), default tick duration, and
     * default number of ticks per wheel.
     */
    public HashedWheelTimer() 
        : this(Executors.defaultThreadFactory())
    {
    }

    /**
     * Creates a new timer with the default thread factory
     * ({@link Executors#defaultThreadFactory()}) and default number of ticks
     * per wheel.
     *
     * @param tickDuration the duration between tick
     * @param unit         the time unit of the {@code tickDuration}
     * @throws NullPointerException     if {@code unit} is {@code null}
     * @throws ArgumentException if {@code tickDuration} is &lt;= 0
     */
    public HashedWheelTimer(TimeSpan tickDuration) 
        : this(Executors.defaultThreadFactory(), tickDuration)
    {
    }

    /**
     * Creates a new timer with the default thread factory
     * ({@link Executors#defaultThreadFactory()}).
     *
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @param ticksPerWheel the size of the wheel
     * @throws NullPointerException     if {@code unit} is {@code null}
     * @throws ArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(TimeSpan tickDuration, int ticksPerWheel) 
        : this(Executors.defaultThreadFactory(), tickDuration, ticksPerWheel)
    {
    }

    /**
     * Creates a new timer with the default tick duration and default number of
     * ticks per wheel.
     *
     * @param threadFactory a {@link IThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link ITimerTask} execution.
     * @throws NullPointerException if {@code threadFactory} is {@code null}
     */
    public HashedWheelTimer(IThreadFactory threadFactory) 
        : this(threadFactory, TimeSpan.FromMilliseconds(100))
    {
    }

    /**
     * Creates a new timer with the default number of ticks per wheel.
     *
     * @param threadFactory a {@link IThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link ITimerTask} execution.
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws ArgumentException if {@code tickDuration} is &lt;= 0
     */
    public HashedWheelTimer(
            IThreadFactory threadFactory, long tickDuration, TimeSpan unit) {
        this(threadFactory, tickDuration, unit, 512);
    }

    /**
     * Creates a new timer.
     *
     * @param threadFactory a {@link IThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link ITimerTask} execution.
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @param ticksPerWheel the size of the wheel
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws ArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(
            IThreadFactory threadFactory,
            TimeSpan tickDuration, int ticksPerWheel) 
        : this(threadFactory, tickDuration, ticksPerWheel, true)
    {
    }

    /**
     * Creates a new timer.
     *
     * @param threadFactory a {@link IThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link ITimerTask} execution.
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @param ticksPerWheel the size of the wheel
     * @param leakDetection {@code true} if leak detection should be enabled always,
     *                      if false it will only be enabled if the worker thread is not
     *                      a daemon thread.
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws ArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(
            IThreadFactory threadFactory,
            TimeSpan tickDuration, int ticksPerWheel, bool leakDetection) 
        : this(threadFactory, tickDuration, ticksPerWheel, leakDetection, -1)
    {
    }

    /**
     * Creates a new timer.
     *
     * @param threadFactory        a {@link IThreadFactory} that creates a
     *                             background {@link Thread} which is dedicated to
     *                             {@link ITimerTask} execution.
     * @param tickDuration         the duration between tick
     * @param unit                 the time unit of the {@code tickDuration}
     * @param ticksPerWheel        the size of the wheel
     * @param leakDetection        {@code true} if leak detection should be enabled always,
     *                             if false it will only be enabled if the worker thread is not
     *                             a daemon thread.
     * @param  maxPendingTimeouts  The maximum number of pending timeouts after which call to
     *                             {@code newTimeout} will result in
     *                             {@link java.util.concurrent.RejectedExecutionException}
     *                             being thrown. No maximum pending timeouts limit is assumed if
     *                             this value is 0 or negative.
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws ArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(
            IThreadFactory threadFactory,
            TimeSpan tickDuration, int ticksPerWheel, bool leakDetection,
            long maxPendingTimeouts) 
        : this(threadFactory, tickDuration, ticksPerWheel, leakDetection, maxPendingTimeouts, ImmediateExecutor.INSTANCE)
    {
    }
    /**
     * Creates a new timer.
     *
     * @param threadFactory        a {@link IThreadFactory} that creates a
     *                             background {@link Thread} which is dedicated to
     *                             {@link ITimerTask} execution.
     * @param tickDuration         the duration between tick
     * @param unit                 the time unit of the {@code tickDuration}
     * @param ticksPerWheel        the size of the wheel
     * @param leakDetection        {@code true} if leak detection should be enabled always,
     *                             if false it will only be enabled if the worker thread is not
     *                             a daemon thread.
     * @param maxPendingTimeouts   The maximum number of pending timeouts after which call to
     *                             {@code newTimeout} will result in
     *                             {@link java.util.concurrent.RejectedExecutionException}
     *                             being thrown. No maximum pending timeouts limit is assumed if
     *                             this value is 0 or negative.
     * @param taskExecutor         The {@link IExecutor} that is used to execute the submitted {@link ITimerTask}s.
     *                             The caller is responsible to shutdown the {@link IExecutor} once it is not needed
     *                             anymore.
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws ArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(
            IThreadFactory threadFactory,
            TimeSpan tickDuration, int ticksPerWheel, bool leakDetection,
            long maxPendingTimeouts, IExecutor taskExecutor) {

        checkNotNull(threadFactory, "threadFactory");
        checkPositive(tickDuration, "tickDuration");
        checkPositive(ticksPerWheel, "ticksPerWheel");
        this.taskExecutor = checkNotNull(taskExecutor, "taskExecutor");

        // Normalize ticksPerWheel to power of two and initialize the wheel.
        wheel = createWheel(ticksPerWheel);
        mask = wheel.Length - 1;

        // Convert tickDuration to nanos.
        long duration = unit.toNanos(tickDuration);

        // Prevent overflow.
        if (duration >= long.MaxValue / wheel.Length) {
            throw new ArgumentException($"tickDuration: {tickDuration} (expected: 0 < tickDuration in nanos < {long.MaxValue / wheel.Length}");
        }

        if (duration < MILLISECOND_NANOS) {
            logger.warn("Configured tickDuration {} smaller than {}, using 1ms.",
                        tickDuration, MILLISECOND_NANOS);
            this.tickDuration = MILLISECOND_NANOS;
        } else {
            this.tickDuration = duration;
        }

        workerThread = threadFactory.newThread(worker);

        leak = leakDetection || !workerThread.IsBackground ? leakDetector.track(this) : null;

        this.maxPendingTimeouts = maxPendingTimeouts;

        if (INSTANCE_COUNTER.incrementAndGet() > INSTANCE_COUNT_LIMIT &&
            WARNED_TOO_MANY_INSTANCES.compareAndSet(false, true)) {
            reportTooManyInstances();
        }
    }

    ~HashedWheelTimer()
    {
        try {
            super.finalize();
        } finally {
            // This object is going to be GCed and it is assumed the ship has sailed to do a proper shutdown. If
            // we have not yet shutdown then we want to make sure we decrement the active instance count.
            if (WORKER_STATE_UPDATER.getAndSet(this, WORKER_STATE_SHUTDOWN) != WORKER_STATE_SHUTDOWN) {
                INSTANCE_COUNTER.decrementAndGet();
            }
        }
    }

    private static HashedWheelBucket[] createWheel(int ticksPerWheel) {
        ticksPerWheel = MathUtil.findNextPositivePowerOfTwo(ticksPerWheel);

        HashedWheelBucket[] wheel = new HashedWheelBucket[ticksPerWheel];
        for (int i = 0; i < wheel.length; i ++) {
            wheel[i] = new HashedWheelBucket();
        }
        return wheel;
    }

    /**
     * Starts the background thread explicitly.  The background thread will
     * start automatically on demand even if you did not call this method.
     *
     * @throws InvalidOperationException if this timer has been
     *                               {@linkplain #stop() stopped} already
     */
    public void start() {
        switch (WORKER_STATE_UPDATER.get(this)) {
            case WORKER_STATE_INIT:
                if (WORKER_STATE_UPDATER.compareAndSet(this, WORKER_STATE_INIT, WORKER_STATE_STARTED)) {
                    workerThread.Start();
                }
                break;
            case WORKER_STATE_STARTED:
                break;
            case WORKER_STATE_SHUTDOWN:
                throw new InvalidOperationException("cannot be started once stopped");
            default:
                throw new InvalidOperationException("Invalid WorkerState");
        }

        // Wait until the startTime is initialized by the worker.
        while (startTime == 0) {
            try {
                startTimeInitialized.await();
            } catch (ThreadInterruptedException ignore) {
                // Ignore - it will be ready very soon.
            }
        }
    }

    public ISet<ITimeout> stop() {
        if (Thread.CurrentThread == workerThread) {
            throw new InvalidOperationException(
                    nameof(HashedWheelTimer) +
                            ".stop() cannot be called from " +
                            nameof(ITimerTask));
        }

        if (!WORKER_STATE_UPDATER.compareAndSet(this, WORKER_STATE_STARTED, WORKER_STATE_SHUTDOWN)) {
            // workerState can be 0 or 2 at this moment - let it always be 2.
            if (WORKER_STATE_UPDATER.getAndSet(this, WORKER_STATE_SHUTDOWN) != WORKER_STATE_SHUTDOWN) {
                INSTANCE_COUNTER.decrementAndGet();
                if (leak != null) {
                    bool closed = leak.close(this);
                    assert closed;
                }
            }

            return Collections.emptySet();
        }

        try {
            bool interrupted = false;
            while (workerThread.isAlive()) {
                workerThread.interrupt();
                try {
                    workerThread.join(100);
                } catch (ThreadInterruptedException ignored) {
                    interrupted = true;
                }
            }

            if (interrupted) {
                Thread.CurrentThread.interrupt();
            }
        } finally {
            INSTANCE_COUNTER.decrementAndGet();
            if (leak != null) {
                bool closed = leak.close(this);
                assert closed;
            }
        }
        ISet<ITimeout> unprocessed = worker.unprocessedTimeouts();
        ISet<ITimeout> cancelled = new HashSet<ITimeout>(unprocessed.Count);
        foreach (ITimeout timeout in unprocessed) {
            if (timeout.cancel()) {
                cancelled.Add(timeout);
            }
        }
        return cancelled;
    }

    public ITimeout newTimeout(ITimerTask task, long delay, TimeSpan unit) {
        checkNotNull(task, "task");
        checkNotNull(unit, "unit");

        long pendingTimeoutsCount = pendingTimeouts.incrementAndGet();

        if (maxPendingTimeouts > 0 && pendingTimeoutsCount > maxPendingTimeouts) {
            pendingTimeouts.decrementAndGet();
            throw new RejectedExecutionException("Number of pending timeouts ("
                + pendingTimeoutsCount + ") is greater than or equal to maximum allowed pending "
                + "timeouts (" + maxPendingTimeouts + ")");
        }

        start();

        // Add the timeout to the timeout queue which will be processed on the next tick.
        // During processing all the queued HashedWheelTimeouts will be added to the correct HashedWheelBucket.
        long deadline = PreciseTimer.nanoTime() + unit.toNanos(delay) - startTime;

        // Guard against overflow.
        if (delay > 0 && deadline < 0) {
            deadline = long.MaxValue;
        }
        HashedWheelTimeout timeout = new HashedWheelTimeout(this, task, deadline);
        timeouts.add(timeout);
        return timeout;
    }

    /**
     * Returns the number of pending timeouts of this {@link ITimer}.
     */
    public long pendingTimeouts() {
        return _pendingTimeouts.get();
    }

    private static void reportTooManyInstances() {
        if (logger.isErrorEnabled()) {
            string resourceType = StringUtil.simpleClassName(typeof(HashedWheelTimer));
            logger.error("You are creating too many " + resourceType + " instances. " +
                    resourceType + " is a shared resource that must be reused across the JVM, " +
                    "so that only a few instances are created.");
        }
    }



}
