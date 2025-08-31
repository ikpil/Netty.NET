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
namespace Netty.NET.Common.Concurrent;


/**
 * Default {@link SingleThreadEventExecutor} implementation which just execute all submitted task in a
 * serial fashion.
 */
public sealed class DefaultEventExecutor : SingleThreadEventExecutor 
{

    public DefaultEventExecutor() {
        this((IEventExecutorGroup) null);
    }

    public DefaultEventExecutor(IThreadFactory threadFactory) {
        this(null, threadFactory);
    }

    public DefaultEventExecutor(IExecutor executor) {
        this(null, executor);
    }

    public DefaultEventExecutor(IEventExecutorGroup parent) {
        this(parent, new DefaultThreadFactory(typeof(DefaultEventExecutor)));
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, IThreadFactory threadFactory) {
        super(parent, threadFactory, true);
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, IExecutor executor) {
        super(parent, executor, true);
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, IThreadFactory threadFactory, int maxPendingTasks,
                                IRejectedExecutionHandler rejectedExecutionHandler) {
        super(parent, threadFactory, true, maxPendingTasks, rejectedExecutionHandler);
    }

    public DefaultEventExecutor(IEventExecutorGroup parent, IExecutor executor, int maxPendingTasks,
                                IRejectedExecutionHandler rejectedExecutionHandler) {
        super(parent, executor, true, maxPendingTasks, rejectedExecutionHandler);
    }

    @Override
    protected void run() {
        for (;;) {
            IRunnable task = takeTask();
            if (task != null) {
                runTask(task);
                updateLastExecutionTime();
            }

            if (confirmShutdown()) {
                break;
            }
        }
    }
}
