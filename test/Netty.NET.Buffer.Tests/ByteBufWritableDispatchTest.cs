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

public class ByteBufWritableDispatchTest
{
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public int Records, Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { ++Closes; return true; }
    }

    private sealed class Heap : UnpooledHeapByteBuf
    {
        public int Calls;
        public Heap() : base(8, 64) { }
        public override ByteBuf EnsureWritable(int count) { ++Calls; return base.EnsureWritable(count); }
        public override int EnsureWritable(int count, bool force) { ++Calls; return base.EnsureWritable(count, force); }
    }

    private sealed class Direct(NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 64, native)
    {
        public int Calls;
        public override ByteBuf EnsureWritable(int count) { ++Calls; return base.EnsureWritable(count); }
        public override int EnsureWritable(int count, bool force) { ++Calls; return base.EnsureWritable(count, force); }
    }

    private sealed class Composite : CompositeByteBuf
    {
        public int Calls;
        public override ByteBuf EnsureWritable(int count) { ++Calls; return base.EnsureWritable(count); }
        public override int EnsureWritable(int count, bool force) { ++Calls; return base.EnsureWritable(count, force); }
    }

    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native;
        public readonly ByteBuf Owner, Facade;
        public readonly Tracker Tracker = new();
        private readonly ByteBuf _tracked;
        public Storage(int backing, int facade, long budget = long.MaxValue)
        {
            Native = new NativeMemoryAllocator(budget);
            Owner = backing switch
            {
                0 => new Heap(),
                1 => new Direct(Native),
                _ => new Composite().AddComponent(Unpooled.WrappedBuffer(new byte[8]), true)
            };
            Owner.SetBytes(0, new byte[] { 17, 34, 51, 68, 85, 102, 119, 136 });
            Owner.SetIndex(1, 3).MarkReaderIndex().MarkWriterIndex();
            _tracked = backing == 2 ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)Owner, Tracker)
                : new AdvancedLeakAwareByteBuf(Owner, Tracker);
            Facade = facade switch
            {
                0 => new Wrapped(_tracked),
                1 => Unpooled.UnreleasableBuffer(_tracked),
                _ => new WrappedCompositeByteBuf((CompositeByteBuf)_tracked)
            };
        }
        public int Calls => Owner switch { Heap h => h.Calls, Direct d => d.Calls, Composite c => c.Calls, _ => -1 };
        public void Dispose()
        {
            if (Owner.ReferenceCount > 0) Assert.True(_tracked.Release());
            Assert.Equal(0, Owner.ReferenceCount);
            Assert.Equal(1, Tracker.Closes);
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        for (int backing = 0; backing < 3; backing++)
        for (int facade = 0; facade < (backing == 2 ? 3 : 2); facade++)
        for (int operation = 0; operation < 9; operation++) yield return [backing, facade, operation];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WritableRequestsReachTheUnderlyingOverrideOnceAndKeepTheOuterReceiver(int backing, int facade, int operation)
    {
        using var storage = new Storage(backing, facade);
        int minimum = operation switch { 0 => 0, 1 or 5 => 6, 2 or 6 or 7 => 62, 4 => 5, _ => -1 };
        int expectedCapacity = operation is 1 or 5 ? 64
            : operation is 2 or 6 or 7 && backing == 2 ? 128
            : operation == 7 ? 64 : 8;
        if (operation is 3 or 8 || operation == 2 && backing != 2)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                if (operation <= 3) storage.Facade.EnsureWritable(minimum);
                else storage.Facade.EnsureWritable(minimum, true);
            });
        }
        else if (operation <= 3) Assert.Same(storage.Facade, storage.Facade.EnsureWritable(minimum));
        else
        {
            int expectedCode = operation == 4 ? 0 : operation == 6 && backing != 2 ? 1
                : operation == 7 && backing != 2 ? 3 : 2;
            Assert.Equal(expectedCode, storage.Facade.EnsureWritable(minimum, operation == 7));
        }
        Assert.Equal(1, storage.Calls);
        Assert.Equal(1, storage.Tracker.Records);
        Assert.Equal(expectedCapacity, storage.Owner.Capacity);
        Assert.Equal(1, storage.Owner.ReaderIndex);
        Assert.Equal(3, storage.Owner.WriterIndex);
        Assert.Equal(1, storage.Owner.ReferenceCount);
        Assert.Equal(new byte[] { 17, 34, 51, 68, 85, 102, 119, 136 }, storage.Owner.AsReadOnlySequence(0, 8).ToArray());
        storage.Owner.SetIndex(0, 4).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, storage.Owner.ReaderIndex);
        Assert.Equal(3, storage.Owner.WriterIndex);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    public void FailedNativeGrowthPreservesTheOriginalReservationBytesAndIndices(int facade, int mode)
    {
        using var storage = new Storage(1, facade, 16);
        Assert.Equal(8, storage.Native.ReservedBytes);
        Assert.Throws<OutOfMemoryException>(() =>
        {
            if (mode == 0) storage.Facade.EnsureWritable(6);
            else storage.Facade.EnsureWritable(6, mode == 2);
        });
        Assert.Equal(1, storage.Calls);
        Assert.Equal(1, storage.Tracker.Records);
        Assert.Equal(8, storage.Native.ReservedBytes);
        Assert.Equal(8, storage.Owner.Capacity);
        Assert.Equal(1, storage.Owner.ReaderIndex);
        Assert.Equal(3, storage.Owner.WriterIndex);
        Assert.Equal(new byte[] { 17, 34, 51, 68, 85, 102, 119, 136 }, storage.Owner.AsReadOnlySequence(0, 8).ToArray());
    }
}
