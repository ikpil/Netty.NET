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
using System.Buffers.Binary;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Portable FixedCompositeByteBufTest setter/copy/segmentation contracts. Original
// channel/stream and pooled-allocator tests remain pending, not skipped here.
public class FixedCompositeByteBufTest
{
    private static ByteBuf Owner(int kind, byte[] bytes)
    {
        if (kind == 0) return Unpooled.WrappedBuffer(bytes);
        if (kind == 1) return Unpooled.DirectBuffer(bytes.Length).WriteBytes(bytes);
        int split = bytes.Length / 2;
        return Unpooled.CompositeBuffer().AddComponents(new[]
        {
            Unpooled.WrappedBuffer(bytes[..split]), Unpooled.WrappedBuffer(bytes[split..])
        }, true);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(1, 1)]
    [InlineData(0, 2)] [InlineData(2, 0)] [InlineData(1, 2)] [InlineData(2, 1)] [InlineData(2, 2)]
    public void ReadsAllBoundariesAndSegmentsWithoutConsumingSources(int firstKind, int secondKind)
    {
        byte[] expected = Enumerable.Range(128, 17).Select(i => (byte)i).ToArray();
        ByteBuf first = Owner(firstKind, expected[..7]), second = Owner(secondKind, expected[7..]);
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(first, Unpooled.EmptyBuffer, second);
        try
        {
            Assert.Equal(17, buffer.Capacity);
            Assert.Equal(17, buffer.MaxCapacity);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(17, buffer.WriterIndex);
            Assert.True(buffer.IsReadOnly);
            Assert.Equal(first.IsDirect && second.IsDirect, buffer.IsDirect);
            Assert.Null(buffer.Unwrap());
            Assert.Same(buffer, buffer.AsReadOnly());
            Assert.Equal(expected, buffer.ReadableSequence.ToArray());
            for (int i = 0; i < expected.Length; ++i) Assert.Equal(expected[i], buffer.GetByte(i));
            for (int i = 0; i <= expected.Length - 8; ++i)
            {
                Assert.Equal(BinaryPrimitives.ReadInt16BigEndian(expected.AsSpan(i)), buffer.GetShort(i));
                Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(expected.AsSpan(i)), buffer.GetShortLE(i));
                Assert.Equal(expected[i] << 16 | expected[i + 1] << 8 | expected[i + 2], buffer.GetUnsignedMedium(i));
                Assert.Equal(expected[i + 2] << 16 | expected[i + 1] << 8 | expected[i], buffer.GetUnsignedMediumLE(i));
                Assert.Equal(BinaryPrimitives.ReadInt32BigEndian(expected.AsSpan(i)), buffer.GetInt(i));
                Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(i)), buffer.GetIntLE(i));
                Assert.Equal(BinaryPrimitives.ReadInt64BigEndian(expected.AsSpan(i)), buffer.GetLong(i));
                Assert.Equal(BinaryPrimitives.ReadInt64LittleEndian(expected.AsSpan(i)), buffer.GetLongLE(i));
            }
            Assert.Equal(expected[5..12], buffer.AsReadOnlySequence(5, 7).ToArray());
            Assert.Throws<NotSupportedException>(() => buffer.AsReadOnlyMemory(0, 17));
            using ByteBufScope heap = new(Unpooled.Buffer(17));
            using ByteBufScope native = new(Unpooled.DirectBuffer(17));
            heap.Value.SetBytes(0, buffer, 0, 17); native.Value.SetBytes(0, buffer, 0, 17);
            Assert.Equal(expected, heap.Value.AsReadOnlyMemory(0, 17).ToArray());
            Assert.Equal(expected, native.Value.AsReadOnlyMemory(0, 17).ToArray());
            byte[] read = new byte[17]; buffer.ReadBytes(read);
            Assert.Equal(expected, read);
            Assert.Equal(0, first.ReaderIndex); Assert.Equal(0, second.ReaderIndex);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
    [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)]
    public void OriginalSettersAndClrWritableMemoryAreForbidden(int operation)
    {
        ByteBuf first = Unpooled.Buffer(4).WriteZero(4), second = Unpooled.Buffer(4).WriteZero(4);
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(first, second);
        using ByteBufScope source = new(Unpooled.CopyInt(1));
        Action[] writes =
        {
            () => buffer.SetBoolean(0, true), () => buffer.SetByte(0, 1),
            () => buffer.SetBytes(0, source.Value, 0, 4), () => buffer.SetBytes(0, new byte[4]),
            () => buffer.SetChar(0, 'b'), () => buffer.SetDouble(0, 1), () => buffer.SetFloat(0, 1),
            () => buffer.SetInt(0, 1), () => buffer.SetLong(0, 1), () => buffer.SetMedium(0, 1),
            () => buffer.SetShort(0, 1), () => buffer.SetLongLE(0, 1), () => buffer.SetZero(0, 0),
            () => buffer.Capacity = 8, () => buffer.DiscardReadBytes(),
            () => buffer.AsMemory(0, 0), () => buffer.EnsureWritable(0),
            () => buffer.WriteBytes(Array.Empty<byte>()), () => buffer.SetBytes(8, Array.Empty<byte>()),
            () => buffer.WriteByte(1)
        };
        try
        {
            Assert.Throws<NotSupportedException>(writes[operation]);
            Assert.False(buffer.IsWritable); Assert.False(buffer.CanWrite(0)); Assert.False(buffer.CanWrite(-1));
            Assert.Equal(new byte[8], buffer.ReadableSequence.ToArray());
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(8, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void FactoryArityPreservesOriginalOwnershipAndIndices()
    {
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedUnmodifiableBuffer());
        ByteBuf source = Unpooled.CopyInt(0x01020304); source.ReaderIndex = 1;
        ByteBuf single = Unpooled.WrappedUnmodifiableBuffer(source);
        Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, single.ReaderIndex);
        single.ReadByte(); Assert.Equal(1, source.ReaderIndex);
        Assert.Same(single, Unpooled.WrappedUnmodifiableBuffer(single));
        Assert.True(single.Release()); Assert.Equal(0, source.ReferenceCount);
    }

    [Fact]
    public void MultipleInputsUseOriginalAbsoluteZeroMappingAndExplicitSlicesUseReadableRanges()
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2 }), second = Unpooled.CopyShort(0x0304);
        first.ReaderIndex = 1;
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(first, second);
        try
        {
            Assert.Equal(new byte[] { 9, 1, 3, 4 }, buffer.ReadableSequence.ToArray());
            Assert.Equal(1, first.ReaderIndex);
        }
        finally { buffer.Release(); }
        first = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2 }); first.ReaderIndex = 1;
        buffer = Unpooled.WrappedUnmodifiableBuffer(first.Slice(), Unpooled.CopyShort(0x0304));
        try { Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.ReadableSequence.ToArray()); }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount);
    }

    [Fact]
    public void ArrayAndLayoutAreCapturedButContentIsShared()
    {
        ByteBuf first = Unpooled.CopyShort(0x0102), second = Unpooled.CopyShort(0x0304);
        ByteBuf replacement = Unpooled.CopyInt(0);
        ByteBuf[] inputs = { first, second };
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(inputs);
        try
        {
            Assert.Equal(1, buffer.GetByte(0)); Assert.Same(first, inputs[0]);
            inputs[0] = replacement; first.Clear(); first.SetByte(0, 9);
            Assert.Equal(new byte[] { 9, 2, 3, 4 }, buffer.ReadableSequence.ToArray());
            Assert.Equal(4, buffer.Capacity);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(1, replacement.ReferenceCount); replacement.Release();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RetainedViewsAndCopiesHaveTheOriginalIndependentLifetimes(int kind)
    {
        ByteBuf first = Owner(kind, new byte[] { 1, 2 }), second = Owner(kind, new byte[] { 3, 4 });
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(first, second);
        ByteBuf view = buffer.RetainedSlice(1, 2), copy = view.Copy();
        Assert.Equal(2, buffer.ReferenceCount); Assert.Equal(1, first.ReferenceCount);
        Assert.True(view.IsReadOnly); Assert.False(copy.IsReadOnly); Assert.False(copy.IsDirect);
        Assert.Equal(2, copy.Capacity); Assert.Equal(int.MaxValue, copy.MaxCapacity);
        Assert.False(buffer.Release()); Assert.Equal(new byte[] { 2, 3 }, view.ReadableSequence.ToArray());
        Assert.True(view.Release()); Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.GetByte(0));
        copy.WriteByte(5); Assert.Equal(new byte[] { 2, 3, 5 }, copy.ReadableSequence.ToArray()); copy.Release();
    }

    [Fact]
    public void EmptyComponentsAreOwnedAndRepeatedSourcesRequireOneReferencePerInput()
    {
        ByteBuf empty = Unpooled.Buffer(0), source = Unpooled.CopyShort(0x0102);
        source.Retain();
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(empty, source, Unpooled.EmptyBuffer, source);
        Assert.Equal(new byte[] { 1, 2, 1, 2 }, buffer.ReadableSequence.ToArray());
        Assert.True(buffer.Release()); Assert.Equal(0, empty.ReferenceCount); Assert.Equal(0, source.ReferenceCount);
        empty = Unpooled.Buffer(0); source = Unpooled.Buffer(0);
        buffer = Unpooled.WrappedUnmodifiableBuffer(empty, source);
        Assert.True(buffer.IsReadOnly); Assert.Equal(0, buffer.Capacity);
        Assert.True(buffer.ReadableSequence.IsEmpty); Assert.True(buffer.AsReadOnlyMemory(0, 0).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetByte(0));
        buffer.Release(); Assert.Equal(0, empty.ReferenceCount); Assert.Equal(0, source.ReferenceCount);
    }

    [Fact]
    public void PreflightFailureDoesNotConsumeReferences()
    {
        ByteBuf first = Unpooled.CopyInt(1), dead = Unpooled.Buffer(1); dead.Release();
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedUnmodifiableBuffer(null));
        Assert.Throws<ArgumentException>(() => Unpooled.WrappedUnmodifiableBuffer(new ByteBuf[] { null }));
        Assert.Throws<ArgumentException>(() => Unpooled.WrappedUnmodifiableBuffer(first, null));
        Assert.Throws<IllegalReferenceCountException>(() => Unpooled.WrappedUnmodifiableBuffer(first, dead));
        Assert.Equal(1, first.ReferenceCount);
        ByteBuf huge = new CapacityOnlyBuffer(int.MaxValue);
        Assert.Throws<ArgumentException>(() => Unpooled.WrappedUnmodifiableBuffer(huge, first));
        Assert.Equal(1, huge.ReferenceCount); Assert.Equal(1, first.ReferenceCount);
        huge.Release(); first.Release();
    }

    [Fact]
    public void BoundsAreCheckedBeforeReadingComponentStorage()
    {
        ByteBuf first = Unpooled.Buffer(8).WriteByte(1), second = Unpooled.Buffer(8).WriteByte(2);
        ByteBuf buffer = Unpooled.WrappedUnmodifiableBuffer(first, second);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetByte(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetByte(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.AsReadOnlySequence(int.MaxValue, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Copy(1, int.MaxValue));
            Assert.True(buffer.AsReadOnlySequence(2, 0).IsEmpty);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void NestedNativeBorrowSurvivesConsolidationAndReleasesItsLease()
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf first = new UnpooledDirectByteBuf(2, 2, allocator).WriteShort(0x0102);
        ByteBuf second = new UnpooledDirectByteBuf(2, 2, allocator).WriteShort(0x0304);
        ByteBuf fixedBuffer = Unpooled.WrappedUnmodifiableBuffer(first, second);
        CompositeByteBuf target = new(1);
        target.AddComponent(fixedBuffer, true);
        try
        {
            ReadOnlySpan<byte> source = fixedBuffer.AsReadOnlySpan(0, 2);
            Assert.Equal(4, allocator.ReservedBytes);
            target.WriteBytes(source);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2 }, target.ReadableSequence.ToArray());
            Assert.Equal(0, fixedBuffer.ReferenceCount);
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
            Assert.Equal(0, allocator.ReservedBytes);
        }
        finally { target.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    private sealed class CapacityOnlyBuffer : AbstractReferenceCountedByteBuf
    {
        internal CapacityOnlyBuffer(int capacity) : base(capacity) { WriterIndex = capacity; }
        public override int Capacity { get => MaxCapacity; set => throw new NotSupportedException(); }
        public override bool IsDirect => false;
        protected override Memory<byte> GetMemoryCore(int index, int length) => throw new InvalidOperationException("No storage expected.");
        protected override void Deallocate() { }
    }

    private readonly struct ByteBufScope(ByteBuf value) : IDisposable
    {
        internal ByteBuf Value { get; } = value;
        public void Dispose() => Value.Release();
    }
}
