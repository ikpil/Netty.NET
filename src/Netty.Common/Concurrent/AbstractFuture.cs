/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Common.Concurrent;

public interface IFuture<V>
{
    AggregateException cause();
}

/**
 * Abstract {@link Future} implementation which does not allow for cancellation.
 *
 * @param <V>
 */
public abstract class AbstractFuture<V> : Task<V>, IFuture<V>
{
    public AbstractFuture() : base(() => default)
    {
    }

    public virtual AggregateException cause()
    {
        return Exception;
    }

    public virtual V get()
    {
        Wait();

        AggregateException cause = this.cause();
        if (cause == null)
        {
            return Result;
        }

        if (cause.GetBaseException() is TaskCanceledException)
        {
            throw (TaskCanceledException)cause.GetBaseException();
        }

        throw cause;
    }

    public virtual V get(TimeSpan timeout)
    {
        if (Wait(timeout))
        {
            AggregateException cause = this.cause();
            if (cause == null)
            {
                return Result;
            }

            if (cause.GetBaseException() is TaskCanceledException)
            {
                throw (TaskCanceledException)cause.GetBaseException();
            }

            throw cause;
        }

        throw new TimeoutException();
    }
}