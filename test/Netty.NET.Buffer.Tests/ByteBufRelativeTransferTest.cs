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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufRelativeTransferTest
{
    private static (ByteBuf Buffer, ByteBuf Owner) Backing(int kind, byte[] data)
    {
        ByteBuf owner = kind switch
        {
            1 => Unpooled.DirectBuffer(data.Length, 128).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..5]), true)
                .AddComponent(Unpooled.WrappedBuffer(data[5..]), true),
            3 => Unpooled.WrappedBuffer(new byte[2].Concat(data).Concat(new byte[2]).ToArray()),
            7 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(data[..5]), Unpooled.WrappedBuffer(data[5..])),
            _ => Unpooled.Buffer(data.Length, 128).WriteBytes(data)
        };
        return (kind switch
        {
            3 => owner.Slice(2, data.Length), 4 => owner.Duplicate(),
            5 => new SimpleLeakAwareByteBuf(owner, new Tracker()), 6 => owner.AsReadOnly(), _ => owner
        }, owner);
    }

    private static ByteBuf Invoke(int operation, ByteBuf source, ByteBuf destination, int length = 3) => operation switch
    {
        0 => source.GetBytes(12, destination), 1 => source.GetBytes(12, destination, length),
        2 => destination.SetBytes(3, source), 3 => destination.SetBytes(3, source, length),
        4 => source.ReadBytes(destination), 5 => source.ReadBytes(destination, 3, length),
        6 => destination.WriteBytes(source), _ => destination.WriteBytes(source, 12, length)
    };

    public static IEnumerable<object[]> BackingPairs()
    {
        for (int source = 0; source < 8; source++)
            for (int destination = 0; destination < 6; destination++)
                yield return new object[] { source, destination };
    }

    [Theory]
    [MemberData(nameof(BackingPairs))]
    public void OriginalRandomAndSequentialTransferContractsPreserveBytesMarksAndOwnership(int sourceKind, int destinationKind)
    {
        // Deterministic octets replace the original random fixture; the expected copy is an independent array model.
        byte[] input = Enumerable.Range(0, 24).Select(i => unchecked((byte)(i * 29 + 131))).ToArray();
        byte[] output = Enumerable.Range(0, 24).Select(i => unchecked((byte)(i * 11 + 7))).ToArray();
        for (int operation = 0; operation < 8; operation++)
        {
            var (source, sourceOwner) = Backing(sourceKind, input);
            var (destination, destinationOwner) = Backing(destinationKind, output);
            int writer = operation is 0 or 4 ? 21 : 4;
            source.SetIndex(2, 9).MarkReaderIndex().MarkWriterIndex();
            destination.SetIndex(1, writer).MarkReaderIndex().MarkWriterIndex();
            try
            {
                int length = operation is 2 or 6 ? 7 : 3;
                int from = operation is 0 or 1 or 7 ? 12 : 2;
                int to = operation is 2 or 3 or 5 ? 3 : writer;
                byte[] expected = (byte[])output.Clone(); Array.Copy(input, from, expected, to, length);
                ByteBuf result = Invoke(operation, source, destination);
                Assert.Same(operation is 0 or 1 or 4 or 5 ? source : destination, result);
                Assert.Equal(input, ByteBufUtil.GetBytes(source, 0, 24));
                Assert.Equal(expected, ByteBufUtil.GetBytes(destination, 0, 24));
                Assert.Equal(operation is 2 or 3 or 4 or 5 or 6 ? 2 + length : 2, source.ReaderIndex);
                Assert.Equal(9, source.WriterIndex); Assert.Equal(1, destination.ReaderIndex);
                Assert.Equal(operation is 0 or 1 or 4 or 6 or 7 ? writer + length : writer, destination.WriterIndex);
                Assert.Equal(24, source.Capacity); Assert.Equal(24, destination.Capacity);
                Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, destination.ReferenceCount);
                source.ResetReaderIndex().ResetWriterIndex(); destination.ResetReaderIndex().ResetWriterIndex();
                Assert.Equal(2, source.ReaderIndex); Assert.Equal(9, source.WriterIndex);
                Assert.Equal(1, destination.ReaderIndex); Assert.Equal(writer, destination.WriterIndex);
            }
            finally { source.Release(); destination.Release(); }
            Assert.Equal(0, sourceOwner.ReferenceCount); Assert.Equal(0, destinationOwner.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RelativeWritesGrowButAbsoluteTransfersNeverGrow(int kind)
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6 }).SetIndex(2, 5);
        ByteBuf destination = kind switch
        {
            1 => Unpooled.DirectBuffer(2, 64),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[2]), true),
            _ => Unpooled.Buffer(2, 64)
        };
        destination.SetIndex(1, 2).MarkReaderIndex().MarkWriterIndex();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetBytes(0, destination, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => destination.SetBytes(0, source));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBytes(destination, 0, 3));
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(2, destination.Capacity); Assert.Equal(2, destination.WriterIndex);
            Assert.Same(destination, destination.WriteBytes(source));
            Assert.Equal(5, source.ReaderIndex); Assert.Equal(5, source.WriterIndex);
            Assert.True(destination.Capacity >= 5); Assert.Equal(5, destination.WriterIndex);
            Assert.Equal(new byte[] { 3, 4, 5 }, ByteBufUtil.GetBytes(destination, 2, 3));
            destination.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, destination.ReaderIndex); Assert.Equal(2, destination.WriterIndex);
            Assert.Same(destination, destination.WriteBytes(source, 0, 6));
            Assert.Equal(5, source.ReaderIndex); Assert.Equal(8, destination.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, ByteBufUtil.GetBytes(destination, 2, 6));
        }
        finally { source.Release(); destination.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void DefaultReadsFillDestinationWritableSpaceRatherThanClampingToReadableBytes(int operation)
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 }).SetIndex(1, 2);
        ByteBuf destination = Unpooled.Buffer(4, 64).WriteByte(99);
        try
        {
            if (operation == 0)
            {
                source.GetBytes(1, destination);
                Assert.Equal(new byte[] { 99, 2, 3, 4 }, ByteBufUtil.GetBytes(destination));
                Assert.Equal(1, source.ReaderIndex); Assert.Equal(2, source.WriterIndex);
            }
            else
            {
                if (operation == 1) Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBytes(destination));
                else Assert.Throws<ArgumentOutOfRangeException>(() => source.GetBytes(2, destination));
                Assert.Equal(1, destination.WriterIndex); Assert.Equal(99, destination.GetByte(0));
            }
            Assert.Equal(4, destination.Capacity); Assert.Equal(1, source.ReaderIndex);
        }
        finally { source.Release(); destination.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void InvalidLengthsNullReadOnlyAndReleasedBuffersLeaveCursorsUnchanged(int operation)
    {
        ByteBuf source = Unpooled.Buffer(24, 64).WriteZero(24).SetIndex(2, 9);
        ByteBuf destination = Unpooled.Buffer(24, 64).WriteZero(24).SetIndex(1, 24);
        try
        {
            if (operation is 1 or 3 or 5 or 7)
                Assert.Throws<ArgumentOutOfRangeException>(() => Invoke(operation, source, destination, -1));
            ByteBuf readOnly = destination.AsReadOnly();
            Assert.Throws<NotSupportedException>(() => Invoke(operation, source, readOnly, 0));
            if (operation is 0 or 1 or 4 or 5)
                Assert.Throws<ArgumentNullException>(() => Invoke(operation, source, null));
            else Assert.Throws<ArgumentNullException>(() => Invoke(operation, null, destination));
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(9, source.WriterIndex);
            Assert.Equal(1, destination.ReaderIndex); Assert.Equal(24, destination.WriterIndex);
            source.Release();
            Assert.Throws<IllegalReferenceCountException>(() => Invoke(operation, source, destination, 0));
            Assert.Equal(24, destination.WriterIndex);
        }
        finally { if (source.ReferenceCount > 0) source.Release(); destination.Release(); }
    }

    public static IEnumerable<object[]> SelfPairs()
    {
        for (int kind = 0; kind < 6; kind++)
            for (int operation = 0; operation < 8; operation++)
                yield return new object[] { kind, operation };
    }

    [Theory]
    [MemberData(nameof(SelfPairs))]
    public void SelfTransfersHaveBothEndpointEffectsWithoutCorruptingOverlap(int kind, int operation)
    {
        var (buffer, owner) = Backing(kind, Enumerable.Range(0, 24).Select(i => (byte)i).ToArray());
        int writer = operation is 0 or 4 ? 21 : 9;
        buffer.SetIndex(2, writer);
        try
        {
            int length = operation is 2 or 6 ? writer - 2 : 3;
            int from = operation is 0 or 1 or 7 ? 12 : 2;
            int to = operation is 2 or 3 or 5 ? 3 : writer;
            byte[] expected = Enumerable.Range(0, 24).Select(i => (byte)i).ToArray();
            Array.Copy(expected, from, expected, to, length);
            Assert.Same(buffer, Invoke(operation, buffer, buffer));
            Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 0, 24));
            Assert.Equal(operation is 2 or 3 or 4 or 5 or 6 ? 2 + length : 2, buffer.ReaderIndex);
            Assert.Equal(operation is 0 or 1 or 4 or 6 or 7 ? writer + length : writer, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, owner.ReferenceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SourceValidationCanFailAfterAbsoluteWriteGrowthWithoutPublishingWriter(int kind)
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf destination = kind switch
        {
            1 => Unpooled.DirectBuffer(1, 64),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[1]), true),
            _ => Unpooled.Buffer(1, 64)
        };
        destination.SetIndex(0, 1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => destination.WriteBytes(source, int.MaxValue, 4));
            Assert.True(destination.Capacity >= 5); Assert.Equal(1, destination.WriterIndex); Assert.Equal(0, source.ReaderIndex);
            int capacity = destination.Capacity;
            Assert.Throws<ArgumentOutOfRangeException>(() => destination.WriteBytes(source, 0, int.MaxValue));
            Assert.Equal(capacity, destination.Capacity); Assert.Equal(1, destination.WriterIndex);
            source.Release();
            Assert.Throws<IllegalReferenceCountException>(() => destination.WriteBytes(source, 0, 0));
            Assert.Equal(1, destination.WriterIndex);
        }
        finally { if (source.ReferenceCount > 0) source.Release(); destination.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void EmptyTransfersValidateBothLifetimesWithoutChangingIndices(int operation)
    {
        ByteBuf source = Unpooled.Buffer(24, 24).SetIndex(2, 2);
        ByteBuf destination = Unpooled.Buffer(24, 24).SetIndex(1, 24);
        try
        {
            Assert.Same(operation is 0 or 1 or 4 or 5 ? source : destination, Invoke(operation, source, destination, 0));
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(2, source.WriterIndex);
            Assert.Equal(1, destination.ReaderIndex); Assert.Equal(24, destination.WriterIndex);
            destination.Release();
            Assert.Throws<IllegalReferenceCountException>(() => Invoke(operation, source, destination, 0));
            Assert.Equal(2, source.ReaderIndex);
        }
        finally { source.Release(); if (destination.ReferenceCount > 0) destination.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void PartialCompositeWriteFailureDoesNotMoveEitherCursor(int operation)
    {
        ByteBuf source = Unpooled.WrappedBuffer(Enumerable.Range(0, 24).Select(i => (byte)(i + 1)).ToArray()).SetIndex(2, 5);
        ByteBuf first = Unpooled.WrappedBuffer(new byte[5]);
        ByteBuf last = new FailingHeapBuffer(19);
        ByteBuf destination = Unpooled.CompositeBuffer().AddComponent(first, true).AddComponent(last, true);
        int writer = operation is 0 or 4 ? 21 : 4;
        destination.SetIndex(0, writer);
        try
        {
            // For default Get/Read, move the boundary into the destination's writable range.
            if (operation is 0 or 4)
            {
                destination.Release(); first = Unpooled.WrappedBuffer(new byte[22]); last = new FailingHeapBuffer(2);
                destination = Unpooled.CompositeBuffer().AddComponent(first, true).AddComponent(last, true).SetIndex(0, writer);
            }
            Assert.Throws<InvalidOperationException>(() => Invoke(operation, source, destination));
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(5, source.WriterIndex);
            Assert.Equal(writer, destination.WriterIndex); Assert.Equal(0, destination.ReaderIndex);
            int copied = operation is 0 or 4 or 6 or 7 or 1 ? writer : 3;
            int from = operation is 0 or 1 or 7 ? 12 : 2;
            Assert.Equal(source.GetByte(from), destination.GetByte(copied));
        }
        finally { source.Release(); destination.Release(); }
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, last.ReferenceCount);
    }

    [Fact]
    public void NativeAliasedViewsSurviveGrowthAndKeepIndependentSourceIndices()
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf owner = new UnpooledDirectByteBuf(8, 64, allocator).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            ByteBuf source = owner.Duplicate().SetIndex(1, 3);
            owner.WriteBytes(source, 0, 4);
            Assert.Equal(1, source.ReaderIndex); Assert.Equal(3, source.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(owner, 8, 4));
            source.SetIndex(2, 4); owner.WriteBytes(source);
            Assert.Equal(4, source.ReaderIndex); Assert.Equal(14, owner.WriterIndex);
            Assert.Equal(new byte[] { 3, 4 }, ByteBufUtil.GetBytes(owner, 12, 2));
            Assert.Equal(owner.Capacity, allocator.ReservedBytes); Assert.Equal(1, owner.ReferenceCount);
        }
        finally { owner.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    private sealed class FailingHeapBuffer : UnpooledHeapByteBuf
    {
        internal FailingHeapBuffer(int capacity) : base(capacity, capacity) => WriterIndex = capacity;
        protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
            => throw new InvalidOperationException("Deliberate partial composite write failure.");
    }

    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public void Record() => throw new InvalidOperationException("Simple transfer must not record.");
        public void Record(object hint) => throw new InvalidOperationException("Simple transfer must not record.");
        public bool Close(ByteBuf buffer) => true;
    }
}
