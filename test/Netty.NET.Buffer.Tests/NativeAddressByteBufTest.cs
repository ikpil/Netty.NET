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
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public unsafe class NativeAddressByteBufTest
{
    public static IEnumerable<object[]> OwnershipAndViews()
    {
        foreach (bool owned in new[] { false, true })
            for (int view = 0; view < 7; view++) yield return [owned, view];
    }

    [Theory]
    [MemberData(nameof(OwnershipAndViews))]
    public void AddressAliasingViewsAndFinalReleaseRespectOwnership(bool owned, int viewKind)
    {
        byte* address = (byte*)NativeMemory.Alloc(16);
        for (int i = 0; i < 16; i++) address[i] = (byte)i;
        ByteBuf owner = null;
        bool transferred = false;
        try
        {
            owner = Unpooled.WrappedBuffer((nint)address, 16, owned); transferred = owned;
            var memory = owner.AsMemory(0, 16); var manager = Manager(memory);
            Assert.True(owner.IsDirect); Assert.True(owner.IsContiguous); Assert.False(owner.IsReadOnly);
            Assert.Equal(16, owner.Capacity); Assert.Equal(16, owner.MaxCapacity);
            Assert.Equal(0, owner.ReaderIndex); Assert.Equal(16, owner.WriterIndex);
            Assert.Same(UnpooledByteBufAllocator.Default, owner.Allocator);
            owner.SetIndex(2, 14);
            ByteBuf view = viewKind switch
            {
                0 => owner.Slice(3, 8), 1 => owner.Duplicate(), 2 => owner.AsReadOnly(),
                3 => owner.RetainedSlice(3, 8), 4 => owner.RetainedDuplicate(),
                5 => owner.ReadSlice(8), _ => owner.ReadRetainedSlice(8)
            };
            int offset = viewKind is 0 or 3 ? 3 : viewKind is 5 or 6 ? 2 : 0;
            using (MemoryHandle pin = view.AsReadOnlyMemory(0, 1).Pin()) Assert.Equal((nint)(address + offset), (nint)pin.Pointer);
            address[offset] = 0xAB; Assert.Equal(0xAB, view.GetByte(0));
            if (!view.IsReadOnly) { view.SetByte(1, 0xCD); Assert.Equal(0xCD, address[offset + 1]); }
            bool retained = viewKind is 3 or 4 or 6;
            Assert.Equal(retained ? 2 : 1, owner.ReferenceCount);
            if (retained) { Assert.False(owner.Release()); Assert.Equal(0xAB, view.GetByte(0)); }
            Assert.True(view.Release()); Assert.Equal(0, owner.ReferenceCount);
            Assert.True(manager.IsHandleReleased);
            Assert.Throws<IllegalReferenceCountException>(() => view.GetByte(0));
            Assert.Throws<ObjectDisposedException>(() => { _ = memory.Span; });
            if (!owned) { Assert.Equal(0xAB, address[offset]); address[offset] = 0xEF; }
        }
        finally
        {
            if (owner?.ReferenceCount > 0) owner.Release(owner.ReferenceCount);
            if (!transferred) NativeMemory.Free(address);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PinsDelayOwnedFreeAndCopiedHandlesDisposeOnlyOnce(bool owned)
    {
        byte* address = (byte*)NativeMemory.Alloc(16); address[5] = 91;
        ByteBuf b = null; bool transferred = false;
        MemoryHandle first = default, second = default, copied = default;
        try
        {
            b = Unpooled.WrappedBuffer((nint)address, 16, owned); transferred = owned;
            Memory<byte> memory = b.AsMemory(0, 16); var manager = Manager(memory);
            first = memory.Slice(5).Pin(); copied = first; second = memory.Slice(5).Pin();
            Assert.True(b.Release()); Assert.False(manager.IsHandleReleased);
            Assert.Equal(91, *(byte*)first.Pointer);
            first.Dispose(); copied.Dispose(); Assert.False(manager.IsHandleReleased);
            Assert.Equal(91, *(byte*)second.Pointer);
            Assert.Throws<ObjectDisposedException>(() => memory.Pin());
            second.Dispose(); Assert.True(manager.IsHandleReleased);
            if (!owned) Assert.Equal(91, address[5]);
        }
        finally
        {
            first.Dispose(); copied.Dispose(); second.Dispose();
            if (b?.ReferenceCount > 0) b.Release();
            if (!transferred) NativeMemory.Free(address);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CapacityChangesDetachAndPreservePinnedOriginalStorage(bool owned)
    {
        byte* address = (byte*)NativeMemory.Alloc(16);
        for (int i = 0; i < 16; i++) address[i] = (byte)i;
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(true, true, native);
        var manager = new NativeAddressMemoryManager((nint)address, 16);
        var b = new UnpooledDirectByteBuf(allocator, manager, native); manager.TakeOwnership(owned);
        Memory<byte> memory = b.AsMemory(0, 16); MemoryHandle pin = memory.Pin();
        try
        {
            b.SetIndex(3, 14); Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, allocator.Metric.UsedDirectMemory);
            b.Capacity = 8;
            Assert.Equal(3, b.ReaderIndex); Assert.Equal(8, b.WriterIndex); Assert.Equal(16, b.MaxCapacity);
            Assert.Equal(8, native.ReservedBytes); Assert.False(manager.IsHandleReleased);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, b.AsReadOnlyMemory(0, 8).ToArray());
            ((byte*)pin.Pointer)[3] = 99; Assert.Equal(3, b.GetByte(3));
            Assert.Throws<ObjectDisposedException>(() => { _ = memory.Span; });
            pin.Dispose(); Assert.True(manager.IsHandleReleased);
            b.Capacity = 16; Assert.Equal(16, native.ReservedBytes);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, b.AsReadOnlyMemory(0, 8).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Capacity = 17);
            Assert.Equal(0, allocator.Metric.UsedDirectMemory);
        }
        finally { pin.Dispose(); b.Release(); if (!owned) NativeMemory.Free(address); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedDetachmentLeavesOriginalAddressAndOwnershipIntact(bool owned)
    {
        byte* address = (byte*)NativeMemory.AllocZeroed(16); address[4] = 55;
        var native = new NativeMemoryAllocator(0);
        var manager = new NativeAddressMemoryManager((nint)address, 16);
        var b = new UnpooledDirectByteBuf(new UnpooledByteBufAllocator(true, true, native), manager, native);
        manager.TakeOwnership(owned);
        try
        {
            b.SetIndex(3, 14).MarkReaderIndex().MarkWriterIndex();
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = 8);
            Assert.False(manager.IsHandleReleased); Assert.Equal(16, b.Capacity);
            Assert.Equal(55, b.GetByte(4)); Assert.Equal(3, b.ReaderIndex); Assert.Equal(14, b.WriterIndex);
            b.SetIndex(1, 2).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(3, b.ReaderIndex); Assert.Equal(14, b.WriterIndex);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { b.Release(); if (!owned) NativeMemory.Free(address); }
        Assert.True(manager.IsHandleReleased);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CopyIsIndependentNativeStorageWithTheOriginalMaximum(bool owned)
    {
        byte* address = (byte*)NativeMemory.Alloc(16);
        for (int i = 0; i < 16; i++) address[i] = (byte)i;
        ByteBuf b = Unpooled.WrappedBuffer((nint)address, 16, owned); ByteBuf copy = null;
        try
        {
            b.SetIndex(3, 13); copy = b.Copy(4, 8);
            Assert.True(copy.IsDirect); Assert.Equal(8, copy.Capacity); Assert.Equal(16, copy.MaxCapacity);
            Assert.Equal(0, copy.ReaderIndex); Assert.Equal(8, copy.WriterIndex);
            address[4] = 99; Assert.Equal(4, copy.GetByte(0));
            b.Release(); Assert.Equal(11, copy.GetByte(7));
        }
        finally { if (b.ReferenceCount > 0) b.Release(); copy?.Release(); if (!owned) NativeMemory.Free(address); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PrimitiveTransfersAndCompositeConsolidationUseTheExistingNativeKernel(bool owned)
    {
        byte* address = (byte*)NativeMemory.AllocZeroed(16);
        ByteBuf b = Unpooled.WrappedBuffer((nint)address, 16, owned);
        var composite = new CompositeByteBuf();
        try
        {
            b.SetInt(0, 0x01020304).SetLongLE(4, 0x1122334455667788L);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 }, b.AsReadOnlyMemory(0, 12).ToArray());
            using var output = new MemoryStream(); b.GetBytes(0, output, 12);
            Assert.Equal(b.AsReadOnlyMemory(0, 12).ToArray(), output.ToArray());
            composite.AddComponents([b, Unpooled.WrappedBuffer(new byte[] { 0xEE })], true);
            composite.Consolidate(); Assert.Equal(0, b.ReferenceCount);
            Assert.Equal(0x01020304, composite.GetInt(0)); Assert.Equal(0xEE, composite.GetByte(16));
            if (!owned) Assert.Equal(1, address[0]);
        }
        finally { composite.Release(); if (b.ReferenceCount > 0) b.Release(); if (!owned) NativeMemory.Free(address); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeGrowthCanConsolidateTheAddressBackingAnAliasedSource(bool owned)
    {
        byte* address = (byte*)NativeMemory.Alloc(8);
        for (int i = 0; i < 8; i++) address[i] = (byte)i;
        ByteBuf b = Unpooled.WrappedBuffer((nint)address, 8, owned);
        var manager = Manager(b.AsMemory(0, 8));
        var composite = new CompositeByteBuf(1).AddComponent(b, true);
        try
        {
            composite.WriteBytes(b.AsReadOnlySpan(0, 8));
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7 },
                composite.AsReadOnlyMemory(0, 16).ToArray());
            Assert.Equal(0, b.ReferenceCount); Assert.True(manager.IsHandleReleased);
            if (!owned) Assert.Equal(7, address[7]);
        }
        finally { composite.Release(); if (!owned) NativeMemory.Free(address); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ZeroLengthAddressWrappersHaveTheirOwnReferenceCount(bool owned)
    {
        byte* address = (byte*)NativeMemory.Alloc(1);
        ByteBuf b = Unpooled.WrappedBuffer((nint)address, 0, owned);
        var memory = b.AsMemory(0, 0); var manager = Manager(memory);
        try
        {
            Assert.NotSame(Unpooled.EmptyBuffer, b); Assert.Equal(1, b.ReferenceCount);
            Assert.Equal(0, b.Capacity); Assert.Equal(0, b.MaxCapacity);
            Assert.True(b.CanRead(0)); Assert.True(b.CanWrite(0));
            using (MemoryHandle pin = memory.Pin()) Assert.Equal((nint)address, (nint)pin.Pointer);
            Assert.True(b.Release()); Assert.True(manager.IsHandleReleased);
        }
        finally { if (b.ReferenceCount > 0) b.Release(); if (!owned) NativeMemory.Free(address); }
    }

    [Fact]
    public void NullAddressIsOnlyAcceptedForAnEmptyOwnedOrBorrowedRange()
    {
        foreach (bool owned in new[] { false, true })
        {
            Assert.Throws<ArgumentException>(() => Unpooled.WrappedBuffer((nint)0, 1, owned));
            ByteBuf b = Unpooled.WrappedBuffer((nint)0, 0, owned);
            Assert.Equal(0, b.Capacity); Assert.True(b.Release());
        }
    }

    [Fact]
    public void InvalidRangesDoNotTransferOwnership()
    {
        byte* address = (byte*)NativeMemory.Alloc(1); address[0] = 42;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.WrappedBuffer((nint)address, -1, true));
            Assert.Equal(42, address[0]);
            Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.WrappedBuffer((nint)(-2), 4, false));
        }
        finally { NativeMemory.Free(address); }
    }

    [Fact]
    public void PinBoundsAndDisposedManagerRejectAccess()
    {
        byte* address = (byte*)NativeMemory.Alloc(4);
        ByteBuf b = Unpooled.WrappedBuffer((nint)address, 4, true); var manager = Manager(b.AsMemory(0, 4));
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Pin(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Pin(5));
            using (MemoryHandle pin = manager.Pin(4)) Assert.Equal((nint)(address + 4), (nint)pin.Pointer);
            Assert.Throws<NotSupportedException>(() => manager.Unpin());
        }
        finally { b.Release(); }
        Assert.Throws<ObjectDisposedException>(() => manager.GetSpan());
        Assert.Throws<ObjectDisposedException>(() => manager.Pin());
    }

    [Fact]
    public void ParallelPinsKeepStorageAliveThroughFinalRelease()
    {
        byte* address = (byte*)NativeMemory.Alloc(16); address[0] = 73;
        ByteBuf b = Unpooled.WrappedBuffer((nint)address, 16, true);
        Memory<byte> memory = b.AsMemory(0, 16); var manager = Manager(memory);
        var pins = new MemoryHandle[32];
        try
        {
            Parallel.For(0, pins.Length, i => pins[i] = memory.Pin());
            Assert.True(b.Release()); Assert.False(manager.IsHandleReleased);
            Parallel.For(0, pins.Length, i => { Assert.Equal(73, *(byte*)pins[i].Pointer); pins[i].Dispose(); });
            Assert.True(manager.IsHandleReleased);
        }
        finally { foreach (MemoryHandle pin in pins) pin.Dispose(); if (b.ReferenceCount > 0) b.Release(); }
    }

    private static NativeAddressMemoryManager Manager(Memory<byte> memory)
    {
        Assert.True(MemoryMarshal.TryGetMemoryManager<byte, NativeAddressMemoryManager>(memory, out var manager));
        return manager;
    }
}
