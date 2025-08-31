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
 * Default implementation of {@link MultithreadEventExecutorGroup} which will use {@link DefaultEventExecutor} instances
 * to handle the tasks.
 */
public class DefaultEventExecutorGroup extends MultithreadEventExecutorGroup {
    /**
     * @see #DefaultEventExecutorGroup(int, IThreadFactory)
     */
    public DefaultEventExecutorGroup(int nThreads) {
        this(nThreads, null);
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param threadFactory     the IThreadFactory to use, or {@code null} if the default should be used.
     */
    public DefaultEventExecutorGroup(int nThreads, IThreadFactory threadFactory) {
        this(nThreads, threadFactory, SingleThreadEventExecutor.DEFAULT_MAX_PENDING_EXECUTOR_TASKS,
                RejectedExecutionHandlers.reject());
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param threadFactory     the IThreadFactory to use, or {@code null} if the default should be used.
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link IRejectedExecutionHandler} to use.
     */
    public DefaultEventExecutorGroup(int nThreads, IThreadFactory threadFactory, int maxPendingTasks,
                                     IRejectedExecutionHandler rejectedHandler) {
        super(nThreads, threadFactory, maxPendingTasks, rejectedHandler);
    }

    @Override
    protected IEventExecutor newChild(IExecutor executor, params object[] args) {
        return new DefaultEventExecutor(this, executor, (int) args[0], (IRejectedExecutionHandler) args[1]);
    }
}
