/*
 * Copyright 2014 The Netty Project
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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufUtf8RangeTest
{
    private static (ByteBuf Buffer, ByteBuf Owner) Destination(int kind)
    {
        ByteBuf owner = kind switch
        {
            1 => Unpooled.DirectBuffer(80, 256),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[5]), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[75]), true),
            3 => Unpooled.WrappedBuffer(new byte[84]),
            _ => Unpooled.Buffer(80, 256)
        };
        ByteBuf buffer = kind switch
        {
            3 => owner.Slice(2, 80), 4 => owner.Duplicate(),
            5 => new SimpleLeakAwareByteBuf(owner, new Tracker()), _ => owner
        };
        buffer.SetZero(0, buffer.Capacity).Clear().WriteBytes(new byte[] { 1, 2, 3, 4 });
        buffer.ReaderIndex = 1;
        buffer.MarkReaderIndex().MarkWriterIndex();
        return (buffer, owner);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void OriginalSubsequenceAndSplitSurrogateScenariosPreserveIndicesAndMarks(int kind)
    {
        string text = "Some UTF-8 like äÄ∏ŒŒ";
        var (buffer, owner) = Destination(kind);
        try
        {
            byte[] expected = Encoding.UTF8.GetBytes(text[5..18]);
            Assert.Equal(expected.Length, ByteBufUtil.Utf8Bytes(text, 5, 18));
            Assert.Equal(expected.Length, ByteBufUtil.WriteUtf8(buffer, text, 5, 18));
            Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 4, expected.Length));
            buffer.WriterIndex = 4;
            Assert.Equal(expected.Length, ByteBufUtil.ReserveAndWriteUtf8(buffer, text, 5, 18, 16));
            Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 4, expected.Length));
            string pair = "\uD800\uDC00"; // surrogate pair: one code point, two chars
            foreach (var range in new[] { (0, 1), (1, 2), (0, 2), (1, 1), (2, 2) })
            {
                buffer.WriterIndex = 4;
                expected = range == (0, 2) ? new byte[] { 0xf0, 0x90, 0x80, 0x80 }
                    : range.Item1 == range.Item2 ? Array.Empty<byte>() : new byte[] { 63 };
                Assert.Equal(expected.Length, ByteBufUtil.Utf8Bytes(pair, range.Item1, range.Item2));
                Assert.Equal(expected.Length, ByteBufUtil.WriteUtf8(buffer, pair, range.Item1, range.Item2));
                Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 4, expected.Length));
                buffer.WriterIndex = 4;
                Assert.Equal(expected.Length, ByteBufUtil.ReserveAndWriteUtf8(buffer, pair, range.Item1, range.Item2, expected.Length));
                Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 4, expected.Length));
            }
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(1, owner.ReferenceCount);
            buffer.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer, 0, 4));
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void AsciiStringRangesCopyAllRawOctetsAndRespectNestedLogicalOffsets(int kind)
    {
        byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        byte[] padded = new byte[] { 17, 18 }.Concat(data).Concat(new byte[] { 19, 20 }).ToArray();
        AsciiString text = new AsciiString(padded, false).SubSequence(1, 259, false).SubSequence(1, 257, false);
        var (buffer, owner) = Destination(kind);
        try
        {
            for (int start = 0; start <= data.Length; start++)
            {
                int end = Math.Min(data.Length, start + 20);
                byte[] expected = data[start..end];
                Assert.Equal(expected.Length, ByteBufUtil.Utf8Bytes(text, start, end));
                buffer.WriterIndex = 4;
                Assert.Equal(expected.Length, ByteBufUtil.WriteUtf8(buffer, text, start, end));
                Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 4, expected.Length));
                buffer.WriterIndex = 4;
                Assert.Equal(expected.Length, ByteBufUtil.ReserveAndWriteUtf8(buffer, text, start, end, expected.Length));
                Assert.Equal(expected, ByteBufUtil.GetBytes(buffer, 4, expected.Length));
            }
            buffer.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount); Assert.Equal(data, text.AsSpan().ToArray());
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(-1, 5)] [InlineData(5, 30)] [InlineData(10, 5)]
    [InlineData(int.MinValue, int.MaxValue)] [InlineData(int.MaxValue, int.MinValue)]
    [InlineData(0, int.MaxValue)] [InlineData(22, 22)] [InlineData(0, -1)]
    public void InvalidRangesFailBeforeDestinationValidationGrowthOrMutation(int start, int end)
    {
        string text = "Some UTF-8 like äÄ∏ŒŒ"; AsciiString raw = new(text);
        ByteBuf buffer = Unpooled.Buffer(1, 64).WriteByte(99);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Utf8Bytes(text, start, end));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Utf8Bytes(raw, start, end));
            foreach (ByteBuf destination in new[] { buffer, null })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteUtf8(destination, text, start, end));
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteUtf8(destination, raw, start, end));
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReserveAndWriteUtf8(destination, text, start, end, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReserveAndWriteUtf8(destination, raw, start, end, -1));
            }
            Assert.Equal(1, buffer.Capacity); Assert.Equal(1, buffer.WriterIndex); Assert.Equal(99, buffer.GetByte(0));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void WorstCaseAndExplicitReservationsControlGrowthEvenForRawAscii(int kind)
    {
        ByteBuf buffer = kind switch
        {
            1 => Unpooled.DirectBuffer(2, 8),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[2]), true),
            _ => Unpooled.Buffer(2, 8)
        };
        try
        {
            buffer.SetIndex(0, 0);
            AsciiString raw = new(new byte[] { 9, 128, 255, 8 }, false);
            Assert.Equal(2, ByteBufUtil.ReserveAndWriteUtf8(buffer, raw, 1, 3, 2));
            Assert.Equal(2, buffer.Capacity); Assert.Equal(new byte[] { 128, 255 }, ByteBufUtil.GetBytes(buffer));
            buffer.Clear();
            Assert.Equal(2, ByteBufUtil.WriteUtf8(buffer, raw, 1, 3));
            Assert.True(buffer.Capacity >= 6); Assert.Equal(2, buffer.WriterIndex);
            buffer.Clear();
            Assert.Equal(2, ByteBufUtil.ReserveAndWriteUtf8(buffer, "xABz", 1, 3, 7));
            Assert.True(buffer.Capacity >= 7); Assert.Equal(2, buffer.WriterIndex);
            Assert.Equal(new byte[] { 65, 66 }, ByteBufUtil.GetBytes(buffer));
        }
        finally { buffer.Release(); }
        buffer = kind == 1 ? Unpooled.DirectBuffer(2, 2) : Unpooled.Buffer(2, 2);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteUtf8(buffer, "xABz", 1, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteUtf8(buffer, new AsciiString("xABz"), 1, 3));
            Assert.Equal(0, buffer.WriterIndex); Assert.Equal(2, buffer.Capacity);
            Assert.Equal(2, ByteBufUtil.ReserveAndWriteUtf8(buffer, new AsciiString("AB"), 2));
            Assert.Equal(new byte[] { 65, 66 }, ByteBufUtil.GetBytes(buffer));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(0, -1)] [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, -1)] [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    [InlineData(2, -1)] [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)]
    public void InsufficientReservationsFailBeforeMutation(int kind, int reserveBytes)
    {
        ByteBuf buffer = (kind switch
        {
            1 => Unpooled.DirectBuffer(1, 64),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[1]), true),
            _ => Unpooled.Buffer(1, 64)
        }).Clear().WriteByte(99);
        try
        {
            AsciiString raw = new(new byte[] { 9, 0, 128, 255, 8 }, false);
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReserveAndWriteUtf8(buffer, raw, 1, 4, reserveBytes));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReserveAndWriteUtf8(buffer, new AsciiString("ABC"), reserveBytes));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReserveAndWriteUtf8(buffer, "x€z", 1, 2, reserveBytes));
            Assert.Equal(1, buffer.Capacity); Assert.Equal(1, buffer.WriterIndex); Assert.Equal(99, buffer.GetByte(0));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReadOnlyAndReleasedDestinationsRejectEmptyAndNonemptyWrites(bool direct)
    {
        ByteBuf owner = direct ? Unpooled.DirectBuffer(4, 16) : Unpooled.Buffer(4, 16);
        ByteBuf buffer = owner.AsReadOnly(); AsciiString raw = new("AB");
        try
        {
            foreach (int end in new[] { 0, 1 })
            {
                Assert.Throws<NotSupportedException>(() => ByteBufUtil.WriteUtf8(buffer, "AB", 0, end));
                Assert.Throws<NotSupportedException>(() => ByteBufUtil.WriteUtf8(buffer, raw, 0, end));
                Assert.Throws<NotSupportedException>(() => ByteBufUtil.ReserveAndWriteUtf8(buffer, raw, 0, end, end));
                Assert.Throws<NotSupportedException>(() => ByteBufUtil.ReserveAndWriteUtf8(buffer, "AB", 0, end, end));
            }
            Assert.Equal(0, owner.WriterIndex); Assert.Equal(1, owner.ReferenceCount);
        }
        finally { owner.Release(); }
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.WriteUtf8(owner, "AB", 0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.ReserveAndWriteUtf8(owner, raw, 0, 0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.ReserveAndWriteUtf8(owner, new AsciiString(""), 0));
    }

    [Fact]
    public void NullSourcesAndDestinationsAreRejectedAndSharedEmptyAllowsEmptyRanges()
    {
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Utf8Bytes((AsciiString)null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteUtf8(Unpooled.EmptyBuffer, (AsciiString)null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReserveAndWriteUtf8(Unpooled.EmptyBuffer, (AsciiString)null, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReserveAndWriteUtf8(Unpooled.EmptyBuffer, (AsciiString)null, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteUtf8((ByteBuf)null, "AB", 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReserveAndWriteUtf8(null, new AsciiString(""), 0));
        Assert.Equal(0, ByteBufUtil.WriteUtf8(Unpooled.EmptyBuffer, "AB", 2, 2));
        Assert.Equal(0, ByteBufUtil.WriteUtf8(Unpooled.EmptyBuffer, new AsciiString("AB"), 2, 2));
        Assert.Equal(0, ByteBufUtil.ReserveAndWriteUtf8(Unpooled.EmptyBuffer, new AsciiString(""), 0));
        Assert.Equal(0, ByteBufUtil.ReserveAndWriteUtf8(Unpooled.EmptyBuffer, "AB", 2, 2, 0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RawAsciiOverlappingSourceSurvivesHeapGrowthAndUsesMemmoveSemantics(bool grow)
    {
        byte[] bytes = { 1, 2, 3, 4, 5, 6, 7, 8 };
        ByteBuf buffer = Unpooled.Buffer(8, 64).WriteBytes(bytes).SetIndex(1, grow ? 7 : 2);
        try
        {
            Assert.True(MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer.AsMemory(0, 8), out var storage));
            AsciiString raw = new(storage.Array, storage.Offset, storage.Count, false); int writer = buffer.WriterIndex;
            Assert.Equal(4, ByteBufUtil.ReserveAndWriteUtf8(buffer, raw, 0, 4, 4));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(buffer, writer, 4));
            Assert.Equal(writer + 4, buffer.WriterIndex); Assert.Equal(1, buffer.ReaderIndex);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAliasedCharacterRangeSurvivesGrowth(bool reserve)
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf buffer = new UnpooledDirectByteBuf(6, 64, allocator);
        try
        {
            Span<char> chars = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(0, 6));
            chars[0] = 'Z'; chars[1] = 'A'; chars[2] = 'é';
            buffer.WriterIndex = 6;
            int written = reserve ? ByteBufUtil.ReserveAndWriteUtf8(buffer, chars, 1, 3, 3)
                : ByteBufUtil.WriteUtf8(buffer, chars, 1, 3);
            Assert.Equal(3, written); Assert.Equal(9, buffer.WriterIndex);
            Assert.Equal(new byte[] { 65, 0xc3, 0xa9 }, ByteBufUtil.GetBytes(buffer, 6, 3));
            Assert.Equal(buffer.Capacity, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedCompositeWriteDoesNotPublishWriterIndex(bool ascii)
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[2]);
        ByteBuf second = new FailingHeapBuffer();
        ByteBuf buffer = Unpooled.CompositeBuffer().AddComponent(first, true).AddComponent(second, true).Clear();
        try
        {
            Assert.Throws<InvalidOperationException>(() => ascii
                ? ByteBufUtil.ReserveAndWriteUtf8(buffer, new AsciiString("ABCD"), 4)
                : ByteBufUtil.ReserveAndWriteUtf8(buffer, "xABCDz", 1, 5, 4));
            Assert.Equal(0, buffer.WriterIndex); Assert.Equal(new byte[] { 65, 66 }, ByteBufUtil.GetBytes(first));
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
    }

    private sealed class FailingHeapBuffer() : UnpooledHeapByteBuf(2, 2)
    {
        protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
            => throw new InvalidOperationException("Deliberate second-component write failure.");
    }

    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public void Record() => throw new InvalidOperationException("Simple writes must not record accesses.");
        public void Record(object hint) => throw new InvalidOperationException("Simple writes must not record hints.");
        public bool Close(ByteBuf buffer) => true;
    }
}
