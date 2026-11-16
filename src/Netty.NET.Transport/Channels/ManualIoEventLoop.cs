/*
 * Copyright 2025 The Netty Project
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * {@link IoEventLoop} implementation that is owned by the user and so needs to be driven by the user manually with the
 * given {@link Thread}. This means that the user is responsible to call either {@link #runNow()} or {@link #run(long)}
 * to execute IO and tasks that were submitted to this {@link IoEventLoop}.
 * <p>
 * This is for <strong>advanced use-cases only</strong>, where the user wants to own the {@link Thread} that drives the
 * {@link IoEventLoop} to also do other work. Care must be taken that the {@link #runNow() or
 * {@link #waitAndRun()}} methods are called in a timely fashion.
 */
public class ManualIoEventLoop : AbstractScheduledEventExecutor, IIoEventLoop
{
    private static new readonly Action WAKEUP_TASK = () =>
    {
        // NOOP
    };
    private const int ST_STARTED = 0;
    private const int ST_SHUTTING_DOWN = 1;
    private const int ST_SHUTDOWN = 2;
    private const int ST_TERMINATED = 3;

    private readonly AtomicInteger _state;
    private readonly TaskCompletionSource _terminationFuture = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // CLR: LinkedBlockingQueue is the unbounded IQueue that fetchFromScheduledTaskQueue accepts; it is only polled.
    private readonly IQueue<Action> _taskQueue = new LinkedBlockingQueue<Action>(int.MaxValue, ReferenceEqualityComparer.Instance);
    private readonly IIoHandlerContext _nonBlockingContext;
    private readonly BlockingIoHandlerContext _blockingContext;
    private readonly IIoEventLoopGroup _parent;
    private readonly AtomicReference<Thread> _owningThread;
    private readonly IIoHandler _handler;
    private readonly Ticker _ticker;

    private long _gracefulShutdownQuietPeriod;
    private long _gracefulShutdownTimeout;
    private long _gracefulShutdownStartTime;
    private long _lastExecutionTime;
    private bool _initialized;
    // CLR: a native close offloaded to a TaskScheduler reserves its owner callback; termination waits for it.
    private int _pendingCloseCompletions;

    /**
     * This allows to specify additional blocking conditions which will be used by the {@link IoHandler} to decide
     * whether it is allowed to block or not.
     */
    protected virtual bool CanBlock()
    {
        return true;
    }

    /**
     * Create a new {@link IoEventLoop} that is owned by the user and so needs to be driven by the user with the given
     * {@link Thread}. This means that the user is responsible to call either {@link #runNow()} or
     * {@link #run(long)} to execute IO or tasks that were submitted to this {@link IoEventLoop}.
     *
     * @param owningThread      the {@link Thread} that executes the IO and tasks for this {@link IoEventLoop}. The
     *                          user will use this {@link Thread} to call {@link #runNow()} or {@link #run(long)} to
     *                          make progress.
     * @param factory           the {@link IoHandlerFactory} that will be used to create the {@link IoHandler} that is
     *                          used by this {@link IoEventLoop}.
     */
    public ManualIoEventLoop(Thread owningThread, IIoHandlerFactory factory) : this(null, owningThread, factory)
    {
    }

    /**
     * Create a new {@link IoEventLoop} that is owned by the user and so needs to be driven by the user with the given
     * {@link Thread}. This means that the user is responsible to call either {@link #runNow()} or
     * {@link #run(long)} to execute IO or tasks that were submitted to this {@link IoEventLoop}.
     *
     * @param parent            the parent {@link IoEventLoopGroup} or {@code null} if no parent.
     * @param owningThread      the {@link Thread} that executes the IO and tasks for this {@link IoEventLoop}. The
     *                          user will use this {@link Thread} to call {@link #runNow()} or {@link #run(long)} to
     *                          make progress. If {@code null}, must be set later using
     *                          {@link #setOwningThread(Thread)}.
     * @param factory           the {@link IoHandlerFactory} that will be used to create the {@link IoHandler} that is
     *                          used by this {@link IoEventLoop}.
     */
    public ManualIoEventLoop(IIoEventLoopGroup parent, Thread owningThread, IIoHandlerFactory factory)
        : this(parent, owningThread, factory, global::Netty.NET.Common.Concurrent.Ticker.SystemTicker())
    {
    }

