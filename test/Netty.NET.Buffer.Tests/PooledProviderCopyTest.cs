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

public class PooledProviderCopyTest
{
    private sealed class CopyFailure : Exception { }
    private sealed class Observation(int mode)
    {
        internal readonly CopyFailure Failure = new();
        internal string Method;
        internal ByteBuf Source;
        internal int SourceIndex, Length, Calls;
        internal void Visit(ByteBuf target, string method, int length, ByteBuf source = null, int index = 0)
        {
            ++Calls; Method = method; Source = source; SourceIndex = index; Length = length;
            if (mode == 2 && length > 0) target.SetByte(0, 0xCC);
            if (mode != 0) throw Failure;
        }
    }
    private sealed class HeapDestination(IByteBufAllocator allocator, Observation o, int size, int max)
        : UnpooledHeapByteBuf(allocator, size, max)
    {
        public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
        { o.Visit(this, "Span", source.Length); return base.WriteBytes(source); }
    }
    private sealed class DirectDestination(IByteBufAllocator allocator, Observation o, int size, int max, NativeMemoryAllocator native)
        : UnpooledDirectByteBuf(allocator, size, max, native)
    {
        public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
        { o.Visit(this, "Buffer", length, source, sourceIndex); return base.WriteBytes(source, sourceIndex, length); }
    }
    private sealed class CopyAllocator(Observation observation, NativeMemoryAllocator native) : AbstractByteBufAllocator(true)
    {
        internal readonly List<ByteBuf> Created = [];
        internal bool RequestedDirect;
        internal int RequestedCapacity, RequestedMaximum;
        public override bool IsDirectBufferPooled => false;
        private ByteBuf New(bool direct, int size, int max)
        {
            RequestedDirect = direct; RequestedCapacity = size; RequestedMaximum = max;
            ByteBuf b = direct ? new DirectDestination(this, observation, size, max, native)
                : new HeapDestination(this, observation, size, max);
            Created.Add(b); return b;
        }
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity) => New(false, initialCapacity, maxCapacity);
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity) => New(true, initialCapacity, maxCapacity);
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (int capacity in new[] { 13, 513, 4097, 17000, 131073 })
        for (int view = 0; view < 4; ++view)
        for (int mode = 0; mode < 3; ++mode) yield return [direct, capacity, view, mode];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ProviderCopyPreservesWriteExtensionCoordinatesAndFailureOwnership(bool direct, int capacity, int view, int mode)
    {
        var native = new NativeMemoryAllocator(); var observation = new Observation(mode);
        var allocator = new CopyAllocator(observation, native);
        var sizes = new SizeClasses(8192, 13, 131072, 0);
        using PoolArena arena = direct ? new PoolDirectArena(allocator, sizes, native) : new PoolHeapArena(allocator, sizes);
        ByteBuf guard = arena.Allocate(capacity, 262144), root = arena.Allocate(capacity, 262144);
        ByteBuf target = root, result = null;
        try
        {
            byte[] data = new byte[capacity]; for (int i = 0; i < capacity; ++i) data[i] = (byte)(i % 251);
            root.SetBytes(0, data); guard.SetZero(0, capacity);
            root.SetIndex(3, 10).MarkReaderIndex().MarkWriterIndex();
            if (view == 2) target = root.Slice(2, 9);
            if (view == 3) target = root.RetainedSlice(2, 9);
            int reader = root.ReaderIndex, writer = root.WriterIndex, claims = root.ReferenceCount;
            ByteBuf Copy() => view == 1 ? target.Copy() : target.Copy(view >= 2 ? 1 : 3, 7);
            if (mode != 0) Assert.Same(observation.Failure, Assert.Throws<CopyFailure>(() => Copy()));
            else result = Copy();
            ByteBuf destination = Assert.Single(allocator.Created);
            Assert.Equal(direct, allocator.RequestedDirect);
            Assert.Equal((7, 262144), (allocator.RequestedCapacity, allocator.RequestedMaximum));
            Assert.Equal(direct ? "Buffer" : "Span", observation.Method);
            Assert.Equal(1, observation.Calls); Assert.Equal(7, observation.Length);
            if (direct) { Assert.Same(root, observation.Source); Assert.Equal(3, observation.SourceIndex); }
            Assert.Equal(mode == 0 ? 1 : 0, destination.ReferenceCount);
            if (mode == 0)
            {
                Assert.Same(destination, result); Assert.Equal(direct, result.IsDirect);
                Assert.Same(allocator, result.Allocator); Assert.Equal((0, 7), (result.ReaderIndex, result.WriterIndex));
                Assert.Equal(data.AsSpan(3, 7).ToArray(), result.AsSpan(0, 7).ToArray());
                result.SetByte(0, 77); Assert.Equal(3, root.GetByte(3));
            }
            Assert.Equal((reader, writer, claims), (root.ReaderIndex, root.WriterIndex, root.ReferenceCount));
            root.SetIndex(4, 11).ResetWriterIndex().ResetReaderIndex(); Assert.Equal((3, 10), (root.ReaderIndex, root.WriterIndex));
            Assert.Equal(data, root.AsSpan(0, capacity).ToArray());
            Assert.Equal(new byte[capacity], guard.AsSpan(0, capacity).ToArray());
        }
        finally
        {
            if (view == 3 && !ReferenceEquals(target, root)) target.Release();
            root.Release(); guard.Release();
            foreach (ByteBuf b in allocator.Created) if (b.ReferenceCount > 0) b.Release();
            arena.Dispose(); Assert.Equal(0, native.ReservedBytes);
        }
    }
}
