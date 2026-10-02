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
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public interface IRunnableFuture<V> : IRunnable, IFuture<V>
{
}

public class PromiseTask<V> : DefaultPromise<V>, IRunnableFuture<V>
{
    private static readonly IRunnable COMPLETED = new SentinelRunnable("COMPLETED");
    private static readonly IRunnable CANCELLED = new SentinelRunnable("CANCELLED");
    private static readonly IRunnable FAILED = new SentinelRunnable("FAILED");

    private sealed class SentinelRunnable : IRunnable
    {
        private readonly string name;
        internal SentinelRunnable(string name) { this.name = name; }
        public void run() { } // no-op
        public override string ToString() => name;
    }


    // Strictly of type Callable<V> or Runnable
    // CLR: Runnable completion without an explicit result yields default(V),
    // including null for reference types, since value types cannot represent null.
    private object task;

    private sealed class RunnableAdapter<T> : ICallable<T>
    {
        private readonly IRunnable task;
        private readonly T result;
        internal RunnableAdapter(IRunnable task, T result) { this.task = task; this.result = result; }
        public T call() { task.run(); return result; }
        public override string ToString()
        {
            string value = result is bool flag ? (flag ? "true" : "false") :
                result is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : result?.ToString();
            return "Callable(task: " + task + ", result: " + value + ')';
        }
    }

    public sealed override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public sealed override bool Equals(object other) => ReferenceEquals(this, other);

    internal PromiseTask(IEventExecutor executor, IRunnable runnable, V result)
        : base(executor)
    {
        task = result is null ? runnable : new RunnableAdapter<V>(runnable, result);
    }

    internal PromiseTask(IEventExecutor executor, IRunnable runnable)
        : base(executor)
    {
        task = runnable;
    }

    internal PromiseTask(IEventExecutor executor, Func<V> callable)
        : base(executor)
    {
        task = new AnonymousCallable<V>(callable);
    }

    internal PromiseTask(IEventExecutor executor, ICallable<V> callable)
        : base(executor)
    {
        task = callable;
    }

    //@SuppressWarnings("unchecked")
    internal virtual V runTask()
    {
        object task = this.task;
        if (task is ICallable<V> callable) return callable.call();
        ((IRunnable)task).run();
        return default;
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
            task = result;
        }

        return done;
    }

    public sealed override IPromise<V> setFailure(Exception cause)
    {
        throw new InvalidOperationException();
    }

    protected IPromise<V> setFailureInternal(Exception cause)
    {
        base.setFailure(cause);
        clearTaskAfterCompletion(true, FAILED);
        return this;
    }

    public sealed override bool tryFailure(Exception cause)
    {
        return false;
    }

    protected bool tryFailureInternal(Exception cause)
    {
        return clearTaskAfterCompletion(base.tryFailure(cause), FAILED);
    }

    public sealed override IPromise<V> setSuccess(V result)
    {
        throw new InvalidOperationException();
    }

    protected IPromise<V> setSuccessInternal(V result)
    {
        base.setSuccess(result);
        clearTaskAfterCompletion(true, COMPLETED);
        return this;
    }

    public sealed override bool trySuccess(V result)
    {
        return false;
    }

    protected bool trySuccessInternal(V result)
    {
        return clearTaskAfterCompletion(base.trySuccess(result), COMPLETED);
    }

    public sealed override bool setUncancellable()
    {
        throw new InvalidOperationException();
    }

    protected bool setUncancellableInternal()
    {
        return base.setUncancellable();
    }

    public override bool cancel(bool mayInterruptIfRunning)
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
