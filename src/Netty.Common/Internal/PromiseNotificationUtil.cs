/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Internal;

/**
 * Internal utilities to notify {@link IPromise}s.
 */
public static class PromiseNotificationUtil
{
    /**
     * Try to cancel the {@link IPromise} and log if {@code logger} is not {@code null} in case this fails.
     */
    public static void tryCancel<T>(TaskCompletionSource<T> p, IInternalLogger logger)
    {
        if (!p.TrySetCanceled() && logger != null)
        {
            Exception err = p.Task.Exception;
            if (err == null)
            {
                logger.warn($"Failed to cancel promise because it has succeeded already: {p}");
            }
            else
            {
                logger.warn($"Failed to cancel promise because it has failed already: {p}, unnotified cause:", err);
            }
        }
    }

    /**
     * Try to mark the {@link IPromise} as success and log if {@code logger} is not {@code null} in case this fails.
     */
    public static void trySuccess<V>(TaskCompletionSource<V> p, V result, IInternalLogger logger)
    {
        if (!p.TrySetResult(result) && logger != null)
        {
            Exception err = p.Task.Exception;
            if (err == null)
            {
                logger.warn($"Failed to mark a promise as success because it has succeeded already: {p}");
            }
            else
            {
                logger.warn($"Failed to mark a promise as success because it has failed already: {p}, unnotified cause:", err);
            }
        }
    }

    /**
     * Try to mark the {@link IPromise} as failure and log if {@code logger} is not {@code null} in case this fails.
     */
    public static void tryFailure<T>(TaskCompletionSource<T> p, Exception cause, IInternalLogger logger)
    {
        if (!p.TrySetException(cause) && logger != null)
        {
            Exception err = p.Task.Exception;
            if (err == null)
            {
                logger.warn($"Failed to mark a promise as failure because it has succeeded already: {p}", cause);
            }
            else if (logger.isWarnEnabled())
            {
                logger.warn($"Failed to mark a promise as failure because it has failed already: {p}, unnotified cause:", cause);
            }
        }
    }
}