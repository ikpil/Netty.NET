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
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufAllocationFailureTest
{
    private sealed class CopyFailure : Exception { }
    private sealed class Observation
    {
        public int FailAt;
        public readonly CopyFailure Failure = new();
        public readonly List<(ByteBuf Source, int Index, int Length)> Visits = [];
        public readonly List<ByteBuf> Allocations = [];
    }
    private sealed class Destination(ByteBuf buffer, Observation observation) : WrappedByteBuf(buffer)
    {
        public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
        {
            observation.Visits.Add((source, sourceIndex, length));
            if (observation.FailAt == observation.Visits.Count) throw observation.Failure;
            base.WriteBytes(source, sourceIndex, length); return this;
        }
    }
    private sealed class Allocator(IByteBufAllocator backing, Observation observation) : AbstractByteBufAllocator
    {
        public override bool IsDirectBufferPooled => backing.IsDirectBufferPooled;
        private ByteBuf Wrap(ByteBuf buffer)
        {
            buffer.SetZero(0, buffer.Capacity);
            ByteBuf result = new Destination(buffer, observation); observation.Allocations.Add(result); return result;
        }
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity) => Wrap(backing.HeapBuffer(initialCapacity, maxCapacity));
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity) => Wrap(backing.DirectBuffer(initialCapacity, maxCapacity));
    }
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 3; ++provider)
        foreach (bool direct in new[] { false, true })
        for (int operation = 0; operation < 4; ++operation)
        for (int failAt = 0; failAt < 3; ++failAt) yield return [provider, direct, operation, failAt];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ConsolidationUsesAllocatedDestinationWriteKernelAndKeepsOwnedSourcesOnFailure(int provider, bool direct, int operation, int failAt)
    {
        var native = new NativeMemoryAllocator();
        IByteBufAllocator backing = provider switch
        {
            0 => new UnpooledByteBufAllocator(direct, true, native),
            1 => new PooledByteBufAllocator(direct, maxOrder: 1, nativeAllocator: native),
            _ => new AdaptiveByteBufAllocator(direct, false, native)
        };
        var observation = new Observation { FailAt = failAt }; var allocator = new Allocator(backing, observation);
        var composite = new CompositeByteBuf(allocator, direct, operation == 2 ? 2 : operation == 3 ? 3 : 8);
        ByteBuf[] sources = new ByteBuf[3]; int[] starts = [2, 4, 1], lengths = [3, 4, 5];
        byte[] expected = new byte[operation == 3 ? 16 : 12]; int offset = 0;
        try
        {
            for (int i = 0; i < 3; ++i)
            {
                sources[i] = backing.Buffer(16, 16);
                for (int j = 0; j < 16; ++j) sources[i].WriteByte(i * 32 + j + 1);
                sources[i].SetIndex(starts[i], starts[i] + lengths[i]);
                sources[i].AsReadOnlyMemory(starts[i], lengths[i]).Span.CopyTo(expected.AsSpan(offset)); offset += lengths[i];
                if (operation != 2 || i < 2) composite.AddComponent(sources[i], true);
            }
            if (operation != 2) composite.SetIndex(1, 10).MarkReaderIndex().MarkWriterIndex();
            int reader = composite.ReaderIndex, writer = operation == 2 ? 12 : composite.WriterIndex;
            void Run()
            {
                if (operation == 0) composite.Consolidate();
                else if (operation == 1) composite.Consolidate(1, 2);
                else if (operation == 2) composite.AddComponent(sources[2], true);
                else composite.Capacity = 16;
            }
            if (failAt != 0) Assert.Same(observation.Failure, Assert.Throws<CopyFailure>(() => Run()));
            else Run();
            int first = operation == 1 ? 1 : 0, count = operation == 1 ? 2 : operation == 3 ? 4 : 3;
            Assert.Equal(failAt == 0 ? count : failAt, observation.Visits.Count);
            for (int i = 0; i < Math.Min(observation.Visits.Count, 3 - first); ++i)
            {
                Assert.Same(sources[first + i], observation.Visits[i].Source);
                Assert.Equal(starts[first + i], observation.Visits[i].Index); Assert.Equal(lengths[first + i], observation.Visits[i].Length);
            }
            ByteBuf destination = observation.Allocations[^1];
            Assert.Equal(failAt == 0 ? 1 : 0, destination.ReferenceCount);
            Assert.Equal(reader, composite.ReaderIndex); Assert.Equal(writer, composite.WriterIndex);
            Assert.Equal(expected, composite.AsReadOnlySequence(0, expected.Length).ToArray());
            Assert.Equal(failAt != 0 ? operation == 3 ? 4 : 3 : operation == 1 ? 2 : 1, composite.NumComponents);
            for (int i = 0; i < 3; ++i) Assert.Equal(failAt != 0 || operation == 1 && i == 0 ? 1 : 0, sources[i].ReferenceCount);
        }
        finally
        {
            if (composite.ReferenceCount > 0) composite.Release();
            foreach (ByteBuf source in sources) if (source?.ReferenceCount > 0) source.Release();
            foreach (ByteBuf allocation in observation.Allocations) if (allocation.ReferenceCount > 0) allocation.Release();
            (backing as IDisposable)?.Dispose(); Assert.Equal(0, native.ReservedBytes);
        }
    }
}
