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
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Source contracts: WrappedByteBuf/WrappedCompositeByteBuf.forEachByte[Desc],
// including the delegates inherited by SimpleLeakAwareByteBuf and UnreleasableByteBuf.
public class ByteBufIterationDispatchTest
{
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public int Records, Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { ++Closes; return true; }
    }

    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Tracker Inner = new(), Outer = new();
        public readonly ByteBuf Root, Facade;
        private readonly ByteBuf _owner;
        private readonly bool _outerTracked;

        public Storage(int backing, int facade)
        {
            byte[] bytes = [99, 1, 2, 3, 4, 5, 6, 99];
            Root = backing switch
            {
                0 => new UnpooledHeapByteBuf(8, 64).WriteBytes(bytes),
                1 => new UnpooledDirectByteBuf(8, 64, Native).WriteBytes(bytes),
                _ => new CompositeByteBuf(16, false, Native)
                    .AddComponent(Unpooled.WrappedBuffer(bytes[..4]), true)
                    .AddComponent(new UnpooledDirectByteBuf(4, 4, Native).WriteBytes(bytes[4..]), true)
            };
            Root.SetIndex(1, 7).MarkReaderIndex().MarkWriterIndex();
            ByteBuf tracked = backing == 2
                ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)Root, Inner)
                : new AdvancedLeakAwareByteBuf(Root, Inner);
            _outerTracked = facade is 1 or 4;
            Facade = facade switch
            {
                0 => new Wrapped(tracked),
                1 => new SimpleLeakAwareByteBuf(tracked, Outer),
                2 => Unpooled.UnreleasableBuffer(tracked),
                3 => new WrappedCompositeByteBuf((CompositeByteBuf)tracked),
                _ => new SimpleLeakAwareCompositeByteBuf((CompositeByteBuf)tracked, Outer)
            };
            _owner = facade == 2 ? tracked : Facade;
        }

        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Assert.True(_owner.Release());
            Assert.Equal(0, Root.ReferenceCount);
            Assert.Equal(1, Inner.Closes);
            Assert.Equal(_outerTracked ? 1 : 0, Outer.Closes);
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        for (int backing = 0; backing < 3; ++backing)
        for (int facade = 0; facade < (backing == 2 ? 5 : 3); ++facade)
        for (int operation = 0; operation < 4; ++operation)
            yield return [backing, facade, operation];
    }

    private static int Scan(ByteBuf buffer, int operation, Func<byte, bool> processor,
        int index = 2, int length = 4) => operation switch
    {
        0 => buffer.ForEachByte(processor),
        1 => buffer.ForEachByte(index, length, processor),
        2 => buffer.ForEachByteDesc(processor),
        _ => buffer.ForEachByteDesc(index, length, processor)
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachScanDelegatesOnceAndPreservesBoundsCallbacksAndOwnership(int backing, int facade, int operation)
    {
        using var storage = new Storage(backing, facade);
        ByteBuf buffer = storage.Facade;
        bool descending = operation >= 2, indexed = (operation & 1) != 0;
        var visited = new List<byte>();
        int before = storage.Inner.Records;
        Assert.Equal(3, Scan(buffer, operation, value => { visited.Add(value); return value != 3; }));
        Assert.Equal(descending ? indexed ? new byte[] { 5, 4, 3 } : new byte[] { 6, 5, 4, 3 }
            : indexed ? new byte[] { 2, 3 } : new byte[] { 1, 2, 3 }, visited);
        Assert.Equal(before + 1, storage.Inner.Records);

        visited.Clear(); before = storage.Inner.Records;
        Assert.Equal(-1, Scan(buffer, operation, value => { visited.Add(value); return true; }));
        Assert.Equal(descending ? indexed ? new byte[] { 5, 4, 3, 2 } : new byte[] { 6, 5, 4, 3, 2, 1 }
            : indexed ? new byte[] { 2, 3, 4, 5 } : new byte[] { 1, 2, 3, 4, 5, 6 }, visited);
        Assert.Equal(before + 1, storage.Inner.Records);

        var failure = new InvalidOperationException("processor failure");
        int calls = 0; before = storage.Inner.Records;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Scan(buffer, operation, _ =>
        {
            if (++calls == 2) throw failure;
            return true;
        })));
        Assert.Equal(2, calls);
        Assert.Equal(before + 1, storage.Inner.Records);

        if (indexed)
        {
            before = storage.Inner.Records;
            Assert.Equal(-1, Scan(buffer, operation, _ => throw failure, 8, 0));
            Assert.Equal(before + 1, storage.Inner.Records);
            before = storage.Inner.Records;
            Assert.Throws<ArgumentOutOfRangeException>(() => Scan(buffer, operation, _ => throw failure, 7, 2));
            Assert.Equal(before + 1, storage.Inner.Records);
        }
        Assert.Equal(0, storage.Outer.Records);
        Assert.Equal(0, storage.Inner.Closes);
        Assert.Equal(1, storage.Root.ReferenceCount);
        Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
        storage.Root.SetIndex(0, 8).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
        storage.Dispose();
        before = storage.Inner.Records;
        Assert.Throws<IllegalReferenceCountException>(() => Scan(buffer, operation, _ => throw failure));
        Assert.Equal(before + 1, storage.Inner.Records);
    }

    private sealed class Observation
    {
        public readonly int[] Calls = new int[4];
        public Func<byte, bool> Processor;
        public int Index, Length;
        public int Visit(int operation, Func<byte, bool> processor, int index = -1, int length = -1)
        {
            ++Calls[operation]; Processor = processor; Index = index; Length = length;
            return 40 + operation;
        }
    }

    private sealed class ObservedHeap(Observation observation) : UnpooledHeapByteBuf(8, 8)
    {
        public override int ForEachByte(Func<byte, bool> processor) => observation.Visit(0, processor);
        public override int ForEachByte(int index, int length, Func<byte, bool> processor) => observation.Visit(1, processor, index, length);
        public override int ForEachByteDesc(Func<byte, bool> processor) => observation.Visit(2, processor);
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor) => observation.Visit(3, processor, index, length);
    }

    private sealed class ObservedComposite(Observation observation) : CompositeByteBuf
    {
        public override int ForEachByte(Func<byte, bool> processor) => observation.Visit(0, processor);
        public override int ForEachByte(int index, int length, Func<byte, bool> processor) => observation.Visit(1, processor, index, length);
        public override int ForEachByteDesc(Func<byte, bool> processor) => observation.Visit(2, processor);
        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor) => observation.Visit(3, processor, index, length);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(0, 3)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(1, 3)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(2, 3)]
    public void ParentOverrideReceivesTheOriginalOperationAndArguments(int facade, int operation)
    {
        var observation = new Observation();
        ByteBuf root = facade == 1 ? new ObservedComposite(observation) : new ObservedHeap(observation);
        ByteBuf buffer = facade switch
        {
            0 => new Wrapped(root),
            1 => new WrappedCompositeByteBuf((CompositeByteBuf)root),
            _ => Unpooled.UnreleasableBuffer(root)
        };
        Func<byte, bool> processor = _ => throw new InvalidOperationException("override owns iteration");
        try
        {
            Assert.Equal(40 + operation, Scan(buffer, operation, processor));
            Assert.Same(processor, observation.Processor);
            Assert.Equal((operation & 1) == 0 ? -1 : 2, observation.Index);
            Assert.Equal((operation & 1) == 0 ? -1 : 4, observation.Length);
            for (int i = 0; i < 4; ++i) Assert.Equal(i == operation ? 1 : 0, observation.Calls[i]);
        }
        finally { root.Release(); }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void CallbackCanGrowNativeStorageWithoutLeavingABorrowedSpanAlive(int facade, bool descending)
    {
        using var storage = new Storage(1, facade);
        var visited = new List<byte>();
        int result = Scan(storage.Facade, descending ? 3 : 1, value =>
        {
            visited.Add(value);
            if (visited.Count == 1)
            {
                storage.Root.Capacity = 32;
                storage.Root.SetByte(descending ? 4 : 3, 42);
            }
            return value != 42;
        });
        Assert.Equal(descending ? 4 : 3, result);
        Assert.Equal(descending ? new byte[] { 5, 42 } : new byte[] { 2, 42 }, visited);
        Assert.Equal(1, storage.Inner.Records);
        Assert.Equal(32, storage.Native.ReservedBytes);
        Assert.Equal(1, storage.Facade.ReaderIndex); Assert.Equal(7, storage.Facade.WriterIndex);
    }
}
