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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * Expose helper methods which create different {@link IRejectedExecutionHandler}s.
 */
public static class RejectedExecutionHandlers
{
    private static readonly IRejectedExecutionHandler REJECT = new RejectedExecutionHandler();

    /**
     * Returns a {@link IRejectedExecutionHandler} that will always just throw a {@link RejectedExecutionException}.
     */
    public static IRejectedExecutionHandler reject()
    {
        return REJECT;
    }

    /**
     * Tries to backoff when the task can not be added due restrictions for an configured amount of time. This
     * is only done if the task was added from outside of the event loop which means
     * {@link IEventExecutor#inEventLoop()} returns {@code false}.
     */
    public static IRejectedExecutionHandler backoff(int retries, TimeSpan backoffAmount)
    {
        ObjectUtil.checkPositive(retries, "retries");
        return new RejectedBackOffExecutionHandler(retries, backoffAmount);
    }
}