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

public class ByteBufAllocationWriteDispatchTest
{
    private sealed class Failure : Exception;
    private sealed class Observation(int mode)
    {
        public readonly Failure Error = new();
        public readonly int Mode = mode;
        public int BufferWrites, SpanWrites, Index, Length;
        public ByteBuf Source;
        public byte[] Input;
        public void Before(ByteBuf source, int index, int length)
        {
            ++BufferWrites; Source = source; Index = index; Length = length;
            Input = ByteBufUtil.GetBytes(source, index, length);
            if (Mode == 1) throw Error;
        }
        public void Before(ReadOnlySpan<byte> source)
        {
            ++SpanWrites; Length = source.Length; Input = source.ToArray();
            if (Mode == 1) throw Error;
        }
        public ByteBuf After(ByteBuf receiver)
        {
            if (Mode == 2) throw Error;
            return Mode == 3 ? Unpooled.EmptyBuffer : receiver;
        }
    }

    private sealed class Heap(ProbeAllocator allocator, int initial, int maximum) : UnpooledHeapByteBuf(allocator, initial, maximum)
    {
        public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
        {
            allocator.Observation.Before(source, sourceIndex, length);
            if (allocator.Observation.Mode != 4) base.WriteBytes(source, sourceIndex, length);
            return allocator.Observation.After(this);
        }
        public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
        {
            allocator.Observation.Before(source);
            if (allocator.Observation.Mode != 4) base.WriteBytes(source);
            return allocator.Observation.After(this);
        }
    }

    private sealed class Direct(ProbeAllocator allocator, int initial, int maximum) : UnpooledDirectByteBuf(allocator, initial, maximum, allocator.Native)
    {
        public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
        {
            allocator.Observation.Before(source, sourceIndex, length);
            if (allocator.Observation.Mode != 4) base.WriteBytes(source, sourceIndex, length);
            return allocator.Observation.After(this);
        }
        public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
        {
            allocator.Observation.Before(source);
            if (allocator.Observation.Mode != 4) base.WriteBytes(source);
            return allocator.Observation.After(this);
        }
    }

