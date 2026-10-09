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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufAllocatedUtilTest
{
    private static (ByteBuf Buffer, ByteBuf Owner) Source(int kind)
    {
        byte[] data = Enumerable.Range(0, 12).Select(i => (byte)(i + 1)).ToArray();
        ByteBuf owner = kind switch
        {
            1 => Unpooled.DirectBuffer(12, 12).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..4]), true).AddComponent(Unpooled.WrappedBuffer(data[4..]), true),
            3 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(data[..4]), Unpooled.WrappedBuffer(data[4..])),
            _ => Unpooled.WrappedBuffer(data)
        };
        ByteBuf b = kind switch { 4 => owner.Slice(1, 10), 5 => Unpooled.UnreleasableBuffer(owner), _ => owner };
        b.SetIndex(2, 9).MarkReaderIndex().MarkWriterIndex();
        return (b, owner);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)]
    [InlineData(3, 0)] [InlineData(3, 1)] [InlineData(3, 2)]
    [InlineData(4, 0)] [InlineData(4, 1)] [InlineData(4, 2)]
    [InlineData(5, 0)] [InlineData(5, 1)] [InlineData(5, 2)]
    public void ReadFactoryConsumesExactlyRequestedBytesAndPreservesMarks(int sourceKind, int destinationKind)
    {
        var (source, owner) = Source(sourceKind); var a = new ProbeAllocator(destinationKind);
        byte[] expected = ByteBufUtil.GetBytes(source, 2, 5);
        try
        {
            ByteBuf result = ByteBufUtil.ReadBytes(a, source, 5);
            try
            {
                Assert.Same(a, result.Allocator); Assert.Equal(5, a.RequestedCapacity);
                Assert.Equal(expected, ByteBufUtil.GetBytes(result)); Assert.Equal(5, result.Capacity);
                Assert.Equal(7, source.ReaderIndex); Assert.Equal(9, source.WriterIndex);
                Assert.Equal(0, result.ReaderIndex); Assert.Equal(5, result.WriterIndex); Assert.Equal(1, result.ReferenceCount);
                result.SetByte(0, 99); Assert.Equal(expected[0], source.GetByte(2));
                source.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(2, source.ReaderIndex); Assert.Equal(9, source.WriterIndex);
                Assert.Equal(1, owner.ReferenceCount);
            }
            finally { result.Release(); }
            Assert.Equal(0, a.Native.ReservedBytes);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ReadFactoryReleasesAfterBoundsAndLifetimeFailuresEvenForEmptyReads(int kind)
    {
        var a = new ProbeAllocator(kind); ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReadBytes(a, source, 4));
            Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(0, a.Native.ReservedBytes); Assert.Equal(0, source.ReaderIndex);
            ByteBuf empty = ByteBufUtil.ReadBytes(a, source, 0);
            try { Assert.Equal(0, empty.Capacity); Assert.Equal(int.MaxValue, empty.MaxCapacity); Assert.Equal(1, empty.ReferenceCount); Assert.Same(a, empty.Allocator); }
            finally { empty.Release(); }
            source.Release();
            Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.ReadBytes(a, source, 0));
            Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(0, a.Native.ReservedBytes);
        }
        finally { if (source.ReferenceCount > 0) source.Release(); }
    }

    [Fact]
    public void ReadFailureReleasesReadOnlyDestinationWithoutAdvancingSource()
    {
        var a = new ProbeAllocator(1) { ReadOnly = true }; ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        try
        {
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.ReadBytes(a, source, 3));
            Assert.Equal(0, source.ReaderIndex); Assert.Equal(3, source.WriterIndex);
            Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(0, a.Native.ReservedBytes);
        }
        finally { source.Release(); }
    }

    [Fact]
    public void ExtraAllocatorCapacityDoesNotCauseOverread()
    {
        var a = new ProbeAllocator(0) { ExtraCapacity = 4 }; ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        try
        {
            ByteBuf result = ByteBufUtil.ReadBytes(a, source, 3);
            try { Assert.Equal(7, result.Capacity); Assert.Equal(3, result.WriterIndex); Assert.Equal(3, source.ReaderIndex); Assert.Equal(new byte[] { 1, 2, 3 }, ByteBufUtil.GetBytes(result)); }
            finally { result.Release(); }
        }
        finally { source.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Utf8AndAsciiFactoriesUseOriginalWireAndReservationRules(int kind)
    {
        var a = new ProbeAllocator(kind);
        foreach (var (text, utf8, ascii) in new[]
        {
            ("", Array.Empty<byte>(), Array.Empty<byte>()),
            ("NettyRocks", Encoding.ASCII.GetBytes("NettyRocks"), Encoding.ASCII.GetBytes("NettyRocks")),
            ("A\u00e9\u20ac\ud83d\ude00", new byte[] { 65, 195, 169, 226, 130, 172, 240, 159, 152, 128 }, new byte[] { 65, 233, 63, 63, 63 }),
            ("\ud800a", new byte[] { 63, 97 }, new byte[] { 63, 97 }),
            ("\udc00\ud800", new byte[] { 63, 63 }, new byte[] { 63, 63 })
        })
        {
            ByteBuf u = ByteBufUtil.WriteUtf8(a, text);
            try { Assert.Equal(text.Length * 3, a.RequestedCapacity); Assert.Equal(text.Length * 3, u.Capacity); Assert.Equal(utf8, ByteBufUtil.GetBytes(u)); Assert.Same(a, u.Allocator); }
            finally { u.Release(); }
            ByteBuf s = ByteBufUtil.WriteAscii(a, text);
            try { Assert.Equal(text.Length, a.RequestedCapacity); Assert.Equal(text.Length, s.Capacity); Assert.Equal(ascii, ByteBufUtil.GetBytes(s)); }
            finally { s.Release(); }
        }
        byte[] padded = new byte[] { 7 }.Concat(Enumerable.Range(0, 256).Select(i => (byte)i)).Append((byte)9).ToArray();
        AsciiString raw = new(padded, 1, 256, false);
        ByteBuf asciiBytes = ByteBufUtil.WriteAscii(a, raw);
        try { Assert.Equal(256, a.RequestedCapacity); Assert.Equal(256, asciiBytes.Capacity); Assert.Equal(padded[1..257], ByteBufUtil.GetBytes(asciiBytes)); }
        finally { asciiBytes.Release(); }
        int callsBeforeGrowth = a.Calls;
        ByteBuf utf8Bytes = ByteBufUtil.WriteUtf8(a, raw);
        try { Assert.Equal(256, a.RequestedCapacities[callsBeforeGrowth]); Assert.Equal(1024, utf8Bytes.Capacity); Assert.Equal(padded[1..257], ByteBufUtil.GetBytes(utf8Bytes)); }
        finally { utf8Bytes.Release(); }
        Assert.Equal(0, a.Native.ReservedBytes);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(0, 3)] [InlineData(0, 4)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(1, 3)] [InlineData(1, 4)]
    public void FailedWritersReleaseTheirNewOwner(int kind, int operation)
    {
        var a = new ProbeAllocator(kind) { ReadOnly = true };
        Assert.Throws<NotSupportedException>(() => Invoke(operation, a));
        Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(0, a.Native.ReservedBytes);
        Assert.Equal(0, a.Last.WriterIndex);
    }

    private static ByteBuf Invoke(int operation, IByteBufAllocator a) => operation switch
    {
        0 => ByteBufUtil.WriteUtf8(a, "A"), 1 => ByteBufUtil.WriteUtf8(a, new AsciiString("A")),
        2 => ByteBufUtil.WriteAscii(a, "A"), 3 => ByteBufUtil.WriteAscii(a, new AsciiString("A")),
        _ => ByteBufUtil.EncodeString(a, "A", Encoding.UTF8)
    };

    [Fact]
    public void AsciiStringUtf8GrowthFailureReleasesTheInitialNativeAllocation()
    {
        var a = new ProbeAllocator(1, 2);
        Assert.Throws<OutOfMemoryException>(() => ByteBufUtil.WriteUtf8(a, new AsciiString("A")));
        Assert.Equal(1, a.RequestedCapacity); Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(0, a.Native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void EncodeStringHonorsEncodingFallbackAndReservesUnwrittenSuffix(int kind)
    {
        var a = new ProbeAllocator(kind);
        Encoding[] encodings = { Encoding.UTF8, Encoding.ASCII, Encoding.Latin1, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 };
        foreach (Encoding encoding in encodings)
        foreach (string text in new[] { "", "NettyRocks", "A\u00e9\ud83d\ude00", "\ud800" })
        {
            byte[] expected = encoding.GetBytes(text); ByteBuf b = ByteBufUtil.EncodeString(a, text, encoding, 3);
            try
            {
                Assert.Equal(expected.Length + 3, a.RequestedCapacity); Assert.Equal(expected.Length + 3, b.Capacity);
                Assert.Equal(expected, ByteBufUtil.GetBytes(b)); Assert.Equal(3, b.WritableBytes);
                Assert.Equal(0, b.ReaderIndex); Assert.Equal(expected.Length, b.WriterIndex); Assert.Same(a, b.Allocator);
                b.WriteBytes(new byte[] { 13, 10, 0 }); Assert.Equal(expected.Concat(new byte[] { 13, 10, 0 }), ByteBufUtil.GetBytes(b));
            }
            finally { b.Release(); }
        }
        Assert.Equal(0, a.Native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void StringAndLineEncoderConsumersUseSpanRangeAndTailReservation(int kind)
    {
        // Pinned codec-base StringEncoder uses encodeString; LineEncoder reserves the
        // encoded separator length with extraCapacity and appends it after the text.
        var a = new ProbeAllocator(kind); string text = "!a\u00e9\ud83d\ude00!";
        ByteBuf b = ByteBufUtil.EncodeString(a, text.AsSpan(1, text.Length - 2), new UTF8Encoding(true), 2);
        try
        {
            Assert.Equal(9, b.Capacity); Assert.Equal(7, b.WriterIndex);
            b.WriteBytes(new byte[] { 13, 10 });
            Assert.Equal(new byte[] { 97, 195, 169, 240, 159, 152, 128, 13, 10 }, ByteBufUtil.GetBytes(b));
        }
        finally { b.Release(); }
        ByteBuf empty = ByteBufUtil.EncodeString(a, ReadOnlySpan<char>.Empty, Encoding.UTF8);
        try { Assert.Equal(0, empty.Capacity); Assert.Equal(int.MaxValue, empty.MaxCapacity); Assert.Equal(1, empty.ReferenceCount); }
        finally { empty.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void EncoderFailureAfterAllocationReleasesEvenPartiallyWrittenStorage(int kind)
    {
        var a = new ProbeAllocator(kind);
        Assert.Throws<InvalidOperationException>(() => ByteBufUtil.EncodeString(a, "A", new FailingEncoding(), 2));
        Assert.Equal(0, a.Last.ReferenceCount); Assert.Equal(0, a.Last.WriterIndex); Assert.Equal(0, a.Native.ReservedBytes);
    }

    [Fact]
    public void ArgumentSizingAndAllocationFailuresPublishNoOwnerOrSourceAdvance()
    {
        var a = new ProbeAllocator(0); AsciiString raw = new("A");
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReadBytes(null, null, 0));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReadBytes(a, null, 0));
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1 });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReadBytes(a, source, -1));
            Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteUtf8((IByteBufAllocator)null, "A"));
            Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteAscii((IByteBufAllocator)null, "A"));
            Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteUtf8(a, (AsciiString)null));
            Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteAscii(a, (AsciiString)null));
            Assert.Throws<ArgumentNullException>(() => ByteBufUtil.EncodeString(null, "A", Encoding.UTF8));
            Assert.Throws<ArgumentNullException>(() => ByteBufUtil.EncodeString(a, "A", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.EncodeString(a, "A", Encoding.UTF8, -1));
            Assert.Throws<OverflowException>(() => ByteBufUtil.EncodeString(a, "A", Encoding.UTF8, int.MaxValue));
            Assert.Throws<EncoderFallbackException>(() => ByteBufUtil.EncodeString(a, "\ud800", new UTF8Encoding(false, true)));
            Assert.Equal(0, a.Calls); Assert.Null(a.Last);
            a.FailAllocation = true;
            Assert.Throws<InvalidOperationException>(() => ByteBufUtil.ReadBytes(a, source, 1));
            Assert.Throws<InvalidOperationException>(() => ByteBufUtil.WriteUtf8(a, raw));
            Assert.Throws<InvalidOperationException>(() => ByteBufUtil.EncodeString(a, "A", Encoding.UTF8));
            Assert.Null(a.Last); Assert.Equal(0, source.ReaderIndex); Assert.Equal(1, source.ReferenceCount);
        }
        finally { source.Release(); }
    }

    private sealed class FailingEncoding : UTF8Encoding
    {
        public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
        { bytes[0] = 99; throw new InvalidOperationException("Deliberate encoding failure after a partial write."); }
    }

    private sealed class ProbeAllocator : AbstractByteBufAllocator
    {
        private readonly int _kind;
        public NativeMemoryAllocator Native { get; }
        public ByteBuf Last { get; private set; }
        public int RequestedCapacity { get; private set; }
        public List<int> RequestedCapacities { get; } = new();
        public int Calls { get; private set; }
        public int ExtraCapacity { get; set; }
        public bool ReadOnly { get; set; }
        public bool FailAllocation { get; set; }
        public ProbeAllocator(int kind, long maximumBytes = long.MaxValue) : base(kind == 1)
        { _kind = kind; Native = new NativeMemoryAllocator(maximumBytes); }
        public override bool IsDirectBufferPooled => false;
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity) => Allocate(initialCapacity, maxCapacity, false);
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity) => Allocate(initialCapacity, maxCapacity, true);
        private ByteBuf Allocate(int initial, int max, bool direct)
        {
            Calls++; RequestedCapacity = initial; RequestedCapacities.Add(initial);
            if (FailAllocation) throw new InvalidOperationException("Deliberate allocation failure.");
            int capacity = checked(initial + ExtraCapacity);
            ByteBuf b;
            if (_kind == 2)
            {
                var composite = new CompositeByteBuf(this, false);
                if (capacity > 0) composite.AddComponent(Unpooled.WrappedBuffer(new byte[capacity / 2]), true)
                    .AddComponent(Unpooled.WrappedBuffer(new byte[capacity - capacity / 2]), true);
                b = composite.SetIndex(0, 0);
            }
            else b = direct ? new UnpooledDirectByteBuf(this, capacity, max, Native) : new UnpooledHeapByteBuf(this, capacity, max);
            Last = ReadOnly ? b.AsReadOnly() : b;
            return Last;
        }
    }
}
