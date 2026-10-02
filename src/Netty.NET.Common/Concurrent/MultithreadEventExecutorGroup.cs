/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract base class for {@link EventExecutorGroup} implementations that handles their tasks with multiple threads at
 * the same time.
 */
public abstract class MultithreadEventExecutorGroup : AbstractEventExecutorGroup
{
    private readonly IEventExecutor[] children;
    private readonly ISet<IEventExecutor> readonlyChildren;
    private int terminatedChildren;
    private readonly TaskCompletionSource _terminationSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IEventExecutorChooser chooser;

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param threadFactory     the ThreadFactory to use, or {@code null} if the default should be used.
     * @param args              arguments which will passed to each {@link #newChild(Executor, Object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, IThreadFactory threadFactory, params object[] args)
        : this(nThreads, threadFactory == null ? null : new ThreadPerTaskExecutor(threadFactory), args)
    {
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param executor          the Executor to use, or {@code null} if the default should be used.
     * @param args              arguments which will passed to each {@link #newChild(Executor, Object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, IExecutor executor, params object[] args)
        : this(nThreads, executor, DefaultEventExecutorChooserFactory.INSTANCE, args)
    {
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param executor          the Executor to use, or {@code null} if the default should be used.
     * @param chooserFactory    the {@link EventExecutorChooserFactory} to use.
     * @param args              arguments which will passed to each {@link #newChild(Executor, Object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, IExecutor executor,
        IEventExecutorChooserFactory chooserFactory, params object[] args)
    {
        ObjectUtil.checkPositive(nThreads, "nThreads");

        if (executor == null)
        {
            executor = new ThreadPerTaskExecutor(newDefaultThreadFactory());
        }

        children = new IEventExecutor[nThreads];

        for (int i = 0; i < nThreads; i++)
        {
            bool success = false;
            try
            {
                children[i] = newChild(executor, args);
                success = true;
            }
            catch (Exception e)
            {
                // TODO: Think about if this is a good exception type
                throw new InvalidOperationException("failed to create a child event loop", e);
            }
            finally
            {
                if (!success)
                {
                    for (int j = 0; j < i; j++)
                    {
                        children[j].ShutdownGracefullyAsync();
                    }

                    for (int j = 0; j < i; j++)
                    {
                        IEventExecutor e = children[j];
                        try
                        {
                            while (!e.isTerminated())
                            {
                                e.awaitTermination(TimeSpan.FromSeconds(int.MaxValue));
                            }
                        }
                        catch (ThreadInterruptedException interrupted)
                        {
                            // Let the caller handle the interruption.
                            Thread.CurrentThread.Interrupt();
                            break;
                        }
                    }
                }
            }
        }

        chooser = chooserFactory.newChooser(children);

        foreach (IEventExecutor e in children)
        {
            Task termination = e.Termination;
            // CLR adaptation: completion counting owns no executor-local state.
            // Do not capture the constructor's SynchronizationContext or
            // ExecutionContext. A failed child still counts as terminated, as
            // in the original listener; the group signal succeeds after all.
            termination.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
            {
                _ = termination.Exception;
                if (Interlocked.Increment(ref terminatedChildren) == children.Length)
                    _terminationSource.SetResult();
            });
        }

        var childrenSet = new LinkedHashSet<IEventExecutor>(children);
        // CLR adaptation: callers can enumerate, but cannot cast back to a mutable set.
        readonlyChildren = childrenSet;
    }

    protected virtual IThreadFactory newDefaultThreadFactory()
    {
        return new DefaultThreadFactory(GetType());
    }

    public override IEventExecutor next()
    {
        return chooser.next();
    }

    public override IEnumerable<IEventExecutor> iterator()
    {
        foreach (var child in readonlyChildren)
        {
            yield return child;
        }
    }

    /**
     * Return the number of {@link EventExecutor} this implementation uses. This number is the maps
     * 1:1 to the threads it use.
     */
    public int executorCount()
    {
        return children.Length;
    }

    /**
     * Returns the number of currently active threads if the group is using an
     * {@link ObservableEventExecutorChooser}. Otherwise, for a non-scaling group,
     * this method returns the total number of threads, as all are considered active.
     *
     * @return the count of active threads.
     */
    public virtual int activeExecutorCount() => chooser is IObservableEventExecutorChooser observable ?
        observable.activeExecutorCount() : executorCount();

    /**
     * Returns a list of real-time utilization metrics if the group was configured
     * with a compatible {@link EventExecutorChooserFactory}, otherwise an empty list.
     *
     * @return A list of {@link AutoScalingUtilizationMetric} objects.
     */
    public virtual IReadOnlyList<AutoScalingUtilizationMetric> executorUtilizations() =>
        chooser is IObservableEventExecutorChooser observable ? observable.executorUtilizations() :
            Array.Empty<AutoScalingUtilizationMetric>();

    /**
     * Create a new EventExecutor which will later then accessible via the {@link #next()}  method. This method will be
     * called for each thread that will serve this {@link MultithreadEventExecutorGroup}.
     *
     */
    protected abstract IEventExecutor newChild(IExecutor executor, params object[] args);

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        foreach (IEventExecutor l in children)
        {
            l.ShutdownGracefullyAsync(quietPeriod, timeout);
        }

        return Termination;
    }

    public override Task Termination => _terminationSource.Task;

    [Obsolete]
    public override void shutdown()
    {
        foreach (IEventExecutor l in children)
        {
            l.shutdown();
        }
    }

    public override bool isShuttingDown()
    {
        foreach (IEventExecutor l in children)
        {
            if (!l.isShuttingDown())
            {
                return false;
            }
        }

        return true;
    }

    public override bool isShutdown()
    {
        foreach (IEventExecutor l in children)
        {
            if (!l.isShutdown())
            {
                return false;
            }
        }

        return true;
    }

    public override bool isTerminated()
    {
        foreach (IEventExecutor l in children)
        {
            if (!l.isTerminated())
            {
                return false;
            }
        }

        return true;
    }

    public override bool awaitTermination(TimeSpan timeout)
    {
        long deadline = unchecked(SystemTimer.nanoTime() + AbstractScheduledEventExecutor.toNanos(timeout));
        foreach (IEventExecutor l in children)
        {
            bool breakLoop = false;
            for (;;)
            {
                long timeLeft = unchecked(deadline - SystemTimer.nanoTime());
                if (timeLeft <= 0)
                {
                    breakLoop = true;
                    break;
                }

                if (l.awaitTermination(TimeSpan.FromTicks(timeLeft / 100)))
                {
                    break;
                }
            }

            if (breakLoop)
                break;
        }

        return isTerminated();
    }

}
