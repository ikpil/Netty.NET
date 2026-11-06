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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
namespace Netty.NET.Transport.Channels;

public class DefaultEventLoop : SingleThreadEventLoop, IIoEventLoop
{
    // TODO: Is this a sensible default ?
    private static readonly long MaxTaskProcessingQuantumNanos = 1_000_000L * Math.Max(100,
        SystemPropertyUtil.GetInt("io.netty.eventLoop.maxTaskProcessingQuantumMs", 1000));
    // 100 preserves the pre-existing behaviour of always using maxTaskProcessingQuantumNs as the task budget.
    internal static readonly int DefaultIoRatio = Math.Clamp(SystemPropertyUtil.GetInt("io.netty.eventLoop.ioRatio", 100), 1, 100);

    private readonly ConcurrentDictionary<IChannel, byte> _channels = new(ReferenceEqualityComparer.Instance);
    private readonly object _ioGate = new();
    private readonly Queue<IoCompletion> _io = new();
    private readonly CompletionIoHandler _ioHandler;
    private int _ioRatio = DefaultIoRatio;
    public DefaultEventLoop() : this(null, new ThreadPerTaskExecutor(new DefaultThreadFactory(typeof(DefaultEventLoop))).Execute) { }
    public DefaultEventLoop(CompletionIoHandler ioHandler)
        : this(null, new ThreadPerTaskExecutor(new DefaultThreadFactory(typeof(DefaultEventLoop))).Execute, false,
            ioHandler ?? throw new ArgumentNullException(nameof(ioHandler))) { }
    public DefaultEventLoop(IEventExecutorGroup parent, Action<Action> executor) : this(parent, executor, false) { }
    internal DefaultEventLoop(IEventExecutorGroup parent, Action<Action> executor, bool supportSuspension, CompletionIoHandler ioHandler = null)
        : base(parent, executor, true, supportSuspension, DEFAULT_MAX_PENDING_TASKS, RejectedExecutionHandlers.Reject())
    {
        _ioHandler = ioHandler ?? new CompletionIoHandler();
        _ioHandler.Attach(this, (_, dispatch) => SubmitIoAsync(dispatch));
    }
    internal void ChannelRegistered(IChannel channel) => _channels.TryAdd(channel, 0);
    internal void ChannelUnregistered(IChannel channel) => _channels.TryRemove(channel, out _);

    public Task<IoRegistration> RegisterIoAsync(IIoHandle handle, CancellationToken cancellationToken = default) =>
        _ioHandler.RegisterAsync(handle, cancellationToken);
    IEventLoop IEventLoopGroup.Next() => this;
    public bool IsCompatible(Type handleType) => _ioHandler.IsCompatible(handleType);
    public bool IsIoType(Type handlerType) => _ioHandler.GetType() == handlerType;
    public int RegisteredIoHandles => _ioHandler.Count;

