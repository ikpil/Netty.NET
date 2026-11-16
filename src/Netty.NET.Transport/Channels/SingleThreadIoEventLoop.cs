/*
 * Copyright 2024 The Netty Project
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
 * {@link IoEventLoop} implementation that execute all its submitted tasks in a single thread using the provided
 * {@link IoHandler}.
 */
public class SingleThreadIoEventLoop : SingleThreadEventLoop, IIoEventLoop
{
    // TODO: Is this a sensible default ?
    private static readonly long DEFAULT_MAX_TASK_PROCESSING_QUANTUM_NS = TimeUnitMillisToNanos(Math.Max(100,
            SystemPropertyUtil.GetInt("io.netty.eventLoop.maxTaskProcessingQuantumMs", 1000)));

    // 100 preserves the pre-existing behaviour of always using maxTaskProcessingQuantumNs as the task budget.
    internal static readonly int DEFAULT_IO_RATIO = Math.Max(1, Math.Min(100,
        SystemPropertyUtil.GetInt("io.netty.eventLoop.ioRatio", 100)));

    private readonly long _maxTaskProcessingQuantumNs;
    private volatile int _ioRatio = DEFAULT_IO_RATIO;
    private long _activeIoTimeNanos = -1;
    private readonly IIoHandlerContext _context;

    private readonly IIoHandler _ioHandler;

    private readonly AtomicInteger _numRegistrations = new AtomicInteger();

    /**
     *  Creates a new instance
     *
     * @param parent            the parent that holds this {@link IoEventLoop}.
     * @param threadFactory     the {@link ThreadFactory} that is used to create the underlying {@link Thread}.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that should be used to obtain {@link IoHandler} to
     *                          handle IO.
     */
    public SingleThreadIoEventLoop(IIoEventLoopGroup parent, IThreadFactory threadFactory,
                                   IIoHandlerFactory ioHandlerFactory)
        : base(parent, threadFactory, false,
            ObjectUtil.CheckNotNull(ioHandlerFactory, "ioHandlerFactory").IsChangingThreadSupported())
    {
        _maxTaskProcessingQuantumNs = DEFAULT_MAX_TASK_PROCESSING_QUANTUM_NS;
        _context = new Context(this);
        _ioHandler = ioHandlerFactory.NewHandler(this);
    }

    /**
     *  Creates a new instance
     *
     * @param parent            the parent that holds this {@link IoEventLoop}.
     * @param executor          the {@link Executor} that is used for dispatching the work.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that should be used to obtain {@link IoHandler} to
     *                          handle IO.
     */
    public SingleThreadIoEventLoop(IIoEventLoopGroup parent, Action<Action> executor, IIoHandlerFactory ioHandlerFactory)
        : base(parent, executor, false,
            ObjectUtil.CheckNotNull(ioHandlerFactory, "ioHandlerFactory").IsChangingThreadSupported())
    {
        _maxTaskProcessingQuantumNs = DEFAULT_MAX_TASK_PROCESSING_QUANTUM_NS;
        _context = new Context(this);
        _ioHandler = ioHandlerFactory.NewHandler(this);
    }

