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

// CLR: a CLR-only helper with no Java counterpart (owner decision 2026-11-24). A Java listener is notified by its
// promise; a Task continuation runs on the TaskScheduler it is given, so the handlers that add a listener to an
// operation Task (handler IdleStateHandler, WriteTimeoutHandler, SslHandler and SslClientHelloHandler; codec-http
// HttpServerCodec, HttpServerKeepAliveHandler, HttpServerExpectContinueHandler, CorsHandler, SpdySessionHandler,
// WebSocketProtocolHandler and WebSocketServerExtensionHandler; Transport's PendingWriteQueue for the producers of its
// queued writes, 2026-11-25) share this one scheduler instead of a copy each. It
// lives in Common because every module that adds such a listener depends on Common; Common cannot see Transport's
// IChannelHandlerContext, so the executor comes from a Func<IEventExecutor>, e.g.
// new EventExecutorTaskScheduler(() => ctx.Executor). A lambda that captures a method parameter makes the C# compiler
// allocate its closure on entry to the method (measured: the IL of HttpServerCodec's encoder WriteAsync began with
// newobj <>c__DisplayClass), so a write path that attaches a listener only in one branch lets the lambda capture a
// copy of ctx declared in that branch, and the other writes allocate no closure.
//
// Java notifies a listener at once when its promise completes on the promise's event loop, and otherwise
// through executor.execute (DefaultPromise.notifyListeners(),
// https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/java/io/netty/util/concurrent/DefaultPromise.java).
// The Transport's operation Tasks are produced with RunContinuationsAsynchronously, which "Forces continuations added
// to the current task to be executed asynchronously" (https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcreationoptions),
// so a continuation on TaskScheduler.Default would run later on a thread-pool thread (IdleStateHandler would update
// lastWriteTime late). A continuation with its own scheduler is queued to that scheduler (QueueTask, "Queues a Task to
// the scheduler", https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskscheduler) by the completing
// thread; this scheduler runs it there when that thread is the executor's event loop, otherwise it hands it to the
// executor, the policy of notifyListeners(). A handler passes its context's executor, which stands for the promise's
// executor: the same event loop unless the handler runs on its own EventExecutorGroup. The executor is read when a
// continuation is queued, so a channel that moves to another event loop notifies on its current one. The
// MAX_LISTENER_STACK_DEPTH bound that notifyListeners() puts on nested inline notification is not applied here.
//
// ExecutorCompletion is not this policy: it registers removable Action<Task> callbacks on a fixed executor, runs them
// in ordered batches under a bounded inline depth (ExecutorNotificationScope) and logs their failures, while
// Task.ContinueWith(..., scheduler) and Unwrap() need a TaskScheduler whose continuations fault their own Tasks.
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