    private sealed class ProbeAllocator(bool preferDirect, Observation observation) : AbstractByteBufAllocator(preferDirect)
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly Observation Observation = observation;
        public ByteBuf Last;
        public int Calls, BufferCalls, Initial, Maximum;
        public bool DirectAllocation;
        public override bool IsDirectBufferPooled => false;
        public override ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        { ++BufferCalls; return base.Buffer(initialCapacity, maxCapacity); }
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity) => Allocate(initialCapacity, maxCapacity, false);
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity) => Allocate(initialCapacity, maxCapacity, true);
        private ByteBuf Allocate(int initial, int maximum, bool direct)
        {
            ++Calls; Initial = initial; Maximum = maximum; DirectAllocation = direct;
            if (Observation.Mode == 5) throw Observation.Error;
            return Last = direct ? new Direct(this, initial, maximum) : new Heap(this, initial, maximum);
        }
    }

    private sealed class CopyCalls
    {
        public int Whole, Indexed;
        public bool Fail;
        public readonly Failure Error = new();
        public readonly ByteBuf Result = Unpooled.WrappedBuffer(new byte[] { 201 });
        public ByteBuf Invoke()
        {
            ++Whole;
            if (Fail) throw Error;
            return Result;
        }
    }
    private sealed class CopyHeap(CopyCalls calls) : UnpooledHeapByteBuf(8, 16)
    {
        public override ByteBuf Copy() => calls.Invoke();
        public override ByteBuf Copy(int index, int length) { ++calls.Indexed; throw calls.Error; }
    }
    private sealed class CopyDirect(CopyCalls calls, NativeMemoryAllocator native) : UnpooledDirectByteBuf(8, 16, native)
    {
        public override ByteBuf Copy() => calls.Invoke();
        public override ByteBuf Copy(int index, int length) { ++calls.Indexed; throw calls.Error; }
    }
    private sealed class CopyComposite(CopyCalls calls, NativeMemoryAllocator native) : CompositeByteBuf(16, false, native)
    {
        public override ByteBuf Copy() => calls.Invoke();
        public override ByteBuf Copy(int index, int length) { ++calls.Indexed; throw calls.Error; }
    }
    private sealed class Wrapped(ByteBuf buffer) : WrappedByteBuf(buffer);

    public static IEnumerable<object[]> WholeCopyCases()
    {
        for (int backing = 0; backing < 3; ++backing)
        for (int facade = 0; facade < (backing == 2 ? 3 : 2); ++facade)
        foreach (bool failure in new[] { false, true }) yield return [backing, facade, failure];
    }

    [Theory]
    [MemberData(nameof(WholeCopyCases))]
    public void WholeCopyFacadePreservesTheParentsOriginalFactoryOverride(int backing, int facade, bool failure)
    {
        var native = new NativeMemoryAllocator();
        var calls = new CopyCalls { Fail = failure };
        ByteBuf owner = backing switch
        {
            0 => new CopyHeap(calls),
            1 => new CopyDirect(calls, native),
            _ => new CopyComposite(calls, native).AddComponent(Unpooled.WrappedBuffer(new byte[8]), true)
        };
        owner.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex();
        ByteBuf buffer = facade switch
        {
            0 => new Wrapped(owner),
            1 => Unpooled.UnreleasableBuffer(owner),
            _ => new WrappedCompositeByteBuf((CompositeByteBuf)owner)
        };
        try
        {
            if (failure) Assert.Same(calls.Error, Assert.Throws<Failure>(() => buffer.Copy()));
            else Assert.Same(calls.Result, buffer.Copy());
            Assert.Equal(1, calls.Whole); Assert.Equal(0, calls.Indexed);
            Assert.Equal(2, owner.ReaderIndex); Assert.Equal(7, owner.WriterIndex);
            owner.SetIndex(0, 8).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount); Assert.Equal(1, calls.Result.ReferenceCount);
            Assert.Equal(201, calls.Result.GetByte(0));
        }
        finally
        {
            Assert.True(facade == 1 ? owner.Release() : buffer.Release());
            Assert.True(calls.Result.Release());
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool copy in new[] { false, true })
        foreach (int length in new[] { -1, 0, 2, 5, 6 })
        for (int mode = 0; mode < 6; ++mode) yield return [direct, copy, length, mode];
    }

    // AbstractByteBuf.readBytes allocates and calls writeBytes(ByteBuf,index,length).
    // UnpooledHeapByteBuf.copy uses the array write, represented by ReadOnlySpan in CLR.
    [Theory]
    [MemberData(nameof(Cases))]
    public void AllocationUsesOriginalWriteHookReturnAndFailureOwnership(bool direct, bool copy, int length, int mode)
    {
        var observation = new Observation(mode);
        // An allocated read follows Buffer's preference; Copy follows the source provider.
        var allocator = new ProbeAllocator(!direct, observation);
        ByteBuf source = direct ? new UnpooledDirectByteBuf(allocator, 8, 16, allocator.Native)
            : new UnpooledHeapByteBuf(allocator, 8, 16);
        ByteBuf result = null;
        try
        {
            source.SetBytes(0, new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 });
            source.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex();
            bool invalid = length < 0 || !copy && length > 5;
            bool allocate = !invalid && (copy || length != 0);
            bool failure = allocate && mode is 1 or 2 or 5;
            ByteBuf Invoke() => copy ? source.Copy(2, length) : source.ReadBytes(length);
            if (invalid) Assert.Throws<ArgumentOutOfRangeException>(() => Invoke());
            else if (failure) Assert.Same(observation.Error, Assert.Throws<Failure>(() => Invoke()));
            else result = Invoke();

            Assert.Equal(allocate ? 1 : 0, allocator.Calls);
            Assert.Equal(allocate && !copy ? 1 : 0, allocator.BufferCalls);
            bool wrote = allocate && mode != 5;
            Assert.Equal(wrote && !copy ? 1 : 0, observation.BufferWrites);
            Assert.Equal(wrote && copy ? 1 : 0, observation.SpanWrites);
            if (allocate)
            {
                Assert.Equal(length, allocator.Initial); Assert.Equal(16, allocator.Maximum);
                Assert.Equal(copy ? direct : !direct, allocator.DirectAllocation);
            }
            if (wrote)
            {
                Assert.Equal(length, observation.Length);
                Assert.Equal(new byte[] { 12, 13, 14, 15, 16, 17 }[..length], observation.Input);
                if (!copy) { Assert.Same(source, observation.Source); Assert.Equal(2, observation.Index); }
                Assert.Equal(mode is 1 or 4 ? 0 : length, allocator.Last.WriterIndex);
                Assert.Equal(failure ? 0 : 1, allocator.Last.ReferenceCount);
            }
            if (!invalid && !failure)
            {
                if (!allocate || copy && mode == 3) Assert.Same(Unpooled.EmptyBuffer, result);
                else Assert.Same(allocator.Last, result);
                if (allocate)
                {
                    Assert.Equal(length, allocator.Last.Capacity); Assert.Equal(16, allocator.Last.MaxCapacity);
                    Assert.Equal(copy ? direct : !direct, allocator.Last.IsDirect);
                    Assert.Same(allocator, allocator.Last.Allocator);
                    if (mode != 4)
                    {
                        Assert.Equal(new byte[] { 12, 13, 14, 15, 16, 17 }[..length], ByteBufUtil.GetBytes(allocator.Last));
                        if (length > 0) { allocator.Last.SetByte(0, 201); Assert.Equal(12, source.GetByte(2)); }
                    }
                }
            }
            Assert.Equal(!copy && !invalid && !failure ? 2 + length : 2, source.ReaderIndex);
            Assert.Equal(7, source.WriterIndex); Assert.Equal(8, source.Capacity); Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17 }, ByteBufUtil.GetBytes(source, 0, 8));
            source.SetIndex(0, 8).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(7, source.WriterIndex);
            if (failure) Assert.Equal(direct ? 8 : 0, allocator.Native.ReservedBytes);
        }
        finally
        {
            // A deliberately redirected copy result leaves ownership with the custom allocator probe.
            if (allocator.Last is { ReferenceCount: > 0 }) Assert.True(allocator.Last.Release());
            Assert.True(source.Release());
        }
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }
}
