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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

/**
 * Light-weight object pool based on a thread-local stack.
 *
 * @param <T> the type of the pooled object
 */

public static class Recycler
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(Recycler));
    
    public static readonly int DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD = 4 * 1024; // Use 4k instances as default.
    public static readonly int DEFAULT_MAX_CAPACITY_PER_THREAD;
    public static readonly int RATIO;
    public static readonly int DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD;
    public static readonly bool BLOCKING_POOL;
    public static readonly bool BATCH_FAST_TL_ONLY;

    static Recycler()
    {
        // In the future, we might have different maxCapacity for different object types.
        // e.g. io.netty.recycler.maxCapacity.writeTask
        //      io.netty.recycler.maxCapacity.outboundBuffer
        int maxCapacityPerThread = SystemPropertyUtil.getInt("io.netty.recycler.maxCapacityPerThread",
            SystemPropertyUtil.getInt("io.netty.recycler.maxCapacity", DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD));
        if (maxCapacityPerThread < 0)
        {
            maxCapacityPerThread = DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD;
        }

        DEFAULT_MAX_CAPACITY_PER_THREAD = maxCapacityPerThread;
        DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD = SystemPropertyUtil.getInt("io.netty.recycler.chunkSize", 32);

        // By default, we allow one push to a Recycler for each 8th try on handles that were never recycled before.
        // This should help to slowly increase the capacity of the recycler while not be too sensitive to allocation
        // bursts.
        RATIO = Math.Max(0, SystemPropertyUtil.getInt("io.netty.recycler.ratio", 8));

        BLOCKING_POOL = SystemPropertyUtil.getBoolean("io.netty.recycler.blocking", false);
        BATCH_FAST_TL_ONLY = SystemPropertyUtil.getBoolean("io.netty.recycler.batchFastThreadLocalOnly", true);

        if (logger.isDebugEnabled())
        {
            if (DEFAULT_MAX_CAPACITY_PER_THREAD == 0)
            {
                logger.debug("-Dio.netty.recycler.maxCapacityPerThread: disabled");
                logger.debug("-Dio.netty.recycler.ratio: disabled");
                logger.debug("-Dio.netty.recycler.chunkSize: disabled");
                logger.debug("-Dio.netty.recycler.blocking: disabled");
                logger.debug("-Dio.netty.recycler.batchFastThreadLocalOnly: disabled");
            }
            else
            {
                logger.debug("-Dio.netty.recycler.maxCapacityPerThread: {}", DEFAULT_MAX_CAPACITY_PER_THREAD);
                logger.debug("-Dio.netty.recycler.ratio: {}", RATIO);
                logger.debug("-Dio.netty.recycler.chunkSize: {}", DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
                logger.debug("-Dio.netty.recycler.blocking: {}", BLOCKING_POOL);
                logger.debug("-Dio.netty.recycler.batchFastThreadLocalOnly: {}", BATCH_FAST_TL_ONLY);
            }
        }
    }
}

public abstract class Recycler<T>
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance<Recycler<T>>();

    private static readonly RecyclerEnhancedHandle<T> NOOP_HANDLE = new NoopRecyclerEnhancedHandle<T>();


    private readonly int maxCapacityPerThread;
    private readonly int interval;
    private readonly int chunkSize;
    private readonly RecyclerFastThreadLocal<T> threadLocal;

    protected Recycler() : this(Recycler.DEFAULT_MAX_CAPACITY_PER_THREAD)
    {
    }

    protected Recycler(int maxCapacityPerThread)
        : this(maxCapacityPerThread, Recycler.RATIO, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD)
    {
    }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    [Obsolete]
    //@SuppressWarnings("unused") // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor)
        : this(maxCapacityPerThread, Recycler.RATIO, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD)
    {
    }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    [Obsolete]
    //@SuppressWarnings("unused") // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor, int ratio, int maxDelayedQueuesPerThread)
        : this(maxCapacityPerThread, ratio, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD)
    {
    }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    [Obsolete]
    //@SuppressWarnings("unused") // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor,
        int ratio, int maxDelayedQueuesPerThread, int delayedQueueRatio)
        : this(maxCapacityPerThread, ratio, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD)
    {
    }

    protected Recycler(int maxCapacityPerThread, int ratio, int chunkSize)
    {
        interval = Math.Max(0, ratio);
        if (maxCapacityPerThread <= 0)
        {
            this.maxCapacityPerThread = 0;
            this.chunkSize = 0;
        }
        else
        {
            this.maxCapacityPerThread = Math.Max(4, maxCapacityPerThread);
            this.chunkSize = Math.Max(2, Math.Min(chunkSize, this.maxCapacityPerThread >> 1));
        }

        threadLocal = new RecyclerFastThreadLocal<T>(this.maxCapacityPerThread, this.interval, this.chunkSize);
    }

    //@SuppressWarnings("unchecked")
    public T get()
    {
        if (maxCapacityPerThread == 0 || !FastThreadLocalThread.currentThreadHasFastThreadLocal())
        {
            return newObject(NOOP_HANDLE);
        }

        RecyclerLocalPool<T> localPool = threadLocal.get();
        RecyclerDefaultHandle<T> handle = localPool.claim();
        T obj;
        if (handle == null)
        {
            handle = localPool.newHandle();
            if (handle != null)
            {
                obj = newObject(handle);
                handle.set(obj);
            }
            else
            {
                obj = newObject(NOOP_HANDLE);
            }
        }
        else
        {
            obj = handle.get();
        }

        return obj;
    }

    /**
     * @deprecated use {@link Handle#recycle(object)}.
     */
    [Obsolete]
    public bool recycle(T o, IRecyclerHandle<T> handle)
    {
        if (handle == NOOP_HANDLE)
        {
            return false;
        }

        handle.recycle(o);
        return true;
    }

    //@VisibleForTesting
    public int threadLocalSize()
    {
        if (PlatformDependent.isVirtualThread(Thread.CurrentThread) &&
            !FastThreadLocalThread.currentThreadHasFastThreadLocal())
        {
            return 0;
        }

        RecyclerLocalPool<T> localPool = threadLocal.getIfExists();
        return localPool == null ? 0 : localPool._pooledHandles.Count + localPool._batch.Count;
    }

    /**
     * @param handle can NOT be null.
     */
    protected abstract T newObject(IRecyclerHandle<T> handle);
}