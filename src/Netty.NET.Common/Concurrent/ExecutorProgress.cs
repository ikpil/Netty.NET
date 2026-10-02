/*
 * Copyright 2013 The Netty Project
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/// <summary>Reports transfer progress on an executor while the supplied Task remains the result owner.</summary>
/// <remarks>
/// Reports and completion callbacks use one ordered notification queue, even on an unordered executor.
/// Constructor/producer ExecutionContext is not captured; callback changes are isolated on the executor.
/// Recursive reports are queued after the current report's observers, keeping the callback stack bounded.
/// Disposing stops this observation, not the underlying operation; a current callback may still return.
/// </remarks>
public sealed class ExecutorProgress : IProgress<TransferProgress>, IDisposable
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.getInstance(typeof(ExecutorProgress));
    private readonly object _gate = new();
    private IEventExecutor _executor;
    private Task _operation;
    private readonly int _maxPendingReports;
    private readonly Queue<Notification> _notifications = new();
    private readonly TaskCompletionSource _notificationsCompleted;
    private readonly LinkedList<ProgressRegistration> _observers = new();
    private bool _drainScheduled;
    private bool _closed;
    private bool _disposed;

    /// <summary>Creates an observation whose callbacks can be registered while the operation is open.</summary>
    public ExecutorProgress(IEventExecutor executor, Task operation, int maxPendingReports = int.MaxValue)
        : this(executor, operation, null, null, maxPendingReports, true) { }

    /// <summary>Creates an IProgress reporter for an existing native operation.</summary>
    /// <param name="executor">Executor used for observer invocation.</param>
    /// <param name="operation">The Task whose producer owns completion, failure and cancellation.</param>
    /// <param name="progress">Progress callbacks; multicast delegates are notified in registration order.</param>
    /// <param name="completed">Optional completion callbacks, after accepted progress notifications.</param>
    /// <param name="maxPendingReports">Maximum buffered reports; TryReport returns false when this observation is full.</param>
    public ExecutorProgress(IEventExecutor executor, Task operation, Action<TransferProgress> progress,
        Action<Task> completed = null, int maxPendingReports = int.MaxValue)
        : this(executor, operation, progress, completed, maxPendingReports, false) { }

    private ExecutorProgress(IEventExecutor executor, Task operation, Action<TransferProgress> progress,
        Action<Task> completed, int maxPendingReports, bool empty)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(operation);
        if (!empty) ArgumentNullException.ThrowIfNull(progress);
        if (maxPendingReports <= 0) throw new ArgumentOutOfRangeException(nameof(maxPendingReports));
        _executor = executor;
        _operation = operation;
        _maxPendingReports = maxPendingReports;
        // A retained notification Task keeps its pending dispatcher alive. Without
        // that Task or reporter, the weak operation continuation permits collection.
        _notificationsCompleted = new TaskCompletionSource(this, TaskCreationOptions.RunContinuationsAsynchronously);
        if (!empty) AddObserver(progress, completed);
        // Task has no detachable completion registration. A weak continuation
        // lets an abandoned reporter and its observers be collected independently.
        var weak = new WeakReference<ExecutorProgress>(this);
        if (operation.IsCompleted) OperationCompleted();
        else operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
        {
            if (weak.TryGetTarget(out var reporter)) reporter.OperationCompleted();
        });
    }

    /// <summary>Completes after terminal observer dispatch; executor rejection faults it and disposal or queued shutdown removal cancels it.</summary>
    /// <remarks>This is notification completion, not another copy of the operation's result or failure.</remarks>
    public Task NotificationsCompleted => _notificationsCompleted.Task;

    /// <summary>Registers progress and/or terminal callbacks while the operation is open.</summary>
    /// <remarks>
    /// Each handle is distinct, even for identical delegates. Reports snapshot registration membership
    /// at admission; later registrations receive no history. Disposal removes pending callbacks and
    /// cancels this handle's notification Task; a batch already claimed may still finish.
    /// A terminal batch already claimed completes its handle normally despite handle disposal.
    /// Notifications use the executor context, without capturing registration ExecutionContext.
    /// After operation completion, use ExecutorCompletion with the source Task for late completion observers.
    /// </remarks>
    public ProgressRegistration Register(Action<TransferProgress> progress, Action<Task> completed = null)
    {
        if (progress == null && completed == null) throw new ArgumentNullException(nameof(progress));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_closed || _operation == null || _operation.IsCompleted)
                throw new InvalidOperationException("The progress observation is complete; observe its source Task for late completion.");
            return AddObserver(progress, completed);
        }
    }

    private ProgressRegistration AddObserver(Action<TransferProgress> progress, Action<Task> completed)
    {
        var registration = new ProgressRegistration(this, progress, completed);
        registration.Node = _observers.AddLast(registration);
        return registration;
    }

    internal void Remove(ProgressRegistration registration)
    {
        lock (_gate)
        {
            if (registration.Owner != this) return;
            _observers.Remove(registration.Node);
            registration.Release();
            if (!registration.TerminalClaimed) registration.Cancel();
        }
    }

    public void Report(TransferProgress value)
    {
        if (!TryReport(value)) throw new InvalidOperationException("The progress observation is closed or its pending report capacity is exhausted.");
    }

    /// <summary>Admits a report while the operation and observation are open.</summary>
    /// <remarks>Successful admission does not guarantee executor availability; observe NotificationsCompleted for dispatch rejection.</remarks>
    public bool TryReport(TransferProgress value)
    {
        bool schedule;
        lock (_gate)
        {
            if (_closed || _operation.IsCompleted || _notifications.Count >= _maxPendingReports) return false;
            _notifications.Enqueue(new Notification(value, false, _observers.ToArray()));
            schedule = !_drainScheduled;
            _drainScheduled = true;
        }
        if (schedule) ScheduleDrain();
        return true;
    }

    /// <summary>Tries to report raw counts, including a negative total to denote unknown length.</summary>
    public bool TryReport(long completed, long total)
    {
        if (completed < 0 || (total >= 0 && completed > total)) return false;
        return TryReport(new TransferProgress(completed, total));
    }

    private void OperationCompleted()
    {
        bool schedule;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _notifications.Enqueue(new Notification(default, true, _observers.ToArray()));
            schedule = !_drainScheduled;
            _drainScheduled = true;
        }
        if (schedule) ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        IEventExecutor executor;
        lock (_gate)
        {
            if (_disposed) return;
            executor = _executor;
        }
        var reservation = new DrainReservation(this);
        try
        {
            if (executor.inEventLoop()) reservation.run();
            else executor.execute(reservation);
        }
        catch (Exception error) { reservation.Reject(error); }
    }

    private void DispatchFailed(Exception error)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = _closed = true;
            ReleaseObservers(error, false);
            _notifications.Clear();
            _drainScheduled = false;
            if (error != null) _notificationsCompleted.TrySetException(error);
            else _notificationsCompleted.TrySetCanceled();
        }
    }

    private void Drain()
    {
        for (;;)
        {
            Notification notification;
            ObserverSnapshot[] observers;
            Task operation;
            lock (_gate)
            {
                if (_disposed || !_notifications.TryDequeue(out notification))
                {
                    _drainScheduled = false;
                    return;
                }
                observers = notification.Observers.Where(observer => observer.Owner == this)
                    .Select(observer =>
                    {
                        if (notification.Terminal) observer.TerminalClaimed = true;
                        return new ObserverSnapshot(observer, observer.Progress, observer.Completed);
                    }).ToArray();
                operation = _operation;
            }
            if (notification.Terminal)
            {
                foreach (ObserverSnapshot observer in observers)
                {
                    foreach (Action<Task> callback in observer.Completed)
                    {
                        lock (_gate) { if (StopDisposedBatch(observers)) return; }
                        InvokeIsolated(() => callback(operation));
                    }
                    lock (_gate)
                    {
                        if (StopDisposedBatch(observers)) return;
                        if (observer.Registration.Owner == this)
                        {
                            _observers.Remove(observer.Registration.Node);
                            observer.Registration.Release();
                        }
                        observer.Registration.Complete();
                    }
                }
                lock (_gate)
                {
                    ReleaseObservers(null, true);
                    _drainScheduled = false;
                    _notificationsCompleted.TrySetResult();
                }
                return;
            }
            foreach (ObserverSnapshot observer in observers)
            {
                foreach (Action<TransferProgress> callback in observer.Progress)
                {
                    lock (_gate) { if (StopDisposedBatch(observers)) return; }
                    InvokeIsolated(() => callback(notification.Progress));
                }
            }
        }
    }

    // A handle can detach after terminal claim and therefore leave the registry.
    // If whole-observation disposal aborts that batch, settle the claimed handles
    // as well as those still in the registry. All calls hold _gate.
    private bool StopDisposedBatch(ObserverSnapshot[] observers)
    {
        if (!_disposed) return false;
        foreach (var observer in observers)
        {
            observer.Registration.Release();
            observer.Registration.Cancel();
        }
        return true;
    }

    private static void InvokeIsolated(Action callback)
    {
        try
        {
            ExecutionContext.Run(NativeScheduledWork<object>.CaptureExecutorContext(),
                static state => ((Action)state)(), callback);
        }
        catch (Exception error) { Logger.warn("An exception was thrown by a progress observer.", error); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = _closed = true;
            ReleaseObservers(null, false);
            _notifications.Clear();
            _notificationsCompleted.TrySetCanceled();
        }
    }

    private void ReleaseObservers(Exception error, bool completed)
    {
        foreach (var observer in _observers)
        {
            observer.Release();
            if (completed) observer.Complete();
            else if (error != null) observer.Reject(error);
            else observer.Cancel();
        }
        _observers.Clear();
        _operation = null;
        _executor = null;
    }

    private readonly record struct Notification(TransferProgress Progress, bool Terminal, ProgressRegistration[] Observers);
    private readonly record struct ObserverSnapshot(ProgressRegistration Registration,
        Action<TransferProgress>[] Progress, Action<Task>[] Completed);

    // The native queue can remove this pending drain without a Java Future wrapper.
    // Each reservation is claimed once; cancellation cannot stop an already-running drain.
    private sealed class DrainReservation(ExecutorProgress owner) : INativeSubmission
    {
        private readonly WeakReference<ExecutorProgress> _owner = new(owner);
        private int _claim;
        public bool IsCanceled => Volatile.Read(ref _claim) == 2;
        public void run()
        {
            if (Interlocked.CompareExchange(ref _claim, 1, 0) == 0 && _owner.TryGetTarget(out var target))
                target.Drain();
        }
        public void CancelForShutdown()
        {
            if (Interlocked.CompareExchange(ref _claim, 2, 0) == 0 && _owner.TryGetTarget(out var target))
                target.DispatchFailed(null);
        }
        public void Reject(Exception error)
        {
            if (Interlocked.CompareExchange(ref _claim, 2, 0) == 0 && _owner.TryGetTarget(out var target))
                target.DispatchFailed(error);
        }
    }
}

/// <summary>A detachable progress/terminal subscription whose Task tracks notification lifetime, not operation outcome.</summary>
public sealed class ProgressRegistration : IDisposable
{
    internal ExecutorProgress Owner;
    internal LinkedListNode<ProgressRegistration> Node;
    internal Action<TransferProgress>[] Progress;
    internal Action<Task>[] Completed;
    internal bool TerminalClaimed;
    private readonly TaskCompletionSource _notification;

    internal ProgressRegistration(ExecutorProgress owner, Action<TransferProgress> progress, Action<Task> completed)
    {
        Owner = owner;
        Progress = progress == null ? Array.Empty<Action<TransferProgress>>() :
            Array.ConvertAll(progress.GetInvocationList(), callback => (Action<TransferProgress>)callback);
        Completed = completed == null ? Array.Empty<Action<Task>>() :
            Array.ConvertAll(completed.GetInvocationList(), callback => (Action<Task>)callback);
        _notification = new TaskCompletionSource(this, TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Completes after this subscription's terminal callbacks; detach/shutdown cancel it and rejection faults it.</summary>
    public Task NotificationsCompleted => _notification.Task;
    public void Dispose() => Volatile.Read(ref Owner)?.Remove(this);
    internal void Release()
    {
        Owner = null;
        Node = null;
        Progress = Array.Empty<Action<TransferProgress>>();
        Completed = Array.Empty<Action<Task>>();
    }
    internal void Complete() => _notification.TrySetResult();
    internal void Cancel() => _notification.TrySetCanceled();
    internal void Reject(Exception error) => _notification.TrySetException(error);
}
