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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/// <summary>Orders detachable synchronous completion callbacks for an existing Task on an executor.</summary>
/// <remarks>
/// The supplied Task owns the operation result. This object owns only callback dispatch.
/// Registrations on this object run in admission order, without overlap, even on an unordered executor.
/// Callbacks registered from a callback follow the current claimed batch. Disposing a registration
/// removes it while pending; a batch already claimed for invocation may still finish.
/// Registration and producer ExecutionContext are not captured. Each callback uses a scoped executor context.
/// Task completion and callback completion are distinct; awaiting the operation does not await its callbacks.
/// Keep this object, a registration, or its NotificationCompleted Task alive while notifications are required.
/// Dispose this object to release the source Task and executor and close further registration.
/// </remarks>
public sealed class ExecutorCompletion : IDisposable
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(ExecutorCompletion));
    private readonly object _gate = new();
    private readonly LinkedList<CompletionRegistration> _pending = new();
    private IEventExecutor _executor;
    private Task _operation;
    private DrainReservation _drain;
    private bool _closed;

    public ExecutorCompletion(IEventExecutor executor, Task operation)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(operation);
        _executor = executor;
        _operation = operation;
        // Task has no detachable continuation. Retain only a weak dispatcher reference
        // in its continuation, so an abandoned observation does not retain its owners.
        var weak = new WeakReference<ExecutorCompletion>(this);
        if (!operation.IsCompleted)
            operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
            {
                if (weak.TryGetTarget(out var observation)) observation.OperationCompleted();
            });
    }

    /**
     * Listens to the result of a {@link Future}.  The result of the asynchronous operation is notified once this listener
     * is added by calling {@link Future#addListener(GenericFutureListener)}.
     */
    // CLR: an Action<Task> and its unique disposable registration replace the listener hierarchy.
    /// <summary>Registers a synchronous callback and returns its removal and notification-completion handle.</summary>
    /// <remarks>
    /// An already-completed Task can invoke the callback before this method returns if called on the executor.
    /// A multicast delegate is one registration; its invocation entries are isolated from each other's failures.
    /// Removing one handle does not remove another registration of the same delegate.
    /// Dispatcher rejection faults pending notification Tasks; queue removal or disposal cancels them.
    /// Callback exceptions are logged and isolated, as in Netty; they do not fault the notification Task.
    /// Async-void callbacks are unsupported: asynchronous consumers should compose or await the source Task;
    /// this registration cannot observe their post-await work or guarantee its executor affinity.
    /// </remarks>
    public CompletionRegistration Register(Action<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        CompletionRegistration registration;
        DrainReservation schedule;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            registration = new CompletionRegistration(this, callback);
            registration.Node = _pending.AddLast(registration);
            schedule = PrepareDrain();
        }
        if (schedule != null) ScheduleDrain(schedule);
        return registration;
    }

    private DrainReservation PrepareDrain()
    {
        if (_closed || _drain != null || _pending.Count == 0 || !_operation.IsCompleted) return null;
        return _drain = new DrainReservation(this);
    }

    private void OperationCompleted()
    {
        DrainReservation schedule;
        lock (_gate) schedule = PrepareDrain();
        if (schedule != null) ScheduleDrain(schedule);
    }

    private void ScheduleDrain(DrainReservation reservation)
    {
        IEventExecutor executor;
        lock (_gate)
        {
            if (_closed || _drain != reservation) return;
            executor = _executor;
        }
        try
        {
            // The shared CLR thread counter also bounds chains between different observations.
            // Like DefaultPromise, an executor must bound reentrant execute() at the dispatch boundary.
            if (executor.InEventLoop() && ExecutorNotificationScope.CanInline) reservation.Run();
            else executor.Execute(reservation);
        }
        catch (Exception error) { reservation.Reject(error); }
    }

    private void Drain(DrainReservation reservation)
    {
        using var scope = new ExecutorNotificationScope();
        for (;;)
        {
            CompletionRegistration[] batch;
            Task operation;
            lock (_gate)
            {
                // Only proceed if there are listeners to notify and we are not already notifying listeners.
                // CLR: the unique reservation and gate serialize snapshots on every executor.
                if (_closed || _drain != reservation || _pending.Count == 0)
                {
                    if (_drain == reservation) _drain = null;
                    return;
                }
                batch = new CompletionRegistration[_pending.Count];
                _pending.CopyTo(batch, 0);
                _pending.Clear();
                foreach (var registration in batch) registration.Node = null;
                operation = _operation;
            }
            foreach (var registration in batch)
            {
                /**
                 * Invoked when the operation associated with the {@link Future} has been completed.
                 *
                 * @param future  the source {@link Future} which called this callback
                 */
                // CLR: the original source Task is passed through, including its original result/token/error.
                foreach (Action<Task> callback in registration.Callback.GetInvocationList())
                {
                    try
                    {
                        ExecutionContext.Run(NativeScheduledWork<object>.CaptureExecutorContext(),
                            _ => callback(operation), null);
                    }
                    catch (Exception error) { Logger.Warn("An exception was thrown by a completion observer.", error); }
                }
                registration.Finish(null, false);
            }
            // Nothing can throw from within this method, so setting notifyingListeners back to false does not
            // need to be in a finally block.
            // CLR: observer failures are isolated above; the next gate acquisition claims reentrant additions.
        }
    }

    internal void Remove(CompletionRegistration registration)
    {
        lock (_gate)
        {
            if (registration.Node == null) return; // Claimed snapshots retain their invocation right.
            _pending.Remove(registration.Node);
            registration.Node = null;
            registration.Finish(null, true);
        }
    }

    private void DispatchFailed(DrainReservation reservation, Exception error)
    {
        lock (_gate)
        {
            if (_drain != reservation) return;
            Close(error);
        }
    }

    public void Dispose()
    {
        lock (_gate) Close(null);
    }

    private void Close(Exception error)
    {
        if (_closed) return;
        _closed = true;
        _drain = null;
        foreach (var registration in _pending)
        {
            registration.Node = null;
            registration.Finish(error, error == null);
        }
        _pending.Clear();
        _executor = null;
        _operation = null;
    }

    // A queue reservation arbitrates dispatch, rejection and shutdown removal. It owns no operation result.
    private sealed class DrainReservation(ExecutorCompletion owner) : INativeSubmission
    {
        private readonly WeakReference<ExecutorCompletion> _owner = new(owner);
        private int _claim;
        public bool IsCanceled => Volatile.Read(ref _claim) == 2;
        public void Run()
        {
            if (Interlocked.CompareExchange(ref _claim, 1, 0) == 0 && _owner.TryGetTarget(out var target))
                target.Drain(this);
        }
        public void CancelForShutdown()
        {
            if (Interlocked.CompareExchange(ref _claim, 2, 0) == 0 && _owner.TryGetTarget(out var target))
                target.DispatchFailed(this, null);
        }
        public void Reject(Exception error)
        {
            if (Interlocked.CompareExchange(ref _claim, 2, 0) == 0 && _owner.TryGetTarget(out var target))
                target.DispatchFailed(this, error);
        }
    }
}

