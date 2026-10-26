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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufBoundaryTest : IDisposable
{
    private readonly NativeMemoryAllocator _native = new();
    private readonly UnpooledByteBufAllocator _allocator;

    public CompositeByteBufBoundaryTest() => _allocator = new UnpooledByteBufAllocator(false, _native);

    public void Dispose()
    {
        Assert.Equal(0, _allocator.Metric.UsedHeapMemory);
        Assert.Equal(0, _allocator.Metric.UsedDirectMemory);
        Assert.Equal(0, _native.ReservedBytes);
    }

    [Fact]
    public void TestAddComponentWithNullEntry()
    {
        // See https://github.com/netty/netty/issues/11612
        ByteBuf buffer = _allocator.HeapBuffer(8).WriteZero(8);
        CompositeByteBuf compositeByteBuf = _allocator.CompositeHeapBuffer(int.MaxValue);
        try
        {
            compositeByteBuf.AddComponents(new ByteBuf[] { buffer, null }, true);
            Assert.Equal(8, compositeByteBuf.ReadableBytes);
            Assert.Equal(1, compositeByteBuf.NumComponents);
        }
        finally
        {
            compositeByteBuf.Release();
        }

    }

    [Fact]
    public void TestOverflowWhileAddingComponent()
    {
        int capacity = 1024 * 1024; // 1MB
        ByteBuf buffer = _allocator.HeapBuffer(capacity).WriteZero(capacity);
        CompositeByteBuf compositeByteBuf = _allocator.CompositeHeapBuffer(int.MaxValue);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                for (int i = 0; i >= 0; i = unchecked(i + buffer.ReadableBytes))
                {
                    ByteBuf duplicate = buffer.Duplicate();
                    compositeByteBuf.AddComponent(duplicate);
                    duplicate.Retain();
                }
            });
        }
        finally
        {
            compositeByteBuf.Release();
            // CLR preflight leaves a rejected array input caller-owned; single insertion can consume it.
            if (buffer.ReferenceCount > 0)
                buffer.Release();
        }

    }

    [Fact]
    public void TestOverflowWhileAddingComponentsViaVarargs()
    {
        int capacity = 1024 * 1024; // 1MB
        ByteBuf buffer = _allocator.HeapBuffer(capacity).WriteZero(capacity);
        CompositeByteBuf compositeByteBuf = _allocator.CompositeHeapBuffer(int.MaxValue);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                for (int i = 0; i >= 0; i = unchecked(i + buffer.ReadableBytes))
                {
                    ByteBuf duplicate = buffer.Duplicate();
                    compositeByteBuf.AddComponents(duplicate);
                    duplicate.Retain();
                }
            });
        }
        finally
        {
            compositeByteBuf.Release();
            // CLR preflight leaves a rejected array input caller-owned; single insertion can consume it.
            if (buffer.ReferenceCount > 0)
                buffer.Release();
        }

    }

    [Fact]
    public void TestOverflowWhileAddingComponentsViaIterable()
    {
        int capacity = 1024 * 1024; // 1MB
        ByteBuf buffer = _allocator.HeapBuffer(capacity).WriteZero(capacity);
        CompositeByteBuf compositeByteBuf = _allocator.CompositeHeapBuffer(int.MaxValue);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                for (int i = 0; i >= 0; i = unchecked(i + buffer.ReadableBytes))
                {
                    ByteBuf duplicate = buffer.Duplicate();
                    compositeByteBuf.AddComponents((IEnumerable<ByteBuf>)new[] { duplicate });
                    duplicate.Retain();
                }
            });
        }
        finally
        {
            compositeByteBuf.Release();
            // CLR preflight leaves a rejected array input caller-owned; single insertion can consume it.
            if (buffer.ReferenceCount > 0)
                buffer.Release();
        }

    }

    [Fact]
    public void TestOverflowWhileUseConstructorWithOffset()
    {
        int capacity = 1024 * 1024; // 1MB
        ByteBuf buffer = _allocator.HeapBuffer(capacity).WriteZero(capacity);
        List<ByteBuf> buffers = new List<ByteBuf>();
        for (long i = 0; i <= int.MaxValue; i += capacity)
        {
            buffers.Add(buffer.Duplicate());
        }
        // Add one more
        buffers.Add(buffer.Duplicate());

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                ByteBuf[] bufferArray = buffers.ToArray();
                new CompositeByteBuf(_allocator, false, int.MaxValue, bufferArray, 0);
            });
        }
        finally
        {
            buffer.Release();
        }

    }

    [Fact]
    public void TestNotOverflowWhileUseConstructorWithOffset()
    {
        int capacity = 1024 * 1024; // 1MB
        ByteBuf buffer = _allocator.HeapBuffer(capacity).WriteZero(capacity);
        List<ByteBuf> buffers = new List<ByteBuf>();
        for (long i = 0; i <= int.MaxValue; i += capacity)
        {
            buffers.Add(buffer.Duplicate());
        }
        // Add one more
        buffers.Add(buffer.Duplicate());

        ByteBuf[] bufferArray = buffers.ToArray();
        CompositeByteBuf compositeByteBuf =
                new CompositeByteBuf(_allocator, false, int.MaxValue, bufferArray, bufferArray.Length - 1);
        compositeByteBuf.Release();

    }

    [Fact]
    public void TestDiscardReadComponentsWithOffsetSourceBuffersDoesNotCorruptNextRead()
    {
        // See https://github.com/netty/netty/issues/16799
        int bufferSize = 32768;
        byte[] sourceData = new byte[2 * bufferSize];
        CompositeByteBuf composite = _allocator.CompositeDirectBuffer();

        try
        {
            Array.Fill(sourceData, (byte)0x01);
            ByteBuf buffer1 = _allocator.DirectBuffer(2 * bufferSize);
            buffer1.WriteBytes(sourceData.AsSpan(0, 9));
            buffer1.SkipBytes(9);
            buffer1.WriteBytes(sourceData.AsSpan(0, bufferSize));
            composite.AddFlattenedComponents(buffer1, true);
            Assert.Equal(bufferSize, composite.ReadableBytes);

            Array.Fill(sourceData, (byte)0x02);
            ByteBuf buffer2 = _allocator.DirectBuffer(2 * bufferSize);
            buffer2.WriteBytes(sourceData.AsSpan(0, 32781));
            buffer2.SkipBytes(32781);
            buffer2.WriteBytes(sourceData.AsSpan(0, bufferSize));
            composite.AddFlattenedComponents(buffer2, true);
            Assert.Equal(2 * bufferSize, composite.ReadableBytes);

            composite.SkipBytes(bufferSize + 9);
            Assert.Equal((byte)0x02, composite.ReadByte());
            composite.SkipBytes(22519);
            composite.DiscardReadComponents();

            Array.Fill(sourceData, (byte)0x03);
            ByteBuf buffer3 = _allocator.DirectBuffer(bufferSize);
            buffer3.WriteBytes(sourceData.AsSpan(0, bufferSize));
            composite.AddFlattenedComponents(buffer3, true);

            Array.Fill(sourceData, (byte)0x04);
            ByteBuf buffer4 = _allocator.DirectBuffer(bufferSize);
            buffer4.WriteBytes(sourceData.AsSpan(0, bufferSize));
            composite.AddFlattenedComponents(buffer4, true);

            composite.SkipBytes(bufferSize);
            Assert.Equal((byte)0x03, composite.ReadByte());
        }
        finally
        {
            composite.Release();
        }

    }

}