    /**
     * Create a new {@link IoEventLoop} that is owned by the user and so needs to be driven by the user with the given
     * {@link Thread}. This means that the user is responsible to call either {@link #runNow()} or
     * {@link #run(long)} to execute IO or tasks that were submitted to this {@link IoEventLoop}.
     *
     * @param parent            the parent {@link IoEventLoopGroup} or {@code null} if no parent.
     * @param owningThread      the {@link Thread} that executes the IO and tasks for this {@link IoEventLoop}. The
     *                          user will use this {@link Thread} to call {@link #runNow()} or {@link #run(long)} to
     *                          make progress. If {@code null}, must be set later using
     *                          {@link #setOwningThread(Thread)}.
     * @param factory           the {@link IoHandlerFactory} that will be used to create the {@link IoHandler} that is
     *                          used by this {@link IoEventLoop}.
     * @param ticker            The {@link #ticker()} to use for this event loop. Note that the {@link IoHandler} does
     *                          not use the ticker, so if the ticker advances faster than system time, you may have to
     *                          {@link #wakeup()} this event loop manually.
     */
    public ManualIoEventLoop(IIoEventLoopGroup parent, Thread owningThread, IIoHandlerFactory factory, Ticker ticker)
        : base(parent)
    {
        _parent = parent;
        _owningThread = new AtomicReference<Thread>(owningThread);
        _nonBlockingContext = new NonBlockingIoHandlerContext(this);
        _blockingContext = new BlockingIoHandlerContext(this);
        _handler = ObjectUtil.CheckNotNull(factory, "factory").NewHandler(this);
        _ticker = ObjectUtil.CheckNotNull(ticker, "ticker");
        _state = new AtomicInteger(ST_STARTED);
    }

    public sealed override Ticker Ticker()
    {
        return _ticker;
    }

    /**
     * Poll and run tasks from the task queue, until the task queue is empty or the given deadline is exceeded.<br>
     * If {@code timeoutNanos} is less or equals 0, no deadline is applied.
     *
     * @param timeoutNanos the maximum time in nanoseconds to run tasks.
     */
    public int RunNonBlockingTasks(long timeoutNanos)
    {
        return RunAllTasks(timeoutNanos, true);
    }

    private int RunAllTasks(long timeoutNanos, bool setCurrentExecutor)
    {
        Debug.Assert(InEventLoop());
        IQueue<Action> taskQueue = _taskQueue;
        // since the taskQueue is unbounded we don't need to keep on calling this while draining it.
        bool alwaysTrue = FetchFromScheduledTaskQueue(taskQueue);
        Debug.Assert(alwaysTrue);
        if (!taskQueue.TryDequeue(out Action task))
        {
            return 0;
        }
        IEventExecutor old = setCurrentExecutor ? ThreadExecutorMap.SetCurrentExecutor(this) : null;
        try
        {
            long deadline = timeoutNanos > 0 ? _ticker.NanoTime() + timeoutNanos : 0;
            int runTasks = 0;
            long lastExecutionTime;
            Ticker ticker = _ticker;
            for (;;)
            {
                SafeExecute(task);

                runTasks++;

                if (timeoutNanos > 0)
                {
                    lastExecutionTime = ticker.NanoTime();
                    if (unchecked(lastExecutionTime - deadline) >= 0)
                    {
                        break;
                    }
                }

                if (!taskQueue.TryDequeue(out task))
                {
                    lastExecutionTime = ticker.NanoTime();
                    break;
                }
            }
            _lastExecutionTime = lastExecutionTime;
            return runTasks;
        }
        finally
        {
            if (setCurrentExecutor)
            {
                ThreadExecutorMap.SetCurrentExecutor(old);
            }
        }
    }

