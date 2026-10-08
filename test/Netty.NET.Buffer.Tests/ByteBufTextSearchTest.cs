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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufTextSearchTest
{
    private static ByteBuf NewBuffer(bool direct, int capacity = 32, int maximum = 256)
        => direct ? new UnpooledDirectByteBuf(capacity, maximum, new NativeMemoryAllocator())
            : Unpooled.Buffer(capacity, maximum);

    [Fact]
    public void SharedEmptySearchValidatesEndpointsWhileOwnedEmptyCanClamp()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Equal(-1, empty.IndexOf(0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.IndexOf(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.IndexOf(-1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.IndexOf(1, 0, 1));
        Assert.Equal(-1, empty.BytesBefore(1));
        Assert.Equal(-1, empty.ForEachByte(_ => throw new InvalidOperationException()));
        Assert.Equal(-1, empty.ForEachByteDesc(_ => throw new InvalidOperationException()));
        // CLR text APIs consistently accept an empty span with a non-null Encoding, including the sentinel.
        Assert.Equal(0, empty.SetString(0, "", Encoding.UTF8));
        Assert.Equal(0, empty.WriteString("", Encoding.UTF8));
        Assert.Equal("", empty.ReadString(0, Encoding.UTF8));
        ByteBuf owned = Unpooled.Buffer(0, 1);
        try { Assert.Equal(-1, owned.IndexOf(0, 1, 1)); }
        finally { owned.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalDirectionalSearchAndClampingScenarios(bool direct)
    {
        ByteBuf buffer = NewBuffer(direct, 5);
        try
        {
            // Ensure the buffer is completely zero'ed.
            buffer.SetZero(0, buffer.Capacity);
            buffer.WriteBytes(new byte[] { 1, 2, 3, 2, 1 });
            Assert.Equal(-1, buffer.IndexOf(1, 4, 1));
            Assert.Equal(-1, buffer.IndexOf(4, 1, 1));
            Assert.Equal(1, buffer.IndexOf(1, 4, 2));
            // lastIndexOf
            Assert.Equal(3, buffer.IndexOf(4, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(0, 6, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(5, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(5, int.MinValue, 0));
            Assert.Equal(4, buffer.IndexOf(6, 0, 1));
            Assert.Equal(0, buffer.IndexOf(-1, 5, 1));
            Assert.Equal(-1, buffer.IndexOf(2, 2, 2));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(5, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchesAgreeWithIndependentScalarLoopsAcrossAllOctetsAndDirections(bool direct)
    {
        ByteBuf buffer = NewBuffer(direct, 515, 515);
        try
        {
            byte[] bytes = new byte[515];
            for (int i = 0; i < bytes.Length; ++i) bytes[i] = unchecked((byte)(i * 73));
            buffer.WriteBytes(bytes);
            for (int value = 0; value <= byte.MaxValue; ++value)
                for (int start = 0; start < bytes.Length; start += 17)
                {
                    int end = Math.Min(start + 123, bytes.Length);
                    int first = -1, last = -1;
                    for (int i = start; i < end; ++i)
                        if (bytes[i] == value) { if (first < 0) first = i; last = i; }
                    Assert.Equal(first, buffer.IndexOf(start, end, (byte)value));
                    Assert.Equal(last, buffer.IndexOf(end, start, (byte)value));
                }
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchReadableRangesAndSliceCoordinatesDoNotChangeIndices(bool direct)
    {
        ByteBuf buffer = NewBuffer(direct, 10);
        try
        {
            buffer.WriteBytes(new byte[] { 1, 2, 3, 2, 1 });
            buffer.SetByte(9, 77);
            buffer.ReaderIndex = 1;
            Assert.Equal(0, buffer.BytesBefore(2));
            Assert.Equal(1, buffer.BytesBefore(3, 3));
            Assert.Equal(-1, buffer.BytesBefore(2, 1));
            Assert.Equal(8, buffer.BytesBefore(1, 9, 77));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.BytesBefore(5, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.BytesBefore(0, -1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.BytesBefore(1, int.MaxValue, 2));
            ByteBuf slice = buffer.Slice(1, 3);
            Assert.Equal(0, slice.IndexOf(0, 3, 2));
            Assert.Equal(2, slice.IndexOf(3, 0, 2));
            Assert.Equal(1, buffer.ReaderIndex);
            Assert.Equal(5, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalProcessorOrderAbortAndCapacityRangeScenarios(bool direct)
    {
        ByteBuf buffer = NewBuffer(direct, 4096, 4096);
        try
        {
            for (int i = 0; i < 4096; ++i) buffer.WriteByte(i + 1);
            buffer.SetIndex(1024, 3072);
            int next = 1024;
            Assert.Equal(-1, buffer.ForEachByte(value => { Assert.Equal(unchecked((byte)(++next)), value); return true; }));
            Assert.Equal(3072, next);
            next = 4096 / 3;
            Assert.Equal(2048, buffer.ForEachByte(next, 4096 / 3, value =>
            {
                Assert.Equal(unchecked((byte)(next + 1)), value);
                return next++ != 2048;
            }));
            next = 3071;
            Assert.Equal(-1, buffer.ForEachByteDesc(1024, 2048, value =>
            {
                Assert.Equal(unchecked((byte)(next-- + 1)), value); return true;
            }));
            Assert.Equal(1023, next);
            Assert.Equal(3071, buffer.ForEachByteDesc(_ => false));
            Assert.Equal(4000, buffer.ForEachByte(4000, 1, _ => false));
            Assert.Equal(1024, buffer.ReaderIndex);
            Assert.Equal(3072, buffer.WriterIndex);
            var exception = new InvalidOperationException("processor failure");
            Assert.Same(exception, Assert.Throws<InvalidOperationException>(() => buffer.ForEachByte(_ => throw exception)));
            Assert.Equal(-1, buffer.ForEachByte(4096, 0, _ => throw exception));
            Assert.Equal(-1, buffer.ForEachByteDesc(4096, 0, _ => throw exception));
            Assert.Throws<ArgumentNullException>(() => buffer.ForEachByte(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ForEachByte(1, int.MaxValue, _ => true));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessorResizeAndReleaseNeverReadStaleStorage(bool reverse)
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf buffer = new UnpooledDirectByteBuf(3, 64, allocator);
        buffer.WriteBytes(new byte[] { 1, 2, 3 });
        var seen = new List<byte>();
        Func<byte, bool> visitor = value =>
        {
            seen.Add(value);
            if (seen.Count == 1) buffer.Capacity = 64;
            return true;
        };
        Assert.Equal(-1, reverse ? buffer.ForEachByteDesc(visitor) : buffer.ForEachByte(visitor));
        Assert.Equal(reverse ? new byte[] { 3, 2, 1 } : new byte[] { 1, 2, 3 }, seen);
        visitor = _ => { buffer.Release(); return true; };
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            if (reverse) buffer.ForEachByteDesc(visitor); else buffer.ForEachByte(visitor);
        });
        Assert.Equal(0, allocator.ReservedBytes);
    }

    public static IEnumerable<object[]> TextEncodings()
    {
        foreach (bool direct in new[] { false, true })
            for (int kind = 0; kind < 6; ++kind) yield return new object[] { direct, kind };
    }

    private static Encoding Encoder(int kind) => kind switch
    {
        0 => new UTF8Encoding(true, true), 1 => new UnicodeEncoding(true, true, true),
        2 => new UnicodeEncoding(false, true, true), 3 => new UTF32Encoding(false, true, true),
        4 => Encoding.Latin1, _ => Encoding.ASCII
    };

    [Theory]
    [MemberData(nameof(TextEncodings))]
    public void EncodesWithExplicitByteOrderNoPreambleAndIndependentIndices(bool direct, int kind)
    {
        Encoding encoding = Encoder(kind);
        byte[] expected = kind switch
        {
            0 => new byte[] { 65, 0xc3, 0xa9 }, 1 => new byte[] { 0, 65, 0, 0xe9 },
            2 => new byte[] { 65, 0, 0xe9, 0 }, 3 => new byte[] { 65, 0, 0, 0, 0xe9, 0, 0, 0 },
            4 => new byte[] { 65, 0xe9 }, _ => new byte[] { 65, 63 }
        };
        string text = kind == 5 ? "A?" : "Aé";
        ByteBuf buffer = NewBuffer(direct, 1);
        try
        {
            buffer.WriteByte(0x7f);
            // This should expand the buffer.
            Assert.Equal(expected.Length, buffer.WriteString("Aé", encoding));
            Assert.Equal(expected.Length + 1, buffer.WriterIndex);
            Assert.Equal(0x7f, buffer.ReadByte());
            Assert.Equal(expected, buffer.ReadableMemory.ToArray());
            Assert.Equal(text, buffer.GetString(encoding));
            Assert.Equal(1, buffer.ReaderIndex);
            Assert.Equal(text, buffer.ReadString(expected.Length, encoding));
            Assert.Equal(buffer.WriterIndex, buffer.ReaderIndex);
            Assert.Equal(expected.Length, buffer.SetString(1, "Aé", encoding));
            Assert.Equal(buffer.WriterIndex, buffer.ReaderIndex);
            Assert.Equal(text, buffer.GetString(1, expected.Length, encoding));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeEncodingPolicyExactReservationAndFailureIndicesAreExplicit(bool direct)
    {
        ByteBuf buffer = NewBuffer(direct, 1, 1);
        Encoding strict = new UTF8Encoding(false, true);
        try
        {
            Assert.Throws<EncoderFallbackException>(() => buffer.WriteString("\ud800", strict));
            Assert.Equal(1, buffer.Capacity);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.SetString(0, "A", strict));
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadString(1, strict));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetString(0, "é", strict));
            Assert.Equal(65, buffer.GetByte(0));
            Assert.Equal(1, buffer.WriteString("A", strict));
        }
        finally { buffer.Release(); }

        buffer = NewBuffer(direct);
        try
        {
            buffer.WriteBytes(new byte[] { 0xff, 65 });
            Assert.Throws<DecoderFallbackException>(() => buffer.ReadString(1, strict));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal("�", buffer.ReadString(1, Encoding.UTF8));
            Assert.Equal(3, buffer.WriteString("\ud800", Encoding.UTF8));
            Assert.Equal(new byte[] { 0xef, 0xbf, 0xbd }, buffer.AsMemory(2, 3).ToArray());
            Encoding custom = (Encoding)Encoding.ASCII.Clone();
            custom.EncoderFallback = new EncoderReplacementFallback("[bad]");
            Assert.Equal(5, buffer.WriteString("é", custom));
            Assert.Equal(new byte[] { 91, 98, 97, 100, 93 }, buffer.AsMemory(5, 5).ToArray());
            Assert.Equal(0, buffer.WriteString(ReadOnlySpan<char>.Empty, strict));
            Assert.Equal("?", buffer.GetString(0, 1, Encoding.ASCII));
            Assert.Equal("ÿ", buffer.GetString(0, 1, Encoding.Latin1));
            ByteBuf slice = buffer.Slice(2, 3);
            Assert.Equal("�", slice.GetString(Encoding.UTF8));
            Assert.Equal(1, slice.SetString(1, "Z", Encoding.ASCII));
            Assert.Equal(90, buffer.GetByte(3));
        }
        finally { buffer.Release(); }
        Assert.Throws<IllegalReferenceCountException>(() => buffer.GetString(0, 0, strict));
        Assert.Throws<IllegalReferenceCountException>(() => buffer.SetString(0, "", strict));
        Assert.Throws<IllegalReferenceCountException>(() => buffer.ForEachByte(_ => true));
    }

    public static IEnumerable<object[]> Utf8Cases()
    {
        var cases = new (string Text, byte[] Bytes)[]
        {
            ("", Array.Empty<byte>()), ("NettyRocks", new byte[] { 78, 101, 116, 116, 121, 82, 111, 99, 107, 115 }),
            // leading surrogate + trailing surrogate
            ("Aé€😀", new byte[] { 65, 0xc3, 0xa9, 0xe2, 0x82, 0xac, 0xf0, 0x9f, 0x98, 0x80 }),
            ("\ud800", new byte[] { 63 }), ("\udc00", new byte[] { 63 }),
            ("\udc00\ud800", new byte[] { 63, 63 }), ("\ud800\ud800", new byte[] { 63, 63 }),
            ("\udc00\udc00", new byte[] { 63, 63 }), ("\ud800A", new byte[] { 63, 65 }),
            ("\ud800€B", new byte[] { 63, 0xac, 66 }), ("\ud800é", new byte[] { 63, 0xe9 }),
            ("\ud800\udbff\udfff", new byte[] { 63, 63, 63 }),
            ("\udbff\udfff", new byte[] { 0xf4, 0x8f, 0xbf, 0xbf })
        };
        foreach (bool direct in new[] { false, true })
            foreach (var item in cases)
                // Transport numeric UTF-16 units: test discovery can replace lone surrogates in serialized strings.
                yield return new object[] { direct, Array.ConvertAll(item.Text.ToCharArray(), c => (int)c), item.Bytes };
    }

    [Theory]
    [MemberData(nameof(Utf8Cases))]
    public void NettyUtf8WireBytesAndExactCountsIncludeMalformedSurrogates(bool direct, int[] units, byte[] expected)
    {
        string text = new(Array.ConvertAll(units, unit => (char)unit));
        ByteBuf buffer = NewBuffer(direct, 1);
        try
        {
            buffer.WriteByte(0x7f);
            Assert.Equal(expected.Length, ByteBufUtil.Utf8Bytes(text));
            Assert.Equal(text.Length * 3, ByteBufUtil.Utf8MaxBytes(text.AsSpan()));
            Assert.Equal(expected.Length, ByteBufUtil.WriteUtf8(buffer, text));
            Assert.Equal(expected.Length + 1, buffer.WriterIndex);
            Assert.Equal(expected, buffer.AsMemory(1, expected.Length).ToArray());
            Assert.Equal(0x7f, buffer.GetByte(0));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NettyUtf8ReservationsAndAsciiOctetMappingRemainDistinctFromEncoding(bool direct)
    {
        ByteBuf buffer = NewBuffer(direct, 1, 1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteUtf8(buffer, "A"));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteUtf8(buffer, new AsciiString("A")));
            Assert.Equal(1, ByteBufUtil.ReserveAndWriteUtf8(buffer, "A", 1));
        }
        finally { buffer.Release(); }
        buffer = NewBuffer(direct);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReserveAndWriteUtf8(buffer, "€", 2));
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(6, ByteBufUtil.WriteAscii(buffer, "A\u0080é€😀"));
            Assert.Equal(new byte[] { 65, 0x80, 0xe9, 63, 63, 63 }, buffer.ReadableMemory.ToArray());
            var ascii = new AsciiString(new byte[] { 0, 0xff, 0x80 }, false);
            Assert.Equal(3, ByteBufUtil.Utf8MaxBytes(ascii));
            Assert.Equal(3, ByteBufUtil.Utf8Bytes(ascii));
            Assert.Equal(3, ByteBufUtil.WriteUtf8(buffer, ascii));
            Assert.Equal(new byte[] { 0, 0xff, 0x80 }, buffer.AsMemory(6, 3).ToArray());
            Assert.Throws<OverflowException>(() => ByteBufUtil.Utf8MaxBytes(int.MaxValue));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeTextAliasingSurvivesCapacityGrowth(int kind)
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf buffer = new UnpooledDirectByteBuf(2, 64, allocator);
        try
        {
            MemoryMarshal.Cast<byte, char>(buffer.AsSpan(0, 2))[0] = 'A';
            buffer.WriterIndex = 2;
            ReadOnlySpan<char> source = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(0, 2));
            ByteBuf duplicate = buffer.Duplicate();
            int written = kind switch
            {
                0 => duplicate.WriteString(source, Encoding.UTF8),
                1 => ByteBufUtil.WriteUtf8(duplicate, source), _ => ByteBufUtil.WriteAscii(duplicate, source)
            };
            Assert.Equal(1, written);
            Assert.Equal(65, buffer.GetByte(2));
            Assert.Equal(3, duplicate.WriterIndex);
            Assert.Equal(2, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeedleSearchUsesReadableIndicesAndOriginalEmptyNullContracts(bool direct)
    {
        ByteBuf haystack = NewBuffer(direct);
        ByteBuf needle = NewBuffer(direct);
        try
        {
            haystack.WriteBytes(new byte[] { 7, 8, 1, 2, 1, 2, 3, 8 });
            haystack.SetIndex(2, 7);
            needle.WriteBytes(new byte[] { 9, 1, 2, 3, 9 });
            needle.SetIndex(1, 4);
            Assert.Equal(4, ByteBufUtil.IndexOf(needle, haystack));
            Assert.Equal(2, ByteBufUtil.IndexOf(needle.Slice(1, 1), haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(needle.Slice(3, 2), haystack));
            Assert.Equal(0, ByteBufUtil.IndexOf(Unpooled.EmptyBuffer, haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(needle, Unpooled.EmptyBuffer));
            Assert.Equal(-1, ByteBufUtil.IndexOf(null, haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(needle, null));
            Assert.Equal(2, ByteBufUtil.IndexOf(haystack.Slice(2, 5), haystack));
            Assert.Equal(2, haystack.ReaderIndex);
            Assert.Equal(7, haystack.WriterIndex);
            Assert.Equal(1, needle.ReaderIndex);
        }
        finally { needle.Release(); haystack.Release(); }
    }
}
