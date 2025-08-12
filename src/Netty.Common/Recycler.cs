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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

//@SuppressWarnings("ClassNameSameAsAncestorName") // Can't change this due to compatibility.
public interface IRecycleHandle<T> : IObjectPoolHandle<T>
{
    
}


//@UnstableApi
public abstract class RecycleEnhancedHandle<T> : IRecycleHandle<T> 
{

    public abstract void unguardedRecycle(object obj);
    public abstract void recycle(T self);
}



public class NoopRecyclerEnhancedHandle<T> : RecycleEnhancedHandle<T>
{
    public override void unguardedRecycle(object obj) 
    {
        // NOOP
    }

    public override void recycle(T self)
    {
        // NOOP
    }

    public override string ToString() 
    {
        return "NOOP_HANDLE";
    }
}

public interface IExitCondition 
{
    bool keepRunning();
}

public interface IWaitStrategy 
{
    int idle(int var1);
}



public class LocalPool<T> : IConsumer<DefaultHandle<T>> 
{
    private readonly int ratioInterval;
    private readonly int chunkSize;
    private readonly ArrayDeque<DefaultHandle<T>> batch;
    private volatile Thread owner;
    private volatile MessagePassingQueue<DefaultHandle<T>> pooledHandles;
    private int ratioCounter;

    @SuppressWarnings("unchecked")
    public LocalPool(int maxCapacity, int ratioInterval, int chunkSize) {
        this.ratioInterval = ratioInterval;
        this.chunkSize = chunkSize;
        batch = new ArrayDeque<DefaultHandle<T>>(chunkSize);
        Thread currentThread = Thread.currentThread();
        owner = !BATCH_FAST_TL_ONLY || FastThreadLocalThread.currentThreadHasFastThreadLocal()
                ? currentThread : null;
        if (BLOCKING_POOL) {
            pooledHandles = new Common.BlockingMessageQueue<DefaultHandle<T>>(maxCapacity);
        } else {
            pooledHandles = (MessagePassingQueue<DefaultHandle<T>>) newMpscQueue(chunkSize, maxCapacity);
        }
        ratioCounter = ratioInterval; // Start at interval so the first one will be recycled.
    }

    DefaultHandle<T> claim() {
        MessagePassingQueue<DefaultHandle<T>> handles = pooledHandles;
        if (handles == null) {
            return null;
        }
        if (batch.isEmpty()) {
            handles.drain(this, chunkSize);
        }
        DefaultHandle<T> handle = batch.pollLast();
        if (null != handle) {
            handle.toClaimed();
        }
        return handle;
    }

    public void release(DefaultHandle<T> handle, bool guarded) {
        if (guarded) {
            handle.toAvailable();
        } else {
            handle.unguardedToAvailable();
        }
        Thread owner = this.owner;
        if (owner != null && Thread.currentThread() == owner && batch.size() < chunkSize) {
            accept(handle);
        } else if (owner != null && isTerminated(owner)) {
            this.owner = null;
            pooledHandles = null;
        } else {
            MessagePassingQueue<DefaultHandle<T>> handles = pooledHandles;
            if (handles != null) {
                handles.relaxedOffer(handle);
            }
        }
    }

    private static bool isTerminated(Thread owner) {
        // Do not use `Thread.getState()` in J9 JVM because it's known to have a performance issue.
        // See: https://github.com/netty/netty/issues/13347#issuecomment-1518537895
        return PlatformDependent.isJ9Jvm() ? !owner.isAlive() : owner.getState() == Thread.State.TERMINATED;
    }

    DefaultHandle<T> newHandle() {
        if (++ratioCounter >= ratioInterval) {
            ratioCounter = 0;
            return new DefaultHandle<T>(this);
        }
        return null;
    }

    @Override
    public void accept(DefaultHandle<T> e) {
        batch.addLast(e);
    }
}


public class DefaultHandle<T> : RecycleEnhancedHandle<T> 
{
    private static readonly int STATE_CLAIMED = 0;
    private static readonly int STATE_AVAILABLE = 1;
    private static readonly AtomicIntegerFieldUpdater<DefaultHandle<?>> STATE_UPDATER;
    static {
        AtomicIntegerFieldUpdater<?> updater = AtomicIntegerFieldUpdater.newUpdater(typeof(DefaultHandle), "state");
        //noinspection unchecked
        STATE_UPDATER = (AtomicIntegerFieldUpdater<DefaultHandle<?>>) updater;
    }

    private volatile int state; // State is initialised to STATE_CLAIMED (aka. 0) so they can be released.
    private readonly LocalPool<T> localPool;
    private T value;

    DefaultHandle(LocalPool<T> localPool) {
        this.localPool = localPool;
    }

    public override void recycle(T obj) {
        if (!obj.Equals(value)) {
            throw new ArgumentException("object does not belong to handle");
        }
        localPool.release(this, true);
    }