/// <summary>A unique removable callback registration, independent of the source operation's lifetime.</summary>
public sealed class CompletionRegistration : IDisposable
{
    private ExecutorCompletion _owner;
    private readonly TaskCompletionSource _notification;
    internal Action<Task> Callback;
    internal LinkedListNode<CompletionRegistration> Node;

    internal CompletionRegistration(ExecutorCompletion owner, Action<Task> callback)
    {
        _owner = owner;
        Callback = callback;
        // Retaining only the notification Task keeps this pending registration and dispatcher alive.
        _notification = new TaskCompletionSource(this, TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Completes after invocation; removal cancels it and executor rejection faults it.</summary>
    /// <remarks>This Task describes this notification, never a second copy of the source operation's result.</remarks>
    public Task NotificationCompleted => _notification.Task;

    /// <summary>Removes this registration if it has not been claimed for notification.</summary>
    /// <remarks>Nonblocking and idempotent; it does not cancel the source Task or wait for a claimed callback.</remarks>
    public void Dispose() => Volatile.Read(ref _owner)?.Remove(this);

    internal void Finish(Exception error, bool canceled)
    {
        Callback = null;
        Volatile.Write(ref _owner, null);
        if (error != null) _notification.TrySetException(error);
        else if (canceled) _notification.TrySetCanceled();
        else _notification.TrySetResult();
    }
}