    private int Run(IIoHandlerContext context, long runAllTasksTimeoutNanos)
    {
        if (!_initialized)
        {
            if (_owningThread.Get() == null)
            {
                throw new InvalidOperationException("Owning thread not set");
            }
            _initialized = true;
            _handler.Initialize();
        }
        IEventExecutor old = ThreadExecutorMap.SetCurrentExecutor(this);
        try
        {
            if (IsShuttingDown())
            {
                if (_terminationFuture.Task.IsCompleted)
                {
                    // Already completely terminated
                    return 0;
                }
                return RunAllTasksBeforeDestroy();
            }
            int ioTasks = _handler.Run(context);
            // Now run all tasks.
            if (runAllTasksTimeoutNanos < 0)
            {
                return ioTasks;
            }
            Debug.Assert(runAllTasksTimeoutNanos >= 0);
            return ioTasks + RunAllTasks(runAllTasksTimeoutNanos, false);
        }
        finally
        {
            ThreadExecutorMap.SetCurrentExecutor(old);
        }
    }

    private int RunAllTasksBeforeDestroy()
    {
        // Run all tasks before prepare to destroy.
        int run = RunAllTasks(-1, false);
        _handler.PrepareToDestroy();
        // CLR: an offloaded native close keeps the loop alive until its reserved owner callback has run.
        if (ConfirmShutdown() && _pendingCloseCompletions == 0)
        {
            // Destroy the handler now and run all remaining tasks.
            try
            {
                _handler.Destroy();
                for (;;)
                {
                    int r = RunAllTasks(-1, false);
                    run += r;
                    if (r == 0)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _state.Set(ST_TERMINATED);
                _terminationFuture.TrySetResult();
            }
        }
        return run;
    }

    /**
     * Executes all ready IO and tasks for this {@link IoEventLoop}.
     * This methods will <strong>NOT</strong> block and wait for IO / tasks to be ready, it will just
     * return directly if there is nothing to do.
     * <p>
     * <strong>Must be called from the owning {@link Thread} that was passed as a parameter on construction.</strong>
     * <p>
     *
     * @param runAllTasksTimeoutNanos the maximum time in nanoseconds to run tasks.
     *                                If {@code = 0}, no timeout is applied; if {@code < 0} it just perform I/O tasks.
     * @return the number of IO and tasks executed.
     * @throws IllegalStateException if the method is not called from the owning {@link Thread}.
     */
    public int RunNow(long runAllTasksTimeoutNanos)
    {
        CheckCurrentThread();
        return Run(_nonBlockingContext, runAllTasksTimeoutNanos);
    }

    /**
     * Run all ready IO and tasks for this {@link IoEventLoop}.
     * This methods will <strong>NOT</strong> block and wait for IO / tasks to be ready, it will just
     * return directly if there is nothing to do.
     * <p>
     * <strong>Must be called from the owning {@link Thread} that was passed as a parameter on construction.</strong>
     *
     * @return the number of IO and tasks executed.
     */
    public int RunNow()
    {
        CheckCurrentThread();
        return Run(_nonBlockingContext, 0);
    }

    /**
     * Run all ready IO and tasks for this {@link IoEventLoop}.
     * This methods will block and wait for IO / tasks to be ready if there is nothing to process atm for the given
     * {@code waitNanos}.
     * <p>
     * <strong>Must be called from the owning {@link Thread} that was passed as an parameter on construction.</strong>
     *
     * @param runAllTasksTimeoutNanos the maximum time in nanoseconds to run tasks.
     *                                If {@code = 0}, no timeout is applied; if {@code < 0} it just perform I/O tasks.
     * @param waitNanos the maximum amount of nanoseconds to wait before returning. IF {@code 0} it will block until
     *                  there is some IO / tasks ready, if {@code -1} will not block at all and just return directly
     *                  if there is nothing to run (like {@link #runNow()}).
     * @return          the number of IO and tasks executed.
     */
    public int Run(long waitNanos, long runAllTasksTimeoutNanos)
    {
        CheckCurrentThread();

        IIoHandlerContext context;
        if (waitNanos < 0)
        {
            context = _nonBlockingContext;
        }
        else
        {
            context = _blockingContext;
            _blockingContext.MaxBlockingNanos = waitNanos == 0 ? long.MaxValue : waitNanos;
        }
        return Run(context, runAllTasksTimeoutNanos);
    }

    /**
     * Run all ready IO and tasks for this {@link IoEventLoop}.
     * This methods will block and wait for IO / tasks to be ready if there is nothing to process atm for the given
     * {@code waitNanos}.
     * <p>
     * <strong>Must be called from the owning {@link Thread} that was passed as an parameter on construction.</strong>
     *
     * @param waitNanos the maximum amount of nanoseconds to wait before returning. IF {@code 0} it will block until
     *                  there is some IO / tasks ready, if {@code -1} will not block at all and just return directly
     *                  if there is nothing to run (like {@link #runNow()}).
     * @return          the number of IO and tasks executed.
     */
    public int Run(long waitNanos)
    {
        return Run(waitNanos, 0);
    }

    // CLR: InvalidOperationException replaces IllegalStateException.
    private void CheckCurrentThread()
    {
        if (!InEventLoop(Thread.CurrentThread))
        {
            throw new InvalidOperationException();
        }
    }

    /**
     * Force a wakeup and so the {@link #run(long)} method will unblock and return even if there was nothing to do.
     */
    public void Wakeup()
    {
        if (IsShuttingDown())
        {
            return;
        }
        _handler.Wakeup();
    }

    public sealed override IIoEventLoop Next()
    {
        return this;
    }

    IEventLoop IEventLoopGroup.Next() => this;

    public sealed override IEventExecutorGroup Parent()
    {
        return _parent;
    }

    /**
     * @deprecated Use {@link #register(IoHandle)}
     */
    // CLR: the Task-returning RegisterAsync(IChannel) replaces the deprecated register(Channel),
    // register(ChannelPromise) and register(Channel, ChannelPromise) overloads.
    public Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        ObjectUtil.CheckNotNull(channel, "channel");
        return channel.RegisterAsync(this, cancellationToken);
    }