    /**
     *  Creates a new instance
     *
     * @param parent                        the parent that holds this {@link IoEventLoop}.
     * @param threadFactory                 the {@link ThreadFactory} that is used to create the underlying
     *                                      {@link Thread}.
     * @param ioHandlerFactory              the {@link IoHandlerFactory} that should be used to obtain {@link IoHandler}
     *                                      to handle IO.
     * @param maxPendingTasks               the maximum pending tasks that are allowed before
     *                                      {@link RejectedExecutionHandler#rejected(Runnable,
     *                                          SingleThreadEventExecutor)}
     *                                      is called to handle it.
     * @param rejectedExecutionHandler      the {@link RejectedExecutionHandler} that handles when more tasks are added
     *                                      then allowed per {@code maxPendingTasks}.
     * @param maxTaskProcessingQuantumMs    the maximum number of milliseconds that will be spent to run tasks before
     *                                      trying to run IO again.
     */
    public SingleThreadIoEventLoop(IIoEventLoopGroup parent, IThreadFactory threadFactory,
                                   IIoHandlerFactory ioHandlerFactory, int maxPendingTasks,
                                   Action<Action, SingleThreadEventExecutor> rejectedExecutionHandler,
                                   long maxTaskProcessingQuantumMs)
        : base(parent, threadFactory, false,
            ObjectUtil.CheckNotNull(ioHandlerFactory, "ioHandlerFactory").IsChangingThreadSupported(),
            maxPendingTasks, rejectedExecutionHandler)
    {
        _maxTaskProcessingQuantumNs =
                ObjectUtil.CheckPositiveOrZero(maxTaskProcessingQuantumMs, "maxTaskProcessingQuantumMs") == 0 ?
                        DEFAULT_MAX_TASK_PROCESSING_QUANTUM_NS :
                        TimeUnitMillisToNanos(maxTaskProcessingQuantumMs);
        _context = new Context(this);
        _ioHandler = ioHandlerFactory.NewHandler(this);
    }

    /**
     *  Creates a new instance
     *
     * @param parent                        the parent that holds this {@link IoEventLoop}.
     * @param ioHandlerFactory              the {@link IoHandlerFactory} that should be used to obtain {@link IoHandler}
     *                                      to handle IO.
     * @param maxPendingTasks               the maximum pending tasks that are allowed before
     *                                      {@link RejectedExecutionHandler#rejected(Runnable,
     *                                          SingleThreadEventExecutor)}
     *                                      is called to handle it.
     * @param rejectedExecutionHandler      the {@link RejectedExecutionHandler} that handles when more tasks are added
     *                                      then allowed per {@code maxPendingTasks}.
     * @param maxTaskProcessingQuantumMs    the maximum number of milliseconds that will be spent to run tasks before
     *                                      trying to run IO again.
     */
    public SingleThreadIoEventLoop(IIoEventLoopGroup parent, Action<Action> executor,
                                   IIoHandlerFactory ioHandlerFactory, int maxPendingTasks,
                                   Action<Action, SingleThreadEventExecutor> rejectedExecutionHandler,
                                   long maxTaskProcessingQuantumMs)
        : base(parent, executor, false,
            ObjectUtil.CheckNotNull(ioHandlerFactory, "ioHandlerFactory").IsChangingThreadSupported(),
            maxPendingTasks, rejectedExecutionHandler)
    {
        _maxTaskProcessingQuantumNs =
                ObjectUtil.CheckPositiveOrZero(maxTaskProcessingQuantumMs, "maxTaskProcessingQuantumMs") == 0 ?
                        DEFAULT_MAX_TASK_PROCESSING_QUANTUM_NS :
                        TimeUnitMillisToNanos(maxTaskProcessingQuantumMs);
        _context = new Context(this);
        _ioHandler = ioHandlerFactory.NewHandler(this);
    }

    /**
     *
     *  Creates a new instance
     *
     * @param parent                    the parent that holds this {@link IoEventLoop}.
     * @param executor                  the {@link Executor} that is used for dispatching the work.
     * @param ioHandlerFactory          the {@link IoHandlerFactory} that should be used to obtain {@link IoHandler}
     *                                  to handle IO.
     * @param taskQueue                 the {@link Queue} used for storing pending tasks.
     * @param tailTaskQueue             the {@link Queue} used for storing tail pending tasks.
     * @param rejectedExecutionHandler  the {@link RejectedExecutionHandler} that handles when more tasks are added
     *                                  then allowed.
     */
    protected SingleThreadIoEventLoop(IIoEventLoopGroup parent, Action<Action> executor,
                                      IIoHandlerFactory ioHandlerFactory, IQueue<Action> taskQueue,
                                      IQueue<Action> tailTaskQueue,
                                      Action<Action, SingleThreadEventExecutor> rejectedExecutionHandler)
        : base(parent, executor, false,
            ObjectUtil.CheckNotNull(ioHandlerFactory, "ioHandlerFactory").IsChangingThreadSupported(),
            taskQueue, tailTaskQueue, rejectedExecutionHandler)
    {
        _maxTaskProcessingQuantumNs = DEFAULT_MAX_TASK_PROCESSING_QUANTUM_NS;
        _context = new Context(this);
        _ioHandler = ioHandlerFactory.NewHandler(this);
    }

