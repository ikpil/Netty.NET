/*
 * Copyright 2026 The Netty Project
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
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

// Run a continuation on its executor immediately when already on the event loop. Resolve the executor at queue
// time so notifications follow channel moves.
// Limit closure allocation to the branch that actually attaches a continuation. This scheduler leaves failures on
// the continuation task; ExecutorCompletion provides removable callbacks with bounded inline depth.
public sealed class EventExecutorTaskScheduler : TaskScheduler
{
    private readonly Func<IEventExecutor> _executor;

    /// <summary>Creates a scheduler with the listener timing of DefaultPromise.notifyListeners().</summary>
    /// <param name="executor">Returns the executor whose event loop runs the continuations; it is read each time a
    /// continuation is queued or offered inline.</param>
    public EventExecutorTaskScheduler(Func<IEventExecutor> executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        _executor = executor;
    }

    protected override void QueueTask(Task task)
    {
        IEventExecutor executor = _executor();
        if (executor.InEventLoop())
        {
            TryExecuteTask(task);
        }
        else
        {
            executor.Execute(() => TryExecuteTask(task));
        }
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return !taskWasPreviouslyQueued && _executor().InEventLoop() && TryExecuteTask(task);
    }

    protected override IEnumerable<Task> GetScheduledTasks()
    {
        return [];
    }
}
