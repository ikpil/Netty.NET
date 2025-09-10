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

namespace Netty.NET.Common.Concurrent;


/**
 * Special {@link Future} which is writable.
 */
public interface IPromise<V> : IFuture
{
    /**
     * Marks this future as a success and notifies all
     * listeners.
     *
     * If it is success or failed already it will throw an {@link InvalidOperationException}.
     */
    IPromise<V> setSuccess(V result);

    /**
     * Marks this future as a success and notifies all
     * listeners.
     *
     * @return {@code true} if and only if successfully marked this future as
     *         a success. Otherwise {@code false} because this future is
     *         already marked as either a success or a failure.
     */
    bool trySuccess(V result);

    /**
     * Marks this future as a failure and notifies all
     * listeners.
     *
     * If it is success or failed already it will throw an {@link InvalidOperationException}.
     */
    IPromise<V> setFailure(Exception cause);

    /**
     * Marks this future as a failure and notifies all
     * listeners.
     *
     * @return {@code true} if and only if successfully marked this future as
     *         a failure. Otherwise {@code false} because this future is
     *         already marked as either a success or a failure.
     */
    bool tryFailure(Exception cause);

    /**
     * Make this future impossible to cancel.
     *
     * @return {@code true} if and only if successfully marked this future as uncancellable or it is already done
     *         without being cancelled.  {@code false} if this future has been cancelled already.
     */
    bool setUncancellable();

    IPromise<V> addListener(IGenericFutureListener<IFuture<V>> listener);

    IPromise<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    IPromise<V> removeListener(IGenericFutureListener<IFuture<V>> listener);

    IPromise<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    IPromise<V> await();

    IPromise<V> awaitUninterruptibly();

    IPromise<V> sync();

    IPromise<V> syncUninterruptibly();
}