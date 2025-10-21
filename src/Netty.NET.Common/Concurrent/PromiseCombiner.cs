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
using System.Diagnostics;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * <p>A promise combiner monitors the outcome of a number of discrete futures, then notifies a final, aggregate promise
 * when all of the combined futures are finished. The aggregate promise will succeed if and only if all of the combined
 * futures succeed. If any of the combined futures fail, the aggregate promise will fail. The cause failure for the
 * aggregate promise will be the failure for one of the failed combined futures; if more than one of the combined
 * futures fails, exactly which cause of failure will be assigned to the aggregate promise is undefined.</p>
 *
 * <p>Callers may populate a promise combiner with any number of futures to be combined via the
 * {@link PromiseCombiner#add(Future)} and {@link PromiseCombiner#addAll(Future[])} methods. When all futures to be
 * combined have been added, callers must provide an aggregate promise to be notified when all combined promises have
 * finished via the {@link PromiseCombiner#finish(IPromise)} method.</p>
 *
 * <p>This implementation is <strong>NOT</strong> thread-safe and all methods must be called
 * from the {@link IEventExecutor} thread.</p>
 */
public class PromiseCombiner 
{
    private int expectedCount;
    private int doneCount;
    private IPromise<Void> aggregatePromise;
    private Exception cause;
    private readonly IEventExecutor _executor;

    private readonly PromiseCombinerListener _listener;

    private class PromiseCombinerListener
    {
        private readonly PromiseCombiner _combiner;
        
        public PromiseCombinerListener(PromiseCombiner combiner)
        {
            _combiner = combiner;
        }
        
        public void operationComplete(Task future) {
            if (_combiner._executor.inEventLoop()) {
                operationComplete0(future);
            } else {
                _combiner._executor.execute(AnonymousRunnable.Create(() =>
                {
                    operationComplete0(future);
                }));
            }
        }
        
        private void operationComplete0(Task future)
        {
            Debug.Assert(_combiner._executor.inEventLoop());
            ++_combiner.doneCount;
            if (!future.IsCompletedSuccessfully && _combiner.cause == null)
            {
                _combiner.cause = future.Exception;
            }
            if (_combiner.doneCount == _combiner.expectedCount && _combiner.aggregatePromise != null) {
                _combiner.tryPromise();
            }
        }
    }


    /**
     * Deprecated use {@link PromiseCombiner#PromiseCombiner(IEventExecutor)}.
     */
    [Obsolete]
    public PromiseCombiner() 
        : this(ImmediateEventExecutor.INSTANCE)
    {
    }

    /**
     * The {@link IEventExecutor} to use for notifications. You must call {@link #add(Future)}, {@link #addAll(Future[])}
     * and {@link #finish(IPromise)} from within the {@link IEventExecutor} thread.
     *
     * @param executor the {@link IEventExecutor} to use for notifications.
     */
    public PromiseCombiner(IEventExecutor executor) {
        _executor = ObjectUtil.checkNotNull(executor, "executor");
        _listener = new PromiseCombinerListener(this);
    }

    /**
     * Adds a new future to be combined. New futures may be added until an aggregate promise is added via the
     * {@link PromiseCombiner#finish(IPromise)} method.
     *
     * @param future the future to add to this promise combiner
     */
    //@SuppressWarnings({ "unchecked", "rawtypes" })
    public void add(Task future) {
        checkAddAllowed();
        checkInEventLoop();
        ++expectedCount;
        future.ContinueWith(_listener.operationComplete);
    }

    /**
     * Adds new futures to be combined. New futures may be added until an aggregate promise is added via the
     * {@link PromiseCombiner#finish(IPromise)} method.
     *
     * @param futures the futures to add to this promise combiner
     */
    //@SuppressWarnings({ "unchecked", "rawtypes" })
    public void addAll(params Task[] futures) {
        foreach (Task future in futures) {
            add(future);
        }
    }

    /**
     * <p>Sets the promise to be notified when all combined futures have finished. If all combined futures succeed,
     * then the aggregate promise will succeed. If one or more combined futures fails, then the aggregate promise will
     * fail with the cause of one of the failed futures. If more than one combined future fails, then exactly which
     * failure will be assigned to the aggregate promise is undefined.</p>
     *
     * <p>After this method is called, no more futures may be added via the {@link PromiseCombiner#add(Future)} or
     * {@link PromiseCombiner#addAll(Future[])} methods.</p>
     *
     * @param aggregatePromise the promise to notify when all combined futures have finished
     */
    public void finish(IPromise<Void> aggregatePromise) {
        ObjectUtil.checkNotNull(aggregatePromise, "aggregatePromise");
        checkInEventLoop();
        if (this.aggregatePromise != null) {
            throw new InvalidOperationException("Already finished");
        }
        this.aggregatePromise = aggregatePromise;
        if (doneCount == expectedCount) {
            tryPromise();
        }
    }

    private void checkInEventLoop() {
        if (!_executor.inEventLoop()) {
            throw new InvalidOperationException("Must be called from IEventExecutor thread");
        }
    }

    private bool tryPromise() {
        return (cause == null) ? aggregatePromise.trySuccess(null) : aggregatePromise.tryFailure(cause);
    }

    private void checkAddAllowed() {
        if (aggregatePromise != null) {
            throw new InvalidOperationException("Adding promises is not allowed after finished adding");
        }
    }
}
