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

namespace Netty.NET.Common.Concurrent;

/**
 * @deprecated Use {@link PromiseCombiner#PromiseCombiner(IEventExecutor)}.
 *
 * {@link GenericFutureListener} implementation which consolidates multiple {@link Future}s
 * into one, by listening to individual {@link Future}s and producing an aggregated result
 * (success/failure) when all {@link Future}s have completed.
 *
 * @param <V> the type of value returned by the {@link Future}
 * @param <F> the type of {@link Future}
 */
@Deprecated
public class PromiseAggregator<V, F extends Future<V>> : GenericFutureListener<F> {

    private readonly Promise<?> aggregatePromise;
    private readonly bool failPending;
    private ISet<Promise<V>> pendingPromises;

    /**
     * Creates a new instance.
     *
     * @param aggregatePromise  the {@link Promise} to notify
     * @param failPending  {@code true} to fail pending promises, false to leave them unaffected
     */
    public PromiseAggregator(Promise<Void> aggregatePromise, bool failPending) {
        this.aggregatePromise = ObjectUtil.checkNotNull(aggregatePromise, "aggregatePromise");
        this.failPending = failPending;
    }

    /**
     * See {@link PromiseAggregator#PromiseAggregator(Promise, bool)}.
     * Defaults {@code failPending} to true.
     */
    public PromiseAggregator(Promise<Void> aggregatePromise) {
        this(aggregatePromise, true);
    }

    /**
     * Add the given {@link Promise}s to the aggregator.
     */
    @SafeVarargs
    public final PromiseAggregator<V, F> add(Promise<V>... promises) {
        ObjectUtil.checkNotNull(promises, "promises");
        if (promises.length == 0) {
            return this;
        }
        synchronized (this) {
            if (pendingPromises == null) {
                int size;
                if (promises.length > 1) {
                    size = promises.length;
                } else {
                    size = 2;
                }
                pendingPromises = new LinkedHashSet<Promise<V>>(size);
            }
            for (Promise<V> p : promises) {
                if (p == null) {
                    continue;
                }
                pendingPromises.add(p);
                p.addListener(this);
            }
        }
        return this;
    }

    @Override
    public synchronized void operationComplete(F future) {
        if (pendingPromises == null) {
            aggregatePromise.setSuccess(null);
        } else {
            pendingPromises.remove(future);
            if (!future.isSuccess()) {
                Exception cause = future.cause();
                aggregatePromise.setFailure(cause);
                if (failPending) {
                    for (Promise<V> pendingFuture : pendingPromises) {
                        pendingFuture.setFailure(cause);
                    }
                }
            } else {
                if (pendingPromises.isEmpty()) {
                    aggregatePromise.setSuccess(null);
                }
            }
        }
    }

}