    // CLR: TimeUnit.MILLISECONDS.toNanos saturates at Long.MAX_VALUE instead of overflowing.
    private static long TimeUnitMillisToNanos(long millis) =>
        millis > long.MaxValue / 1_000_000L ? long.MaxValue : millis * 1_000_000L;

    // CLR: the anonymous IoHandlerContext of the Java constructor becomes a nested class.
    private sealed class Context(SingleThreadIoEventLoop loop) : IIoHandlerContext
    {
        public bool CanBlock()
        {
            Debug.Assert(loop.InEventLoop());
            return !loop.HasTasks() && !loop.HasScheduledTasks();
        }

        public long DelayNanos(long currentTimeNanos)
        {
            Debug.Assert(loop.InEventLoop());
            return loop.DelayNanos(currentTimeNanos);
        }

        public long DeadlineNanos()
        {
            Debug.Assert(loop.InEventLoop());
            return loop.DeadlineNanos();
        }

        public void ReportActiveIoTime(long activeNanos)
        {
            if (activeNanos >= 0)
            {
                loop._activeIoTimeNanos = activeNanos;
            }
            loop.ReportActiveIoTime(activeNanos);
        }

        public bool ShouldReportActiveIoTime()
        {
            return loop.IsSuspensionSupported() || loop._ioRatio != 100;
        }
    }

    protected override void Run()
    {
        Debug.Assert(InEventLoop());
        _ioHandler.Initialize();
        do
        {
            int ioRatio = _ioRatio;
            long taskQuantumNs;

            // reset before calling runIo() which is responsible for calling reportActiveIoTime(...).
            _activeIoTimeNanos = -1;
            RunIo();
            if (ioRatio == 100 || _activeIoTimeNanos == -1)
            {
                taskQuantumNs = _maxTaskProcessingQuantumNs;
            }
            else
            {
                // Give tasks a budget proportional to the time just spent on IO, still bounded by the
                // configured maximum so a burst of IO activity cannot starve the task queue indefinitely.
                long quantum = Math.Min(_maxTaskProcessingQuantumNs, unchecked(_activeIoTimeNanos * (100 - ioRatio)) / ioRatio);
                if (quantum == 0)
                {
                    // Ensure we always use a "deadline"
                    taskQuantumNs = _maxTaskProcessingQuantumNs;
                }
                else
                {
                    taskQuantumNs = quantum;
                }
            }
            if (IsShuttingDown())
            {
                _ioHandler.PrepareToDestroy();
            }
            // Now run tasks for the computed amount of time before trying to run IO again.
            RunAllTasks(taskQuantumNs);

            // We should continue with our loop until we either confirmed a shutdown or we can suspend it.
        } while (!ConfirmShutdown() && !CanSuspend());
    }

    // CLR: an immediate shutdown (StopAsync, Java's deprecated shutdown()) can arrive after the loop body checked
    // isShuttingDown(), so confirmShutdown() would finish without ioHandler.prepareToDestroy() and leave registered
    // handles open. IoHandler.prepareToDestroy() may be called multiple times, so it also precedes the confirmation.
    protected override bool ConfirmShutdown()
    {
        if (IsShuttingDown() && InEventLoop())
        {
            _ioHandler.PrepareToDestroy();
        }
        return base.ConfirmShutdown();
    }

    protected IIoHandler IoHandler()
    {
        return _ioHandler;
    }

    /**
     * Returns the current IO ratio. This value only affects scheduling behaviour when it's not the default
     * value of {@code 100}, in which case each iteration's task-processing budget will be capped to the
     * proportion of time just spent handling IO, in addition to the cap already imposed by
     * {@code io.netty.eventLoop.maxTaskProcessingQuantumMs}.
     */
    public int GetIoRatio()
    {
        return _ioRatio;
    }

