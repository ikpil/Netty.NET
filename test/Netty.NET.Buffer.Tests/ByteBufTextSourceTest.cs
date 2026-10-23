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
            if (Root.ReferenceCount > 0) Root.Release();
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    private static Encoding Charset(int kind) => kind switch
    { 0 => Encoding.ASCII, 1 => Encoding.UTF8, 2 => Encoding.Latin1, _ => Encoding.BigEndianUnicode };

    private static string Sequence(int kind)
    {
        char[] chars = new char[kind == 0 ? 128 : 256];
        for (int i = 0; i < chars.Length; ++i) chars[i] = (char)i;
        return new string(chars);
    }

    public static IEnumerable<object[]> OriginalCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int group = 0; group < 7; ++group)
        for (int charset = 0; charset < 4; ++charset) yield return [provider, group, charset];
    }

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void OriginalTextReleaseExpansionAndRoundTripFixtures(int provider, int group, int charset)
    {
        using var s = new Storage(provider, group < 2 ? 8 : group < 4 ? 1 : 1024);
        ByteBuf buffer = s.Root;
        Encoding encoding = Charset(charset);
        if (group < 2)
        {
            // Clear the buffer so we are sure the reader and writer indices are 0.
            // This is important as we may return a slice from newBuffer(...).
            buffer.Clear(); Assert.True(buffer.Release());
            if (group == 0) Assert.Throws<IllegalReferenceCountException>(() => buffer.SetString(0, "x", encoding));
            else Assert.Throws<IllegalReferenceCountException>(() => buffer.WriteString("x", encoding));
            return;
        }
        if (group == 2)
        {
            int writer = buffer.Capacity - 1;
            buffer.WriterIndex = writer;
            int written = buffer.WriteString("AB", encoding);
            Assert.Equal(writer, buffer.WriterIndex - written);
            Assert.Equal(charset == 3 ? 4 : 2, written);
            Assert.Equal(charset == 3 ? new byte[] { 0, 65, 0, 66 } : new byte[] { 65, 66 },
                ByteBufUtil.GetBytes(buffer, writer, written));
        }
        else if (group == 3)
        {
            buffer.SetByte(0, 90);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetString(0, "AB", encoding));
            Assert.Equal(1, buffer.Capacity); Assert.Equal(90, buffer.GetByte(0));
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
        }
        else
        {
            string sequence = Sequence(charset);
            int expectedLength = charset switch { 0 => 128, 1 => 384, 2 => 256, _ => 512 };
            if (group == 4)
            {
                int written = buffer.SetString(1, sequence, encoding);
                Assert.Equal(expectedLength, written);
                Assert.Equal(sequence, buffer.GetString(1, written, encoding));
                Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            }
            else
            {
                buffer.WriterIndex = 1;
                int written = buffer.WriteString(sequence, encoding);
                Assert.Equal(expectedLength, written);
                buffer.ReaderIndex = 1;
                // CLR strings replace both CharBuffer/CharSequence and Java String results.
                Assert.Equal(sequence, buffer.ReadString(written, encoding));
                Assert.Equal(1 + written, buffer.ReaderIndex); Assert.Equal(1 + written, buffer.WriterIndex);
            }
        }
        Assert.Equal(1, buffer.ReferenceCount);
    }

    public static IEnumerable<object[]> ExtraOriginalCases()
    {
        for (int provider = 0; provider < 5; ++provider) yield return [provider, 0];
        // These two originals use Unpooled.buffer directly, rather than the inherited provider factory.
        yield return [0, 1]; yield return [0, 2];
    }

    [Theory]
    [MemberData(nameof(ExtraOriginalCases))]
    public void OriginalReadableStringAndUtf8LengthFixtures(int provider, int scenario)
    {
        using var s = new Storage(provider, scenario == 0 ? 4096 : 4, scenario == 0 ? int.MaxValue : 128);
        ByteBuf buffer = s.Root;
        if (scenario == 0)
        {
            // CLR managed expected bytes replace the allocated copied comparison buffer.
            buffer.Clear().WriteBytes(Encoding.Latin1.GetBytes("Hello, World!"));
            Assert.Equal("Hello, World!", buffer.GetString(Encoding.Latin1));
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(13, buffer.WriterIndex);
        }
        else
        {
            const string sequence = "ÖÄÜ€";
            int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
            Assert.True(buffer.WritableBytes < maxBytes);
            if (scenario == 1)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetString(0, sequence, Encoding.UTF8));
                Assert.Equal(4, buffer.Capacity); Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            }
            else
            {
                int capacity = buffer.Capacity;
                // This should expand the buffer.
                Assert.Equal(9, buffer.WriteString(sequence, Encoding.UTF8));
                Assert.NotEqual(capacity, buffer.Capacity); Assert.Equal(9, buffer.WriterIndex);
            }
        }
        Assert.Equal(1, buffer.ReferenceCount);
    }

    public static IEnumerable<object[]> EncodingCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int charset = 0; charset < 4; ++charset) yield return [provider, charset];
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
        Assert.Equal(expectedWire.Length, buffer.SetString(7, "Aé", encoding));
        byte[] expectedContent = new byte[16];
        expectedWire.CopyTo(expectedContent, 7);
        Assert.Equal(expectedContent, ByteBufUtil.GetBytes(buffer, 0, 16));
        Assert.Equal(expectedText, buffer.GetString(7, expectedWire.Length, encoding));
        Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        buffer.SetIndex(7, 7 + expectedWire.Length);
        Assert.Equal(expectedText, buffer.ReadString(expectedWire.Length, encoding));
        Assert.Equal(7 + expectedWire.Length, buffer.ReaderIndex);
        buffer.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex);
        Assert.Equal(16, buffer.Capacity); Assert.Equal(1, buffer.ReferenceCount);
    }
}
