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
using System.Numerics;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Buffer;

public partial class PooledByteBufAllocator
{
    private readonly ThreadCleanup _threadCleanup;

    // CLR: the process settings are snapshotted once, like the source's static DEFAULT_* fields; the shared Default
    // and the omitted constructor arguments use them. Snapshotting alone allocates no arenas.
    private static class Configuration
    {
        internal static readonly RuntimeDefaults Value = new();
        static Configuration() { }
    }
    private static class DefaultInstance
    {
        internal static readonly PooledByteBufAllocator Value = new(ByteBufUtil.PreferDirect);
        static DefaultInstance() { }
    }

    /**
     * Default number of heap arenas - System Property: io.netty.allocator.numHeapArenas - default 2 * cores
     */
    public static int DefaultNumHeapArena() => Configuration.Value.HeapArenaCount;

    /**
     * Default number of direct arenas - System Property: io.netty.allocator.numDirectArenas - default 2 * cores
     */
    public static int DefaultNumDirectArena() => Configuration.Value.DirectArenaCount;

    /**
     * Default buffer page size - System Property: io.netty.allocator.pageSize - default 8192
     */
    public static int DefaultPageSize() => Configuration.Value.PageSize;

    /**
     * Default maximum order - System Property: io.netty.allocator.maxOrder - default 9
     */
    public static int DefaultMaxOrder() => Configuration.Value.MaxOrder;

    /**
     * Default thread caching behavior - System Property: io.netty.allocator.useCacheForAllThreads - default false
     */
    public static bool DefaultUseCacheForAllThreads() => Configuration.Value.UseCacheForAllThreads;

    /**
     * Default small cache size - System Property: io.netty.allocator.smallCacheSize - default 256
     */
    public static int DefaultSmallCacheSize() => Configuration.Value.SmallCacheSize;

    /**
     * Default normal cache size - System Property: io.netty.allocator.normalCacheSize - default 64
     */
    public static int DefaultNormalCacheSize() => Configuration.Value.NormalCacheSize;

    /**
     * Default prefer direct - System Property: io.netty.noPreferDirect - default false
     */
    public static bool DefaultPreferDirect() => ByteBufUtil.PreferDirect;

    /**
     * Default tiny cache size - default 0
     *
     * @deprecated Tiny caches have been merged into small caches.
     */
    [Obsolete("Tiny caches have been merged into small caches.")]
    public static int DefaultTinyCacheSize() => 0;

    private sealed class RuntimeDefaults
    {
        internal readonly int PageSize, MaxOrder, Alignment, HeapArenaCount, DirectArenaCount;
        internal readonly int SmallCacheSize, NormalCacheSize, MaxCachedCapacity, TrimInterval;
        internal readonly bool UseCacheForAllThreads;
        internal readonly TimeSpan TrimPeriod;

        internal RuntimeDefaults()
        {
            IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(PooledByteBufAllocator));
            int page = SystemPropertyUtil.GetInt("io.netty.allocator.pageSize", 8192);
            int alignment = SystemPropertyUtil.GetInt("io.netty.allocator.directMemoryCacheAlignment", 0);
            if (page < 4096 || !BitOperations.IsPow2(page) || alignment > page)
            {
                logger.Debug("Invalid pooled page size/alignment; using pageSize 8192 and alignment 0");
                page = 8192; alignment = 0;
            }
            // Java validates alignment shape when constructing the allocator, after
            // its page fallback. Preserve that timing instead of silently correcting it.
            PageSize = page; Alignment = alignment;
            // 8192 << 9 = 4 MiB per chunk
            int order = SystemPropertyUtil.GetInt("io.netty.allocator.maxOrder", 9);
            if (order < 0 || order > 14 || ((long)page << order) > 1 << 30)
            {
                logger.Debug("Invalid pooled maxOrder; using 9");
                order = 9;
            }
            MaxOrder = order;

