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
// CLR: original constructor Executor parameters are Action<Action> worker starters.
// The entry runs for the worker lifetime; queued submissions use the event executor APIs.
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
        : this(nThreads, threadFactory == null ? (Action<Action>)null : new ThreadPerTaskExecutor(threadFactory).Execute, args)
    {
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param executor          the Executor to use, or {@code null} if the default should be used.
     * @param args              arguments which will passed to each {@link #newChild(Executor, Object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, Action<Action> executor, params object[] args)
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
    protected MultithreadEventExecutorGroup(int nThreads, Action<Action> executor,
        IEventExecutorChooserFactory chooserFactory, params object[] args)
    {
        ObjectUtil.CheckPositive(nThreads, "nThreads");

        if (executor == null)
        {
            executor = new ThreadPerTaskExecutor(NewDefaultThreadFactory()).Execute;
        }

        children = new IEventExecutor[nThreads];

        for (int i = 0; i < nThreads; i++)
        {
            bool success = false;
            try
            {
                children[i] = NewChild(executor, args);
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
                            while (!e.IsTerminated())
                            {
                                e.AwaitTermination(TimeSpan.FromSeconds(int.MaxValue));
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

        chooser = chooserFactory.NewChooser(children);

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

    protected virtual IThreadFactory NewDefaultThreadFactory()
    {
        return new DefaultThreadFactory(GetType());
    }

    public override IEventExecutor Next()
    {
        return chooser.Next();
    }

    public override IEnumerable<IEventExecutor> Iterator()
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
    public int ExecutorCount()
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
    public virtual int ActiveExecutorCount() => chooser is IObservableEventExecutorChooser observable ?
        observable.ActiveExecutorCount() : ExecutorCount();

    /**
     * Returns a list of real-time utilization metrics if the group was configured
     * with a compatible {@link EventExecutorChooserFactory}, otherwise an empty list.
     *
     * @return A list of {@link AutoScalingUtilizationMetric} objects.
     */
    public virtual IReadOnlyList<AutoScalingUtilizationMetric> ExecutorUtilizations() =>
        chooser is IObservableEventExecutorChooser observable ? observable.ExecutorUtilizations() :
            Array.Empty<AutoScalingUtilizationMetric>();

    /**
     * Create a new EventExecutor which will later then accessible via the {@link #next()}  method. This method will be
     * called for each thread that will serve this {@link MultithreadEventExecutorGroup}.
     *
     */
    protected abstract IEventExecutor NewChild(Action<Action> executor, params object[] args);

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        foreach (IEventExecutor l in children)
        {
            l.ShutdownGracefullyAsync(quietPeriod, timeout);
        }

        return Termination;
    }

    public override Task Termination => _terminationSource.Task;

    /// <exception cref="AggregateException">A child stop request threw; every other child was still requested.</exception>
    public override Task StopAsync()
    {
        List<Exception> failures = null;
        foreach (IEventExecutor child in children)
        {
            try { child.StopAsync(); }
            catch (Exception error) { (failures ??= new List<Exception>()).Add(error); }
        }
        // Request failures are distinct from the existing all-child completion
        // signal. Do not strand later children or invent a second termination Task.
        if (failures != null) throw new AggregateException("Executor stop requests failed.", failures);
        return Termination;
    }

    [Obsolete]
    public override void Shutdown()
    {
        foreach (IEventExecutor l in children)
        {
            l.Shutdown();
        }
    }

    public override bool IsShuttingDown()
    {
        foreach (IEventExecutor l in children)
        {
            if (!l.IsShuttingDown())
            {
                return false;
            }
        }

        return true;
    }

    public override bool IsShutdown()
    {
        foreach (IEventExecutor l in children)
        {
            if (!l.IsShutdown())
            {
                return false;
            }
        }

        return true;
    }

    public override bool IsTerminated()
    {
        foreach (IEventExecutor l in children)
        {
            if (!l.IsTerminated())
            {
                return false;
            }
        }

        return true;
    }

    public override bool AwaitTermination(TimeSpan timeout)
    {
        long deadline = unchecked(SystemTimer.NanoTime() + AbstractScheduledEventExecutor.ToNanos(timeout));
        foreach (IEventExecutor l in children)
        {
            bool breakLoop = false;
            for (;;)
            {
                long timeLeft = unchecked(deadline - SystemTimer.NanoTime());
                if (timeLeft <= 0)
                {
                    breakLoop = true;
                    break;
                }

                if (l.AwaitTermination(TimeSpan.FromTicks(timeLeft / 100)))
                {
                    break;
                }
            }

            if (breakLoop)
                break;
        }

        return IsTerminated();
    }

}