    public override void unguardedRecycle(object obj) {
        if (!obj.Equals(value)) {
            throw new ArgumentException("object does not belong to handle");
        }
        localPool.release(this, false);
    }

    public T get() {
        return value;
    }

    public void set(T value) {
        this.value = value;
    }

    public void toClaimed()
    {
        Debug.Assert(state == STATE_AVAILABLE);
        STATE_UPDATER.lazySet(this, STATE_CLAIMED);
    }

    public void toAvailable() {
        int prev = STATE_UPDATER.getAndSet(this, STATE_AVAILABLE);
        if (prev == STATE_AVAILABLE) {
            throw new InvalidOperationException("object has been recycled already.");
        }
    }

    public void unguardedToAvailable() {
        int prev = state;
        if (prev == STATE_AVAILABLE) {
            throw new InvalidOperationException("object has been recycled already.");
        }
        STATE_UPDATER.lazySet(this, STATE_AVAILABLE);
    }
}
    

/**
 * This is an implementation of {@link MessagePassingQueue}, similar to what might be returned from
 * {@link PlatformDependent#newMpscQueue(int)}, but intended to be used for debugging purpose.
 * The implementation relies on synchronised monitor locks for thread-safety.
 * The {@code fill} bulk operation is not supported by this implementation.
 */
public class BlockingMessageQueue<T>
{
    private readonly object _lock;
    private readonly Queue<T> deque;
    private readonly int maxCapacity;

    BlockingMessageQueue(int maxCapacity) {
        _lock = new object();
        this.maxCapacity = maxCapacity;
        // This message passing queue is backed by an ArrayDeque instance,
        // made thread-safe by synchronising on `this` BlockingMessageQueue instance.
        // Why ArrayDeque?
        // We use ArrayDeque instead of LinkedList or LinkedBlockingQueue because it's more space efficient.
        // We use ArrayDeque instead of List because we need the queue APIs.
        // We use ArrayDeque instead of ConcurrentLinkedQueue because CLQ is unbounded and has O(n) size().
        // We use ArrayDeque instead of ArrayBlockingQueue because ABQ allocates its max capacity up-front,
        // and these queues will usually have large capacities, in potentially great numbers (one per thread),
        // but often only have comparatively few items in them.
        deque = new Queue<T>(maxCapacity);
    }

    public bool offer(T e) {
        lock (_lock)
        {
            if (deque.Count == maxCapacity)
            {
                return false;
            }

            deque.Enqueue(e);
            return true;
        }
    }

    public T poll() 
    {
        lock (_lock)
        {
            return deque.Dequeue();
        }
    }

    public T peek() {
        lock (_lock)
        {
            deque.TryPeek(out var result);
            return result;
        }
    }

    public int size() 
    {
        lock (_lock)
        {
            return deque.Count;
        }
    }

    public void clear() {
        lock (_lock)
        {
            deque.Clear();
        }
    }

    public bool isEmpty() {
        lock (_lock)
        {
            return 0 == deque.Count;
        }
    }

    public int capacity() 
    {
        return maxCapacity;
    }

    public int drain(Action<T> c, int limit) {
        T obj;
        int i = 0;
        for (; i < limit && (obj = poll()) != null; i++) {
            c.Invoke(obj);
        }
        return i;
    }

    public int fill(Func<T> s, int limit) {
        throw new NotSupportedException();
    }

    public int drain(Action<T> c) {
        throw new NotSupportedException();
    }

    public int fill(Func<T> s) {
        throw new NotSupportedException();
    }
    
    public void drain(Action<T> c, IWaitStrategy wait, IExitCondition exit) 
    {
        throw new NotSupportedException();
    }
    
    public void fill(Func<T> s, IWaitStrategy wait, IExitCondition exit) 
    {
        throw new NotSupportedException();
    }
}

/**
 * Light-weight object pool based on a thread-local stack.
 *
 * @param <T> the type of the pooled object
 */
