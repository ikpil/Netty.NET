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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufTextValidationTest
{
    private static ByteBuf Owner(int kind, byte[] data, int split = 2)
    {
        split = Math.Min(split, data.Length);
        return kind switch
        {
            0 => Unpooled.WrappedBuffer(data),
            1 => Unpooled.DirectBuffer(data.Length, data.Length).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..split]), true)
                .AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.WrappedBuffer(data[split..]), true),
            3 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(data[..split]), Unpooled.WrappedBuffer(data[split..])),
            4 => Unpooled.WrappedBuffer(new byte[] { 9 }.Concat(data).Append((byte)9).ToArray()).Slice(1, data.Length),
            5 => Unpooled.WrappedBuffer(data).AsReadOnly(),
            6 => Unpooled.WrappedBuffer(data).Duplicate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static readonly string[] OriginalValidUtf8 =
    {
        "6E65747479", "24", "C2A2", "E282AC", "F0908D88",
        "24C2A2E282ACF0908D88" // multiple characters
    };
    private static readonly string[] OriginalInvalidUtf8 =
    {
        "80", "F08282AC", // Overlong encodings
        "C2",             // not enough bytes
        "E282",           // not enough bytes
        "F0908D",         // not enough bytes
        "C2C0",           // not correct bytes
        "E282C0",         // not correct bytes
        "F0908DC0",       // not correct bytes
        "C180",           // out of lower bound
        "E08080",         // out of lower bound
        "EDAF80"          // out of upper bound
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void OriginalUtf8AsciiAndGeneralDecoderVectors(int kind)
    {
        foreach (string hex in OriginalValidUtf8) Check(kind, hex, Encoding.UTF8, true);
        foreach (string hex in OriginalInvalidUtf8) Check(kind, hex, Encoding.UTF8, false);
        Check(kind, "0001377F", Encoding.ASCII, true); Check(kind, "80FF", Encoding.ASCII, false);
        Check(kind, "01D837DC", Encoding.Unicode, true); Check(kind, "01D8", Encoding.Unicode, false);
    }

    private static void Check(int kind, string hex, Encoding encoding, bool expected)
    {
        ByteBuf b = Owner(kind, Convert.FromHexString(hex));
        try { Assert.Equal(expected, ByteBufUtil.IsText(b, encoding)); }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Utf8ScalarBoundariesTruncationAndInvalidTails(int kind)
    {
        string[] valid = { "", "00", "7F", "C280", "DFBF", "E0A080", "ED9FBF", "EE8080", "EFBFBF", "F0908080", "F48FBFBF", "EFBBBF" };
        string[] invalid = { "C080", "C1BF", "E09FBF", "EDA080", "EDBFBF", "F08FBFBF", "F4908080", "F5808080", "FF", "FE", "F888808080", "80", "BF" };
        foreach (string hex in valid)
        {
            Check(kind, hex, Encoding.UTF8, true);
            byte[] bytes = Convert.FromHexString(hex);
            for (int length = 1; length < bytes.Length; length++) Check(kind, Convert.ToHexString(bytes[..length]), Encoding.UTF8, false);
        }
        foreach (string hex in invalid) Check(kind, hex, Encoding.UTF8, false);
        Check(kind, "4142C2A243E282AC44F0908D8845", Encoding.UTF8, true);
        Check(kind, "4142C2A243E282AC44F0908D88FF", Encoding.UTF8, false);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void DecoderStateSurvivesEveryComponentSplitAndFinalFlush(int kind)
    {
        (string Hex, Encoding Encoding, bool Expected)[] vectors =
        {
            ("24C2A2E282ACF0908D88", Encoding.UTF8, true),
            ("24C2A2E282ACF0908D", Encoding.UTF8, false),
            ("410001D837DC4200", Encoding.Unicode, true),
            ("410001D837DC42", Encoding.Unicode, false),
            ("0041D801DC370042", Encoding.BigEndianUnicode, true),
            ("0041D801DC37D800", Encoding.BigEndianUnicode, false),
            ("410000000037010042000000", Encoding.UTF32, true),
            ("4100000000370100420000", Encoding.UTF32, false),
            ("000000410001370000000042", new UTF32Encoding(true, false), true),
            ("0000004100013700000000", new UTF32Encoding(true, false), false)
        };
        foreach (var vector in vectors)
        {
            byte[] bytes = Convert.FromHexString(vector.Hex);
            for (int split = 0; split <= bytes.Length; split++)
            {
                ByteBuf b = Owner(kind, bytes, split);
                try { Assert.Equal(vector.Expected, ByteBufUtil.IsText(b, vector.Encoding)); }
                finally { b.Release(); }
            }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void OriginalBoundsAndWriterBoundBeforeReaderKeepMarksAndLifetime(int kind)
    {
        ByteBuf b = Owner(kind, new byte[8]);
        try
        {
            b.SetIndex(0, 4);
            foreach (var (index, length) in new[] { (4, 0), (0, 4), (1, 3) })
                Assert.True(ByteBufUtil.IsText(b, index, length, Encoding.ASCII));
            foreach (var (index, length) in new[] { (4, 1), (-1, 2), (3, -1), (3, -2), (5, 0), (1, 5), (int.MaxValue, 1), (1, int.MaxValue) })
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.IsText(b, index, length, Encoding.ASCII));
            b.SetIndex(2, 4).MarkReaderIndex().MarkWriterIndex();
            Assert.True(ByteBufUtil.IsText(b, Encoding.UTF8));
            Assert.True(ByteBufUtil.IsText(b, 0, 4, Encoding.UTF8));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.IsText(b, 0, 8, Encoding.UTF8));
            b.SetIndex(3, 5).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(2, b.ReaderIndex); Assert.Equal(4, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ValidationIsStrictWithoutChangingCallerFallbackAndResetsDecoderState(int kind)
    {
        var encoding = (Encoding)Encoding.UTF8.Clone();
        encoding.DecoderFallback = new DecoderReplacementFallback("replacement");
        DecoderFallback fallback = encoding.DecoderFallback;
        Check(kind, "E282", encoding, false); Check(kind, "41", encoding, true);
        Check(kind, "80", encoding, false); Check(kind, "F0908D88", encoding, true);
        Assert.Same(fallback, encoding.DecoderFallback); Assert.Equal("replacement", encoding.GetString(new byte[] { 128 }));
        var ascii = (Encoding)Encoding.ASCII.Clone(); ascii.DecoderFallback = new DecoderReplacementFallback("x");
        Check(kind, "FF", ascii, false); Assert.Equal("x", ascii.GetString(new byte[] { 255 }));
        Check(kind, "00FF80", Encoding.Latin1, true);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void BoundedScratchDrainsLongInputAndFindsMalformedTail(int kind)
    {
        string text = string.Concat(Enumerable.Repeat("A\u00A2\u20AC\U00010348", 3000));
        foreach (Encoding encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, new UTF32Encoding(true, false) })
        {
            byte[] bytes = encoding.GetBytes(text); ByteBuf b = Owner(kind, bytes, 1);
            try
            {
                Assert.True(ByteBufUtil.IsText(b, encoding));
                Assert.False(ByteBufUtil.IsText(b, 0, bytes.Length - 1, encoding));
            }
            finally { b.Release(); }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task OriginalConcurrentValidationUsesIndependentDecoderState(int kind)
    {
        ByteBuf b = Owner(kind, Encoding.Latin1.GetBytes("Hello, World!"));
        try
        {
            int remaining = 60000;
            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                while (Interlocked.Decrement(ref remaining) > 0) Assert.True(ByteBufUtil.IsText(b, Encoding.Latin1));
            }, TestContext.Current.CancellationToken)));
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(13, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
    }

    [Fact]
    public void NullEmptyReleasedAndSharedCursorContracts()
    {
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.IsText(null, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.IsText(Unpooled.EmptyBuffer, null));
        ByteBuf b = Unpooled.WrappedBuffer(new byte[] { 255, 65 }); ByteBuf wrapper = Unpooled.UnreleasableBuffer(b);
        try
        {
            wrapper.ReaderIndex = 1; Assert.True(ByteBufUtil.IsText(wrapper, Encoding.ASCII));
            Assert.False(ByteBufUtil.IsText(wrapper, 0, 2, Encoding.ASCII));
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(1, b.ReferenceCount);
        }
        finally { b.Release(); }
        foreach (Encoding encoding in new[] { Encoding.UTF8, Encoding.ASCII, Encoding.Unicode, Encoding.Latin1 })
        {
            Assert.True(ByteBufUtil.IsText(Unpooled.EmptyBuffer, encoding));
            ByteBuf dead = Unpooled.Buffer(1); dead.Release();
            Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.IsText(dead, encoding));
            Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.IsText(dead, 0, 0, encoding));
        }
    }

    [Fact]
    public void CustomEncodingWithUtf8CodePageUsesItsOwnDecoder()
    {
        var encoding = new AsciiDecoderEncoding();
        DecoderFallback originalFallback = encoding.DecoderFallback;
        ByteBuf b = Unpooled.WrappedBuffer(new byte[] { 194, 162 });
        try
        {
            Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
            Assert.False(ByteBufUtil.IsText(b, encoding)); Assert.Equal(1, encoding.Calls);
            Assert.Same(originalFallback, encoding.DecoderFallback);
        }
        finally { b.Release(); }
    }

    private sealed class AsciiDecoderEncoding : UTF8Encoding
    {
        public int Calls;
        public override Decoder GetDecoder() { Calls++; return Encoding.ASCII.GetDecoder(); }
    }

    [Fact]
    public void CustomDecoderErrorsPropagateAndNoProgressCannotLoop()
    {
        ByteBuf b = Unpooled.WrappedBuffer(new byte[] { 65 });
        try
        {
            Assert.Throws<InvalidOperationException>(() => ByteBufUtil.IsText(b, new FaultingEncoding()));
            Assert.Throws<InvalidOperationException>(() => ByteBufUtil.IsText(b, new StalledEncoding()));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.IsText(b, 0, 2, new FaultingEncoding()));
        }
        finally { b.Release(); }
    }

    private sealed class FaultingEncoding : UTF8Encoding
    {
        public override Decoder GetDecoder() => throw new InvalidOperationException("Decoder creation failed.");
    }
    private sealed class StalledEncoding : UTF8Encoding
    {
        public override Decoder GetDecoder() => new StalledDecoder();
    }
    private sealed class StalledDecoder : Decoder
    {
        public override int GetCharCount(byte[] bytes, int index, int count) => throw new NotSupportedException();
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => throw new NotSupportedException();
        public override void Convert(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        { bytesUsed = charsUsed = 0; completed = false; }
    }
}
