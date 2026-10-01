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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;


public class DefaultProgressivePromise<V> : DefaultPromise<V>, IProgressivePromise<V> {
    // CLR adaptation: C# interfaces require explicit implementations for covariant fluent returns.
    IProgressiveFuture<V> IProgressiveFuture<V>.addListener(IGenericFutureListener<IFuture<V>> listener) => addListener(listener);
    IProgressiveFuture<V> IProgressiveFuture<V>.addListeners(params IGenericFutureListener<IFuture<V>>[] listeners) => addListeners(listeners);
    IProgressiveFuture<V> IProgressiveFuture<V>.removeListener(IGenericFutureListener<IFuture<V>> listener) => removeListener(listener);
    IProgressiveFuture<V> IProgressiveFuture<V>.removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners) => removeListeners(listeners);
    IProgressiveFuture<V> IProgressiveFuture<V>.sync() => sync();
    IProgressiveFuture<V> IProgressiveFuture<V>.syncUninterruptibly() => syncUninterruptibly();
    IProgressiveFuture<V> IProgressiveFuture<V>.await() => this.await();
    IProgressiveFuture<V> IProgressiveFuture<V>.awaitUninterruptibly() => awaitUninterruptibly();

    /**
     * Creates a new instance.
     *
     * It is preferable to use {@link EventExecutor#newProgressivePromise()} to create a new progressive promise
     *
     * @param executor
     *        the {@link EventExecutor} which is used to notify the promise when it progresses or it is complete
     */
    public DefaultProgressivePromise(IEventExecutor executor) : base(executor) {
        
    }

    protected DefaultProgressivePromise() : base() { /* only for subclasses */ }

    public virtual IProgressivePromise<V> setProgress(long progress, long total) {
        if (total < 0) {
            // total unknown
            total = -1; // normalize
            ObjectUtil.checkPositiveOrZero(progress, "progress");
        } else if (progress < 0 || progress > total) {
            throw new ArgumentException(
                    "progress: " + progress + " (expected: 0 <= progress <= total (" + total + "))");
        }

        if (isDone()) {
            throw new InvalidOperationException("complete already");
        }

        notifyProgressiveListeners(progress, total);
        return this;
    }

    public virtual bool tryProgress(long progress, long total) {
        if (total < 0) {
            total = -1;
            if (progress < 0 || isDone()) {
                return false;
            }
        } else if (progress < 0 || progress > total || isDone()) {
            return false;
        }

        notifyProgressiveListeners(progress, total);
        return true;
    }

    public override IProgressivePromise<V> addListener(IGenericFutureListener<IFuture<V>> listener) {
        base.addListener(listener);
        return this;
    }

    public override IProgressivePromise<V> addListeners(params IGenericFutureListener<IFuture<V>>[] listeners) {
        base.addListeners(listeners);
        return this;
    }

    public override IProgressivePromise<V> removeListener(IGenericFutureListener<IFuture<V>> listener) {
        base.removeListener(listener);
        return this;
    }

    public override IProgressivePromise<V> removeListeners(params IGenericFutureListener<IFuture<V>>[] listeners) {
        base.removeListeners(listeners);
        return this;
    }

    public override IProgressivePromise<V> sync() {
        base.sync();
        return this;
    }

    public override IProgressivePromise<V> syncUninterruptibly() {
        base.syncUninterruptibly();
        return this;
    }

    public override IProgressivePromise<V> await() {
        base.await();
        return this;
    }

    public override IProgressivePromise<V> awaitUninterruptibly() {
        base.awaitUninterruptibly();
        return this;
    }

    public override IProgressivePromise<V> setSuccess(V result) {
        base.setSuccess(result);
        return this;
    }

    public override IProgressivePromise<V> setFailure(Exception cause) {
        base.setFailure(cause);
        return this;
    }
}
