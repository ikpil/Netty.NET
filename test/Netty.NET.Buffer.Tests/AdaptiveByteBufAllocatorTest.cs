/*
 * Copyright 2024 The Netty Project
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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveByteBufAllocatorTest : AbstractByteBufAllocatorTest<AdaptiveByteBufAllocator>
{
    protected override AdaptiveByteBufAllocator NewAllocator(bool preferDirect)
    {
        return Track(new AdaptiveByteBufAllocator(preferDirect));
    }

    protected override AdaptiveByteBufAllocator NewUnpooledAllocator()
    {
        return NewAllocator(false);
    }

    protected override long ExpectedUsedMemory(AdaptiveByteBufAllocator allocator, int capacity)
    {
        return 128 * 1024; // Min chunk size
    }

    protected override long ExpectedUsedMemoryAfterRelease(AdaptiveByteBufAllocator allocator, int capacity)
    {
        return 128 * 1024; // Min chunk size
    }

    // CLR: shared helper for tests that inspect the adaptive root buffer under derived/leak-aware wrappers.
    internal static AdaptiveByteBuf Root(ByteBuf buffer)
    {
        while (buffer is not AdaptiveByteBuf) buffer = buffer.Unwrap();
        return (AdaptiveByteBuf)buffer;
    }

    [Fact]
    public override void TestUnsafeHeapBufferAndUnsafeDirectBuffer()
    {
        AdaptiveByteBufAllocator allocator = NewUnpooledAllocator();
        ByteBuf directBuffer = allocator.DirectBuffer();
        AssertInstanceOf(directBuffer, typeof(AdaptivePoolingAllocator.AdaptiveByteBuf));
        Assert.True(directBuffer.IsDirect);
        directBuffer.Release();

        ByteBuf heapBuffer = allocator.HeapBuffer();
        AssertInstanceOf(heapBuffer, typeof(AdaptivePoolingAllocator.AdaptiveByteBuf));
        Assert.False(heapBuffer.IsDirect);
        heapBuffer.Release();
    }

    [Fact]
    public override void TestUsedDirectMemory()
    {
        AdaptiveByteBufAllocator allocator =  NewAllocator(true);
        IByteBufAllocatorMetric metric = allocator.Metric;
        Assert.Equal(0, metric.UsedDirectMemory);
        ByteBuf buffer = allocator.DirectBuffer(1024, 4096);
        int capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory);

        // Double the size of the buffer
        buffer.Capacity = capacity << 1;
        capacity = buffer.Capacity;
        // This is a new size class, and a new magazine with a new chunk
        AssertEquals(2 * ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory, buffer.ToString());

        buffer.Release();
        // Memory is still held by the magazines
        Assert.Equal(2 * ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory);
    }

    [Fact]
    public override void TestUsedHeapMemory()
    {
        AdaptiveByteBufAllocator allocator =  NewAllocator(true);
        IByteBufAllocatorMetric metric = allocator.Metric;
        Assert.Equal(0, metric.UsedHeapMemory);
        ByteBuf buffer = allocator.HeapBuffer(1024, 4096);
        int capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory);

        // Double the size of the buffer
        buffer.Capacity = capacity << 1;
        capacity = buffer.Capacity;
        // This is a new size class, and a new magazine with a new chunk
        AssertEquals(2 * ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory, buffer.ToString());

        buffer.Release();
        // Memory is still held by the magazines
        Assert.Equal(2 * ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory);
    }

    [Fact]
    public void AdaptiveChunkMustDeallocateOrReuseWthBufferRelease()
    {
        AdaptiveByteBufAllocator allocator = NewAllocator(false);
        // CLR: a LinkedList used through AddLast/RemoveFirst replaces the ArrayDeque used through add/pop.
        LinkedList<ByteBuf> bufs = new LinkedList<ByteBuf>();
        Assert.Equal(0, allocator.UsedHeapMemory);
        Assert.Equal(0, allocator.UsedHeapMemory);
        bufs.AddLast(allocator.HeapBuffer(256));
        long usedHeapMemory = allocator.UsedHeapMemory;
        int buffersPerChunk = checked((int)(usedHeapMemory / 256));
        for (int i = 0; i < buffersPerChunk; i++)
        {
            bufs.AddLast(allocator.HeapBuffer(256));
        }
        Assert.Equal(2 * usedHeapMemory, allocator.UsedHeapMemory);
        Pop(bufs).Release();
        Assert.Equal(2 * usedHeapMemory, allocator.UsedHeapMemory);
        while (bufs.Count != 0)
        {
            Pop(bufs).Release();
        }
        Assert.Equal(2 * usedHeapMemory, allocator.UsedHeapMemory);
        for (int i = 0; i < 2 * buffersPerChunk; i++)
        {
            bufs.AddLast(allocator.HeapBuffer(256));
        }
        Assert.Equal(2 * usedHeapMemory, allocator.UsedHeapMemory);
        while (bufs.Count != 0)
        {
            Pop(bufs).Release();
        }
    }

    // CLR: Deque.pop() removes the first element.
    private static ByteBuf Pop(LinkedList<ByteBuf> bufs)
    {
        ByteBuf first = bufs.First!.Value;
        bufs.RemoveFirst();
        return first;
    }

    [Fact]
    public void GetBytesBoundaryCheckWithFileChannel()
    {
        AdaptiveByteBufAllocator allocator = NewAllocator(false);
        ByteBuf buf = allocator.DirectBuffer(7);
        try
        {
            // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException; a positioned SafeFileHandle replaces
            // FileChannel (src/Netty.NET.Buffer/ByteBuf.Files.cs).
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                // capacity 7，4+8=12 <= maxFastCapacity 32
                buf.GetBytes(4, (SafeFileHandle) null, 0L, 8);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestGetBytesBoundaryCheckWithGatheringByteChannel()
    {
        AdaptiveByteBufAllocator allocator = NewAllocator(false);
        TestGatheringByteChannel channel = new TestGatheringByteChannel();
        ByteBuf buf = allocator.DirectBuffer(7);
        try
        {
            // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException; a connected Socket replaces the
            // GatheringByteChannel (src/Netty.NET.Buffer/ByteBuf.Sockets.cs).
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                // capacity 7，4+8=12 <= maxFastCapacity 32
                buf.GetBytes(4, channel.Socket, 8);
            });
        }
        finally
        {
            channel.Close();
            buf.Release();
        }
    }

    // CLR: AbstractByteBufTest.TestGatheringByteChannel collects writes in memory; GatheringByteChannel maps to a
    // connected Socket in the CLR API, so this stand-in owns a loopback socket pair
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket?view=net-10.0).
    private sealed class TestGatheringByteChannel
    {
        internal Socket Socket { get; }
        private readonly Socket _peer;

        internal TestGatheringByteChannel()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                Socket.Connect(listener.LocalEndPoint!);
                _peer = listener.Accept();
            }
            catch { _peer?.Dispose(); Socket.Dispose(); throw; }
        }

        internal void Close() { _peer.Dispose(); Socket.Dispose(); }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void SliceOrDuplicateUnwrapLetNotEscapeRootParent(bool slice)
    {
        AdaptiveByteBufAllocator allocator = NewAllocator(false);
        ByteBuf buffer = allocator.Buffer(8);
        AssertInstanceOf(buffer, typeof(AdaptivePoolingAllocator.AdaptiveByteBuf));
        // Unwrap if this is wrapped by a leak aware buffer.
        if (buffer is SimpleLeakAwareByteBuf)
        {
            Assert.Null(buffer.Unwrap().Unwrap());
        }
        else
        {
            Assert.Null(buffer.Unwrap());
        }

        ByteBuf derived = slice ? buffer.Slice(0, 4) : buffer.Duplicate();
        // When we unwrap the derived buffer we should get our original buffer of type AdaptiveByteBuf back.
        ByteBuf unwrapped = derived is SimpleLeakAwareByteBuf ?
                derived.Unwrap().Unwrap() : derived.Unwrap();
        AssertInstanceOf(unwrapped, typeof(AdaptivePoolingAllocator.AdaptiveByteBuf));
        AssertSameBuffer(buffer is SimpleLeakAwareByteBuf ? buffer.Unwrap() : buffer, unwrapped);

        ByteBuf retainedDerived = slice ? buffer.RetainedSlice(0, 4) : buffer.RetainedDuplicate();
        // When we unwrap the derived buffer we should get our original buffer of type AdaptiveByteBuf back.
        ByteBuf unwrappedRetained = retainedDerived is SimpleLeakAwareByteBuf ?
                retainedDerived.Unwrap().Unwrap() :  retainedDerived.Unwrap();
        AssertInstanceOf(unwrappedRetained, typeof(AdaptivePoolingAllocator.AdaptiveByteBuf));
        AssertSameBuffer(buffer is SimpleLeakAwareByteBuf ? buffer.Unwrap() : buffer, unwrappedRetained);
        retainedDerived.Release();

        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestAllocateWithoutLock()
    {
        AdaptiveByteBufAllocator alloc = Track(new AdaptiveByteBufAllocator());
        // Make `threadCount` bigger than `AdaptivePoolingAllocator.MAX_STRIPES`, to let thread collision easily happen.
        int threadCount = NettyRuntime.AvailableProcessors() * 4;
        CountdownEvent countDownLatch = new CountdownEvent(threadCount);
        Exception throwableAtomicReference = null;
        for (int i = 0; i < threadCount; i++)
        {
            new Thread(() =>
            {
                for (int j = 0; j < 1024; j++)
                {
                    try
                    {
                        ByteBuf buffer = null;
                        try
                        {
                            buffer = alloc.HeapBuffer(128);
                            // CLR: Random.Shared replaces ThreadLocalRandom.current().
                            buffer.EnsureWritable(Random.Shared.Next(512, 32769));
                        }
                        finally
                        {
                            if (buffer != null)
                            {
                                buffer.Release();
                            }
                        }
                    }
                    catch (Exception t)
                    {
                        Volatile.Write(ref throwableAtomicReference, t);
                    }
                }
                countDownLatch.Signal();
            }).Start();
        }
        countDownLatch.Wait(TestContext.Current.CancellationToken);
        Exception throwable = Volatile.Read(ref throwableAtomicReference);
        if (throwable != null)
        {
            Assert.Fail("Expected no exception, but got" + Environment.NewLine + throwable);
        }
    }

    // CLR: @RepeatedTest(100) becomes a theory over the 1-based repetition numbers. @DisabledForSlowLeakDetection only
    // skips the original under paranoid leak detection; the CLR test always runs.
    public static IEnumerable<object[]> BuddyAllocationConsistencyRepetitions()
    {
        return Enumerable.Range(1, 100).Select(repetition => new object[] { repetition });
    }

    [Theory]
    [MemberData(nameof(BuddyAllocationConsistencyRepetitions))]
    public void BuddyAllocationConsistency(int currentRepetition)
    {
        // CLR: System.Random seeded with the repetition number replaces SplittableRandom(getCurrentRepetition()).
        Random rng = new Random(currentRepetition);
        AdaptiveByteBufAllocator allocator = NewAllocator(true);
        int small = 32768;
        int large = 2 * small;
        int xlarge = 2 * large;

        int[] allocationSizes = {
                small, small, small, small, small, small, small, small,
                large, large, large, large,
                xlarge, xlarge,
        };

        Shuffle(rng, allocationSizes);

        ByteBuf[] bufs = new ByteBuf[allocationSizes.Length];
        for (int i = 0; i < bufs.Length; i++)
        {
            bufs[i] = allocator.Buffer(allocationSizes[i], allocationSizes[i]);
        }

        Shuffle(rng, bufs);

        int[] reallocations = new int[bufs.Length / 2];
        for (int i = 0; i < reallocations.Length; i++)
        {
            reallocations[i] = bufs[i].Capacity;
            bufs[i].Release();
            bufs[i] = null;
        }
        for (int i = 0; i < reallocations.Length; i++)
        {
            Assert.Null(bufs[i]);
            bufs[i] = allocator.Buffer(reallocations[i], reallocations[i]);
        }

        for (int i = 0; i < bufs.Length; i++)
        {
            while (bufs[i].IsWritable())
            {
                bufs[i].WriteByte(i + 1);
            }
        }
        try
        {
            for (int i = 0; i < bufs.Length; i++)
            {
                while (bufs[i].IsReadable())
                {
                    int b = bufs[i].ReadByte();
                    if (b != i + 1)
                    {
                        Assert.Fail("Expected byte " + (i + 1).ToString(CultureInfo.InvariantCulture) +
                                " at index " + (bufs[i].ReaderIndex - 1).ToString(CultureInfo.InvariantCulture) +
                                " but got " + b.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }
        finally
        {
            foreach (ByteBuf buf in bufs)
            {
                buf.Release();
            }
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PurgeScanShouldEvictIdleChunks(bool threadLocal)
    {
        // Thread-local magazines require FastThreadLocalThread
        // (allocate() checks currentThreadWillCleanupFastThreadLocals)
        AdaptiveByteBufAllocator allocator = Track(new AdaptiveByteBufAllocator(false, threadLocal));
        long purgePolls = threadLocal ?
                PoolSetting<long>(allocator, "_chunkPurgePollsThreadLocal") :
                PoolSetting<long>(allocator, "_chunkPurgePollsShared");
        Action test = () => AssertPurgeScanEvictsIdleChunks(allocator, purgePolls);
        if (threadLocal)
        {
            FastThreadLocalThread.RunWithFastThreadLocal(test);
        }
        else
        {
            test();
        }
    }

    // CLR: CHUNK_PURGE_POLLS_*, CHUNK_REUSE_QUEUE and CHUNK_PURGE_THRESHOLD are per-pool private settings with the same
    // system-property defaults instead of class statics (src/Netty.NET.Buffer/AdaptivePoolingAllocator.cs); the test
    // reads the values the allocator under test actually uses.
    private static TValue PoolSetting<TValue>(AdaptiveByteBufAllocator allocator, string field)
    {
        return (TValue)typeof(AdaptivePoolingAllocator).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(allocator.HeapPool)!;
    }

    private static void AssertPurgeScanEvictsIdleChunks(AdaptiveByteBufAllocator allocator, long purgePolls)
    {
        ByteBuf probe = allocator.HeapBuffer(256);
        long chunkSize = allocator.UsedHeapMemory;
        int buffersPerChunk = (int) (chunkSize / 256);
        probe.Release();

        int chunkReuseQueue = PoolSetting<int>(allocator, "_chunkReuseQueue");
        int totalChunks = (int) Math.Max(purgePolls, chunkReuseQueue) * 4 + 10;
        int totalBuffers = totalChunks * buffersPerChunk;
        List<ByteBuf> bufs = new List<ByteBuf>(totalBuffers);
        for (int i = 0; i < totalBuffers; i++)
        {
            bufs.Add(allocator.HeapBuffer(256));
        }

        foreach (ByteBuf buf in bufs)
        {
            buf.Release();
        }
        bufs.Clear();
        long memoryAfterRelease = allocator.UsedHeapMemory;

        int threshold = PoolSetting<int>(allocator, "_chunkPurgeThreshold");
        // Account for pollChunk calls burned during setup (one per chunk created)
        // and partition shuffle: with N notEmpty and P polls, each chunk is polled
        // P/N of the time. Epochs advance at rate 1-P/N per cycle. Need enough cycles
        // for the slowest chunk to reach threshold.
        int setupPolls = totalChunks;
        int notEmpty = totalChunks - chunkReuseQueue;
        double advanceRate = 1.0 - (double) purgePolls / notEmpty;
        int cyclesNeeded = advanceRate > 0 ? (int) Math.Ceiling((threshold + 1) / advanceRate) + 2 : threshold + 2;
        int pollsNeeded = setupPolls + (int) (cyclesNeeded * purgePolls);
        for (int poll = 0; poll < pollsNeeded; poll++)
        {
            for (int i = 0; i < buffersPerChunk; i++)
            {
                bufs.Add(allocator.HeapBuffer(256));
            }
            foreach (ByteBuf buf in bufs)
            {
                buf.Release();
            }
            bufs.Clear();
        }

        long memoryAfterPurge = allocator.UsedHeapMemory;
        Assert.True(memoryAfterPurge < memoryAfterRelease,
                "Memory should decrease after purge scans evict idle chunks. " +
                "Before purge: " + memoryAfterRelease.ToString(CultureInfo.InvariantCulture) +
                ", after purge: " + memoryAfterPurge.ToString(CultureInfo.InvariantCulture));
    }

    // CLR: a generic array shuffle replaces the java.lang.reflect.Array based shuffle(SplittableRandom, Object).
    private static void Shuffle<TElement>(Random rng, TElement[] array)
    {
        int len = array.Length;
        for (int i = 0; i < len; i++)
        {
            int n = rng.Next(i, len);
            TElement value = array[i];
            array[i] = array[n];
            array[n] = value;
        }
    }
}

// CLR: supplemental adaptive-allocator contracts that are not part of the original fixture; they run once instead of
// once per concrete AdaptiveByteBufAllocatorTest instance.
public class AdaptiveByteBufAllocatorClrContractTest
{
    private static AdaptiveByteBufAllocator Allocator(bool lowMemory = false, NativeMemoryAllocator native = null,
        int reuseCapacity = 2, long purgePolls = 128, int threshold = 3, int stripes = 4)
    {
        return new(false, lowMemory, native, reuseCapacity, purgePolls, threshold, stripes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void PublicFactoriesPreservePreferenceAllocatorAndMetrics(bool preferDirect, bool lowMemory)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new AdaptiveByteBufAllocator(preferDirect, lowMemory: lowMemory, nativeAllocator: native);
        ByteBuf normal = allocator.Buffer(7, 100), heap = allocator.HeapBuffer(7, 100), direct = allocator.IoBuffer(7, 100);
        try
        {
            Assert.Equal(preferDirect, normal.IsDirect); Assert.False(heap.IsDirect); Assert.True(direct.IsDirect);
            Assert.Same(allocator, normal.Alloc()); Assert.Same(allocator, allocator.Metric);
            Assert.Equal(131072, allocator.UsedHeapMemory); Assert.Equal(131072, allocator.UsedDirectMemory);
            Assert.Equal(131072, native.ReservedBytes); Assert.True(allocator.IsDirectBufferPooled);
            Assert.Null(AdaptiveByteBufAllocatorTest.Root(normal).Unwrap()); Assert.Equal(7, normal.Capacity);
        }
        finally { normal.Release(); heap.Release(); direct.Release(); }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TestUsedMemoryFollowsSizeClassChangesAndRetainedChunks(bool direct)
    {
        using var allocator = Allocator();
        ByteBuf buffer = direct ? allocator.DirectBuffer(1024, 4096) : allocator.HeapBuffer(1024, 4096);
        try
        {
            Assert.Equal(131072, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
            // Double the size of the buffer
            buffer.Capacity = 2048;
            // This is a new size class, and a new magazine with a new chunk
            Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
        }
        finally { buffer.Release(); }
        // Memory is still held by the magazines
        Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AdaptiveChunkReuseForBothStorageKinds(bool direct)
    {
        using var allocator = Allocator(); var live = new List<ByteBuf>();
        ByteBuf NewBuffer()
        {
            return direct ? allocator.DirectBuffer(256) : allocator.HeapBuffer(256);
        }
        try
        {
            for (int i = 0; i < 513; i++) live.Add(NewBuffer());
            Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
            foreach (var b in live) b.Release(); live.Clear();
            for (int i = 0; i < 1024; i++) live.Add(NewBuffer());
            Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
        }
        finally { foreach (var b in live) b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PurgeScanEvictsIdleChunksWithTunedThresholds(bool direct)
    {
        using var allocator = Allocator(reuseCapacity: 2, purgePolls: 4, threshold: 1);
        var live = new List<ByteBuf>();
        ByteBuf NewBuffer()
        {
            return direct ? allocator.DirectBuffer(256, 256) : allocator.HeapBuffer(256, 256);
        }
        try
        {
            for (int i = 0; i < 10 * 512; i++) live.Add(NewBuffer());
            foreach (var b in live) b.Release(); live.Clear();
            long before = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            for (int poll = 0; poll < 128; poll++)
            {
                for (int i = 0; i < 512; i++) live.Add(NewBuffer());
                foreach (var b in live) b.Release(); live.Clear();
            }
            long after = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            Assert.True(after < before, $"before={before}, after={after}");
            Assert.True(after >= 2 * 131072);
        }
        finally { foreach (var b in live) b.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void DedicatedCutoffsAndLowMemoryDoNotRetainFallbackChunks(bool direct, bool lowMemory)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(lowMemory, native);
        int[] sizes = [16897, 32769, 262145, 1048576, 1048577];
        // Buddy reservations are the next power of two; low memory and sizes above the 1 MiB
        // pooled maximum use a dedicated chunk of exactly the requested size.
        int[] buddy = [32768, 65536, 524288, 1048576, 0];
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            long before = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            ByteBuf b = direct ? allocator.DirectBuffer(size, size + 3) : allocator.HeapBuffer(size, size + 3);
            AdaptiveChunkAllocation reservation = AdaptiveByteBufAllocatorTest.Root(b).Allocation;
            bool pooled = !lowMemory && buddy[i] != 0;
            Assert.Equal(pooled, reservation.IsPooled);
            b.SetInt(0, 12345); Assert.Equal(12345, b.GetInt(0)); b.Release();
            long after = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            if (!pooled) Assert.Equal(before, after);
            Assert.Equal(pooled ? buddy[i] : size, reservation.Length);
        }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BuddyAllocationConsistencyForBothStorageKinds(bool direct)
    {
        using var allocator = Allocator(); var random = new Random(91);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            int[] sizes = [32768,32768,32768,32768,32768,32768,32768,32768,65536,65536,65536,65536,131072,131072];
            random.Shuffle(sizes); ByteBuf[] buffers = new ByteBuf[sizes.Length];
            try
            {
                for (int i = 0; i < sizes.Length; i++) buffers[i] = direct ? allocator.DirectBuffer(sizes[i], sizes[i]) : allocator.HeapBuffer(sizes[i], sizes[i]);
                random.Shuffle(buffers);
                for (int i = 0; i < buffers.Length / 2; i++)
                {
                    int size = buffers[i].Capacity; buffers[i].Release(); buffers[i] = null;
                    buffers[i] = direct ? allocator.DirectBuffer(size, size) : allocator.HeapBuffer(size, size);
                }
                for (int i = 0; i < buffers.Length; i++) buffers[i].AsSpan(0, buffers[i].Capacity).Fill((byte)(i + 1));
                for (int i = 0; i < buffers.Length; i++) Assert.True(buffers[i].AsSpan(0, buffers[i].Capacity).IndexOfAnyExcept((byte)(i + 1)) < 0);
            }
            finally { foreach (var buffer in buffers) buffer?.Release(); }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AllocateWithoutLockKeepsRetainedViewsAndCounters(bool lowMemory)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(lowMemory, native);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            var random = new Random(worker);
            for (int i = 0; i < 256; i++)
            {
                ByteBuf b = allocator.DirectBuffer(128, 65536);
                try
                {
                    b.WriteInt(worker); b.EnsureWritable(random.Next(512, 32769)); Assert.Equal(worker, b.GetInt(0));
                    ByteBuf retained = b.RetainedSlice(0, 4); Assert.Equal(2, b.RefCnt); retained.Release();
                }
                finally { b.Release(); }
            }
        }, TestContext.Current.CancellationToken)));
        allocator.Dispose(); Assert.Equal(0, allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(1)] [InlineData(4)]
    public async Task ForcedContentionExpandsStripesOrUsesDedicatedFallback(int stripes)
    {
        using var allocator = Allocator(stripes: stripes);
        Magazine magazine = allocator.HeapPool.SizeClassedMagazineGroups[0].Magazines[0];
        using var locked = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { lock (magazine.AllocationLock) { locked.Set(); release.Wait(); } });
        holder.Start(); locked.Wait(TestContext.Current.CancellationToken);
        try
        {
            Task<ByteBuf> allocation = Task.Run(() => allocator.HeapBuffer(7, 100), TestContext.Current.CancellationToken);
            if (stripes > 1)
            {
                Assert.True(SpinWait.SpinUntil(() => allocator.HeapPool.SizeClassedMagazineGroups[0].Magazines.Length > 1, TimeSpan.FromSeconds(10)));
                release.Set();
            }
            ByteBuf b = await allocation;
            try
            {
                Assert.Equal(stripes == 1 ? 7 : 32, AdaptiveByteBufAllocatorTest.Root(b).Allocation.Length);
                Assert.Equal(stripes == 1 ? 7 : 131072, allocator.UsedHeapMemory);
            }
            finally { b.Release(); }
        }
        finally { release.Set(); Assert.True(holder.Join(TimeSpan.FromSeconds(10))); }
        allocator.Dispose();
        Assert.Equal(0, allocator.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisposePreservesLiveViewsAndLeasesButClosesReallocation(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(native: native);
        ByteBuf b = direct ? allocator.DirectBuffer(7, 100) : allocator.HeapBuffer(7, 100); b.SetInt(0, 12345);
        ByteBuf child = b.RetainedSlice(0, 4); using var lease = child.AcquireReadLease();
        b.Release(); allocator.Dispose(); Assert.Equal(12345, child.GetInt(0));
        AdaptiveByteBuf root = AdaptiveByteBufAllocatorTest.Root(child); root.Capacity = 32;
        Assert.Throws<ObjectDisposedException>(() => root.Capacity = 33);
        Assert.Throws<ObjectDisposedException>(() => allocator.HeapBuffer(1));
        Assert.Throws<ObjectDisposedException>(() => allocator.DirectBuffer(1));
        child.Release(); Assert.Equal(131072, allocator.UsedHeapMemory + allocator.UsedDirectMemory);
        lease.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void NativeAllocationFailureLeavesExistingBuffersUsableAndCountersBalanced()
    {
        var native = new NativeMemoryAllocator(131072); using var allocator = Allocator(native: native);
        ByteBuf b = allocator.DirectBuffer(7, 100); b.SetInt(0, 12345);
        try
        {
            Assert.Throws<OutOfDirectMemoryError>(() => b.Capacity = 33);
            Assert.Equal(7, b.Capacity); Assert.Equal(12345, b.GetInt(0)); Assert.Equal(131072, allocator.UsedDirectMemory);
        }
        finally { b.Release(); }
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeConsolidationUsesTheSameAllocatorAndNativeReservationDomain(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(native: native);
        CompositeByteBuf composite = direct ? allocator.CompositeDirectBuffer(1) : allocator.CompositeHeapBuffer(1);
        try
        {
            ByteBuf first = direct ? allocator.DirectBuffer(4, 100) : allocator.HeapBuffer(4, 100);
            ByteBuf second = direct ? allocator.DirectBuffer(4, 100) : allocator.HeapBuffer(4, 100);
            first.WriteInt(12345); second.WriteInt(67890);
            composite.AddComponent(first, true).AddComponent(second, true);
            Assert.Equal(12345, composite.GetInt(0)); Assert.Equal(67890, composite.GetInt(4));
            Assert.Same(allocator, composite.Alloc()); Assert.Equal(direct, composite.IsDirect);
        }
        finally { composite.Release(); }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }
}
