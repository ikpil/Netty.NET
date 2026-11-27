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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Inherited AbstractByteBufTest text methods over five heap/native/mixed/pooled providers.
// CLR string and ReadOnlySpan<char> replace CharBuffer/CharSequence; ReadString serves both
// readCharSequence and readString. CLR Encoding writes the exact byte length without an automatic
// BOM, so Encoding.BigEndianUnicode stands in for CharsetUtil.UTF_16.
public class ByteBufTextSourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root;
        public Storage(int provider, int length, int maximum = int.MaxValue)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(length, maximum),
                1 => new UnpooledDirectByteBuf(length, maximum, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(length / 2).SetIndex(0, length / 2), true)
                    .AddComponent(new UnpooledDirectByteBuf(length - length / 2, int.MaxValue, Native)
                        .SetIndex(0, length - length / 2), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], length, maximum),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], length, maximum)
            };
            Root.Clear();
        }
        public void Dispose()
        {
            if (Root.RefCnt > 0) Root.Release();
            Assert.Equal(0, Root.RefCnt);
            _pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    // CLR: Encoding.BigEndianUnicode stands in for CharsetUtil.UTF_16 without its BOM.
    private static readonly Encoding UTF_16 = Encoding.BigEndianUnicode;

    private static readonly string EXTENDED_ASCII_CHARS, ASCII_CHARS;

    static ByteBufTextSourceTest()
    {
        char[] chars = new char[256];
        for (char c = (char)0; c < chars.Length; c++)
        {
            chars[c] = c;
        }
        EXTENDED_ASCII_CHARS = new string(chars);
        ASCII_CHARS = new string(chars, 0, 128);
    }

    public static IEnumerable<object[]> Providers()
    {
        for (int provider = 0; provider < 5; ++provider) yield return [provider];
    }

    // Original releasedBuffer(): a released eight-byte buffer.
    private static ByteBuf ReleasedBuffer(Storage s)
    {
        ByteBuf buffer = s.Root;
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        buffer.Clear();
        Assert.True(buffer.Release());
        return buffer;
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestToString(int provider)
    {
        using var s = new Storage(provider, 4096);
        ByteBuf buffer = s.Root;
        // CLR: managed expected bytes replace the allocated copied buffer.
        byte[] copied = Encoding.Latin1.GetBytes("Hello, World!");
        buffer.Clear();
        buffer.WriteBytes(copied);
        Assert.Equal("Hello, World!", buffer.ToString(Encoding.Latin1));
        // CLR: ToString(Encoding) reads without consuming.
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(13, buffer.WriterIndex);
        Assert.Equal(1, buffer.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUsAsciiCharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestSetCharSequenceAfterRelease0(provider, Encoding.ASCII));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetIso88591CharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestSetCharSequenceAfterRelease0(provider, Encoding.Latin1));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUtf8CharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestSetCharSequenceAfterRelease0(provider, Encoding.UTF8));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUtf16CharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestSetCharSequenceAfterRelease0(provider, UTF_16));
    }

    private static void TestSetCharSequenceAfterRelease0(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 8);
        ReleasedBuffer(s).SetCharSequence(0, "x", charset);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteUsAsciiCharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestWriteCharSequenceAfterRelease0(provider, Encoding.ASCII));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteIso88591CharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestWriteCharSequenceAfterRelease0(provider, Encoding.Latin1));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteUtf8CharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestWriteCharSequenceAfterRelease0(provider, Encoding.UTF8));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteUtf16CharSequenceAfterRelease(int provider)
    {
        Assert.Throws<IllegalReferenceCountException>(() => TestWriteCharSequenceAfterRelease0(provider, UTF_16));
    }

    private static void TestWriteCharSequenceAfterRelease0(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 8);
        ReleasedBuffer(s).WriteCharSequence("x", charset);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteUsAsciiCharSequenceExpand(int provider)
    {
        TestWriteCharSequenceExpand(provider, Encoding.ASCII);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteUtf8CharSequenceExpand(int provider)
    {
        TestWriteCharSequenceExpand(provider, Encoding.UTF8);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteIso88591CharSequenceExpand(int provider)
    {
        TestWriteCharSequenceExpand(provider, Encoding.Latin1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteUtf16CharSequenceExpand(int provider)
    {
        TestWriteCharSequenceExpand(provider, UTF_16);
    }

    private static void TestWriteCharSequenceExpand(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 1);
        ByteBuf buf = s.Root;
        int writerIndex = buf.Capacity - 1;
        buf.WriterIndex = writerIndex;
        int written = buf.WriteCharSequence("AB", charset);
        Assert.Equal(writerIndex, buf.WriterIndex - written);
        // CLR: exact encoded bytes; UTF-16 writes four bytes without the Java BOM.
        bool utf16 = ReferenceEquals(charset, UTF_16);
        Assert.Equal(utf16 ? 4 : 2, written);
        Assert.Equal(utf16 ? new byte[] { 0, 65, 0, 66 } : new byte[] { 65, 66 },
            ByteBufUtil.GetBytes(buf, writerIndex, written));
        Assert.Equal(1, buf.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUsAsciiCharSequenceNoExpand(int provider)
    {
        TestSetCharSequenceNoExpand(provider, Encoding.ASCII);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUtf8CharSequenceNoExpand(int provider)
    {
        TestSetCharSequenceNoExpand(provider, Encoding.UTF8);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetIso88591CharSequenceNoExpand(int provider)
    {
        TestSetCharSequenceNoExpand(provider, Encoding.Latin1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUtf16CharSequenceNoExpand(int provider)
    {
        TestSetCharSequenceNoExpand(provider, UTF_16);
    }

    private static void TestSetCharSequenceNoExpand(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 1);
        ByteBuf buf = s.Root;
        // CLR: a marker byte shows that the rejected write leaves the content unchanged.
        buf.SetByte(0, 90);
        // CLR: the original assertThrows around this helper becomes an inner Assert.Throws, so the
        // unchanged state can be checked afterwards; ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.SetCharSequence(0, "AB", charset));
        Assert.Equal(1, buf.Capacity); Assert.Equal(90, buf.GetByte(0));
        Assert.Equal(0, buf.ReaderIndex); Assert.Equal(0, buf.WriterIndex);
        Assert.Equal(1, buf.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUsAsciiCharSequence(int provider)
    {
        TestSetGetCharSequence(provider, Encoding.ASCII);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUtf8CharSequence(int provider)
    {
        TestSetGetCharSequence(provider, Encoding.UTF8);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetIso88591CharSequence(int provider)
    {
        TestSetGetCharSequence(provider, Encoding.Latin1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestSetUtf16CharSequence(int provider)
    {
        TestSetGetCharSequence(provider, UTF_16);
    }

    // CLR: exact encoded byte count of the original character table (UTF-16 has no BOM).
    private static int ExpectedLength(Encoding charset)
    {
        return charset == Encoding.ASCII ? 128 : charset == Encoding.UTF8 ? 384 : charset == Encoding.Latin1 ? 256 : 512;
    }

    private static void TestSetGetCharSequence(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 1024);
        ByteBuf buf = s.Root;
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        int bytes = buf.SetCharSequence(1, sequence, charset);
        // CLR: the ICharSequence content is compared through ToString(), as CharBuffer.wrap does in the source.
        Assert.Equal(sequence, buf.GetCharSequence(1, bytes, charset).ToString());
        // CLR: exact byte count and unchanged cursors.
        Assert.Equal(ExpectedLength(charset), bytes);
        Assert.Equal(0, buf.ReaderIndex); Assert.Equal(0, buf.WriterIndex);
        Assert.Equal(1, buf.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadUsAsciiCharSequence(int provider)
    {
        TestWriteReadCharSequence(provider, Encoding.ASCII);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadUtf8CharSequence(int provider)
    {
        TestWriteReadCharSequence(provider, Encoding.UTF8);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadIso88591CharSequence(int provider)
    {
        TestWriteReadCharSequence(provider, Encoding.Latin1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadUtf16CharSequence(int provider)
    {
        TestWriteReadCharSequence(provider, UTF_16);
    }

    private static void TestWriteReadCharSequence(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 1024);
        ByteBuf buf = s.Root;
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        buf.WriterIndex = 1;
        int bytes = buf.WriteCharSequence(sequence, charset);
        buf.ReaderIndex = 1;
        // CLR: the ICharSequence content is compared through ToString(), as CharBuffer.wrap does in the source.
        Assert.Equal(sequence, buf.ReadCharSequence(bytes, charset).ToString());
        // CLR: exact byte count and consumed cursors.
        Assert.Equal(ExpectedLength(charset), bytes);
        Assert.Equal(1 + bytes, buf.ReaderIndex); Assert.Equal(1 + bytes, buf.WriterIndex);
        Assert.Equal(1, buf.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadUsAsciiString(int provider)
    {
        TestWriteReadString(provider, Encoding.ASCII);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadUtf8String(int provider)
    {
        TestWriteReadString(provider, Encoding.UTF8);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadIso88591String(int provider)
    {
        TestWriteReadString(provider, Encoding.Latin1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestWriteReadUtf16String(int provider)
    {
        TestWriteReadString(provider, UTF_16);
    }

    private static void TestWriteReadString(int provider, Encoding charset)
    {
        using var s = new Storage(provider, 1024);
        ByteBuf buf = s.Root;
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        buf.WriterIndex = 1;
        int bytes = buf.WriteCharSequence(sequence, charset);
        buf.ReaderIndex = 1;
        Assert.Equal(sequence, buf.ReadString(bytes, charset));
        // CLR: exact byte count and consumed cursors.
        Assert.Equal(ExpectedLength(charset), bytes);
        Assert.Equal(1 + bytes, buf.ReaderIndex); Assert.Equal(1 + bytes, buf.WriterIndex);
        Assert.Equal(1, buf.RefCnt);
    }

    [Fact]
    public void TestSetCharSequenceWithTooLongSequence()
    {
        // CLR: an unpooled heap buffer(4, 128), as the original Unpooled factory.
        using var s = new Storage(0, 4, 128);
        ByteBuf buffer = s.Root;
        const string sequence = "ÖÄÜ€";
        int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
        Assert.True(buffer.WritableBytes < maxBytes);
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetCharSequence(0, sequence, Encoding.UTF8));
        // CLR: the rejected write leaves the capacity and cursors unchanged.
        Assert.Equal(4, buffer.Capacity); Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
        Assert.Equal(1, buffer.RefCnt);
    }

    [Fact]
    public void TestWriteCharSequence()
    {
        // CLR: an unpooled heap buffer(4, 128), as the original Unpooled factory.
        using var s = new Storage(0, 4, 128);
        ByteBuf buffer = s.Root;
        const string sequence = "ÖÄÜ€";
        int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
        Assert.True(buffer.WritableBytes < maxBytes);
        int capacity = buffer.Capacity;
        // This should expand the buffer.
        // CLR: the exact encoded UTF-8 length is nine bytes.
        Assert.Equal(9, buffer.WriteCharSequence(sequence, Encoding.UTF8));
        Assert.NotEqual(capacity, buffer.Capacity);
        Assert.Equal(9, buffer.WriterIndex);
        Assert.Equal(1, buffer.RefCnt);
    }

    public static IEnumerable<object[]> EncodingCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int charset = 0; charset < 4; ++charset) yield return [provider, charset];
    }

    private static Encoding Charset(int kind)
    {
        return kind switch
        { 0 => Encoding.ASCII, 1 => Encoding.UTF8, 2 => Encoding.Latin1, _ => Encoding.BigEndianUnicode };
    }

    [Theory]
    [MemberData(nameof(EncodingCases))]
    public void ClrEncodingWireBytesCrossComponentsWithoutPreambleOrImplicitJavaAsciiMapping(int provider, int charset)
    {
        using var s = new Storage(provider, 16);
        ByteBuf buffer = s.Root;
        Encoding encoding = Charset(charset);
        byte[] expectedWire = charset switch
        { 0 => [65, 63], 1 => [65, 195, 169], 2 => [65, 233], _ => [0, 65, 0, 233] };
        string expectedText = charset == 0 ? "A?" : "Aé";
        buffer.SetZero(0, 16).SetIndex(2, 4).MarkReaderIndex().MarkWriterIndex();
        Assert.Equal(expectedWire.Length, buffer.SetCharSequence(7, "Aé", encoding));
        byte[] expectedContent = new byte[16];
        expectedWire.CopyTo(expectedContent, 7);
        Assert.Equal(expectedContent, ByteBufUtil.GetBytes(buffer, 0, 16));
        Assert.Equal(expectedText, buffer.ToString(7, expectedWire.Length, encoding));
        Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        buffer.SetIndex(7, 7 + expectedWire.Length);
        Assert.Equal(expectedText, buffer.ReadString(expectedWire.Length, encoding));
        Assert.Equal(7 + expectedWire.Length, buffer.ReaderIndex);
        buffer.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        Assert.Equal(16, buffer.Capacity); Assert.Equal(1, buffer.RefCnt);
    }
}
