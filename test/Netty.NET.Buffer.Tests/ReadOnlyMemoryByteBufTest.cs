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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ReadOnlyMemoryByteBufTest
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalNonzeroPositionAndRoundTripCaptureOnlyTheSelectedRange(int kind)
    {
        using var storage = new ExternalStorage(kind, 16);
        storage.Memory.Span.Fill(0xee); storage.Memory.Span.Slice(2, 8).Clear();
        storage.Memory.Span[5] = 1; storage.Memory.Span[9] = 2;
        // Test for https://github.com/netty/netty/issues/1708
        ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory.Slice(2, 8));
        try
        {
            Assert.True(b.IsReadOnly); Assert.Equal(kind == 1, b.IsDirect);
            Assert.Equal(8, b.Capacity); Assert.Equal(8, b.MaxCapacity); Assert.Equal(0, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
            Assert.Equal(1, b.ReferenceCount); Assert.Null(b.Unwrap()); Assert.Same(b, b.AsReadOnly());
            Assert.Equal(1, b.ReadInt());
            ReadOnlyMemory<byte> remaining = b.AsReadOnlyMemory(b.ReaderIndex, b.ReadableBytes);
            // Ensure this can be accessed without throwing a BufferUnderflowException
            Assert.Equal(new byte[] { 0, 0, 0, 2 }, remaining.ToArray());
            storage.Memory.Span[9] = 3; Assert.Equal(3, b.GetInt(4)); Assert.Equal(3, remaining.Span[3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetByte(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetByte(-1));
        }
        finally { b.Release(); }
        Assert.Equal(0xee, storage.Memory.Span[0]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalPrimitiveAndBulkReadsUseTheBorrowedMemory(int kind)
    {
        using var storage = new ExternalStorage(kind, 16);
        for (int i = 0; i < 16; i++) storage.Memory.Span[i] = (byte)(i + 1);
        ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory);
        try
        {
            Assert.Equal(1, b.ReadByte()); Assert.Equal(0x0203, b.ReadShort());
            Assert.Equal(0x040506, b.ReadUnsignedMedium()); Assert.Equal(0x0708090a, b.ReadInt());
            Assert.Equal(0x0102030405060708L, b.GetLong(0)); Assert.Equal(0x0807060504030201L, b.GetLongLE(0));
            Assert.Equal(0x030201, b.GetUnsignedMediumLE(0)); Assert.Equal(0x04030201, b.GetIntLE(0));
            Assert.Equal(10, b.ReaderIndex); Assert.Equal(16, b.WriterIndex);
            // Ensure destination buffer is bigger then what is in the ByteBuf.
            Assert.Throws<ArgumentOutOfRangeException>(() => b.GetBytes(0, new byte[17].AsSpan()));
            byte[] destination = new byte[18]; b.GetBytes(0, destination.AsSpan(1, 16));
            Assert.Equal(storage.Memory.ToArray(), destination[1..17]); Assert.Equal(10, b.ReaderIndex);
            Assert.Equal(storage.Memory.ToArray(), b.AsReadOnlySequence(0, 16).ToArray());
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalWriteRejectionAlsoCoversEmptyWritesAndMemoryBorrows(int kind)
    {
        using var storage = new ExternalStorage(kind, 8); storage.Memory.Span.Fill(7);
        ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory);
        try
        {
            b.Clear(); Assert.False(b.CanWrite(0)); Assert.False(b.CanWrite(1));
            Assert.Equal(1, b.EnsureWritable(0, false)); Assert.Equal(1, b.EnsureWritable(8, true));
            Assert.Throws<NotSupportedException>(() => b.EnsureWritable(0));
            Assert.Throws<NotSupportedException>(() => b.Capacity = b.Capacity);
            Assert.Throws<NotSupportedException>(() => b.SetByte(0, 1));
            Assert.Throws<NotSupportedException>(() => b.SetShort(0, 1));
            Assert.Throws<NotSupportedException>(() => b.SetMedium(0, 1));
            Assert.Throws<NotSupportedException>(() => b.SetInt(0, 1));
            Assert.Throws<NotSupportedException>(() => b.SetLong(0, 1));
            Assert.Throws<NotSupportedException>(() => b.SetBytes(0, Array.Empty<byte>()));
            Assert.Throws<NotSupportedException>(() => b.SetZero(0, 0));
            Assert.Throws<NotSupportedException>(() => b.AsMemory(0, 0));
            using var input = new MemoryStream(new byte[] { 1 });
            Assert.Throws<NotSupportedException>(() => b.SetBytes(0, input, 1)); Assert.Equal(0, input.Position);
            Assert.Equal(Enumerable.Repeat((byte)7, 8), storage.Memory.ToArray()); Assert.Equal(0, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalCopiesKeepCreatorAndStoragePolicyButAreWritableAndIndependent(int kind)
    {
        using var storage = new ExternalStorage(kind, 16);
        for (int i = 0; i < 16; i++) storage.Memory.Span[i] = (byte)i;
        var native = new NativeMemoryAllocator(128); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = new ReadOnlyMemoryByteBuf(a, storage.Memory);
        try
        {
            b.SetIndex(1, 10).MarkReaderIndex().MarkWriterIndex();
            ByteBuf[] views = { b, b.Duplicate(), b.Slice(1, 9), b.Slice(0, 12).Duplicate().AsReadOnly() };
            foreach (ByteBuf view in views)
            {
                ByteBuf copy = view.Copy();
                try
                {
                    Assert.Same(a, copy.Allocator); Assert.Equal(kind == 1, copy.IsDirect); Assert.False(copy.IsReadOnly);
                    Assert.Equal(int.MaxValue, copy.MaxCapacity); Assert.Equal(view.ReadableBytes, copy.WriterIndex);
                    Assert.Equal(ByteBufUtil.GetBytes(view), ByteBufUtil.GetBytes(copy));
                    copy.SetByte(0, 99); Assert.NotEqual(99, view.GetByte(view.ReaderIndex));
                }
                finally { copy.Release(); }
            }
            ByteBuf offsetCopy = b.Copy(1, 9);
            try { Assert.Equal(Enumerable.Range(1, 9).Select(i => (byte)i), ByteBufUtil.GetBytes(offsetCopy)); }
            finally { offsetCopy.Release(); }
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(10, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
            Assert.Equal(0, a.Metric.UsedHeapMemory); Assert.Equal(0, a.Metric.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalDerivedViewsShareReleaseAndIndependentIndices(int kind)
    {
        using var storage = new ExternalStorage(kind, 8);
        Func<ByteBuf, ByteBuf>[] create = { b => b.AsReadOnly(), b => b.Duplicate(), b => b.Slice(), b => b.Duplicate().Duplicate(), b => b.Duplicate().Slice(), b => b.Slice().Duplicate(), b => b.Slice().Slice() };
        foreach (var factory in create)
        {
            ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory); b.WriterIndex = 4;
            ByteBuf derived = factory(b); Assert.True(derived.IsReadOnly); Assert.Same(derived, derived.AsReadOnly());
            Assert.True(derived.Release()); Assert.Equal(0, b.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => b.GetByte(0));
            Assert.Throws<IllegalReferenceCountException>(() => derived.GetByte(0));
        }
        ByteBuf root = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory);
        ByteBuf retained = root.RetainedSlice(1, 4); Assert.False(root.Release());
        retained.ReaderIndex = 1; Assert.Equal(0, root.ReaderIndex); Assert.True(retained.Release());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CompositeOwnsWrapperReferencesAndConsolidationCreatesWritableStorage(int kind)
    {
        using var storage = new ExternalStorage(kind, 8);
        for (int i = 0; i < 8; i++) storage.Memory.Span[i] = (byte)(i + 1);
        ByteBuf first = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory[..3]);
        ByteBuf second = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory[3..]);
        var a = new UnpooledByteBufAllocator(); CompositeByteBuf b = a.CompositeHeapBuffer();
        try
        {
            b.AddComponent(first, true).AddComponent(second, true);
            Assert.Equal(0x02030405, b.GetInt(1)); Assert.Equal(storage.Memory.ToArray(), b.ReadableSequence.ToArray());
            b.Consolidate(); Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
            Assert.Equal(8, a.Metric.UsedHeapMemory); b.SetByte(0, 99);
            Assert.Equal(1, storage.Memory.Span[0]); Assert.Equal(99, b.GetByte(0));
        }
        finally { b.Release(); }
        Assert.Equal(0, a.Metric.UsedHeapMemory); Assert.Equal(1, storage.Memory.Span[0]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task OriginalConcurrentCopiesUseIndependentRangesWithoutSharedCursor(int kind)
    {
        using var storage = new ExternalStorage(kind, 32);
        for (int i = 0; i < 32; i++) storage.Memory.Span[i] = (byte)i;
        var a = new UnpooledByteBufAllocator(); ByteBuf b = new ReadOnlyMemoryByteBuf(a, storage.Memory);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() =>
            {
                ByteBuf copy = b.Copy(i, 32 - i);
                try { Assert.Equal(Enumerable.Range(i, 32 - i).Select(v => (byte)v), ByteBufUtil.GetBytes(copy)); }
                finally { copy.Release(); }
            }, TestContext.Current.CancellationToken)));
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(32, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
            Assert.Equal(0, a.Metric.UsedHeapMemory); Assert.Equal(0, a.Metric.UsedDirectMemory);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task SynchronousAndAsynchronousStreamOutputUseReadOnlyContent(int kind)
    {
        using var storage = new ExternalStorage(kind, 8);
        for (int i = 0; i < 8; i++) storage.Memory.Span[i] = (byte)i;
        ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)storage.Memory);
        try
        {
            using var output = new MemoryStream(); b.GetBytes(1, output, 4);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.ToArray()); Assert.Equal(0, b.ReaderIndex);
            output.SetLength(0); await b.ReadBytesAsync(output, 5, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, output.ToArray()); Assert.Equal(5, b.ReaderIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void EmptyFactorySentinelAndExplicitEmptyOwnerHaveDifferentOwnership()
    {
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(ReadOnlyMemory<byte>.Empty));
        var a = new UnpooledByteBufAllocator(); ByteBuf b = new ReadOnlyMemoryByteBuf(a, default);
        Assert.True(b.IsReadOnly); Assert.Equal(0, b.Capacity); Assert.Equal(0, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Copy(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Copy(int.MaxValue, 1));
        Assert.True(b.Release());
        Assert.Throws<IllegalReferenceCountException>(() => b.Copy(0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => b.EnsureWritable(0, false));
        Assert.Throws<ArgumentNullException>(() => new ReadOnlyMemoryByteBuf(null, default));
    }

    [Fact]
    public void FinalWrapperReleaseDoesNotDisposeExternalOwnerAndPinsRemainPhysical()
    {
        var native = new NativeMemoryAllocator(16); NativeMemoryOwner owner = native.Allocate(8);
        ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)owner.Memory);
        ReadOnlyMemory<byte> memory = b.AsReadOnlyMemory(0, 8); var pin = memory.Pin();
        try
        {
            b.Release(); Assert.Equal(8, native.ReservedBytes); owner.Memory.Span[0] = 42;
            Assert.Equal(42, memory.Span[0]); owner.Dispose(); Assert.Equal(8, native.ReservedBytes);
        }
        finally { if (b.ReferenceCount > 0) b.Release(); owner.Dispose(); pin.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedNativeCopyDoesNotChargeOrDisposeBorrowedMemory()
    {
        var external = new NativeMemoryAllocator(8); using NativeMemoryOwner owner = external.Allocate(8);
        owner.Memory.Span.Fill(7); var destination = new NativeMemoryAllocator(4);
        var a = new UnpooledByteBufAllocator(true, destination); ByteBuf b = new ReadOnlyMemoryByteBuf(a, owner.Memory);
        try
        {
            Assert.Throws<OutOfMemoryException>(() => b.Copy());
            Assert.Equal(0, a.Metric.UsedDirectMemory); Assert.Equal(0, destination.ReservedBytes);
            Assert.Equal(8, external.ReservedBytes); Assert.Equal(1, b.ReferenceCount); Assert.Equal(7, b.GetByte(0));
        }
        finally { b.Release(); }
    }

    [Fact]
    public void CopyWriteFailureReleasesTheAlreadyAllocatedResult()
    {
        var a = new ReadOnlyResultAllocator(); ByteBuf b = new ReadOnlyMemoryByteBuf(a, new byte[] { 1, 2 });
        try
        {
            Assert.Throws<NotSupportedException>(() => b.Copy());
            Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(1, b.ReferenceCount);
            Assert.Equal(new byte[] { 1, 2 }, ByteBufUtil.GetBytes(b));
        }
        finally { b.Release(); }
    }

    private sealed class ReadOnlyResultAllocator : AbstractByteBufAllocator
    {
        public ByteBuf Last { get; private set; }
        public override bool IsDirectBufferPooled => false;
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
            => Last = new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity).AsReadOnly();
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
            => throw new NotSupportedException();
    }

    private sealed class ExternalStorage : IDisposable
    {
        private readonly NativeMemoryOwner _native;
        private readonly ArrayMemoryManager _manager;
        public Memory<byte> Memory { get; }
        public ExternalStorage(int kind, int length)
        {
            if (kind == 1) { _native = new NativeMemoryAllocator(1024).Allocate(length); Memory = _native.Memory; }
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
