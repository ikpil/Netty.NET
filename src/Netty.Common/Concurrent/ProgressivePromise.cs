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
 * Special {@link ProgressiveFuture} which is writable.
 */
public interface ProgressivePromise<V> : Promise<V>, ProgressiveFuture<V>
{
    /**
     * Sets the current progress of the operation and notifies the listeners that implement
     * {@link IGenericProgressiveFutureListener}.
     */
    ProgressivePromise<V> setProgress(long progress, long total);

    /**
     * Tries to set the current progress of the operation and notifies the listeners that implement
     * {@link IGenericProgressiveFutureListener}.  If the operation is already complete or the progress is out of range,
     * this method does nothing but returning {@code false}.
     */
    bool tryProgress(long progress, long total);

    ProgressivePromise<V> setSuccess(V result);

    ProgressivePromise<V> setFailure(Exception cause);

    ProgressivePromise<V> addListener(IGenericFutureListener<IFuture<V>> listener);

    ProgressivePromise<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    ProgressivePromise<V> removeListener(IGenericFutureListener<IFuture<V>> listener);

    ProgressivePromise<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners);

    ProgressivePromise<V> await();
    ProgressivePromise<V> awaitUninterruptibly();

    ProgressivePromise<V> sync();

    ProgressivePromise<V> syncUninterruptibly();
}