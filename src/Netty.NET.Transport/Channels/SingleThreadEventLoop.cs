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
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * Abstract base class for {@link EventLoop}s that execute all its submitted tasks in a single thread.
 */
public abstract class SingleThreadEventLoop : SingleThreadEventExecutor, IEventLoop
{
    protected static readonly int DEFAULT_MAX_PENDING_TASKS = Math.Max(16, SystemPropertyUtil.GetInt("io.netty.eventLoop.maxPendingTasks", int.MaxValue));
    private readonly IQueue<Action> _tailTasks;
    protected SingleThreadEventLoop(IEventExecutorGroup parent, Action<Action> executor, bool addTaskWakesUp)
        : this(parent, executor, addTaskWakesUp, DEFAULT_MAX_PENDING_TASKS, RejectedExecutionHandlers.Reject()) { }
    protected SingleThreadEventLoop(IEventExecutorGroup parent, IThreadFactory factory, bool addTaskWakesUp)
        : this(parent, new ThreadPerTaskExecutor(factory).Execute, addTaskWakesUp) { }
    protected SingleThreadEventLoop(IEventExecutorGroup parent, Action<Action> executor, bool addTaskWakesUp,
        int maxPendingTasks, Action<Action, SingleThreadEventExecutor> rejectedHandler)
        : base(parent, executor, addTaskWakesUp, maxPendingTasks, rejectedHandler)
        => _tailTasks = NewTaskQueue(maxPendingTasks);
    public Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(channel); return channel.RegisterAsync(this, cancellationToken); }
    /**
     * Adds a task to be run once at the end of next (or current) {@code eventloop} iteration.
     *
     * @param task to be added.
     */
    public void ExecuteAfterEventLoopIteration(Action task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (IsShutdown())
            Reject();
        if (!_tailTasks.TryEnqueue(task))
            Reject(task);
        if (WakesUpForTask(task))
            Wakeup(InEventLoop());
    }
    /**
     * Removes a task that was added previously via {@link #executeAfterEventLoopIteration(Runnable)}.
     *
     * @param task to be removed.
     *
     * @return {@code true} if the task was removed as a result of this call.
     */
    public bool RemoveAfterEventLoopIterationTask(Action task)
    { ArgumentNullException.ThrowIfNull(task); return _tailTasks.TryRemove(task); }
    protected override void AfterRunningAllTasks() => RunAllTasksFrom(_tailTasks);
    protected override bool HasTasks() => base.HasTasks() || _tailTasks.Count != 0;
    public new int PendingTasks() => base.PendingTasks() + _tailTasks.Count;
    /**
     * Returns the number of {@link Channel}s registered with this {@link EventLoop} or {@code -1}
     * if operation is not supported. The returned value is not guaranteed to be exact accurate and
     * should be viewed as a best effort.
     */
    public virtual int RegisteredChannels() => -1;
    /**
     * @return read-only iterator of active {@link Channel}s registered with this {@link EventLoop}.
     *         The returned value is not guaranteed to be exact accurate and
     *         should be viewed as a best effort. This method is expected to be called from within
     *         event loop.
     * @throws UnsupportedOperationException if operation is not supported by implementation.
     */
    public virtual IEnumerable<IChannel> RegisteredChannelsIterator() => throw new NotSupportedException();
}
