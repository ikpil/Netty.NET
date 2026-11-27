/*
 * Copyright 2022 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    internal interface IChunkManagementStrategy
    {
        IChunkController CreateController(MagazineGroup group);

        IChunkCache CreateChunkCache(bool isThreadLocal);
    }

    internal interface IChunkController
    {
        /**
         * Compute the "fast max capacity" value for the buffer.
         */
        int ComputeBufferCapacity(int requestedSize, int maxCapacity, bool isReallocation);

        /**
         * Allocate a new {@link Chunk} for the given {@link Magazine}.
         */
        Chunk NewChunkAllocation(int promptingSize, Magazine magazine);
    }

    // CLR: the strategies keep the allocator only to read its per-instance cache settings.
    internal sealed class SizeClassChunkManagementStrategy : IChunkManagementStrategy
    {
        // To amortize activation/deactivation of chunks, we should have a minimum number of segments per chunk.
        // We choose 32 because it seems neither too small nor too big.
        // For segments of 16 KiB, the chunks will be half a megabyte.
        private const int MIN_SEGMENTS_PER_CHUNK = 32;
        private readonly AdaptivePoolingAllocator _allocator;
        private readonly int _segmentSize;
        private readonly int _chunkSize;

        internal SizeClassChunkManagementStrategy(AdaptivePoolingAllocator allocator, int segmentSize)
        {
            _allocator = allocator;
            _segmentSize = ObjectUtil.CheckPositive(segmentSize, nameof(segmentSize));
            _chunkSize = Math.Max(MIN_CHUNK_SIZE, segmentSize * MIN_SEGMENTS_PER_CHUNK);
        }

        public IChunkController CreateController(MagazineGroup group)
        {
            return new SizeClassChunkController(group, _segmentSize, _chunkSize);
        }

        public IChunkCache CreateChunkCache(bool isThreadLocal)
        {
            return SizeClassedChunkCache.Create(isThreadLocal, _chunkSize, _allocator);
        }
    }

    internal sealed class SizeClassChunkController : IChunkController
    {
        private readonly IChunkAllocator _chunkAllocator;
        internal readonly int SegmentSize;
        internal readonly int ChunkSize;
        private readonly ChunkRegistry _chunkRegistry;

        internal SizeClassChunkController(MagazineGroup group, int segmentSize, int chunkSize)
        {
            _chunkAllocator = group.ChunkAllocator;
            SegmentSize = segmentSize;
            ChunkSize = chunkSize;
            _chunkRegistry = group.Allocator._chunkRegistry;
        }

        public int ComputeBufferCapacity(
                int requestedSize, int maxCapacity, bool isReallocation)
        {
            // CLR: argument guards; magazines only ask for valid pooled requests.
            ArgumentOutOfRangeException.ThrowIfNegative(requestedSize);
            if (maxCapacity < requestedSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            }
            return Math.Min(SegmentSize, maxCapacity);
        }

        public Chunk NewChunkAllocation(int promptingSize, Magazine magazine)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            ByteBuf chunkBuffer = _chunkAllocator.Allocate(ChunkSize, ChunkSize);
            // CLR: thread-local magazines have no allocation lock; their chunks keep the physical owner.
            bool threadLocal = magazine.AllocationLock == null;
            SizeClassedChunk chunk;
            try
            {
                chunk = new SizeClassedChunk(chunkBuffer, _chunkRegistry, SegmentSize, threadLocal);
                chunk.TrackAllocation(threadLocal);
            }
            catch
            {
                chunkBuffer.Release();
                throw;
            }
            chunk.AttachToMagazine(magazine);
            return chunk;
        }
    }

    internal sealed class BuddyChunkManagementStrategy : IChunkManagementStrategy
    {
        // CLR: StrongBox<int> with Volatile access replaces the shared AtomicInteger.
        private readonly StrongBox<int> _maxChunkSize = new();
        private readonly AdaptivePoolingAllocator _allocator;

        internal BuddyChunkManagementStrategy(AdaptivePoolingAllocator allocator)
        {
            _allocator = allocator;
        }

        public IChunkController CreateController(MagazineGroup group)
        {
            return new BuddyChunkController(group, _maxChunkSize);
        }

        public IChunkCache CreateChunkCache(bool isThreadLocal)
        {
            return new ConcurrentSkipListChunkCache(_allocator._chunkReuseQueue);
        }
    }

    internal sealed class BuddyChunkController : IChunkController
    {
        private readonly IChunkAllocator _chunkAllocator;
        private readonly ChunkRegistry _chunkRegistry;
        private readonly StrongBox<int> _maxChunkSize;
        // CLR: reads the allocator's per-instance MAX_CHUNK_SIZE.
        private readonly AdaptivePoolingAllocator _allocator;

        internal BuddyChunkController(MagazineGroup group, StrongBox<int> maxChunkSize)
        {
            _chunkAllocator = group.ChunkAllocator;
            _chunkRegistry = group.Allocator._chunkRegistry;
            _maxChunkSize = maxChunkSize;
            _allocator = group.Allocator;
        }

        public int ComputeBufferCapacity(int requestedSize, int maxCapacity, bool isReallocation)
        {
            // CLR: argument guards; magazines only ask for valid pooled requests.
            ArgumentOutOfRangeException.ThrowIfNegative(requestedSize);
            if (maxCapacity < requestedSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            }
            // Buddy reservations stay powers of two even when the buffer maximum is smaller.
            // The public MaxFastWritableBytes contract clamps that reservation to maxCapacity.
            return MathUtil.SafeFindNextPositivePowerOfTwo(requestedSize);
        }

        public Chunk NewChunkAllocation(int promptingSize, Magazine magazine)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // CLR: argument guard before the remembered size can change.
            ArgumentOutOfRangeException.ThrowIfNegative(promptingSize);
            int maxChunkSize = Volatile.Read(ref _maxChunkSize.Value);
            int proposedChunkSize = MathUtil.SafeFindNextPositivePowerOfTwo(BUFS_PER_CHUNK * promptingSize);
            int chunkSize = Math.Min(_allocator.MaxChunkSize, Math.Max(maxChunkSize, proposedChunkSize));
            if (chunkSize > maxChunkSize)
            {
                // Update our stored max chunk size. It's fine that this is racy.
                Volatile.Write(ref _maxChunkSize.Value, chunkSize);
            }
            ByteBuf chunkBuffer = _chunkAllocator.Allocate(chunkSize, chunkSize);
            bool threadLocal = magazine.AllocationLock == null;
            BuddyChunk chunk;
            try
            {
                chunk = new BuddyChunk(chunkBuffer, _chunkRegistry);
                chunk.TrackAllocation(threadLocal);
            }
            catch
            {
                chunkBuffer.Release();
                throw;
            }
            chunk.AttachToMagazine(magazine);
            return chunk;
        }
    }
}
