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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Internal;

/**
 * Allows a way to register some {@link IRunnable} that will executed once there are no references to an {@link object}
 * anymore.
 */
public static class ObjectCleaner
{
    private static readonly int REFERENCE_QUEUE_POLL_TIMEOUT_MS =
        Math.Max(500, SystemPropertyUtil.getInt("io.netty.util.internal.ObjectCleaner.refQueuePollTimeout", 10000));

    // Package-private for testing
    static readonly string CLEANER_THREAD_NAME = nameof(ObjectCleaner) + "Thread";

    // This will hold a reference to the AutomaticCleanerReference which will be removed once we called cleanup()
    private static readonly ConcurrentHashSet<AutomaticCleanerReference> LIVE_SET = new ConcurrentHashSet<AutomaticCleanerReference>();
    private static readonly ReferenceQueue<object> REFERENCE_QUEUE = new ReferenceQueue<>();
    private static readonly AtomicBoolean CLEANER_RUNNING = new AtomicBoolean(false);

    private static readonly IRunnable CLEANER_TASK = AnonymousRunnable.Create(() =>
    {
        bool interrupted = false;
        for (;;)
        {
            // Keep on processing as long as the LIVE_SET is not empty and once it becomes empty
            // See if we can let this thread complete.
            while (!LIVE_SET.IsEmpty())
            {
                AutomaticCleanerReference reference = null;
                try
                {
                    reference = (AutomaticCleanerReference)REFERENCE_QUEUE.remove(REFERENCE_QUEUE_POLL_TIMEOUT_MS);
                }
                catch (ThreadInterruptedException ex)
                {
                    // Just consume and move on
                    interrupted = true;
                    continue;
                }

                if (reference != null)
                {
                    try
                    {
                        reference.cleanup();
                    }
                    catch (Exception ignored)
                    {
                        // ignore exceptions, and don't log in case the logger throws an exception, blocks, or has
                        // other unexpected side effects.
                    }

                    LIVE_SET.Remove(reference);
                }
            }

            CLEANER_RUNNING.set(false);

            // Its important to first access the LIVE_SET and then CLEANER_RUNNING to ensure correct
            // behavior in multi-threaded environments.
            if (LIVE_SET.IsEmpty() || !CLEANER_RUNNING.compareAndSet(false, true))
            {
                // There was nothing added after we set STARTED to false or some other cleanup Thread
                // was started already so its safe to let this Thread complete now.
                break;
            }
        }

        if (interrupted)
        {
            // As we caught the ThreadInterruptedException above we should mark the Thread as interrupted.
            Thread.CurrentThread.Interrupt();
        }
    });

    /**
     * Register the given {@link object} for which the {@link IRunnable} will be executed once there are no references
     * to the object anymore.
     *
     * This should only be used if there are no other ways to execute some cleanup once the object is not reachable
     * anymore because it is not a cheap way to handle the cleanup.
     */
    public static void register(object obj, IRunnable cleanupTask)
    {
        AutomaticCleanerReference reference = new AutomaticCleanerReference(obj, ObjectUtil.checkNotNull(cleanupTask, "cleanupTask"));
        // Its important to add the reference to the LIVE_SET before we access CLEANER_RUNNING to ensure correct
        // behavior in multi-threaded environments.
        LIVE_SET.Add(reference);

        // Check if there is already a cleaner running.
        if (CLEANER_RUNNING.compareAndSet(false, true))
        {
            Thread cleanupThread = new FastThreadLocalThread(CLEANER_TASK);
            cleanupThread.Priority = ThreadPriority.MIN_PRIORITY;
            cleanupThread.Name = CLEANER_THREAD_NAME;

            // Mark this as a daemon thread to ensure that we the JVM can exit if this is the only thread that is
            // running.
            cleanupThread.IsBackground = true;
            cleanupThread.Start();
        }
    }

    public static int getLiveSetCount()
    {
        return LIVE_SET.Count;
    }
}