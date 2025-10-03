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
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Concurrent;


/**
 * {@link IGenericFutureListener} implementation which takes other {@link IPromise}s
 * and notifies them on completion.
 *
 * @param <V> the type of value returned by the future
 * @param <F> the type of future
 */
public class PromiseNotifier<V, F> : IGenericFutureListener<F> where F : TaskCompletionSource<V>
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(PromiseNotifier<V, F>));
    private readonly TaskCompletionSource<V>[] promises;
    private readonly bool logNotifyFailure;

    /**
     * Create a new instance.
     *
     * @param promises  the {@link IPromise}s to notify once this {@link IGenericFutureListener} is notified.
     */
    public PromiseNotifier(params TaskCompletionSource<V>[] promises) 
    : this(true, promises)
    {
    }

    /**
     * Create a new instance.
     *
     * @param logNotifyFailure {@code true} if logging should be done in case notification fails.
     * @param promises  the {@link IPromise}s to notify once this {@link IGenericFutureListener} is notified.
     */
    public PromiseNotifier(bool logNotifyFailure, params TaskCompletionSource<V>[] promises) 
    {
        checkNotNull(promises, "promises");
        foreach (IPromise<V> promise in promises) {
            checkNotNullWithIAE(promise, "promise");
        }

        this.promises = promises.ToArray();
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
    public static F cascade(F future, TaskCompletionSource<V> promise) {
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
    //@SuppressWarnings({"unchecked", "rawtypes"})
    public static F cascade(bool logNotifyFailure, F future, TaskCompletionSource<V> promise)
    {
        throw new NotImplementedException();
        // promise.Task.ContinueWith(t =>
        // {
        //     if (t.IsCanceled) {
        //         future.cancel(false);
        //     
        // });
        //
        // future.addListener(new PromiseNotifier(logNotifyFailure, promise) {
        //     @Override
        //     public void operationComplete(Future f) {
        //         if (promise.isCancelled() && f.isCancelled()) {
        //             // Just return if we propagate a cancel from the promise to the future and both are notified already
        //             return;
        //         }
        //         super.operationComplete(future);
        //     }
        // });
        // return future;
    }

    public void operationComplete(F future) {
        IInternalLogger internalLogger = logNotifyFailure ? logger : null;
        if (future.Task.IsCompletedSuccessfully)
        {
            V result = future.Task.Result;
            foreach (var p in promises) {
                PromiseNotificationUtil.trySuccess(p, result, internalLogger);
            }
        } else if (future.Task.IsCanceled) {
            foreach (var p in promises) {
                PromiseNotificationUtil.tryCancel(p, internalLogger);
            }
        } else
        {
            Exception cause = future.Task.Exception;
            foreach (var p in promises) {
                PromiseNotificationUtil.tryFailure(p, cause, internalLogger);
            }
        }
    }
}
