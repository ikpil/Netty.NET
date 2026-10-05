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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

internal interface ITaskScheduledWork : IScheduledWork
{
    Action QueueCallback { get; }
    long PeriodNanos { get; }
    Task Completion { get; }
    void Reject(Exception error);
}

// CLR implementation of ScheduledFutureTask's deadline/invocation boundary.
// Queue membership is independent of the TaskCompletionSource result owner.
internal sealed class NativeScheduledWork<T> : ITaskScheduledWork, IPriorityQueueNode<IScheduledWork>
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;
    private readonly Func<long> _clock;
    private readonly Func<bool> _canRun;
    private readonly Action<ITaskScheduledWork> _enqueue;
    private readonly Action<ITaskScheduledWork> _remove;
    private Func<CancellationToken, T> _function;
    private ExecutionContext _context;
    // Invocation ownership: waiting, running, or finished. Task alone owns the result.
    private int _invocation;
    private int _published;
    // set once when added to priority queue
    private long _id;
    private long _deadline;
    private int _queueIndex = IPriorityQueueNode<IScheduledWork>.INDEX_NOT_IN_QUEUE;
    /* 0 - no repeat, >0 - repeat at fixed rate, <0 - repeat with fixed delay */
    public long PeriodNanos { get; }

    internal NativeScheduledWork(Func<CancellationToken, T> function, CancellationToken token, long deadline,
        long period, Func<long> clock, Func<bool> canRun, Action<ITaskScheduledWork> enqueue,
        Action<ITaskScheduledWork> remove, bool captureContext = true)
    {
        // One opaque callback owns every submission, due transfer and cancellation
        // dispatch. Periodic execution and a full ready queue do not recreate it.
        QueueCallback = Run;
        _function = function;
        _context = captureContext ? ExecutionContext.Capture() : null;
        _token = token;
        _deadline = deadline;
        PeriodNanos = period;
        _clock = clock;
        _canRun = canRun;
        _enqueue = enqueue;
        _remove = remove;
        _registration = token.UnsafeRegister(static state => ((NativeScheduledWork<T>)state).Cancel(), this);
        // The callback can run before UnsafeRegister returns. The constructor and
        // published paths alone access the immutable registration handle.
        if (Completion.IsCompleted) _registration.Unregister();
    }

    internal Task<T> ResultTask => _completion.Task;
    public Action QueueCallback { get; }
    public Task Completion => _completion.Task;
    public bool IsCanceled => Completion.IsCanceled;
    public long DeadlineNanos() => _deadline;
    public long DelayNanos() => DelayNanos(_clock());
    public long DelayNanos(long now) => AbstractScheduledEventExecutor.DeadlineToDelayNanos(now, _deadline);
    public long GetId() => _id;
    public void AssignId(long id) { if (_id == 0) _id = id; }
    public void SetConsumed()
    {
        // Optimization to avoid checking system clock again
        // after deadline has passed and task has been dequeued
        if (PeriodNanos == 0) _deadline = 0;
    }

    internal void Publish()
    {
        Volatile.Write(ref _published, 1);
        if (Completion.IsCompleted)
        {
            _registration.Unregister();
            if (IsCanceled) RemoveCanceled();
        }
    }

    private void Cancel()
    {
        lock (_gate)
        {
            if (_invocation == 2 || (_invocation == 1 && PeriodNanos == 0)) return;
            _invocation = 2;
            ReleaseInvocation();
            _completion.TrySetCanceled(_token);
        }
        if (Volatile.Read(ref _published) != 0)
        {
            _registration.Unregister();
            RemoveCanceled();
        }
    }

    private void RemoveCanceled()
    {
        // Native backends ignore removal after termination; callback cancellation
        // must not interrupt a shared executor thread or reject the caller's Cancel().
        _remove(this);
    }

    public void CancelForShutdown()
    {
        lock (_gate)
        {
            if (_invocation == 2 || (_invocation == 1 && PeriodNanos == 0)) return;
            _invocation = 2;
            ReleaseInvocation();
            _completion.TrySetCanceled();
        }
        _registration.Unregister();
    }

    public void Reject(Exception error)
    {
        lock (_gate)
        {
            if (_invocation == 2) return;
            _invocation = 2;
            ReleaseInvocation();
            _completion.TrySetException(error);
        }
        _registration.Unregister();
    }

    public void Run()
    {
        if (Completion.IsCompleted) { if (IsCanceled) RemoveCanceled(); return; }
        if (DelayNanos() > 0)
        {
            // Not yet expired, need to add or remove from queue
            _enqueue(this);
            return;
        }
        Invocation invocation;
        ExecutionContext context;
        lock (_gate)
        {
            // check if is done as it may was cancelled
            if (_invocation != 0) return;
            _invocation = 1;
            invocation = new Invocation(_function, _token);
            context = _context;
        }
        if (PeriodNanos == 0) _registration.Unregister();
        try
        {
            if (!_canRun()) { FinishCanceled(); return; }
            ExecutionContext.Run(context ?? CaptureExecutorContext(), static state => ((Invocation)state).Run(), invocation);
            lock (_gate)
            {
                if (_invocation == 2) return;
                if (PeriodNanos == 0)
                {
                    _invocation = 2;
                    ReleaseInvocation();
                    _completion.TrySetResult(invocation.Result);
                }
                else _invocation = 0;
            }
            if (PeriodNanos != 0 && !Completion.IsCompleted)
            {
                if (!_canRun()) { CancelForShutdown(); return; }
                _deadline = PeriodNanos > 0 ? unchecked(_deadline + PeriodNanos) : unchecked(_clock() - PeriodNanos);
                _enqueue(this);
            }
        }
        catch (OperationCanceledException error) when (_token.IsCancellationRequested && error.CancellationToken == _token)
        { FinishCanceled(_token); }
        catch (Exception error) { Reject(error); }
        finally { if (Completion.IsCompleted) _registration.Unregister(); }
    }

    private void FinishCanceled(CancellationToken token = default)
    {
        lock (_gate)
        {
            _invocation = 2;
            ReleaseInvocation();
            _completion.TrySetCanceled(token);
        }
    }

    private void ReleaseInvocation() { _function = null; _context = null; }

    private sealed class Invocation(Func<CancellationToken, T> function, CancellationToken token)
    {
        internal T Result;
        internal void Run() => Result = function(token);
    }

    internal static ExecutionContext CaptureExecutorContext()
    {
        bool suppressed = ExecutionContext.IsFlowSuppressed();
        if (!suppressed) return ExecutionContext.Capture();
        ExecutionContext.RestoreFlow();
        try { return ExecutionContext.Capture(); }
        finally { ExecutionContext.SuppressFlow(); }
    }

    public int PriorityQueueIndex(DefaultPriorityQueue<IScheduledWork> queue) => _queueIndex;
    public void PriorityQueueIndex(DefaultPriorityQueue<IScheduledWork> queue, int index) => _queueIndex = index;
}
