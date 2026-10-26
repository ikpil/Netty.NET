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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class UnpooledAsciiStringCopyingTest
{
    private static Encoding Encoder(int kind) => kind switch
    {
        0 => Encoding.UTF8, 1 => Encoding.ASCII, 2 => Encoding.Latin1,
        3 => Encoding.Unicode, 4 => Encoding.BigEndianUnicode, _ => Encoding.UTF32
    };
    public static IEnumerable<object[]> Cases()
    {
        for (int kind = 0; kind < 6; ++kind)
        for (int shape = 0; shape < 5; ++shape)
        foreach (bool range in new[] { false, true })
        foreach (bool offset in new[] { false, true }) yield return [kind, shape, range, offset];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void WholeOctetShortcutsAndRangedCharacterEncodingKeepDistinctContracts(int kind, int shape, bool range, bool offset)
    {
        byte[] payload = shape switch
        {
            0 => [], 1 => "ASCII"u8.ToArray(), 2 => [0, 127, 128, 255],
            3 => Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), _ => [65, 195, 169, 66]
        };
        byte[] backing = offset ? new byte[] { 91, 92 }.Concat(payload).Concat(new byte[] { 93, 94 }).ToArray() : payload;
        var text = new AsciiString(backing, offset ? 2 : 0, payload.Length, false);
        // CharAt widens octets; a cached ToString must not hide later borrowed-array changes.
        _ = text.ToString();
        if (payload.Length > 0) { backing[offset ? 2 : 0] = 255; payload = text.AsSpan().ToArray(); }
        Encoding encoding = Encoder(kind);
        byte[] expected = !range && kind <= 1 ? payload : encoding.GetBytes(payload.Select(b => (char)b).ToArray());
        ByteBuf copy = range ? Unpooled.CopiedBuffer(text, 0, text.Length(), encoding) : Unpooled.CopiedBuffer(text, encoding);
        try
        {
            if (range && payload.Length == 0) Assert.Same(Unpooled.EmptyBuffer, copy);
            else Assert.NotSame(Unpooled.EmptyBuffer, copy);
            Assert.Equal(expected.Length, copy.Capacity);
            Assert.Equal(range && payload.Length == 0 ? 0 : int.MaxValue, copy.MaxCapacity);
            Assert.Equal((0, expected.Length, 1), (copy.ReaderIndex, copy.WriterIndex, copy.ReferenceCount));
            Assert.False(copy.IsReadOnly); Assert.False(copy.IsDirect && expected.Length > 0);
            Assert.Equal(expected, ByteBufUtil.GetBytes(copy));
            if (payload.Length > 0) { backing[offset ? 2 : 0] = 0; Assert.Equal(expected, ByteBufUtil.GetBytes(copy)); }
            if (range && payload.Length == 0) Assert.Throws<ArgumentOutOfRangeException>(() => copy.WriteByte(42));
            else { copy.WriteByte(42); Assert.Equal(expected.Length + 1, copy.WriterIndex); }
        }
        finally { copy.Release(); }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void SubregionsUseLogicalAsciiStringOffsets(int kind)
    {
        var text = new AsciiString(new byte[] { 9, 65, 128, 255, 66, 9 }, 1, 4, false);
        Encoding encoding = Encoder(kind);
        ByteBuf copy = Unpooled.CopiedBuffer(text, 1, 2, encoding);
        try { Assert.Equal(encoding.GetBytes(new[] { (char)128, (char)255 }), ByteBufUtil.GetBytes(copy)); }
        finally { copy.Release(); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WholeAsciiOctetsBypassCharacterFallbackWhileRangesHonorIt(bool ignore)
    {
        Encoding encoding = Encoding.GetEncoding("us-ascii", ignore ? new EncoderReplacementFallback(string.Empty)
            : EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var text = new AsciiString(new byte[] { 128, 255 }, false);
        ByteBuf whole = Unpooled.CopiedBuffer(text, encoding);
        try { Assert.Equal(new byte[] { 128, 255 }, ByteBufUtil.GetBytes(whole)); } finally { whole.Release(); }
        if (ignore)
        {
            ByteBuf range = Unpooled.CopiedBuffer(text, 0, 2, encoding);
            try { Assert.Equal(0, range.ReadableBytes); Assert.NotSame(Unpooled.EmptyBuffer, range); }
            finally { range.Release(); }
        }
        else Assert.Throws<EncoderFallbackException>(() => Unpooled.CopiedBuffer(text, 0, 2, encoding));
    }
    [Fact]
    public void ArgumentsAreValidatedEvenForEmptyAsciiStringRanges()
    {
        var text = new AsciiString(new byte[] { 1, 2 });
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((AsciiString)null, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(text, null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((AsciiString)null, 0, 0, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(text, 0, 0, null));
        foreach (var pair in new[] { (-1, 0), (3, 0), (0, -1), (1, int.MaxValue), (int.MaxValue, 1) })
            Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.CopiedBuffer(text, pair.Item1, pair.Item2, Encoding.UTF8));
    }
}
