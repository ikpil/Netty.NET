/*
 * Copyright 2012 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

public partial class PooledByteBufAllocator
{
    // CLR: physical-thread affinity must not flow with ExecutionContext/AsyncLocal.
    // The allocator owns this registry; no thread-local root retains the allocator.
    // Weak thread identity prevents retaining terminated Thread objects and protects
    // against managed thread ID reuse. Each binding owns its optional region caches.
    private readonly Dictionary<int, ThreadBinding> _threadBindings = new();

    // Slow resize initializes the caller's binding, just like Java reallocate's
    // parent.threadCache(). A miss uses the old arena; a hit uses the cached owner.
    internal PoolThreadCache PrepareThreadBinding()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetThreadBinding()?.Cache;
        }
    }

    private ThreadBinding GetThreadBinding()
    {
        Debug.Assert(Monitor.IsEntered(_sync));
        if (_heapArenas.Length == 0 && _directArenas.Length == 0) return null;
        Thread thread = Thread.CurrentThread;
        int id = thread.ManagedThreadId;
        if (_threadBindings.TryGetValue(id, out ThreadBinding binding) && binding.IsOwner(thread)) return binding;
        ReleaseTerminatedThreadBindingsCore();
        IEventExecutor executor = ThreadExecutorMap.CurrentExecutor();
        bool useCache = _runtimeDefaults == null || _runtimeDefaults.UseCacheForAllThreads ||
            // If the current thread is a FastThreadLocalThread we will always use the cache
            FastThreadLocalThread.CurrentThreadHasFastThreadLocal() || executor != null;
        // No caching so just use 0 as sizes.
        binding = new ThreadBinding(thread, LeastUsedArena(_heapArenas), LeastUsedArena(_directArenas),
            useCache ? SmallCacheSize : 0, useCache ? NormalCacheSize : 0,
            useCache ? _maxCachedBufferCapacity : 0, useCache ? _cacheTrimInterval : 0);
        _threadBindings.Add(id, binding);
        binding.HeapArena?.AddThreadBinding();
        binding.DirectArena?.AddThreadBinding();
        try
        {
            // The Thread is used by an EventExecutor, let's use the cache as the chances are good that we
            // will allocate a lot!
            // CLR: explicit constructor sizes remain opt-in. Runtime defaults follow
            // the original eligibility rules; mapped executors permit periodic trim.
            if (_cacheTrimPeriod > TimeSpan.Zero && executor != null)
                binding.StartTrimming(executor, _cacheTrimPeriod);
            if (_threadCleanup != null && FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals())
                _threadCleanup.Set(new WeakReference<ThreadBinding>(binding));
        }
        catch
        {
            // Failed scheduling must not publish a partial binding/count or reserve storage.
            _threadBindings.Remove(id);
            binding.Detach();
            throw;
        }
        return binding;
    }

    // CLR: a cleanup registration must not root the allocator through its arena/binding.
    // Both links are weak. The allocator registry keeps active bindings alive until detach.
    private sealed class ThreadCleanup(PooledByteBufAllocator allocator) : FastThreadLocal<WeakReference<ThreadBinding>>
    {
        private readonly WeakReference<PooledByteBufAllocator> _allocator = new(allocator);
        protected override void OnRemoval(WeakReference<ThreadBinding> value)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (value == null || !value.TryGetTarget(out var binding) || !_allocator.TryGetTarget(out var owner)) return;
            lock (owner._sync)
            {
                int id = Environment.CurrentManagedThreadId;
                if (!owner._threadBindings.TryGetValue(id, out var current) || !ReferenceEquals(current, binding)) return;
                owner._threadBindings.Remove(id);
                binding.Detach();
            }
        }
    }

    private static PoolArena LeastUsedArena(PoolArena[] arenas)
    {
        if (arenas.Length == 0) return null;
        PoolArena minArena = arenas[0];
        //optimized
        //If it is the first execution, directly return minarena and reduce the number of for loop comparisons below
        if (minArena.NumThreadCaches == 0) return minArena;
        for (int i = 1; i < arenas.Length; ++i)
            if (arenas[i].NumThreadCaches < minArena.NumThreadCaches) minArena = arenas[i];
        return minArena;
    }

    /**
     * Trim thread local cache for the current {@link Thread}, which will give back any cached memory that was not
     * allocated frequently since the last trim operation.
     *
     * Returns {@code true} if a cache for the current {@link Thread} exists and so was trimmed, false otherwise.
     */
    public bool TrimCurrentThreadCache()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        lock (_sync)
        {
            Thread thread = Thread.CurrentThread;
            if (!_threadBindings.TryGetValue(thread.ManagedThreadId, out ThreadBinding binding) || !binding.IsOwner(thread))
                return false;
            binding.Cache.Trim();
            return true;
        }
    }

    /// <summary>Drains cached regions and releases the calling physical thread's arena assignment.</summary>
    /// <remarks>Returns false if the thread has no assignment. The next factory allocation
    /// chooses the least-used arenas again. This operation does not return live buffers
    /// or their operation leases. Later returns bypass the detached cache.</remarks>
    public bool ReleaseCurrentThreadBinding()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        lock (_sync)
        {
            Thread thread = Thread.CurrentThread;
            if (!_threadBindings.TryGetValue(thread.ManagedThreadId, out ThreadBinding binding) || !binding.IsOwner(thread))
                return false;
            _threadBindings.Remove(thread.ManagedThreadId);
            binding.Detach();
            return true;
        }
    }

    /// <summary>Reclaims arena assignments for terminated or collected physical threads.</summary>
    /// <returns>The number of removed thread assignments, independent of storage kind.</returns>
    /// <remarks>Assignments on live CLR worker threads remain until explicit release or
    /// allocator disposal. Buffers retain their owning arena across thread exit and awaits.</remarks>
    public int ReleaseTerminatedThreadBindings()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        lock (_sync) return ReleaseTerminatedThreadBindingsCore();
    }

    private int ReleaseTerminatedThreadBindingsCore()
    {
        Debug.Assert(Monitor.IsEntered(_sync));
        List<int> removed = null;
        foreach (var pair in _threadBindings)
            if (!pair.Value.IsAlive) (removed ??= new List<int>()).Add(pair.Key);
        if (removed == null) return 0;
        foreach (int id in removed)
        {
            ThreadBinding binding = _threadBindings[id];
            _threadBindings.Remove(id);
            binding.Detach();
        }
        return removed.Count;
    }

    internal int NumThreadBindings
    {
        get
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                ReleaseTerminatedThreadBindingsCore();
                return _threadBindings.Count;
            }
        }
    }

    private sealed class ThreadBinding
    {
        private readonly WeakReference<Thread> _thread;
        internal readonly PoolArena HeapArena;
        internal readonly PoolArena DirectArena;
        internal readonly PoolThreadCache Cache;
        private CancellationTokenSource _trimCancellation;

        internal ThreadBinding(Thread thread, PoolArena heapArena, PoolArena directArena,
            int smallCacheSize, int normalCacheSize, int maxCachedBufferCapacity, int cacheTrimInterval)
        {
            _thread = new WeakReference<Thread>(thread);
            HeapArena = heapArena;
            DirectArena = directArena;
            Cache = new PoolThreadCache(heapArena, directArena, smallCacheSize, normalCacheSize,
                maxCachedBufferCapacity, cacheTrimInterval);
        }

        internal bool IsOwner(Thread thread) => _thread.TryGetTarget(out Thread owner) && ReferenceEquals(owner, thread);
        internal bool IsAlive => _thread.TryGetTarget(out Thread thread) && thread.IsAlive;

        internal void StartTrimming(IEventExecutor executor, TimeSpan period)
        {
            // Housekeeping must not retain the caller's AsyncLocal payload/context.
            if (ExecutionContext.IsFlowSuppressed()) StartTrimmingCore(executor, period);
            else
                using (ExecutionContext.SuppressFlow()) StartTrimmingCore(executor, period);
        }

        private void StartTrimmingCore(IEventExecutor executor, TimeSpan period)
        {
            _trimCancellation = new CancellationTokenSource();
            // Target this registration, not a task continuation's current thread.
            // Cache's Monitor permits unordered executors and prevents an old
            // reservation from trimming a later binding after release/reassignment.
            Task task = executor.ScheduleAtFixedRateAsync(Cache.Trim, period, period, _trimCancellation.Token);
            if (task.IsCompleted) task.GetAwaiter().GetResult();
            // Immediate rejection is propagated to the factory; later scheduler
            // faults are observed without retaining the binding in a continuation.
            _ = task.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        /**
         *  Should be called if the Thread that uses this cache is about to exit to release resources out of the cache
         */
        internal void Detach()
        {
            // As free() may be called either by the finalizer or by FastThreadLocal.onRemoval(...) we need to ensure
            // we only call this one time.
            // CLR: removing the registration under the allocator Monitor makes detach
            // exactly once, replacing the Java finalizer/AtomicBoolean strategy.
            // CLR: cancellation removes queued housekeeping and releases captured
            // cache/allocator references. A callback already transferred to a ready
            // queue cannot revive the closed cache or initialize another binding.
            try { _trimCancellation?.Cancel(); }
            finally
            {
                _trimCancellation?.Dispose();
                Cache.Dispose();
                HeapArena?.RemoveThreadBinding();
                DirectArena?.RemoveThreadBinding();
            }
        }
    }
}
