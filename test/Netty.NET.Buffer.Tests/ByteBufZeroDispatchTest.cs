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

public class ByteBufZeroDispatchTest
{
    private sealed class Failure : Exception;
    private sealed class Calls
    {
        public int Set, Write, Ensure, Index, Length;
        public bool RejectSet, ThrowOperation, ThrowCore;
        public readonly Failure Error = new();
        public void Before(bool write, int index, int length)
        {
            if (write) ++Write; else ++Set;
            Index = index; Length = length;
            if (ThrowOperation || RejectSet && !write) throw Error;
        }
    }
    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public int Records, Closes;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { ++Closes; return true; }
    }
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);

    private sealed class Heap(Calls calls) : UnpooledHeapByteBuf(8, 64)
    {
        public override ByteBuf EnsureWritable(int length) { ++calls.Ensure; return base.EnsureWritable(length); }
        public override ByteBuf SetZero(int index, int length)
        { calls.Before(false, index, length); return base.SetZero(index, length); }
        public override ByteBuf WriteZero(int length)
        { calls.Before(true, -1, length); return base.WriteZero(length); }
        protected override void SetZeroCore(int index, int length)
        {
            if (calls.ThrowCore) { base.SetZeroCore(index, 2); throw calls.Error; }
            base.SetZeroCore(index, length);
        }
    }

    private sealed class Direct(Calls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 64, native)
    {
        public override ByteBuf EnsureWritable(int length) { ++calls.Ensure; return base.EnsureWritable(length); }
        public override ByteBuf SetZero(int index, int length)
        { calls.Before(false, index, length); return base.SetZero(index, length); }
        public override ByteBuf WriteZero(int length)
        { calls.Before(true, -1, length); return base.WriteZero(length); }
        protected override void SetZeroCore(int index, int length)
        {
            if (calls.ThrowCore) { base.SetZeroCore(index, 2); throw calls.Error; }
            base.SetZeroCore(index, length);
        }
    }

    private sealed class Composite(Calls calls, NativeMemoryAllocator native) : CompositeByteBuf(16, false, native)
    {
        public override ByteBuf EnsureWritable(int length) { ++calls.Ensure; return base.EnsureWritable(length); }
        public override ByteBuf SetZero(int index, int length)
        { calls.Before(false, index, length); return base.SetZero(index, length); }
        public override ByteBuf WriteZero(int length)
        { calls.Before(true, -1, length); return base.WriteZero(length); }
        protected override void SetZeroCore(int index, int length)
        {
            if (calls.ThrowCore) { base.SetZeroCore(index, 2); throw calls.Error; }
            base.SetZeroCore(index, length);
        }
    }

    private static ByteBuf Create(int backing, Calls calls, NativeMemoryAllocator native)
    {
        ByteBuf result = backing switch
        {
            0 => new Heap(calls),
            1 => new Direct(calls, native),
            _ => new Composite(calls, native)
                .AddComponent(new UnpooledHeapByteBuf(4, 4).SetIndex(0, 4), true)
                .AddComponent(new UnpooledDirectByteBuf(4, 4, native).SetIndex(0, 4), true)
        };
        result.SetBytes(0, new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 });
        return result.SetIndex(1, 3).MarkReaderIndex().MarkWriterIndex();
    }

    public static IEnumerable<object[]> OwnerCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (int length in new[] { -1, 0, 1, 4, 7, 8, 9, 17, 62 }) yield return [direct, length];
    }

    // AbstractByteBuf.writeZero uses low-level storage writes, independently of setZero.
    [Theory]
    [MemberData(nameof(OwnerCases))]
    public void InheritedWriteUsesStorageKernelWithoutCallingPublicSetZero(bool direct, int length)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls { RejectSet = true };
        ByteBuf owner = Create(direct ? 1 : 0, calls, native);
        try
        {
            if (length is < 0 or > 61) Assert.Throws<ArgumentOutOfRangeException>(() => owner.WriteZero(length));
            else Assert.Same(owner, owner.WriteZero(length));
            Assert.Equal(1, calls.Write); Assert.Equal(0, calls.Set); Assert.Equal(1, calls.Ensure);
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(length is < 0 or > 61 ? 3 : 3 + length, owner.WriterIndex);
            Assert.Equal(10, owner.GetByte(0)); Assert.Equal(11, owner.GetByte(1)); Assert.Equal(12, owner.GetByte(2));
            if (length is >= 0 and <= 61)
                for (int i = 0; i < length; ++i) Assert.Equal(0, owner.GetByte(3 + i));
            else Assert.Equal(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, ByteBufUtil.GetBytes(owner, 0, 8));
            owner.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex); Assert.Equal(1, owner.ReferenceCount);
        }
        finally { Assert.True(owner.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PartialStorageFailureDoesNotPublishWriterOrMarkerChanges(bool direct)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls { RejectSet = true, ThrowCore = true };
        ByteBuf owner = Create(direct ? 1 : 0, calls, native);
        try
        {
            Assert.Same(calls.Error, Assert.Throws<Failure>(() => owner.WriteZero(3)));
            Assert.Equal(0, calls.Set); Assert.Equal(1, calls.Write); Assert.Equal(1, calls.Ensure);
            Assert.Equal(new byte[] { 10, 11, 12, 0, 0, 15, 16, 17 }, ByteBufUtil.GetBytes(owner, 0, 8));
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex);
            owner.SetIndex(0, 4).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex);
        }
        finally { Assert.True(owner.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }

    public static IEnumerable<object[]> FacadeCases()
    {
        for (int backing = 0; backing < 3; ++backing)
        for (int facade = 0; facade < (backing == 2 ? 5 : 3); ++facade)
        foreach (bool write in new[] { false, true })
        foreach (bool failure in new[] { false, true }) yield return [backing, facade, write, failure];
    }

    [Theory]
    [MemberData(nameof(FacadeCases))]
    public void FacadeForwardsOriginalZeroOperationOnceAndKeepsReceiver(int backing, int facade, bool write, bool failure)
    {
        var native = new NativeMemoryAllocator();
        var calls = new Calls { ThrowOperation = failure };
        ByteBuf owner = Create(backing, calls, native);
        var inner = new Tracker(); var outer = new Tracker();
        ByteBuf tracked = backing == 2 ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)owner, inner)
            : new AdvancedLeakAwareByteBuf(owner, inner);
        ByteBuf buffer = facade switch
        {
            0 => new Wrapped(tracked),
            1 => new SimpleLeakAwareByteBuf(tracked, outer),
            2 => Unpooled.UnreleasableBuffer(tracked),
            3 => new WrappedCompositeByteBuf((CompositeByteBuf)tracked),
            _ => new SimpleLeakAwareCompositeByteBuf((CompositeByteBuf)tracked, outer)
        };
        try
        {
            ByteBuf Invoke() => write ? buffer.WriteZero(7) : buffer.SetZero(1, 5);
            if (failure) Assert.Same(calls.Error, Assert.Throws<Failure>(() => Invoke()));
            else Assert.Same(buffer, Invoke());
            Assert.Equal(write ? 1 : 0, calls.Write); Assert.Equal(write ? 0 : 1, calls.Set);
            Assert.Equal(write ? -1 : 1, calls.Index); Assert.Equal(write ? 7 : 5, calls.Length);
            Assert.Equal(1, inner.Records); Assert.Equal(0, outer.Records);
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(!failure && write ? 10 : 3, owner.WriterIndex);
            Assert.Equal(owner.ReaderIndex, buffer.ReaderIndex); Assert.Equal(owner.WriterIndex, buffer.WriterIndex);
            if (failure) Assert.Equal(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, ByteBufUtil.GetBytes(owner, 0, 8));
            else for (int i = write ? 3 : 1; i < (write ? 10 : 6); ++i) Assert.Equal(0, owner.GetByte(i));
            owner.SetIndex(0, Math.Min(11, owner.Capacity)).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(3, buffer.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount);
        }
        finally { Assert.True(facade == 2 ? tracked.Release() : buffer.Release()); }
        Assert.Equal(1, inner.Closes); Assert.Equal(facade is 1 or 4 ? 1 : 0, outer.Closes);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedNativeGrowthPreservesOriginalStorageAndCursors()
    {
        var native = new NativeMemoryAllocator(16);
        var calls = new Calls { RejectSet = true };
        ByteBuf owner = Create(1, calls, native);
        try
        {
            Assert.Throws<OutOfMemoryException>(() => owner.WriteZero(17));
            Assert.Equal(1, calls.Write); Assert.Equal(1, calls.Ensure); Assert.Equal(0, calls.Set);
            Assert.Equal(8, owner.Capacity); Assert.Equal(8, native.ReservedBytes);
            Assert.Equal(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, ByteBufUtil.GetBytes(owner, 0, 8));
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex);
            owner.SetIndex(0, 4).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, owner.ReaderIndex); Assert.Equal(3, owner.WriterIndex);
        }
        finally { Assert.True(owner.Release()); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
