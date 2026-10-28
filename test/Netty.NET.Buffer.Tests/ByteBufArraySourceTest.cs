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

public class ByteBufArraySourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root;
        public Storage(int provider, int length)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(length),
                1 => new UnpooledDirectByteBuf(length, int.MaxValue, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(length / 2).SetIndex(0, length / 2), true)
                    .AddComponent(new UnpooledDirectByteBuf(length - length / 2, int.MaxValue, Native)
                        .SetIndex(0, length - length / 2), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], length, int.MaxValue),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], length, int.MaxValue)
            };
            Root.Clear();
        }
        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Root.Release();
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    // Supplemental span/state model; original named bodies use all seven actual root fixtures.
    public enum ArrayCase
    {
        NegativeBufferIndex, NegativeBufferIndexEmptyRange, NegativeDestinationIndex, DestinationRangeTooLong, RandomArrayRange, RandomArray, RandomWrappedArrayRange, SequentialArray, SequentialArrayRange, GetReleasedArray, GetReleasedRange, SetReleasedArray, SetReleasedRange, ReadReleasedArray, ReadReleasedRange, WriteReleasedArray, WriteReleasedRange
    }

    public static IEnumerable<object[]> SupplementalCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (ArrayCase scenario in Enum.GetValues<ArrayCase>()) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(SupplementalCases))]
    public void ArrayTransfersPreserveSpanModelAndOwnership(int provider, ArrayCase scenario)
    {
        const int capacity = 4096, blockSize = 128, seed = 12345;
        using var s = new Storage(provider, scenario is ArrayCase.GetReleasedArray or ArrayCase.GetReleasedRange or ArrayCase.SetReleasedArray or ArrayCase.SetReleasedRange or ArrayCase.ReadReleasedArray or ArrayCase.ReadReleasedRange or ArrayCase.WriteReleasedArray or ArrayCase.WriteReleasedRange ? 8 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario is ArrayCase.GetReleasedArray or ArrayCase.GetReleasedRange or ArrayCase.SetReleasedArray or ArrayCase.SetReleasedRange or ArrayCase.ReadReleasedArray or ArrayCase.ReadReleasedRange or ArrayCase.WriteReleasedArray or ArrayCase.WriteReleasedRange)
        {
            // Clear the buffer so we are sure the reader and writer indices are 0.
            // This is important as we may return a slice from newBuffer(...).
            buffer.Clear(); Assert.True(buffer.Release());
            byte[] bytes = new byte[8];
            switch (scenario)
            {
                case ArrayCase.GetReleasedArray: Assert.Throws<IllegalReferenceCountException>(() => buffer.GetBytes(0, bytes)); break;
                case ArrayCase.GetReleasedRange: Assert.Throws<IllegalReferenceCountException>(() => buffer.GetBytes(0, bytes.AsSpan(0, 1))); break;
                case ArrayCase.SetReleasedArray: Assert.Throws<IllegalReferenceCountException>(() => buffer.SetBytes(0, bytes)); break;
                case ArrayCase.SetReleasedRange: Assert.Throws<IllegalReferenceCountException>(() => buffer.SetBytes(0, bytes.AsSpan(0, 1))); break;
                case ArrayCase.ReadReleasedArray: Assert.Throws<IllegalReferenceCountException>(() => buffer.ReadBytes(bytes)); break;
                case ArrayCase.ReadReleasedRange: Assert.Throws<IllegalReferenceCountException>(() => buffer.ReadBytes(bytes.AsSpan(0, 1))); break;
                case ArrayCase.WriteReleasedArray: Assert.Throws<IllegalReferenceCountException>(() => buffer.WriteBytes(bytes)); break;
                default: Assert.Throws<IllegalReferenceCountException>(() => buffer.WriteBytes(bytes.AsSpan(0, 1))); break;
            }
            Assert.Equal(new byte[8], bytes);
            return;
        }
        if (scenario is ArrayCase.NegativeBufferIndex or ArrayCase.NegativeBufferIndexEmptyRange or ArrayCase.NegativeDestinationIndex or ArrayCase.DestinationRangeTooLong)
        {
            if (scenario == ArrayCase.NegativeBufferIndex)
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(-1, Span<byte>.Empty));
            else if (scenario == ArrayCase.NegativeBufferIndexEmptyRange)
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(-1, Array.Empty<byte>().AsSpan(0, 0)));
            else
            {
                byte[] destination = new byte[4];
                buffer.SetInt(0, 0x01020304);
                if (scenario == ArrayCase.NegativeDestinationIndex)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(0, destination.AsSpan(-1, 4)));
                    // No partial copy is expected.
                    Assert.Equal(new byte[4], destination);
                }
                else
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(0, destination.AsSpan(1, 4)));
                    // No partial copy is expected.
                    Assert.Equal(new byte[4], destination);
                }
            }
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        bool sequential = scenario is ArrayCase.SequentialArray or ArrayCase.SequentialArrayRange or ArrayCase.GetReleasedArray or ArrayCase.GetReleasedRange or ArrayCase.SetReleasedArray or ArrayCase.SetReleasedRange or ArrayCase.ReadReleasedArray or ArrayCase.ReadReleasedRange or ArrayCase.WriteReleasedArray or ArrayCase.WriteReleasedRange, ranged = scenario is ArrayCase.RandomArrayRange or ArrayCase.RandomWrappedArrayRange or ArrayCase.SequentialArrayRange;
        byte[] value = new byte[ranged ? blockSize * 2 : blockSize];
        var random = new Random(seed);
        if (sequential) buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - blockSize + 1; i += blockSize)
        {
            random.NextBytes(value);
            if (sequential) { Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(i, buffer.WriterIndex); }
            int offset = ranged ? random.Next(blockSize) : 0;
            ReadOnlySpan<byte> source = value.AsSpan(offset, blockSize);
            Assert.Same(buffer, sequential ? buffer.WriteBytes(source) : buffer.SetBytes(i, source));
        }
        // CLR Random is independently reseeded; expected arrays need no ByteBuf wrapper or reference claim.
        random = new Random(seed);
        byte[] expected = new byte[value.Length];
        for (int i = 0; i < buffer.Capacity - blockSize + 1; i += blockSize)
        {
            random.NextBytes(expected);
            int offset = ranged ? random.Next(blockSize) : 0;
            if (sequential) { Assert.Equal(i, buffer.ReaderIndex); Assert.Equal(capacity, buffer.WriterIndex); }
            Span<byte> destination = value.AsSpan(offset, blockSize);
            Assert.Same(buffer, sequential ? buffer.ReadBytes(destination) : buffer.GetBytes(i, destination));
            for (int j = offset; j < offset + blockSize; ++j) Assert.Equal(expected[j], value[j]);
        }
        Assert.Equal(sequential ? capacity : 0, buffer.ReaderIndex);
        Assert.Equal(sequential ? capacity : 0, buffer.WriterIndex);
        Assert.Equal(1, buffer.ReferenceCount);
    }

    public static IEnumerable<object[]> SpanCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int operation = 0; operation < 4; ++operation) yield return [provider, operation];
    }

    [Theory]
    [MemberData(nameof(SpanCases))]
    public void BoundedSpansCrossStorageBoundariesAndKeepGuardsAndMarks(int provider, int operation)
    {
        using var s = new Storage(provider, 32);
        byte[] bytes = new byte[32];
        for (int i = 0; i < bytes.Length; ++i) bytes[i] = (byte)(i * 7);
        byte[] source = [99, 99, 83, 91, 113, 127, 99, 99];
        byte[] destination = [199, 199, 199, 199, 199, 199, 199, 199];
        byte[] expectedSource = (byte[])source.Clone(), expectedDestination = (byte[])destination.Clone();
        byte[] expectedContent = (byte[])bytes.Clone();
        ByteBuf buffer = s.Root;
        buffer.SetBytes(0, bytes).SetIndex(3, 25).MarkReaderIndex().MarkWriterIndex();
        if (operation == 2) buffer.SetIndex(15, 25);
        if (operation == 3) buffer.SetIndex(3, 15);
        int reader = buffer.ReaderIndex, writer = buffer.WriterIndex;
        switch (operation)
        {
            case 0: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(15, destination.AsSpan(-1, 4))); break;
            case 1: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetBytes(15, source.AsSpan(-1, 4))); break;
            case 2: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadBytes(destination.AsSpan(-1, 4))); break;
            default: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteBytes(source.AsSpan(-1, 4))); break;
        }
        Assert.Equal(reader, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
        Assert.Equal(bytes, ByteBufUtil.GetBytes(buffer, 0, 32));
        Assert.Equal(expectedSource, source); Assert.Equal(expectedDestination, destination);
        switch (operation)
        {
            case 0: Assert.Same(buffer, buffer.GetBytes(15, destination.AsSpan(2, 4))); break;
            case 1: Assert.Same(buffer, buffer.SetBytes(15, source.AsSpan(2, 4))); break;
            case 2: Assert.Same(buffer, buffer.ReadBytes(destination.AsSpan(2, 4))); break;
            default: Assert.Same(buffer, buffer.WriteBytes(source.AsSpan(2, 4))); break;
        }
        if (operation is 0 or 2) Array.Copy(bytes, 15, expectedDestination, 2, 4);
        else Array.Copy(source, 2, expectedContent, 15, 4);
        Assert.Equal(expectedSource, source); Assert.Equal(expectedDestination, destination);
        Assert.Equal(expectedContent, ByteBufUtil.GetBytes(buffer, 0, 32));
        Assert.Equal(operation == 2 ? 19 : reader, buffer.ReaderIndex);
        Assert.Equal(operation == 3 ? 19 : writer, buffer.WriterIndex);
        Assert.Equal(32, buffer.Capacity); Assert.Equal(1, buffer.ReferenceCount);
        buffer.SetIndex(0, 32).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(3, buffer.ReaderIndex); Assert.Equal(25, buffer.WriterIndex);
    }
}
