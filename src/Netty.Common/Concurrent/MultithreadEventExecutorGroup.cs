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
 * Abstract base class for {@link IEventExecutorGroup} implementations that handles their tasks with multiple threads at
 * the same time.
 */
public abstract class MultithreadEventExecutorGroup : AbstractEventExecutorGroup 
{

    private readonly IEventExecutor[] children;
    private readonly ISet<IEventExecutor> readonlyChildren;
    private readonly AtomicInteger terminatedChildren = new AtomicInteger();
    private readonly TaskCompletionSource _terminationFuture = null;
    private readonly IEventExecutorChooser chooser;

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param threadFactory     the IThreadFactory to use, or {@code null} if the default should be used.
     * @param args              arguments which will passed to each {@link #newChild(IExecutor, object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, IThreadFactory threadFactory, params object[] args) 
        : this(nThreads, threadFactory == null ? null : new ThreadPerTaskExecutor(threadFactory), args)
    {
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param executor          the IExecutor to use, or {@code null} if the default should be used.
     * @param args              arguments which will passed to each {@link #newChild(IExecutor, object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, IExecutor executor, params object[] args) 
        : this(nThreads, executor, DefaultEventExecutorChooserFactory.INSTANCE, args)
    {
    }

    /**
     * Create a new instance.
     *
     * @param nThreads          the number of threads that will be used by this instance.
     * @param executor          the IExecutor to use, or {@code null} if the default should be used.
     * @param chooserFactory    the {@link IEventExecutorChooserFactory} to use.
     * @param args              arguments which will passed to each {@link #newChild(IExecutor, object...)} call
     */
    protected MultithreadEventExecutorGroup(int nThreads, IExecutor executor,
                                            IEventExecutorChooserFactory chooserFactory, params object[] args) {
        ObjectUtil.checkPositive(nThreads, "nThreads");

        if (executor == null) {
            executor = new ThreadPerTaskExecutor(newDefaultThreadFactory());
        }

        children = new IEventExecutor[nThreads];

        for (int i = 0; i < nThreads; i ++) {
            bool success = false;
            try {
                children[i] = newChild(executor, args);
                success = true;
            } catch (Exception e) {
                // TODO: Think about if this is a good exception type
                throw new InvalidOperationException("failed to create a child event loop", e);
            } finally {
                if (!success) {
                    for (int j = 0; j < i; j ++) {
                        children[j].shutdownGracefullyAsync();
                    }

                    for (int j = 0; j < i; j ++) {
                        IEventExecutor e = children[j];
                        try {
                            while (!e.isTerminated()) {
                                e.awaitTermination(int.MaxValue, TimeSpan.SECONDS);
                            }
                        } catch (ThreadInterruptedException interrupted) {
                            // Let the caller handle the interruption.
                            Thread.CurrentThread.interrupt();
                            break;
                        }
                    }
                }
            }
        }

        chooser = chooserFactory.newChooser(children);

        final IFutureListener<object> terminationListener = new IFutureListener<object>() {
            @Override
            public void operationComplete(Future<object> future) {
                if (terminatedChildren.incrementAndGet() == children.length) {
                    terminationFuture.setSuccess(null);
                }
            }
        };

        for (IEventExecutor e: children) {
            e.terminationFuture().addListener(terminationListener);
        }

        ISet<IEventExecutor> childrenSet = new LinkedHashSet<IEventExecutor>(children.length);
        Collections.addAll(childrenSet, children);
        readonlyChildren = Collections.unmodifiableSet(childrenSet);
    }

    protected IThreadFactory newDefaultThreadFactory() {
        return new DefaultThreadFactory(GetType());
    }

    @Override
    public IEventExecutor next() {
        return chooser.next();
    }

    @Override
    public Iterator<IEventExecutor> iterator() {
        return readonlyChildren.iterator();
    }

    /**
     * Return the number of {@link IEventExecutor} this implementation uses. This number is the maps
     * 1:1 to the threads it use.
     */
    public final int executorCount() {
        return children.length;
    }

    /**
     * Create a new IEventExecutor which will later then accessible via the {@link #next()}  method. This method will be
     * called for each thread that will serve this {@link MultithreadEventExecutorGroup}.
     *
     */
    protected abstract IEventExecutor newChild(IExecutor executor, params object[] args);

    @Override
    public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit) {
        for (IEventExecutor l: children) {
            l.shutdownGracefully(quietPeriod, timeout, unit);
        }
        return terminationFuture();
    }

    @Override
    public Future<?> terminationFuture() {
        return terminationFuture;
    }

    @Override
    [Obsolete]
    public void shutdown() {
        for (IEventExecutor l: children) {
            l.shutdown();
        }
    }

    @Override
    public bool isShuttingDown() {
        for (IEventExecutor l: children) {
            if (!l.isShuttingDown()) {
                return false;
            }
        }
        return true;
    }

    @Override
    public bool isShutdown() {
        for (IEventExecutor l: children) {
            if (!l.isShutdown()) {
                return false;
            }
        }
        return true;
    }

    @Override
    public bool isTerminated() {
        for (IEventExecutor l: children) {
            if (!l.isTerminated()) {
                return false;
            }
        }
        return true;
    }

    @Override
    public bool awaitTermination(TimeSpan timeout)
            throws ThreadInterruptedException {
        long deadline = PreciseTimer.nanoTime() + unit.toNanos(timeout);
        loop: for (IEventExecutor l: children) {
            for (;;) {
                long timeLeft = deadline - PreciseTimer.nanoTime();
                if (timeLeft <= 0) {
                    break loop;
                }
                if (l.awaitTermination(timeLeft, TimeSpan.NANOSECONDS)) {
                    break;
                }
            }
        }
        return isTerminated();
    }
}
