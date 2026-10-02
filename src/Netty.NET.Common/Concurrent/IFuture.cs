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
 * The result of an asynchronous operation.
 */
// CLR adaptation: non-generic observers retain failure identity and status;
// consumers await the read-only Task without access to the completion source.
public interface IFuture
{
    /**
     * Returns the cause of the failed I/O operation if the I/O operation has
     * failed.
     *
     * @return the cause of the failure.
     *         {@code null} if succeeded or this future is not
     *         completed yet.
     */
    Exception cause();
    /**
     * Returns {@code true} if and only if the I/O operation was completed
     * successfully.
     */
    bool isSuccess();
    /**
     * returns {@code true} if and only if the operation can be cancelled via {@link #cancel(boolean)}.
     */
    bool isCancellable();
    bool isCancelled();
    bool isDone();
    /**
     * {@inheritDoc}
     *
     * If the cancellation was successful it will fail the future with a {@link CancellationException}.
     */
    bool cancel(bool mayInterruptIfRunning);
    // CLR adaptation: Java Future<?> observers use this non-generic listener bridge.
    IFuture addListener(IGenericFutureListener<IFuture> listener);
}
public interface IFuture<V> : IFuture {
    Task<V> Task { get; }
    V get();
    V get(TimeSpan timeout);
    V get(long timeoutMillis);







    /**
     * Adds the specified listener to this future.  The
     * specified listener is notified when this future is
     * {@linkplain #isDone() done}.  If this future is already
     * completed, the specified listener is notified immediately.
     */
    IFuture<V> addListener(IGenericFutureListener<IFuture<V>> listener);

    /**
     * Adds the specified listeners to this future.  The
     * specified listeners are notified when this future is
     * {@linkplain #isDone() done}.  If this future is already
     * completed, the specified listeners are notified immediately.
     */
    IFuture<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    /**
     * Removes the first occurrence of the specified listener from this future.
     * The specified listener is no longer notified when this
     * future is {@linkplain #isDone() done}.  If the specified
     * listener is not associated with this future, this method
     * does nothing and returns silently.
     */
    IFuture<V> removeListener(IGenericFutureListener<IFuture<V>> listener);

    /**
     * Removes the first occurrence for each of the listeners from this future.
     * The specified listeners are no longer notified when this
     * future is {@linkplain #isDone() done}.  If the specified
     * listeners are not associated with this future, this method
     * does nothing and returns silently.
     */
    IFuture<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    /**
     * Waits for this future until it is done, and rethrows the cause of the failure if this future
     * failed.
     */
    IFuture<V> sync();

    /**
     * Waits for this future until it is done, and rethrows the cause of the failure if this future
     * failed.
     */
    IFuture<V> syncUninterruptibly();

    /**
     * Waits for this future to be completed.
     *
     * @throws InterruptedException
     *         if the current thread was interrupted
     */
    IFuture<V> await();

    /**
     * Waits for this future to be completed without
     * interruption.  This method catches an {@link InterruptedException} and
     * discards it silently.
     */
    IFuture<V> awaitUninterruptibly();

    /**
     * Waits for this future to be completed within the
     * specified time limit.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     *
     * @throws InterruptedException
     *         if the current thread was interrupted
     */
    bool await(TimeSpan timeout);

    /**
     * Waits for this future to be completed within the
     * specified time limit.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     *
     * @throws InterruptedException
     *         if the current thread was interrupted
     */
    bool await(long timeoutMillis);

    /**
     * Waits for this future to be completed within the
     * specified time limit without interruption.  This method catches an
     * {@link InterruptedException} and discards it silently.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     */
    bool awaitUninterruptibly(TimeSpan timeout);

    /**
     * Waits for this future to be completed within the
     * specified time limit without interruption.  This method catches an
     * {@link InterruptedException} and discards it silently.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     */
    bool awaitUninterruptibly(long timeoutMillis);

    /**
     * Return the result without blocking. If the future is not done yet this will return {@code null}.
     * <p>
     * As it is possible that a {@code null} value is used to mark the future as successful you also need to check
     * if the future is really done with {@link #isDone()} and not rely on the returned {@code null} value.
     */
    V getNow();



    // CLR adapters preserve Java listener wildcard bindings and fluent returns.
    IFuture<V> addListener<F>(IGenericFutureListener<F> listener) where F : IFuture<V>;
    IFuture<V> addListeners<F>(params IGenericFutureListener<F>[] listeners) where F : IFuture<V>;
    IFuture<V> removeListener<F>(IGenericFutureListener<F> listener) where F : IFuture<V>;
    IFuture<V> removeListeners<F>(params IGenericFutureListener<F>[] listeners) where F : IFuture<V>;
}
