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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

public static class Recycler
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(Recycler));
    public const int DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD = 4 * 1024; // Use 4k instances as default.
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
        int capacity = SystemPropertyUtil.getInt("io.netty.recycler.maxCapacityPerThread",
            SystemPropertyUtil.getInt("io.netty.recycler.maxCapacity", DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD));
        DEFAULT_MAX_CAPACITY_PER_THREAD = capacity < 0 ? DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD : capacity;
        DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD = SystemPropertyUtil.getInt("io.netty.recycler.chunkSize", 32);
        // By default, we allow one push to a Recycler for each 8th try on handles that were never recycled before.
        // This should help to slowly increase the capacity of the recycler while not be too sensitive to allocation
        // bursts.
        RATIO = Math.Max(0, SystemPropertyUtil.getInt("io.netty.recycler.ratio", 8));
        BLOCKING_POOL = SystemPropertyUtil.getBoolean("io.netty.recycler.blocking", false);
        BATCH_FAST_TL_ONLY = SystemPropertyUtil.getBoolean("io.netty.recycler.batchFastThreadLocalOnly", true);
        if (logger.isDebugEnabled())
        {
            object capacityText = DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : DEFAULT_MAX_CAPACITY_PER_THREAD;
            logger.debug("-Dio.netty.recycler.maxCapacityPerThread: {}", capacityText);
            logger.debug("-Dio.netty.recycler.ratio: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : RATIO);
            logger.debug("-Dio.netty.recycler.chunkSize: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
            logger.debug("-Dio.netty.recycler.blocking: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : BLOCKING_POOL);
            logger.debug("-Dio.netty.recycler.batchFastThreadLocalOnly: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : BATCH_FAST_TL_ONLY);
        }
    }

    /**
     * Disassociates the {@link Recycler} from the current {@link Thread} if it was pinned,
     * see {@link #Recycler(Thread, boolean)}.
     * <p>
     * Be aware that this method is not thread-safe: it's necessary to allow a {@link Thread} to
     * be garbage collected even if {@link Handle}s are still referenced by other objects.
     * <p>
     */
    public static void unpinOwner<T>(Recycler<T> recycler) where T : class => recycler.unpinOwner();
}

/**
 * Light-weight object pool based on a thread-local stack.
 *
 * @param <T> the type of the pooled object
 */
public abstract class Recycler<T> where T : class
{
    private static readonly RecyclerEnhancedHandle<T> NOOP_HANDLE = new LocalPoolHandle(null);
    private static readonly LocalPool NOOP_LOCAL_POOL = new UnguardedLocalPool(0);
    private readonly LocalPool localPool;
    private readonly FastThreadLocal<LocalPool> threadLocalPool;

    /**
     * USE IT CAREFULLY!<br>
     * This is creating a shareable {@link Recycler} which {@code get()} can be called concurrently from different
     * {@link Thread}s.<br>
     * Usually {@link Recycler}s uses some form of thread-local storage, but this constructor is disabling it
     * and using a single pool of instances instead, sized as {@code maxCapacity}<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded = true}:
     * it means that {@link Handle#recycle(Object)} is not checking that {@code object} is the same which was
     * recycled and assume no other recycling happens concurrently
     * (similar to what {@link EnhancedHandle#unguardedRecycle(Object)} does).<br>
     */
    protected Recycler(int maxCapacity, bool unguarded)
    {
        maxCapacity = maxCapacity <= 0 ? 0 : Math.Max(4, maxCapacity);
        localPool = maxCapacity == 0 ? NOOP_LOCAL_POOL : createPool(maxCapacity, unguarded);
    }

    /**
     * USE IT CAREFULLY!<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded = true}:
     * it means that {@link Handle#recycle(Object)} is not checking that {@code object} is the same which was
     * recycled and assume no other recycling happens concurrently
     * (similar to what {@link EnhancedHandle#unguardedRecycle(Object)} does).<br>
     */
    protected Recycler(bool unguarded)
        : this(Recycler.DEFAULT_MAX_CAPACITY_PER_THREAD, Recycler.RATIO, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD, unguarded) { }

    /**
     * USE IT CAREFULLY!<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded = true} as stated by
     * {@link #Recycler(boolean)} and allows to pin the recycler to a specific {@link Thread}, if {@code owner}
     * is not {@code null}.
     * <p>
     * Since this method has been introduced for performance-sensitive cases it doesn't validate if {@link #get()} is
     * called from the {@code owner} {@link Thread}: it assumes {@link #get()} to never happen concurrently.
     * <p>
     */
    protected Recycler(Thread owner, bool unguarded)
        : this(Recycler.DEFAULT_MAX_CAPACITY_PER_THREAD, Recycler.RATIO, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD, owner, unguarded) { }

    protected Recycler(int maxCapacityPerThread)
        : this(maxCapacityPerThread, Recycler.RATIO, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD) { }
    protected Recycler() : this(Recycler.DEFAULT_MAX_CAPACITY_PER_THREAD) { }

    /**
     * USE IT CAREFULLY!<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded = true} as stated by
     * {@link #Recycler(boolean)}, but it allows to tune the chunk size used for local pooling.
     */
    protected Recycler(int chunksSize, int maxCapacityPerThread, bool unguarded)
        : this(maxCapacityPerThread, Recycler.RATIO, chunksSize, unguarded) { }

    /**
     * USE IT CAREFULLY!<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded = true} and allows pinning
     * the recycler to a specific {@link Thread}, as stated by {@link #Recycler(Thread, boolean)}.<br>
     * It also allows tuning the chunk size used for local pooling and the max capacity per thread.
     *
     * @throws IllegalArgumentException if {@code owner} is {@code null}.
     */
    protected Recycler(int chunkSize, int maxCapacityPerThread, Thread owner, bool unguarded)
        : this(maxCapacityPerThread, Recycler.RATIO, chunkSize, owner, unguarded) { }

    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    [Obsolete]
    // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor)
        : this(maxCapacityPerThread, Recycler.RATIO, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD) { }
    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    [Obsolete]
    // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor, int ratio, int maxDelayedQueuesPerThread)
        : this(maxCapacityPerThread, ratio, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD) { }
    /**
     * @deprecated Use one of the following instead:
     * {@link #Recycler()}, {@link #Recycler(int)}, {@link #Recycler(int, int, int)}.
     */
    [Obsolete]
    // Parameters we can't remove due to compatibility.
    protected Recycler(int maxCapacityPerThread, int maxSharedCapacityFactor, int ratio, int maxDelayedQueuesPerThread, int delayedQueueRatio)
        : this(maxCapacityPerThread, ratio, Recycler.DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD) { }

    protected Recycler(int maxCapacityPerThread, int interval, int chunkSize)
        : this(maxCapacityPerThread, interval, chunkSize, true, null, false) { }
    /**
     * USE IT CAREFULLY!<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded =true}
     * as stated by {@link #Recycler(boolean)}.
     */
    protected Recycler(int maxCapacityPerThread, int interval, int chunkSize, bool unguarded)
        : this(maxCapacityPerThread, interval, chunkSize, true, null, unguarded) { }
    /**
     * USE IT CAREFULLY!<br>
     * This is NOT enforcing pooled instances states to be validated if {@code unguarded =true}
     * as stated by {@link #Recycler(boolean)}.
     */
    protected Recycler(int maxCapacityPerThread, int interval, int chunkSize, Thread owner, bool unguarded)
        : this(maxCapacityPerThread, interval, chunkSize, false, owner, unguarded) { }

    private Recycler(int maxCapacityPerThread, int ratio, int chunkSize, bool useThreadLocalStorage, Thread owner, bool unguarded)
    {
        int interval = Math.Max(0, ratio);
        if (maxCapacityPerThread <= 0) { maxCapacityPerThread = 0; chunkSize = 0; }
        else
        {
            maxCapacityPerThread = Math.Max(4, maxCapacityPerThread);
            chunkSize = Math.Max(2, Math.Min(chunkSize, maxCapacityPerThread >> 1));
        }
        if (maxCapacityPerThread > 0 && useThreadLocalStorage)
            threadLocalPool = new PoolThreadLocal(maxCapacityPerThread, interval, chunkSize, unguarded);
        else
        {
            if (maxCapacityPerThread == 0) localPool = NOOP_LOCAL_POOL;
            else
            {
                ArgumentNullException.ThrowIfNull(owner);
                localPool = createPool(owner, maxCapacityPerThread, interval, chunkSize, unguarded);
            }
        }
    }

    private static LocalPool createPool(int capacity, bool unguarded)
        => unguarded ? new UnguardedLocalPool(capacity) : new GuardedLocalPool(capacity);
    private static LocalPool createPool(Thread owner, int capacity, int interval, int chunkSize, bool unguarded)
        => unguarded ? new UnguardedLocalPool(owner, capacity, interval, chunkSize) : new GuardedLocalPool(owner, capacity, interval, chunkSize);

    public T get()
    {
        if (localPool != null) return localPool.getWith(this);
        if (!FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals()) return newObject(NOOP_HANDLE);
        return threadLocalPool.get().getWith(this);
    }

    internal void unpinOwner() { if (localPool != null) localPool.unpin(); }

    /**
     * @deprecated use {@link Handle#recycle(Object)}.
     */
    [Obsolete]
    public bool recycle(T value, IRecyclerHandle<T> handle)
    {
        if (ReferenceEquals(handle, NOOP_HANDLE)) return false;
        handle.recycle(value);
        return true;
    }

    public int threadLocalSize()
    {
        if (localPool != null) return localPool.size();
        if (!FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals()) return 0;
        return threadLocalPool.getIfExists()?.size() ?? 0;
    }

    /**
     * @param handle can NOT be null.
     */
    protected abstract T newObject(IRecyclerHandle<T> handle);

    private sealed class PoolThreadLocal(int capacity, int interval, int chunkSize, bool unguarded) : FastThreadLocal<LocalPool>
    {
        protected override LocalPool initialValue()
            => createPool(!Recycler.BATCH_FAST_TL_ONLY || FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals()
                ? Thread.CurrentThread : null, capacity, interval, chunkSize, unguarded);
        protected override void onRemoval(LocalPool pool)
        {
            base.onRemoval(pool);
            pool.detach();
        }
    }

    /**
     * We created this handle to avoid having more than 2 concrete implementations of {@link EnhancedHandle}
     * i.e. NOOP_HANDLE, {@link DefaultHandle} and the one used in the LocalPool.
     */
    private sealed class LocalPoolHandle(UnguardedLocalPool pool) : RecyclerEnhancedHandle<T>
    {
        public override void recycle(T value) => pool?.release(value);
        public override void unguardedRecycle(object value) => pool?.release((T)value);
    }

    private sealed class DefaultHandle(GuardedLocalPool pool) : RecyclerEnhancedHandle<T>
    {
        private const int STATE_CLAIMED = 0;
        private const int STATE_AVAILABLE = 1;
        // CLR Interlocked replaces the JVM updater; no unchecked updater cast is needed.
        //noinspection unchecked
        private int state; // State is initialised to STATE_CLAIMED (aka. 0) so they can be released.
        private T value;
        public override void recycle(T item)
        {
            if (!ReferenceEquals(item, value)) throw new ArgumentException("object does not belong to handle");
            if (Interlocked.Exchange(ref state, STATE_AVAILABLE) == STATE_AVAILABLE)
                throw new InvalidOperationException("Object has been recycled already.");
            pool.release(this);
        }
        public override void unguardedRecycle(object item)
        {
            if (!ReferenceEquals(item, value)) throw new ArgumentException("object does not belong to handle");
            if (Volatile.Read(ref state) == STATE_AVAILABLE)
                throw new InvalidOperationException("Object has been recycled already.");
            Volatile.Write(ref state, STATE_AVAILABLE);
            pool.release(this);
        }
        internal T claim() { Volatile.Write(ref state, STATE_CLAIMED); return value; }
        internal void set(T item) => value = item;
    }

    private sealed class GuardedLocalPool : LocalPool<DefaultHandle>
    {
        // Eagerly initiate DefaultHandle class-loading.
        // CLR initializes the generic handle type on use; Java's eager class-loading block is unnecessary.
        internal GuardedLocalPool(int capacity) : base(capacity) { }
        internal GuardedLocalPool(Thread owner, int capacity, int interval, int chunkSize) : base(owner, capacity, interval, chunkSize) { }
        internal override T getWith(Recycler<T> recycler)
        {
            DefaultHandle handle = acquire();
            if (handle != null) return handle.claim();
            if (!canAllocatePooled()) return recycler.newObject(NOOP_HANDLE);
            handle = new DefaultHandle(this);
            T value = recycler.newObject(handle);
            handle.set(value);
            return value;
        }
    }

    private sealed class UnguardedLocalPool : LocalPool<T>
    {
        private readonly RecyclerEnhancedHandle<T> handle;
        internal UnguardedLocalPool(int capacity) : base(capacity)
            => handle = capacity == 0 ? null : new LocalPoolHandle(this);
        internal UnguardedLocalPool(Thread owner, int capacity, int interval, int chunkSize) : base(owner, capacity, interval, chunkSize)
            => handle = new LocalPoolHandle(this);
        internal override T getWith(Recycler<T> recycler)
            => acquire() ?? recycler.newObject(canAllocatePooled() ? handle : NOOP_HANDLE);
    }

    private abstract class LocalPool
    {
        internal abstract T getWith(Recycler<T> recycler);
        internal abstract int size();
        internal abstract void detach();
        internal abstract void unpin();
    }

    private abstract class LocalPool<H> : LocalPool where H : class
    {
        private readonly int ratioInterval;
        private readonly H[] batch;
        private int batchSize;
        private Thread owner;
        private ReturnQueue<H> pooledHandles;
        private int ratioCounter;
        protected LocalPool(int capacity)
        {
            // if there's no capacity, we need to never allocate pooled objects.
            // if there's capacity, because there is a shared pool, we always pool them, since we cannot trust the
            // thread unsafe ratio counter.
            ratioInterval = capacity == 0 ? -1 : 0;
            pooledHandles = createQueue<H>(capacity);
        }
        protected LocalPool(Thread owner, int capacity, int interval, int chunkSize)
        {
            ratioInterval = interval;
            this.owner = owner;
            batch = owner != null ? new H[chunkSize] : null;
            pooledHandles = createQueue<H>(capacity);
            ratioCounter = interval; // Start at interval so the first one will be recycled.
        }
        protected H acquire()
        {
            int size = batchSize;
            if (size == 0)
            {
                // it's ok to be racy; at worst we reuse something that won't return back to the pool
                return Volatile.Read(ref pooledHandles)?.poll();
            }
            int top = size - 1;
            H value = batch[top];
            batchSize = top;
            batch[top] = null;
            return value;
        }
        internal void release(H value)
        {
            Thread currentOwner = Volatile.Read(ref owner);
            if (currentOwner != null && Thread.CurrentThread == currentOwner && batchSize < batch.Length)
                batch[batchSize++] = value;
            else if (currentOwner != null && isTerminated(currentOwner))
            {
                Volatile.Write(ref pooledHandles, null);
                Volatile.Write(ref owner, null);
            }
            else Volatile.Read(ref pooledHandles)?.offer(value);
        }
        private static bool isTerminated(Thread owner)
        {
            // Do not use `Thread.getState()` in J9 JVM because it's known to have a performance issue.
            // See: https://github.com/netty/netty/issues/13347#issuecomment-1518537895
            // CLR ThreadState.Stopped distinguishes termination from an unstarted thread.
            return (owner.ThreadState & ThreadState.Stopped) != 0;
        }
        protected bool canAllocatePooled()
        {
            if (ratioInterval < 0) return false;
            if (ratioInterval == 0) return true;
            if (++ratioCounter < ratioInterval) return false;
            ratioCounter = 0;
            return true;
        }
        internal override int size() => (Volatile.Read(ref pooledHandles)?.size() ?? 0) + (batch != null ? batchSize : 0);
        internal override void unpin() => Volatile.Write(ref owner, null);
        internal override void detach()
        {
            ReturnQueue<H> handles = Volatile.Read(ref pooledHandles);
            Volatile.Write(ref pooledHandles, null);
            Volatile.Write(ref owner, null);
            handles?.clear();
        }
    }

    private interface ReturnQueue<H> where H : class
    {
        bool offer(H value);
        H poll();
        int size();
        void clear();
    }
    private static ReturnQueue<H> createQueue<H>(int capacity) where H : class
        => capacity == 0 ? null : Recycler.BLOCKING_POOL ? new BlockingMessageQueue<H>(capacity)
            : new ConcurrentReturnQueue<H>(MathUtil.safeFindNextPositivePowerOfTwo(capacity));

    // CLR segments replace JCTools chunk allocation. CAS reservations enforce the
    // rounded fixed bound while ConcurrentQueue supplies MPMC publication and FIFO.
    private sealed class ConcurrentReturnQueue<H>(int capacity) : ReturnQueue<H> where H : class
    {
        private readonly ConcurrentQueue<H> queue = new();
        private int count;
        public bool offer(H value)
        {
            int before;
            do
            {
                before = Volatile.Read(ref count);
                if (before >= capacity) return false;
            } while (Interlocked.CompareExchange(ref count, before + 1, before) != before);
            try { queue.Enqueue(value); }
            catch { Interlocked.Decrement(ref count); throw; }
            return true;
        }
        public H poll()
        {
            if (!queue.TryDequeue(out H value)) return null;
            Interlocked.Decrement(ref count);
            return value;
        }
        public int size() => Volatile.Read(ref count);
        public void clear() { while (queue.TryDequeue(out _)) Interlocked.Decrement(ref count); }
    }

    /**
     * This is an implementation of {@link MessagePassingQueue}, similar to what might be returned from
     * {@link PlatformDependent#newMpscQueue(int)}, but intended to be used for debugging purpose.
     * The implementation relies on synchronised monitor locks for thread-safety.
     * The {@code fill} bulk operation is not supported by this implementation.
     */
    private sealed class BlockingMessageQueue<H> : ReturnQueue<H> where H : class
    {
        private readonly Queue<H> deque = new();
        private readonly int capacity;
        internal BlockingMessageQueue(int capacity)
        {
            this.capacity = capacity;
            // This message passing queue is backed by an ArrayDeque instance,
            // made thread-safe by synchronising on `this` BlockingMessageQueue instance.
            // Why ArrayDeque?
            // We use ArrayDeque instead of LinkedList or LinkedBlockingQueue because it's more space efficient.
            // We use ArrayDeque instead of ArrayList because we need the queue APIs.
            // We use ArrayDeque instead of ConcurrentLinkedQueue because CLQ is unbounded and has O(n) size().
            // We use ArrayDeque instead of ArrayBlockingQueue because ABQ allocates its max capacity up-front,
            // and these queues will usually have large capacities, in potentially great numbers (one per thread),
            // but often only have comparatively few items in them.
            // CLR Queue uses the same compact, lazily grown array strategy.
        }
        public bool offer(H value)
        {
            using var held = UninterruptibleMonitor.enter(this);
            if (deque.Count == capacity) return false;
            deque.Enqueue(value);
            return true;
        }
        public H poll()
        {
            using var held = UninterruptibleMonitor.enter(this);
            return deque.TryDequeue(out H value) ? value : null;
        }
        public int size() { using var held = UninterruptibleMonitor.enter(this); return deque.Count; }
        public void clear() { using var held = UninterruptibleMonitor.enter(this); deque.Clear(); }
    }
}
