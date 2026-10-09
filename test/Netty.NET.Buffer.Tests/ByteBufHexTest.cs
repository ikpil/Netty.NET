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
using System.Globalization;
using System.Linq;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class ByteBufHexTest
{
    private const string Header = "         +-------------------------------------------------+";
    private const string Columns = "         |  0  1  2  3  4  5  6  7  8  9  a  b  c  d  e  f |";
    private const string Border = "+--------+-------------------------------------------------+----------------+";

    private static ByteBuf Owner(int kind, byte[] data)
    {
        if (kind == 0) return Unpooled.WrappedBuffer(data);
        if (kind == 1) return Unpooled.DirectBuffer(data.Length, data.Length).WriteBytes(data);
        int split = Math.Min(7, data.Length);
        if (kind == 2) return Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..split]), true)
            .AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.WrappedBuffer(data[split..]), true);
        if (kind == 3) return Unpooled.WrappedUnmodifiableBuffer(
            Unpooled.WrappedBuffer(data[..split]), Unpooled.WrappedBuffer(data[split..]));
        if (kind == 4) return Unpooled.WrappedBuffer(new byte[] { 9 }.Concat(data).Append((byte)9).ToArray()).Slice(1, data.Length);
        if (kind == 5) return Unpooled.WrappedBuffer(data).AsReadOnly();
        if (kind == 6) return Unpooled.WrappedBuffer(data).Duplicate();
        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    [Theory]
    [InlineData(256)] [InlineData(257)]
    public void OriginalDecodeRandomHexBytesIncludingEverySuffix(int length)
    {
        byte[] bytes = new byte[length]; new Random(12345).NextBytes(bytes);
        string hexDump = ByteBufUtil.HexDump(bytes);
        for (int i = 0; i <= length; i++) // going over sub-strings of various lengths including empty byte[].
        {
            Assert.Equal(bytes[i..], ByteBufUtil.DecodeHexDump(hexDump, i * 2, (length - i) * 2));
            Assert.Equal(bytes[i..], ByteBufUtil.DecodeHexDump(hexDump.AsSpan(i * 2)));
        }
    }

    [Theory]
    [InlineData("abc")] [InlineData("fg")] [InlineData("0x")] [InlineData(" 0")]
    [InlineData("0 ")] [InlineData("\t0")] [InlineData("0\n")] [InlineData("０１")]
    [InlineData("é1")] [InlineData("\uD8000")] [InlineData("00-1")] [InlineData("\uFFFF1")]
    public void OriginalOddLengthAndInvalidHexAreRejected(string text)
    {
        Assert.Throws<ArgumentException>(() => ByteBufUtil.DecodeHexDump(text));
        Assert.Throws<ArgumentException>(() => ByteBufUtil.DecodeHexDump(text.AsSpan()));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void AllOctetsAndAbsoluteReadableRangesKeepIndicesAndMarks(int kind)
    {
        byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); ByteBuf b = Owner(kind, data);
        try
        {
            b.SetIndex(3, 200).MarkReaderIndex().MarkWriterIndex();
            string expected = string.Concat(data.Select(v => v.ToString("x2", CultureInfo.InvariantCulture)));
            Assert.Equal(expected, ByteBufUtil.HexDump(b, 0, 256));
            Assert.Equal(expected[6..400], ByteBufUtil.HexDump(b));
            Assert.Equal(expected, ByteBufUtil.HexDump(data));
            Assert.Equal(expected, ByteBufUtil.HexDump(data.AsSpan()));
            Assert.Equal(expected[10..42], ByteBufUtil.HexDump(data, 5, 16));
            Assert.Equal(data, ByteBufUtil.DecodeHexDump(expected.ToUpperInvariant()));
            Assert.Equal(data, ByteBufUtil.DecodeHexDump("prefix" + expected + "suffix", 6, expected.Length));
            for (int i = 0; i < 256; i++)
            {
                Assert.Equal(data[i], ByteBufUtil.DecodeHexByte(expected, i * 2));
                Assert.Equal(data[i], ByteBufUtil.DecodeHexByte(expected.AsSpan(), i * 2));
            }
            Assert.Equal(3, b.ReaderIndex); Assert.Equal(200, b.WriterIndex);
            b.SetIndex(4, 201).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(3, b.ReaderIndex); Assert.Equal(200, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void PrettyDumpHasExactHeaderPaddingAsciiAndAppendBehavior(int kind)
    {
        byte[] data = new byte[] { 32, 33, 65, 126, 127, 128, 0, 31, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 255 };
        ByteBuf b = Owner(kind, data); string nl = StringUtil.NEWLINE;
        string first = "|00000000| 20 21 41 7e 7f 80 00 1f 30 31 32 33 34 35 36 37 | !A~....01234567|";
        string last = "|00000010| 38 39 ff" + new string(' ', 39) + " |89." + new string(' ', 13) + "|";
        string expected = string.Join(nl, Header, Columns, Border, first, last, Border);
        try
        {
            Assert.Equal(expected, ByteBufUtil.PrettyHexDump(b));
            var builder = new StringBuilder("prefix"); ByteBufUtil.AppendPrettyHexDump(builder, b);
            Assert.Equal("prefix" + expected, builder.ToString());
            b.ReaderIndex = 1; builder.Clear(); ByteBufUtil.AppendPrettyHexDump(builder, b, 0, data.Length);
            Assert.Equal(expected, builder.ToString());
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(data.Length, b.WriterIndex);
            b.ReaderIndex = b.WriterIndex;
            Assert.Equal("", ByteBufUtil.HexDump(b)); Assert.Equal("", ByteBufUtil.PrettyHexDump(b));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(1)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(31)] [InlineData(32)] [InlineData(33)]
    public void PrettyRowsHaveExactByteAndCharacterColumnWidths(int length)
    {
        ByteBuf b = Owner(2, Enumerable.Repeat((byte)'A', length + 9).ToArray()); b.SetIndex(9, 9 + length);
        try
        {
            string[] lines = ByteBufUtil.PrettyHexDump(b).Split(StringUtil.NEWLINE);
            int rows = (length + 15) / 16; Assert.Equal(rows + 4, lines.Length);
            for (int i = 0; i < rows; i++)
            {
                int count = Math.Min(16, length - i * 16); string line = lines[3 + i];
                Assert.Equal(77, line.Length);
                Assert.Equal("|" + (i * 16).ToString("x8", CultureInfo.InvariantCulture) + "|", line[..10]);
                Assert.Equal(string.Concat(Enumerable.Repeat(" 41", count)) + new string(' ', (16 - count) * 3), line[10..58]);
                Assert.Equal(" |" + new string('A', count) + new string(' ', 16 - count) + "|", line[58..]);
            }
            Assert.Equal(9, b.ReaderIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(7)]
    public void LargeDumpPreservesOriginalCachedAndFallbackRowLabels(int offset)
    {
        ByteBuf b = Unpooled.WrappedBuffer(new byte[65553 + offset]);
        try
        {
            string[] lines = ByteBufUtil.PrettyHexDump(b, offset, 65553).Split(StringUtil.NEWLINE);
            Assert.StartsWith("|0000fff0|", lines[3 + 4095]);
            Assert.StartsWith("|" + (65536 + offset).ToString("x8", CultureInfo.InvariantCulture) + "|", lines[3 + 4096]);
            Assert.StartsWith("|" + (65552 + offset).ToString("x8", CultureInfo.InvariantCulture) + "|", lines[3 + 4097]);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void InvalidAndReleasedRangesFailBeforeAppendingIncludingEmptyRanges(int kind)
    {
        ByteBuf b = Owner(kind, new byte[] { 1, 2, 3 }); var builder = new StringBuilder("prefix");
        try
        {
            Assert.Equal("", ByteBufUtil.HexDump(b, 3, 0)); Assert.Equal("", ByteBufUtil.PrettyHexDump(b, 3, 0));
            ByteBufUtil.AppendPrettyHexDump(builder, b, 3, 0); Assert.Equal("prefix", builder.ToString());
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.HexDump(b, 4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.HexDump(b, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.HexDump(b, 1, int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.PrettyHexDump(b, 4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.AppendPrettyHexDump(builder, b, 2, 2));
            Assert.Equal("prefix", builder.ToString());
        }
        finally { b.Release(); }
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.HexDump(b, 0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.PrettyHexDump(b, 0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.AppendPrettyHexDump(builder, b, 0, 0));
        Assert.Equal("prefix", builder.ToString());
    }

    [Fact]
    public void ArrayStringSpanAndNullValidationIsConsistentAtZeroLength()
    {
        Assert.Equal("", ByteBufUtil.HexDump(ReadOnlySpan<byte>.Empty));
        Assert.Same(Array.Empty<byte>(), ByteBufUtil.DecodeHexDump(ReadOnlySpan<char>.Empty));
        Assert.Equal("", ByteBufUtil.PrettyHexDump(Unpooled.EmptyBuffer));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.HexDump((byte[])null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.HexDump((ByteBuf)null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.HexDump((byte[])null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.DecodeHexDump((string)null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.DecodeHexDump(null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.DecodeHexByte((string)null, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.PrettyHexDump(null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.AppendPrettyHexDump(null, Unpooled.EmptyBuffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.HexDump(new byte[1], 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.HexDump(new byte[1], 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.DecodeHexDump("aa", 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.DecodeHexDump("aa", 1, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.DecodeHexByte("aa", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.DecodeHexByte("aa".AsSpan(), -1));
        Assert.Equal(255, ByteBufUtil.DecodeHexByte("fF".AsSpan(), 0));
    }
}
