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
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Recycler));
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
        int capacity = SystemPropertyUtil.GetInt("io.netty.recycler.maxCapacityPerThread",
            SystemPropertyUtil.GetInt("io.netty.recycler.maxCapacity", DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD));
        DEFAULT_MAX_CAPACITY_PER_THREAD = capacity < 0 ? DEFAULT_INITIAL_MAX_CAPACITY_PER_THREAD : capacity;
        DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD = SystemPropertyUtil.GetInt("io.netty.recycler.chunkSize", 32);
        // By default, we allow one push to a Recycler for each 8th try on handles that were never recycled before.
        // This should help to slowly increase the capacity of the recycler while not be too sensitive to allocation
        // bursts.
        RATIO = Math.Max(0, SystemPropertyUtil.GetInt("io.netty.recycler.ratio", 8));
        BLOCKING_POOL = SystemPropertyUtil.GetBoolean("io.netty.recycler.blocking", false);
        BATCH_FAST_TL_ONLY = SystemPropertyUtil.GetBoolean("io.netty.recycler.batchFastThreadLocalOnly", true);
        if (logger.IsDebugEnabled())
        {
            object capacityText = DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : DEFAULT_MAX_CAPACITY_PER_THREAD;
            logger.Debug("-Dio.netty.recycler.maxCapacityPerThread: {}", capacityText);
            logger.Debug("-Dio.netty.recycler.ratio: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : RATIO);
            logger.Debug("-Dio.netty.recycler.chunkSize: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : DEFAULT_QUEUE_CHUNK_SIZE_PER_THREAD);
            logger.Debug("-Dio.netty.recycler.blocking: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : BLOCKING_POOL);
            logger.Debug("-Dio.netty.recycler.batchFastThreadLocalOnly: {}", DEFAULT_MAX_CAPACITY_PER_THREAD == 0 ? "disabled" : BATCH_FAST_TL_ONLY);
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
    public static void UnpinOwner<T>(Recycler<T> recycler) where T : class => recycler.UnpinOwner();
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
        localPool = maxCapacity == 0 ? NOOP_LOCAL_POOL : CreatePool(maxCapacity, unguarded);
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
                localPool = CreatePool(owner, maxCapacityPerThread, interval, chunkSize, unguarded);
            }
        }
    }

    private static LocalPool CreatePool(int capacity, bool unguarded)
        => unguarded ? new UnguardedLocalPool(capacity) : new GuardedLocalPool(capacity);
    private static LocalPool CreatePool(Thread owner, int capacity, int interval, int chunkSize, bool unguarded)
        => unguarded ? new UnguardedLocalPool(owner, capacity, interval, chunkSize) : new GuardedLocalPool(owner, capacity, interval, chunkSize);

    public T Get()
    {
        if (localPool != null) return localPool.GetWith(this);
        if (!FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals()) return NewObject(NOOP_HANDLE);
        return threadLocalPool.Get().GetWith(this);
    }

    internal void UnpinOwner() { if (localPool != null) localPool.Unpin(); }

    /**
     * @deprecated use {@link Handle#recycle(Object)}.
     */
    [Obsolete]
    public bool Recycle(T value, IRecyclerHandle<T> handle)
    {
        if (ReferenceEquals(handle, NOOP_HANDLE)) return false;
        handle.Recycle(value);
        return true;
    }

    public int ThreadLocalSize()
    {
        if (localPool != null) return localPool.Size();
        if (!FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals()) return 0;
        return threadLocalPool.GetIfExists()?.Size() ?? 0;
    }

    /**
     * @param handle can NOT be null.
     */
    protected abstract T NewObject(IRecyclerHandle<T> handle);

    private sealed class PoolThreadLocal(int capacity, int interval, int chunkSize, bool unguarded) : FastThreadLocal<LocalPool>
    {
        protected override LocalPool InitialValue()
            => CreatePool(!Recycler.BATCH_FAST_TL_ONLY || FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals()
                ? Thread.CurrentThread : null, capacity, interval, chunkSize, unguarded);
        protected override void OnRemoval(LocalPool pool)
        {
            base.OnRemoval(pool);
            pool.Detach();
        }
    }

    /**
     * We created this handle to avoid having more than 2 concrete implementations of {@link EnhancedHandle}
     * i.e. NOOP_HANDLE, {@link DefaultHandle} and the one used in the LocalPool.
     */
    private sealed class LocalPoolHandle(UnguardedLocalPool pool) : RecyclerEnhancedHandle<T>
    {
        public override void Recycle(T value) => pool?.Release(value);
        public override void UnguardedRecycle(object value) => pool?.Release((T)value);
    }

    private sealed class DefaultHandle(GuardedLocalPool pool) : RecyclerEnhancedHandle<T>
    {
        private const int STATE_CLAIMED = 0;
        private const int STATE_AVAILABLE = 1;
        // CLR Interlocked replaces the JVM updater; no unchecked updater cast is needed.
        //noinspection unchecked
        private int state; // State is initialised to STATE_CLAIMED (aka. 0) so they can be released.
        private T value;
        public override void Recycle(T item)
        {
            if (!ReferenceEquals(item, value)) throw new ArgumentException("object does not belong to handle");
            if (Interlocked.Exchange(ref state, STATE_AVAILABLE) == STATE_AVAILABLE)
                throw new InvalidOperationException("Object has been recycled already.");
            pool.Release(this);
        }
        public override void UnguardedRecycle(object item)
        {
            if (!ReferenceEquals(item, value)) throw new ArgumentException("object does not belong to handle");
            if (Volatile.Read(ref state) == STATE_AVAILABLE)
                throw new InvalidOperationException("Object has been recycled already.");
            Volatile.Write(ref state, STATE_AVAILABLE);
            pool.Release(this);
        }
        internal T Claim() { Volatile.Write(ref state, STATE_CLAIMED); return value; }
        internal void Set(T item) => value = item;
    }

    private sealed class GuardedLocalPool : LocalPool<DefaultHandle>
    {
        // Eagerly initiate DefaultHandle class-loading.
        // CLR initializes the generic handle type on use; Java's eager class-loading block is unnecessary.
        internal GuardedLocalPool(int capacity) : base(capacity) { }
        internal GuardedLocalPool(Thread owner, int capacity, int interval, int chunkSize) : base(owner, capacity, interval, chunkSize) { }
        internal override T GetWith(Recycler<T> recycler)
        {
            DefaultHandle handle = Acquire();
            if (handle != null) return handle.Claim();
            if (!CanAllocatePooled()) return recycler.NewObject(NOOP_HANDLE);
            handle = new DefaultHandle(this);
            T value = recycler.NewObject(handle);
            handle.Set(value);
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
        internal override T GetWith(Recycler<T> recycler)
            => Acquire() ?? recycler.NewObject(CanAllocatePooled() ? handle : NOOP_HANDLE);
    }

    private abstract class LocalPool
    {
        internal abstract T GetWith(Recycler<T> recycler);
        internal abstract int Size();
        internal abstract void Detach();
        internal abstract void Unpin();
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
            pooledHandles = CreateQueue<H>(capacity);
        }
        protected LocalPool(Thread owner, int capacity, int interval, int chunkSize)
        {
            ratioInterval = interval;
            this.owner = owner;
            batch = owner != null ? new H[chunkSize] : null;
            pooledHandles = CreateQueue<H>(capacity);
            ratioCounter = interval; // Start at interval so the first one will be recycled.
        }
        protected H Acquire()
        {
            int size = batchSize;
            if (size == 0)
            {
                // it's ok to be racy; at worst we reuse something that won't return back to the pool
                return Volatile.Read(ref pooledHandles)?.Poll();
            }
            int top = size - 1;
            H value = batch[top];
            batchSize = top;
            batch[top] = null;
            return value;
        }
        internal void Release(H value)
        {
            Thread currentOwner = Volatile.Read(ref owner);
            if (currentOwner != null && Thread.CurrentThread == currentOwner && batchSize < batch.Length)
                batch[batchSize++] = value;
            else if (currentOwner != null && IsTerminated(currentOwner))
            {
                Volatile.Write(ref pooledHandles, null);
                Volatile.Write(ref owner, null);
            }
            else Volatile.Read(ref pooledHandles)?.Offer(value);
        }
        private static bool IsTerminated(Thread owner)
        {
            // Do not use `Thread.getState()` in J9 JVM because it's known to have a performance issue.
            // See: https://github.com/netty/netty/issues/13347#issuecomment-1518537895
            // CLR ThreadState.Stopped distinguishes termination from an unstarted thread.
            return (owner.ThreadState & ThreadState.Stopped) != 0;
        }
        protected bool CanAllocatePooled()
        {
            if (ratioInterval < 0) return false;
            if (ratioInterval == 0) return true;
            if (++ratioCounter < ratioInterval) return false;
            ratioCounter = 0;
            return true;
        }
        internal override int Size() => (Volatile.Read(ref pooledHandles)?.Size() ?? 0) + (batch != null ? batchSize : 0);
        internal override void Unpin() => Volatile.Write(ref owner, null);
        internal override void Detach()
        {
            ReturnQueue<H> handles = Volatile.Read(ref pooledHandles);
            Volatile.Write(ref pooledHandles, null);
            Volatile.Write(ref owner, null);
            handles?.Clear();
        }
    }

    private interface ReturnQueue<H> where H : class
    {
        bool Offer(H value);
        H Poll();
        int Size();
        void Clear();
    }
    private static ReturnQueue<H> CreateQueue<H>(int capacity) where H : class
        => capacity == 0 ? null : Recycler.BLOCKING_POOL ? new BlockingMessageQueue<H>(capacity)
            : new ConcurrentReturnQueue<H>(MathUtil.SafeFindNextPositivePowerOfTwo(capacity));

    // CLR segments replace JCTools chunk allocation. CAS reservations enforce the
    // rounded fixed bound while ConcurrentQueue supplies MPMC publication and FIFO.
    private sealed class ConcurrentReturnQueue<H>(int capacity) : ReturnQueue<H> where H : class
    {
        private readonly ConcurrentQueue<H> queue = new();
        private int count;
        public bool Offer(H value)
        {
            int before;
            do
            {
                before = Volatile.Read(ref count);
                if (before >= capacity) return false;
            } while (Interlocked.CompareExchange(ref count, before + 1, before) != before);
            try { ConcurrentQueueOperations.EnqueueUninterruptibly(queue, value); }
            catch { Interlocked.Decrement(ref count); throw; }
            return true;
        }
        public H Poll() => TryPoll(out H value) ? value : null;
        private bool TryPoll(out H value)
        {
            if (!ConcurrentQueueOperations.TryDequeueUninterruptibly(queue, out value)) return false;
            Interlocked.Decrement(ref count);
            return true;
        }
        public int Size() => Volatile.Read(ref count);
        public void Clear() { while (TryPoll(out _)) { } }
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
        public bool Offer(H value)
        {
            using var held = UninterruptibleMonitor.Enter(this);
            if (deque.Count == capacity) return false;
            deque.Enqueue(value);
            return true;
        }
        public H Poll()
        {
            using var held = UninterruptibleMonitor.Enter(this);
            return deque.TryDequeue(out H value) ? value : null;
        }
        public int Size() { using var held = UninterruptibleMonitor.Enter(this); return deque.Count; }
        public void Clear() { using var held = UninterruptibleMonitor.Enter(this); deque.Clear(); }
    }
}
