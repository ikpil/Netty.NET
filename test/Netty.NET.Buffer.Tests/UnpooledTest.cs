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
using System.Net.Sockets;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// CLR mapping used throughout: Memory<byte>/ReadOnlyMemory<byte> replace ByteBuffer (allocateDirect = native
// NativeMemoryAllocator storage), Unpooled.EmptyBuffer is EMPTY_BUFFER, Array.Empty<T>() is EmptyArrays,
// ByteBuf.Equals is the source's content equality, and AsReadOnly replaces the deprecated
// unmodifiableBuffer(ByteBuf) ("@deprecated Use ByteBuf#asReadOnly()").

/**
 * Tests channel buffers
 */
public class UnpooledTest
{
    private static readonly ByteBuf[] EMPTY_BYTE_BUFS = new ByteBuf[0];
    private static readonly byte[][] EMPTY_BYTES_2D = new byte[0][];

    [Fact]
    public void TestCompositeWrappedBuffer()
    {
        ByteBuf header = Unpooled.Buffer(12);
        ByteBuf payload = Unpooled.Buffer(512);

        header.WriteBytes(new byte[12]);
        payload.WriteBytes(new byte[512]);

        ByteBuf buffer = Unpooled.WrappedBuffer(header, payload);

        Assert.Equal(12, header.ReadableBytes);
        Assert.Equal(512, payload.ReadableBytes);

        Assert.Equal(12 + 512, buffer.ReadableBytes);
        // CLR: the nonempty segments of the readable sequence replace nioBufferCount().
        int segments = 0;

        foreach (var segment in buffer.ReadableSequence)
        {
            Assert.False(segment.IsEmpty);
            ++segments;
        }

        Assert.Equal(2, segments);
        Assert.Equal(2, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);

        buffer.Release();
    }

    [Fact]
    public void TestHashCode()
    {
        var map = new List<KeyValuePair<byte[], int>>
        {
            new(Array.Empty<byte>(), 1),
            new(new byte[] { 1 }, 32),
            new(new byte[] { 2 }, 33),
            new(new byte[] { 0, 1 }, 962),
            new(new byte[] { 1, 2 }, 994),
            new(new byte[] { 0, 1, 2, 3, 4, 5 }, 63504931),
            new(new byte[] { 6, 7, 8, 9, 0, 1 }, unchecked((int)97180294697L)),
            new(new byte[] { unchecked((byte)-1), unchecked((byte)-1), unchecked((byte)-1), 0xE1 }, 1),
        };

        foreach (var e in map)
        {
            ByteBuf buffer = Unpooled.WrappedBuffer(e.Key);
            Assert.Equal(
                    e.Value,
                    ByteBufUtil.HashCode(buffer));
            buffer.Release();
        }
    }

