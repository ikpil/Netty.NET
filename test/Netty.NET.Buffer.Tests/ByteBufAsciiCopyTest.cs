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
using System.Linq;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufAsciiCopyTest
{
    private static (ByteBuf Buffer, ByteBuf Owner) Destination(int kind, int capacity)
    {
        ByteBuf owner = kind switch
        {
            1 => Unpooled.DirectBuffer(capacity, capacity),
            2 => Unpooled.CompositeBuffer()
                .AddComponent(Unpooled.WrappedBuffer(new byte[capacity / 2]), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[capacity - capacity / 2]), true),
            3 => Unpooled.WrappedBuffer(new byte[capacity + 4]),
            _ => Unpooled.WrappedBuffer(new byte[capacity])
        };
        ByteBuf b = kind switch
        {
            3 => owner.Slice(2, capacity),
            4 => owner.Duplicate(),
            5 => Unpooled.UnreleasableBuffer(owner),
            _ => owner
        };
        b.SetZero(0, capacity).SetIndex(0, 0);
        return (b, owner);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)]
    [InlineData(3, 0)] [InlineData(3, 1)] [InlineData(3, 2)]
    [InlineData(4, 0)] [InlineData(4, 1)] [InlineData(4, 2)]
    [InlineData(5, 0)] [InlineData(5, 1)] [InlineData(5, 2)]
    public void CopiesAllOctetsWithSourceOffsetsAndPreservesMarks(int kind, int mode)
    {
        byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        byte[] padded = new byte[] { 90, 91 }.Concat(data).Concat(new byte[] { 92, 93 }).ToArray();
        AsciiString source = new AsciiString(padded, false).SubSequence(1, 259, false).SubSequence(1, 257, false);
        var (b, owner) = Destination(kind, 272);
        try
        {
            b.SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex();
            if (mode == 0) ByteBufUtil.Copy(source, b);
            else if (mode == 1) ByteBufUtil.Copy(new AsciiString(padded, true), 2, b, 256);
            else ByteBufUtil.Copy(source, 0, b, 8, 256);
            int start = mode == 2 ? 8 : 4;
            Assert.Equal(data, ByteBufUtil.GetBytes(b, start, 256));
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(mode == 2 ? 4 : 260, b.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount);
            b.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(4, b.WriterIndex);
            if (kind == 5) Assert.Equal(4, owner.WriterIndex);
            if (kind == 3 || kind == 4) Assert.Equal(kind == 3 ? 276 : 272, owner.WriterIndex);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RelativeCopyGrowsAndRetainsIndicesAndMarks(int kind)
    {
        ByteBuf b = kind switch { 0 => Unpooled.Buffer(4, 64), 1 => Unpooled.DirectBuffer(4, 64), _ => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[4]), true) };
        try
        {
            b.SetBytes(0, new byte[] { 1, 2, 3, 4 }).SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex();
            ByteBufUtil.Copy(new AsciiString(new byte[] { 9, 128, 255, 0, 7 }, false), 1, b, 3);
            Assert.True(b.Capacity >= 7); Assert.Equal(7, b.WriterIndex); Assert.Equal(1, b.ReaderIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 128, 255, 0 }, ByteBufUtil.GetBytes(b, 0, 7));
            b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(1, b.ReaderIndex); Assert.Equal(4, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(0, -1)] [InlineData(4, 0)] [InlineData(3, 1)]
    [InlineData(1, 3)] [InlineData(int.MaxValue, 1)] [InlineData(1, int.MaxValue)]
    public void InvalidSourceRangesFailBeforeGrowthOrDestinationValidation(int start, int length)
    {
        AsciiString source = new(new byte[] { 8, 1, 2, 3, 9 }, 1, 3, false);
        ByteBuf b = Unpooled.Buffer(2, 32).WriteBytes(new byte[] { 5, 6 });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(source, start, b, length));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(source, start, b, 0, length));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(source, start, null, length));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(source, start, null, 0, length));
            Assert.Equal(2, b.Capacity); Assert.Equal(2, b.WriterIndex); Assert.Equal(new byte[] { 5, 6 }, ByteBufUtil.GetBytes(b));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(9, 0)] [InlineData(8, 1)]
    [InlineData(int.MaxValue, 1)] [InlineData(7, 2)]
    public void InvalidAbsoluteDestinationNeverGrowsOrChangesBytes(int start, int length)
    {
        ByteBuf b = Unpooled.Buffer(8, 64).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(new AsciiString(new byte[8], false), 0, b, start, length));
            Assert.Equal(8, b.Capacity); Assert.Equal(8, b.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ByteBufUtil.GetBytes(b));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(1, 0)]
    [InlineData(1, 1)] [InlineData(2, 0)] [InlineData(2, 1)]
    public void ReadOnlyDestinationsRejectEvenEmptyCopies(int kind, int length)
    {
        ByteBuf b = kind == 2
            ? Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(new byte[4]), Unpooled.WrappedBuffer(new byte[4]))
            : (kind == 0 ? Unpooled.Buffer(8, 8) : Unpooled.DirectBuffer(8, 8)).AsReadOnly();
        try
        {
            AsciiString source = new(new byte[length], false);
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.Copy(source, b));
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.Copy(source, 0, b, length));
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.Copy(source, 0, b, 0, length));
            Assert.Equal(kind == 2 ? 8 : 0, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ReleasedDestinationsRejectEvenEmptyCopies(int kind)
    {
        var (b, owner) = Destination(kind, 8); owner.Release();
        AsciiString source = new(Array.Empty<byte>(), false);
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Copy(source, b));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Copy(source, 0, b, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.Copy(source, 0, b, 0, 0));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void OverlapUsesSnapshotSemanticsAcrossComponentBoundaries(bool composite, bool forward)
    {
        byte[] data = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        ByteBuf root = Unpooled.WrappedBuffer(data);
        ByteBuf b = composite ? Unpooled.CompositeBuffer().AddComponent(root.Slice(0, 6), true).AddComponent(root.Slice(6, 6).Retain(), true) : root;
        int sourceIndex = forward ? 0 : 3, destinationIndex = forward ? 3 : 0;
        byte[] expected = data.ToArray(); data.AsSpan(sourceIndex, 9).CopyTo(expected.AsSpan(destinationIndex));
        try
        {
            b.SetIndex(0, destinationIndex);
            AsciiString source = new(data, false);
            ByteBufUtil.Copy(source, sourceIndex, b, 9);
            Assert.Equal(expected, data); Assert.Equal(destinationIndex + 9, b.WriterIndex);
            // Repeat the same aliasing contract through the absolute overload.
            for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
            ByteBufUtil.Copy(source, sourceIndex, b, destinationIndex, 9);
            Assert.Equal(expected, data); Assert.Equal(destinationIndex + 9, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void SourceAliasingOldHeapArrayRemainsValidDuringGrowth()
    {
        ByteBuf b = Unpooled.Buffer(4, 16).WriteBytes(new byte[] { 1, 2, 128, 255 });
        try
        {
            byte[] old = ByteBufUtil.GetBytes(b, 0, 4, false);
            ByteBufUtil.Copy(new AsciiString(old, 1, 3, false), b);
            Assert.Equal(new byte[] { 1, 2, 128, 255, 2, 128, 255 }, ByteBufUtil.GetBytes(b));
            Assert.Equal(new byte[] { 1, 2, 128, 255 }, old);
            Assert.NotSame(old, ByteBufUtil.GetBytes(b, 0, b.Capacity, false));
        }
        finally { b.Release(); }
    }

    [Fact]
    public void ExhaustionDoesNotAdvanceWriterAndValidEndEmptyRangeDoesNothing()
    {
        ByteBuf b = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 });
        try
        {
            AsciiString source = new(new byte[] { 9 }, false);
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(source, b));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Copy(source, 0, b, 1));
            ByteBufUtil.Copy(source, 1, b, 0); ByteBufUtil.Copy(source, 1, b, 4, 0);
            ByteBufUtil.Copy(new AsciiString(Array.Empty<byte>(), false), b);
            Assert.Equal(4, b.WriterIndex); Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b));
            ByteBufUtil.Copy(source, 1, Unpooled.EmptyBuffer, 0);
            ByteBufUtil.Copy(source, 1, Unpooled.EmptyBuffer, 0, 0);
            ByteBufUtil.Copy(new AsciiString(Array.Empty<byte>(), false), Unpooled.EmptyBuffer);
            Assert.Equal(0, Unpooled.EmptyBuffer.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedCompositeWriteMayChangeEarlierComponentButNotWriterIndex(bool relative)
    {
        ByteBuf b = Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), true)
            .AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4 }).AsReadOnly(), true);
        try
        {
            b.SetIndex(0, 0);
            AsciiString source = new(new byte[] { 9, 8, 7, 6, 5 }, false);
            if (relative) Assert.Throws<NotSupportedException>(() => ByteBufUtil.Copy(source, b));
            else Assert.Throws<NotSupportedException>(() => ByteBufUtil.Copy(source, 0, b, 0, 4));
            Assert.Equal(new byte[] { 9, 8, 3, 4 }, ByteBufUtil.GetBytes(b, 0, 4));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(0, b.ReaderIndex);
            if (relative) Assert.True(b.Capacity >= 5);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void HttpHeadersEncoderConsumerReservesAndPublishesWriterIndexOnce(int kind)
    {
        // Pinned HttpHeadersEncoder.encoderHeader uses absolute AsciiString copies between
        // colon/space and CRLF writes, publishing writerIndex only after the header is complete.
        AsciiString name = new(Encoding.ASCII.GetBytes("!Content-Type!"), 1, 12, false);
        AsciiString value = new(Encoding.ASCII.GetBytes("!application/octet-stream!"), 1, 24, false);
        var (b, owner) = Destination(kind, 48);
        try
        {
            b.WriteByte(99); int start = b.WriterIndex, offset = start;
            b.EnsureWritable(name.Length() + value.Length() + 4);
            ByteBufUtil.Copy(name, 0, b, offset, name.Length()); offset += name.Length();
            b.SetShort(offset, 0x3a20); offset += 2;
            ByteBufUtil.Copy(value, 0, b, offset, value.Length()); offset += value.Length();
            b.SetShort(offset, 0x0d0a); offset += 2;
            Assert.Equal(start, b.WriterIndex); b.WriterIndex = offset;
            Assert.Equal(Encoding.ASCII.GetBytes("Content-Type: application/octet-stream\r\n"), ByteBufUtil.GetBytes(b, start, offset - start));
        }
        finally { owner.Release(); }
    }

    [Fact]
    public void NullValidationPreservesSourceFirstOrder()
    {
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Copy(null, null)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Copy(null, -1, null, -1)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Copy(null, -1, null, -1, -1)).ParamName);
        AsciiString source = new(new byte[] { 1 }, false);
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Copy(source, null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Copy(source, 0, null, 1));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.Copy(source, 0, null, 0, 1));
    }
}
