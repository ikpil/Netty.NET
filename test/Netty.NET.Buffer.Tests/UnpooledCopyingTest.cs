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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Selected pinned UnpooledTest copy scenarios and CLR ownership/range/failure cases.
public class UnpooledCopyingTest
{
    private static byte[] Bytes(ByteBuf buffer) => buffer.ReadableSequence.ToArray();

    [Fact]
    public void EmptyInputsReturnTheSentinelWithoutConsumingSourceOwnership()
    {
        ByteBuf source = Unpooled.Buffer(8);
        try
        {
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer());
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<byte>()));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(ReadOnlySpan<byte>.Empty));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<byte[]>()));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<ByteBuf>()));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<byte>(), Array.Empty<byte>()));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(source));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(source, source));
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(0, source.ReaderIndex);
            Assert.Equal(0, source.WriterIndex);
        }
        finally { source.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayAndSpanCopiesHaveIndependentStorageAndFixedMaximum(bool span)
    {
        byte[] source = { 1, 2, 3 };
        ByteBuf copy = span ? Unpooled.CopiedBuffer(source.AsSpan()) : Unpooled.CopiedBuffer(source);
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy);
            Assert.Equal(3, copy.Capacity);
            Assert.Equal(3, copy.MaxCapacity);
            Assert.Equal(0, copy.ReaderIndex);
            Assert.Equal(3, copy.WriterIndex);
            source[0] = 7;
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(copy));
            copy.SetByte(1, 8);
            Assert.Equal(2, source[1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => copy.Capacity = 4);
        }
        finally { copy.Release(); }
    }

    [Fact]
    public void ArraySubregionCopiesOnlyTheRequestedBytes()
    {
        byte[] source = { 9, 1, 2, 8 };
        ByteBuf copy = Unpooled.CopiedBuffer(source, 1, 2);
        try
        {
            Assert.Equal(2, copy.MaxCapacity);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(copy));
            source[1] = 7;
            Assert.Equal(1, copy.GetByte(0));
            copy.SetByte(1, 6);
            Assert.Equal(2, source[2]);
        }
        finally { copy.Release(); }
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(source, source.Length, 0));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 2)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void ArraySubregionChecksBoundsIncludingEmptyRanges(int offset, int length)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.CopiedBuffer(new byte[4], offset, length));

    [Fact]
    public void OriginalMultipleArrayCopyScenariosProduceOneIndependentBuffer()
    {
        byte[] a = { 1 }, b = { 2 }, c = { 3 };
        ByteBuf copy = Unpooled.CopiedBuffer(a, Array.Empty<byte>(), b, c);
        ByteBuf single = Unpooled.CopiedBuffer(new byte[][] { new byte[] { 1, 2, 3 } });
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy);
            Assert.Equal(3, copy.MaxCapacity);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(copy));
            Assert.Equal(Bytes(single), Bytes(copy));
            a[0] = 7;
            copy.SetByte(2, 8);
            Assert.Equal(1, copy.GetByte(0));
            Assert.Equal(3, c[0]);
        }
        finally { copy.Release(); single.Release(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SingleByteBufCopiesReadableBytesAndPreservesIndicesMarksAndReferences(bool direct, bool arrayOverload)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf source = direct ? new UnpooledDirectByteBuf(5, allocator: allocator) : Unpooled.Buffer(5);
        source.WriteBytes(new byte[] { 9, 1, 2, 3, 8 }).SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex();
        ByteBuf copy = arrayOverload ? Unpooled.CopiedBuffer(new[] { source }) : Unpooled.CopiedBuffer(source);
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy);
            Assert.False(copy.IsDirect);
            Assert.False(copy.IsReadOnly);
            Assert.Equal(3, copy.Capacity);
            Assert.Equal(int.MaxValue, copy.MaxCapacity);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(copy));
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(4, source.WriterIndex);
            Assert.Equal(1, source.ReferenceCount);
            source.ReaderIndex = 2; source.WriterIndex = 3;
            source.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(4, source.WriterIndex);
            source.SetByte(1, 7);
            Assert.Equal(1, copy.GetByte(0));
            copy.SetByte(1, 8);
            Assert.Equal(2, source.GetByte(2));
            copy.WriteByte(4);
            Assert.Equal(new byte[] { 1, 8, 3, 4 }, Bytes(copy));
        }
        finally { copy.Release(); }
        Assert.Equal(1, source.ReferenceCount);
        source.Release();
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleNativeReadonlySourcesRemainOwnedAndCopiedDataOutlivesThem(bool direct)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf a = direct ? new UnpooledDirectByteBuf(4, allocator: allocator) : Unpooled.Buffer(4);
        ByteBuf b = direct ? new UnpooledDirectByteBuf(3, allocator: allocator) : Unpooled.Buffer(3);
        a.WriteBytes(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        b.WriteBytes(new byte[] { 7, 3, 6 }).SetIndex(1, 2);
        ByteBuf copy = Unpooled.CopiedBuffer(a.AsReadOnly(), b.AsReadOnly());
        Assert.Equal(new[] { 1, 1 }, new[] { a.ReferenceCount, b.ReferenceCount });
        Assert.Equal(new[] { 1, 3, 1, 2 }, new[] { a.ReaderIndex, a.WriterIndex, b.ReaderIndex, b.WriterIndex });
        a.Release(); b.Release();
        Assert.Equal(0, allocator.ReservedBytes);
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy);
            Assert.Equal(3, copy.MaxCapacity);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(copy));
            copy.SetByte(0, 7);
            Assert.Equal(7, copy.GetByte(0));
        }
        finally { copy.Release(); }
    }

    [Fact]
    public void EmptyExtraInputSelectsTheOriginalFixedMaximumMultipleBufferPath()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        ByteBuf empty = Unpooled.Buffer(4);
        ByteBuf single = Unpooled.CopiedBuffer(new[] { source });
        ByteBuf multiple = Unpooled.CopiedBuffer(source, empty);
        try
        {
            Assert.Equal(int.MaxValue, single.MaxCapacity);
            Assert.Equal(2, multiple.MaxCapacity);
            Assert.Equal(Bytes(single), Bytes(multiple));
            Assert.Equal(new[] { 1, 1 }, new[] { source.ReferenceCount, empty.ReferenceCount });
        }
        finally { single.Release(); multiple.Release(); source.Release(); empty.Release(); }
    }

    [Fact]
    public void SegmentedNestedAndRetainedViewsAreBorrowedAndNotFlattenedOrConsumed()
    {
        ByteBuf owner = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2, 8 });
        ByteBuf retained = owner.RetainedSlice(1, 2);
        var nested = new CompositeByteBuf();
        nested.AddComponents(new[] { Unpooled.WrappedBuffer(new byte[] { 3 }), Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[] { 4 }) }, true);
        ByteBuf copy = Unpooled.CopiedBuffer(retained.AsReadOnly(), nested.Duplicate());
        try
        {
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(copy));
            Assert.Equal(2, owner.ReferenceCount);
            Assert.Equal(1, nested.ReferenceCount);
            Assert.Equal(3, nested.NumComponents);
            copy.Release(); copy = null;
            Assert.Equal(new byte[] { 1, 2 }, Bytes(retained));
            Assert.Equal(new byte[] { 3, 4 }, Bytes(nested));
        }
        finally { copy?.Release(); retained.Release(); owner.Release(); nested.Release(); }
    }

    [Fact]
    public void SingleSegmentedReadonlySliceCopiesTheCapturedReadableRange()
    {
        var source = new CompositeByteBuf();
        source.AddComponents(new[] { Unpooled.WrappedBuffer(new byte[] { 9, 1 }), Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[] { 2, 8 }) }, true);
        source.SetIndex(1, 3);
        ByteBuf copy = Unpooled.CopiedBuffer(source.AsReadOnly().Slice());
        source.Release();
        try { Assert.Equal(new byte[] { 1, 2 }, Bytes(copy)); Assert.Equal(int.MaxValue, copy.MaxCapacity); }
        finally { copy.Release(); }
    }

    [Fact]
    public void ExplicitEndianWritesAreCopiedAsWireBytes()
    {
        ByteBuf a = Unpooled.Buffer(2).WriteShortLE(0x1234), b = Unpooled.Buffer(2).WriteShort(0x5678);
        ByteBuf copy = Unpooled.CopiedBuffer(a, b);
        try
        {
            Assert.Equal(new byte[] { 0x34, 0x12, 0x56, 0x78 }, Bytes(copy));
            Assert.Equal(0x1234, copy.ReadUnsignedShortLE());
            Assert.Equal(0x5678, copy.ReadUnsignedShort());
        }
        finally { copy.Release(); a.Release(); b.Release(); }
    }

    [Fact]
    public void RepeatedSourceNeedsNoExtraReferencesForCopying()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        ByteBuf copy = Unpooled.CopiedBuffer(source, source, source);
        try
        {
            Assert.Equal(new byte[] { 1, 2, 1, 2, 1, 2 }, Bytes(copy));
            Assert.Equal(1, source.ReferenceCount);
        }
        finally { copy.Release(); source.Release(); }
    }

    [Fact]
    public void NullInputsAreRejectedWithoutChangingBorrowedOwnership()
    {
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((byte[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((byte[])null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((byte[][])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((ByteBuf)null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((ByteBuf[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(new byte[] { 1 }, null, new byte[] { 2 }));
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1 });
        try
        {
            Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(source, null, source));
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(new byte[] { 1 }, Bytes(source));
        }
        finally { source.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSourceReadPreservesOwnershipAndCanBeRetried(bool multiple)
    {
        var source = new FailingReadHeapByteBuf();
        ByteBuf other = Unpooled.WrappedBuffer(new byte[] { 3 });
        try
        {
            Assert.Same(source.Failure, Assert.Throws<InvalidOperationException>(() => multiple
                ? Unpooled.CopiedBuffer(other, source) : Unpooled.CopiedBuffer(source)));
            Assert.Equal(new[] { 1, 1 }, new[] { source.ReferenceCount, other.ReferenceCount });
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(3, source.WriterIndex);
            source.Fail = false;
            ByteBuf copy = multiple ? Unpooled.CopiedBuffer(other, source) : Unpooled.CopiedBuffer(source);
            try { Assert.Equal(multiple ? new byte[] { 3, 1, 2 } : new byte[] { 1, 2 }, Bytes(copy)); }
            finally { copy.Release(); }
        }
        finally { source.Release(); other.Release(); }
    }

    [Fact]
    public void EmptyReleasedSourceIsIgnoredOnlyWhenTheEntireInputIsUnreadable()
    {
        ByteBuf dead = Unpooled.Buffer(1); dead.Release();
        ByteBuf readable = Unpooled.WrappedBuffer(new byte[] { 1 });
        try
        {
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(dead));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(dead, dead));
            Assert.Throws<IllegalReferenceCountException>(() => Unpooled.CopiedBuffer(readable, dead));
            Assert.Equal(1, readable.ReferenceCount);
        }
        finally { readable.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverflowPreflightRejectsRepeatedBackingBeforeAllocationOrOwnershipChanges(bool buffers)
    {
        byte[] backing = new byte[1 << 20];
        if (buffers)
        {
            ByteBuf source = Unpooled.WrappedBuffer(backing);
            var inputs = new ByteBuf[2048]; Array.Fill(inputs, source);
            try
            {
                Assert.Throws<ArgumentException>(() => Unpooled.CopiedBuffer(inputs));
                Assert.Equal(1, source.ReferenceCount);
                Assert.Equal(0, source.ReaderIndex);
                Assert.Equal(backing.Length, source.WriterIndex);
            }
            finally { source.Release(); }
        }
        else
        {
            var inputs = new byte[2048][]; Array.Fill(inputs, backing);
            Assert.Throws<ArgumentException>(() => Unpooled.CopiedBuffer(inputs));
            Assert.Equal(0, backing[0]);
        }
    }

    private sealed class FailingReadHeapByteBuf : UnpooledHeapByteBuf
    {
        internal bool Fail = true;
        internal readonly InvalidOperationException Failure = new("Source read failed.");
        internal FailingReadHeapByteBuf() : base(4) => WriteBytes(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
            => Fail ? throw Failure : base.GetReadOnlyMemoryCore(index, length);
    }
}
