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
using System.Buffers;
using System.Globalization;

namespace Netty.NET.Buffer.Tests;

public abstract class AbstractByteBufAllocatorTest<T> : ByteBufAllocatorTest where T : AbstractByteBufAllocator
{
    protected abstract override T NewAllocator(bool preferDirect);

    protected abstract T NewUnpooledAllocator();

    // CLR: NativeMemoryOwner always frees native storage deterministically, so
    // PlatformDependent.canReliabilyFreeDirectBuffers() has no CLR counterpart and is always satisfied
    // (src/Netty.NET.Buffer/AbstractByteBufAllocator.cs: IoBuffer always chooses native storage).
    protected override bool IsDirectExpected(bool preferDirect)
    {
        return preferDirect;
    }

    // CLR: AbstractByteBufAllocator.DEFAULT_MAX_CAPACITY is the int.MaxValue default argument of the C# factories.
    protected sealed override int DefaultMaxCapacity()
    {
        return int.MaxValue;
    }

    // CLR: AbstractByteBufAllocator.DEFAULT_MAX_COMPONENTS is the 16 default argument of the C# composite factories.
    protected sealed override int DefaultMaxComponents()
    {
        return 16;
    }

    [Fact]
    public void TestCalculateNewCapacity()
    {
        TestCalculateNewCapacity0(true);
        TestCalculateNewCapacity0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestCalculateNewCapacity test name.
    private void TestCalculateNewCapacity0(bool preferDirect)
    {
        T allocator = NewAllocator(preferDirect);
        // CLR: AbstractByteBufAllocator keeps CALCULATE_THRESHOLD private.
        const int CALCULATE_THRESHOLD = 4 * 1024 * 1024;
        Assert.Equal(8, allocator.CalculateNewCapacity(1, 8));
        Assert.Equal(7, allocator.CalculateNewCapacity(1, 7));
        Assert.Equal(64, allocator.CalculateNewCapacity(1, 129));
        Assert.Equal(CALCULATE_THRESHOLD,
                allocator.CalculateNewCapacity(CALCULATE_THRESHOLD,
                        CALCULATE_THRESHOLD + 1));
        Assert.Equal(CALCULATE_THRESHOLD * 2,
                allocator.CalculateNewCapacity(CALCULATE_THRESHOLD + 1,
                        CALCULATE_THRESHOLD * 4));
        // CLR: ArgumentOutOfRangeException replaces IllegalArgumentException.
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.CalculateNewCapacity(8, 7));

        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.CalculateNewCapacity(-1, 8));
    }

    [Fact]
    public virtual void TestUnsafeHeapBufferAndUnsafeDirectBuffer()
    {
        T allocator = NewUnpooledAllocator();
        ByteBuf directBuffer = allocator.DirectBuffer();
        // CLR: there is no sun.misc.Unsafe subclass split; PlatformDependent.hasUnsafe() is false-equivalent and the
        // managed/native owner types are the only storage classes (src/Netty.NET.Buffer/UnpooledByteBufAllocator.cs,
        // src/Netty.NET.Buffer/PooledByteBufAllocator.cs fallbacks).
        AssertInstanceOf(directBuffer, typeof(UnpooledDirectByteBuf));
        directBuffer.Release();

        ByteBuf heapBuffer = allocator.HeapBuffer();
        AssertInstanceOf(heapBuffer, typeof(UnpooledHeapByteBuf));
        heapBuffer.Release();
    }

    protected static void AssertInstanceOf(ByteBuf buffer, Type clazz)
    {
        // Unwrap if needed
        if (buffer is SimpleLeakAwareByteBuf)
        {
            buffer = buffer.Unwrap();
        }
        Assert.IsAssignableFrom(clazz, buffer);
    }

    protected static void AssertSameBuffer(ByteBuf expected, ByteBuf buffer)
    {
        // Unwrap if needed
        Assert.Same(expected, buffer is SimpleLeakAwareByteBuf ? buffer.Unwrap() : buffer);
    }

    [Fact]
    public void DirectBuffersMustHaveMemoryAddress()
    {
        // The memory address must always be available when we either have Unsafe available,
        // or when we have memory segments available (though CleanerJava25 only enables for Java 25+).
        // CLR: no capability assumption is needed; a scoped Memory<T>.Pin() replaces hasMemoryAddress()/memoryAddress()
        // (https://learn.microsoft.com/en-us/dotnet/api/system.memory-1.pin?view=net-10.0).
        T allocator = NewAllocator(true);
        ByteBuf buf = allocator.DirectBuffer();
        try
        {
            Assert.NotEqual(0, ManagedMemoryTest.Address(buf.AsMemory(0, buf.Capacity)));
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public virtual void TestUsedDirectMemory()
    {
        T allocator =  NewAllocator(true);
        IByteBufAllocatorMetric metric = ((IByteBufAllocatorMetricProvider) allocator).Metric;
        Assert.Equal(0, metric.UsedDirectMemory);
        ByteBuf buffer = allocator.DirectBuffer(1024, 4096);
        int capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory);

        // Double the size of the buffer
        buffer.Capacity = capacity << 1;
        capacity = buffer.Capacity;
        AssertEquals(ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory, buffer.ToString());

        buffer.Release();
        Assert.Equal(ExpectedUsedMemoryAfterRelease(allocator, capacity), metric.UsedDirectMemory);
    }

    [Fact]
    public void TestUsedDirectMemoryHuge()
    {
        T allocator =  NewAllocator(true);
        IByteBufAllocatorMetric metric = ((IByteBufAllocatorMetricProvider) allocator).Metric;
        Assert.Equal(0, metric.UsedHeapMemory);
        int size = 32 * 1024 * 1024;
        ByteBuf buffer = allocator.DirectBuffer(size, size);
        Assert.Equal(size, metric.UsedDirectMemory);
        buffer.Release();
    }

    [Fact]
    public virtual void TestUsedHeapMemory()
    {
        T allocator =  NewAllocator(true);
        IByteBufAllocatorMetric metric = ((IByteBufAllocatorMetricProvider) allocator).Metric;

        Assert.Equal(0, metric.UsedHeapMemory);
        ByteBuf buffer = allocator.HeapBuffer(1024, 4096);
        int capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory);

        // Double the size of the buffer
        buffer.Capacity = capacity << 1;
        capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory);

        buffer.Release();
        Assert.Equal(ExpectedUsedMemoryAfterRelease(allocator, capacity), metric.UsedHeapMemory);
    }

    [Fact]
    public void TestUsedHeapMemoryHuge()
    {
        T allocator =  NewAllocator(true);
        IByteBufAllocatorMetric metric = ((IByteBufAllocatorMetricProvider) allocator).Metric;
        Assert.Equal(0, metric.UsedHeapMemory);
        int size = 32 * 1024 * 1024;
        ByteBuf buffer = allocator.HeapBuffer(size, size);
        Assert.Equal(size, metric.UsedHeapMemory);
        buffer.Release();
    }

    [Fact]
    public virtual void ShouldReuseChunks()
    {
        int bufSize = 1024 * 1024;
        IByteBufAllocator allocator = NewAllocator(false);
        allocator.HeapBuffer(bufSize, bufSize).Release();
        // CLR: GC.GetAllocatedBytesForCurrentThread() replaces the optional com.sun.management.ThreadMXBean
        // getThreadAllocatedBytes(currentThreadId); it is always available, so the original abort/assumptions on its
        // absence or -1 results have no CLR counterpart
        // (https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0).
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; ++i)
        {
            allocator.HeapBuffer(bufSize, bufSize).Release();
        }
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(allocAfter - allocBefore < 8 * 1024 * 1024,
                string.Format(CultureInfo.InvariantCulture,
                        "allocated MB: {0:F3}", (allocAfter - allocBefore) / 1024.0 / 1024.0));
    }

    [Fact]
    public void TestCapacityNotGreaterThanMaxCapacity()
    {
        TestCapacityNotGreaterThanMaxCapacity0(true);
        TestCapacityNotGreaterThanMaxCapacity0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestCapacityNotGreaterThanMaxCapacity test name.
    private void TestCapacityNotGreaterThanMaxCapacity0(bool preferDirect)
    {
        int maxSize = 100000;
        // CLR: the public DirectBuffer factory replaces the protected newDirectBuffer access; it validates the same
        // arguments and then calls NewDirectBuffer (src/Netty.NET.Buffer/AbstractByteBufAllocator.cs).
        ByteBuf buf = NewAllocator(preferDirect).DirectBuffer(maxSize, maxSize);
        try
        {
            // CLR: ArgumentOutOfRangeException replaces IllegalArgumentException.
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buf.Capacity = maxSize + 1;
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetBytesFromOwnInternalNioBuffer()
    {
        T allocator = NewAllocator(true);
        int size = 8;
        ByteBuf directBuffer = allocator.DirectBuffer(size, size);
        try
        {
            for (int i = 0; i < size; ++i)
            {
                directBuffer.SetByte(i, i);
            }
            int srcStartIndex = size / 2;
            // The source is this buffer's own internal NIO buffer.
            // CLR: AsReadOnlyMemory replaces internalNioBuffer(index, length) (jvm-nio-platform; src/Netty.NET.Buffer/ByteBuf.ReadOnly.cs).
            ReadOnlyMemory<byte> srcByteBuf = directBuffer.AsReadOnlyMemory(srcStartIndex, size - srcStartIndex);
            directBuffer.SetBytes(0, srcByteBuf.Span);
            for (int i = 0; i < srcStartIndex; i++)
            {
                Assert.Equal((byte) (srcStartIndex + i), directBuffer.GetByte(i));
            }
            for (int i = srcStartIndex; i < size; i++)
            {
                Assert.Equal((byte) i, directBuffer.GetByte(i));
            }
        }
        finally
        {
            directBuffer.Release();
        }
    }

    protected virtual long ExpectedUsedMemory(T allocator, int capacity)
    {
        return capacity;
    }

    protected virtual long ExpectedUsedMemoryAfterRelease(T allocator, int capacity)
    {
        return 0;
    }

    protected virtual void TrimCaches(T allocator)
    {
    }

    // CLR: xUnit's Assert.Equal has no message overload for integers; this keeps the original
    // assertEquals(expected, actual, message) diagnostic.
    protected static void AssertEquals(long expected, long actual, string message)
    {
        Assert.True(expected == actual, message + " ==> expected: <" + expected.ToString(CultureInfo.InvariantCulture) +
                "> but was: <" + actual.ToString(CultureInfo.InvariantCulture) + ">");
    }
}
