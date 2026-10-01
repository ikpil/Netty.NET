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
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;



/**
 * A skeletal {@link Future} implementation which represents a {@link Future} which has been completed already.
 */
public abstract class CompleteFuture<V>  : AbstractFuture<V> {

    private readonly IEventExecutor _executor;

    /**
     * Creates a new instance.
     *
     * @param executor the {@link EventExecutor} associated with this future
     */
    protected CompleteFuture(IEventExecutor executor) {
        _executor = executor;
    }

    /**
     * Return the {@link EventExecutor} which is used by this {@link CompleteFuture}.
     */
    protected virtual IEventExecutor executor() {
        return _executor;
    }

    public override IFuture<V> addListener(IGenericFutureListener<IFuture<V>> listener) {
        DefaultPromise<V>.notifyListener(executor(), this, ObjectUtil.checkNotNull(listener, "listener"));
        return this;
    }

    public override IFuture<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners) {
        foreach (IGenericFutureListener<IFuture<V>> l in ObjectUtil.checkNotNull(listeners, "listeners")) {

            if (l == null) {
                break;
            }
            DefaultPromise<V>.notifyListener(executor(), this, l);
        }
        return this;
    }

    public override IFuture<V> removeListener(IGenericFutureListener<IFuture<V>> listener) {
        // NOOP
        return this;
    }

    public override IFuture<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners) {
        // NOOP
        return this;
    }

    public override IFuture<V> await() {
        Thread.Sleep(0);
        return this;
    }

    public override bool await(TimeSpan timeout) {
        Thread.Sleep(0);
        return true;
    }

    public override IFuture<V> sync() {
        return this;
    }

    public override IFuture<V> syncUninterruptibly() {
        return this;
    }

    public override bool await(long timeoutMillis) {
        Thread.Sleep(0);
        return true;
    }

    public override IFuture<V> awaitUninterruptibly() {
        return this;
    }

    public override bool awaitUninterruptibly(TimeSpan timeout) {
        return true;
    }

    public override bool awaitUninterruptibly(long timeoutMillis) {
        return true;
    }

    public override bool isDone() {
        return true;
    }

    public override bool isCancellable() {
        return false;
    }

    public override bool isCancelled() {
        return false;
    }

    /**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
    public override bool cancel(bool mayInterruptIfRunning) {
        return false;
    }
}
