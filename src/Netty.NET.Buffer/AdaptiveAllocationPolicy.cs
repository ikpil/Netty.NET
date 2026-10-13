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
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: these are the pinned allocator's size-class and chunk-controller decisions.
// Magazine/cache ownership and AdaptiveByteBuf integration are separate porting units.
internal sealed class AdaptiveAllocationPolicy
{
    /**
     * The 128 KiB minimum chunk size is chosen to encourage the system allocator to delegate to mmap for chunk
     * allocations. For instance, glibc will do this.
     * This pushes any fragmentation from chunk size deviations off physical memory, onto virtual memory,
     * which is a much, much larger space. Chunks are also allocated in whole multiples of the minimum
     * chunk size, which itself is a whole multiple of popular page sizes like 4 KiB, 16 KiB, and 64 KiB.
     */
    internal const int MinChunkSize = 128 * 1024;
    // The current fixed-class controller can produce chunks that are not multiples of this minimum.
    private const int BuffersPerChunk = 8; // For large buffers, aim to have about this many buffers per chunk.
    // To amortize activation/deactivation of chunks, we should have a minimum number of segments per chunk.
    // We choose 32 because it seems neither too small nor too big.
    // For segments of 16 KiB, the chunks will be half a megabyte.
    private const int MinSegmentsPerChunk = 32;

    /**
     * The size classes are chosen based on the following observation:
     * <p>
     * Most allocations, particularly ones above 256 bytes, aim to be a power-of-2. However, many use cases, such
     * as framing protocols, are themselves operating or moving power-of-2 sized payloads, to which they add a
     * small amount of overhead, such as headers or checksums.
     * This means we seem to get a lot of mileage out of having both power-of-2 sizes, and power-of-2-plus-a-bit.
     * <p>
     * On the conflicting requirements of both having as few chunks as possible, and having as little wasted
     * memory within each chunk as possible, this seems to strike a surprisingly good balance for the use cases
     * tested so far.
     */
    private static readonly int[] SizeClasses = [
            32,
            64,
            128,
            256,
            512,
            640, // 512 + 128
            1024,
            1152, // 1024 + 128
            2048,
            2304, // 2048 + 256
            4096,
            4352, // 4096 + 256
            8192,
            8704, // 8192 + 512
            16384,
            16896, // 16384 + 512
    ];

    private static readonly byte[] SizeIndexes = CreateSizeIndexes();
    private int _maxBuddyChunkSize;
    internal bool LowMemory { get; }
    /**
     * The maximum size of a pooled chunk, in bytes. Allocations bigger than this will never be pooled.
     * <p>
     * This number is 8 MiB, and is derived from the limitations of internal histograms.
     */
    internal int MaxChunkSize { get; }
    // The original histogram rationale is retained above; the pinned implementation
    // uses fixed classes and a remembered maximum for buddy chunks.
    internal int MaxPooledBufferSize => MaxChunkSize / BuffersPerChunk;

    internal AdaptiveAllocationPolicy(bool lowMemory)
    {
        LowMemory = lowMemory;
        MaxChunkSize = lowMemory
            ? 2 * 1024 * 1024 // 2 MiB for systems with small heaps.
            : 8 * 1024 * 1024; // 8 MiB.
    }

    private static byte[] CreateSizeIndexes()
    {
        var indexes = new byte[SizeClasses[^1] / 32 + 1];
        int lastIndex = 0;
        for (int i = 0; i < SizeClasses.Length; i++)
        {
            //noinspection ConstantValue
            System.Diagnostics.Debug.Assert((SizeClasses[i] & 31) == 0, "Size class must be a multiple of 32");
            int nextIndex = SizeIndexOf(SizeClasses[i]);
            Array.Fill(indexes, (byte)i, lastIndex + 1, nextIndex - lastIndex);
            lastIndex = nextIndex;
        }
        return indexes;
    }
    private static int SizeIndexOf(int size)
    {
        // this is aligning the size to the next multiple of 32 and dividing by 32 to get the size index.
        // CLR widens before addition so valid nonnegative input is safe in checked builds.
        return (int)(((long)size + 31) >> 5);
    }
    internal static int[] GetSizeClasses() => (int[])SizeClasses.Clone();
    internal static int SizeClassIndexOf(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        int index = SizeIndexOf(size);
        return index < SizeIndexes.Length ? SizeIndexes[index] : SizeClasses.Length;
    }
    internal bool IsPooled(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        return size <= MaxPooledBufferSize && (SizeClassIndexOf(size) < SizeClasses.Length || !LowMemory);
    }
    /**
     * Compute the "fast max capacity" value for the buffer.
     */
    internal int ComputeBufferCapacity(int requestedSize, int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestedSize);
        if (maxCapacity < requestedSize) throw new ArgumentOutOfRangeException(nameof(maxCapacity));
        if (!IsPooled(requestedSize)) return requestedSize;
        int index = SizeClassIndexOf(requestedSize);
        // Buddy reservations stay powers of two even when the buffer maximum is smaller.
        // The public MaxFastWritableBytes contract clamps that reservation to maxCapacity.
        return index < SizeClasses.Length ? Math.Min(SizeClasses[index], maxCapacity)
            : MathUtil.SafeFindNextPositivePowerOfTwo(requestedSize);
    }
    internal int ComputeChunkSize(int promptingSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(promptingSize);
        if (!IsPooled(promptingSize)) return promptingSize;
        int index = SizeClassIndexOf(promptingSize);
        if (index < SizeClasses.Length) return Math.Max(MinChunkSize, SizeClasses[index] * MinSegmentsPerChunk);
        int previous = Volatile.Read(ref _maxBuddyChunkSize);
        int proposed = MathUtil.SafeFindNextPositivePowerOfTwo(BuffersPerChunk * promptingSize);
        int size = Math.Min(MaxChunkSize, Math.Max(previous, proposed));
        if (size > previous)
        {
            // Update our stored max chunk size. It's fine that this is racy.
            Volatile.Write(ref _maxBuddyChunkSize, size);
        }
        return size;
    }
}
