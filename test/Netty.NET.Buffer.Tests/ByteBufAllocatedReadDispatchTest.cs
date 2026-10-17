/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufAllocatedReadDispatchTest
{
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);

    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public int Records;
        public int Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf trackedObject) { ++Closes; return true; }
    }

    private sealed class Heap(IByteBufAllocator allocator) : UnpooledHeapByteBuf(allocator, 4, 16)
    {
        public int Calls;
        public override ByteBuf ReadBytes(int length) { ++Calls; return base.ReadBytes(length); }
    }

    private sealed class Composite(IByteBufAllocator allocator) : CompositeByteBuf(allocator, false, 16)
    {
        public int Calls;
        public override ByteBuf ReadBytes(int length) { ++Calls; return base.ReadBytes(length); }
    }

    private static ByteBuf Wrap(ByteBuf owner, Tracker tracker, int kind) => kind switch
    {
        0 => new Wrapped(owner),
        1 => new SimpleLeakAwareByteBuf(owner, tracker),
        2 => new AdvancedLeakAwareByteBuf(owner, tracker),
        3 => Unpooled.UnreleasableBuffer(owner),
        4 => new Wrapped(Unpooled.UnreleasableBuffer(owner)),
        5 => new WrappedCompositeByteBuf((CompositeByteBuf)owner),
        6 => new SimpleLeakAwareCompositeByteBuf((CompositeByteBuf)owner, tracker),
        7 => new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)owner, tracker),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void AllocatorEmptyReadReturnsItsOwnSentinelThroughTransparentFacades(int allocatorKind, bool preferDirect)
    {
        var native = new NativeMemoryAllocator();
        IByteBufAllocator allocator = allocatorKind switch
        {
            0 => new UnpooledByteBufAllocator(preferDirect, native),
            1 => new PooledByteBufAllocator(preferDirect, maxOrder: 1, nativeAllocator: native),
            _ => new AdaptiveByteBufAllocator(preferDirect, false, native)
        };
        try
        {
            ByteBuf empty = allocator.IoBuffer(0, 0);
            Assert.NotSame(Unpooled.EmptyBuffer, empty);
            for (int kind = 0; kind <= 4; kind++)
            {
                var tracker = new Tracker();
                ByteBuf facade = Wrap(empty, tracker, kind);
                foreach (ByteBuf source in new[] { empty, facade })
                {
                    Assert.Same(empty, source.ReadBytes(0));
                    Assert.Same(allocator, source.ReadBytes(0).Allocator);
                    Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBytes(-1));
                    Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBytes(1));
                    Assert.Equal(0, source.ReaderIndex);
                    Assert.Equal(0, source.WriterIndex);
                    Assert.Equal(1, source.ReferenceCount);
                    Assert.False(source.Release());
                }
                Assert.Equal(0, tracker.Closes);
            }
            // A derived read-only view uses AbstractByteBuf's ordinary allocated-read contract.
            Assert.Same(Unpooled.EmptyBuffer, empty.AsReadOnly().ReadBytes(0));
            Assert.Same(empty, empty.ReadSlice(0));
            Assert.Same(empty, empty.ReadRetainedSlice(0));
            Assert.Same(empty, empty.Copy());
            Assert.Same(empty, empty.Duplicate());
            var metric = ((IByteBufAllocatorMetricProvider)allocator).Metric;
            Assert.Equal(0, metric.UsedHeapMemory);
            Assert.Equal(0, metric.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally
        {
            (allocator as IDisposable)?.Dispose();
            Assert.Equal(0, native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> DispatchCases()
    {
        foreach (bool composite in new[] { false, true })
        foreach (bool preferDirect in new[] { false, true })
        foreach (int length in new[] { -1, 0, 2, 4 })
        for (int kind = composite ? 5 : 0; kind <= (composite ? 7 : 4); kind++)
            yield return [composite, preferDirect, length, kind];
    }

    [Theory]
    [MemberData(nameof(DispatchCases))]
    public void AllocatedReadsDelegateOnceAndPreserveValidationAndOwnership(bool composite,
        bool preferDirect, int length, int kind)
    {
        var native = new NativeMemoryAllocator();
        var allocator = new UnpooledByteBufAllocator(preferDirect, native);
        ByteBuf owner;
        if (composite)
        {
            owner = new Composite(allocator).AddComponent(
                new UnpooledHeapByteBuf(allocator, 4, 16).WriteBytes(new byte[] { 17, 34, 51, 68 }), true);
        }
        else owner = new Heap(allocator).WriteBytes(new byte[] { 17, 34, 51, 68 });
        owner.ReaderIndex = 1;
        var tracker = new Tracker();
        ByteBuf facade = Wrap(owner, tracker, kind);
        ByteBuf result = null;
        try
        {
            if (length is < 0 or > 3)
                Assert.Throws<ArgumentOutOfRangeException>(() => facade.ReadBytes(length));
            else result = facade.ReadBytes(length);
            Assert.Equal(1, composite ? ((Composite)owner).Calls : ((Heap)owner).Calls);
            Assert.Equal(1 + (length == 2 ? 2 : 0), owner.ReaderIndex);
            Assert.Equal(owner.ReaderIndex, facade.ReaderIndex);
            Assert.Equal(4, owner.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount);
            Assert.Equal(kind is 2 or 7 ? 1 : 0, tracker.Records);
            if (length == 0) Assert.Same(Unpooled.EmptyBuffer, result);
            if (length == 2)
            {
                Assert.Same(allocator, result.Allocator);
                Assert.Equal(preferDirect, result.IsDirect);
                Assert.Equal(2, result.Capacity);
                Assert.Equal(composite ? int.MaxValue : 16, result.MaxCapacity);
                Assert.Equal(new byte[] { 34, 51 }, result.ReadableSequence.ToArray());
                result.SetByte(0, 99);
                Assert.Equal(34, owner.GetByte(1));
            }
            Assert.True(kind is 3 or 4 ? owner.Release() : facade.Release());
            Assert.Equal(0, owner.ReferenceCount);
            Assert.Equal(kind is 1 or 2 or 6 or 7 ? 1 : 0, tracker.Closes);
            if (length == 2)
            {
                Assert.Equal(new byte[] { 99, 51 }, result.ReadableSequence.ToArray());
                Assert.True(result.Release());
                result = null;
            }
            Assert.Equal(0, allocator.Metric.UsedHeapMemory);
            Assert.Equal(0, allocator.Metric.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally
        {
            if (result != null) result.Release();
            if (owner.ReferenceCount > 0) owner.Release();
        }
    }
}
