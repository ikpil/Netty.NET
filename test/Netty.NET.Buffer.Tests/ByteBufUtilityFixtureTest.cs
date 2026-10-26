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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufUtilityFixtureTest
{
    private sealed class Provider : IDisposable
    {
        private readonly NativeMemoryAllocator _native = new();
        private readonly IByteBufAllocator _allocator;
        private readonly bool _direct;
        public Provider(int provider)
        {
            _direct = (provider & 1) != 0;
            _allocator = (provider / 2) switch
            {
                0 => new UnpooledByteBufAllocator(false, true, _native),
                1 => new PooledByteBufAllocator(false, maxOrder: 4, smallCacheSize: 0, nativeAllocator: _native),
                _ => new AdaptiveByteBufAllocator(false, false, _native)
            };
        }
        public ByteBuf Buffer(int capacity) => _direct ? _allocator.DirectBuffer(capacity) : _allocator.HeapBuffer(capacity);
        public CompositeByteBuf Composite() => _direct ? _allocator.CompositeDirectBuffer() : _allocator.CompositeHeapBuffer();
        public void Dispose() { (_allocator as IDisposable)?.Dispose(); Assert.Equal(0, _native.ReservedBytes); }
    }
    private const string OriginalText = "Some UTF-8 like äÄ∏ŒŒ";
    private static readonly Encoding NettyUtf8 = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback);
    private static readonly string[] ProviderMethods =
    [
        "writeShortBE", "writeMediumBE", "readUnsignedShortBE", "readIntBE",
        "testWriteUtf8Subsequence", "testWriteUtf8SubsequenceSplitSurrogate", "testReserveAndWriteUtf8Subsequence",
        "testWriteUtf8InvalidSubsequences", "testReserveAndWriteUtf8InvalidSubsequences", "testUtf8BytesInvalidSubsequences",
        "testToStringDoesNotThrowIndexOutOfBounds", "testIsTextWithUtf8", "testIsTextWithoutOptimization", "testIsTextWithAscii",
        "testIsTextWithInvalidIndexAndLength", "testUtf8Bytes", "testUtf8BytesWithSurrogates",
        "testUtf8BytesWithNonSurrogates3Bytes", "testUtf8BytesWithNonSurrogatesNonAscii", "testGetBytes"
    ];
    private static readonly string[] HeapMethods =
    [ "testIsTextMultiThreaded", "testGetBytesHeapWithNonZeroArrayOffset", "testGetBytesHeapWithArrayLengthGreaterThanCapacity" ];
    public static IEnumerable<object[]> ProviderCases()
    {
        for (int provider = 0; provider < 6; ++provider)
        foreach (string method in ProviderMethods) yield return [provider, method];
        // These three original bodies require HEAP_UNPOOLED. Do not create their 15 JVM assumption-only invocations.
        foreach (string method in HeapMethods) yield return [0, method];
    }
    [Theory]
    [MemberData(nameof(ProviderCases))]
    public async Task OriginalProviderCases(int provider, string upstreamMethod)
    {
        using var owner = new Provider(provider);
        switch (upstreamMethod)
        {
            case "writeShortBE": case "writeMediumBE": case "readUnsignedShortBE": case "readIntBE":
                NumericCases(owner, upstreamMethod); break;
            case "testWriteUtf8Subsequence": case "testWriteUtf8SubsequenceSplitSurrogate": case "testReserveAndWriteUtf8Subsequence":
                SubsequenceCases(owner, upstreamMethod); break;
            case "testWriteUtf8InvalidSubsequences": case "testReserveAndWriteUtf8InvalidSubsequences": case "testUtf8BytesInvalidSubsequences":
                InvalidSubsequences(owner, upstreamMethod); break;
            case "testToStringDoesNotThrowIndexOutOfBounds":
                DecodeBeyondWriter(owner); break;
            case "testIsTextWithUtf8": case "testIsTextWithoutOptimization": case "testIsTextWithAscii":
                TextVectors(owner, upstreamMethod); break;
            case "testIsTextWithInvalidIndexAndLength":
                ValidationBounds(owner); break;
            case "testUtf8Bytes": case "testUtf8BytesWithSurrogates": case "testUtf8BytesWithNonSurrogates3Bytes": case "testUtf8BytesWithNonSurrogatesNonAscii":
                CountCases(owner, upstreamMethod); break;
            case "testGetBytes":
                ByteBuf b = owner.Buffer(4); try { CheckGetBytes(b); } finally { b.Release(); } break;
            case "testGetBytesHeapWithNonZeroArrayOffset": case "testGetBytesHeapWithArrayLengthGreaterThanCapacity":
                ArraySlice(owner, upstreamMethod); break;
            default: Assert.Equal("testIsTextMultiThreaded", upstreamMethod); await ConcurrentValidation(owner); break;
        }
    }
    private static void NumericCases(Provider owner, string method)
    {
        foreach (bool sourceDefaultLittle in new[] { false, true })
        {
            // Java mutable order is represented by explicit default-order reads/writes in this branch.
            ByteBuf b = owner.Buffer(method == "writeMediumBE" || method == "readIntBE" ? 4 : 2);
            try
            {
                if (method == "writeShortBE")
                {
                    ByteBufUtil.WriteShortBE(b, 0x1234);
                    Assert.Equal(sourceDefaultLittle ? 0x3412 : 0x1234, sourceDefaultLittle ? b.ReadShortLE() : b.ReadShort());
                    b.ResetReaderIndex(); Assert.Equal(0x3412, b.ReadShortLE());
                }
                else if (method == "writeMediumBE")
                {
                    ByteBufUtil.WriteMediumBE(b, 0x123456);
                    Assert.Equal(sourceDefaultLittle ? 0x563412 : 0x123456, sourceDefaultLittle ? b.ReadMediumLE() : b.ReadMedium());
                    b.ResetReaderIndex(); Assert.Equal(0x563412, b.ReadMediumLE());
                }
                else if (method == "readUnsignedShortBE")
                {
                    foreach (int value in new[] { 0x1234, 0xfedc })
                    {
                        int swapped = value == 0x1234 ? 0x3412 : 0xdcfe;
                        b.Clear(); if (sourceDefaultLittle) b.WriteShortLE(value); else b.WriteShort(value);
                        Assert.Equal(sourceDefaultLittle ? swapped : value, ByteBufUtil.ReadUnsignedShortBE(b));
                        b.Clear().WriteShortLE(value); Assert.Equal(swapped, ByteBufUtil.ReadUnsignedShortBE(b));
                    }
                }
                else
                {
                    if (sourceDefaultLittle) b.WriteIntLE(0x12345678); else b.WriteInt(0x12345678);
                    Assert.Equal(sourceDefaultLittle ? 0x78563412 : 0x12345678, ByteBufUtil.ReadIntBE(b));
                    b.Clear().WriteIntLE(0x12345678); Assert.Equal(0x78563412, ByteBufUtil.ReadIntBE(b));
                }
            }
            finally { b.Release(); }
        }
    }
    private static void SubsequenceCases(Provider owner, string method)
    {
        string text = method == "testWriteUtf8SubsequenceSplitSurrogate" ? "\ud800\udc00" : OriginalText;
        int start = method == "testWriteUtf8SubsequenceSplitSurrogate" ? 0 : 5, end = method == "testWriteUtf8SubsequenceSplitSurrogate" ? 1 : 18;
        ByteBuf b = owner.Buffer(16);
        try
        {
            byte[] expected = NettyUtf8.GetBytes(text.AsSpan(start, end - start).ToString());
            int written = method == "testReserveAndWriteUtf8Subsequence" ? ByteBufUtil.ReserveAndWriteUtf8(b, text, start, end, 16)
                : ByteBufUtil.WriteUtf8(b, text, start, end);
            Assert.Equal(expected.Length, written); Assert.Equal(expected, ByteBufUtil.GetBytes(b)); Assert.Equal(0, b.ReaderIndex);
        }
        finally { b.Release(); }
    }
    private static void InvalidSubsequences(Provider owner, string method)
    {
        foreach (var range in new[] { (-1, 5), (5, 30), (10, 5) })
        {
            ByteBuf b = owner.Buffer(16);
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                {
                    if (method == "testWriteUtf8InvalidSubsequences") ByteBufUtil.WriteUtf8(b, OriginalText, range.Item1, range.Item2);
                    else if (method == "testReserveAndWriteUtf8InvalidSubsequences") ByteBufUtil.ReserveAndWriteUtf8(b, OriginalText, range.Item1, range.Item2, 32);
                    else ByteBufUtil.Utf8Bytes(OriginalText, range.Item1, range.Item2);
                });
                Assert.False(b.IsReadable); Assert.Equal((0, 0), (b.ReaderIndex, b.WriterIndex));
            }
            finally { b.Release(); }
        }
    }
    private static void DecodeBeyondWriter(Provider owner)
    {
        CompositeByteBuf b = owner.Composite();
        try
        {
            b.AddComponent(owner.Buffer(4).WriteBytes("1234"u8)); b.AddComponent(owner.Buffer(4).WriteBytes("1234"u8));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(8, b.Capacity);
            Assert.Equal("1234", b.GetString(4, 4, Encoding.UTF8)); Assert.Equal((0, 0), (b.ReaderIndex, b.WriterIndex));
        }
        finally { b.Release(); }
    }
    private static void TextVectors(Provider owner, string method)
    {
        string[] valid, invalid; Encoding encoding;
        if (method == "testIsTextWithUtf8")
        {
            encoding = Encoding.UTF8;
            valid = ["6E65747479", "24", "C2A2", "E282AC", "F0908D88", "24C2A2E282ACF0908D88"];
            invalid = ["80", "F08282AC", "C2", "E282", "F0908D", "C2C0", "E282C0", "F0908DC0", "C180", "E08080", "EDAF80"];
        }
        else if (method == "testIsTextWithoutOptimization") { encoding = Encoding.Unicode; valid = ["01D837DC"]; invalid = ["01D8"]; }
        else { encoding = Encoding.ASCII; valid = ["0001377F"]; invalid = ["80FF"]; }
        foreach (bool expected in new[] { true, false })
        foreach (string hex in expected ? valid : invalid)
        {
            byte[] data = Convert.FromHexString(hex); ByteBuf b = owner.Buffer(data.Length);
            try { b.WriteBytes(data); Assert.Equal(expected, ByteBufUtil.IsText(b, encoding)); Assert.Equal((0, data.Length), (b.ReaderIndex, b.WriterIndex)); }
            finally { b.Release(); }
        }
    }
    private static void ValidationBounds(Provider owner)
    {
        ByteBuf b = owner.Buffer(4);
        try
        {
            b.WriteBytes(new byte[4]);
            foreach (var range in new[] { (4, 0), (0, 4), (1, 3) }) Assert.True(ByteBufUtil.IsText(b, range.Item1, range.Item2, Encoding.ASCII));
            foreach (var range in new[] { (4, 1), (-1, 2), (3, -1), (3, -2), (5, 0), (1, 5) })
                Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.IsText(b, range.Item1, range.Item2, Encoding.ASCII));
            Assert.Equal((0, 4), (b.ReaderIndex, b.WriterIndex));
        }
        finally { b.Release(); }
    }
    private static void CountCases(Provider owner, string method)
    {
        string text = method switch { "testUtf8Bytes" => OriginalText, "testUtf8BytesWithSurrogates" => "a\ud800\udc00b",
            "testUtf8BytesWithNonSurrogates3Bytes" => "a\ue000b", _ => "a\u0081b" };
        ByteBuf b = owner.Buffer(ByteBufUtil.Utf8MaxBytes(text));
        try { Assert.Equal(ByteBufUtil.WriteUtf8(b, text), ByteBufUtil.Utf8Bytes(text)); Assert.Equal(NettyUtf8.GetBytes(text), ByteBufUtil.GetBytes(b)); }
        finally { b.Release(); }
    }
    private static void CheckGetBytes(ByteBuf b)
    {
        b.WriteInt(0x01020304);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b, 0, b.ReadableBytes, false));
        foreach (var row in new[] { (0, new byte[] { 1, 2, 3 }), (1, new byte[] { 2, 3, 4 }), (1, new byte[] { 2, 3 }) })
        {
            Assert.Equal(row.Item2, ByteBufUtil.GetBytes(b, row.Item1, row.Item2.Length));
            Assert.Equal(row.Item2, ByteBufUtil.GetBytes(b, row.Item1, row.Item2.Length, false));
        }
        Assert.Equal((0, 4), (b.ReaderIndex, b.WriterIndex));
    }
    private static void ArraySlice(Provider owner, string method)
    {
        ByteBuf root = owner.Buffer(5);
        try
        {
            int offset = method == "testGetBytesHeapWithNonZeroArrayOffset" ? 1 : 0;
            root.SetByte(offset == 1 ? 0 : 4, 5);
            ByteBuf slice = root.Slice(offset, 4); slice.WriterIndex = 0;
            Assert.True(MemoryMarshal.TryGetArray(slice.AsReadOnlyMemory(0, slice.Capacity), out ArraySegment<byte> array)); Assert.Equal(offset, array.Offset);
            if (offset == 1) Assert.Equal(root.Capacity, array.Array!.Length); else Assert.True(array.Array!.Length > slice.Capacity);
            CheckGetBytes(slice);
        }
        finally { root.Release(); }
    }
    private static async Task ConcurrentValidation(Provider owner)
    {
        ByteBuf b = owner.Buffer(13).WriteBytes("Hello, World!"u8);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30)); int counter = 60000, completed = 0;
        try
        {
            Task[] workers = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                while (Interlocked.Decrement(ref counter) > 0)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    Assert.True(ByteBufUtil.IsText(b, Encoding.Latin1)); Interlocked.Increment(ref completed);
                }
            }, timeout.Token)).ToArray();
            await Task.WhenAll(workers);
            Assert.Equal(59999, completed); Assert.Equal((0, 13, 1), (b.ReaderIndex, b.WriterIndex, b.ReferenceCount));
        }
        finally { b.Release(); }
    }
    public static IEnumerable<object[]> StandaloneCases()
    {
        foreach (string method in new[] { "testIndexOf", "testIndexOfNeedleWithReaderIndex", "equalsBufferSubsections", "notEqualsBufferSubsections",
                     "notEqualsBufferOverflow", "notEqualsBufferUnderflow", "setShortBE", "testUtf8BytesSubsequence", "testDecodeUsAscii", "testDecodeUtf8" }) yield return [method];
    }
    [Theory]
    [MemberData(nameof(StandaloneCases))]
    public void OriginalStandaloneCases(string upstreamMethod)
    {
        if (upstreamMethod.StartsWith("testIndexOf", StringComparison.Ordinal)) { NeedleCases(upstreamMethod); return; }
        if (upstreamMethod.Contains("Buffer", StringComparison.Ordinal)) { EqualityCases(upstreamMethod); return; }
        if (upstreamMethod == "testUtf8BytesSubsequence") { Assert.Equal(NettyUtf8.GetBytes(OriginalText[5..18]).Length, ByteBufUtil.Utf8Bytes(OriginalText, 5, 18)); return; }
        if (upstreamMethod == "setShortBE")
        {
            foreach (bool sourceDefaultLittle in new[] { false, true })
            {
                ByteBuf b = Unpooled.WrappedBuffer(new byte[2]);
                try
                {
                    ByteBufUtil.SetShortBE(b, 0, 0x1234); Assert.Equal(0x3412, b.ReadShortLE()); b.ResetReaderIndex();
                    Assert.Equal(sourceDefaultLittle ? 0x3412 : 0x1234, sourceDefaultLittle ? b.ReadShortLE() : b.ReadShort());
                }
                finally { b.Release(); }
            }
            return;
        }
        string text = upstreamMethod == "testDecodeUsAscii" ? "This is a test" : OriginalText;
        Encoding encoding = upstreamMethod == "testDecodeUsAscii" ? Encoding.ASCII : Encoding.UTF8;
        ByteBuf copy = Unpooled.CopiedBuffer(text, encoding);
        try { Assert.Equal(text, copy.GetString(0, copy.ReadableBytes, encoding)); } finally { copy.Release(); }
    }
    private static void EqualityCases(string method)
    {
        int firstLength = method == "equalsBufferSubsections" ? 128 : method == "notEqualsBufferSubsections" ? 50 : 8;
        byte[] first = new byte[firstLength], second = new byte[firstLength <= 8 ? 16 : 256];
        var random = new Random(5656); random.NextBytes(first); random.NextBytes(second);
        int aIndex = first.Length / 2, bIndex = aIndex + first.Length, length = first.Length - aIndex;
        Array.Copy(first, aIndex, second, bIndex, method is "notEqualsBufferOverflow" or "notEqualsBufferUnderflow" ? length - 1 : length);
        if (method == "notEqualsBufferSubsections")
        {
            // Randomly pick an index in the range that will be compared and make the value at that index differ between
            // the 2 arrays.
            int different = random.Next(aIndex, aIndex + length); first[different] = unchecked((byte)(first[different] + 1));
        }
        ByteBuf a = Unpooled.WrappedBuffer(first), b = Unpooled.WrappedBuffer(second);
        try
        {
            if (method == "notEqualsBufferUnderflow") Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.Equals(a, aIndex, b, bIndex, -1));
            else Assert.Equal(method == "equalsBufferSubsections", ByteBufUtil.Equals(a, aIndex, b, bIndex,
                method == "notEqualsBufferOverflow" ? Math.Max(first.Length, second.Length) * 2 : length));
        }
        finally { a.Release(); b.Release(); }
    }
    private static void NeedleCases(string method)
    {
        static void Find(string input, string data, int expected, int inputReader = 0, int dataReader = 0, bool wrapped = false)
        {
            ByteBuf needle = Unpooled.CopiedBuffer(input, Encoding.UTF8), root = Unpooled.CopiedBuffer(data, Encoding.UTF8);
            ByteBuf haystack = wrapped ? new ByteBufUtilitySourceTest.Wrapper(root) : root;
            try
            {
                needle.ReaderIndex = inputReader; haystack.ReaderIndex = dataReader;
                Assert.Equal(expected, ByteBufUtil.IndexOf(needle, haystack)); Assert.Equal(inputReader, needle.ReaderIndex); Assert.Equal(dataReader, haystack.ReaderIndex);
            }
            finally { needle.Release(); root.Release(); }
        }
        if (method == "testIndexOfNeedleWithReaderIndex") { Find("abc123", "ab1123", 3, 3); Find("xab", "aab", 1, 1); return; }
        foreach (var row in new[] { ("a", 0), ("bc", 1), ("c", 2), ("abc12", 0), ("abcdef", -1), ("abc12x", -1), ("abc123def", -1) }) Find(row.Item1, "abc123", row.Item2);
        Find("abc12", "abc123", 1, 1, 1); Find("abc12", "abc123", 3, 3, 2); Find("abc12", "abc123", 2, 2, 1);
        Find("a", "abc123", 0, wrapped: true); Find("bc", "abc123", 1, wrapped: true); Find("abcdef", "abc123", -1, wrapped: true);
        Find("aab", "123aab123", 3);
    }
}
