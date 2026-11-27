/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common;
using static Netty.NET.Buffer.Tests.AllocatorLeakDetectionTest;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

// Selected remaining AbstractByteBufAllocatorTest contracts. CLR storage and
// scoped memory replace the JVM Unsafe subtype and persistent-address APIs.
public class AllocatorStorageContractTest
{
    private sealed class Storage(int provider, bool preferDirect) : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private IByteBufAllocator _allocator;
        public IByteBufAllocator Allocator => _allocator ??= provider switch
        {
            0 => new UnpooledByteBufAllocator(preferDirect, Native),
            1 => new PooledByteBufAllocator(preferDirect, poolHeap: false, poolDirect: false,
                nativeAllocator: Native),
            2 => new PooledByteBufAllocator(preferDirect, nativeAllocator: Native),
            _ => new AdaptiveByteBufAllocator(preferDirect, lowMemory: false, nativeAllocator: Native,
                useCacheForNonEventLoopThreads: false)
        };
        public void Dispose()
        {
            (_allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Providers()
    {
        for (int provider = 0; provider < 4; ++provider)
        foreach (bool preferDirect in new[] { false, true }) yield return [provider, preferDirect];
    }

    public static IEnumerable<object[]> CopyWindows()
    {
        foreach (object[] p in Providers())
        foreach (int window in new[] { 1, 2 }) yield return [p[0], p[1], window];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestUnsafeHeapBufferAndUnsafeDirectBuffer(int provider, bool preferDirect)
    {
        // CLR: managed heap and owned native storage types replace the JVM safe/Unsafe subclasses. Providers 0/1
        // are the original newUnpooledAllocator factories; provider 3 is the AdaptiveByteBufAllocatorTest override.
        using var s = new Storage(provider, preferDirect);
        foreach (bool direct in new[] { true, false })
        {
            ByteBuf buffer = direct ? s.Allocator.DirectBuffer() : s.Allocator.HeapBuffer();
            try
            {
                // Unwrap if needed
                ByteBuf storage = UnwrapLeak(buffer);
                if (provider == 3) Assert.IsType<AdaptiveByteBuf>(storage);
                else if (provider == 2)
                {
                    if (direct) Assert.IsType<PooledDirectByteBuf>(storage);
                    else Assert.IsType<PooledHeapByteBuf>(storage);
                }
                else
                {
                    if (direct) Assert.IsAssignableFrom<UnpooledDirectByteBuf>(storage);
                    else Assert.IsAssignableFrom<UnpooledHeapByteBuf>(storage);
                }
                Assert.Equal(direct, buffer.IsDirect);
                Assert.True(buffer.IsContiguous);
                Assert.Same(s.Allocator, storage.Alloc());
                Assert.Equal(256, buffer.Capacity);
                Assert.Equal(0, buffer.ReaderIndex);
                Assert.Equal(0, buffer.WriterIndex);
                Assert.Equal(1, buffer.RefCnt);
            }
            finally { Assert.True(buffer.Release()); }
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void DirectBuffersMustHaveMemoryAddress(int provider, bool preferDirect)
    {
        using var s = new Storage(provider, preferDirect);
        ByteBuf buffer = s.Allocator.DirectBuffer();
        try
        {
            // Unwrap if needed
            Assert.Same(s.Allocator, UnwrapLeak(buffer).Alloc());
            // The memory address must always be available when we either have Unsafe available,
            // or when we have memory segments available (though CleanerJava25 only enables for Java 25+).
            // CLR: borrow an address only within a pin; no JVM capability check or persistent pointer API.
            Span<byte> bytes = buffer.AsSpan(0, buffer.Capacity);
            Assert.NotEqual(0, ManagedMemoryTest.Address(buffer.AsMemory(0, buffer.Capacity)));
            bytes[0] = 0x5a;
            Assert.Equal(0x5a, buffer.GetByte(0));
            buffer.SetByte(buffer.Capacity - 1, 0xa5);
            Assert.Equal(0xa5, bytes[buffer.Capacity - 1]);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.RefCnt);
        }
        finally { Assert.True(buffer.Release()); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestCapacityNotGreaterThanMaxCapacity(int provider, bool preferDirect)
    {
        using var s = new Storage(provider, preferDirect);
        const int maximum = 100000;
        // CLR: the public DirectBuffer factory replaces the protected newDirectBuffer access.
        ByteBuf buffer = s.Allocator.DirectBuffer(maximum, maximum);
        try
        {
            buffer.SetByte(0, 0x5a).SetByte(maximum - 1, 0xa5);
            long reserved = s.Native.ReservedBytes;
            var metric = ((IByteBufAllocatorMetricProvider)s.Allocator).Metric;
            long used = metric.UsedDirectMemory;
            // CLR: ArgumentOutOfRangeException replaces IllegalArgumentException.
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = maximum + 1);
            // CLR: the rejected resize leaves storage, state, metric and native reservation unchanged.
            Assert.Equal(maximum, buffer.Capacity);
            Assert.Equal(maximum, buffer.MaxCapacity);
            Assert.Equal(0x5a, buffer.GetByte(0));
            Assert.Equal(0xa5, buffer.GetByte(maximum - 1));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.RefCnt);
            Assert.Equal(reserved, s.Native.ReservedBytes);
            Assert.Equal(used, metric.UsedDirectMemory);
        }
        finally { Assert.True(buffer.Release()); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetBytesFromOwnInternalNioBuffer(int provider, bool preferDirect)
    {
        using var s = new Storage(provider, preferDirect);
        int size = 8;
        ByteBuf directBuffer = s.Allocator.DirectBuffer(size, size);
        try
        {
            for (int i = 0; i < size; ++i)
            {
                directBuffer.SetByte(i, i);
            }
            int srcStartIndex = size / 2;
            // The source is this buffer's own internal NIO buffer.
            // CLR: ReadOnlyMemory/Span carries the exact source window without a NIO cursor.
            ReadOnlyMemory<byte> srcByteBuf = directBuffer.AsReadOnlyMemory(srcStartIndex, size - srcStartIndex);
            long reserved = s.Native.ReservedBytes;
            Assert.Same(directBuffer, directBuffer.SetBytes(0, srcByteBuf.Span));
            for (int i = 0; i < srcStartIndex; i++)
            {
                Assert.Equal((byte)(srcStartIndex + i), directBuffer.GetByte(i));
            }
            for (int i = srcStartIndex; i < size; i++)
            {
                Assert.Equal((byte)i, directBuffer.GetByte(i));
            }
            // CLR: capacity, indices, claim and native reservation stay unchanged.
            Assert.Equal(size, directBuffer.Capacity);
            Assert.Equal(size, directBuffer.MaxCapacity);
            Assert.Equal(0, directBuffer.ReaderIndex);
            Assert.Equal(0, directBuffer.WriterIndex);
            Assert.Equal(1, directBuffer.RefCnt);
            Assert.Equal(reserved, s.Native.ReservedBytes);
        }
        finally { Assert.True(directBuffer.Release()); }
    }

    // CLR: supplemental overlapping windows of the own-memory copy, in both directions.
    [Theory]
    [MemberData(nameof(CopyWindows))]
    public void OwnBorrowedMemoryCopiesOverlappingRanges(int provider, bool preferDirect, int window)
    {
        using var s = new Storage(provider, preferDirect);
        ByteBuf buffer = s.Allocator.DirectBuffer(8, 8);
        try
        {
            byte[] original = [0, 1, 2, 3, 4, 5, 6, 7], expected = (byte[])original.Clone();
            (int source, int destination, int length) = window == 1 ? (2, 0, 6) : (0, 2, 6);
            buffer.SetBytes(0, original);
            for (int i = 0; i < length; ++i) expected[destination + i] = original[source + i];
            ReadOnlyMemory<byte> borrowed = buffer.AsReadOnlyMemory(source, length);
            long reserved = s.Native.ReservedBytes;
            Assert.Same(buffer, buffer.SetBytes(destination, borrowed.Span));
            byte[] actual = new byte[8]; buffer.GetBytes(0, actual);
            Assert.Equal(expected, actual);
            Assert.Equal(8, buffer.Capacity);
            Assert.Equal(8, buffer.MaxCapacity);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.RefCnt);
            Assert.Equal(reserved, s.Native.ReservedBytes);
        }
        finally { Assert.True(buffer.Release()); }
    }
}
