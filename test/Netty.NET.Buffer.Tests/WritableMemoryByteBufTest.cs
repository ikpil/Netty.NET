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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class WritableMemoryByteBufTest
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalWrappedRangeCapturesPositionAndAliasesBothDirections(int kind)
    {
        using var storage = new ExternalStorage(kind, 16);
        storage.Memory.Span.Fill(0xee);
        ByteBuf b = Unpooled.WrappedBuffer(storage.Memory.Slice(4, 8));
        try
        {
            Assert.False(b.IsReadOnly); Assert.Equal(kind == 1, b.IsDirect);
            Assert.Equal(8, b.Capacity); Assert.Equal(8, b.MaxCapacity);
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(8, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
            b.SetLong(0, 0x0102030405060708L);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, storage.Memory.Slice(4, 8).ToArray());
            storage.Memory.Span[4] = 9; Assert.Equal(9, b.GetByte(0));
            Memory<byte> view = b.AsMemory(1, 4); view.Span[0] = 10;
            Assert.Equal(10, storage.Memory.Span[5]); Assert.Equal(0xee, storage.Memory.Span[3]);
            Assert.Equal(0xee, storage.Memory.Span[12]);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetByte(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetByte(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.EnsureWritable(1));
        }
        finally { b.Release(); }
        Assert.Equal(9, storage.Memory.Span[4]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void PrimitiveBulkAndOverlappingWritesUseTheExternalRange(int kind)
    {
        using var storage = new ExternalStorage(kind, 32);
        ByteBuf b = Unpooled.WrappedBuffer(storage.Memory.Slice(2, 24));
        try
        {
            b.Clear().WriteByte(0xff).WriteShortLE(0xfedc).WriteMedium(0xabcdef)
                .WriteIntLE(unchecked((int)0xfedcba98)).WriteLong(long.MinValue);
            Assert.Equal(0xff, b.ReadByte()); Assert.Equal(unchecked((short)0xfedc), b.ReadShortLE());
            Assert.Equal(0xabcdef, b.ReadUnsignedMedium()); Assert.Equal(unchecked((int)0xfedcba98), b.ReadIntLE());
            Assert.Equal(long.MinValue, b.ReadLong());
            for (int i = 0; i < 8; ++i) b.SetByte(i, i);
            b.SetBytes(2, b.AsSpan(0, 6));
            Assert.Equal(new byte[] { 0, 1, 0, 1, 2, 3, 4, 5 }, b.AsMemory(0, 8).ToArray());
            b.SetBytes(0, b.AsSpan(2, 6));
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 4, 5 }, b.AsMemory(0, 8).ToArray());
            Assert.Equal(0, storage.Memory.Span[0]); Assert.Equal(0, storage.Memory.Span[26]);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CopiesAndViewsKeepStoragePolicyAndReferenceOwnership(int kind)
    {
        using var storage = new ExternalStorage(kind, 16);
        for (int i = 0; i < 16; i++) storage.Memory.Span[i] = (byte)i;
        ByteBuf b = Unpooled.WrappedBuffer(storage.Memory.Slice(2, 8));
        ByteBuf retained = b.RetainedSlice(1, 5);
        try
        {
            retained.ReaderIndex = 1; Assert.Equal(0, b.ReaderIndex);
            ByteBuf readOnly = b.AsReadOnly(); Assert.True(readOnly.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => readOnly.SetByte(0, 99));
            b.SetByte(0, 99); Assert.Equal(99, readOnly.GetByte(0));
            ByteBuf copy = retained.Copy();
            try
            {
                Assert.Same(b.Allocator, copy.Allocator); Assert.Equal(kind == 1, copy.IsDirect);
                Assert.Equal(kind == 0 ? 16 : 8, copy.MaxCapacity);
                Assert.Equal(new byte[] { 4, 5, 6, 7 }, ByteBufUtil.GetBytes(copy));
                copy.SetByte(0, 42); Assert.Equal(4, retained.GetByte(1));
            }
            finally { copy.Release(); }
            Assert.False(b.Release()); Assert.Equal(3, retained.GetByte(0));
        }
        finally { if (b.ReferenceCount == 2) b.Release(); retained.Release(); }
        Assert.Throws<IllegalReferenceCountException>(() => b.GetByte(0));
        Assert.Equal(99, storage.Memory.Span[2]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task StreamsAndCompositeWritesReachBorrowedMemory(int kind)
    {
        using var storage = new ExternalStorage(kind, 12);
        ByteBuf b = Unpooled.WrappedBuffer(storage.Memory.Slice(2, 8));
        try
        {
            using var input = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            Assert.Equal(4, b.SetBytes(0, input, 4));
            using var output = new MemoryStream(); await b.GetBytesAsync(0, output, 4, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.ToArray());
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
            CompositeByteBuf composite = Unpooled.CompositeBuffer();
            try
            {
                composite.AddComponent(b.RetainedSlice(0, 4), true).AddComponent(b.RetainedSlice(4, 4), true);
                composite.SetInt(2, 0x12345678);
                Assert.Equal(0x12345678, b.GetInt(2)); Assert.Equal(0x12, storage.Memory.Span[4]);
                composite.Consolidate(); composite.SetByte(0, 99); Assert.Equal(1, b.GetByte(0));
                Assert.Equal(1, b.ReferenceCount);
            }
            finally { composite.Release(); }
        }
        finally { b.Release(); }
    }

    [Fact]
    public void OriginalEmptyMemoryFactoryReturnsSharedSentinel()
    {
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Memory<byte>.Empty));
        using var storage = new ExternalStorage(1, 8);
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(storage.Memory.Slice(8, 0)));
    }

    [Fact]
    public unsafe void OriginalDirectSliceAddressMapsToPinAtSelectedOffset()
    {
        var native = new NativeMemoryAllocator(16); using NativeMemoryOwner owner = native.Allocate(16);
        ByteBuf b = Unpooled.WrappedBuffer(owner.Memory.Slice(4, 8));
        try
        {
            using MemoryHandle original = owner.Memory.Pin(); using MemoryHandle selected = b.AsMemory(0, 8).Pin();
            Assert.True((byte*)original.Pointer + 4 == selected.Pointer);
            b.SetByte(0, 42); Assert.Equal(42, *((byte*)original.Pointer + 4));
        }
        finally { b.Release(); }
        Assert.Equal(16, native.ReservedBytes);
    }

    [Fact]
    public void WrappedNativeShrinkDetachesWithoutDisposingExternalOwner()
    {
        var external = new NativeMemoryAllocator(16); using NativeMemoryOwner owner = external.Allocate(12);
        for (int i = 0; i < 12; i++) owner.Memory.Span[i] = (byte)i;
        ByteBuf b = Unpooled.WrappedBuffer(owner.Memory.Slice(2, 8));
        try
        {
            Memory<byte> original = b.AsMemory(0, 8); b.SetIndex(6, 8); b.Capacity = 4;
            Assert.Equal(4, b.ReaderIndex); Assert.Equal(4, b.WriterIndex); Assert.Equal(8, b.MaxCapacity);
            Assert.Equal(new byte[] { 2, 3, 4, 5 }, b.AsMemory(0, 4).ToArray());
            b.SetByte(0, 99); Assert.Equal(2, original.Span[0]); owner.Memory.Span[3] = 77; Assert.Equal(3, b.GetByte(1));
            b.Capacity = 8; Assert.Equal(new byte[] { 99, 3, 4, 5, 0, 0, 0, 0 }, b.AsMemory(0, 8).ToArray());
            Assert.Equal(12, external.ReservedBytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Capacity = 9);
        }
        finally { b.Release(); }
        Assert.Equal(12, external.ReservedBytes); Assert.Equal(77, owner.Memory.Span[3]);
    }

    [Fact]
    public void WrappedNativeSameCapacityKeepsAliasingAndRawReplacementIsUncharged()
    {
        var external = new NativeMemoryAllocator(16); using NativeMemoryOwner owner = external.Allocate(8);
        var native = new NativeMemoryAllocator(64); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = new UnpooledDirectByteBuf(a, owner.Memory, 16, native);
        try
        {
            b.Capacity = 8; b.SetByte(0, 42); Assert.Equal(42, owner.Memory.Span[0]);
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.Metric.UsedDirectMemory);
            b.Capacity = 12; Assert.Equal(12, native.ReservedBytes); Assert.Equal(0, a.Metric.UsedDirectMemory);
            ByteBuf copy = b.Copy(0, 4);
            try
            {
                Assert.Same(a, copy.Allocator); Assert.Equal(16, copy.MaxCapacity); Assert.Equal(4, a.Metric.UsedDirectMemory);
                Assert.Equal(16, native.ReservedBytes); Assert.Equal(42, copy.GetByte(0));
            }
            finally { copy.Release(); }
            b.Capacity = 0; Assert.Equal(1, native.ReservedBytes); Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex);
        }
        finally { b.Release(); }
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.Metric.UsedDirectMemory); Assert.Equal(8, external.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AllocationFailurePreservesBorrowedStorageAndIndices(bool shrink)
    {
        var external = new NativeMemoryAllocator(8); using NativeMemoryOwner owner = external.Allocate(8);
        owner.Memory.Span.Fill(7); var native = new NativeMemoryAllocator(0);
        ByteBuf b = new UnpooledDirectByteBuf(new UnpooledByteBufAllocator(), owner.Memory, 16, native);
        try
        {
            b.SetIndex(6, 8); b.MarkReaderIndex().MarkWriterIndex();
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = shrink ? 4 : 12);
            Assert.Equal(8, b.Capacity); Assert.Equal(6, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
            b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(7, b.GetByte(0));
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(8, external.ReservedBytes);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void FailedCopyOfDisposedBorrowedMemoryReleasesReplacement()
    {
        var external = new NativeMemoryAllocator(8); NativeMemoryOwner owner = external.Allocate(8);
        var native = new NativeMemoryAllocator(32); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = new UnpooledDirectByteBuf(a, owner.Memory, 16, native);
        owner.Dispose();
        try
        {
            Assert.Throws<ObjectDisposedException>(() => b.Capacity = 12);
            Assert.Equal(8, b.Capacity); Assert.Equal(8, b.WriterIndex); Assert.Equal(0, native.ReservedBytes);
            Assert.Throws<ObjectDisposedException>(() => b.GetByte(0));
        }
        finally { b.Release(); }
        Assert.Equal(0, external.ReservedBytes); Assert.Equal(0, a.Metric.UsedDirectMemory);
    }

    [Fact]
    public unsafe void PinsPreserveBorrowedAndReplacementStorageAcrossRelease()
    {
        var external = new NativeMemoryAllocator(8); NativeMemoryOwner owner = external.Allocate(8);
        owner.Memory.Span.Fill(7); var native = new NativeMemoryAllocator(32);
        ByteBuf b = new UnpooledDirectByteBuf(new UnpooledByteBufAllocator(), owner.Memory, 16, native);
        MemoryHandle original = b.AsMemory(0, 8).Pin(); MemoryHandle replacement = default;
        try
        {
            b.Capacity = 12; replacement = b.AsMemory(0, 12).Pin(); b.SetByte(0, 99);
            Assert.Equal(7, *(byte*)original.Pointer); Assert.Equal(99, *(byte*)replacement.Pointer);
            b.Release(); owner.Dispose(); Assert.Equal(8, external.ReservedBytes); Assert.Equal(12, native.ReservedBytes);
            Assert.Equal(7, *(byte*)original.Pointer); Assert.Equal(99, *(byte*)replacement.Pointer);
        }
        finally { if (b.ReferenceCount > 0) b.Release(); owner.Dispose(); original.Dispose(); replacement.Dispose(); }
        Assert.Equal(0, external.ReservedBytes); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GrowingAliasedWritePinsInitialAndOwnedStorage(bool detachFirst)
    {
        var external = new NativeMemoryAllocator(4); using NativeMemoryOwner owner = external.Allocate(4);
        new byte[] { 1, 2, 3, 4 }.CopyTo(owner.Memory.Span);
        var native = new NativeMemoryAllocator(32);
        ByteBuf b = new UnpooledDirectByteBuf(new UnpooledByteBufAllocator(), owner.Memory, 16, native);
        try
        {
            if (detachFirst) { b.Capacity = 8; b.Capacity = 4; }
            ReadOnlySpan<byte> source = b.AsSpan(0, 4); b.Duplicate().WriteBytes(source);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, b.AsMemory(0, 8).ToArray());
            Assert.Equal(4, b.WriterIndex); Assert.Equal(4, external.ReservedBytes);
        }
        finally { b.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void InvalidWrappingArgumentsDoNotAllocateOrDisposeExternalStorage()
    {
        var native = new NativeMemoryAllocator(8); using NativeMemoryOwner owner = native.Allocate(8);
        var a = new UnpooledByteBufAllocator();
        Assert.Throws<ArgumentNullException>(() => new UnpooledDirectByteBuf(null, owner.Memory, 8));
        Assert.Throws<ArgumentException>(() => new UnpooledDirectByteBuf(a, new byte[8].AsMemory(), 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnpooledDirectByteBuf(a, owner.Memory, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnpooledDirectByteBuf(a, owner.Memory, -1));
        Assert.Equal(8, native.ReservedBytes); owner.Memory.Span[0] = 42;
    }

    [Fact]
    public void UnknownManagerHasFixedCapacityAndFinalReleaseDoesNotDisposeIt()
    {
        using var storage = new ExternalStorage(2, 8); ByteBuf b = Unpooled.WrappedBuffer(storage.Memory);
        Assert.False(b.IsDirect); Assert.Throws<NotSupportedException>(() => b.Capacity = 4);
        Assert.Throws<NotSupportedException>(() => b.Capacity = 8); Assert.Equal(0, b.EnsureWritable(0, false));
        Assert.Equal(1, b.EnsureWritable(1, true)); b.Release();
        Assert.Throws<IllegalReferenceCountException>(() => b.SetByte(0, 1)); storage.Memory.Span[0] = 42;
    }

    private sealed class ExternalStorage : IDisposable
    {
        private readonly NativeMemoryOwner _native;
        private readonly ArrayMemoryManager _manager;
        public Memory<byte> Memory { get; }
        public ExternalStorage(int kind, int length)
        {
            if (kind == 1) { _native = new NativeMemoryAllocator(1024).Allocate(length, clear: true); Memory = _native.Memory; }
            else if (kind == 2) { _manager = new ArrayMemoryManager(length); Memory = _manager.Memory; }
            else Memory = new byte[length];
        }
        public void Dispose() { _native?.Dispose(); ((IDisposable)_manager)?.Dispose(); }
    }

    private sealed class ArrayMemoryManager : MemoryManager<byte>
    {
        private byte[] _array;
        public ArrayMemoryManager(int length) => _array = new byte[length];
        public override Span<byte> GetSpan() => _array ?? throw new ObjectDisposedException(nameof(ArrayMemoryManager));
        public override MemoryHandle Pin(int elementIndex = 0) => _array.AsMemory(elementIndex).Pin();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) => _array = null;
    }
}
