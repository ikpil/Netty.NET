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
namespace Netty.NET.Common.Concurrent;




class PromiseTask<V> extends DefaultPromise<V> : RunnableFuture<V> {

    private static readonly class RunnableAdapter<T> : Func<T> {
        final Runnable task;
        final T result;

        RunnableAdapter(Runnable task, T result) {
            this.task = task;
            this.result = result;
        }

        @Override
        public T call() {
            task.run();
            return result;
        }

        @Override
        public string toString() {
            return "Callable(task: " + task + ", result: " + result + ')';
        }
    }

    private static readonly Runnable COMPLETED = new SentinelRunnable("COMPLETED");
    private static readonly Runnable CANCELLED = new SentinelRunnable("CANCELLED");
    private static readonly Runnable FAILED = new SentinelRunnable("FAILED");

    private static class SentinelRunnable : Runnable {
        private readonly string name;

        SentinelRunnable(string name) {
            this.name = name;
        }

        @Override
        public void run() { } // no-op

        @Override
        public string toString() {
            return name;
        }
    }

    // Strictly of type Func<V> or Runnable
    private object task;

    PromiseTask(IEventExecutor executor, Runnable runnable, V result) {
        super(executor);
        task = result == null ? runnable : new RunnableAdapter<V>(runnable, result);
    }

    PromiseTask(IEventExecutor executor, Runnable runnable) {
        super(executor);
        task = runnable;
    }

    PromiseTask(IEventExecutor executor, Func<V> callable) {
        super(executor);
        task = callable;
    }

    @Override
    public final int hashCode() {
        return System.identityHashCode(this);
    }

    @Override
    public final bool equals(object obj) {
        return this == obj;
    }

    @SuppressWarnings("unchecked")
    V runTask() {
        final object task = this.task;
        if (task instanceof Callable) {
            return ((Func<V>) task).call();
        }
        ((Runnable) task).run();
        return null;
    }

    @Override
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

    private bool clearTaskAfterCompletion(bool done, Runnable result) {
        if (done) {
            // The only time where it might be possible for the sentinel task
            // to be called is in the case of a periodic ScheduledFutureTask,
            // in which case it's a benign race with cancellation and the (null)
            // return value is not used.
            task = result;
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
