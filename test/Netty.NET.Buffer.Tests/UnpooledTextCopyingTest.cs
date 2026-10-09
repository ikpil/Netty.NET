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

namespace Netty.NET.Buffer.Tests;

// Selected pinned UnpooledTest text-copy scenarios plus CLR encoding/range/ownership contracts.
public class UnpooledTextCopyingTest
{
    private static Encoding Encoder(int kind) => kind switch
    {
        0 => new UTF8Encoding(true, true),
        1 => new UnicodeEncoding(false, true, true),
        2 => new UnicodeEncoding(true, true, true),
        3 => new UTF32Encoding(false, true, true),
        4 => new UTF32Encoding(true, true, true),
        5 => Encoding.Latin1,
        _ => Encoding.ASCII
    };

    private static ByteBuf Copy(string text, Encoding encoding, int shape) => shape switch
    {
        0 => Unpooled.CopiedBuffer(text, encoding),
        1 => Unpooled.CopiedBuffer("<" + text + ">", 1, text.Length, encoding),
        2 => Unpooled.CopiedBuffer(text.ToCharArray(), encoding),
        3 => Unpooled.CopiedBuffer(("<" + text + ">").ToCharArray(), 1, text.Length, encoding),
        _ => Unpooled.CopiedBuffer(text.AsSpan(), encoding)
    };

    public static IEnumerable<object[]> EncodedCopies()
    {
        for (int kind = 0; kind < 7; ++kind)
            for (int shape = 0; shape < 5; ++shape)
                yield return new object[] { kind, shape };
    }