    /**
     * Sets the percentage of the desired amount of time spent for I/O in the event loop relative to tasks.
     * The default value is {@code 100}, which means the event loop will not attempt to balance I/O and tasks,
     * relying purely on {@code io.netty.eventLoop.maxTaskProcessingQuantumMs} to bound the time spent running
     * tasks. Setting a value below {@code 100}, e.g. {@code 50}, restores the pre-4.2 behaviour of giving tasks
     * a per-iteration budget proportional to the time just spent on IO, which helps ensure that a busy task
     * queue does not delay IO processing (and so response handling) for extended stretches of time.
     */
    public void SetIoRatio(int ioRatio)
    {
        _ioRatio = ObjectUtil.CheckInRange(ioRatio, 1, 100, "ioRatio");
    }

    protected override bool CanSuspend(int state)
    {
        // We should only allow to suspend if there are no registrations on this loop atm.
        return base.CanSuspend(state) && _numRegistrations.Get() == 0;
    }

    /**
     * Called when IO will be processed for all the {@link IoHandle}s on this {@link SingleThreadIoEventLoop}.
     * This method returns the number of {@link IoHandle}s for which IO was processed.
     *
     * This method must be called from the {@link EventLoop} thread.
     */
    protected int RunIo()
    {
        Debug.Assert(InEventLoop());
        return _ioHandler.Run(_context);
    }

    public override IIoEventLoop Next()
    {
        return this;
    }

    IEventLoop IEventLoopGroup.Next() => this;

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

    protected override int GetNumOfRegisteredChannels()
    {
        return _numRegistrations.Get();
    }

    private void RegisterForIo0(IIoHandle handle, TaskCompletionSource<IIoRegistration> promise)
    {
        Debug.Assert(InEventLoop());
        IIoRegistration registration;
        try
        {
            registration = _ioHandler.Register(handle);
        }
        catch (Exception e)
        {
            promise.SetException(e);
            return;
        }
        _numRegistrations.IncrementAndGet();
        promise.SetResult(new IoRegistrationWrapper(this, registration));
    }

    protected sealed override void Wakeup(bool inEventLoop)
    {
        _ioHandler.Wakeup();
    }

    protected sealed override void Cleanup()
    {
        Debug.Assert(InEventLoop());
        _ioHandler.Destroy();
    }

    public bool IsCompatible(Type handleType)
    {
        return _ioHandler.IsCompatible(handleType);
    }

    public bool IsIoType(Type handlerType)
    {
        return _ioHandler.GetType() == handlerType;
    }

    protected override IQueue<Action> NewTaskQueue(int maxPendingTasks)
    {
        return NewTaskQueue0(maxPendingTasks);
    }

    // CLR: Common provides no non-blocking MPSC IQueue; the reference-equality LinkedBlockingQueue of
    // SingleThreadEventExecutor is used and, as in Java, this loop never blocks in takeTask().
    protected static IQueue<Action> NewTaskQueue0(int maxPendingTasks)
    {
        // This event loop never calls takeTask()
        return new LinkedBlockingQueue<Action>(maxPendingTasks, ReferenceEqualityComparer.Instance);
    }

    private sealed class IoRegistrationWrapper : IIoRegistration
    {
        private readonly SingleThreadIoEventLoop _loop;
        private readonly IIoRegistration _registration;
        internal IoRegistrationWrapper(SingleThreadIoEventLoop loop, IIoRegistration registration)
        {
            _loop = loop;
            _registration = registration;
        }

        public T Attachment<T>()
        {
            return _registration.Attachment<T>();
        }

        public long Submit(IIoOps ops)
        {
            return _registration.Submit(ops);
        }

        public bool IsValid()
        {
            return _registration.IsValid();
        }

        public bool Cancel()
        {
            if (_registration.Cancel())
            {
                _loop._numRegistrations.DecrementAndGet();
                return true;
            }
            return false;
        }
    }
}
