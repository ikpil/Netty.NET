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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

internal sealed class NonStickyOrderedEventExecutor : AbstractEventExecutor, IOrderedEventExecutor
{
    private readonly IEventExecutor _executor;
    private readonly object _gate = new();
    private readonly Queue<IRunnable> tasks = new();
    private RunnerReservation _reservation;
    private RunnerReservation _executingReservation;
    private bool _stopped;
    private readonly int _maxTaskExecutePerRun;

    private Thread _executingThread;
    [ThreadStatic] private static NonStickyOrderedEventExecutor _inlineDispatchOwner;

    public NonStickyOrderedEventExecutor(IEventExecutor executor, int maxTaskExecutePerRun)
        : base(executor)
    {
        _executor = executor;
        _maxTaskExecutePerRun = maxTaskExecutePerRun;
    }

    private void Run(RunnerReservation reservation)
    {
        Thread current = Thread.CurrentThread;
        try
        {
            for (;;)
            {
                lock (_gate)
                {
                    if (_stopped || !ReferenceEquals(_reservation, reservation)) return;
                    _executingReservation = reservation;
                    Volatile.Write(ref _executingThread, current);
                }
                int i = 0;
                for (; i < _maxTaskExecutePerRun; ++i)
                {
                    if (IsImmediateShutdownRequested(reservation))
                    {
                        FinishPending(reservation, null, true);
                        return;
                    }
                    IRunnable task;
                    lock (_gate)
                    {
                        if (_stopped || !ReferenceEquals(_reservation, reservation)) return;
                        if (!tasks.TryDequeue(out task)) break;
                    }
                    SafeExecute(task);
                }

                RunnerReservation next;
                lock (_gate)
                {
                    if (_stopped || !ReferenceEquals(_reservation, reservation)) return;
                    // CLR adaptation: queue emptiness and runner ownership change
                    // under one gate. The original CAS scenarios below explain the
                    // producer-versus-empty-drain race this atomic change resolves.
                    // After setting the state to NONE, look at the tasks queue one more time.
                    // If it is empty, then we can return from this method.
                    // Otherwise, it means the producer thread has called execute(Runnable)
                    // and enqueued a task in between the tasks.poll() above and the state.set(NONE) here.
                    // There are two possible scenarios when this happens
                    //
                    // 1. The producer thread sees state == NONE, hence the compareAndSet(NONE, SUBMITTED)
                    //    is successfully setting the state to SUBMITTED. This mean the producer
                    //    will call / has called executor.execute(this). In this case, we can just return.
                    // 2. The producer thread don't see the state change, hence the compareAndSet(NONE, SUBMITTED)
                    //    returns false. In this case, the producer thread won't call executor.execute.
                    //    In this case, we need to change the state to RUNNING and keeps running.
                    //
                    // The above cases can be distinguished by performing a
                    // compareAndSet(NONE, RUNNING). If it returns "false", it is case 1; otherwise it is case 2.
                    if (tasks.Count == 0 && i < _maxTaskExecutePerRun)
                    {
                        _reservation = null;
                        // Only set executingThread to null if no other thread did update it yet.
                        ClearExecuting(reservation);
                        return; // done
                    }
                    if (i < _maxTaskExecutePerRun) continue;
                    next = new RunnerReservation(this);
                    _reservation = next;
                    // Only set executingThread to null if no other thread did update it yet.
                    ClearExecuting(reservation);
                }
                try
                {
                    if (Dispatch(next))
                    {
                        // An inline executor must not recursively grow the stack at
                        // each batch boundary. Its reserved runner is claimed here.
                        reservation = next;
                        continue;
                    }
                    return; // done
                }
                catch (Exception ignore)
                {
                    if (IsImmediateShutdownRequested(next))
                    {
                        next.CancelForShutdown();
                        return;
                    }
                    if (!next.Invalidate()) return;
                    lock (_gate)
                    {
                        if (_stopped || !ReferenceEquals(_reservation, next)) return;
                        // Restore executingThread since we're continuing to execute tasks.
                        Volatile.Write(ref _executingThread, current);
                        _executingReservation = reservation;
                        // Reset the state back to running as we will keep on executing tasks.
                        _reservation = reservation;
                    }
                    // if an error happened we should just ignore it and let the loop run again as there is not
                    // much else we can do. Most likely this was triggered by a full task queue. In this case
                    // we just will run more tasks and try again later.
                }
            }
        }
        catch (Exception error)
        {
            FinishPending(reservation, error, false);
            throw;
        }
        finally { lock (_gate) ClearExecuting(reservation); }
    }

