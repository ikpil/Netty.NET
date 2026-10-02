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
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract {@link Future} implementation which does not allow for cancellation.
 *
 * @param <V>
 */
public abstract class AbstractFuture<V> : IFuture<V>
{
    // CLR adaptation: Java listener wildcards accept listeners bound to Promise/ProgressiveFuture.
    // Keep one adapter per listener identity so removal, duplicate registration and progress retain their contracts.
    private readonly ConditionalWeakTable<object, IGenericFutureListener<IFuture<V>>> _typedListeners = new();
    private sealed class TypedListener<F> : IGenericFutureListener<IFuture<V>> where F : IFuture<V>
    {
        private readonly IGenericFutureListener<F> listener;
        internal TypedListener(IGenericFutureListener<F> listener) { this.listener = listener; }
        public void operationComplete(IFuture<V> future) => listener.operationComplete((F)future);
    }
    private sealed class TypedProgressiveListener<F> : IGenericProgressiveFutureListener<IFuture<V>> where F : IFuture<V>
    {
        private readonly IGenericProgressiveFutureListener<F> listener;
        internal TypedProgressiveListener(IGenericProgressiveFutureListener<F> listener) { this.listener = listener; }
        public void operationComplete(IFuture<V> future) => listener.operationComplete((F)future);
        public void operationProgressed(IFuture<V> future, long progress, long total) => listener.operationProgressed((F)future, progress, total);
    }
    private IGenericFutureListener<IFuture<V>> adaptListener<F>(IGenericFutureListener<F> listener) where F : IFuture<V>
    {
        ObjectUtil.checkNotNull(listener, "listener");
        return _typedListeners.GetValue(listener, _ => listener is IGenericProgressiveFutureListener<F> progressive ?
            new TypedProgressiveListener<F>(progressive) : new TypedListener<F>(listener));
    }
    public virtual IFuture<V> addListener<F>(IGenericFutureListener<F> listener) where F : IFuture<V> => addListener(adaptListener(listener));
    public virtual IFuture<V> removeListener<F>(IGenericFutureListener<F> listener) where F : IFuture<V> => removeListener(adaptListener(listener));
    public virtual IFuture<V> addListeners<F>(params IGenericFutureListener<F>[] listeners) where F : IFuture<V>
    {
        ObjectUtil.checkNotNull(listeners, "listeners");
        var adapted = new IGenericFutureListener<IFuture<V>>[listeners.Length];
        for (int i = 0; i < listeners.Length && listeners[i] != null; i++) adapted[i] = adaptListener(listeners[i]);
        return addListeners(adapted);
    }
    public virtual IFuture<V> removeListeners<F>(params IGenericFutureListener<F>[] listeners) where F : IFuture<V>
    {
        ObjectUtil.checkNotNull(listeners, "listeners");
        var adapted = new IGenericFutureListener<IFuture<V>>[listeners.Length];
        for (int i = 0; i < listeners.Length && listeners[i] != null; i++) adapted[i] = adaptListener(listeners[i]);
        return removeListeners(adapted);
    }
    public abstract Task<V> Task { get; }
    public abstract Exception cause();
    public abstract bool isSuccess();
    public abstract bool isCancellable();
    public abstract bool isCancelled();
    public abstract bool isDone();
    public abstract bool cancel(bool mayInterruptIfRunning);
    public abstract V getNow();
    public abstract IFuture<V> addListener(IGenericFutureListener<IFuture<V>> listener);
    IFuture IFuture.addListener(IGenericFutureListener<IFuture> listener) => addListener(listener);
    public abstract IFuture<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners);
    public abstract IFuture<V> removeListener(IGenericFutureListener<IFuture<V>> listener);
    public abstract IFuture<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners);
    public abstract IFuture<V> await();
    public abstract IFuture<V> awaitUninterruptibly();
    public abstract bool await(TimeSpan timeout);
    public abstract bool await(long timeoutMillis);
    public abstract bool awaitUninterruptibly(TimeSpan timeout);
    public abstract bool awaitUninterruptibly(long timeoutMillis);
    public abstract IFuture<V> sync();
    public abstract IFuture<V> syncUninterruptibly();

    public virtual V get()
    {
        this.await();
        return resultOrThrow();
    }
    public virtual V get(TimeSpan timeout)
    {
        if (!this.await(timeout)) throw new TimeoutException("timeout after " + timeout);
        return resultOrThrow();
    }
    public virtual V get(long timeoutMillis)
    {
        if (!this.await(timeoutMillis)) throw new TimeoutException("timeout after " + timeoutMillis + " milliseconds");
        return resultOrThrow();
    }
    private V resultOrThrow()
    {
        Exception error = cause();
        if (error == null) return getNow();
        if (error is OperationCanceledException) throw error;
        // CLR adaptation: Java ExecutionException is represented by AggregateException;
        // cause() and sync() preserve the original exception object.
        throw new AggregateException(error);
    }
}
