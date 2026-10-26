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
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests channel buffers
 */
public class UnpooledSourceTest
{
    public static IEnumerable<object[]> HashCases()
    {
        yield return [Array.Empty<byte>(), 1];
        yield return [new byte[] { 1 }, 32];
        yield return [new byte[] { 2 }, 33];
        yield return [new byte[] { 0, 1 }, 962];
        yield return [new byte[] { 1, 2 }, 994];
        yield return [new byte[] { 0, 1, 2, 3, 4, 5 }, 63504931];
        yield return [new byte[] { 6, 7, 8, 9, 0, 1 }, unchecked((int)97180294697L)];
        yield return [new byte[] { 255, 255, 255, 225 }, 1];
    }
    [Theory]
    [MemberData(nameof(HashCases))]
    public void OriginalHashTable(byte[] bytes, int expected)
    {
        ByteBuf b = Unpooled.WrappedBuffer(bytes);
        try { Assert.Equal(expected, ByteBufUtil.HashCode(b)); }
        finally { b.Release(); }
    }

    public static IEnumerable<object[]> EqualityCases()
    {
        // Different length.
        yield return [new byte[] { 1 }, new byte[] { 1, 2 }, 0, 2, false];
        // Same content, same firstIndex, short length.
        yield return [new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, 0, 3, true];
        // Same content, different firstIndex, short length.
        yield return [new byte[] { 1, 2, 3 }, new byte[] { 0, 1, 2, 3, 4 }, 1, 3, true];
        // Different content, same firstIndex, short length.
        yield return [new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }, 0, 3, false];
        // Different content, different firstIndex, short length.
        yield return [new byte[] { 1, 2, 3 }, new byte[] { 0, 1, 2, 4, 5 }, 1, 3, false];
        // Same content, same firstIndex, long length.
        yield return [new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 0, 10, true];
        // Same content, different firstIndex, long length.
        yield return [new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 1, 10, true];
        // Different content, same firstIndex, long length.
        yield return [new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, new byte[] { 1, 2, 3, 4, 6, 7, 8, 5, 9, 10 }, 0, 10, false];
        // Different content, different firstIndex, long length.
        yield return [new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }, 1, 10, false];
    }
    [Theory]
    [MemberData(nameof(EqualityCases))]
    public void OriginalEqualsTable(byte[] left, byte[] right, int offset, int length, bool expected)
    {
        ByteBuf a = Unpooled.WrappedBuffer(left), b = Unpooled.WrappedBuffer(right, offset, length);
        try { Assert.Equal(expected, ByteBufUtil.Equals(a, b)); }
        finally { a.Release(); b.Release(); }
    }

    private static readonly (byte[] Bytes, int Offset, int Length)[] OrderedInputs =
    [
        ([1], 0, 1),
        ([1, 2], 0, 2),
        ([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 0, 10),
        ([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], 0, 12),
        ([2], 0, 1),
        ([2, 3], 0, 2),
        ([2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 0, 10),
        ([2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13], 0, 12),
        ([2, 3, 4], 1, 1),
        ([1, 2, 3, 4], 2, 2),
        ([2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], 1, 10),
        ([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], 2, 12),
        ([2, 3, 4, 5], 2, 1),
        ([1, 2, 3, 4, 5], 3, 2),
        ([2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], 2, 10),
        ([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15], 3, 12),
    ];
    public static IEnumerable<object[]> CompareCases()
    {
        for (int i = 0; i < OrderedInputs.Length; ++i)
        for (int j = 0; j < OrderedInputs.Length; ++j) yield return [i, j];
    }
    [Theory]
    [MemberData(nameof(CompareCases))]
    public void OriginalCompareTable(int i, int j)
    {
        var x = OrderedInputs[i]; var y = OrderedInputs[j];
        ByteBuf a = Unpooled.WrappedBuffer(x.Bytes, x.Offset, x.Length), b = Unpooled.WrappedBuffer(y.Bytes, y.Offset, y.Length);
        try { Assert.Equal(Math.Sign(i - j), Math.Sign(ByteBufUtil.Compare(a, b))); }
        finally { a.Release(); b.Release(); }
    }
    [Theory]
    [InlineData(1)] [InlineData(4)]
    public void OriginalCompareUnsignedBytes(int length)
    {
        ByteBuf a = Unpooled.WrappedBuffer(Enumerable.Repeat((byte)255, length).ToArray());
        ByteBuf b = Unpooled.WrappedBuffer(new byte[length]);
        try { Assert.True(ByteBufUtil.Compare(a, b) > 0); }
        finally { a.Release(); b.Release(); }
    }

    public static IEnumerable<object[]> EmptyCases() => Enumerable.Range(0, 26).Select(i => new object[] { i });
    [Theory]
    [MemberData(nameof(EmptyCases))]
    public void OriginalEmptyFactories(int operation)
    {
        var sources = new List<ByteBuf>();
        ByteBuf Source() { ByteBuf b = Unpooled.Buffer(0); sources.Add(b); return b; }
        ByteBuf result = operation switch
        {
            0 => Unpooled.WrappedBuffer(Array.Empty<byte>()),
            1 => Unpooled.WrappedBuffer(new byte[8], 0, 0), 2 => Unpooled.WrappedBuffer(new byte[8], 8, 0),
            3 => Unpooled.WrappedBuffer(Memory<byte>.Empty), 4 => Unpooled.WrappedBuffer(Unpooled.EmptyBuffer),
            5 => Unpooled.WrappedBuffer(Array.Empty<byte[]>()), 6 => Unpooled.WrappedBuffer(new[] { Array.Empty<byte>() }),
            7 => Unpooled.WrappedBuffer(Array.Empty<Memory<byte>>()), 8 => Unpooled.WrappedBuffer(new[] { Memory<byte>.Empty }),
            9 => Unpooled.WrappedBuffer(Memory<byte>.Empty, Memory<byte>.Empty), 10 => Unpooled.WrappedBuffer(Array.Empty<ByteBuf>()),
            11 => Unpooled.WrappedBuffer(new[] { Source() }), 12 => Unpooled.WrappedBuffer(Source(), Source()),
            13 => Unpooled.CopiedBuffer(Array.Empty<byte>()),
            14 => Unpooled.CopiedBuffer(new byte[8], 0, 0), 15 => Unpooled.CopiedBuffer(new byte[8], 8, 0),
            16 => Unpooled.CopiedBuffer(ReadOnlyMemory<byte>.Empty), 17 => Unpooled.CopiedBuffer(Unpooled.EmptyBuffer),
            18 => Unpooled.CopiedBuffer(Array.Empty<byte[]>()), 19 => Unpooled.CopiedBuffer(new[] { Array.Empty<byte>() }),
            20 => Unpooled.CopiedBuffer(Array.Empty<ReadOnlyMemory<byte>>()), 21 => Unpooled.CopiedBuffer(new[] { ReadOnlyMemory<byte>.Empty }),
            22 => Unpooled.CopiedBuffer(ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty), 23 => Unpooled.CopiedBuffer(Array.Empty<ByteBuf>()),
            24 => Unpooled.CopiedBuffer(new[] { Source() }), _ => Unpooled.CopiedBuffer(Source(), Source())
        };
        try { Assert.Same(Unpooled.EmptyBuffer, result); Assert.False(result.Release()); }
        finally { foreach (ByteBuf source in sources) if (source.ReferenceCount > 0) source.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void OriginalInputOwnershipIsTransferred(bool readable, bool multiple)
    {
        ByteBuf a = Unpooled.Buffer(12), b = multiple ? Unpooled.Buffer(12) : null;
        if (readable) { a.WriteByte(0); b?.WriteByte(0); }
        ByteBuf result = multiple ? Unpooled.WrappedBuffer(16, a, b) : Unpooled.WrappedBuffer(a);
        try
        {
            // EMPTY_BUFFER cannot be released
            Assert.Equal(readable, result.Release());
            Assert.Equal(0, a.ReferenceCount); if (multiple) Assert.Equal(0, b.ReferenceCount);
        }
        finally { if (a.ReferenceCount > 0) a.Release(); if (b?.ReferenceCount > 0) b.Release(); }
    }
    [Fact]
    public void OriginalHeaderAndPayload()
    {
        ByteBuf header = Unpooled.Buffer(12).WriteBytes(new byte[12]);
        ByteBuf payload = Unpooled.Buffer(512).WriteBytes(new byte[512]);
        ByteBuf result = Unpooled.WrappedBuffer(header, payload);
        try
        {
            Assert.Equal(12, header.ReadableBytes); Assert.Equal(512, payload.ReadableBytes);
            Assert.Equal(524, result.ReadableBytes); Assert.Equal(2, Assert.IsType<CompositeByteBuf>(result).NumComponents);
            int segments = 0; foreach (var segment in result.ReadableSequence) { Assert.False(segment.IsEmpty); ++segments; }
            Assert.Equal(2, segments);
        }
        finally { result.Release(); }
    }
    [Fact]
    public void OriginalEmptyComponentsAroundContent()
    {
        // CLR explicit BE/LE operations replace the source's little-endian facade.
        ByteBuf result = Unpooled.WrappedBuffer(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[16]), Unpooled.EmptyBuffer);
        try { Assert.Equal(16, result.Capacity); } finally { result.Release(); }
    }

    public static IEnumerable<object[]> FactoryCases()
    {
        foreach (bool copy in new[] { false, true })
        for (int shape = 0; shape < 7; ++shape) yield return [copy, shape];
    }
    [Theory]
    [MemberData(nameof(FactoryCases))]
    public void OriginalWrappedAndCopiedFactories(bool copy, int shape)
    {
        var native = new NativeMemoryAllocator(); using var storage = native.Allocate(16, true);
        var inputs = new List<ByteBuf>();
        ByteBuf Input(byte[] bytes) { ByteBuf b = Unpooled.WrappedBuffer(bytes); inputs.Add(b); return b; }
        ByteBuf result = shape switch
        {
            0 => copy ? Unpooled.CopiedBuffer((ReadOnlyMemory<byte>)storage.Memory) : Unpooled.WrappedBuffer(storage.Memory),
            1 => copy ? Unpooled.CopiedBuffer(new byte[][] { [1, 2, 3] }) : Unpooled.WrappedBuffer(new byte[][] { [1, 2, 3] }),
            2 => copy ? Unpooled.CopiedBuffer(new byte[][] { [1], [2], [3] }) : Unpooled.WrappedBuffer(new byte[][] { [1], [2], [3] }),
            3 => copy ? Unpooled.CopiedBuffer(new[] { Input([1, 2, 3]) }) : Unpooled.WrappedBuffer(new[] { Input([1, 2, 3]) }),
            4 => copy ? Unpooled.CopiedBuffer(Input([1]), Input([2]), Input([3])) : Unpooled.WrappedBuffer(Input([1]), Input([2]), Input([3])),
            5 => copy ? Unpooled.CopiedBuffer(new ReadOnlyMemory<byte>[] { new byte[] { 1, 2, 3 } }) : Unpooled.WrappedBuffer(new Memory<byte>[] { new byte[] { 1, 2, 3 } }),
            _ => copy ? Unpooled.CopiedBuffer(new ReadOnlyMemory<byte>[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } })
                : Unpooled.WrappedBuffer(new Memory<byte>[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } })
        };
        try
        {
            if (shape == 0) Assert.Equal(16, result.Capacity);
            else Assert.Equal(new byte[] { 1, 2, 3 }, ByteBufUtil.GetBytes(result));
        }
        finally
        {
            result.Release(); foreach (ByteBuf input in inputs) if (input.ReferenceCount > 0) input.Release();
        }
        Assert.Equal(16, native.ReservedBytes); // Only the caller-owned borrowed native owner remains.
        storage.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public unsafe void OriginalNativeSliceAddressUsesScopedPins()
    {
        var native = new NativeMemoryAllocator(); using var owner = native.Allocate(16);
        var selected = owner.Memory.Slice(4, 8); ByteBuf b = Unpooled.WrappedBuffer(selected);
        try
        {
            Assert.True(b.IsDirect); Assert.Equal(8, b.Capacity);
            using var expected = selected.Pin(); using var actual = b.AsReadOnlyMemory(0, 8).Pin();
            Assert.True(expected.Pointer == actual.Pointer);
        }
        finally { b.Release(); }
        Assert.Equal(16, native.ReservedBytes); owner.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }
    [Fact]
    public void OriginalReadOnlyNativeWrapping()
    {
        var native = new NativeMemoryAllocator(); using var owner = native.Allocate(12);
        for (int i = 0; i < 12; ++i) owner.Memory.Span[i] = (byte)i;
        ByteBuf b = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)owner.Memory);
        try { for (int i = 0; i < 12; ++i) Assert.Equal(i, b.ReadByte()); }
        finally { b.Release(); }
        owner.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }
    private sealed class ObservedStream : MemoryStream
    {
        public int Reads;
        public override int Read(Span<byte> buffer) { ++Reads; return base.Read(buffer); }
    }
    public static IEnumerable<object[]> ReadOnlyCases() => Enumerable.Range(0, 11).Select(i => new object[] { i });
    [Theory]
    [MemberData(nameof(ReadOnlyCases))]
    public void OriginalUnmodifiableOperations(int operation)
    {
        // AsReadOnly is the CLR entry point for the retired singular Java factory.
        ByteBuf b = Unpooled.Buffer(16).AsReadOnly(); using var input = new ObservedStream();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                switch (operation)
                {
                    case 0: b.DiscardReadBytes(); break; case 1: b.SetByte(0, 0); break;
                    case 2: b.SetBytes(0, Unpooled.EmptyBuffer, 0, 0); break;
                    case 3: b.SetBytes(0, Array.Empty<byte>().AsSpan()); break;
                    case 4: b.SetBytes(0, Memory<byte>.Empty.Span); break;
                    case 5: b.SetShort(0, 0); break; case 6: b.SetMedium(0, 0); break;
                    case 7: b.SetInt(0, 0); break; case 8: b.SetLong(0, 0); break;
                    case 9: b.SetBytes(0, input, 0); break; default: b.SetBytes(0, socket, 0); break;
                }
            });
            Assert.Equal(0, input.Reads); Assert.False(socket.Connected); Assert.Equal((0, 0, 1), (b.ReaderIndex, b.WriterIndex, b.ReferenceCount));
        }
        finally { b.Release(); }
    }
    [Fact]
    public void OriginalNegativeSkip()
    {
        ByteBuf b = Unpooled.Buffer(8);
        try { Assert.Throws<ArgumentOutOfRangeException>(() => b.SkipBytes(-1)); } finally { b.Release(); }
    }
    [Fact]
    public void OriginalLeadingUnreadableRegression()
    {
        // See https://github.com/netty/netty/issues/5597
        ByteBuf a = Unpooled.Buffer(8);
        ByteBuf b = Unpooled.Buffer(8).WriteZero(8); // Ensure the ByteBuf is readable.
        ByteBuf c = Unpooled.Buffer(8);
        ByteBuf d = Unpooled.Buffer(8).WriteZero(8); // Ensure the ByteBuf is readable.
        ByteBuf result = Unpooled.WrappedBuffer(a, b, c, d);
        try { Assert.Equal(16, result.ReadableBytes); }
        finally { Assert.True(result.Release()); }
        Assert.All(new[] { a, b, c, d, result }, x => Assert.Equal(0, x.ReferenceCount));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalOversizedMemoryDestination(bool range)
    {
        byte[] bytes = "abcdefg"u8.ToArray();
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        Memory<byte> output = new byte[bytes.Length + 1];
        ByteBuf b = range ? Unpooled.WrappedBuffer(bytes, 0, bytes.Length) : Unpooled.WrappedBuffer(bytes);
        try { Assert.Throws<ArgumentOutOfRangeException>(() => b.GetBytes(b.ReaderIndex, output.Span)); }
        finally { b.Release(); }
    }
    [Theory]
    [InlineData("", "")] [InlineData("123456", "123456")] [InlineData("1234567890ABCDEF", "1234567890abcdef")]
    public void OriginalHexDump(string hex, string expected)
    {
        ByteBuf b = Unpooled.WrappedBuffer(Convert.FromHexString(hex));
        try { Assert.Equal(expected, ByteBufUtil.HexDump(b)); } finally { b.Release(); }
    }
    [Theory]
    [InlineData(0x123456, 0x563412)] [InlineData(0x800000, 0x80)]
    public void OriginalSwapMedium(int value, int expected) => Assert.Equal(expected, ByteBufUtil.SwapMedium(value));

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(8)]
    public void OriginalExplicitLittleEndianWrites(int width)
    {
        // Mutable order and its default getter are replaced by explicit BE/LE pairs.
        ByteBuf b = Unpooled.Buffer(1024);
        try
        {
            long value = width switch { 2 => 0x0102, 3 => 0x010203, 4 => 0x01020304, _ => 0x0102030405060708 };
            long reversed = width switch { 2 => 0x0201, 3 => 0x030201, 4 => 0x04030201, _ => 0x0807060504030201 };
            switch (width)
            {
                case 2: b.WriteShortLE((int)value); Assert.Equal(value, b.GetShortLE(0)); Assert.Equal(reversed, b.GetShort(0)); break;
                case 3: b.WriteMediumLE((int)value); Assert.Equal(value, b.GetMediumLE(0)); Assert.Equal(reversed, b.GetMedium(0)); break;
                case 4: b.WriteIntLE((int)value); Assert.Equal(value, b.GetIntLE(0)); Assert.Equal(reversed, b.GetInt(0)); break;
                default: b.WriteLongLE(value); Assert.Equal(value, b.GetLongLE(0)); Assert.Equal(reversed, b.GetLong(0)); break;
            }
        }
        finally { b.Release(); }
    }
}