    [Theory]
    [MemberData(nameof(EncodedCopies))]
    public void CopiesHaveGoldenWireBytesWithoutPreambleAndCanGrow(int kind, int shape)
    {
        string text = kind == 5 ? "Aéÿ" : kind == 6 ? "ABC" : "Aé😀";
        string hex = kind switch
        {
            0 => "41C3A9F09F9880", 1 => "4100E9003DD800DE", 2 => "004100E9D83DDE00",
            3 => "41000000E900000000F60100", 4 => "00000041000000E90001F600",
            5 => "41E9FF", _ => "414243"
        };
        byte[] expected = Convert.FromHexString(hex);
        ByteBuf copy = Copy(text, Encoder(kind), shape);
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy);
            Assert.False(copy.IsReadOnly);
            Assert.Equal(expected.Length, copy.Capacity);
            Assert.Equal(int.MaxValue, copy.MaxCapacity);
            Assert.Equal(0, copy.ReaderIndex);
            Assert.Equal(expected.Length, copy.WriterIndex);
            Assert.Equal(1, copy.ReferenceCount);
            Assert.Equal(expected, copy.ReadableMemory.ToArray());
            copy.WriteByte(0x7f);
            Assert.Equal(expected.Length + 1, copy.WriterIndex);
            Assert.Equal(expected, copy.AsMemory(0, expected.Length).ToArray());
            Assert.Equal(0x7f, copy.GetByte(expected.Length));
        }
        finally { Assert.True(copy.Release()); }
        Assert.Equal(0, copy.ReferenceCount);
    }

    [Theory]
    [InlineData("Some UTF_8 like äÄ∏ŒŒ", 0)]
    [InlineData("Some US_ASCII", 6)]
    [InlineData("Some ISO_8859_1", 5)]
    public void OriginalCopiedBufferCharSequenceScenarios(string text, int kind)
    {
        ByteBuf copy = Unpooled.CopiedBuffer(text, Encoder(kind));
        try { Assert.Equal(text, copy.GetString(Encoder(kind))); }
        finally { copy.Release(); }
    }

    [Fact]
    public void CopiesAreIndependentOfMutableArrayAndBorrowedNativeInput()
    {
        char[] input = "<Aé😀>".ToCharArray();
        ByteBuf arrayCopy = Unpooled.CopiedBuffer(input, 1, 4, Encoding.UTF8);
        ByteBuf owner = Unpooled.DirectBuffer(input.Length * 2);
        ByteBuf nativeCopy = null;
        try
        {
            // A borrowed UTF-16 span replaces the JVM CharBuffer view without transferring ownership.
            Span<char> borrowed = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(
                owner.AsSpan(0, owner.Capacity));
            input.CopyTo(borrowed);
            nativeCopy = Unpooled.CopiedBuffer(borrowed.Slice(1, 4), Encoding.UTF8);
            Assert.Equal(1, owner.ReferenceCount);
            Assert.Equal(0, owner.ReaderIndex);
            Assert.Equal(0, owner.WriterIndex);
            Array.Fill(input, 'X');
            borrowed.Fill('Y');
            Assert.True(owner.Release());
            owner = null;
            Assert.Equal("Aé😀", arrayCopy.GetString(Encoding.UTF8));
            Assert.Equal("Aé😀", nativeCopy.GetString(Encoding.UTF8));
            arrayCopy.SetByte(0, 'Z');
            Assert.Equal("Aé😀", nativeCopy.GetString(Encoding.UTF8));
            Assert.All(input, c => Assert.Equal('X', c));
        }
        finally { owner?.Release(); nativeCopy?.Release(); arrayCopy.Release(); }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 2)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RegionsCheckUtf16BoundsIncludingEmptyRanges(int offset, int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Unpooled.CopiedBuffer("abcd", offset, length, Encoding.UTF8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Unpooled.CopiedBuffer("abcd".ToCharArray(), offset, length, Encoding.UTF8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RegionsCountUtf16UnitsAndHonorFallbackWhenSplittingSurrogatePairs(int offset)
    {
        const string text = "A😀Z";
        ByteBuf stringCopy = Unpooled.CopiedBuffer(text, offset, 1, Encoding.UTF8);
        ByteBuf arrayCopy = Unpooled.CopiedBuffer(text.ToCharArray(), offset, 1, Encoding.UTF8);
        ByteBuf spanCopy = Unpooled.CopiedBuffer(text.AsSpan(offset, 1), Encoding.UTF8);
        try
        {
            byte[] replacement = { 0xef, 0xbf, 0xbd };
            Assert.Equal(replacement, stringCopy.ReadableMemory.ToArray());
            Assert.Equal(replacement, arrayCopy.ReadableMemory.ToArray());
            Assert.Equal(replacement, spanCopy.ReadableMemory.ToArray());
            Assert.Throws<EncoderFallbackException>(() =>
                Unpooled.CopiedBuffer(text, offset, 1, new UTF8Encoding(false, true)));
        }
        finally { stringCopy.Release(); arrayCopy.Release(); spanCopy.Release(); }
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void AllShapesHonorUtf8ReplacementAndExceptionPolicies(int unit)
    {
        string text = new(new[] { (char)unit });
        Encoding strict = new UTF8Encoding(false, true);
        for (int shape = 0; shape < 5; ++shape)
        {
            Assert.Throws<EncoderFallbackException>(() => Copy(text, strict, shape));
            ByteBuf copy = Copy(text, Encoding.UTF8, shape);
            try { Assert.Equal(new byte[] { 0xef, 0xbf, 0xbd }, copy.ReadableMemory.ToArray()); }
            finally { copy.Release(); }
        }
    }

    [Fact]
    public void CustomFallbackPoliciesAreUsedEvenForWholeUtf8AndAsciiStrings()
    {
        Encoding utf8 = (Encoding)Encoding.UTF8.Clone();
        utf8.EncoderFallback = new EncoderReplacementFallback("[bad]");
        Encoding ascii = (Encoding)Encoding.ASCII.Clone();
        ascii.EncoderFallback = new EncoderReplacementFallback("[bad]");
        for (int shape = 0; shape < 5; ++shape)
        {
            ByteBuf a = Copy(new string(new[] { (char)0xd800 }), utf8, shape);
            ByteBuf b = Copy("é", ascii, shape);
            ByteBuf c = Copy("é", Encoding.ASCII, shape);
            try
            {
                byte[] expected = { 91, 98, 97, 100, 93 };
                Assert.Equal(expected, a.ReadableMemory.ToArray());
                Assert.Equal(expected, b.ReadableMemory.ToArray());
                Assert.Equal(new byte[] { 63 }, c.ReadableMemory.ToArray());
            }
            finally { a.Release(); b.Release(); c.Release(); }
        }
        Assert.Equal("[bad]", ((EncoderReplacementFallback)utf8.EncoderFallback).DefaultString);
        Assert.Equal("[bad]", ((EncoderReplacementFallback)ascii.EncoderFallback).DefaultString);
    }

    [Theory]
    [InlineData(0, "EFBFBDE282AC42")]
    [InlineData(1, "EFBFBDF48FBFBF")]
    [InlineData(2, "3F3F")]
    [InlineData(3, "3F3F")]
    public void EncodingPoliciesAreConsistentAcrossWholeStringsAndRegions(int kind, string hex)
    {
        string text = kind switch
        {
            0 => new(new[] { (char)0xd800, '€', 'B' }),
            1 => new(new[] { (char)0xd800, (char)0xdbff, (char)0xdfff }),
            2 => "\u0080é", _ => "😀"
        };
        Encoding encoding = kind < 2 ? Encoding.UTF8 : Encoding.ASCII;
        for (int shape = 0; shape < 5; ++shape)
        {
            ByteBuf copy = Copy(text, encoding, shape);
            try { Assert.Equal(Convert.FromHexString(hex), copy.ReadableMemory.ToArray()); }
            finally { copy.Release(); }
        }
    }

    [Fact]
    public void NonemptyInputWithEmptyFallbackStillProducesAnIndependentGrowableOwner()
    {
        Encoding ascii = (Encoding)Encoding.ASCII.Clone();
        ascii.EncoderFallback = new EncoderReplacementFallback("");
        for (int shape = 0; shape < 5; ++shape)
        {
            ByteBuf copy = Copy("é", ascii, shape);
            try
            {
                Assert.NotSame(Unpooled.EmptyBuffer, copy);
                Assert.Equal(0, copy.Capacity);
                Assert.Equal(0, copy.WriterIndex);
                Assert.Equal(int.MaxValue, copy.MaxCapacity);
                copy.WriteByte(7);
                Assert.Equal(7, copy.ReadByte());
            }
            finally { Assert.True(copy.Release()); }
        }
    }

    [Fact]
    public void EmptyWholeStringOwnsAGrowableBufferWhileEmptyRangesUseSentinel()
    {
        for (int kind = 0; kind < 7; ++kind)
        {
            Encoding encoding = Encoder(kind);
            ByteBuf whole = Unpooled.CopiedBuffer("", encoding);
            ByteBuf other = Unpooled.CopiedBuffer("", encoding);
            try
            {
                Assert.NotSame(Unpooled.EmptyBuffer, whole);
                Assert.NotSame(whole, other);
                Assert.Equal(0, whole.Capacity);
                Assert.Equal(int.MaxValue, whole.MaxCapacity);
                Assert.Equal(0, whole.WriterIndex);
                Assert.Equal(0, whole.ReaderIndex);
                whole.WriteByte(7);
                Assert.Equal(0, other.WriterIndex);
                Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer("A", 1, 0, encoding));
                Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<char>(), encoding));
                Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new[] { 'A' }, 1, 0, encoding));
                Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(ReadOnlySpan<char>.Empty, encoding));
            }
            finally { Assert.True(whole.Release()); Assert.True(other.Release()); }
        }
    }

    [Fact]
    public void NullTextAndEncodingAreRejectedIncludingEmptyInputs()
    {
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((string)null, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((char[])null, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((string)null, 0, 0, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((char[])null, 0, 0, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer("", null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(Array.Empty<char>(), null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer("A", 1, 0, null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(new[] { 'A' }, 1, 0, null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer(ReadOnlySpan<char>.Empty, null));
    }

    [Fact]
    public void EncodingFailureAfterSizingLeavesInputUnchangedAndAllowsRetry()
    {
        char[] input = { '<', 'A', '>' };
        Assert.Throws<InvalidOperationException>(() => Unpooled.CopiedBuffer(input, 1, 1, new FailingEncoding()));
        Assert.Equal(new[] { '<', 'A', '>' }, input);
        ByteBuf retry = Unpooled.CopiedBuffer(input, 1, 1, Encoding.UTF8);
        try { Assert.Equal(new byte[] { 65 }, retry.ReadableMemory.ToArray()); }
        finally { retry.Release(); }
    }

    private sealed class FailingEncoding : UTF8Encoding
    {
        public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            bytes[0] = 0x7f;
            throw new InvalidOperationException("Failed after writing into the new copy.");
        }
    }
}
