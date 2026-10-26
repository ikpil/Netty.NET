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

public class ByteBufUtilitySourceTest
{
    internal sealed class Wrapper(ByteBuf buffer) : WrappedByteBuf(buffer);
    internal sealed class Storage : IDisposable
    {
        internal readonly NativeMemoryAllocator Native = new();
        internal readonly IByteBufAllocator Allocator;
        internal readonly ByteBuf Root;
        internal Storage(int provider, int maximum = 64, bool composite = false)
        {
            Allocator = (provider / 2) switch
            {
                0 => new UnpooledByteBufAllocator(false, true, Native),
                1 => new PooledByteBufAllocator(false, maxOrder: 4, smallCacheSize: 0, nativeAllocator: Native),
                _ => new AdaptiveByteBufAllocator(false, false, Native)
            };
            bool direct = (provider & 1) != 0;
            ByteBuf Buffer(int size) => direct ? Allocator.DirectBuffer(size, maximum) : Allocator.HeapBuffer(size, maximum);
            if (composite)
            {
                var c = direct ? Allocator.CompositeDirectBuffer(16) : Allocator.CompositeHeapBuffer(16);
                Root = c.AddComponent(Buffer(8)).AddComponent(Buffer(24));
            }
            else Root = Buffer(16);
        }
        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Root.Release();
            (Allocator as IDisposable)?.Dispose(); Assert.Equal(0, Native.ReservedBytes);
        }
    }
    // Original ByteBufUtilTest strings, including its exact malformed UTF-16 pairs.
    private static readonly string[] Texts =
    [
        "NettyRocks", "Some UTF-8 like äÄ∏ŒŒ", "a\ud800\udc00b", "a\udc00b", "a\ud800b",
        "a\udc00\ud800b", "a\ud800\ud800b", "a\udc00\udc00b", "\ud800", "\udc00"
    ];
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 6; ++provider)
        for (int form = 0; form < 5; ++form)
        for (int text = 0; text < Texts.Length; ++text)
        for (int mode = 0; mode < 6; ++mode) yield return [provider, form, text, mode];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalWritersPreserveWireBytesAcrossAllAllocatorProviders(int provider, int form, int text, int mode)
    {
        using var storage = new Storage(provider, composite: form >= 3);
        ByteBuf root = storage.Root;
        ByteBuf b = form switch
        {
            1 => new Wrapper(root), 2 => Unpooled.UnreleasableBuffer(root),
            4 => new WrappedCompositeByteBuf((CompositeByteBuf)root), _ => root
        };
        int prefix = form >= 3 ? 1 : 0;
        // write some byte so we start writing with an offset.
        if (prefix != 0) b.WriteByte(1);
        b.MarkReaderIndex().MarkWriterIndex();
        string value = Texts[text];
        Encoding encoder = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback);
        byte[] expected = mode >= 2 ? value.Select(c => c <= 255 ? (byte)c : (byte)'?').ToArray() : encoder.GetBytes(value);
        var ascii = new AsciiString(value);
        int written = mode switch
        {
            0 => ByteBufUtil.WriteUtf8(b, value), 1 => ByteBufUtil.ReserveAndWriteUtf8(b, value, expected.Length),
            2 => ByteBufUtil.WriteAscii(b, value), 3 => ByteBufUtil.WriteAscii(b, ascii),
            4 => ByteBufUtil.WriteUtf8(b, ascii), _ => ByteBufUtil.ReserveAndWriteUtf8(b, ascii, expected.Length)
        };
        Assert.Equal(expected.Length, written);
        Assert.Equal((0, prefix + written, 1), (b.ReaderIndex, b.WriterIndex, b.ReferenceCount));
        if (prefix != 0) Assert.Equal(1, b.GetByte(0));
        // Skip the previously written byte.
        Assert.Equal(expected, ByteBufUtil.GetBytes(b, prefix, written));
        if (mode <= 1) Assert.Equal(written, ByteBufUtil.Utf8Bytes(value));
        else if (mode >= 3) Assert.Equal(written, ByteBufUtil.Utf8Bytes(ascii));
        b.ResetWriterIndex().ResetReaderIndex(); Assert.Equal((0, prefix), (b.ReaderIndex, b.WriterIndex));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void OriginalSeededNeedleSearchAgreesWithAnIndependentNaiveLoop(int needleReaderIndex)
    {
        // Retain the original 10,000 trials, small alphabet, lengths and reader prefixes.
        // CLR and Java seeded RNG streams differ; the scalar reference checks every trial.
        var random = new Random(needleReaderIndex);
        var allocator = new UnpooledByteBufAllocator(false, true);
        for (int trial = 0; trial < 10000; ++trial)
        {
            byte[] input = new byte[2 + random.Next(5)], data = new byte[random.Next(16)], prefix = new byte[needleReaderIndex];
            foreach (byte[] bytes in new[] { input, data, prefix })
                for (int i = 0; i < bytes.Length; ++i) bytes[i] = (byte)random.Next(3);
            int expected = -1;
            for (int i = 0; i <= data.Length - input.Length; ++i)
            {
                int j = 0; while (j < input.Length && input[j] == data[i + j]) ++j;
                if (j == input.Length) { expected = i; break; }
            }
            ByteBuf needle = allocator.HeapBuffer(256).WriteBytes(prefix).WriteBytes(input);
            needle.ReaderIndex = prefix.Length;
            ByteBuf haystack = Unpooled.WrappedBuffer(data);
            try
            {
                Assert.Equal(expected, ByteBufUtil.IndexOf(needle, haystack));
                Assert.Equal(prefix.Length, needle.ReaderIndex); Assert.Equal(0, haystack.ReaderIndex);
            }
            finally { needle.Release(); haystack.Release(); }
        }
    }

}
