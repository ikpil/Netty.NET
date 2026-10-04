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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A {@link Timer} optimized for approximated I/O timeout scheduling.
 *
 * <h3>Tick Duration</h3>
 *
 * As described with 'approximated', this timer does not execute the scheduled
 * {@link TimerTask} on time.  {@link HashedWheelTimer}, on every tick, will
 * check if there are any {@link TimerTask}s behind the schedule and execute
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
 * To put simply, a wheel is a hash table of {@link TimerTask}s whose hash
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
public class HashedWheelTimer : ITimer, IDisposable
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(HashedWheelTimer));

    private static int INSTANCE_COUNTER;
    private static int WARNED_TOO_MANY_INSTANCES;
    private static readonly int INSTANCE_COUNT_LIMIT = 64;
    private static readonly long MILLISECOND_NANOS = TimeSpan.FromMilliseconds(1).Ticks * TimeSpan.NanosecondsPerTick;

    private static readonly ResourceLeakDetector<HashedWheelTimer> leakDetector = ResourceLeakDetectorFactory.Instance()
        .NewResourceLeakDetector<HashedWheelTimer>(typeof(HashedWheelTimer), 1);

    private readonly IResourceLeakTracker<HashedWheelTimer> _leak;
    private readonly HashedWheelWorker _worker;
    private readonly Thread _workerThread;

    public const int WORKER_STATE_INIT = 0;
    public const int WORKER_STATE_STARTED = 1;
    public const int WORKER_STATE_SHUTDOWN = 2;

    internal int _workerState; // 0 - init, 1 - started, 2 - shut down

    internal readonly long _tickDuration;
    internal readonly HashedWheelBucket[] _wheel;
    internal readonly int _mask;
    internal readonly CountdownEvent _startTimeInitialized = new CountdownEvent(1);
    internal readonly ConcurrentQueue<HashedWheelTimeout> _timeouts = new();
    internal readonly ConcurrentQueue<HashedWheelTimeout> _cancelledTimeouts = new();
    internal long _pendingTimeouts;
    private readonly long _maxPendingTimeouts;
    internal readonly Action<Action> _taskExecutor;

    internal long _startTime;
    private int _instanceCounted;

    /**
     * Creates a new timer with the default thread factory
     * ({@link Executors#defaultThreadFactory()}), default tick duration, and
     * default number of ticks per wheel.
     */
    public HashedWheelTimer()
        : this(Executors.DefaultThreadFactory())
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
     * @throws IllegalArgumentException if {@code tickDuration} is &lt;= 0
     */
    public HashedWheelTimer(TimeSpan tickDuration)
        : this(Executors.DefaultThreadFactory(), tickDuration)
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
     * @throws IllegalArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(TimeSpan tickDuration, int ticksPerWheel)
        : this(Executors.DefaultThreadFactory(), tickDuration, ticksPerWheel)
    {
    }

    /**
     * Creates a new timer with the default tick duration and default number of
     * ticks per wheel.
     *
     * @param threadFactory a {@link ThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link TimerTask} execution.
     * @throws NullPointerException if {@code threadFactory} is {@code null}
     */
    public HashedWheelTimer(IThreadFactory threadFactory)
        : this(threadFactory, TimeSpan.FromMilliseconds(100))
    {
    }

    /**
     * Creates a new timer with the default number of ticks per wheel.
     *
     * @param threadFactory a {@link ThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link TimerTask} execution.
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws IllegalArgumentException if {@code tickDuration} is &lt;= 0
     */
    public HashedWheelTimer(
        IThreadFactory threadFactory, TimeSpan tickDuration)
        : this(threadFactory, tickDuration, 512)
    {
    }

    /**
     * Creates a new timer.
     *
     * @param threadFactory a {@link ThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link TimerTask} execution.
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @param ticksPerWheel the size of the wheel
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws IllegalArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
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
     * @param threadFactory a {@link ThreadFactory} that creates a
     *                      background {@link Thread} which is dedicated to
     *                      {@link TimerTask} execution.
     * @param tickDuration  the duration between tick
     * @param unit          the time unit of the {@code tickDuration}
     * @param ticksPerWheel the size of the wheel
     * @param leakDetection {@code true} if leak detection should be enabled always,
     *                      if false it will only be enabled if the worker thread is not
     *                      a daemon thread.
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws IllegalArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
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
     * @param threadFactory        a {@link ThreadFactory} that creates a
     *                             background {@link Thread} which is dedicated to
     *                             {@link TimerTask} execution.
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
     * @throws IllegalArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    public HashedWheelTimer(
        IThreadFactory threadFactory,
        TimeSpan tickDuration, int ticksPerWheel, bool leakDetection,
        long maxPendingTimeouts)
        : this(threadFactory, tickDuration, ticksPerWheel, leakDetection, maxPendingTimeouts, static command => command())
    {
    }

    /**
     * Creates a new timer.
     *
     * @param threadFactory        a {@link ThreadFactory} that creates a
     *                             background {@link Thread} which is dedicated to
     *                             {@link TimerTask} execution.
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
     * @param taskExecutor         The {@link Executor} that is used to execute the submitted {@link TimerTask}s.
     *                             The caller is responsible to shutdown the {@link Executor} once it is not needed
     *                             anymore.
     * @throws NullPointerException     if either of {@code threadFactory} and {@code unit} is {@code null}
     * @throws IllegalArgumentException if either of {@code tickDuration} and {@code ticksPerWheel} is &lt;= 0
     */
    // CLR: taskExecutor dispatches a callback Action. Its queue/lifetime belongs to the caller,
    // and synchronous admission failures are logged after the timeout has expired.
    public HashedWheelTimer(
        IThreadFactory threadFactory,
        TimeSpan tickDuration, int ticksPerWheel, bool leakDetection,
        long maxPendingTimeouts, Action<Action> taskExecutor)
    {
        CheckNotNull(threadFactory, "threadFactory");
        CheckPositive(tickDuration, "tickDuration");
        CheckPositive(ticksPerWheel, "ticksPerWheel");
        _taskExecutor = CheckNotNull(taskExecutor, "taskExecutor");

        // Normalize ticksPerWheel to power of two and initialize the wheel.
        _wheel = CreateWheel(ticksPerWheel);
        _mask = _wheel.Length - 1;

        // Convert tickDuration to nanos.
        long duration = AbstractScheduledEventExecutor.ToNanos(tickDuration);

        // Prevent overflow.
        if (duration >= long.MaxValue / _wheel.Length)
        {
            throw new ArgumentException($"tickDuration: {tickDuration} (expected: 0 < tickDuration in nanos < {long.MaxValue / _wheel.Length}");
        }

        if (duration < MILLISECOND_NANOS)
        {
            logger.Warn("Configured tickDuration {} smaller than {}, using 1ms.",
                tickDuration, MILLISECOND_NANOS);
            _tickDuration = MILLISECOND_NANOS;
        }
        else
        {
            _tickDuration = duration;
        }

        _worker = new HashedWheelWorker(this);
        _workerThread = threadFactory.NewThread(_worker.Run);

        _leak = leakDetection || !_workerThread.IsBackground ? leakDetector.Track(this) : null;

        _maxPendingTimeouts = maxPendingTimeouts;

        int instances = Interlocked.Increment(ref INSTANCE_COUNTER);
        _instanceCounted = 1;
        if (instances > INSTANCE_COUNT_LIMIT &&
            Interlocked.CompareExchange(ref WARNED_TOO_MANY_INSTANCES, 1, 0) == 0)
        {
            ReportTooManyInstances();
        }
    }

    ~HashedWheelTimer()
    {
        // This object is going to be GCed and it is assumed the ship has sailed to do a proper shutdown. If
        // we have not yet shutdown then we want to make sure we decrement the active instance count.
        // CLR finalizers also run after a failed constructor. Only decrement
        // the instance count if construction actually registered this timer.
        if (Interlocked.Exchange(ref _workerState, WORKER_STATE_SHUTDOWN) != WORKER_STATE_SHUTDOWN)
        {
            DecrementInstanceCount();
        }
    }

    private void DecrementInstanceCount()
    {
        if (Interlocked.Exchange(ref _instanceCounted, 0) != 0) Interlocked.Decrement(ref INSTANCE_COUNTER);
    }

    // CLR callers can give the timer an explicit using/IDisposable lifetime.
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static HashedWheelBucket[] CreateWheel(int ticksPerWheel)
    {
        ticksPerWheel = MathUtil.FindNextPositivePowerOfTwo(ticksPerWheel);

        HashedWheelBucket[] wheel = new HashedWheelBucket[ticksPerWheel];
        for (int i = 0; i < wheel.Length; i++)
        {
            wheel[i] = new HashedWheelBucket();
        }

        return wheel;
    }

    /**
     * Starts the background thread explicitly.  The background thread will
     * start automatically on demand even if you did not call this method.
     *
     * @throws IllegalStateException if this timer has been
     *                               {@linkplain #stop() stopped} already
     */
    public virtual void Start()
    {
        switch (Volatile.Read(ref _workerState))
        {
            case WORKER_STATE_INIT:
                if (Interlocked.CompareExchange(ref _workerState, WORKER_STATE_STARTED, WORKER_STATE_INIT) == WORKER_STATE_INIT)
                {
                    _workerThread.Start();
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
        while (Volatile.Read(ref _startTime) == 0)
        {
            try
            {
                _startTimeInitialized.Wait();
            }
            catch (ThreadInterruptedException ignore)
            {
                // Ignore - it will be ready very soon.
            }
        }
    }

    public virtual ISet<ITimeout> Stop()
    {
        if (Thread.CurrentThread == _workerThread)
        {
            throw new InvalidOperationException(
                nameof(HashedWheelTimer) +
                ".stop() cannot be called from " +
                nameof(ITimerTask));
        }

        if (Interlocked.CompareExchange(ref _workerState, WORKER_STATE_SHUTDOWN, WORKER_STATE_STARTED) != WORKER_STATE_STARTED)
        {
            // workerState can be 0 or 2 at this moment - let it always be 2.
            if (Interlocked.Exchange(ref _workerState, WORKER_STATE_SHUTDOWN) != WORKER_STATE_SHUTDOWN)
            {
                DecrementInstanceCount();
                if (_leak != null)
                {
                    bool closed = _leak.Close(this);
                    Debug.Assert(closed);
                }
            }

            return new HashSet<ITimeout>();
        }

        try
        {
            bool interrupted = false;
            while (_workerThread.IsAlive)
            {
                // Java interrupt on a terminated thread is harmless. CLR can
                // throw when termination races with this call.
                try { _workerThread.Interrupt(); }
                catch (ThreadStateException) { }
                try
                {
                    _workerThread.Join(100);
                }
                catch (ThreadInterruptedException ignored)
                {
                    interrupted = true;
                }
            }

            if (interrupted)
            {
                Thread.CurrentThread.Interrupt();
            }
        }
        finally
        {
            DecrementInstanceCount();
            if (_leak != null)
            {
                bool closed = _leak.Close(this);
                Debug.Assert(closed);
            }
        }

        var unprocessed = _worker.UnprocessedTimeouts();
        var cancelled = new HashSet<ITimeout>(unprocessed.Count);
        foreach (ITimeout timeout in unprocessed)
        {
            if (timeout.Cancel())
            {
                cancelled.Add(timeout);
            }
        }

        return cancelled;
    }

    public virtual ITimeout NewTimeout(ITimerTask task, TimeSpan delay)
    {
        CheckNotNull(task, "task");

        long pendingTimeoutsCount = Interlocked.Increment(ref _pendingTimeouts);

        if (_maxPendingTimeouts > 0 && pendingTimeoutsCount > _maxPendingTimeouts)
        {
            Interlocked.Decrement(ref _pendingTimeouts);
            throw new RejectedExecutionException("Number of pending timeouts ("
                                                 + pendingTimeoutsCount + ") is greater than or equal to maximum allowed pending "
                                                 + "timeouts (" + _maxPendingTimeouts + ")");
        }

        try { Start(); }
        catch
        {
            Interlocked.Decrement(ref _pendingTimeouts);
            throw;
        }

        // Add the timeout to the timeout queue which will be processed on the next tick.
        // During processing all the queued HashedWheelTimeouts will be added to the correct HashedWheelBucket.
        long delayNano = AbstractScheduledEventExecutor.ToNanos(delay);
        // Match Java long wrap before the positive-delay overflow guard below.
        long deadline = unchecked(SystemTimer.NanoTime() + delayNano - Volatile.Read(ref _startTime));

        // Guard against overflow.
        if (delay.Ticks > 0 && deadline < 0)
        {
            deadline = long.MaxValue;
        }

        HashedWheelTimeout timeout = new HashedWheelTimeout(this, task, deadline);
        ConcurrentQueueOperations.EnqueueUninterruptibly(_timeouts, timeout);

        // stop() might have been called after start() returned, in which case the worker might have already drained
        // the timeouts queue for the last time. If we can still cancel the timeout it was neither expired nor returned
        // by stop(), so reject it as if start() had failed.
        if (Volatile.Read(ref _workerState) == WORKER_STATE_SHUTDOWN &&
            timeout.CompareAndSetState(HashedWheelTimeout.ST_INIT, HashedWheelTimeout.ST_CANCELLED))
        {
            Interlocked.Decrement(ref _pendingTimeouts);
            throw new InvalidOperationException("cannot be started once stopped");
        }
        return timeout;
    }

    /**
     * Returns the number of pending timeouts of this {@link Timer}.
     */
    public virtual long PendingTimeouts()
    {
        return Volatile.Read(ref _pendingTimeouts);
    }

    private static void ReportTooManyInstances()
    {
        if (logger.IsErrorEnabled())
        {
            string resourceType = StringUtil.SimpleClassName(typeof(HashedWheelTimer));
            logger.Error("You are creating too many " + resourceType + " instances. " +
                         resourceType + " is a shared resource that must be reused across the JVM, " +
                         "so that only a few instances are created.");
        }
    }
}
