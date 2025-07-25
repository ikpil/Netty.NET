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

namespace Netty.NET.Common;

/**
 * Schedules {@link ITimerTask}s for one-time future execution in a background
 * thread.
 */
public interface Timer
{
    /**
     * Schedules the specified {@link ITimerTask} for one-time execution after
     * the specified delay.
     *
     * @return a handle which is associated with the specified task
     *
     * @throws InvalidOperationException       if this timer has been {@linkplain #stop() stopped} already
     * @throws RejectedExecutionException if the pending timeouts are too many and creating new timeout
     *                                    can cause instability in the system.
     */
    Timeout newTimeout(ITimerTask task, long delay, TimeSpan unit);

    /**
     * Releases all resources acquired by this {@link Timer} and cancels all
     * tasks which were scheduled but not executed yet.
     *
     * @return the handles associated with the tasks which were canceled by
     *         this method
     */
    ISet<Timeout> stop();
}