    // CLR: Task<IIoRegistration> replaces Future<IoRegistration>; the TaskCompletionSource is the promise.
    public Task<IIoRegistration> RegisterAsync(IIoHandle handle)
    {
        var promise = new TaskCompletionSource<IIoRegistration>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (InEventLoop())
        {
            RegisterForIo0(handle, promise);
        }
        else
        {
            Execute(() => RegisterForIo0(handle, promise));
        }

        return promise.Task;
    }

    private void RegisterForIo0(IIoHandle handle, TaskCompletionSource<IIoRegistration> promise)
    {
        Debug.Assert(InEventLoop());
        IIoRegistration registration;
        try
        {
            registration = _handler.Register(handle);
        }
        catch (Exception e)
        {
            promise.SetException(e);
            return;
        }
        promise.SetResult(registration);
    }

    public bool IsCompatible(Type handleType)
    {
        return _handler.IsCompatible(handleType);
    }

    public bool IsIoType(Type handlerType)
    {
        return _handler.GetType() == handlerType;
    }

    public sealed override bool InEventLoop(Thread thread)
    {
        return _owningThread.Get() == thread;
    }

    /**
     * Set the owning thread that will call {@link #run}. May only be called once, and only if the owning thread was
     * not set in the constructor already.
     *
     * @param owningThread The owning thread
     */
    public void SetOwningThread(Thread owningThread)
    {
        ObjectUtil.CheckNotNull(owningThread, "owningThread");
        if (!_owningThread.CompareAndSet(null, owningThread))
        {
            throw new InvalidOperationException("Owning thread already set");
        }
    }

