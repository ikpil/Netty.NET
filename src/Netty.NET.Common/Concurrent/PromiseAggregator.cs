/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * @deprecated Use {@link PromiseCombiner#PromiseCombiner(EventExecutor)}.
 *
 * {@link GenericFutureListener} implementation which consolidates multiple {@link Future}s
 * into one, by listening to individual {@link Future}s and producing an aggregated result
 * (success/failure) when all {@link Future}s have completed.
 *
 * @param <V> the type of value returned by the {@link Future}
 * @param <F> the type of {@link Future}
 */
[Obsolete]
public class PromiseAggregator<V, F> : IGenericFutureListener<F> where F : IFuture<V>
{
    private readonly IPromise<Void> aggregatePromise;
    private readonly bool failPending;
    // CLR: the future view permits removal of F without a narrowing promise cast.
    // Only IPromise<V> instances are inserted, retaining Java's ordered set semantics.
    private LinkedHashSet<IFuture<V>> pendingPromises;

    /**
     * Creates a new instance.
     *
     * @param aggregatePromise  the {@link Promise} to notify
     * @param failPending  {@code true} to fail pending promises, false to leave them unaffected
     */
    public PromiseAggregator(IPromise<Void> aggregatePromise, bool failPending)
    {
        this.aggregatePromise = ObjectUtil.checkNotNull(aggregatePromise, "aggregatePromise");
        this.failPending = failPending;
    }

    /**
     * See {@link PromiseAggregator#PromiseAggregator(Promise, boolean)}.
     * Defaults {@code failPending} to true.
     */
    public PromiseAggregator(IPromise<Void> aggregatePromise) : this(aggregatePromise, true)
    {
    }

    /**
     * Add the given {@link Promise}s to the aggregator.
     */
    public PromiseAggregator<V, F> add(params IPromise<V>[] promises)
    {
        ObjectUtil.checkNotNull(promises, "promises");
        if (promises.Length == 0)
        {
            return this;
        }
        using (UninterruptibleMonitor.enter(this))
        {
            if (pendingPromises == null)
            {
                pendingPromises = new LinkedHashSet<IFuture<V>>(promises.Length > 1 ? promises.Length : 2);
            }
            foreach (IPromise<V> p in promises)
            {
                if (p == null)
                {
                    continue;
                }
                pendingPromises.Add(p);
                p.addListener(this);
            }
        }
        return this;
    }

    public virtual void operationComplete(F future)
    {
        using (UninterruptibleMonitor.enter(this))
        {
            if (pendingPromises == null)
            {
                aggregatePromise.setSuccess(null);
            }
            else
            {
                pendingPromises.Remove(future);
                if (!future.isSuccess())
                {
                    Exception cause = future.cause();
                    aggregatePromise.setFailure(cause);
                    if (failPending)
                    {
                        foreach (IPromise<V> pendingFuture in pendingPromises)
                        {
                            pendingFuture.setFailure(cause);
                        }
                    }
                }
                else if (pendingPromises.Count == 0)
                {
                    aggregatePromise.setSuccess(null);
                }
            }
        }
    }
}
