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
using System.Reflection;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufNativeCopyLeaseTest
{
    private sealed class Failure : Exception;

    private sealed class ProbeAllocator : AbstractByteBufAllocator
    {
        public readonly NativeMemoryAllocator SourceNative = new(), TargetNative = new();
        public readonly Failure Error = new();
        public Action Callback;
        public bool FailAllocation, FailWrite;
        public int Calls, Initial, Maximum, Writes;
        public byte[] Input;
        public ByteBuf Last;
        public override bool IsDirectBufferPooled => false;
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
            => throw new InvalidOperationException("Native copy must use DirectBuffer.");
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            ++Calls; Initial = initialCapacity; Maximum = maxCapacity;
            Callback?.Invoke();
            if (FailAllocation) throw Error;
            return Last = new Target(this, initialCapacity, maxCapacity);
        }
    }

    private sealed class Target(ProbeAllocator allocator, int initial, int maximum)
        : UnpooledDirectByteBuf(allocator, initial, maximum, allocator.TargetNative)
    {
        public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
        {
            ++allocator.Writes; allocator.Input = source.ToArray();
            base.WriteBytes(source);
            if (allocator.FailWrite) throw allocator.Error;
            return this;
        }
    }

    private sealed class ReadOnlyDirect(ProbeAllocator allocator)
        : UnpooledDirectByteBuf(allocator, 16, 64, allocator.SourceNative)
    {
        public bool ReadOnly;
        public override bool IsReadOnly => ReadOnly;
    }

    private static readonly byte[] Bytes = [128, 129, 130, 131, 132, 133, 134, 135,
        136, 137, 138, 139, 140, 141, 142, 143];

    private static (ByteBuf Root, ByteBuf View, int Offset) Create(ProbeAllocator allocator, int form)
    {
        ByteBuf root = form == 1 ? new ReadOnlyDirect(allocator)
            : new UnpooledDirectByteBuf(allocator, 16, 64, allocator.SourceNative);
        root.SetBytes(0, Bytes).SetIndex(3, 11).MarkReaderIndex().MarkWriterIndex();
        if (root is ReadOnlyDirect readOnly) readOnly.ReadOnly = true;
        ByteBuf view = form switch
        {
            2 => root.AsReadOnly(),
            3 => root.Duplicate(),
            4 => root.Slice(2, 10),
            5 => root.Slice(1, 12).Slice(2, 8),
            6 => root.Slice(2, 10).AsReadOnly(),
            _ => root
        };
        if (!ReferenceEquals(root, view)) view.SetIndex(1, view.Capacity - 1).MarkReaderIndex().MarkWriterIndex();
        return (root, view, form == 5 ? 3 : form is 4 or 6 ? 2 : 0);
    }

    private static void AssertIndices(ByteBuf root, ByteBuf view)
    {
        int reader = ReferenceEquals(root, view) ? 3 : 1;
        int writer = ReferenceEquals(root, view) ? 11 : Math.Min(view.Capacity, 16) - 1;
        Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
        Assert.Equal(3, root.ReaderIndex); Assert.Equal(11, root.WriterIndex);
        view.SetIndex(0, view.Capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(reader, view.ReaderIndex); Assert.Equal(writer, view.WriterIndex);
        root.SetIndex(0, root.Capacity).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(3, root.ReaderIndex); Assert.Equal(11, root.WriterIndex);
    }

    public static IEnumerable<object[]> PortableCases()
    {
        for (int form = 0; form < 7; ++form)
        for (int range = 0; range < 5; ++range)
        for (int failure = 0; failure < 3; ++failure) yield return [form, range, failure];
    }

    // UnpooledDirectByteBuf.copy duplicates a read source, then calls the allocator's
    // directBuffer(length,maxCapacity).writeBytes(ByteBuffer). ReadOnlySpan is the CLR view.
    [Theory]
    [MemberData(nameof(PortableCases))]
    public void CopyReadsCapacityRangeWithoutRequestingWriteAccess(int form, int range, int failure)
    {
        var allocator = new ProbeAllocator { FailAllocation = failure == 1, FailWrite = failure == 2 };
        var (root, view, offset) = Create(allocator, form);
        int index = range switch { 0 => -1, 1 => view.Capacity, 4 => view.Capacity - 1, _ => 3 };
        int length = range switch { 0 or 1 => 0, 2 => 4, 3 => view.Capacity - 3, _ => 2 };
        bool invalid = range is 0 or 4;
        try
        {
            if (invalid) Assert.Throws<ArgumentOutOfRangeException>(() => view.Copy(index, length));
            else if (failure != 0) Assert.Same(allocator.Error, Assert.Throws<Failure>(() => view.Copy(index, length)));
            else
            {
                ByteBuf result = view.Copy(index, length);
                Assert.Same(allocator.Last, result); Assert.Same(allocator, result.Allocator);
                Assert.True(result.IsDirect); Assert.False(result.IsReadOnly);
                Assert.Equal(length, result.Capacity); Assert.Equal(64, result.MaxCapacity);
                Assert.Equal(0, result.ReaderIndex); Assert.Equal(length, result.WriterIndex);
                Assert.Equal(Bytes.AsSpan(offset + index, length).ToArray(), result.AsReadOnlySpan(0, length).ToArray());
                if (length > 0)
                {
                    result.SetByte(0, 7);
                    Assert.Equal(Bytes[offset + index], root.GetByte(offset + index));
                }
            }
            Assert.Equal(invalid ? 0 : 1, allocator.Calls);
            Assert.Equal(!invalid && failure != 1 ? 1 : 0, allocator.Writes);
            if (!invalid)
            {
                Assert.Equal(length, allocator.Initial); Assert.Equal(64, allocator.Maximum);
                if (failure != 1) Assert.Equal(Bytes.AsSpan(offset + index, length).ToArray(), allocator.Input);
            }
            // CLR policy releases a newly allocated result after a failed write;
            // Java leaves that result owned by the allocator/caller's failure path.
            if (!invalid && failure == 2) Assert.Equal(0, allocator.Last.ReferenceCount);
            Assert.Equal(16, allocator.SourceNative.ReservedBytes);
            Assert.Equal(1, root.ReferenceCount); AssertIndices(root, view);
        }
        finally
        {
            if (allocator.Last is { ReferenceCount: > 0 }) allocator.Last.Release();
            root.Release();
            Assert.Equal(0, allocator.SourceNative.ReservedBytes);
            Assert.Equal(0, allocator.TargetNative.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> LeaseCases()
    {
        foreach (int form in new[] { 0, 2, 3, 4, 5, 6 })
        for (int action = 0; action < 4; ++action) yield return [form, action];
    }

    private static int Pins(NativeMemoryOwner owner)
        => (int)typeof(NativeMemoryOwner).GetProperty("PinCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    // CLR-specific lifetime contract: an already borrowed native span must stay
    // physically valid across allocator callbacks. This is not a Java reentrancy claim.
    [Theory]
    [MemberData(nameof(LeaseCases))]
    public void CopyKeepsOldStorageAliveAcrossAllocationCallbacks(int form, int action)
    {
        var allocator = new ProbeAllocator { FailWrite = action == 3 };
        var (root, view, offset) = Create(allocator, form);
        Assert.True(NativeMemoryOwner.TryGetOwner(root.AsReadOnlyMemory(0, 16), out var owner));
        byte[] expected = Bytes.AsSpan(offset + 3, 4).ToArray();
        allocator.Callback = () =>
        {
            // Fail BEFORE resize/release on the broken implementation, avoiding a
            // test that would deliberately read a freed native address.
            Assert.Equal(1, Pins(owner)); Assert.Equal(1, root.ReferenceCount);
            if (action is 0 or 2)
            {
                root.Capacity = 32; root.SetByte(offset + 3, 99);
                Assert.Equal(48, allocator.SourceNative.ReservedBytes);
                if (action == 2) throw allocator.Error;
            }
            else
            {
                Assert.True(root.Release()); Assert.Equal(0, root.ReferenceCount);
                Assert.Equal(16, allocator.SourceNative.ReservedBytes);
            }
            Assert.Equal(1, Pins(owner));
        };
        try
        {
            if (action is 2 or 3) Assert.Same(allocator.Error, Assert.Throws<Failure>(() => view.Copy(3, 4)));
            else
            {
                ByteBuf result = view.Copy(3, 4);
                Assert.Same(allocator.Last, result);
                Assert.Equal(expected, result.AsReadOnlySpan(0, 4).ToArray());
                Assert.Equal(0, result.ReaderIndex); Assert.Equal(4, result.WriterIndex);
            }
            Assert.Equal(0, Pins(owner));
            Assert.Equal(action is 0 or 2 ? 32 : 0, allocator.SourceNative.ReservedBytes);
            Assert.Equal(1, allocator.Calls); Assert.Equal(action == 2 ? 0 : 1, allocator.Writes);
            if (action != 2) Assert.Equal(expected, allocator.Input);
            if (action == 3) Assert.Equal(0, allocator.Last.ReferenceCount);
            if (action is 0 or 2)
            {
                Assert.Equal(99, root.GetByte(offset + 3)); AssertIndices(root, view);
            }
        }
        finally
        {
            if (allocator.Last is { ReferenceCount: > 0 }) allocator.Last.Release();
            if (root.ReferenceCount > 0) root.Release();
            Assert.Equal(0, allocator.SourceNative.ReservedBytes);
            Assert.Equal(0, allocator.TargetNative.ReservedBytes);
        }
    }
}
