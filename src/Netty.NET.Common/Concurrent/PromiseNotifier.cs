/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * {@link GenericFutureListener} implementation which takes other {@link Promise}s
 * and notifies them on completion.
 *
 * @param <V> the type of value returned by the future
 * @param <F> the type of future
 */
public class PromiseNotifier<V, F> : IGenericFutureListener<F> where F : IFuture<V>
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(PromiseNotifier<V, F>));
    private readonly IPromise<V>[] promises;
    private readonly bool logNotifyFailure;
    /**
     * Create a new instance.
     *
     * @param promises  the {@link Promise}s to notify once this {@link GenericFutureListener} is notified.
     */
    public PromiseNotifier(params IPromise<V>[] promises) : this(true, promises) { }
    /**
     * Create a new instance.
     *
     * @param logNotifyFailure {@code true} if logging should be done in case notification fails.
     * @param promises  the {@link Promise}s to notify once this {@link GenericFutureListener} is notified.
     */
    public PromiseNotifier(bool logNotifyFailure, params IPromise<V>[] promises)
    {
        ObjectUtil.checkNotNull(promises, "promises");
        foreach (IPromise<V> promise in promises) ObjectUtil.checkNotNullWithIAE(promise, "promise");
        this.promises = (IPromise<V>[])promises.Clone();
        this.logNotifyFailure = logNotifyFailure;
    }
    /**
     * Link the {@link Future} and {@link Promise} such that if the {@link Future} completes the {@link Promise}
     * will be notified. Cancellation is propagated both ways such that if the {@link Future} is cancelled
     * the {@link Promise} is cancelled and vise-versa.
     *
     * @param future    the {@link Future} which will be used to listen to for notifying the {@link Promise}.
     * @param promise   the {@link Promise} which will be notified
     * @param <V>       the type of the value.
     * @param <F>       the type of the {@link Future}
     * @return          the passed in {@link Future}
     */
    public static F cascade(F future, IPromise<V> promise) => cascade(true, future, promise);
    /**
     * Link the {@link Future} and {@link Promise} such that if the {@link Future} completes the {@link Promise}
     * will be notified. Cancellation is propagated both ways such that if the {@link Future} is cancelled
     * the {@link Promise} is cancelled and vise-versa.
     *
     * @param logNotifyFailure  {@code true} if logging should be done in case notification fails.
     * @param future            the {@link Future} which will be used to listen to for notifying the {@link Promise}.
     * @param promise           the {@link Promise} which will be notified
     * @param <V>               the type of the value.
     * @param <F>               the type of the {@link Future}
     * @return                  the passed in {@link Future}
     */
    public static F cascade(bool logNotifyFailure, F future, IPromise<V> promise)
    {
        promise.addListener(new CallbackListener(f => { if (f.isCancelled()) future.cancel(false); }));
        var notifier = new PromiseNotifier<V, F>(logNotifyFailure, promise);
        future.addListener(new CallbackListener(f =>
        {
            if (promise.isCancelled() && f.isCancelled())
            {
                // Just return if we propagate a cancel from the promise to the future and both are notified already
                return;
            }
            notifier.operationComplete(future);
        }));
        return future;
    }
    // CLR adaptation: F is reified, so a typed adapter bridges the Future<V> listener signature.
    private sealed class CallbackListener : IFutureListener<V>
    {
        private readonly Action<IFuture<V>> callback;
        internal CallbackListener(Action<IFuture<V>> callback) { this.callback = callback; }
        public void operationComplete(IFuture<V> future) => callback(future);
    }
    public virtual void operationComplete(F future)
    {
        IInternalLogger internalLogger = logNotifyFailure ? logger : null;
        if (future.isSuccess())
        {
            V result = future.get();
            foreach (var promise in promises) PromiseNotificationUtil.trySuccess(promise, result, internalLogger);
        }
        else if (future.isCancelled())
        {
            foreach (var promise in promises) PromiseNotificationUtil.tryCancel(promise, internalLogger);
        }
        else
        {
            Exception cause = future.cause();
            foreach (var promise in promises) PromiseNotificationUtil.tryFailure(promise, cause, internalLogger);
        }
    }
}
