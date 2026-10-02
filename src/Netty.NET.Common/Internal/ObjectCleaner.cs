/*
 * Copyright 2017 The Netty Project
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

namespace Netty.NET.Common.Internal;

/**
 * Allows a way to register some {@link Runnable} that will executed once there are no references to an {@link Object}
 * anymore.
 *
 * @deprecated The object cleaner is deprecated for removal.
 */
[Obsolete("Prefer IDisposable/SafeHandle ownership. Use registration only as a GC fallback.")]
public static class ObjectCleaner
{
    // CLR runtime mechanisms replace the Java LIVE_SET/ReferenceQueue/polling
    // worker. Conditional values do not globally root callbacks or their keys.
    // CollectedObjectWatch finalizers only enqueue work; user code runs on a
    // background thread-pool worker, with no registrar ExecutionContext flow.
    private static int _pending;

    /// <summary>Number of registrations waiting for collection or callback completion.</summary>
    public static int PendingCount => Volatile.Read(ref _pending);

    /**
     * Register the given {@link Object} for which the {@link Runnable} will be executed once there are no references
     * to the object anymore.
     *
     * This should only be used if there are no other ways to execute some cleanup once the Object is not reachable
     * anymore because it is not a cheap way to handle the cleanup.
     */
    /// <summary>Registers an at-most-once Action after the target becomes unreachable.
    /// Callbacks have no ordering or executor affinity and may run concurrently.
    /// GC timing and process shutdown do not provide a deterministic cleanup deadline.</summary>
    public static void Register(object target, Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(cleanup);
        Interlocked.Increment(ref _pending);
        try
        {
            CollectedObjectWatch.register(target, () => QueueCleanup(cleanup));
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            throw;
        }
        GC.KeepAlive(target);
    }

    private static void QueueCleanup(Action cleanup)
    {
        try
        {
            bool queued = ThreadPool.UnsafeQueueUserWorkItem(static action =>
            {
                try { action(); }
                catch (Exception)
                {
                    // ignore exceptions, and don't log in case the logger throws an exception, blocks, or has
                    // other unexpected side effects.
                }
                finally { Interlocked.Decrement(ref _pending); }
            }, cleanup, preferLocal: false);
            if (!queued) throw new InvalidOperationException("The CLR thread pool rejected GC cleanup work.");
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            throw;
        }
    }
}
