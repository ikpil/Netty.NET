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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;


/**
 * {@link IEventExecutorGroup} which will preserve {@link IRunnable} execution order but makes no guarantees about what
 * {@link IEventExecutor} (and therefore {@link Thread}) will be used to execute the {@link IRunnable}s.
 *
 * <p>The {@link IEventExecutorGroup#next()} for the wrapped {@link IEventExecutorGroup} must <strong>NOT</strong> return
 * executors of type {@link IOrderedEventExecutor}.
 */
[UnstableApi]
public class NonStickyEventExecutorGroup : IEventExecutorGroup 
{
    private readonly IEventExecutorGroup _group;
    private readonly int _maxTaskExecutePerRun;

    /**
     * Creates a new instance. Be aware that the given {@link IEventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link IOrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group) 
        : this(group, 1024)
    {
    }

    /**
     * Creates a new instance. Be aware that the given {@link IEventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link IOrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group, int maxTaskExecutePerRun) {
        _group = verify(group);
        _maxTaskExecutePerRun = ObjectUtil.checkPositive(maxTaskExecutePerRun, "maxTaskExecutePerRun");
    }

    private static IEventExecutorGroup verify(IEventExecutorGroup group) {
        IEnumerable<IEventExecutor> executors = ObjectUtil.checkNotNull(group, "group").iterator();
        while (executors.hasNext()) {
            IEventExecutor executor = executors.next();
            if (executor instanceof OrderedEventExecutor) {
                throw new ArgumentException("IEventExecutorGroup " + group
                        + " contains OrderedEventExecutors: " + executor);
            }
        }
        return group;
    }

    private NonStickyOrderedEventExecutor newExecutor(IEventExecutor executor) {
        return new NonStickyOrderedEventExecutor(executor, maxTaskExecutePerRun);
    }

    @Override
    public bool isShuttingDown() {
        return _group.isShuttingDown();
    }

    @Override
    public Future<?> shutdownGracefully() {
        return _group.shutdownGracefullyAsync();
    }

    @Override
    public Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) {
        return _group.shutdownGracefully(quietPeriod, timeout, unit);
    }

    public override Task terminationTask() {
        return _group.terminationTask();
    }

    //@SuppressWarnings("deprecation")
    @Override
    public void shutdown() {
        _group.shutdown();
    }

    //@SuppressWarnings("deprecation")
    @Override
    public List<IRunnable> shutdownNow() {
        return _group.shutdownNow();
    }

    @Override
    public IEventExecutor next() {
        return newExecutor(_group.next());
    }

    public override IEnumerable<IEventExecutor> iterator() {
        IEnumerable<IEventExecutor> itr = _group.iterator();
        return new IEnumerable<IEventExecutor>() {
            @Override
            public bool hasNext() {
                return itr.hasNext();
            }

            @Override
            public IEventExecutor next() {
                return newExecutor(itr.next());
            }

            @Override
            public void remove() {
                itr.remove();
            }
        };
    }

    @Override
    public Future<?> submit(IRunnable task) {
        return _group.submit(task);
    }

    @Override
    public Task<T> submit(IRunnable task, T result) {
        return _group.submit(task, result);
    }

    @Override
    public Task<T> submit(ICallable<T> task) {
        return _group.submit(task);
    }

    @Override
    public IScheduledTask<?> schedule(IRunnable command, long delay, TimeSpan unit) {
        return _group.schedule(command, delay, unit);
    }

    @Override
    public <V> IScheduledTask<V> schedule(Func<V> callable, long delay, TimeSpan unit) {
        return _group.schedule(callable, delay, unit);
    }

    @Override
    public IScheduledTask<?> scheduleAtFixedRate(IRunnable command, long initialDelay, long period, TimeSpan unit) {
        return _group.scheduleAtFixedRate(command, initialDelay, period, unit);
    }

    @Override
    public IScheduledTask<?> scheduleWithFixedDelay(IRunnable command, long initialDelay, long delay, TimeSpan unit) {
        return _group.scheduleWithFixedDelay(command, initialDelay, delay, unit);
    }

    @Override
    public bool isShutdown() {
        return _group.isShutdown();
    }

    @Override
    public bool isTerminated() {
        return _group.isTerminated();
    }

    @Override
    public bool awaitTermination(TimeSpan timeout) {
        return _group.awaitTermination(timeout, unit);
    }

    @Override
    public <T> List<java.util.concurrent.Future<T>> invokeAll(
            ICollection<? extends ICallable<T>> tasks) {
        return _group.invokeAll(tasks);
    }

    @Override
    public <T> List<java.util.concurrent.Future<T>> invokeAll(
            ICollection<? extends ICallable<T>> tasks, long timeout, TimeSpan unit) {
        return _group.invokeAll(tasks, timeout, unit);
    }

    @Override
    public <T> T invokeAny(ICollection<? extends ICallable<T>> tasks) throws ThreadInterruptedException, ExecutionException {
        return _group.invokeAny(tasks);
    }

    @Override
    public <T> T invokeAny(ICollection<? extends ICallable<T>> tasks, long timeout, TimeSpan unit)
            throws ThreadInterruptedException, ExecutionException, TimeoutException {
        return _group.invokeAny(tasks, timeout, unit);
    }

    @Override
    public void execute(IRunnable command) {
        _group.execute(command);
    }

}