    [Fact]
    public void TestEquals()
    {
        ByteBuf a, b;

        // Different length.
        a = Unpooled.WrappedBuffer(new byte[] { 1 });
        b = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        Assert.False(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Same content, same firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        Assert.True(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Same content, different firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4 }, 1, 3);
        Assert.True(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Different content, same firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(new byte[] { 1, 2, 4 });
        Assert.False(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Different content, different firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 4, 5 }, 1, 3);
        Assert.False(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Same content, same firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        Assert.True(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Same content, different firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 1, 10);
        Assert.True(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Different content, same firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 6, 7, 8, 5, 9, 10 });
        Assert.False(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();

        // Different content, different firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }, 1, 10);
        Assert.False(ByteBufUtil.Equals(a, b));
        a.Release();
        b.Release();
    }

    [Fact]
    public void TestCompare()
    {
        List<ByteBuf> expected = new List<ByteBuf>();
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3, 4 }, 1, 1));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 }, 2, 2));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }, 1, 10));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }, 2, 12));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3, 4, 5 }, 2, 1));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5 }, 3, 2));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }, 2, 10));
        expected.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, 3, 12));

        for (int i = 0; i < expected.Count; i ++)
        {
            for (int j = 0; j < expected.Count; j ++)
            {
                if (i == j)
                {
                    Assert.Equal(0, ByteBufUtil.Compare(expected[i], expected[j]));
                }
                else if (i < j)
                {
                    Assert.True(ByteBufUtil.Compare(expected[i], expected[j]) < 0);
                }
                else
                {
                    Assert.True(ByteBufUtil.Compare(expected[i], expected[j]) > 0);
                }
            }
        }
        foreach (ByteBuf buffer in expected)
        {
            buffer.Release();
        }
    }

    [Fact]
    public void ShouldReturnEmptyBufferWhenLengthIsZero()
    {
        // CLR: the source leaks the buffer(0) inputs of the copiedBuffer cases (copying takes no ownership); the
        // CLR test releases them at the end.
        var sources = new List<ByteBuf>();

        ByteBuf Buffer0()
        {
            ByteBuf b = Unpooled.Buffer(0);
            sources.Add(b);
            return b;
        }

        try
        {
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Array.Empty<byte>()));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[8], 0, 0));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[8], 8, 0));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Memory<byte>.Empty));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Unpooled.EmptyBuffer));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(EMPTY_BYTES_2D));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[][] { Array.Empty<byte>() }));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Array.Empty<Memory<byte>>()));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new Memory<byte>[] { Memory<byte>.Empty }));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Memory<byte>.Empty, Memory<byte>.Empty));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(EMPTY_BYTE_BUFS));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new ByteBuf[] { Buffer0() }));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Buffer0(), Buffer0()));

            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<byte>()));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new byte[8], 0, 0));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new byte[8], 8, 0));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(ReadOnlyMemory<byte>.Empty));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Unpooled.EmptyBuffer));
            Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(EMPTY_BYTES_2D));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new byte[][] { Array.Empty<byte>() }));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<ReadOnlyMemory<byte>>()));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new ReadOnlyMemory<byte>[] { ReadOnlyMemory<byte>.Empty }));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(EMPTY_BYTE_BUFS));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new ByteBuf[] { Buffer0() }));
            AssertSameAndRelease(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Buffer0(), Buffer0()));
        }
        finally
        {
            foreach (ByteBuf source in sources)
            if (source.RefCnt > 0)
            {
                source.Release();
            }
        }
    }

    [Fact]
    public void TestCompare2()
    {
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        ByteBuf actual = Unpooled.WrappedBuffer(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        Assert.True(ByteBufUtil.Compare(expected, actual) > 0);
        expected.Release();
        actual.Release();

        expected = Unpooled.WrappedBuffer(new byte[] { 0xFF });
        actual = Unpooled.WrappedBuffer(new byte[] { 0x00 });
        Assert.True(ByteBufUtil.Compare(expected, actual) > 0);
        expected.Release();
        actual.Release();
    }

    [Fact]
    public void ShouldAllowEmptyBufferToCreateCompositeBuffer()
    {
        // CLR: explicit BE/LE operations replace the source's .order(LITTLE_ENDIAN) facade on the middle buffer.
        ByteBuf buf = Unpooled.WrappedBuffer(
                Unpooled.EmptyBuffer,
                Unpooled.WrappedBuffer(new byte[16]),
                Unpooled.EmptyBuffer);
        try
        {
            Assert.Equal(16, buf.Capacity);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestWrappedBuffer()
    {
        var native = new NativeMemoryAllocator();
        using (var direct = native.Allocate(16, true))
        {
            ByteBuf buffer = Unpooled.WrappedBuffer(direct.Memory);
            Assert.Equal(16, buffer.Capacity);
            buffer.Release();
            // CLR: the wrapper borrows the caller-owned native storage and leaves it to its owner.
            Assert.Equal(16, native.ReservedBytes);
        }
        Assert.Equal(0, native.ReservedBytes);

        AssertEqualsAndRelease(
                Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(new byte[][] { new byte[] { 1, 2, 3 } }));

        AssertEqualsAndRelease(
                Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(new ByteBuf[] { Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }) }));

        AssertEqualsAndRelease(
                Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1 }),
                        Unpooled.WrappedBuffer(new byte[] { 2 }), Unpooled.WrappedBuffer(new byte[] { 3 })));

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(new Memory<byte>[] { new byte[] { 1, 2, 3 } }));

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(new Memory<byte>(new byte[] { 1 }),
                new Memory<byte>(new byte[] { 2 }), new Memory<byte>(new byte[] { 3 })));
    }

    [Fact]
    public void TestWrappedDirectBufferMemoryAddress()
    {
        // CLR: a slice of native storage replaces the positioned direct ByteBuffer, and scoped pins replace
        // memoryAddress(); native storage always has an address, so there is no assumption gate.
        var native = new NativeMemoryAllocator();
        using var nioBuffer = native.Allocate(16);
        Memory<byte> expectedBuffer = nioBuffer.Memory.Slice(4, 8);

        ByteBuf buffer = Unpooled.WrappedBuffer(expectedBuffer);
        try
        {
            Assert.True(buffer.IsDirect);
            Assert.Equal(8, buffer.Capacity);
            Assert.Equal(ManagedMemoryTest.Address(expectedBuffer), ManagedMemoryTest.Address(buffer.AsReadOnlyMemory(0, 8)));
        }
        finally
        {
            buffer.Release();
        }
        Assert.Equal(16, native.ReservedBytes);
        nioBuffer.Dispose();
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void TestSingleWrappedByteBufReleased()
    {
        ByteBuf buf = Unpooled.Buffer(12).WriteByte(0);
        ByteBuf wrapped = Unpooled.WrappedBuffer(buf);
        Assert.True(wrapped.Release());
        Assert.Equal(0, buf.RefCnt);
    }

    [Fact]
    public void TestSingleUnReadableWrappedByteBufReleased()
    {
        ByteBuf buf = Unpooled.Buffer(12);
        ByteBuf wrapped = Unpooled.WrappedBuffer(buf);
        Assert.False(wrapped.Release()); // EMPTY_BUFFER cannot be released
        Assert.Equal(0, buf.RefCnt);
    }

    [Fact]
    public void TestMultiByteBufReleased()
    {
        ByteBuf buf1 = Unpooled.Buffer(12).WriteByte(0);
        ByteBuf buf2 = Unpooled.Buffer(12).WriteByte(0);
        ByteBuf wrapped = Unpooled.WrappedBuffer(16, buf1, buf2);
        Assert.True(wrapped.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf2.RefCnt);
    }

    [Fact]
    public void TestMultiUnReadableByteBufReleased()
    {
        ByteBuf buf1 = Unpooled.Buffer(12);
        ByteBuf buf2 = Unpooled.Buffer(12);
        ByteBuf wrapped = Unpooled.WrappedBuffer(16, buf1, buf2);
        Assert.False(wrapped.Release()); // EMPTY_BUFFER cannot be released
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf2.RefCnt);
    }

    // CLR: Encoding.UTF8, Encoding.ASCII and Encoding.Latin1 are CharsetUtil.UTF_8, US_ASCII and ISO_8859_1.
    [Fact]
    public void TestCopiedBufferUtf8()
    {
        TestCopiedBufferCharSequence("Some UTF_8 like äÄ∏ŒŒ", Encoding.UTF8);
    }

    [Fact]
    public void TestCopiedBufferAscii()
    {
        TestCopiedBufferCharSequence("Some US_ASCII", Encoding.ASCII);
    }

    [Fact]
    public void TestCopiedBufferSomeOtherCharset()
    {
        TestCopiedBufferCharSequence("Some ISO_8859_1", Encoding.Latin1);
    }

    private static void TestCopiedBufferCharSequence(string sequence, Encoding charset)
    {
        ByteBuf copied = Unpooled.CopiedBuffer(sequence, charset);
        try
        {
            Assert.Equal(sequence, copied.ToString(charset));
        }
        finally
        {
            copied.Release();
        }
    }

    [Fact]
    public void TestCopiedBuffer()
    {
        var native = new NativeMemoryAllocator();
        using (var direct = native.Allocate(16, true))
        {
            ByteBuf copied = Unpooled.CopiedBuffer((ReadOnlyMemory<byte>)direct.Memory);
            Assert.Equal(16, copied.Capacity);
            copied.Release();
        }
        Assert.Equal(0, native.ReservedBytes);

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.CopiedBuffer(new byte[][] { new byte[] { 1, 2, 3 } }));

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.CopiedBuffer(new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }));

        // CLR: copying takes no ownership; the source leaks these inputs, the CLR test releases them.
        ByteBuf whole = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.CopiedBuffer(new ByteBuf[] { whole }));
        whole.Release();

        ByteBuf[] parts = { Unpooled.WrappedBuffer(new byte[] { 1 }),
                Unpooled.WrappedBuffer(new byte[] { 2 }), Unpooled.WrappedBuffer(new byte[] { 3 }) };
        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.CopiedBuffer(parts[0], parts[1], parts[2]));

        foreach (ByteBuf part in parts)
        {
            part.Release();
        }

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.CopiedBuffer(new ReadOnlyMemory<byte>[] { new byte[] { 1, 2, 3 } }));

        AssertEqualsAndRelease(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.CopiedBuffer(new ReadOnlyMemory<byte>(new byte[] { 1 }),
                        new ReadOnlyMemory<byte>(new byte[] { 2 }), new ReadOnlyMemory<byte>(new byte[] { 3 })));
    }

    private static void AssertEqualsAndRelease(ByteBuf expected, ByteBuf actual)
    {
        Assert.Equal(expected, actual);
        expected.Release();
        actual.Release();
    }

    private static void AssertSameAndRelease(ByteBuf expected, ByteBuf actual)
    {
        Assert.Equal(expected, actual);
        // CLR: the empty factories return the shared sentinel itself; the identity is checked as well.
        Assert.Same(expected, actual);
        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestHexDump()
    {
        Assert.Equal("", ByteBufUtil.HexDump(Unpooled.EmptyBuffer));

        ByteBuf buffer = Unpooled.WrappedBuffer(new byte[] { 0x12, 0x34, 0x56 });
        Assert.Equal("123456", ByteBufUtil.HexDump(buffer));
        buffer.Release();

        buffer = Unpooled.WrappedBuffer(new byte[] {
                0x12, 0x34, 0x56, 0x78,
                0x90, 0xAB, 0xCD, 0xEF
        });
        Assert.Equal("1234567890abcdef", ByteBufUtil.HexDump(buffer));
        buffer.Release();
    }

    [Fact]
    public void TestSwapMedium()
    {
        Assert.Equal(0x563412, ByteBufUtil.SwapMedium(0x123456));
        Assert.Equal(0x80, ByteBufUtil.SwapMedium(0x800000));
    }

    // CLR: the observed stream replaces Mockito.mock(InputStream.class) and records any read.
    private sealed class ObservedStream : MemoryStream
    {
        public int Reads;
        public override int Read(Span<byte> buffer)
        {
            ++Reads;
            return base.Read(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ++Reads;
            return base.Read(buffer, offset, count);
        }

        public override int ReadByte()
        {
            ++Reads;
            return base.ReadByte();
        }
    }

    [Fact]
    public void TestUnmodifiableBuffer()
    {
        ByteBuf buf = Unpooled.Buffer(16).AsReadOnly();

        Assert.Throws<NotSupportedException>(() => buf.DiscardReadBytes());

        Assert.Throws<NotSupportedException>(() => buf.SetByte(0, 0));

        Assert.Throws<NotSupportedException>(() => buf.SetBytes(0, Unpooled.EmptyBuffer, 0, 0));

        Assert.Throws<NotSupportedException>(() => buf.SetBytes(0, Array.Empty<byte>().AsSpan(0, 0)));

        Assert.Throws<NotSupportedException>(() => buf.SetBytes(0, Memory<byte>.Empty.Span));

        Assert.Throws<NotSupportedException>(() => buf.SetShort(0, 0));

        Assert.Throws<NotSupportedException>(() => buf.SetMedium(0, 0));

        Assert.Throws<NotSupportedException>(() => buf.SetInt(0, 0));

        Assert.Throws<NotSupportedException>(() => buf.SetLong(0, 0));

        using var inputStream = new ObservedStream();
        Assert.Throws<NotSupportedException>(() => buf.SetBytes(0, inputStream, 0));
        Assert.Equal(0, inputStream.Reads);

        // CLR: an unconnected Socket replaces the ScatteringByteChannel mock; the rejection precedes any use of it.
        using var scatteringByteChannel = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<NotSupportedException>(() => buf.SetBytes(0, scatteringByteChannel, 0));
        Assert.False(scatteringByteChannel.Connected);
        Assert.Equal((0, 0, 1), (buf.ReaderIndex, buf.WriterIndex, buf.RefCnt));
        buf.Release();
    }

    [Fact]
    public void TestWrapSingleInt()
    {
        ByteBuf buffer = Unpooled.CopyInt(42);
        Assert.Equal(4, buffer.Capacity);
        Assert.Equal(42, buffer.ReadInt());
        Assert.False(buffer.IsReadable());
        buffer.Release();
    }

    [Fact]
    public void TestWrapInt()
    {
        ByteBuf buffer = Unpooled.CopyInt(1, 4);
        Assert.Equal(8, buffer.Capacity);
        Assert.Equal(1, buffer.ReadInt());
        Assert.Equal(4, buffer.ReadInt());
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyInt(null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyInt(new int[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapSingleShort()
    {
        ByteBuf buffer = Unpooled.CopyShort(42);
        Assert.Equal(2, buffer.Capacity);
        Assert.Equal(42, buffer.ReadShort());
        Assert.False(buffer.IsReadable());
        buffer.Release();
    }

    [Fact]
    public void TestWrapShortFromShortArray()
    {
        ByteBuf buffer = Unpooled.CopyShort(new short[] { 1, 4 });
        Assert.Equal(4, buffer.Capacity);
        Assert.Equal(1, buffer.ReadShort());
        Assert.Equal(4, buffer.ReadShort());
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyShort((short[])null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyShort(new short[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapShortFromIntArray()
    {
        ByteBuf buffer = Unpooled.CopyShort(1, 4);
        Assert.Equal(4, buffer.Capacity);
        Assert.Equal(1, buffer.ReadShort());
        Assert.Equal(4, buffer.ReadShort());
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyShort((int[])null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyShort(new int[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapSingleMedium()
    {
        ByteBuf buffer = Unpooled.CopyMedium(42);
        Assert.Equal(3, buffer.Capacity);
        Assert.Equal(42, buffer.ReadMedium());
        Assert.False(buffer.IsReadable());
        buffer.Release();
    }

    [Fact]
    public void TestWrapMedium()
    {
        ByteBuf buffer = Unpooled.CopyMedium(1, 4);
        Assert.Equal(6, buffer.Capacity);
        Assert.Equal(1, buffer.ReadMedium());
        Assert.Equal(4, buffer.ReadMedium());
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyMedium(null);
        Assert.Equal(0, Unpooled.CopyMedium(null).Capacity);
        buffer.Release();

        buffer = Unpooled.CopyMedium(new int[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapSingleLong()
    {
        ByteBuf buffer = Unpooled.CopyLong(42);
        Assert.Equal(8, buffer.Capacity);
        Assert.Equal(42, buffer.ReadLong());
        Assert.False(buffer.IsReadable());
        buffer.Release();
    }

    [Fact]
    public void TestWrapLong()
    {
        ByteBuf buffer = Unpooled.CopyLong(1, 4);
        Assert.Equal(16, buffer.Capacity);
        Assert.Equal(1, buffer.ReadLong());
        Assert.Equal(4, buffer.ReadLong());
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyLong(null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyLong(new long[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapSingleFloat()
    {
        ByteBuf buffer = Unpooled.CopyFloat(42);
        Assert.Equal(4, buffer.Capacity);
        Assert.Equal(42, buffer.ReadFloat(), 0.01);
        Assert.False(buffer.IsReadable());
        buffer.Release();
    }

    [Fact]
    public void TestWrapFloat()
    {
        ByteBuf buffer = Unpooled.CopyFloat(1, 4);
        Assert.Equal(8, buffer.Capacity);
        Assert.Equal(1, buffer.ReadFloat(), 0.01);
        Assert.Equal(4, buffer.ReadFloat(), 0.01);
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyFloat(null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyFloat(new float[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapSingleDouble()
    {
        ByteBuf buffer = Unpooled.CopyDouble(42);
        Assert.Equal(8, buffer.Capacity);
        Assert.Equal(42, buffer.ReadDouble(), 0.01);
        Assert.False(buffer.IsReadable());
        buffer.Release();
    }

    [Fact]
    public void TestWrapDouble()
    {
        ByteBuf buffer = Unpooled.CopyDouble(1, 4);
        Assert.Equal(16, buffer.Capacity);
        Assert.Equal(1, buffer.ReadDouble(), 0.01);
        Assert.Equal(4, buffer.ReadDouble(), 0.01);
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyDouble(null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyDouble(new double[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestWrapBoolean()
    {
        ByteBuf buffer = Unpooled.CopyBoolean(true, false);
        Assert.Equal(2, buffer.Capacity);
        Assert.True(buffer.ReadBoolean());
        Assert.False(buffer.ReadBoolean());
        Assert.False(buffer.IsReadable());
        buffer.Release();

        buffer = Unpooled.CopyBoolean(null);
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();

        buffer = Unpooled.CopyBoolean(new bool[] { });
        Assert.Equal(0, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void WrappedReadOnlyDirectBuffer()
    {
        // CLR: ReadOnlyMemory over native storage replaces ByteBuffer.allocateDirect(12).asReadOnlyBuffer().
        var native = new NativeMemoryAllocator();
        using var buffer = native.Allocate(12);
        for (int i = 0; i < 12; i++)
        {
            buffer.Memory.Span[i] = (byte)i;
        }
        ByteBuf wrapped = Unpooled.WrappedBuffer((ReadOnlyMemory<byte>)buffer.Memory);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal((byte)i, wrapped.ReadByte());
        }
        wrapped.Release();
        buffer.Dispose();
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void SkipBytesNegativeLength()
    {
        // CLR: ArgumentOutOfRangeException (an ArgumentException) replaces IllegalArgumentException.
        ByteBuf buf = Unpooled.Buffer(8);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buf.SkipBytes(-1));
        }
        finally
        {
            buf.Release();
        }
    }

    // See https://github.com/netty/netty/issues/5597
    [Fact]
    public void TestWrapByteBufArrayStartsWithNonReadable()
    {
        ByteBuf buffer1 = Unpooled.Buffer(8);
        ByteBuf buffer2 = Unpooled.Buffer(8).WriteZero(8); // Ensure the ByteBuf is readable.
        ByteBuf buffer3 = Unpooled.Buffer(8);
        ByteBuf buffer4 = Unpooled.Buffer(8).WriteZero(8); // Ensure the ByteBuf is readable.

        ByteBuf wrapped = Unpooled.WrappedBuffer(buffer1, buffer2, buffer3, buffer4);
        Assert.Equal(16, wrapped.ReadableBytes);
        Assert.True(wrapped.Release());
        Assert.Equal(0, buffer1.RefCnt);
        Assert.Equal(0, buffer2.RefCnt);
        Assert.Equal(0, buffer3.RefCnt);
        Assert.Equal(0, buffer4.RefCnt);
        Assert.Equal(0, wrapped.RefCnt);
    }

    [Fact]
    public void TestGetBytesByteBuffer()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g' };
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        Memory<byte> nioBuffer = new byte[bytes.Length + 1];
        ByteBuf wrappedBuffer = Unpooled.WrappedBuffer(bytes);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => wrappedBuffer.GetBytes(wrappedBuffer.ReaderIndex, nioBuffer.Span));
        }
        finally
        {
            wrappedBuffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesByteBuffer2()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g' };
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        Memory<byte> nioBuffer = new byte[bytes.Length + 1];
        ByteBuf wrappedBuffer = Unpooled.WrappedBuffer(bytes, 0, bytes.Length);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => wrappedBuffer.GetBytes(wrappedBuffer.ReaderIndex, nioBuffer.Span));
        }
        finally
        {
            wrappedBuffer.Release();
        }
    }

    [Fact]
    public void LittleEndianWriteOnLittleEndianBufferMustStoreLittleEndianValue()
    {
        // CLR has no mutable ByteOrder: the default getters of an order(LITTLE_ENDIAN) buffer are its explicit
        // little-endian getters, so both source assertions read through the LE accessors.
        ByteBuf b = Unpooled.Buffer(1024);

        b.WriteShortLE(0x0102);
        Assert.Equal((short)0x0102, b.GetShortLE(0));
        Assert.Equal((short)0x0102, b.GetShortLE(0));
        b.Clear();

        b.WriteMediumLE(0x010203);
        Assert.Equal(0x010203, b.GetMediumLE(0));
        Assert.Equal(0x010203, b.GetMediumLE(0));
        b.Clear();

        b.WriteIntLE(0x01020304);
        Assert.Equal(0x01020304, b.GetIntLE(0));
        Assert.Equal(0x01020304, b.GetIntLE(0));
        b.Clear();

        b.WriteLongLE(0x0102030405060708L);
        Assert.Equal(0x0102030405060708L, b.GetLongLE(0));
        Assert.Equal(0x0102030405060708L, b.GetLongLE(0));
        b.Release();
    }

    [Fact]
    public void LittleEndianWriteOnDefaultBufferMustStoreLittleEndianValue()
    {
        ByteBuf b = Unpooled.Buffer(1024);

        b.WriteShortLE(0x0102);
        Assert.Equal((short)0x0102, b.GetShortLE(0));
        Assert.Equal((short)0x0201, b.GetShort(0));
        b.Clear();

        b.WriteMediumLE(0x010203);
        Assert.Equal(0x010203, b.GetMediumLE(0));
        Assert.Equal(0x030201, b.GetMedium(0));
        b.Clear();

        b.WriteIntLE(0x01020304);
        Assert.Equal(0x01020304, b.GetIntLE(0));
        Assert.Equal(0x04030201, b.GetInt(0));
        b.Clear();

        b.WriteLongLE(0x0102030405060708L);
        Assert.Equal(0x0102030405060708L, b.GetLongLE(0));
        Assert.Equal(0x0807060504030201L, b.GetLong(0));
        b.Release();
    }
}
