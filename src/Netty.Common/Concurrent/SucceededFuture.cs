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

public class CompleteFuture<T>
{
    public CompleteFuture(IEventExecutor executor)
    {
        // ..
    }

    public virtual Exception cause()
    {
        return null;
        // ..
    }
}

/**
 * The {@link CompleteFuture} which is succeeded already.  It is
 * recommended to use {@link IEventExecutor#newSucceededFuture(object)} instead of
 * calling the constructor of this future.
 */
public sealed class SucceededFuture<V> : CompleteFuture<V>
{
    private readonly V result;

    /**
     * Creates a new instance.
     *
     * @param executor the {@link IEventExecutor} associated with this future
     */
    public SucceededFuture(IEventExecutor executor, V result)
        : base(executor)
    {
        this.result = result;
    }

    public override Exception cause()
    {
        return null;
    }

    public bool isSuccess()
    {
        return true;
    }

    public V getNow()
    {
        return result;
    }
}