    private void Shutdown0(long quietPeriod, long timeout, int shutdownState)
    {
        bool inEventLoop = InEventLoop();
        bool wakeup;
        int oldState;
        for (;;)
        {
            if (IsShuttingDown())
            {
                return;
            }
            int newState;
            wakeup = true;
            oldState = _state.Get();
            if (inEventLoop)
            {
                newState = shutdownState;
            }
            else if (oldState == ST_STARTED)
            {
                newState = shutdownState;
            }
            else
            {
                newState = oldState;
                wakeup = false;
            }

            if (_state.CompareAndSet(oldState, newState))
            {
                break;
            }
        }
        if (quietPeriod != -1)
        {
            Volatile.Write(ref _gracefulShutdownQuietPeriod, quietPeriod);
        }
        if (timeout != -1)
        {
            Volatile.Write(ref _gracefulShutdownTimeout, timeout);
        }

        if (wakeup)
        {
            // same as AbstractScheduledEventExecutor.WAKEUP_TASK
            _taskQueue.TryEnqueue(WAKEUP_TASK);
            _handler.Wakeup();
        }
    }

    // CLR: TimeSpan replaces the (long, TimeUnit) pair; ArgumentException replaces IllegalArgumentException.
    public sealed override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        ObjectUtil.CheckPositiveOrZero(quietPeriod.Ticks, "quietPeriod");
        if (timeout < quietPeriod)
        {
            throw new ArgumentException(
                    "timeout: " + timeout + " (expected >= quietPeriod (" + quietPeriod + "))");
        }

