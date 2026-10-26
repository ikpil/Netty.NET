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

public class ByteBufUtilTest
{
    public enum BufferType
    {
        DirectUnpooled = 1,
        DirectPooled = 3,
        DirectAdaptive = 5,
        HeapPooled = 2,
        HeapUnpooled = 0,
        HeapAdaptive = 4
    }

    private sealed class Provider : IDisposable
    {
        private readonly NativeMemoryAllocator _native = new();
        private readonly IByteBufAllocator _allocator;
        private readonly bool _direct;
        public Provider(BufferType bufferType)
        {
            int provider = (int)bufferType;
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
    // Java UTF-8 encodes an isolated UTF-16 surrogate as '?'.
    private static readonly Encoding NettyUtf8 = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback);

    public static IEnumerable<object[]> NoUnsafe()
    {
        yield return [BufferType.DirectPooled];
        yield return [BufferType.DirectUnpooled];
        yield return [BufferType.HeapPooled];
        yield return [BufferType.HeapUnpooled];
        yield return [BufferType.DirectAdaptive];
        yield return [BufferType.HeapAdaptive];
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void WriteShortBE(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        int expected = 0x1234;

        ByteBuf buf = owner.Buffer(2); // CLR byte order is explicit at each read/write call.
        ByteBufUtil.WriteShortBE(buf, expected);
        Assert.Equal(expected, buf.ReadShort());
        buf.ResetReaderIndex();
        Assert.Equal(ByteBufUtil.SwapShort((short)expected), buf.ReadShortLE());
        buf.Release();

        buf = owner.Buffer(2); // CLR byte order is explicit at each read/write call.
        ByteBufUtil.WriteShortBE(buf, expected);
        Assert.Equal(ByteBufUtil.SwapShort((short)expected), buf.ReadShortLE());
        buf.ResetReaderIndex();
        Assert.Equal(ByteBufUtil.SwapShort((short)expected), buf.ReadShortLE());
        buf.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void WriteMediumBE(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        int mediumValue = 0x123456;

        ByteBuf buf = owner.Buffer(4); // CLR byte order is explicit at each read/write call.
        ByteBufUtil.WriteMediumBE(buf, mediumValue);
        Assert.Equal(mediumValue, buf.ReadMedium());
        buf.ResetReaderIndex();
        Assert.Equal(ByteBufUtil.SwapMedium(mediumValue), buf.ReadMediumLE());
        buf.Release();

        buf = owner.Buffer(4); // CLR byte order is explicit at each read/write call.
        ByteBufUtil.WriteMediumBE(buf, mediumValue);
        Assert.Equal(ByteBufUtil.SwapMedium(mediumValue), buf.ReadMediumLE());
        buf.ResetReaderIndex();
        Assert.Equal(ByteBufUtil.SwapMedium(mediumValue), buf.ReadMediumLE());
        buf.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void ReadUnsignedShortBE(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        int shortValue = 0x1234; // unsigned short
        int swappedShortValue = 0x3412; // swapped version of the value above

        ByteBuf buf = owner.Buffer(2); // CLR byte order is explicit at each read/write call.
        buf.WriteShort(shortValue);
        Assert.Equal(shortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Clear();
        buf.WriteShortLE(shortValue);
        Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Release();

        buf = owner.Buffer(2); // CLR byte order is explicit at each read/write call.
        buf.WriteShortLE(shortValue);
        Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Clear();
        buf.WriteShortLE(shortValue);
        Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Release();

        shortValue = 0xfedc; // unsigned short
        swappedShortValue = 0xdcfe; // swapped version of the value above

        buf = owner.Buffer(2); // CLR byte order is explicit at each read/write call.
        buf.WriteShort(shortValue);
        Assert.Equal(shortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Clear();
        buf.WriteShortLE(shortValue);
        Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Release();

        buf = owner.Buffer(2); // CLR byte order is explicit at each read/write call.
        buf.WriteShortLE(shortValue);
        Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Clear();
        buf.WriteShortLE(shortValue);
        Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(buf));
        buf.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void ReadIntBE(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        int intValue = 0x12345678;

        ByteBuf buf = owner.Buffer(4); // CLR byte order is explicit at each read/write call.
        buf.WriteInt(intValue);
        Assert.Equal(intValue, ByteBufUtil.ReadIntBE(buf));
        buf.Clear();
        buf.WriteIntLE(intValue);
        Assert.Equal(ByteBufUtil.SwapInt(intValue), ByteBufUtil.ReadIntBE(buf));
        buf.Release();

        buf = owner.Buffer(4); // CLR byte order is explicit at each read/write call.
        buf.WriteIntLE(intValue);
        Assert.Equal(ByteBufUtil.SwapInt(intValue), ByteBufUtil.ReadIntBE(buf));
        buf.Clear();
        buf.WriteIntLE(intValue);
        Assert.Equal(ByteBufUtil.SwapInt(intValue), ByteBufUtil.ReadIntBE(buf));
        buf.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8Subsequence(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "Some UTF-8 like äÄ∏ŒŒ";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(usAscii[5..18]));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, usAscii, 5, 18);

        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8SubsequenceSplitSurrogate(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "\uD800\uDC00"; // surrogate pair: one code point, two chars
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(usAscii[0..1]));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, usAscii, 0, 1);

        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestReserveAndWriteUtf8Subsequence(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "Some UTF-8 like äÄ∏ŒŒ";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(usAscii[5..18]));
        ByteBuf buf2 = owner.Buffer(16);
        int count = ByteBufUtil.ReserveAndWriteUtf8(buf2, usAscii, 5, 18, 16);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, count);

        buf.Release();
        buf2.Release();
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidSubsequences(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        TestInvalidSubsequences(owner, (buf, text, start, end) => ByteBufUtil.WriteUtf8(buf, text, start, end));
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestReserveAndWriteUtf8InvalidSubsequences(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        TestInvalidSubsequences(owner, (buf, text, start, end) => ByteBufUtil.ReserveAndWriteUtf8(buf, text, start, end, 32));
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestUtf8BytesInvalidSubsequences(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        TestInvalidSubsequences(owner, (buf, text, start, end) => ByteBufUtil.Utf8Bytes(text, start, end));
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestToStringDoesNotThrowIndexOutOfBounds(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        CompositeByteBuf buffer = owner.Composite();
        try
        {
            byte[] bytes = NettyUtf8.GetBytes("1234");
            buffer.AddComponent(owner.Buffer(bytes.Length).WriteBytes(bytes));
            buffer.AddComponent(owner.Buffer(bytes.Length).WriteBytes(bytes));
            Assert.Equal("1234", buffer.GetString(bytes.Length, bytes.Length, NettyUtf8));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestIsTextWithUtf8(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        byte[][] validUtf8Bytes = [
                NettyUtf8.GetBytes("netty"),
                [(byte) 0x24],
                [(byte) 0xC2, (byte) 0xA2],
                [(byte) 0xE2, (byte) 0x82, (byte) 0xAC],
                [(byte) 0xF0, (byte) 0x90, (byte) 0x8D, (byte) 0x88],
                [(byte) 0x24,
                        (byte) 0xC2, (byte) 0xA2,
                        (byte) 0xE2, (byte) 0x82, (byte) 0xAC,
                        (byte) 0xF0, (byte) 0x90, (byte) 0x8D, (byte) 0x88] // multiple characters
        ];
        foreach (byte[] bytes in validUtf8Bytes)
        {
            AssertIsText(owner, bytes, true, NettyUtf8);
        }
        byte[][] invalidUtf8Bytes = [
                [(byte) 0x80],
                [(byte) 0xF0, (byte) 0x82, (byte) 0x82, (byte) 0xAC], // Overlong encodings
                [(byte) 0xC2],                                        // not enough bytes
                [(byte) 0xE2, (byte) 0x82],                           // not enough bytes
                [(byte) 0xF0, (byte) 0x90, (byte) 0x8D],              // not enough bytes
                [(byte) 0xC2, (byte) 0xC0],                           // not correct bytes
                [(byte) 0xE2, (byte) 0x82, (byte) 0xC0],              // not correct bytes
                [(byte) 0xF0, (byte) 0x90, (byte) 0x8D, (byte) 0xC0], // not correct bytes
                [(byte) 0xC1, (byte) 0x80],                           // out of lower bound
                [(byte) 0xE0, (byte) 0x80, (byte) 0x80],              // out of lower bound
                [(byte) 0xED, (byte) 0xAF, (byte) 0x80]               // out of upper bound
        ];
        foreach (byte[] bytes in invalidUtf8Bytes)
        {
            AssertIsText(owner, bytes, false, NettyUtf8);
        }
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestIsTextWithoutOptimization(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        byte[] validBytes = { (byte)0x01, (byte)0xD8, (byte)0x37, (byte)0xDC };
        byte[] invalidBytes = { (byte)0x01, (byte)0xD8 };

        AssertIsText(owner, validBytes, true, Encoding.Unicode);
        AssertIsText(owner, invalidBytes, false, Encoding.Unicode);
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestIsTextWithAscii(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        byte[] validBytes = { (byte)0x00, (byte)0x01, (byte)0x37, (byte)0x7F };
        byte[] invalidBytes = { (byte)0x80, (byte)0xFF };

        AssertIsText(owner, validBytes, true, Encoding.ASCII);
        AssertIsText(owner, invalidBytes, false, Encoding.ASCII);
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestIsTextWithInvalidIndexAndLength(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        ByteBuf buffer = owner.Buffer(4);
        try
        {
            buffer.WriteBytes(new byte[4]);
            int[][] validIndexLengthPairs = [
                    [4, 0],
                    [0, 4],
                    [1, 3],
            ];
            foreach (int[] pair in validIndexLengthPairs)
            {
                Assert.True(ByteBufUtil.IsText(buffer, pair[0], pair[1], Encoding.ASCII));
            }
            int[][] invalidIndexLengthPairs = [
                    [4, 1],
                    [-1, 2],
                    [3, -1],
                    [3, -2],
                    [5, 0],
                    [1, 5],
            ];
            foreach (int[] pair in invalidIndexLengthPairs)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                        ByteBufUtil.IsText(buffer, pair[0], pair[1], Encoding.ASCII));
            }
        }
        finally
        {
            buffer.Release();
        }
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestUtf8Bytes(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string s = "Some UTF-8 like äÄ∏ŒŒ";
        CheckUtf8Bytes(owner, s);
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestUtf8BytesWithSurrogates(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string s = "a\uD800\uDC00b";
        CheckUtf8Bytes(owner, s);
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestUtf8BytesWithNonSurrogates3Bytes(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string s = "a\uE000b";
        CheckUtf8Bytes(owner, s);
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestUtf8BytesWithNonSurrogatesNonAscii(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        char nonAscii = (char)0x81;
        string s = "a" + nonAscii + "b";
        CheckUtf8Bytes(owner, s);
    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestGetBytes(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        ByteBuf buf = owner.Buffer(4);
        try
        {
            CheckGetBytes(buf);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public async Task TestIsTextMultiThreaded()
    {
        ByteBuf b = Unpooled.CopiedBuffer("Hello, World!", Encoding.Latin1);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        int counter = 60000, completed = 0;
        try
        {
            Task[] workers = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                while (Interlocked.Decrement(ref counter) > 0)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    Assert.True(ByteBufUtil.IsText(b, Encoding.Latin1));
                    Interlocked.Increment(ref completed);
                }
            }, timeout.Token)).ToArray();
            await Task.WhenAll(workers);
            Assert.Equal(59999, completed);
            Assert.Equal((0, 13, 1), (b.ReaderIndex, b.WriterIndex, b.ReferenceCount));
        }
        finally { b.Release(); }
    }

    [Fact]
    public void TestGetBytesHeapWithNonZeroArrayOffset()
    {
        using var owner = new Provider(BufferType.HeapUnpooled);

        ByteBuf buf = owner.Buffer(5);
        try
        {
            buf.SetByte(0, 0x05);

            ByteBuf slice = buf.Slice(1, 4);
            slice.WriterIndex = 0;

            Assert.True(MemoryMarshal.TryGetArray(slice.AsReadOnlyMemory(0, slice.Capacity), out ArraySegment<byte> array));
            Assert.Equal(1, array.Offset);
            Assert.Equal(buf.Capacity, array.Array!.Length);

            CheckGetBytes(slice);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestGetBytesHeapWithArrayLengthGreaterThanCapacity()
    {
        using var owner = new Provider(BufferType.HeapUnpooled);

        ByteBuf buf = owner.Buffer(5);
        try
        {
            buf.SetByte(4, 0x05);

            ByteBuf slice = buf.Slice(0, 4);
            slice.WriterIndex = 0;

            Assert.True(MemoryMarshal.TryGetArray(slice.AsReadOnlyMemory(0, slice.Capacity), out ArraySegment<byte> array));
            Assert.Equal(0, array.Offset);
            Assert.True(array.Array!.Length > slice.Capacity);

            CheckGetBytes(slice);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestIndexOf()
    {
        var copies = new List<ByteBuf>();
        ByteBuf Copy(string value, Encoding encoding)
        { ByteBuf copy = Unpooled.CopiedBuffer(value, encoding); copies.Add(copy); return copy; }
        ByteBuf CopyBytes(byte[] value)
        { ByteBuf copy = Unpooled.CopiedBuffer(value); copies.Add(copy); return copy; }
        try
        {
            ByteBuf haystack = Copy("abc123", NettyUtf8);
            Assert.Equal(0, ByteBufUtil.IndexOf(Copy("a", NettyUtf8), haystack));
            Assert.Equal(1, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("bc")), haystack));
            Assert.Equal(2, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("c")), haystack));
            Assert.Equal(0, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("abc12")), haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("abcdef")), haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("abc12x")), haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("abc123def")), haystack));

            ByteBuf needle = Copy("abc12", NettyUtf8);
            haystack.ReaderIndex = 1;
            needle.ReaderIndex = 1;
            Assert.Equal(1, ByteBufUtil.IndexOf(needle, haystack));
            haystack.ReaderIndex = 2;
            needle.ReaderIndex = 3;
            Assert.Equal(3, ByteBufUtil.IndexOf(needle, haystack));
            haystack.ReaderIndex = 1;
            needle.ReaderIndex = 2;
            Assert.Equal(2, ByteBufUtil.IndexOf(needle, haystack));
            haystack.Release();

            haystack = new ByteBufUtilitySourceTest.Wrapper(Copy("abc123", NettyUtf8));
            Assert.Equal(0, ByteBufUtil.IndexOf(Copy("a", NettyUtf8), haystack));
            Assert.Equal(1, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("bc")), haystack));
            Assert.Equal(-1, ByteBufUtil.IndexOf(CopyBytes(NettyUtf8.GetBytes("abcdef")), haystack));
            haystack.Release();

            haystack = Copy("123aab123", NettyUtf8);
            Assert.Equal(3, ByteBufUtil.IndexOf(Copy("aab", NettyUtf8), haystack));
            haystack.Release();
            needle.Release();

        }
        finally { foreach (ByteBuf copy in copies) if (copy.ReferenceCount > 0) copy.Release(); }
    }

    [Fact]
    public void TestIndexOfNeedleWithReaderIndex()
    {
        ByteBuf needle = Unpooled.CopiedBuffer("abc123", Encoding.ASCII);
        needle.ReaderIndex = 3;
        ByteBuf haystack = Unpooled.CopiedBuffer("ab1123", Encoding.ASCII);
        Assert.Equal(3, ByteBufUtil.IndexOf(needle, haystack));
        haystack.Release();
        needle.Release();

        needle = Unpooled.CopiedBuffer("xab", Encoding.ASCII);
        needle.ReaderIndex = 1;
        haystack = Unpooled.CopiedBuffer("aab", Encoding.ASCII);
        Assert.Equal(1, ByteBufUtil.IndexOf(needle, haystack));
        haystack.Release();
        needle.Release();
    }

    [Fact]
    public void EqualsBufferSubsections()
    {
        byte[] b1 = new byte[128];
        byte[] b2 = new byte[256];
        Random.Shared.NextBytes(b1);
        Random.Shared.NextBytes(b2);
        int iB1 = b1.Length / 2;
        int iB2 = iB1 + b1.Length;
        int length = b1.Length - iB1;
        Array.Copy(b1, iB1, b2, iB2, length);
        ByteBuf first = Unpooled.WrappedBuffer(b1), second = Unpooled.WrappedBuffer(b2);
        try
        {
            Assert.True(ByteBufUtil.Equals(first, iB1, second, iB2, length));

        }
        finally { first.Release(); second.Release(); }
    }

    [Fact]
    public void NotEqualsBufferSubsections()
    {
        byte[] b1 = new byte[50];
        byte[] b2 = new byte[256];
        Random.Shared.NextBytes(b1);
        Random.Shared.NextBytes(b2);
        int iB1 = b1.Length / 2;
        int iB2 = iB1 + b1.Length;
        int length = b1.Length - iB1;
        Array.Copy(b1, iB1, b2, iB2, length);
        // Randomly pick an index in the range that will be compared and make the value at that index differ between
        // the 2 arrays.
        int diffIndex = Random.Shared.Next(iB1, iB1 + length);
        b1[diffIndex] = unchecked((byte)(b1[diffIndex] + 1));
        ByteBuf first = Unpooled.WrappedBuffer(b1), second = Unpooled.WrappedBuffer(b2);
        try
        {
            Assert.False(ByteBufUtil.Equals(first, iB1, second, iB2, length));

        }
        finally { first.Release(); second.Release(); }
    }

    [Fact]
    public void NotEqualsBufferOverflow()
    {
        byte[] b1 = new byte[8];
        byte[] b2 = new byte[16];
        Random.Shared.NextBytes(b1);
        Random.Shared.NextBytes(b2);
        int iB1 = b1.Length / 2;
        int iB2 = iB1 + b1.Length;
        int length = b1.Length - iB1;
        Array.Copy(b1, iB1, b2, iB2, length - 1);
        ByteBuf first = Unpooled.WrappedBuffer(b1), second = Unpooled.WrappedBuffer(b2);
        try
        {
            Assert.False(ByteBufUtil.Equals(first, iB1, second, iB2,
                    Math.Max(b1.Length, b2.Length) * 2));

        }
        finally { first.Release(); second.Release(); }
    }

    [Fact]
    public void NotEqualsBufferUnderflow()
    {
        byte[] b1 = new byte[8];
        byte[] b2 = new byte[16];
        Random.Shared.NextBytes(b1);
        Random.Shared.NextBytes(b2);
        int iB1 = b1.Length / 2;
        int iB2 = iB1 + b1.Length;
        int length = b1.Length - iB1;
        Array.Copy(b1, iB1, b2, iB2, length - 1);
        ByteBuf first = Unpooled.WrappedBuffer(b1), second = Unpooled.WrappedBuffer(b2);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                ByteBufUtil.Equals(first, iB1, second, iB2, -1);
            });

        }
        finally { first.Release(); second.Release(); }
    }

    [Fact]
    public void SetShortBE()
    {
        int shortValue = 0x1234;

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[2]); // CLR byte order is explicit at each read/write call.
        ByteBufUtil.SetShortBE(buf, 0, shortValue);
        Assert.Equal(shortValue, buf.ReadShort());
        buf.ResetReaderIndex();
        Assert.Equal(ByteBufUtil.SwapShort((short)shortValue), buf.ReadShortLE());
        buf.Release();

        buf = Unpooled.WrappedBuffer(new byte[2]); // CLR byte order is explicit at each read/write call.
        ByteBufUtil.SetShortBE(buf, 0, shortValue);
        Assert.Equal(ByteBufUtil.SwapShort((short)shortValue), buf.ReadShortLE());
        buf.ResetReaderIndex();
        Assert.Equal(ByteBufUtil.SwapShort((short)shortValue), buf.ReadShortLE());
        buf.Release();
    }

    [Fact]
    public void TestUtf8BytesSubsequence()
    {
        string usAscii = "Some UTF-8 like äÄ∏ŒŒ";
        Assert.Equal(NettyUtf8.GetBytes(usAscii[5..18]).Length,
                ByteBufUtil.Utf8Bytes(usAscii, 5, 18));
    }

    [Fact]
    public void TestDecodeUsAscii()
    {
        TestDecodeString("This is a test", Encoding.ASCII);
    }

    [Fact]
    public void TestDecodeUtf8()
    {
        TestDecodeString("Some UTF-8 like äÄ∏ŒŒ", NettyUtf8);
    }

    private static void CheckUtf8Bytes(Provider owner, string charSequence)
    {
        ByteBuf buf = owner.Buffer(ByteBufUtil.Utf8MaxBytes(charSequence));
        try
        {
            int writtenBytes = ByteBufUtil.WriteUtf8(buf, charSequence);
            int utf8Bytes = ByteBufUtil.Utf8Bytes(charSequence);
            Assert.Equal(writtenBytes, utf8Bytes);
        }
        finally
        {
            buf.Release();
        }
    }

    private static void AssertIsText(Provider owner, byte[] bytes, bool expected, Encoding charset)
    {
        ByteBuf buffer = owner.Buffer(bytes.Length);
        try
        {
            buffer.WriteBytes(bytes);
            Assert.Equal(expected, ByteBufUtil.IsText(buffer, charset));
        }
        finally
        {
            buffer.Release();
        }
    }

    private static void CheckGetBytes(ByteBuf buf)
    {
        buf.WriteInt(0x01020304);

        byte[] expected = { 0x01, 0x02, 0x03, 0x04 };
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf));
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 0, buf.ReadableBytes, false));

        expected = new byte[] { 0x01, 0x02, 0x03 };
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 0, 3));
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 0, 3, false));

        expected = new byte[] { 0x02, 0x03, 0x04 };
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 1, 3));
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 1, 3, false));

        expected = new byte[] { 0x02, 0x03 };
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 1, 2));
        Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 1, 2, false));
    }

    private static void TestDecodeString(string text, Encoding charset)
    {
        ByteBuf buffer = Unpooled.CopiedBuffer(text, charset);
        Assert.Equal(text, buffer.GetString(0, buffer.ReadableBytes, charset));
        buffer.Release();
    }

    private static void TestInvalidSubsequences(Provider owner, Func<ByteBuf, string, int, int, int> method)
    {
        foreach (int[] range in new[] { new[] { -1, 5 }, new[] { 5, 30 }, new[] { 10, 5 } })
        {
            ByteBuf buf = owner.Buffer(16);
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => method(buf, "Some UTF-8 like äÄ∏ŒŒ", range[0], range[1]));
            }
            finally
            {
                Assert.False(buf.IsReadable);
                buf.Release();
            }
        }
    }
    [Fact]
    public void DecodeRandomHexBytesWithEvenLength()
    {
        decodeRandomHexBytes(256);

    }

    [Fact]
    public void DecodeRandomHexBytesWithOddLength()
    {
        decodeRandomHexBytes(257);

    }

    [Fact]
    public void DecodeHexDumpWithOddLength()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            ByteBufUtil.DecodeHexDump("abc");
        });

    }

    [Fact]
    public void DecodeHexDumpWithInvalidChar()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            ByteBufUtil.DecodeHexDump("fg");
        });

    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TestIndexOfMatchesNaiveSearch(int needleReaderIndex)
    {
        // CLR seeded RNG values differ from Java; keep all 10,000 trials, the small alphabet,
        // original lengths/prefixes and the independent scalar oracle for every generated input.
        Random random = new Random(needleReaderIndex);
        for (int i = 0; i < 10000; i++)
        {
            byte[] needleBytes = new byte[2 + random.Next(5)];
            byte[] haystackBytes = new byte[random.Next(16)];
            byte[] prefix = new byte[needleReaderIndex];
            foreach (byte[] bytes in new[] { needleBytes, haystackBytes, prefix })
            {
                for (int j = 0; j < bytes.Length; j++)
                {
                    bytes[j] = (byte)random.Next(3);
                }
            }
            ByteBuf needle = Unpooled.Buffer().WriteBytes(prefix).WriteBytes(needleBytes);
            needle.ReaderIndex = prefix.Length;
            ByteBuf haystack = Unpooled.WrappedBuffer(haystackBytes);
            Assert.Equal(naiveIndexOf(needleBytes, haystackBytes), ByteBufUtil.IndexOf(needle, haystack));
            needle.Release();
            haystack.Release();
        }

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUsAscii(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "NettyRocks";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(Encoding.ASCII.GetBytes(usAscii));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteAscii(buf2, usAscii);

        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUsAsciiSwapped(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        // CLR byte writes have no mutable order. Keep the forwarding facade exercised by the Java swapped view.
        string usAscii = "NettyRocks";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(Encoding.ASCII.GetBytes(usAscii));
        ByteBuf buf2 = new ByteBufUtilitySourceTest.Wrapper(owner.Buffer(16));
        ByteBufUtil.WriteAscii(buf2, usAscii);

        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUsAsciiWrapped(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "NettyRocks";
        ByteBuf buf = Unpooled.UnreleasableBuffer(owner.Buffer(16));
        AssertWrapped(buf);
        buf.WriteBytes(Encoding.ASCII.GetBytes(usAscii));
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(owner.Buffer(16));
        AssertWrapped(buf2);
        ByteBufUtil.WriteAscii(buf2, usAscii);

        Assert.Equal(buf, buf2);

        buf.Unwrap().Release();
        buf2.Unwrap().Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUsAsciiComposite(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "NettyRocks";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(Encoding.ASCII.GetBytes(usAscii));
        ByteBuf buf2 = owner.Composite().AddComponent(
                owner.Buffer(8)).AddComponent(owner.Buffer(24));
        // write some byte so we start writing with an offset.
        buf2.WriteByte(1);
        ByteBufUtil.WriteAscii(buf2, usAscii);

        // Skip the previously written byte.
        Assert.Equal(buf, buf2.SkipBytes(1));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUsAsciiCompositeWrapped(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "NettyRocks";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(Encoding.ASCII.GetBytes(usAscii));
        ByteBuf buf2 = new WrappedCompositeByteBuf(owner.Composite().AddComponent(
                owner.Buffer(8)).AddComponent(owner.Buffer(24)));
        // write some byte so we start writing with an offset.
        buf2.WriteByte(1);
        ByteBufUtil.WriteAscii(buf2, usAscii);

        // Skip the previously written byte.
        Assert.Equal(buf, buf2.SkipBytes(1));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "Some UTF-8 like äÄ∏ŒŒ";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(usAscii));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, usAscii);

        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8Composite(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string utf8 = "Some UTF-8 like äÄ∏ŒŒ";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(utf8));
        ByteBuf buf2 = owner.Composite().AddComponent(
                owner.Buffer(8)).AddComponent(owner.Buffer(24));
        // write some byte so we start writing with an offset.
        buf2.WriteByte(1);
        ByteBufUtil.WriteUtf8(buf2, utf8);

        // Skip the previously written byte.
        Assert.Equal(buf, buf2.SkipBytes(1));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8CompositeWrapped(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string utf8 = "Some UTF-8 like äÄ∏ŒŒ";
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(utf8));
        ByteBuf buf2 = new WrappedCompositeByteBuf(owner.Composite().AddComponent(
                owner.Buffer(8)).AddComponent(owner.Buffer(24)));
        // write some byte so we start writing with an offset.
        buf2.WriteByte(1);
        ByteBufUtil.WriteUtf8(buf2, utf8);

        // Skip the previously written byte.
        Assert.Equal(buf, buf2.SkipBytes(1));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8Surrogates(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        // leading surrogate + trailing surrogate
        string surrogateString = new StringBuilder(2)
                                .Append('a')
                                .Append('\uD800')
                                .Append('\uDC00')
                                .Append('b')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidOnlyTrailingSurrogate(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('a')
                                .Append('\uDC00')
                                .Append('b')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidOnlyLeadingSurrogate(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('a')
                                .Append('\uD800')
                                .Append('b')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidSurrogatesSwitched(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('a')
                                .Append('\uDC00')
                                .Append('\uD800')
                                .Append('b')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidTwoLeadingSurrogates(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('a')
                                .Append('\uD800')
                                .Append('\uD800')
                                .Append('b')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));
        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidTwoTrailingSurrogates(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('a')
                                .Append('\uDC00')
                                .Append('\uDC00')
                                .Append('b')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidEndOnLeadingSurrogate(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('\uD800')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8InvalidEndOnTrailingSurrogate(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string surrogateString = new StringBuilder(2)
                                .Append('\uDC00')
                                .ToString();
        ByteBuf buf = owner.Buffer(16);
        buf.WriteBytes(NettyUtf8.GetBytes(surrogateString));
        ByteBuf buf2 = owner.Buffer(16);
        ByteBufUtil.WriteUtf8(buf2, surrogateString);

        Assert.Equal(buf, buf2);
        Assert.Equal(buf.ReadableBytes, ByteBufUtil.Utf8Bytes(surrogateString));

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUsAsciiString(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        AsciiString usAscii = new AsciiString("NettyRocks");
        int expectedCapacity = usAscii.Length();
        ByteBuf buf = owner.Buffer(expectedCapacity);
        buf.WriteBytes(Encoding.ASCII.GetBytes(usAscii.ToString()));
        ByteBuf buf2 = owner.Buffer(expectedCapacity);
        ByteBufUtil.WriteAscii(buf2, usAscii);

        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

    }

    [Theory]
    [MemberData(nameof(NoUnsafe))]
    public void TestWriteUtf8Wrapped(BufferType bufferType)
    {
        using var owner = new Provider(bufferType);
        string usAscii = "Some UTF-8 like äÄ∏ŒŒ";
        ByteBuf buf = Unpooled.UnreleasableBuffer(owner.Buffer(16));
        AssertWrapped(buf);
        buf.WriteBytes(NettyUtf8.GetBytes(usAscii));
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(owner.Buffer(16));
        AssertWrapped(buf2);
        ByteBufUtil.WriteUtf8(buf2, usAscii);

        Assert.Equal(buf, buf2);

        buf.Unwrap().Release();
        buf2.Unwrap().Release();

    }

    private static void decodeRandomHexBytes(int len)
    {
        byte[] b = new byte[len];
        Random.Shared.NextBytes(b);
        string hexDump = ByteBufUtil.HexDump(b);
        for (int i = 0; i <= len; i++)
        {  // going over sub-strings of various lengths including empty byte[].
            byte[] b2 = b[i..];
            byte[] decodedBytes = ByteBufUtil.DecodeHexDump(hexDump, i * 2, (len - i) * 2);
            Assert.Equal(b2, decodedBytes);
        }
    }

    private static int naiveIndexOf(byte[] needle, byte[] haystack)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
            {
                j++;
            }
            if (j == needle.Length)
            {
                return i;
            }
        }
        return -1;
    }

    private static void AssertWrapped(ByteBuf buf)
    {
        Assert.IsAssignableFrom<WrappedByteBuf>(buf);
    }

}
