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
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class EmptyByteBufTest
{
    [Fact]
    public void TestIsContiguous()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        Assert.True(empty.IsContiguous);
    }

    [Fact]
    public void TestIsWritable()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        Assert.False(empty.IsWritable);
        Assert.False(empty.CanWrite(1));
    }

    [Fact]
    public void TestWriteEmptyByteBuf()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        empty.WriteBytes(Unpooled.EmptyBuffer); // Ok
        ByteBuf nonEmpty = UnpooledByteBufAllocator.Default.Buffer().WriteBoolean(false);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => empty.WriteBytes(nonEmpty));
            Assert.Equal(0, nonEmpty.ReaderIndex);
            Assert.Equal(1, nonEmpty.WriterIndex);
            Assert.Equal(0, empty.WriterIndex);
        }
        finally { Assert.True(nonEmpty.Release()); }
    }

    [Fact]
    public void TestIsReadable()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        Assert.False(empty.IsReadable);
        Assert.False(empty.CanRead(1));
    }

    [Fact]
    public void TestArray()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        // CLR: array access is an optional MemoryMarshal projection, not a second ByteBuf storage API.
        Assert.True(MemoryMarshal.TryGetArray(empty.AsReadOnlyMemory(0, 0), out var array));
        Assert.NotNull(array.Array);
        Assert.Empty(array.Array);
        Assert.Equal(0, array.Offset);
        Assert.Equal(0, array.Count);
    }

    [Fact]
    public void TestNioBuffer()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        // CLR: value-type memory/sequence coordinates replace NIO object identity, position and limit.
        ReadOnlySequence<byte> sequence = empty.ReadableSequence;
        Assert.True(sequence.IsSingleSegment);
        Assert.Equal(0, sequence.Length);
        Assert.Equal(0, sequence.First.Length);
        Assert.Equal(empty.AsMemory(0, 0), empty.AsMemory(empty.ReaderIndex, 0));
        Assert.Equal(empty.ReadableMemory, sequence.First);
        Assert.Equal(sequence.Start, sequence.End);
        Assert.Equal(0, empty.ReaderIndex);
        Assert.Equal(0, empty.WriterIndex);
    }

    [Fact]
    public void TestMemoryAddress()
    {
        AssertEmptyMemoryPins(new EmptyByteBuf(UnpooledByteBufAllocator.Default));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void TestAllocatorIoBufferMemoryAddress(int kind, bool preferDirect)
    {
        var native = new NativeMemoryAllocator();
        IByteBufAllocator allocator = kind switch
        {
            0 => new UnpooledByteBufAllocator(preferDirect, native),
            1 => new PooledByteBufAllocator(preferDirect, maxOrder: 1, nativeAllocator: native),
            _ => new AdaptiveByteBufAllocator(preferDirect, false, native)
        };
        ByteBuf empty = allocator.IoBuffer(0, 0);
        try
        {
            Assert.Same(allocator, empty.Allocator);
            Assert.Same(empty, allocator.Buffer(0, 0));
            Assert.Same(empty, allocator.HeapBuffer(0, 0));
            Assert.Same(empty, allocator.DirectBuffer(0, 0));
            AssertEmptyMemoryPins(empty);
            IByteBufAllocatorMetric metric = ((IByteBufAllocatorMetricProvider)allocator).Metric;
            Assert.Equal(0, metric.UsedHeapMemory);
            Assert.Equal(0, metric.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { (allocator as IDisposable)?.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    private static unsafe void AssertEmptyMemoryPins(ByteBuf empty)
    {
        // CLR: Memory.Empty has a null pin, so an empty sentinel needs no dummy native allocation.
        // Simultaneous writable/read-only pins cover the original NIO/address agreement without JVM capability skips.
        using (MemoryHandle writable = empty.AsMemory(0, 0).Pin())
        using (MemoryHandle readable = empty.ReadableMemory.Pin())
        {
            Assert.True(writable.Pointer == null);
            Assert.True(readable.Pointer == writable.Pointer);
            Assert.False(empty.Release());
            Assert.Equal(1, empty.ReferenceCount);
            Assert.True(empty.ReadableMemory.IsEmpty);
        }
        Assert.True(empty.AsMemory(0, 0).IsEmpty);
        Assert.Equal(1, empty.ReferenceCount);
    }

    [Fact]
    public void ConsistentEqualsAndHashCodeWithAbstractBytebuf()
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        ByteBuf emptyAbstract = new UnpooledHeapByteBuf(UnpooledByteBufAllocator.Default, 0, 0);
        try
        {
            Assert.True(empty.Equals(emptyAbstract));
            Assert.True(emptyAbstract.Equals(empty));
            Assert.Equal(emptyAbstract.GetHashCode(), empty.GetHashCode());
            Assert.Equal(1, empty.GetHashCode());
        }
        finally { Assert.True(emptyAbstract.Release()); }
        Assert.False(empty.Release());
    }

    [Theory]
    [InlineData("us-ascii")]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void TestReadStringAndCharSequence(string encodingName)
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        // CLR: both original readCharSequence and readString use the same string/Encoding API.
        Encoding encoding = Encoding.GetEncoding(encodingName);
        Assert.Equal(string.Empty, empty.ReadString(0, encoding));
        Assert.Equal(string.Empty, empty.GetString(0, 0, encoding));
        Assert.Equal(0, empty.ReaderIndex);
        Assert.Equal(0, empty.WriterIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void TestReadStringThrowsIndexOutOfBoundsException(int length)
    {
        var empty = new EmptyByteBuf(UnpooledByteBufAllocator.Default);
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.ReadString(length, Encoding.ASCII));
        Assert.Equal(0, empty.ReaderIndex);
    }

    [Fact]
    public void EmptyFactoriesAndViewsReturnTheSharedSentinel()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Same(empty, Unpooled.Buffer(0, 0));
        Assert.Same(empty, Unpooled.DirectBuffer(0, 0));
        Assert.Same(empty, Unpooled.WrappedBuffer(Array.Empty<byte>()));
        Assert.Same(empty, Unpooled.CopiedBuffer(ReadOnlySpan<byte>.Empty));
        Assert.Same(empty, empty.Copy());
        Assert.Same(empty, empty.Slice());
        Assert.Same(empty, empty.Duplicate());
        Assert.Same(empty, empty.RetainedDuplicate());
        Assert.Same(empty, empty.RetainedSlice());
        Assert.Same(empty, empty.ReadBytes(0));
        Assert.Same(empty, empty.ReadSlice(0));
        Assert.Same(empty, empty.ReadRetainedSlice(0));
    }

    [Fact]
    public void EmptyOwnershipIsPermanentlyAccessible()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Equal(1, empty.ReferenceCount);
        Assert.Same(empty, empty.Retain());
        Assert.Same(empty, empty.Retain(int.MaxValue));
        Assert.Same(empty, empty.Retain(0));
        Assert.Same(empty, empty.Retain(-1));
        Assert.False(empty.Release());
        Assert.False(empty.Release(int.MaxValue));
        Assert.False(empty.Release(0));
        Assert.False(empty.Release(-1));
        Assert.Equal(1, empty.ReferenceCount);
        Assert.Equal(0, empty.AsMemory(0, 0).Length);
    }

    [Fact]
    public void EmptyReadableWritableAndBoundsContracts()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Equal(0, empty.Capacity);
        Assert.Equal(0, empty.MaxCapacity);
        Assert.False(empty.IsReadable);
        Assert.False(empty.IsWritable);
        Assert.True(empty.IsDirect);
        Assert.Equal(0, empty.EnsureWritable(0, false));
        Assert.Equal(1, empty.EnsureWritable(1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.GetByte(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.SetIndex(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.EnsureWritable(1));
        Assert.Throws<NotSupportedException>(() => empty.Capacity = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Slice(1, 0));
    }

    [Fact]
    public void ZeroLengthReadUsesSentinelWithoutAcquiringOwnership()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Same(Unpooled.EmptyBuffer, buffer.ReadBytes(0));
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.False(Unpooled.EmptyBuffer.Release());
        }
        finally { buffer.Release(); }
    }
}
