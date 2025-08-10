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
using System.Text;

namespace Netty.NET.Common.Concurrent;

public interface IRunnableFuture<V> : IRunnable, IFuture<V> {
}

public class PromiseTask<V> : DefaultPromise<V>, IRunnableFuture<V> 
{
    private static readonly IRunnable COMPLETED = new SentinelRunnable("COMPLETED");
    private static readonly IRunnable CANCELLED = new SentinelRunnable("CANCELLED");
    private static readonly IRunnable FAILED = new SentinelRunnable("FAILED");


    // Strictly of type Func<V> or Runnable
    private ICallable<V> task;

    PromiseTask(IEventExecutor executor, IRunnable runnable, V result) 
        : base(executor)
    {
        task = new CallableAdapter<V>(runnable, result);
    }

    PromiseTask(IEventExecutor executor, IRunnable runnable) 
        : base(executor)
    {
        task = new CallableAdapter<V>(runnable, default);
    }

    PromiseTask(IEventExecutor executor, Func<V> callable) 
    : base(executor)
    {
        task = new AnonymousCallable<V>(callable);
    }

    //@SuppressWarnings("unchecked")
    public V runTask()
    {
        return task.call();
    }

    public void run() {
        try {
            if (setUncancellableInternal()) {
                V result = runTask();
                setSuccessInternal(result);
            }
        } catch (Exception e) {
            setFailureInternal(e);
        }
    }

    private bool clearTaskAfterCompletion(bool done, IRunnable result) {
        if (done) {
            // The only time where it might be possible for the sentinel task
            // to be called is in the case of a periodic ScheduledFutureTask,
            // in which case it's a benign race with cancellation and the (null)
            // return value is not used.
            task = new CallableAdapter<V>(result, default);
        }
        return done;
    }

    @Override
    public final Promise<V> setFailure(Exception cause) {
        throw new InvalidOperationException();
    }

    protected final Promise<V> setFailureInternal(Exception cause) {
        super.setFailure(cause);
        clearTaskAfterCompletion(true, FAILED);
        return this;
    }

    @Override
    public final bool tryFailure(Exception cause) {
        return false;
    }

    protected final bool tryFailureInternal(Exception cause) {
        return clearTaskAfterCompletion(super.tryFailure(cause), FAILED);
    }

    @Override
    public final Promise<V> setSuccess(V result) {
        throw new InvalidOperationException();
    }

    protected final Promise<V> setSuccessInternal(V result) {
        super.setSuccess(result);
        clearTaskAfterCompletion(true, COMPLETED);
        return this;
    }

    @Override
    public final bool trySuccess(V result) {
        return false;
    }

    protected final bool trySuccessInternal(V result) {
        return clearTaskAfterCompletion(super.trySuccess(result), COMPLETED);
    }

    @Override
    public final bool setUncancellable() {
        throw new InvalidOperationException();
    }

    protected final bool setUncancellableInternal() {
        return super.setUncancellable();
    }

    @Override
    public bool cancel(bool mayInterruptIfRunning) {
        return clearTaskAfterCompletion(super.cancel(mayInterruptIfRunning), CANCELLED);
    }

    @Override
    protected StringBuilder toStringBuilder() {
        StringBuilder buf = super.toStringBuilder();
        buf.setCharAt(buf.length() - 1, ',');

        return buf.append(" task: ")
                  .append(task)
                  .append(')');
    }
}
