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

namespace Netty.NET.Common.Concurrent;

/**
 * Default {@link SingleThreadEventExecutor} implementation which just execute all submitted task in a
 * serial fashion.
 */
public sealed class DefaultEventExecutor : SingleThreadEventExecutor
{
    /// <summary>
    /// Uses a native timestamp provider while retaining the dedicated executor
    /// thread. After manually advancing the provider, submit work to wake the
    /// executor. Provider timers do not dispatch scheduled callbacks.
    /// </summary>
    public DefaultEventExecutor(TimeProvider timeProvider)
        : base(null, new ThreadPerTaskExecutor(new DefaultThreadFactory(typeof(DefaultEventExecutor))).Execute,
            true, false, DEFAULT_MAX_PENDING_EXECUTOR_TASKS, RejectedExecutionHandlers.Reject(), timeProvider)
    {
    }

    public DefaultEventExecutor()
        : this((IEventExecutorGroup)null)
    {
    }

    public DefaultEventExecutor(IThreadFactory threadFactory)
        : this(null, threadFactory)
    {
    }

    /// <summary>Dispatches the long-lived worker entry using the supplied backend.</summary>
    public DefaultEventExecutor(Action<Action> executor)
        : this(null, executor)
    {
    }

    public DefaultEventExecutor(IEventExecutorGroup parent)
        : this(parent, new DefaultThreadFactory(typeof(DefaultEventExecutor)))
    {
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, IThreadFactory threadFactory)
        : base(parent, threadFactory, true)
    {
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, Action<Action> executor)
        : base(parent, executor, true)
    {
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, IThreadFactory threadFactory, int maxPendingTasks, Action<Action, SingleThreadEventExecutor> rejectedExecutionHandler)
        : base(parent, threadFactory, true, maxPendingTasks, rejectedExecutionHandler)
    {
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, Action<Action> executor, int maxPendingTasks, Action<Action, SingleThreadEventExecutor> rejectedExecutionHandler)
        : base(parent, executor, true, maxPendingTasks, rejectedExecutionHandler)
    {
    }

    protected override void Run()
    {
        for (;;)
        {
            Action task = TakeTask();
            if (task != null)
            {
                RunTask(task);
                UpdateLastExecutionTime();
            }

            if (ConfirmShutdown())
            {
                break;
            }
        }
    }
}
