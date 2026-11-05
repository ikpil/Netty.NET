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
/// <summary>An event loop pumped by its owning thread.</summary>
public sealed class ManualIoEventLoop : AbstractScheduledEventExecutor, IEventLoop, IEventLoopGroup
{
    private const int Started = 0;
    private const int ShuttingDown = 1;
    private const int ShutdownState = 2;
    private const int Terminated = 3;

    private readonly object _gate = new();
    private readonly LinkedBlockingQueue<Action> _tasks = new(int.MaxValue);
    private readonly Queue<(AbstractChannel Channel, Func<bool> Dispatch)> _io = new();
    private readonly HashSet<AbstractChannel> _channels = new(ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource _termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Ticker _ticker;
    private Thread _owner;
    private int _state;
    private bool _wakeRequested;
    private bool _running;
    private long _quietPeriod;
    private long _shutdownTimeout;
    private long? _shutdownStarted;
    private long _lastExecution;

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
    public ManualIoEventLoop(Thread owningThread, IEventLoopGroup parent = null, Ticker ticker = null) : base(parent)
    {
        _owner = owningThread;
        _ticker = ticker ?? global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
    }

    public override Ticker Ticker() => _ticker;
    public override ManualIoEventLoop Next() => this;
    IEventLoop IEventLoopGroup.Next() => this;
    public override bool InEventLoop(Thread thread) => thread != null && ReferenceEquals(Volatile.Read(ref _owner), thread);

    /**
     * Set the owning thread that will call {@link #run}. May only be called once, and only if the owning thread was
     * not set in the constructor already.
     *
     * @param owningThread The owning thread
     */
    public void SetOwningThread(Thread owningThread)
    {
        ArgumentNullException.ThrowIfNull(owningThread);
        if (Interlocked.CompareExchange(ref _owner, owningThread, null) != null)
            throw new InvalidOperationException("The owning thread is already set.");
    }

    public Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.RegisterAsync(this, cancellationToken);
    }

    internal void ChannelRegistered(AbstractChannel channel)
    {
        lock (_gate)
        {
            if (IsShutdown()) throw new RejectedExecutionException("The event loop is shut down.");
            _channels.Add(channel);
        }
    }

    internal void ChannelUnregistered(AbstractChannel channel)
    {
        lock (_gate) _channels.Remove(channel);
    }