    public override bool InEventLoop(Thread thread)
    {
        return thread != null && ReferenceEquals(Volatile.Read(ref _executingThread), thread);
    }

    public override bool IsShuttingDown()
    {
        return _executor.IsShutdown();
    }

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return _executor.ShutdownGracefullyAsync(quietPeriod, timeout);
    }

    public override Task Termination => _executor.Termination;
    public override Task StopAsync() => _executor.StopAsync();

    public override void Shutdown()
    {
        _executor.Shutdown();
    }

    public override bool IsShutdown()
    {
        return _executor.IsShutdown();
    }

    public override bool IsTerminated()
    {
        return _executor.IsTerminated();
    }

    public override bool AwaitTermination(TimeSpan timeout)
    {
        return _executor.AwaitTermination(timeout);
    }

    public override void Execute(Action command)
    {
        IRunnable queuedTask = ExecutorWork.Unwrap(command, nameof(command));
        ArgumentNullException.ThrowIfNull(queuedTask);
        RunnerReservation reservation;
        lock (_gate)
        {
            if (_stopped)
                throw new RejectedExecutionException("Ordered executor has been stopped.");
            tasks.Enqueue(queuedTask);
            if (_reservation != null)
                return;
            reservation = new RunnerReservation(this);
            _reservation = reservation;
        }

        try
        {
            // Actually it could happen that the runnable was picked up in between but we not care to much and just
            // execute ourself. At worst this will be a NOOP when run() is called.
            // CLR adaptation: a separate reservation identifies this admission
            // attempt, so a stale rejection/removal cannot claim a later runner.
            if (Dispatch(reservation))
                Run(reservation);
        }
        catch (Exception error)
        {
            reservation.Reject(error);
            throw;
        }
    }

    private static bool IsImmediateShutdownRequested(RunnerReservation reservation) =>
        reservation.QueueOwner?.IsImmediateShutdownRequested ?? false;

    private bool Dispatch(RunnerReservation reservation)
    {
        NonStickyOrderedEventExecutor previous = _inlineDispatchOwner;
        _inlineDispatchOwner = this;
        try
        {
            // A native reservation owns only admission, never a result Task.
            _executor.Execute(reservation);
        }
        finally { _inlineDispatchOwner = previous; }
        return reservation.Inline;
    }

    private void ClearExecuting(RunnerReservation reservation)
    {
        if (!ReferenceEquals(_executingReservation, reservation)) return;
        _executingReservation = null;
        Volatile.Write(ref _executingThread, null);
    }

    private void FinishPending(RunnerReservation reservation, Exception error, bool stopped)
    {
        IRunnable[] pending;
        lock (_gate)
        {
            if (!ReferenceEquals(_reservation, reservation)) return;
            _stopped |= stopped;
            pending = tasks.ToArray();
            tasks.Clear();
            _reservation = null;
        }
        // Native outcomes settle outside the child gate. No caller callback is
        // invoked by these cancellation/rejection hooks.
        foreach (IRunnable command in pending)
        {
            if (command is INativeSubmission native)
            {
                if (error == null) native.CancelForShutdown();
                else native.Reject(error);
            }
        }
    }

    private sealed class RunnerReservation(NonStickyOrderedEventExecutor owner) : IQueueBoundNativeSubmission
    {
        // One claim selects runner start or pre-start invalidation/removal. It is
        // membership metadata; user operations retain their own TCS results.
        private int _claim;
        internal bool Inline { get; private set; }
        internal UnorderedThreadPoolEventExecutor QueueOwner { get; private set; }
        public void BindQueueOwner(UnorderedThreadPoolEventExecutor queueOwner) => QueueOwner = queueOwner;
        public bool IsCanceled => Volatile.Read(ref _claim) == 2;
        internal bool Invalidate() => Interlocked.CompareExchange(ref _claim, 2, 0) == 0;
        public void CancelForShutdown()
        {
            if (Invalidate()) owner.FinishPending(this, null, true);
        }
        public void Reject(Exception error)
        {
            if (Invalidate()) owner.FinishPending(this, error, false);
        }
        public void Run()
        {
            if (Interlocked.CompareExchange(ref _claim, 1, 0) != 0) return;
            if (ReferenceEquals(_inlineDispatchOwner, owner)) Inline = true;
            else owner.Run(this);
        }
    }
}