        Shutdown0(ToNanos(quietPeriod), ToNanos(timeout), ST_SHUTTING_DOWN);
        return TerminationFuture;
    }

    private static long ToNanos(TimeSpan duration) =>
        duration.Ticks > long.MaxValue / 100 ? long.MaxValue : duration.Ticks * 100;

    [Obsolete]
    public sealed override void Shutdown()
    {
        Shutdown0(-1, -1, ST_SHUTDOWN);
    }

    // CLR: StopAsync is the Task-returning shutdown() of the Common executor contract.
    public sealed override Task StopAsync()
    {
        Shutdown0(-1, -1, ST_SHUTDOWN);
        return TerminationFuture;
    }

    public sealed override Task TerminationFuture => _terminationFuture.Task;

    public sealed override bool IsShuttingDown()
    {
        return _state.Get() >= ST_SHUTTING_DOWN;
    }

    public sealed override bool IsShutdown()
    {
        return _state.Get() >= ST_SHUTDOWN;
    }

    public sealed override bool IsTerminated()
    {
        return _state.Get() == ST_TERMINATED;
    }

    // CLR: invokeAny/invokeAll have no counterpart on the Task-based executor; the synchronous AwaitTermination is
    // the blocking call that would deadlock when made by the owner before termination.
    public sealed override bool AwaitTermination(TimeSpan timeout)
    {
        if (!_terminationFuture.Task.IsCompleted)
        {
            // We need to check if the method was called from within the EventLoop as this would cause a deadlock.
            ThrowIfInEventLoop("awaitTermination");
        }
        return _terminationFuture.Task.Wait(timeout);
    }

    private void ThrowIfInEventLoop(string method)
    {
        if (InEventLoop())
        {
            throw new RejectedExecutionException(
                    "Calling " + method + " from within the EventLoop is not allowed as it would deadlock");
        }
    }

    // CLR: RejectedExecutionException is Common's port of the Java exception.
    public sealed override void Execute(Action command)
    {
        ObjectUtil.CheckNotNull(command, "command");
        bool inEventLoop = InEventLoop();
        if (inEventLoop)
        {
            if (IsShutdown())
            {
                throw new RejectedExecutionException("event executor terminated");
            }
        }
        _taskQueue.TryEnqueue(command);
        if (!inEventLoop)
        {
            if (IsShutdown())
            {
                bool reject = false;
                try
                {
                    if (_taskQueue.TryRemove(command))
                    {
                        reject = true;
                    }
                }
                catch (NotSupportedException)
                {
                    // The task queue does not support removal so the best thing we can do is to just move on and
                    // hope we will be able to pick-up the task before its completely terminated.
                    // In worst case we will log on termination.
                }
                if (reject)
                {
                    throw new RejectedExecutionException("event executor terminated");
                }
            }
            _handler.Wakeup();
        }
    }

    // CLR-only native-close admission; public task submission remains closed after StopAsync.
    internal Action<Action> ReserveCloseCompletion()
    {
        if (!InEventLoop()) throw new InvalidOperationException("Native close must be reserved by the event-loop owner.");
        _pendingCloseCompletions++;
        return completion =>
        {
            _taskQueue.TryEnqueue(() =>
            {
                try { completion(); }
                finally { _pendingCloseCompletions--; }
            });
            _handler.Wakeup();
        };
    }

    private bool HasTasks()
    {
        return !_taskQueue.IsEmpty();
    }

    // CLR: InvalidOperationException replaces IllegalStateException.
    private bool ConfirmShutdown()
    {
        if (!IsShuttingDown())
        {
            return false;
        }

        if (!InEventLoop())
        {
            throw new InvalidOperationException("must be invoked from an event loop");
        }

        CancelScheduledTasks();

        if (_gracefulShutdownStartTime == 0)
        {
            _gracefulShutdownStartTime = _ticker.NanoTime();
        }

        if (RunAllTasks(-1, false) > 0)
        {
            if (IsShutdown())
            {
                // Executor shut down - no new tasks anymore.
                return true;
            }

            // There were tasks in the queue. Wait a little bit more until no tasks are queued for the quiet period or
            // terminate if the quiet period is 0.
            // See https://github.com/netty/netty/issues/4241
            if (Volatile.Read(ref _gracefulShutdownQuietPeriod) == 0)
            {
                return true;
            }
            return false;
        }

        long nanoTime = _ticker.NanoTime();

        if (IsShutdown() || unchecked(nanoTime - _gracefulShutdownStartTime) > Volatile.Read(ref _gracefulShutdownTimeout))
        {
            return true;
        }

        if (unchecked(nanoTime - _lastExecutionTime) <= Volatile.Read(ref _gracefulShutdownQuietPeriod))
        {
            try
            {
                Thread.Sleep(100);
            }
            catch (ThreadInterruptedException)
            {
                // Ignore
            }

            return false;
        }

        // No tasks were added for last quiet period - hopefully safe to shut down.
        // (Hopefully because we really cannot make a guarantee that there will be no execute() calls by a user.)
        return true;
    }

    // CLR: the anonymous non-blocking IoHandlerContext of the Java field becomes a nested class.
    private sealed class NonBlockingIoHandlerContext(ManualIoEventLoop loop) : IIoHandlerContext
    {
        public bool CanBlock()
        {
            Debug.Assert(loop.InEventLoop());
            return false;
        }

        public long DelayNanos(long currentTimeNanos)
        {
            Debug.Assert(loop.InEventLoop());
            return 0;
        }

        public long DeadlineNanos()
        {
            Debug.Assert(loop.InEventLoop());
            return -1;
        }
    }

    private sealed class BlockingIoHandlerContext(ManualIoEventLoop loop) : IIoHandlerContext
    {
        // this is a positive amount of nanos or Long.MAX_VALUE for no limit
        internal long MaxBlockingNanos = long.MaxValue;

        public bool CanBlock()
        {
            Debug.Assert(loop.InEventLoop());
            return !loop.HasTasks() && !loop.HasScheduledTasks() && loop.CanBlock();
        }

        public long DelayNanos(long currentTimeNanos)
        {
            Debug.Assert(loop.InEventLoop());
            return Math.Min(MaxBlockingNanos, loop.DelayNanos(currentTimeNanos, MaxBlockingNanos));
        }

        public long DeadlineNanos()
        {
            Debug.Assert(loop.InEventLoop());
            long next = loop.NextScheduledTaskDeadlineNanos();
            if (MaxBlockingNanos == long.MaxValue)
            {
                // next == -1? -1 : next i.e. return next
                return next;
            }
            long now = loop._ticker.NanoTime();
            // we cannot just check Math.min as nanoTime can be negative or wrap around!
            if (next == -1 || unchecked(next - now) > MaxBlockingNanos)
            {
                return unchecked(now + MaxBlockingNanos);
            }
            return next;
        }
    }
}