    /**
     * Returns the current IO ratio. This value only affects scheduling behaviour when it's not the default
     * value of {@code 100}, in which case each iteration's task-processing budget will be capped to the
     * proportion of time just spent handling IO, in addition to the cap already imposed by
     * {@code io.netty.eventLoop.maxTaskProcessingQuantumMs}.
     */
    /**
     * Sets the percentage of the desired amount of time spent for I/O in the event loop relative to tasks.
     * The default value is {@code 100}, which means the event loop will not attempt to balance I/O and tasks,
     * relying purely on {@code io.netty.eventLoop.maxTaskProcessingQuantumMs} to bound the time spent running
     * tasks. Setting a value below {@code 100}, e.g. {@code 50}, restores the pre-4.2 behaviour of giving tasks
     * a per-iteration budget proportional to the time just spent on IO, which helps ensure that a busy task
     * queue does not delay IO processing (and so response handling) for extended stretches of time.
     */
    /// <summary>The desired percentage of loop time spent on I/O when processing ready completions.</summary>
    public int IoRatio
    {
        get => Volatile.Read(ref _ioRatio);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            Volatile.Write(ref _ioRatio, value);
        }
    }

    internal Task SubmitIoAsync(Func<(Task Completion, bool Handled)> dispatch, bool captureContext = true)
    {
        var completion = new IoCompletion(dispatch, captureContext);
        lock (_ioGate)
        {
            if (IsShutdown()) throw new RejectedExecutionException("The event loop is shut down.");
            bool signal = _io.Count == 0 && !InEventLoop();
            if (signal) StartThread();
            _io.Enqueue(completion);
            if (signal) Wakeup(false);
        }
        return completion.Completion;
    }

    private int PendingIoCount()
    {
        lock (_ioGate) return _io.Count;
    }

    protected override bool HasTasks() => base.HasTasks() || PendingIoCount() != 0;
    public override int PendingTasks() => base.PendingTasks() + PendingIoCount();
    public override int RegisteredChannels() => _channels.Count;
    protected override int GetNumOfRegisteredChannels() => _channels.Count + _ioHandler.Count;
    // A raw registration delays suspension until the owner unregisters it.
    public override bool TrySuspend() => _channels.IsEmpty && base.TrySuspend();
    protected override bool CanSuspend(int state)
    {
        // We should only allow to suspend if there are no registrations on this loop atm.
        return base.CanSuspend(state) && GetNumOfRegisteredChannels() == 0;
    }
    public override IEnumerable<IChannel> RegisteredChannelsIterator()
    {
        if (!InEventLoop()) throw new InvalidOperationException("Registered channels must be inspected on their event loop.");
        return Array.AsReadOnly(_channels.Keys.ToArray());
    }
    protected override void Run()
    {
        bool measureWork = IsSuspensionSupported();
        do
        {
            var task = PendingIoCount() == 0 ? TakeTask() : null;
            if (RunIoBatch() is long activeIoNanos)
            {
                long budget = TaskBudgetNanos(activeIoNanos, IoRatio, MaxTaskProcessingQuantumNanos);
                if (task != null)
                {
                    long start = Ticker().NanoTime();
                    SafeExecute(task);
                    long elapsed = unchecked(Ticker().NanoTime() - start);
                    ReportActiveIoTime(elapsed);
                    UpdateLastExecutionTime();
                    budget = Math.Max(0, budget - elapsed);
                }
                // Now run tasks for the computed amount of time before trying to run IO again.
                RunAllTasks(budget);
            }
            else if (measureWork && (task != null || HasTailTasks))
            {
                // Socket readiness completes through CLR tasks. Count the time the
                // owner spends dispatching them, excluding time blocked in TakeTask.
                long start = Ticker().NanoTime();
                if (task != null) SafeExecute(task);
                AfterRunningAllTasks();
                ReportActiveIoTime(unchecked(Ticker().NanoTime() - start));
                UpdateLastExecutionTime();
            }
            else if (task != null)
            {
                RunTask(task);
                UpdateLastExecutionTime();
            }
            // We should continue with our loop until we either confirmed a shutdown or we can suspend it.
        } while (!ConfirmShutdown() && !CanSuspend());
    }

    internal static long TaskBudgetNanos(long activeIoNanos, int ratio, long maximum)
    {
        if (ratio == 100 || activeIoNanos <= 0) return maximum;

        // Give tasks a budget proportional to the time just spent on IO, still bounded by the
        // configured maximum so a burst of IO activity cannot starve the task queue indefinitely.
        long budget = (long)Int128.Min(maximum, (Int128)activeIoNanos * (100 - ratio) / ratio);
        // Ensure we always use a "deadline"
        return budget == 0 ? maximum : budget;
    }

    private long? RunIoBatch()
    {
        int remaining = PendingIoCount();
        if (remaining == 0) return null;
        long active = 0;
        long started = Ticker().NanoTime();
        // Work published by callbacks is for the next I/O phase.
        while (remaining-- > 0)
        {
            IoCompletion completion;
            lock (_ioGate) completion = _io.Dequeue();
            long before = Ticker().NanoTime();
            if (completion.Dispatch())
                active += unchecked(Ticker().NanoTime() - before);
        }
        ReportActiveIoTime(unchecked(Ticker().NanoTime() - started));
        UpdateLastExecutionTime();
        return active;
    }

    protected override bool ConfirmShutdown()
    {
        if (IsShuttingDown()) RunIoBatch();
        return base.ConfirmShutdown();
    }

    protected override void Cleanup()
    {
        // Drain completions admitted before the common executor closed admission.
        RunIoBatch();
        _ioHandler.CloseAll();
        base.Cleanup();
    }
}
