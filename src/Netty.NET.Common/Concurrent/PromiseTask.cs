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
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public interface IRunnableFuture<V> : IRunnable, IFuture<V>
{
}

public class DefaultPromise<V> : TaskCompletionSource<V>, IPromise<V>
{
    public DefaultPromise(IEventExecutor executor)
    {
    }

    public virtual AggregateException cause()
    {
        throw new NotImplementedException();
    }

    public virtual bool cancel(bool mayInterruptIfRunning)
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> setSuccess(V result)
    {
        throw new NotImplementedException();
    }

    public virtual bool trySuccess(V result)
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> setFailure(Exception cause)
    {
        throw new NotImplementedException();
    }

    public virtual bool tryFailure(Exception cause)
    {
        throw new NotImplementedException();
    }

    public virtual bool setUncancellable()
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> addListener(IGenericFutureListener<IFuture<V>> listener)
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners)
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> removeListener(IGenericFutureListener<IFuture<V>> listener)
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners)
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> await()
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> awaitUninterruptibly()
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> sync()
    {
        throw new NotImplementedException();
    }

    public virtual IPromise<V> syncUninterruptibly()
    {
        throw new NotImplementedException();
    }
    
    protected virtual void checkDeadLock() {
        throw new NotImplementedException();
    }

    protected virtual StringBuilder toStringBuilder()
    {
        throw new NotImplementedException();
    }
}

public class PromiseTask<V> : DefaultPromise<V>, IRunnableFuture<V>
{
    private static readonly IRunnable COMPLETED = new SentinelRunnable("COMPLETED");
    private static readonly IRunnable CANCELLED = new SentinelRunnable("CANCELLED");
    private static readonly IRunnable FAILED = new SentinelRunnable("FAILED");


    // Strictly of type Func<V> or IRunnable
    private ICallable<V> task;

    internal PromiseTask(IEventExecutor executor, IRunnable runnable, V result)
        : base(executor)
    {
        task = new CallableAdapter<V>(runnable, result);
    }

    internal PromiseTask(IEventExecutor executor, IRunnable runnable)
        : base(executor)
    {
        task = new CallableAdapter<V>(runnable, default);
    }

    internal PromiseTask(IEventExecutor executor, Func<V> callable)
        : base(executor)
    {
        task = new AnonymousCallable<V>(callable);
    }

    //@SuppressWarnings("unchecked")
    public V runTask()
    {
        return task.call();
    }

    public virtual void run()
    {
        try
        {
            if (setUncancellableInternal())
            {
                V result = runTask();
                setSuccessInternal(result);
            }
        }
        catch (Exception e)
        {
            setFailureInternal(e);
        }
    }

    private bool clearTaskAfterCompletion(bool done, IRunnable result)
    {
        if (done)
        {
            // The only time where it might be possible for the sentinel task
            // to be called is in the case of a periodic ScheduledFutureTask,
            // in which case it's a benign race with cancellation and the (null)
            // return value is not used.
            task = new CallableAdapter<V>(result, default);
        }

        return done;
    }

    public override IPromise<V> setFailure(Exception cause)
    {
        throw new InvalidOperationException();
    }

    protected IPromise<V> setFailureInternal(Exception cause)
    {
        base.setFailure(cause);
        clearTaskAfterCompletion(true, FAILED);
        return this;
    }

    public override bool tryFailure(Exception cause)
    {
        return false;
    }

    protected virtual bool tryFailureInternal(Exception cause)
    {
        return clearTaskAfterCompletion(base.tryFailure(cause), FAILED);
    }

    public override IPromise<V> setSuccess(V result)
    {
        throw new InvalidOperationException();
    }

    protected virtual IPromise<V> setSuccessInternal(V result)
    {
        base.setSuccess(result);
        clearTaskAfterCompletion(true, COMPLETED);
        return this;
    }

    public override bool trySuccess(V result)
    {
        return false;
    }

    protected virtual bool trySuccessInternal(V result)
    {
        return clearTaskAfterCompletion(base.trySuccess(result), COMPLETED);
    }

    public override bool setUncancellable()
    {
        throw new InvalidOperationException();
    }

    protected virtual bool setUncancellableInternal()
    {
        return base.setUncancellable();
    }

    public bool cancel(bool mayInterruptIfRunning)
    {
        return clearTaskAfterCompletion(base.cancel(mayInterruptIfRunning), CANCELLED);
    }

    protected override StringBuilder toStringBuilder()
    {
        StringBuilder buf = base.toStringBuilder();
        buf[buf.Length - 1] = ',';

        return buf.Append(" task: ")
            .Append(task)
            .Append(')');
    }
}