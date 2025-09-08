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
namespace Netty.NET.Common.Concurrent;








/**
 * {@link IGenericFutureListener} implementation which takes other {@link IPromise}s
 * and notifies them on completion.
 *
 * @param <V> the type of value returned by the future
 * @param <F> the type of future
 */
public class PromiseNotifier<V, F extends Future<V>> : IGenericFutureListener<> <F> {

    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(PromiseNotifier));
    private readonly IPromise<? super V>[] promises;
    private readonly bool logNotifyFailure;

    /**
     * Create a new instance.
     *
     * @param promises  the {@link IPromise}s to notify once this {@link IGenericFutureListener} is notified.
     */
    @SafeVarargs
    public PromiseNotifier(IPromise<? super V>... promises) {
        this(true, promises);
    }

    /**
     * Create a new instance.
     *
     * @param logNotifyFailure {@code true} if logging should be done in case notification fails.
     * @param promises  the {@link IPromise}s to notify once this {@link IGenericFutureListener} is notified.
     */
    @SafeVarargs
    public PromiseNotifier(bool logNotifyFailure, IPromise<? super V>... promises) {
        checkNotNull(promises, "promises");
        for (IPromise<> <? super V> promise: promises) {
            checkNotNullWithIAE(promise, "promise");
        }
        this.promises = promises.clone();
        this.logNotifyFailure = logNotifyFailure;
    }

    /**
     * Link the {@link Future} and {@link IPromise} such that if the {@link Future} completes the {@link IPromise}
     * will be notified. Cancellation is propagated both ways such that if the {@link Future} is cancelled
     * the {@link IPromise} is cancelled and vise-versa.
     *
     * @param future    the {@link Future} which will be used to listen to for notifying the {@link IPromise}.
     * @param promise   the {@link IPromise} which will be notified
     * @param <V>       the type of the value.
     * @param <F>       the type of the {@link Future}
     * @return          the passed in {@link Future}
     */
    public static <V, F extends Future<V>> F cascade(final F future, final Promise<? super V> promise) {
        return cascade(true, future, promise);
    }

    /**
     * Link the {@link Future} and {@link IPromise} such that if the {@link Future} completes the {@link IPromise}
     * will be notified. Cancellation is propagated both ways such that if the {@link Future} is cancelled
     * the {@link IPromise} is cancelled and vise-versa.
     *
     * @param logNotifyFailure  {@code true} if logging should be done in case notification fails.
     * @param future            the {@link Future} which will be used to listen to for notifying the {@link IPromise}.
     * @param promise           the {@link IPromise} which will be notified
     * @param <V>               the type of the value.
     * @param <F>               the type of the {@link Future}
     * @return                  the passed in {@link Future}
     */
    @SuppressWarnings({"unchecked", "rawtypes"})
    public static <V, F extends Future<V>> F cascade(bool logNotifyFailure, final F future,
                                                     final Promise<? super V> promise) {
        promise.addListener(new IFutureListener<>() {
            @Override
            public void operationComplete(Future f) {
                if (f.isCancelled()) {
                    future.cancel(false);
                }
            }
        });
        future.addListener(new PromiseNotifier(logNotifyFailure, promise) {
            @Override
            public void operationComplete(Future f) {
                if (promise.isCancelled() && f.isCancelled()) {
                    // Just return if we propagate a cancel from the promise to the future and both are notified already
                    return;
                }
                super.operationComplete(future);
            }
        });
        return future;
    }

    @Override
    public void operationComplete(F future) {
        InternalLogger internalLogger = logNotifyFailure ? logger : null;
        if (future.isSuccess()) {
            V result = future.get();
            for (IPromise<> <? super V> p: promises) {
                PromiseNotificationUtil.trySuccess(p, result, internalLogger);
            }
        } else if (future.isCancelled()) {
            for (IPromise<> <? super V> p: promises) {
                PromiseNotificationUtil.tryCancel(p, internalLogger);
            }
        } else {
            Exception cause = future.cause();
            for (IPromise<> <? super V> p: promises) {
                PromiseNotificationUtil.tryFailure(p, cause, internalLogger);
            }
        }
    }
}