    public override void Execute(Action task)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (_gate)
        {
            if (IsShutdown()) throw new RejectedExecutionException("The event loop is shut down.");
            _tasks.TryEnqueue(task);
            Monitor.Pulse(_gate);
        }
    }

    internal Task SubmitIoAsync(AbstractChannel channel, Func<(Task Completion, bool Handled)> dispatch,
        bool captureContext = true)
    {
        var context = captureContext ? ExecutionContext.Capture() : null;
        var completion = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (IsShutdown())
                throw new RejectedExecutionException("The event loop is shut down.");
            _io.Enqueue((channel, Invoke));
            Monitor.Pulse(_gate);
        }
        return completion.Task.Unwrap();

        bool Invoke()
        {
            bool handled = false;
            if (context == null)
                Dispatch();
            else
                ExecutionContext.Run(context, _ => Dispatch(), null);
            return handled;

            void Dispatch()
            {
                try
                {
                    var result = dispatch();
                    completion.SetResult(result.Completion);
                    handled = result.Handled;
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            }
        }
    }

    /**
     * Force a wakeup and so the {@link #run(long)} method will unblock and return even if there was nothing to do.
     */
    public void Wakeup()
    {
        lock (_gate)
        {
            if (IsShuttingDown()) return;
            _wakeRequested = true;
            Monitor.Pulse(_gate);
        }
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
    public int RunNow() => Run(-1);
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
    public int RunNow(long runAllTasksTimeoutNanos) => Run(-1, runAllTasksTimeoutNanos);
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
    public int Run(long waitNanos) => Run(waitNanos, 0);

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
    /// <summary>
    /// A zero wait permits indefinite blocking. A negative wait pumps without blocking.
    /// A negative task budget processes socket I/O completions only.
    /// </summary>
    public int Run(long waitNanos, long runAllTasksTimeoutNanos)
    {
        EnterRun();
        var previous = ThreadExecutorMap.SetCurrentExecutor(this);
        try
        {
            // Already completely terminated
            if (IsTerminated()) return 0;
            if (!IsShuttingDown() && waitNanos >= 0) WaitForWork(waitNanos);
            if (IsShuttingDown())
            {
                if (waitNanos >= 0) WaitForShutdownProgress(waitNanos);
                return RunShutdown();
            }

            int ioCount = RunIo();
            // Now run all tasks.
            return runAllTasksTimeoutNanos < 0 ? ioCount : ioCount + RunTasks(runAllTasksTimeoutNanos);
        }
        finally
        {
            ThreadExecutorMap.SetCurrentExecutor(previous);
            _running = false;
        }
    }

    /**
     * Poll and run tasks from the task queue, until the task queue is empty or the given deadline is exceeded.<br>
     * If {@code timeoutNanos} is less or equals 0, no deadline is applied.
     *
     * @param timeoutNanos the maximum time in nanoseconds to run tasks.
     */
    public int RunNonBlockingTasks(long timeoutNanos)
    {
        EnterRun();
        var previous = ThreadExecutorMap.SetCurrentExecutor(this);
        try { return IsTerminated() ? 0 : RunTasks(timeoutNanos); }
        finally
        {
            ThreadExecutorMap.SetCurrentExecutor(previous);
            _running = false;
        }
    }

    private void EnterRun()
    {
        if (!InEventLoop()) throw new InvalidOperationException("The event loop must be driven by its owning thread.");
        if (_running) throw new InvalidOperationException("The event loop is already running.");
        _running = true;
    }

    private void WaitForWork(long waitNanos)
    {
        var clock = global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
        long started = clock.NanoTime();
        lock (_gate)
        {
            while (!IsShuttingDown() && !_wakeRequested && _tasks.IsEmpty() && _io.Count == 0)
            {
                // we cannot just check Math.min as nanoTime can be negative or wrap around!
                // Compare the remaining durations after subtracting their respective clocks.
                long remaining = waitNanos == 0 ? long.MaxValue : waitNanos - unchecked(clock.NanoTime() - started);
                long scheduled = NextScheduledTaskNano();
                if (scheduled >= 0) remaining = Math.Min(remaining, scheduled);
                if (remaining <= 0) break;

                WaitForSignal(remaining);
            }

            _wakeRequested = false;
        }
    }

    private void WaitForShutdownProgress(long waitNanos)
    {
        lock (_gate)
        {
            if (_state != ShuttingDown || _shutdownStarted is not long started || !_tasks.IsEmpty() || _io.Count != 0)
                return;
            long now = _ticker.NanoTime();
            long remaining = Math.Min(_shutdownTimeout - unchecked(now - started),
                _quietPeriod - unchecked(now - _lastExecution));
            if (waitNanos > 0) remaining = Math.Min(remaining, waitNanos);
            if (remaining > 0) WaitForSignal(remaining);
        }
    }

    private void WaitForSignal(long remainingNanos)
    {
        // this is a positive amount of nanos or Long.MAX_VALUE for no limit
        int milliseconds = remainingNanos == long.MaxValue ? Timeout.Infinite
            : (int)Math.Min(int.MaxValue, 1 + (remainingNanos - 1) / 1_000_000);
        Monitor.Wait(_gate, milliseconds);
    }

    private int RunTasks(long timeoutNanos)
    {
        // since the taskQueue is unbounded we don't need to keep on calling this while draining it.
        FetchFromScheduledTaskQueue(_tasks);
        long started = _ticker.NanoTime();
        int count = 0;
        while (_tasks.TryDequeue(out Action task))
        {
            SafeExecute(task);
            count++;
            _lastExecution = _ticker.NanoTime();
            if (timeoutNanos > 0 && unchecked(_lastExecution - started) >= timeoutNanos) break;
        }
        return count;
    }

    /**
     * @return          the number of {@link IoHandle} for which I/O was handled.
     *                  Internal events such as wakeups and timer expirations must not be included in this count.
     */
    private int RunIo()
    {
        int remaining;
        lock (_gate) remaining = _io.Count;
        if (remaining == 0) return 0;

        // Only drain the current snapshot. Socket operations may re-arm or publish another
        // completion, and ordinary tasks must still get their turn.
        HashSet<AbstractChannel> handled = null;
        while (remaining-- > 0)
        {
            (AbstractChannel Channel, Func<bool> Dispatch) entry;
            lock (_gate) entry = _io.Dequeue();
            if (entry.Dispatch())
                (handled ??= new HashSet<AbstractChannel>(ReferenceEqualityComparer.Instance)).Add(entry.Channel);
        }
        return handled?.Count ?? 0;
    }

    public override Task Termination => _termination.Task;
    public override bool IsShuttingDown() => Volatile.Read(ref _state) >= ShuttingDown;
    public override bool IsShutdown() => Volatile.Read(ref _state) >= ShutdownState;
    public override bool IsTerminated() => Volatile.Read(ref _state) == Terminated;

    public override bool AwaitTermination(TimeSpan timeout)
    {
        // We need to check if the method was called from within the EventLoop as this would cause a deadlock.
        if (InEventLoop() && !Termination.IsCompleted)
            throw new InvalidOperationException("The owning thread must drive the event loop to termination.");
        return Termination.Wait(timeout);
    }

    [Obsolete]
    public override void Shutdown() => StopAsync();

    public override Task StopAsync() => RequestShutdown(0, 0, ShutdownState);

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quietPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, quietPeriod);
        return RequestShutdown(ToNanoseconds(quietPeriod), ToNanoseconds(timeout), ShuttingDown);
    }

    private static long ToNanoseconds(TimeSpan duration) =>
        duration.Ticks > long.MaxValue / 100 ? long.MaxValue : duration.Ticks * 100;

    private Task RequestShutdown(long quietPeriod, long timeout, int state)
    {
        lock (_gate)
        {
            if (_state == Started || (_state == ShuttingDown && state == ShutdownState))
            {
                _quietPeriod = quietPeriod;
                _shutdownTimeout = timeout;
                Volatile.Write(ref _state, state);
                Monitor.Pulse(_gate);
            }
        }
        return Termination;
    }

    private int RunShutdown()
    {
        long started = _shutdownStarted ??= _ticker.NanoTime();
        // Run all tasks before prepare to destroy.
        int count = RunTasks(0);
        CloseRegisteredChannels();
        count += RunIo();
        CancelScheduledTasks();

        lock (_gate)
        {
            long now = _ticker.NanoTime();
            // There were tasks in the queue. Wait a little bit more until no tasks are queued for the quiet period or
            // terminate if the quiet period is 0.
            // See https://github.com/netty/netty/issues/4241
            if (_state == ShuttingDown && _quietPeriod > 0 &&
                unchecked(now - started) < _shutdownTimeout &&
                (!_tasks.IsEmpty() || _io.Count != 0 || unchecked(now - _lastExecution) <= _quietPeriod))
                return count;

            // Executor shut down - no new tasks anymore.
            Volatile.Write(ref _state, ShutdownState);
        }

        // Execute work accepted before shutdown admission closed.
        count += RunTasks(0);
        CloseRegisteredChannels();
        count += RunIo();
        CancelScheduledTasks();
        Volatile.Write(ref _state, Terminated);
        _termination.SetResult();
        return count;
    }

    private void CloseRegisteredChannels()
    {
        AbstractChannel[] channels;
        lock (_gate)
        {
            if (_channels.Count == 0) return;
            channels = new AbstractChannel[_channels.Count];
            _channels.CopyTo(channels);
        }

        foreach (var channel in channels)
            SafeExecute(() => channel.CloseTransportAsync().GetAwaiter().GetResult());
    }
}