public abstract class Recycler<T>
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance<Recycler<T>>();

    private static readonly RecycleEnhancedHandle<T> NOOP_HANDLE = new NoopRecyclerEnhancedHandle<T>();
    private static readonly int DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD = 4 * 1024; // Use 4k instances as default.
    private static readonly int DEFAULT_MAX_CAPACITY_PER_THREAD;
    private static readonly int RATIO;
    private static readonly int DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD;
    private static readonly bool BLOCKING_POOL;
    private static readonly bool BATCH_FAST_TL_ONLY;

    static Recycler() 
    {
        // In the future, we might have different maxCapacity for different object types.
        // e.g. io.netty.recycler.maxCapacity.writeTask
        //      io.netty.recycler.maxCapacity.outboundBuffer
        int maxCapacityPerThread = SystemPropertyUtil.getInt("io.netty.recycler.maxCapacityPerThread",
                SystemPropertyUtil.getInt("io.netty.recycler.maxCapacity", DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD));
        if (maxCapacityPerThread < 0) {
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

        if (logger.isDebugEnabled()) {
            if (DEFAULT_MAX_CAPACITY_PER_THREAD == 0) {
                logger.debug("-Dio.netty.recycler.maxCapacityPerThread: disabled");
                logger.debug("-Dio.netty.recycler.ratio: disabled");
                logger.debug("-Dio.netty.recycler.chunkSize: disabled");
                logger.debug("-Dio.netty.recycler.blocking: disabled");
                logger.debug("-Dio.netty.recycler.batchFastThreadLocalOnly: disabled");
            } else {
                logger.debug("-Dio.netty.recycler.maxCapacityPerThread: {}", DEFAULT_MAX_CAPACITY_PER_THREAD);
                logger.debug("-Dio.netty.recycler.ratio: {}", RATIO);
                logger.debug("-Dio.netty.recycler.chunkSize: {}", DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
                logger.debug("-Dio.netty.recycler.blocking: {}", BLOCKING_POOL);
                logger.debug("-Dio.netty.recycler.batchFastThreadLocalOnly: {}", BATCH_FAST_TL_ONLY);
            }
        }
    }

    private readonly int maxCapacityPerThread;
    private readonly int interval;
    private readonly int chunkSize;
    private readonly FastThreadLocal<LocalPool<T>> threadLocal = new FastThreadLocal<LocalPool<T>>() {
        @Override
        protected LocalPool<T> initialValue() {
            return new LocalPool<T>(maxCapacityPerThread, interval, chunkSize);
        }

        @Override
        protected void onRemoval(LocalPool<T> value) {
            super.onRemoval(value);
            MessagePassingQueue<DefaultHandle<T>> handles = value.pooledHandles;
            value.pooledHandles = null;
            value.owner = null;
            handles.clear();
        }
    };

    protected Recycler() {
        this(DEFAULT_MAX_CAPACITY_PER_THREAD);
    }

    protected Recycler(int maxCapacityPerThread) {
        this(maxCapacityPerThread, RATIO, DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
    }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    @Deprecated
    @SuppressWarnings("unused") // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor) {
        this(maxCapacityPerThread, RATIO, DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
    }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    @Deprecated
    @SuppressWarnings("unused") // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor,
                       int ratio, int maxDelayedQueuesPerThread) {
        this(maxCapacityPerThread, ratio, DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
    }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    @Deprecated
    @SuppressWarnings("unused") // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor,
                       int ratio, int maxDelayedQueuesPerThread, int delayedQueueRatio) {
        this(maxCapacityPerThread, ratio, DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
    }

    protected Recycler(int maxCapacityPerThread, int ratio, int chunkSize) {
        interval = Math.Max(0, ratio);
        if (maxCapacityPerThread <= 0) {
            this.maxCapacityPerThread = 0;
            this.chunkSize = 0;
        } else {
            this.maxCapacityPerThread = Math.Max(4, maxCapacityPerThread);
            this.chunkSize = Math.Max(2, Math.Min(chunkSize, this.maxCapacityPerThread >> 1));
        }
    }

    @SuppressWarnings("unchecked")
    public final T get() {
        if (maxCapacityPerThread == 0 ||
                (PlatformDependent.isVirtualThread(Thread.currentThread()) &&
                        !FastThreadLocalThread.currentThreadHasFastThreadLocal())) {
            return newObject((Handle<T>) NOOP_HANDLE);
        }
        LocalPool<T> localPool = threadLocal.get();
        DefaultHandle<T> handle = localPool.claim();
        T obj;
        if (handle == null) {
            handle = localPool.newHandle();
            if (handle != null) {
                obj = newObject(handle);
                handle.set(obj);
            } else {
                obj = newObject((Handle<T>) NOOP_HANDLE);
            }
        } else {
            obj = handle.get();
        }

        return obj;
    }

    /**
     * @deprecated use {@link Handle#recycle(object)}.
     */
    @Deprecated
    public final bool recycle(T o, Handle<T> handle) {
        if (handle == NOOP_HANDLE) {
            return false;
        }

        handle.recycle(o);
        return true;
    }

    @VisibleForTesting
    final int threadLocalSize() {
        if (PlatformDependent.isVirtualThread(Thread.currentThread()) &&
                !FastThreadLocalThread.currentThreadHasFastThreadLocal()) {
            return 0;
        }
        LocalPool<T> localPool = threadLocal.getIfExists();
        return localPool == null ? 0 : localPool.pooledHandles.size() + localPool.batch.size();
    }

    /**
     * @param handle can NOT be null.
     */
    protected abstract T newObject(Handle<T> handle);



}
