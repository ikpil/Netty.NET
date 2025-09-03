using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    protected virtual QueueingTaskNode<T> newTaskFor<T>(IRunnable runnable, T value)
    {
        var node = new QueueingRunnableTaskNode<T>(runnable, value);
        return node;
    }

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
    protected virtual QueueingTaskNode<T> newTaskFor<T>(ICallable<T> callable)
    {
        var node = new QueueingCallableTaskNode<T>(callable);
        return node;
    }

    /**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
    public virtual Task submit(IRunnable task)
    {
        if (task == null) throw new NullReferenceException();
        var ftask = newTaskFor(task, Void.Empty);
        execute(ftask);
        return ftask.Completion;
    }

    /**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
    public virtual Task<T> submit<T>(IRunnable task, T result)
    {
        if (task == null) throw new NullReferenceException();
        var ftask = newTaskFor(task, result);
        execute(ftask);
        return ftask.Completion;
    }


    /**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
    public virtual Task<T> submit<T>(ICallable<T> task)
    {
        if (task == null) throw new NullReferenceException();
        var ftask = newTaskFor(task);
        execute(ftask);
        return ftask.Completion;
    }

    /**
     * the main mechanics of invokeAny.
     */
    private T doInvokeAny<T>(ICollection<T> tasks, bool timed, long nanos) where T : ICallable<T>
    {
        if (tasks == null)
            throw new NullReferenceException();
        int ntasks = tasks.Count;
        if (ntasks == 0)
            throw new ArgumentException();

        var futures = new List<QueueingTaskNode<T>>(ntasks);
        ExecutorCompletionService<T> ecs =
            new ExecutorCompletionService<T>(this);

        // For efficiency, especially in executors with limited
        // parallelism, check to see if previously submitted tasks are
        // done before submitting more of them. This interleaving
        // plus the exception mechanics account for messiness of main
        // loop.

        try
        {
            // Record exceptions so that if we fail to obtain any
            // result, we can throw the last exception we got.
            ExecutionException ee = null;
            long deadline = timed ? PreciseTimer.nanoTime() + nanos : 0L;
            IEnumerable<T> it = tasks;

            // Start one task for sure; the rest incrementally
            futures.add(ecs.submit(it.next()));
            --ntasks;
            int active = 1;

            for (;;)
            {
                Task<T> f = ecs.poll();
                if (f == null)
                {
                    if (ntasks > 0)
                    {
                        --ntasks;
                        futures.add(ecs.submit(it.next()));
                        ++active;
                    }
                    else if (active == 0)
                        break;
                    else if (timed)
                    {
                        f = ecs.poll(nanos, NANOSECONDS);
                        if (f == null)
                            throw new TimeoutException();
                        nanos = deadline - PreciseTimer.nanoTime();
                    }
                    else
                        f = ecs.take();
                }

                if (f != null)
                {
                    --active;
                    try
                    {
                        return f.get();
                    }
                    catch (ExecutionException eex)
                    {
                        ee = eex;
                    }
                    catch (RuntimeException rex)
                    {
                        ee = new ExecutionException(rex);
                    }
                }
            }

            if (ee == null)
                ee = new ExecutionException();
            throw ee;
        }
        finally
        {
            cancelAll(futures);
        }
    }

    public T invokeAny<T>(ICollection<T> tasks) where T : ICallable<T>
    {
        try
        {
            return doInvokeAny(tasks, false, 0);
        }
        catch (TimeoutException cannotHappen)
        {
            Debug.Assert(false);
            return default;
        }
    }

    public T invokeAny<T>(ICollection<T> tasks, TimeSpan timeout) where T : ICallable<T>
    {
        return doInvokeAny(tasks, true, (long)timeout.TotalNanoseconds);
    }

    public List<Task<T>> invokeAll<T>(ICollection<T> tasks) where T : ICallable<T>
    {
        if (tasks == null)
            throw new NullReferenceException();
        var futures = new List<QueueingTaskNode<T>>(tasks.Count);
        try
        {
            foreach (var t in tasks)
            {
                var f = newTaskFor(t);
                futures.Add(f);
                execute(f);
            }

            for (int i = 0, size = futures.Count; i < size; i++)
            {
                var f = futures[i];
                if (!f.IsCompleted)
                {
                    try
                    {
                        f.Wait();
                    }
                    catch (Exception e)
                    {
                        _ = e;
                    }
                }
            }

            return futures;
        }
        catch (Exception t)
        {
            cancelAll(futures);
            throw;
        }
    }

    public List<Task<T>> invokeAll<T>(ICollection<T> tasks, TimeSpan timeout) where T : ICallable<T>
    {
        if (tasks == null)
            throw new NullReferenceException();
        long nanos = (long)timeout.TotalNanoseconds;
        long deadline = PreciseTimer.nanoTime() + nanos;
        var futures = new List<QueueingTaskNode<T>>(tasks.Count);
        int j = 0;
        timedOut:
        try
        {
            foreach (var t in tasks)
            {
                var node = newTaskFor(t);
                futures.Add(node);
            }

            int size = futures.Count;

            // Interleave time checks and calls to execute in case
            // executor doesn't have any/much parallelism.
            for (int i = 0; i < size; i++)
            {
                if (((i == 0) ? nanos : deadline - PreciseTimer.nanoTime()) <= 0L)
                    break
                timedOut;
                execute((IRunnable)futures.get(i));
            }

            for (; j < size; j++)
            {
                Task<T> f = futures.get(j);
                if (!f.isDone())
                {
                    try { f.get(deadline - PreciseTimer.nanoTime(), NANOSECONDS); }
                    catch (CancellationException |

                    ExecutionException ignore) {
                    }
                    catch (TimeoutException timedOut) {
                        break timedOut;
                    }
                }
            }

            return futures;
        }
        catch (Exception t)
        {
            cancelAll(futures);
            throw t;
        }

        // Timed out before all the tasks could be completed; cancel remaining
        cancelAll(futures, j);
        return futures;
    }

    private static void cancelAll<T>(List<QueueingTaskNode<T>> futures)
    {
        cancelAll(futures, 0);
    }

    /** Cancels all futures with index at least j. */
    private static void cancelAll<T>(List<QueueingTaskNode<T>> futures, int j)
    {
        for (int size = futures.Count; j < size; j++)
            futures[j].cancel(true);
    }
}