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
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ThreadLocalDirectBufferTest
{
    [Theory]
    [InlineData(1, 256, false)] [InlineData(256, 256, true)]
    [InlineData(512, 512, true)] [InlineData(512, 513, false)]
    public void CapacityThresholdPreservesStoredBytesButUsesFreshWrappersAndIndices(int limit, int capacity, bool reused)
    {
        var native = new NativeMemoryAllocator();
        using var cache = new ThreadLocalDirectBufferCache(limit, native);
        ByteBuf first = cache.Rent(); first.Capacity = capacity;
        Assert.True(first.IsDirect); Assert.True(first.IsContiguous);
        Assert.Same(UnpooledByteBufAllocator.Default, first.Allocator);
        Assert.Equal(int.MaxValue, first.MaxCapacity);
        first.SetByte(0, 73).SetIndex(2, 5).MarkReaderIndex().MarkWriterIndex();
        ByteBuf view = first.Slice(0, 8);
        Memory<byte> memory = first.AsMemory(0, 8);
        Assert.True(first.Release());
        Assert.Equal(reused ? capacity : 0, native.ReservedBytes);
        ByteBuf next = cache.Rent();
        try
        {
            Assert.NotSame(first, next); Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, view.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => first.Retain());
            Assert.Throws<IllegalReferenceCountException>(() => view.GetByte(0));
            Assert.Throws<ObjectDisposedException>(() => memory.Span.ToArray());
            Assert.Equal(reused ? capacity : 256, next.Capacity);
            Assert.Equal(reused ? 73 : 0, next.GetByte(0));
            Assert.Equal(0, next.ReaderIndex); Assert.Equal(0, next.WriterIndex);
            next.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(0, next.WriterIndex);
        }
        finally { next.Release(); cache.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void OnlyOneIdleOwnerIsRetainedAndLiveBuffersNeverAlias()
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf first = cache.Rent(), second = cache.Rent();
        first.WriteByte(1); second.WriteByte(2);
        Assert.Equal(512, native.ReservedBytes);
        first.Release(); second.Release(); Assert.Equal(256, native.ReservedBytes);
        ByteBuf third = cache.Rent(); Assert.Equal(1, third.GetByte(0)); third.Release();
        cache.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public unsafe void PinsAndInternalLeasesDelayReuseUntilTheLastClaim(bool lease)
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent().WriteByte(72);
        Memory<byte> memory = b.AsMemory(0, 1);
        MemoryHandle pin = lease ? default : memory.Pin();
        BufferMemoryLease claim = lease ? b.AcquireReadLease() : default;
        b.Release();
        Assert.Throws<ObjectDisposedException>(() => memory.Span.ToArray());
        ByteBuf other = cache.Rent(); other.SetByte(0, 22);
        Assert.Equal(512, native.ReservedBytes);
        if (!lease) Assert.Equal(72, *((byte*)pin.Pointer));
        var copiedPin = pin; pin.Dispose(); copiedPin.Dispose(); claim.Dispose();
        Assert.Equal(512, native.ReservedBytes);
        other.Release(); Assert.Equal(256, native.ReservedBytes);
        ByteBuf reused = cache.Rent(); Assert.Equal(72, reused.GetByte(0)); reused.Release();
        cache.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public unsafe void ResizeRetiresOldStorageAndPreservesPinnedBytesWithoutCachingIt()
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent().WriteByte(72);
        Memory<byte> old = b.AsMemory(0, 1); MemoryHandle pin = old.Pin();
        b.Capacity = 512; b.SetByte(0, 22);
        Assert.Equal(768, native.ReservedBytes); Assert.Equal(72, *((byte*)pin.Pointer));
        Assert.Throws<ObjectDisposedException>(() => old.Span.ToArray());
        pin.Dispose(); Assert.Equal(512, native.ReservedBytes);
        b.Release(); ByteBuf next = cache.Rent(); Assert.Equal(512, next.Capacity); Assert.Equal(22, next.GetByte(0));
        next.Release(); cache.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void GrowthFailureKeepsStorageBytesIndicesMarksAndCacheUsable()
    {
        var native = new NativeMemoryAllocator(512); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent().WriteByte(72).MarkWriterIndex();
        Assert.Throws<OutOfMemoryException>(() => b.Capacity = 512);
        Assert.Equal(256, b.Capacity); Assert.Equal(72, b.GetByte(0)); Assert.Equal(1, b.WriterIndex);
        b.ResetWriterIndex(); Assert.Equal(1, b.WriterIndex);
        b.Release(); ByteBuf next = cache.Rent(); Assert.Equal(72, next.GetByte(0)); next.Release();
        cache.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ScopeRemovalDrainsIdleButPreservesLiveBuffersAndLateReturns()
    {
        var native = new NativeMemoryAllocator(); var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf first = cache.Rent(), live = cache.Rent().WriteByte(71);
        first.Release(); cache.Dispose(); Assert.Equal(256, native.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => cache.Rent());
        Assert.Equal(71, live.ReadByte()); live.Release(); Assert.Equal(0, native.ReservedBytes);
        cache.Dispose();
    }

    [Fact]
    public void CrossThreadReturnFreesStorageAndCrossThreadRentIsRejected()
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent(); Exception error = null;
        var thread = new Thread(() =>
        {
            try { Assert.Throws<InvalidOperationException>(() => cache.Rent()); b.Release(); }
            catch (Exception e) { error = e; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(error);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void DerivedOwnershipDelaysReturnAndCopyUsesCreatorAllocator()
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent().WriteInt(1234), retained = b.RetainedSlice(0, 4), copy = b.Copy(0, 4);
        try
        {
            Assert.Same(b.Allocator, copy.Allocator); Assert.True(copy.IsDirect); Assert.Equal(1234, copy.GetInt(0));
            Assert.False(b.Release()); Assert.Equal(1234, retained.GetInt(0));
            ByteBuf other = cache.Rent(); Assert.Equal(512, native.ReservedBytes); other.Release();
            retained.Release(); Assert.Equal(256, native.ReservedBytes);
        }
        finally { if (b.ReferenceCount != 0) b.Release(b.ReferenceCount); copy.Release(); cache.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void SelfAliasedGrowthUsesOperationLeaseBeforeRetiringStorage()
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent(); for (int i = 0; i < 256; i++) b.WriteByte(i);
        b.WriteBytes(b.AsReadOnlySpan(0, 64));
        Assert.Equal(320, b.WriterIndex);
        for (int i = 0; i < 64; i++) Assert.Equal((byte)i, b.GetByte(256 + i));
        Assert.Equal(b.Capacity, native.ReservedBytes);
        b.Release(); cache.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosingCacheBeforeCrossThreadPinOrLeaseReturnNeverReusesStorage(bool lease)
    {
        var native = new NativeMemoryAllocator(); using var cache = new ThreadLocalDirectBufferCache(512, native);
        ByteBuf b = cache.Rent();
        MemoryHandle pin = lease ? default : b.AsMemory(0, 1).Pin();
        BufferMemoryLease claim = lease ? b.AcquireReadLease() : default;
        b.Release(); cache.Dispose(); Assert.Equal(256, native.ReservedBytes);
        var worker = new Thread(() => { pin.Dispose(); claim.Dispose(); });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedCacheMissCanRetryWithoutLosingTheLiveOwner()
    {
        var native = new NativeMemoryAllocator(512); using var cache = new ThreadLocalDirectBufferCache(512, native);
        using var blocker = native.Allocate(256);
        ByteBuf live = cache.Rent().WriteByte(77);
        Assert.Throws<OutOfMemoryException>(() => cache.Rent());
        Assert.Equal(512, native.ReservedBytes); Assert.Equal(77, live.GetByte(0));
        blocker.Dispose(); ByteBuf retry = cache.Rent();
        Assert.Equal(0, retry.GetByte(0)); Assert.Equal(512, native.ReservedBytes);
        live.Release(); retry.Release(); cache.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }
}
