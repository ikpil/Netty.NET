using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public abstract class AbstractExecutorService : IExecutorService
{
    public abstract void execute(IRunnable task);
    public abstract void shutdown();
    public abstract List<IRunnable> shutdownNow();
    public abstract bool isShutdown();
    public abstract bool isTerminated();
    public abstract bool awaitTermination(TimeSpan timeout);

    /**
     * Returns a {@code RunnableFuture} for the given runnable and default
     * value.
     *
     * @param runnable the runnable task being wrapped
     * @param value the default value for the returned future
     * @param <T> the type of the given value
     * @return a {@code RunnableFuture} which, when run, will run the
     * underlying runnable and which, as a {@code Future}, will yield
     * the given value as its result and provide for cancellation of
     * the underlying task
     * @since 1.6
     */
    protected abstract IRunnableFuture<T> newTaskFor<T>(IRunnable runnable, T value);

    /**
     * Returns a {@code RunnableFuture} for the given callable task.
     *
     * @param callable the callable task being wrapped
     * @param <T> the type of the callable's result
     * @return a {@code RunnableFuture} which, when run, will call the
     * underlying callable and which, as a {@code Future}, will yield
     * the callable's result as its result and provide for
     * cancellation of the underlying task
     * @since 1.6
     */
    protected abstract IRunnableFuture<T> newTaskFor<T>(ICallable<T> callable);

    /**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
    public virtual IFuture<Void> submit(IRunnable task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var ftask = newTaskFor<Void>(task, null);
        execute(ftask);
        return ftask;
    }

    /**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
    public virtual IFuture<T> submit<T>(IRunnable task, T result)
    {
        ArgumentNullException.ThrowIfNull(task);
        var ftask = newTaskFor(task, result);
        execute(ftask);
        return ftask;
    }


    /**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
    public virtual IFuture<T> submit<T>(ICallable<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var ftask = newTaskFor(task);
        execute(ftask);
        return ftask;
    }

    /**
     * the main mechanics of invokeAny.
     */
    // CLR implementation of the inherited JDK ExecutorService contract.
    // Netty supplies PromiseTask from newTaskFor; completion is queued directly
    // after run, independently of executor-dispatched future listeners.
    private T doInvokeAny<T>(ICollection<ICallable<T>> tasks, bool timed, long nanos)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        int remaining = tasks.Count;
        if (remaining == 0) throw new ArgumentException("tasks is empty", nameof(tasks));
        var futures = new List<IFuture<T>>(remaining);
        var completed = new BlockingCollection<IFuture<T>>();
        using var iterator = tasks.GetEnumerator();
        long started = Stopwatch.GetTimestamp();
        AggregateException failure = null;
        try
        {
            void submitNext()
            {
                if (!iterator.MoveNext()) throw new InvalidOperationException("tasks changed during invocation");
                ICallable<T> callable = iterator.Current;
                ArgumentNullException.ThrowIfNull(callable);
                IRunnableFuture<T> future = newTaskFor(callable);
                futures.Add(future);
                execute(Runnables.Create(() =>
                {
                    try { future.run(); }
                    finally { completed.Add(future); }
                }));
                --remaining;
            }

            submitNext();
            int active = 1;
            for (;;)
            {
                IFuture<T> future;
                if (!completed.TryTake(out future))
                {
                    if (remaining > 0)
                    {
                        submitNext();
                        ++active;
                    }
                    else if (active == 0)
                    {
                        break;
                    }
                    else if (timed)
                    {
                        for (;;)
                        {
                            long left = remainingNanos(started, nanos);
                            if (left <= 0) throw new TimeoutException();
                            int millis = (int)Math.Min(int.MaxValue, 1 + (left - 1) / 1_000_000);
                            if (completed.TryTake(out future, millis)) break;
                        }
                    }
                    else
                    {
                        future = completed.Take();
                    }
                }
                if (future != null)
                {
                    --active;
                    try { return future.get(); }
                    catch (AggregateException error) { failure = error; }
                    catch (OperationCanceledException error) { failure = new AggregateException(error); }
                }
            }
            throw failure ?? new AggregateException();
        }
        finally { cancelAll(futures); }
    }

    public virtual T invokeAny<T>(ICollection<ICallable<T>> tasks) => doInvokeAny(tasks, false, 0);

    public virtual T invokeAny<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout) =>
        doInvokeAny(tasks, true, AbstractScheduledEventExecutor.toNanos(timeout));

    public virtual List<IFuture<T>> invokeAll<T>(ICollection<ICallable<T>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var futures = new List<IFuture<T>>(tasks.Count);
        bool done = false;
        try
        {
            foreach (ICallable<T> callable in tasks)
            {
                ArgumentNullException.ThrowIfNull(callable);
                IRunnableFuture<T> future = newTaskFor(callable);
                futures.Add(future);
                execute(future);
            }
            foreach (IFuture<T> future in futures)
            {
                if (!future.isDone())
                {
                    try { future.get(); }
                    catch (OperationCanceledException) { }
                    catch (AggregateException) { }
                }
            }
            done = true;
            return futures;
        }
        finally { if (!done) cancelAll(futures); }
    }

    public virtual List<IFuture<T>> invokeAll<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        long nanos = AbstractScheduledEventExecutor.toNanos(timeout);
        long started = Stopwatch.GetTimestamp();
        var futures = new List<IFuture<T>>(tasks.Count);
        bool done = false;
        try
        {
            foreach (ICallable<T> callable in tasks)
            {
                ArgumentNullException.ThrowIfNull(callable);
                futures.Add(newTaskFor(callable));
            }
            foreach (IFuture<T> future in futures)
            {
                if (remainingNanos(started, nanos) <= 0) return futures;
                execute((IRunnableFuture<T>)future);
            }
            foreach (IFuture<T> future in futures)
            {
                if (future.isDone()) continue;
                long left = remainingNanos(started, nanos);
                if (left <= 0) return futures;
                try { future.get(TimeSpan.FromTicks(left / 100)); }
                catch (OperationCanceledException) { }
                catch (AggregateException) { }
                catch (TimeoutException) { return futures; }
            }
            done = true;
            return futures;
        }
        finally { if (!done) cancelAll(futures); }
    }

    private static long remainingNanos(long started, long timeoutNanos) =>
        timeoutNanos <= 0 ? timeoutNanos :
            timeoutNanos - AbstractScheduledEventExecutor.toNanos(Stopwatch.GetElapsedTime(started));

    private static void cancelAll<T>(List<IFuture<T>> futures)
    {
        foreach (IFuture<T> future in futures) future.cancel(true);
    }
}
