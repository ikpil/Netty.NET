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
        foreach (int window in new[] { 0, 1, 2 }) yield return [p[0], p[1], window];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void FactoryStorageMatchesManagedHeapAndOwnedNativePolicy(int provider, bool preferDirect)
    {
        using var s = new Storage(provider, preferDirect);
        foreach (bool direct in new[] { false, true })
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
                Assert.Same(s.Allocator, storage.Allocator);
                Assert.Equal(256, buffer.Capacity);
                Assert.Equal(0, buffer.ReaderIndex);
                Assert.Equal(0, buffer.WriterIndex);
                Assert.Equal(1, buffer.ReferenceCount);
            }
            finally { Assert.True(buffer.Release()); }
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public unsafe void DirectStorageExposesAValidAddressWithinThePinScope(int provider, bool preferDirect)
    {
        using var s = new Storage(provider, preferDirect);
        ByteBuf buffer = s.Allocator.DirectBuffer();
        try
        {
            // Unwrap if needed
            Assert.Same(s.Allocator, UnwrapLeak(buffer).Allocator);
            // The memory address must always be available when we either have Unsafe available,
            // or when we have memory segments available (though CleanerJava25 only enables for Java 25+).
            // CLR: borrow an address only within a pin; no JVM capability check or persistent pointer API.
            using (var pin = buffer.AsMemory(0, buffer.Capacity).Pin())
            {
                Assert.NotEqual((nint)0, (nint)pin.Pointer);
                ((byte*)pin.Pointer)[0] = 0x5a;
                Assert.Equal(0x5a, buffer.GetByte(0));
                buffer.SetByte(buffer.Capacity - 1, 0xa5);
                Assert.Equal(0xa5, ((byte*)pin.Pointer)[buffer.Capacity - 1]);
                Assert.Equal(0, buffer.ReaderIndex);
                Assert.Equal(0, buffer.WriterIndex);
                Assert.Equal(1, buffer.ReferenceCount);
            }
        }
        finally { Assert.True(buffer.Release()); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void CapacityAboveMaximumPreservesTheOriginalStorageAndState(int provider, bool preferDirect)
    {
        using var s = new Storage(provider, preferDirect);
        const int maximum = 100000;
        ByteBuf buffer = s.Allocator.DirectBuffer(maximum, maximum);
        try
        {
            buffer.SetByte(0, 0x5a).SetByte(maximum - 1, 0xa5);
            long reserved = s.Native.ReservedBytes;
            var metric = ((IByteBufAllocatorMetricProvider)s.Allocator).Metric;
            long used = metric.UsedDirectMemory;
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = maximum + 1);
            Assert.Equal(maximum, buffer.Capacity);
            Assert.Equal(maximum, buffer.MaxCapacity);
            Assert.Equal(0x5a, buffer.GetByte(0));
            Assert.Equal(0xa5, buffer.GetByte(maximum - 1));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(reserved, s.Native.ReservedBytes);
            Assert.Equal(used, metric.UsedDirectMemory);
        }
        finally { Assert.True(buffer.Release()); }
    }

    [Theory]
    [MemberData(nameof(CopyWindows))]
    public void OwnBorrowedMemoryCopiesTheOriginalWindowAndOverlappingRanges(int provider, bool preferDirect, int window)
    {
        using var s = new Storage(provider, preferDirect);
        ByteBuf buffer = s.Allocator.DirectBuffer(8, 8);
        try
        {
            byte[] original = [0, 1, 2, 3, 4, 5, 6, 7], expected = (byte[])original.Clone();
            (int source, int destination, int length) = window switch
            {
                0 => (4, 0, 4), 1 => (2, 0, 6), _ => (0, 2, 6)
            };
            buffer.SetBytes(0, original);
            for (int i = 0; i < length; ++i) expected[destination + i] = original[source + i];
            // The source is this buffer's own internal NIO buffer.
            // CLR: ReadOnlyMemory/Span carries the exact source window without a NIO cursor.
            ReadOnlyMemory<byte> borrowed = buffer.AsReadOnlyMemory(source, length);
            long reserved = s.Native.ReservedBytes;
            Assert.Same(buffer, buffer.SetBytes(destination, borrowed.Span));
            byte[] actual = new byte[8]; buffer.GetBytes(0, actual);
            Assert.Equal(expected, actual);
            Assert.Equal(8, buffer.Capacity);
            Assert.Equal(8, buffer.MaxCapacity);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(reserved, s.Native.ReservedBytes);
        }
        finally { Assert.True(buffer.Release()); }
    }
}
