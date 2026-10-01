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

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract {@link Future} implementation which does not allow for cancellation.
 *
 * @param <V>
 */
public abstract class AbstractFuture<V> : IFuture<V>
{
    public abstract Task<V> Task { get; }
    public abstract Exception cause();
    public abstract bool isSuccess();
    public abstract bool isCancellable();
    public abstract bool isCancelled();
    public abstract bool isDone();
    public abstract bool cancel(bool mayInterruptIfRunning);
    public abstract V getNow();
    public abstract IFuture<V> addListener(IGenericFutureListener<IFuture<V>> listener);
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
