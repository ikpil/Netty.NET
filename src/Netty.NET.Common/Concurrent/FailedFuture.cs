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
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;


/**
 * The {@link CompleteFuture} which is failed already.  It is
 * recommended to use {@link IEventExecutor#newFailedFuture(Exception)}
 * instead of calling the constructor of this future.
 */
public static class FailedFuture
{
    public static TaskCompletionSource<T> Create<T>(IEventExecutor executor, Exception cause)
    {
        var tcs = new TaskCompletionSource<T>();
        tcs.SetException(cause);
        return tcs;
    }
}