            // Determine reasonable default for nHeapArena and nDirectArena.
            // Assuming each arena has 3 chunks, the pool should not consume more than 50% of max memory.
            // CLR: GCMemoryInfo.TotalAvailableMemoryBytes (GCHeapHardLimit, a container limit or physical memory) is
            // the counterpart of Runtime.maxMemory().
            long maxMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

            /*
             * We use 2 * available processors by default to reduce contention as we use 2 * available processors for the
             * number of EventLoops in NIO and EPOLL as well. If we choose a smaller number we will run into hot spots as
             * allocation and de-allocation needs to be synchronized on the PoolArena.
             *
             * See https://github.com/netty/netty/issues/3888.
             */
            int defaultMinNumArena = NettyRuntime.AvailableProcessors() * 2;
            int defaultChunkSize = PageSize << MaxOrder;
            HeapArenaCount = Math.Max(0,
                    SystemPropertyUtil.GetInt(
                            "io.netty.allocator.numHeapArenas",
                            (int)Math.Min(
                                    defaultMinNumArena,
                                    maxMemory / defaultChunkSize / 2 / 3)));
            DirectArenaCount = Math.Max(0,
                    SystemPropertyUtil.GetInt(
                            "io.netty.allocator.numDirectArenas",
                            (int)Math.Min(
                                    defaultMinNumArena,
                                    MaxDirectMemory(maxMemory) / defaultChunkSize / 2 / 3)));
            // cache sizes
            SmallCacheSize = SystemPropertyUtil.GetInt("io.netty.allocator.smallCacheSize", 256);
            NormalCacheSize = SystemPropertyUtil.GetInt("io.netty.allocator.normalCacheSize", 64);
            // 32 kb is the default maximum capacity of the cached buffer. Similar to what is explained in
            // 'Scalable memory allocation using jemalloc'
            MaxCachedCapacity = SystemPropertyUtil.GetInt("io.netty.allocator.maxCachedBufferCapacity", 32 * 1024);
            // the number of threshold of allocations when cached entries will be freed up if not frequently used
            TrimInterval = SystemPropertyUtil.GetInt("io.netty.allocator.cacheTrimInterval", 8192);
            long milliseconds;
            if (SystemPropertyUtil.Contains("io.netty.allocation.cacheTrimIntervalMillis"))
            {
                logger.Warn("-Dio.netty.allocation.cacheTrimIntervalMillis is deprecated, use -Dio.netty.allocator.cacheTrimIntervalMillis");
                if (SystemPropertyUtil.Contains("io.netty.allocator.cacheTrimIntervalMillis"))
                {
                    // Both system properties are specified. Use the non-deprecated one.
                    milliseconds = SystemPropertyUtil.GetLong("io.netty.allocator.cacheTrimIntervalMillis", 0);
                }
                else milliseconds = SystemPropertyUtil.GetLong("io.netty.allocation.cacheTrimIntervalMillis", 0);
            }
            else milliseconds = SystemPropertyUtil.GetLong("io.netty.allocator.cacheTrimIntervalMillis", 0);
            // CLR TimeSpan has a narrower range than Java's long millisecond delays.
            TrimPeriod = milliseconds <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(
                Math.Min(milliseconds, long.MaxValue / TimeSpan.TicksPerMillisecond) * TimeSpan.TicksPerMillisecond);
            UseCacheForAllThreads = SystemPropertyUtil.GetBoolean("io.netty.allocator.useCacheForAllThreads", false);
        }

        // PlatformDependent.maxDirectMemory(): a positive io.netty.maxDirectMemory, otherwise estimateMaxDirectMemory(),
        // which falls back to Runtime.maxMemory() without -XX:MaxDirectMemorySize. CLR: there is no JVM
        // direct-memory flag, so the GC memory budget is that fallback.
        private static long MaxDirectMemory(long maxMemory)
        {
            long maxDirectMemory = SystemPropertyUtil.GetLong("io.netty.maxDirectMemory", -1);
            return maxDirectMemory >= 1 ? maxDirectMemory : maxMemory;
        }
    }
}
